using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly Dictionary<long, PropertyBlockEntry> m_PropertyBlockEntries =
            new Dictionary<long, PropertyBlockEntry>();
        private readonly List<PropertyBlockEntry> m_PendingPropertyBlocks = new List<PropertyBlockEntry>();

        private void ApplyWriteCommands(IReadOnlyList<MaterialWriteCommand> commands)
        {
            if (commands == null)
                return;

            m_PendingPropertyBlocks.Clear();
            for (int i = 0; i < commands.Count; i++)
            {
                MaterialWriteCommand command = commands[i];
                ParamWriteMethod writeMethod = ApplyWriteCommand(command);
                ParamWriteResultInfo resultInfo = writeMethod == ParamWriteMethod.None
                    ? ParamWriteResultInfo.WriterFailed()
                    : ParamWriteResultInfo.CreateApplied();
                MaterialParameterSubmitPayload payload = command.Payload;
                TraceSubmit(
                    ref payload,
                    "Write.Apply",
                    resultInfo.Type,
                    resultInfo.Code,
                    string.IsNullOrEmpty(resultInfo.Message) ? "Write applied." : resultInfo.Message);
                m_Logging.RecordWriteResult(command, writeMethod, payload);
            }

            FlushPropertyBlocks();
        }

        private ParamWriteMethod ApplyWriteCommand(MaterialWriteCommand command)
        {
            if (command.Renderer == null || command.Property?.PropertyInfo == null)
                return ParamWriteMethod.None;

#if UNITY_EDITOR
            if (TryApplyPropertyBlock(command))
                return ParamWriteMethod.MaterialPropertyBlock;
            if (ApplyMaterial(command, false))
                return ParamWriteMethod.MaterialInstance;
#else
            if (ApplyMaterial(command, false))
                return ParamWriteMethod.MaterialInstance;
            if (TryApplyPropertyBlock(command))
                return ParamWriteMethod.MaterialPropertyBlock;
#endif
            if (ApplyMaterial(command, true))
                return ParamWriteMethod.SharedMaterial;

            return ParamWriteMethod.None;
        }

        private bool TryApplyPropertyBlock(MaterialWriteCommand command)
        {
            QueuePropertyBlock(command);
            return true;
        }

        private void QueuePropertyBlock(MaterialWriteCommand command)
        {
            int rendererId = command.Renderer.GetInstanceID();
            int materialSlot = command.Payload.RendererBinding.MaterialSlot;
            long key = PropertyBlockEntry.GetLookupKey(rendererId, materialSlot);
            if (!m_PropertyBlockEntries.TryGetValue(key, out PropertyBlockEntry entry))
            {
                entry = new PropertyBlockEntry(rendererId, command.Renderer, materialSlot);
                m_PropertyBlockEntries.Add(key, entry);
            }

            entry.UpdateRenderer(command.Renderer);
            if (!entry.IsQueued)
            {
                entry.ReadCurrentBlock();
                entry.IsQueued = true;
                m_PendingPropertyBlocks.Add(entry);
            }

            SetValue(entry.Block, command.BindingResolution.PropertyId, command.Payload.Value);
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

        private void ClearWrittenState()
        {
            foreach (PropertyBlockEntry entry in m_PropertyBlockEntries.Values)
                entry.Clear();

            m_PropertyBlockEntries.Clear();
            m_PendingPropertyBlocks.Clear();
        }

        private static bool ApplyMaterial(MaterialWriteCommand command, bool shared)
        {
            Material material = GetMaterial(command.Renderer, command.Payload.RendererBinding.MaterialSlot, shared);
            if (material == null)
                return false;

            SetValue(material, command.BindingResolution.PropertyId, command.Payload.Value);
            return true;
        }

        private static Material GetMaterial(Renderer renderer, int materialSlot, bool shared)
        {
            Material[] materials = shared ? renderer.sharedMaterials : renderer.materials;
            if (materials == null || materialSlot < 0 || materialSlot >= materials.Length)
                return null;

            return materials[materialSlot];
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
                return ((long)rendererId << 32) ^ (uint)materialSlot;
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
    }
}
