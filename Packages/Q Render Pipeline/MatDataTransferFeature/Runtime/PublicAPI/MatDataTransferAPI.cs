using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public static class MatDataTransferAPI
    {
        public const string GenericProviderName = "GenericMaterialParameter";

        private static readonly ParamWriteLayer[] s_LayerOptions =
        {
            ParamWriteLayer.MaterialDefault,
            ParamWriteLayer.Gameplay,
            ParamWriteLayer.VFX
        };

        public static bool RuntimeFileLoggingEnabled => MatDataTransferLogger.RuntimeFileOutputEnabled;
        public static IReadOnlyList<ParamWriteLayer> LayerOptions => s_LayerOptions;

        public static void SetRuntimeFileLoggingEnabled(bool enabled)
        {
            MatDataTransferLogger.SetRuntimeFileOutputEnabled(enabled);
        }

        public static MaterialParameterSubmitResult Submit(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer = ParamWriteLayer.Gameplay,
            int priority = 0)
        {
            MaterialParameterSubmitPayload payload = new MaterialParameterSubmitPayload(
                target,
                semanticKey,
                value,
                source,
                binding,
                layer,
                priority);

            return SubmitPayload(ref payload);
        }

        public static MaterialParameterSubmitResult ForMaterial(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            Renderer targetRenderer,
            int materialSlot,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer = ParamWriteLayer.Gameplay,
            int priority = 0)
        {
            RendererMaterialBinding binding = ResolveTargetBinding(target, targetRenderer, materialSlot);
            return Submit(target, semanticKey, value, binding, source, layer, priority);
        }

        private static MaterialParameterSubmitResult SubmitPayload(ref MaterialParameterSubmitPayload payload)
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            payload.RefreshRouting();

            if (!TryValidate(ref payload, feature))
            {
                string path = payload.Target != null
                    ? MatDataTransferFeature.GetTransformPath(payload.Target.transform)
                    : string.Empty;
                feature?.RecordSubmitResult(payload, path);
                return payload.Result;
            }

            Trace(
                ref payload,
                feature,
                "Submit.Validated",
                ParamWriteResultType.Accepted,
                ParamWriteResultCode.None,
                "Submit payload validated.");

            if (!GenericMaterialParameterProvider.TryQueue(payload))
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Queue",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.ProviderUnavailable,
                    "Generic material parameter provider rejected the request.");
                string path = payload.Target != null
                    ? MatDataTransferFeature.GetTransformPath(payload.Target.transform)
                    : string.Empty;
                feature?.RecordSubmitResult(payload, path);
                return payload.Result;
            }

            Trace(
                ref payload,
                feature,
                "Submit.Queue",
                ParamWriteResultType.Accepted,
                ParamWriteResultCode.None,
                "Submit accepted.");
            MatDataTransferRuntime.RequestEditorUpdate();
            return payload.Result;
        }

        private static bool TryValidate(
            ref MaterialParameterSubmitPayload payload,
            MatDataTransferFeature feature)
        {
            if (payload.Target == null)
            {
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, ParamWriteResultCode.InstanceMissing, "Target instance is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.SemanticKey))
            {
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, ParamWriteResultCode.SemanticKeyMissing, "Semantic key is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload.Source.Id))
            {
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, ParamWriteResultCode.SourceIdMissing, "Submit source id is empty.");
                return false;
            }

            if (!payload.Target.IsReady)
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Validate",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.InstanceMissing,
                    "Target instance is not registered by MatDataTransferFeature.");
                return false;
            }

            RendererMaterialBinding binding = payload.Binding;
            if (binding == null)
            {
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, ParamWriteResultCode.RendererOrMaterialSlotMissing, "Material target binding is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(binding.RendererPathId) || binding.MaterialSlot < 0)
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Validate",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.RendererOrMaterialSlotMissing,
                    "Material target binding needs renderer path id and material slot.");
                return false;
            }

            if (feature == null)
            {
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, ParamWriteResultCode.FeatureMissing, "No active MatDataTransferFeature.");
                return false;
            }

            if (feature.GetRequestProvider(GenericProviderName) == null)
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Validate",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.ProviderUnavailable,
                    "Generic material parameter provider is unavailable.");
                return false;
            }

            List<RendererMaterialBinding> bindings = payload.Target.QueryBindingsByTrace(
                payload.Binding.RendererPathId,
                payload.Binding.MaterialSlot);
            if (bindings.Count == 0)
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Validate",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.BindingMissing,
                    "No renderer/material binding matches the submit target.");
                return false;
            }

            if (!TryFindCatalogProperty(bindings, payload.SemanticKey, feature, out CatalogProperty property, out bool hasCatalog))
            {
                ParamWriteResultCode code = hasCatalog
                    ? ParamWriteResultCode.PropertyMissing
                    : ParamWriteResultCode.BindingMissing;
                string message = hasCatalog
                    ? $"No catalog property found for semantic key: {payload.SemanticKey}."
                    : "No shader catalog matches the submit target.";
                Trace(ref payload, feature, "Submit.Validate", ParamWriteResultType.Rejected, code, message);
                return false;
            }

            ParamValueType expectedType = property.PropertyInfo.ValueType;
            if (expectedType != payload.Value.Type)
            {
                Trace(
                    ref payload,
                    feature,
                    "Submit.Validate",
                    ParamWriteResultType.Rejected,
                    ParamWriteResultCode.TypeMismatch,
                    $"Type mismatch: {payload.SemanticKey} expects {expectedType}, got {payload.Value.Type}.");
                return false;
            }

            return true;
        }

        private static void Trace(
            ref MaterialParameterSubmitPayload payload,
            MatDataTransferFeature feature,
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            if (feature != null)
            {
                feature.TraceSubmit(ref payload, stage, type, code, message);
                return;
            }

            payload.Result.AddTrace(stage, type, code, message);
        }

        private static bool TryFindCatalogProperty(
            List<RendererMaterialBinding> bindings,
            string semanticKey,
            MatDataTransferFeature feature,
            out CatalogProperty property,
            out bool hasCatalog)
        {
            property = null;
            hasCatalog = false;

            for (int i = 0; i < bindings.Count; i++)
            {
                RendererMaterialBinding binding = bindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.ShaderName))
                    continue;

                if (feature.TryGetCatalogForShader(binding.ShaderName, out _))
                    hasCatalog = true;

                if (feature.TryGetProperty(binding.ShaderName, semanticKey, out _, out property))
                    return property?.PropertyInfo != null;
            }

            return false;
        }

        private static RendererMaterialBinding ResolveTargetBinding(
            MatDataTransferInstance target,
            Renderer targetRenderer,
            int materialSlot)
        {
            if (target == null || targetRenderer == null || materialSlot < 0)
                return null;

            List<RendererMaterialBinding> bindings = target.QueryBindings(targetRenderer.GetInstanceID(), materialSlot);
            return bindings != null && bindings.Count > 0 ? bindings[0] : null;
        }
    }
}
