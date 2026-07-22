using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MaterialParameterWriter
    {
#if UNITY_INCLUDE_TESTS
        private static int s_TestMaterialArrayReadCount;
#endif

        private readonly Dictionary<long, PropertyBlockEntry> m_PropertyBlockEntries =
            new Dictionary<long, PropertyBlockEntry>();
        private readonly Dictionary<long, FrameWriteTarget> m_FrameWriteTargets =
            new Dictionary<long, FrameWriteTarget>();
        private readonly List<PropertyBlockEntry> m_PendingPropertyBlocks = new List<PropertyBlockEntry>();
        private ParamWriteMethod m_WriteMode = ParamWriteMethod.MaterialInstance;
        private WriterStats m_LastStats;

        internal WriterStats LastStats => m_LastStats;

        internal void ResetStats()
        {
            m_LastStats.Reset();
        }

        internal void SetWriteMode(ParamWriteMethod writeMode)
        {
            if (m_WriteMode == writeMode)
                return;

            if (m_WriteMode == ParamWriteMethod.MaterialPropertyBlock)
                ClearWrittenState();

            m_WriteMode = writeMode;
        }

#if UNITY_INCLUDE_TESTS
        internal static void ResetMaterialArrayReadCountForTests()
        {
            s_TestMaterialArrayReadCount = 0;
        }

        internal static int GetMaterialArrayReadCountForTests()
        {
            return s_TestMaterialArrayReadCount;
        }
#endif

        internal void Apply(
            IReadOnlyList<ParamWriteCommand> commands,
            IReadOnlyList<RequestDiagnosticContext> diagnostics)
        {
            using (MatDataTransferProfiling.ModuleWriter.Auto())
                ApplyProfiled(commands, diagnostics);
        }

        private void ApplyProfiled(
            IReadOnlyList<ParamWriteCommand> commands,
            IReadOnlyList<RequestDiagnosticContext> diagnostics)
        {
            ResetStats();
            if (commands == null)
                return;

            BeginFrame();
            for (int i = 0; i < commands.Count; i++)
            {
                ParamWriteCommand command = commands[i];
                ParamWriteMethod writeMethod = Apply(command, out string failureReason);
                m_LastStats.Add(writeMethod);
                MatDataTransferLogging.RecordWrite(
                    command.Trace,
                    command.DiagnosticIndex,
                    diagnostics,
                    command.Target.Renderer,
                    writeMethod,
                    failureReason);
            }

            FlushPropertyBlocks();
        }

        internal void ClearWrittenState()
        {
            foreach (PropertyBlockEntry entry in m_PropertyBlockEntries.Values)
                entry.Clear();

            m_PropertyBlockEntries.Clear();
            m_PendingPropertyBlocks.Clear();
            m_FrameWriteTargets.Clear();
        }

        private void BeginFrame()
        {
            m_PendingPropertyBlocks.Clear();
            m_FrameWriteTargets.Clear();
        }

        private ParamWriteMethod Apply(ParamWriteCommand command, out string failureReason)
        {
            if (!TryCreateWriteTarget(command, out ParamWriteTarget target, out failureReason))
                return ParamWriteMethod.None;

            if (m_WriteMode == ParamWriteMethod.MaterialPropertyBlock)
            {
                if (TryApplyPropertyBlock(command, target, out failureReason))
                {
                    failureReason = string.Empty;
                    return ParamWriteMethod.MaterialPropertyBlock;
                }

                return ParamWriteMethod.None;
            }

            bool shared = m_WriteMode == ParamWriteMethod.SharedMaterial;
            if (m_WriteMode != ParamWriteMethod.MaterialInstance && !shared)
            {
                failureReason = $"Unsupported material write mode: {m_WriteMode}.";
                return ParamWriteMethod.None;
            }

            if (TryApplyMaterial(command, target, shared, out failureReason))
            {
                failureReason = string.Empty;
                return shared
                    ? ParamWriteMethod.SharedMaterial
                    : ParamWriteMethod.MaterialInstance;
            }

            if (string.IsNullOrEmpty(failureReason))
                failureReason = "Write target resource became unavailable.";
            return ParamWriteMethod.None;
        }

        private bool TryApplyPropertyBlock(
            ParamWriteCommand command,
            ParamWriteTarget target,
            out string failureReason)
        {
            if (!TryGetFrameMaterial(target, true, out Material material, out failureReason)
                || !TryValidateMaterialProperty(target, material, out failureReason))
                return false;

            if (!TryGetPropertyBlock(target, out MaterialPropertyBlock block, out failureReason))
                return false;

            using (MatDataTransferProfiling.PipelineWriteSetValue.Auto())
                SetValue(block, target.PropertyId, command.Value);
            return true;
        }

        private bool TryApplyMaterial(
            ParamWriteCommand command,
            ParamWriteTarget target,
            bool shared,
            out string failureReason)
        {
            if (!TryGetFrameMaterial(target, shared, out Material material, out failureReason)
                || !TryValidateMaterialProperty(target, material, out failureReason))
                return false;

            using (MatDataTransferProfiling.PipelineWriteSetValue.Auto())
                SetValue(material, target.PropertyId, command.Value);
            return true;
        }

        private static bool TryCreateWriteTarget(
            ParamWriteCommand command,
            out ParamWriteTarget target,
            out string failureReason)
        {
            target = default;
            failureReason = string.Empty;

            if (command.Target.Renderer == null)
            {
                failureReason = "Write target renderer is missing.";
                return false;
            }

            if (command.Target.PropertyId == 0)
            {
                failureReason = "Write target shader property is empty.";
                return false;
            }

            target = command.Target;
            return true;
        }

        private bool TryGetPropertyBlock(
            ParamWriteTarget target,
            out MaterialPropertyBlock block,
            out string failureReason)
        {
            block = null;
            failureReason = string.Empty;

            int rendererId = target.Renderer.GetInstanceID();
            long key = PropertyBlockEntry.GetLookupKey(rendererId, target.MaterialSlot);
            if (!m_PropertyBlockEntries.TryGetValue(key, out PropertyBlockEntry entry))
            {
                entry = new PropertyBlockEntry(rendererId, target.Renderer, target.MaterialSlot);
                m_PropertyBlockEntries.Add(key, entry);
            }

            entry.UpdateRenderer(target.Renderer);
            if (!entry.IsQueued)
            {
                entry.ReadCurrentBlock();
                entry.IsQueued = true;
                m_PendingPropertyBlocks.Add(entry);
            }

            block = entry.Block;
            return true;
        }

        private bool TryGetFrameMaterial(
            ParamWriteTarget target,
            bool shared,
            out Material material,
            out string failureReason)
        {
            long key = FrameWriteTarget.GetLookupKey(
                target.Renderer.GetInstanceID(),
                target.MaterialSlot);
            if (m_FrameWriteTargets.TryGetValue(key, out FrameWriteTarget cached))
            {
                material = cached.Material;
                failureReason = cached.FailureReason;
                return cached.IsValid;
            }

            material = ResolveMaterial(target.Renderer, target.MaterialSlot, shared);
            if (material == null)
            {
                failureReason = $"Write target material is missing at slot {target.MaterialSlot}.";
                m_FrameWriteTargets.Add(key, FrameWriteTarget.Failed(failureReason));
                return false;
            }

            if (material.shader == null)
            {
                failureReason = $"Write target material '{material.name}' has no shader.";
                m_FrameWriteTargets.Add(key, FrameWriteTarget.Failed(failureReason));
                return false;
            }

            failureReason = string.Empty;
            m_FrameWriteTargets.Add(key, FrameWriteTarget.Valid(material));
            return true;
        }

        private static bool TryValidateMaterialProperty(
            ParamWriteTarget target,
            Material material,
            out string failureReason)
        {
            failureReason = string.Empty;

            if (material.shader == null)
            {
                failureReason = $"Write target material '{material.name}' has no shader.";
                return false;
            }

            if (!material.HasProperty(target.PropertyId))
            {
                failureReason = $"Shader '{material.shader.name}' does not contain property id {target.PropertyId}.";
                return false;
            }

            return true;
        }

        private static Material ResolveMaterial(Renderer renderer, int materialSlot, bool shared)
        {
            using (MatDataTransferProfiling.PipelineWriteResolveMaterial.Auto())
            {
                MatDataTransferProfiling.AddMaterialArrayRead();
#if UNITY_INCLUDE_TESTS
                s_TestMaterialArrayReadCount++;
#endif
                Material[] materials = shared ? renderer.sharedMaterials : renderer.materials;
                if (materials == null || materialSlot < 0 || materialSlot >= materials.Length)
                    return null;

                return materials[materialSlot];
            }
        }

        private void FlushPropertyBlocks()
        {
            for (int i = 0; i < m_PendingPropertyBlocks.Count; i++)
            {
                PropertyBlockEntry entry = m_PendingPropertyBlocks[i];
                entry.Flush();
                entry.IsQueued = false;
            }

            m_PendingPropertyBlocks.Clear();
        }

        private static void SetValue(MaterialPropertyBlock block, int propertyId, ParamValue value)
        {
            switch (value.Type)
            {
                case ParamValueType.Float:
                    block.SetFloat(propertyId, value.FloatValue);
                    break;
                case ParamValueType.Color:
                    block.SetColor(propertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    block.SetVector(propertyId, value.VectorValue);
                    break;
                case ParamValueType.Texture:
                    block.SetTexture(propertyId, value.TextureValue);
                    break;
            }
        }

        private static void SetValue(Material material, int propertyId, ParamValue value)
        {
            switch (value.Type)
            {
                case ParamValueType.Float:
                    material.SetFloat(propertyId, value.FloatValue);
                    break;
                case ParamValueType.Color:
                    material.SetColor(propertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    material.SetVector(propertyId, value.VectorValue);
                    break;
                case ParamValueType.Texture:
                    material.SetTexture(propertyId, value.TextureValue);
                    break;
            }
        }

        private sealed class PropertyBlockEntry
        {
            private readonly int m_RendererId;
            private Renderer m_Renderer;

            public readonly int MaterialSlot;
            public readonly MaterialPropertyBlock Block;
            public bool IsQueued;

            public PropertyBlockEntry(int rendererId, Renderer renderer, int materialSlot)
            {
                m_RendererId = rendererId;
                m_Renderer = renderer;
                MaterialSlot = materialSlot;
                Block = new MaterialPropertyBlock();
            }

            public static long GetLookupKey(int rendererId, int materialSlot)
            {
                return FrameWriteTarget.GetLookupKey(rendererId, materialSlot);
            }

            public void UpdateRenderer(Renderer renderer)
            {
                if (renderer != null && renderer.GetInstanceID() == m_RendererId)
                    m_Renderer = renderer;
            }

            public void ReadCurrentBlock()
            {
                if (m_Renderer != null)
                    m_Renderer.GetPropertyBlock(Block, MaterialSlot);
            }

            public void Flush()
            {
                if (m_Renderer != null)
                    m_Renderer.SetPropertyBlock(Block, MaterialSlot);
            }

            public void Clear()
            {
                if (m_Renderer != null)
                    m_Renderer.SetPropertyBlock(null, MaterialSlot);
            }
        }

        private readonly struct FrameWriteTarget
        {
            public readonly Material Material;
            public readonly string FailureReason;
            public readonly bool IsValid;

            private FrameWriteTarget(Material material, string failureReason, bool isValid)
            {
                Material = material;
                FailureReason = failureReason;
                IsValid = isValid;
            }

            public static FrameWriteTarget Valid(Material material)
            {
                return new FrameWriteTarget(material, string.Empty, true);
            }

            public static FrameWriteTarget Failed(string failureReason)
            {
                return new FrameWriteTarget(null, failureReason ?? string.Empty, false);
            }

            public static long GetLookupKey(int rendererId, int materialSlot)
            {
                return ((long)rendererId << 32) ^ (uint)materialSlot;
            }
        }
    }
}
