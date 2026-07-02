using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public struct ParamRequestIdentity
    {
        public string SourceId;
        public string ProviderName;
        public string SemanticKey;

        public ParamRequestIdentity(string sourceId, string providerName, string semanticKey)
        {
            SourceId = sourceId;
            ProviderName = providerName;
            SemanticKey = semanticKey;
        }
    }

    [Serializable]
    public struct ParamWriteConfig
    {
        public ParamWriteLayer Layer;
        public int Priority;

        public ParamWriteConfig(ParamWriteLayer layer, int priority = 0)
        {
            Layer = layer;
            Priority = priority;
        }
    }

    [Serializable]
    public struct ParamRendererBinding
    {
        public int RendererId;
        public int MaterialSlot;
        public string RendererPathId;
        public string MaterialTraceId;
        [NonSerialized] public Renderer Renderer;

        public ParamRendererBinding(int rendererId, int materialSlot)
        {
            RendererId = rendererId;
            MaterialSlot = materialSlot;
            RendererPathId = string.Empty;
            MaterialTraceId = string.Empty;
            Renderer = null;
        }

        public ParamRendererBinding(Renderer renderer, int materialSlot)
        {
            Renderer = renderer;
            RendererId = renderer != null ? renderer.GetInstanceID() : 0;
            MaterialSlot = materialSlot;
            RendererPathId = string.Empty;
            MaterialTraceId = string.Empty;
        }

        public ParamRendererBinding(string rendererPathId, string materialTraceId, int materialSlot)
        {
            Renderer = null;
            RendererId = 0;
            MaterialSlot = materialSlot;
            RendererPathId = rendererPathId ?? string.Empty;
            MaterialTraceId = materialTraceId ?? string.Empty;
        }

        public ParamRendererBinding(RendererMaterialBinding binding)
        {
            Renderer = binding?.Renderer;
            RendererId = binding != null ? binding.RendererId : 0;
            MaterialSlot = binding != null ? binding.MaterialSlot : -1;
            RendererPathId = binding != null ? binding.RendererPathId : string.Empty;
            MaterialTraceId = binding != null ? binding.MaterialTraceId : string.Empty;
        }
    }

    internal readonly struct MaterialWriteCommand
    {
        public readonly MaterialParameterSubmitPayload Payload;
        public readonly CatalogProperty Property;
        public readonly ResolvedMaterialBinding BindingResolution;
        public readonly Renderer Renderer;
        public readonly string GameObjectPath;

        public MaterialWriteCommand(
            MaterialParameterSubmitPayload payload,
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
