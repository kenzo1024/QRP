using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly Dictionary<long, PropertyBlockEntry> m_PropertyBlockEntries =
            new Dictionary<long, PropertyBlockEntry>();
        private readonly List<PropertyBlockEntry> m_PendingPropertyBlocks = new List<PropertyBlockEntry>();

        private void BeginWriteTargetFrame()
        {
            m_PendingPropertyBlocks.Clear();
        }

        private static bool TryCreateWriteTarget(
            MaterialWriteCommand command,
            out MaterialWriteTarget target,
            out string failureReason)
        {
            target = default;
            failureReason = string.Empty;

            if (command.Renderer == null)
            {
                failureReason = "Write target renderer is missing.";
                return false;
            }

            if (command.Property?.PropertyInfo == null)
            {
                failureReason = "Write target catalog property is missing.";
                return false;
            }

            string propertyName = command.BindingResolution.PropertyName;
            int propertyId = command.BindingResolution.PropertyId;
            if (string.IsNullOrEmpty(propertyName) || propertyId == 0)
            {
                failureReason = "Write target shader property is empty.";
                return false;
            }

            int materialSlot = command.Payload.Identity.Binding.MaterialSlot;
            target = new MaterialWriteTarget(
                command.Renderer,
                materialSlot,
                propertyName,
                propertyId);

            return TryGetMaterial(target, true, out _, out failureReason);
        }

        private bool TryGetPropertyBlock(
            MaterialWriteTarget target,
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

        private static bool TryGetMaterial(
            MaterialWriteTarget target,
            bool shared,
            out Material material,
            out string failureReason)
        {
            material = ResolveMaterial(target.Renderer, target.MaterialSlot, shared);
            if (material == null)
            {
                failureReason = $"Write target material is missing at slot {target.MaterialSlot}.";
                return false;
            }

            return TryValidateMaterialProperty(target, material, out failureReason);
        }

        private static bool TryValidateMaterialProperty(
            MaterialWriteTarget target,
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
                failureReason = $"Shader '{material.shader.name}' does not contain property '{target.PropertyName}'.";
                return false;
            }

            return true;
        }

        private static Material ResolveMaterial(Renderer renderer, int materialSlot, bool shared)
        {
            Material[] materials = shared ? renderer.sharedMaterials : renderer.materials;
            if (materials == null || materialSlot < 0 || materialSlot >= materials.Length)
                return null;

            return materials[materialSlot];
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

        private readonly struct MaterialWriteTarget
        {
            public readonly Renderer Renderer;
            public readonly int MaterialSlot;
            public readonly string PropertyName;
            public readonly int PropertyId;

            public MaterialWriteTarget(
                Renderer renderer,
                int materialSlot,
                string propertyName,
                int propertyId)
            {
                Renderer = renderer;
                MaterialSlot = materialSlot;
                PropertyName = propertyName;
                PropertyId = propertyId;
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
