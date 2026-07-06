using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public struct ParamRequestIdentity
    {
        [NonSerialized] public MatDataTransferInstance Target;
        public MatDataTransferSubmitSource Source;
        public string SemanticKey;
        public ParamValue Value;
        public ParamRendererBinding Binding;

        public string SourceId => Source.Id;

        public ParamRequestIdentity(
            MatDataTransferInstance target,
            MatDataTransferSubmitSource source,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding)
        {
            Target = target;
            Source = source;
            SemanticKey = semanticKey;
            Value = value;
            Binding = new ParamRendererBinding(binding);
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
        public readonly ParamTransferPayload Payload;
        public readonly CatalogProperty Property;
        public readonly ResolvedMaterialBinding BindingResolution;
        public readonly Renderer Renderer;
        public readonly string GameObjectPath;

        public MaterialWriteCommand(
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
