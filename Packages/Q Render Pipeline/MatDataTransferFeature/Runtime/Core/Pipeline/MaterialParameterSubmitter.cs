using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    internal static class MaterialParameterSubmitter
    {
        internal static ParamSubmitTrace Submit(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer = ParamWriteLayer.Gameplay,
            int priority = 0)
        {
            ParamRequestIdentity identity = new ParamRequestIdentity(
                target,
                source,
                semanticKey,
                value,
                binding);
            ParamWriteConfig writeConfig = new ParamWriteConfig(layer, priority);
            ParamTransferPayload payload = new ParamTransferPayload(
                identity,
                writeConfig);

            return SubmitPayload(ref payload);
        }

        internal static ParamSubmitTrace ForMaterial(
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

        private static ParamSubmitTrace SubmitPayload(ref ParamTransferPayload payload)
        {
            payload.Sequence = MatDataTransferSubmitSequence.Next(out int submitFrameIndex);
            payload.SubmitFrameIndex = submitFrameIndex;
            MatDataTransferLogging.AppendSubmitStep(
                ref payload,
                ParamSubmitStep.Submitted(
                    "Submit.Begin",
                    "Submit payload created."));

            MatDataTransferFeature feature = MatDataTransferFeature.Instance;

            if (!TryValidate(ref payload, feature))
            {
                MatDataTransferLogging.CaptureSubmitSnapshot(ref payload);
                return payload.Trace;
            }

            if (!GenericMaterialParameterProvider.TryQueue(ref payload))
            {
                MatDataTransferLogging.CaptureSubmitSnapshot(ref payload);
                return payload.Trace;
            }

            MatDataTransferRuntime.RequestEditorUpdate();
            return payload.Trace;
        }

        private static bool TryValidate(
            ref ParamTransferPayload payload,
            MatDataTransferFeature feature)
        {
            if (payload.Identity.Target == null)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.InstanceMissing,
                    "Target instance is missing.");

            if (string.IsNullOrWhiteSpace(payload.Identity.SemanticKey))
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.SemanticKeyMissing,
                    "Semantic key is empty.");

            if (string.IsNullOrWhiteSpace(payload.Identity.SourceId))
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.SourceIdMissing,
                    "Submit source id is empty.");

            if (!payload.Identity.Target.IsReady)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.InstanceMissing,
                    "Target instance is not registered by MatDataTransferFeature.");

            ParamRendererBinding binding = payload.Identity.Binding;
            if (binding.Renderer == null && binding.RendererId == 0)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.RendererOrMaterialSlotMissing,
                    "Material target binding is missing.");

            if (binding.MaterialSlot < 0)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.RendererOrMaterialSlotMissing,
                    "Material target binding needs a material slot.");

            if (feature == null)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.FeatureMissing,
                    "No active MatDataTransferFeature.");

            if (feature.GetRequestProvider(MatDataTransferProviderNames.GenericMaterialParameter) == null)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.ProviderUnavailable,
                    "Generic material parameter provider is unavailable.");

            RendererMaterialBinding bindingTarget = payload.Identity.Target.QueryBinding(payload.Identity.Binding);
            if (bindingTarget == null)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.BindingMissing,
                    "No renderer/material binding matches the submit target.");

            if (!TryFindCatalogProperty(bindingTarget, payload.Identity.SemanticKey, feature, out CatalogProperty property, out bool hasCatalog))
            {
                ParamWriteResultCode code = hasCatalog
                    ? ParamWriteResultCode.PropertyMissing
                    : ParamWriteResultCode.BindingMissing;
                string message = hasCatalog
                    ? $"No catalog property found for semantic key: {payload.Identity.SemanticKey}."
                    : "No shader catalog matches the submit target.";
                return RejectSubmit(ref payload, code, message);
            }

            ParamValueType expectedType = property.PropertyInfo.ValueType;
            if (expectedType != payload.Identity.Value.Type)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.TypeMismatch,
                    $"Type mismatch: {payload.Identity.SemanticKey} expects {expectedType}, got {payload.Identity.Value.Type}.");

            return true;
        }

        private static bool RejectSubmit(
            ref ParamTransferPayload payload,
            ParamWriteResultCode code,
            string message)
        {
            MatDataTransferLogging.AppendSubmitStep(
                ref payload,
                ParamSubmitStep.Rejected(
                    "Submit.Validate",
                    code,
                    message));
            return false;
        }

        private static bool TryFindCatalogProperty(
            RendererMaterialBinding binding,
            string semanticKey,
            MatDataTransferFeature feature,
            out CatalogProperty property,
            out bool hasCatalog)
        {
            property = null;
            hasCatalog = false;

            if (binding == null || string.IsNullOrEmpty(binding.ShaderName))
                return false;

            if (feature.TryGetCatalogForShader(binding.ShaderName, out _))
                hasCatalog = true;

            if (feature.TryGetProperty(binding.ShaderName, semanticKey, out _, out property))
                return property?.PropertyInfo != null;

            return false;
        }

        private static RendererMaterialBinding ResolveTargetBinding(
            MatDataTransferInstance target,
            Renderer targetRenderer,
            int materialSlot)
        {
            if (target == null || targetRenderer == null || materialSlot < 0)
                return null;

            return target.QueryBinding(targetRenderer, materialSlot);
        }
    }
}
