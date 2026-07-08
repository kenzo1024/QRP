using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MatDataTransferResolver
    {
        private readonly Dictionary<RequestGroupKey, List<ResolvedMaterialRequest>> m_Groups =
            new Dictionary<RequestGroupKey, List<ResolvedMaterialRequest>>();
        private readonly List<MaterialWriteCommand> m_Commands = new List<MaterialWriteCommand>();

        internal IReadOnlyList<MaterialWriteCommand> Resolve(
            IReadOnlyList<ShaderPropertyCatalog> catalogs,
            InstanceRegister registry,
            List<ParamTransferPayload> payloads)
        {
            m_Groups.Clear();
            m_Commands.Clear();

            if (payloads == null || payloads.Count == 0)
                return m_Commands;

            for (int i = 0; i < payloads.Count; i++)
                AddPayload(catalogs, registry, payloads[i]);

            foreach (KeyValuePair<RequestGroupKey, List<ResolvedMaterialRequest>> pair in m_Groups)
                ResolveGroup(pair.Value);

            return m_Commands;
        }

        private void AddPayload(
            IReadOnlyList<ShaderPropertyCatalog> catalogs,
            InstanceRegister registry,
            ParamTransferPayload payload)
        {
            if (!TryResolveTarget(registry, payload, out RendererMaterialBinding rendererBinding, out string gameObjectPath))
                return;

            ParamTransferPayload resolvedPayload = payload;
            resolvedPayload.Identity.Binding = new ParamRendererBinding(rendererBinding);

            string shaderName = rendererBinding.ShaderName;
            if (string.IsNullOrEmpty(shaderName))
                return;

            if (!MaterialBindingResolver.TryGetProperty(
                catalogs,
                shaderName,
                resolvedPayload.Identity.SemanticKey,
                out ShaderPropertyCatalog matchedCatalog,
                out CatalogProperty matchedProperty))
            {
                return;
            }

            if (!IsTypeCompatible(matchedProperty.PropertyInfo.ValueType, resolvedPayload.Identity.Value.Type))
                return;

            string catalogName = matchedCatalog != null ? matchedCatalog.name : string.Empty;
            ResolvedMaterialBinding bindingResolution = ResolvedMaterialBinding.FromCatalog(
                matchedProperty,
                matchedProperty.SuggestedSemanticKey,
                shaderName,
                catalogName);

            resolvedPayload.Identity.SemanticKey = matchedProperty.SuggestedSemanticKey;

            RequestGroupKey key = new RequestGroupKey(
                GetInstanceId(resolvedPayload),
                resolvedPayload.Identity.Binding.RendererId,
                resolvedPayload.Identity.Binding.MaterialSlot,
                matchedProperty.SuggestedSemanticKey);

            if (!m_Groups.TryGetValue(key, out List<ResolvedMaterialRequest> group))
            {
                group = new List<ResolvedMaterialRequest>();
                m_Groups.Add(key, group);
            }

            group.Add(new ResolvedMaterialRequest(
                resolvedPayload,
                matchedProperty,
                bindingResolution,
                rendererBinding.Renderer,
                gameObjectPath));
        }

        private void ResolveGroup(List<ResolvedMaterialRequest> group)
        {
            if (group == null || group.Count == 0)
                return;

            group.Sort(CompareRequestStrength);
            ResolvedMaterialRequest strongest = group[group.Count - 1];
            RecordOverriddenRequests(group, strongest);
            m_Commands.Add(new MaterialWriteCommand(
                strongest.Payload,
                strongest.Property,
                strongest.BindingResolution,
                strongest.Renderer,
                strongest.GameObjectPath));
        }

        private static void RecordOverriddenRequests(
            List<ResolvedMaterialRequest> group,
            ResolvedMaterialRequest strongest)
        {
            for (int i = 0; i < group.Count - 1; i++)
            {
                ResolvedMaterialRequest request = group[i];
                ParamTransferPayload payload = request.Payload;
                MatDataTransferLogging.CaptureResolvedSnapshot(
                    ref payload,
                    request.BindingResolution,
                    ParamSubmitStep.Overridden("Resolve.Conflict", strongest.Payload.Identity.SourceId),
                    request.GameObjectPath,
                    request.Renderer);
            }
        }

        private static int CompareRequestStrength(ResolvedMaterialRequest left, ResolvedMaterialRequest right)
        {
            int layer = ParamWriteLayers.GetStrength(left.Payload.WriteConfig.Layer)
                .CompareTo(ParamWriteLayers.GetStrength(right.Payload.WriteConfig.Layer));
            if (layer != 0)
                return layer;

            int priority = left.Payload.WriteConfig.Priority.CompareTo(right.Payload.WriteConfig.Priority);
            if (priority != 0)
                return priority;

            int submitFrame = left.Payload.SubmitFrameIndex.CompareTo(right.Payload.SubmitFrameIndex);
            if (submitFrame != 0)
                return submitFrame;

            return left.Payload.Sequence.CompareTo(right.Payload.Sequence);
        }

        private static bool IsTypeCompatible(ParamValueType bindingType, ParamValueType requestType)
        {
            return bindingType == requestType;
        }

        private static bool TryResolveTarget(
            InstanceRegister registry,
            ParamTransferPayload payload,
            out RendererMaterialBinding binding,
            out string gameObjectPath)
        {
            binding = null;
            gameObjectPath = string.Empty;

            if (registry == null || !registry.TryGet(GetInstanceId(payload), out MatDataTransferInstance instance))
                return false;

            gameObjectPath = MatDataTransferFeature.GetTransformPath(instance.transform);
            binding = instance.QueryBinding(payload.Identity.Binding);

            return binding != null;
        }

        private static int GetInstanceId(ParamTransferPayload payload)
        {
            return payload.Identity.Target != null
                ? payload.Identity.Target.InstanceId
                : -1;
        }

        private readonly struct RequestGroupKey : IEquatable<RequestGroupKey>
        {
            private readonly int m_InstanceId;
            private readonly int m_RendererId;
            private readonly int m_MaterialSlot;
            private readonly string m_SemanticKey;

            public RequestGroupKey(int instanceId, int rendererId, int materialSlot, string semanticKey)
            {
                m_InstanceId = instanceId;
                m_RendererId = rendererId;
                m_MaterialSlot = materialSlot;
                m_SemanticKey = semanticKey ?? string.Empty;
            }

            public bool Equals(RequestGroupKey other)
            {
                return m_InstanceId == other.m_InstanceId
                    && m_RendererId == other.m_RendererId
                    && m_MaterialSlot == other.m_MaterialSlot
                    && string.Equals(m_SemanticKey, other.m_SemanticKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RequestGroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = m_InstanceId;
                    hash = (hash * 397) ^ m_RendererId;
                    hash = (hash * 397) ^ m_MaterialSlot;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(m_SemanticKey);
                    return hash;
                }
            }
        }

        private readonly struct ResolvedMaterialRequest
        {
            public readonly ParamTransferPayload Payload;
            public readonly CatalogProperty Property;
            public readonly ResolvedMaterialBinding BindingResolution;
            public readonly Renderer Renderer;
            public readonly string GameObjectPath;

            public ResolvedMaterialRequest(
                ParamTransferPayload payload,
                CatalogProperty property,
                ResolvedMaterialBinding bindingResolution,
                Renderer renderer,
                string gameObjectPath)
            {
                Payload = payload;
                Property = property;
                BindingResolution = bindingResolution;
                Renderer = renderer;
                GameObjectPath = gameObjectPath;
            }
        }
    }
}
