using System.Collections.Generic;
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
            ParamWriteLayer layer,
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
            ParamWriteLayer layer,
            int priority = 0)
        {
            RendererMaterialBinding binding = ResolveTargetBinding(target, targetRenderer, materialSlot);
            return Submit(target, semanticKey, value, binding, source, layer, priority);
        }

        internal static ParamSubmitTrace Submit(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            ParamSubmitScope scope,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            ParamSubmitTrace rootTrace = new ParamSubmitTrace();
            rootTrace.AddStep(ParamSubmitStep.Submitted(
                "Scope.Begin",
                "Scope submit created."));

            if (!TryValidateScopeRoot(target, semanticKey, scope, source, rootTrace))
                return rootTrace;

            rootTrace.MarkBatchRoot();
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            IReadOnlyList<RendererMaterialBinding> bindings = target.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                RendererMaterialBinding binding = bindings[i];
                if (!IsBindingInsideScope(binding, scope))
                    continue;

                if (!BindingSupportsKey(binding, semanticKey, feature))
                {
                    rootTrace.AddSkipped();
                    continue;
                }

                ParamSubmitTrace childTrace = Submit(
                    target,
                    semanticKey,
                    value,
                    binding,
                    source,
                    layer,
                    priority);
                rootTrace.AddChild(childTrace);
            }

            rootTrace.AddStep(ParamSubmitStep.Queued(
                "Scope.Expand",
                $"Scope expanded: {rootTrace.Children.Count} submitted, {rootTrace.SkippedCount} skipped."));
            return rootTrace;
        }

        internal static ParamSubmitTrace ForInstance(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            return Submit(
                target,
                semanticKey,
                value,
                ParamSubmitScope.SupportsKey(),
                source,
                layer,
                priority);
        }

        internal static ParamSubmitTrace ForShader(
            MatDataTransferInstance target,
            string shaderName,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            return Submit(
                target,
                semanticKey,
                value,
                ParamSubmitScope.Shader(shaderName),
                source,
                layer,
                priority);
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

            if (!feature.TrySubmitRequestToProvider(
                MatDataTransferProviderNames.GenericMaterialParameter,
                ref payload))
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

            if (!feature.HasRequestProvider(MatDataTransferProviderNames.GenericMaterialParameter))
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

        private static bool TryValidateScopeRoot(
            MatDataTransferInstance target,
            string semanticKey,
            ParamSubmitScope scope,
            MatDataTransferSubmitSource source,
            ParamSubmitTrace trace)
        {
            if (target == null)
                return RejectScope(trace, ParamWriteResultCode.InstanceMissing, "Target instance is missing.");

            if (string.IsNullOrWhiteSpace(semanticKey))
                return RejectScope(trace, ParamWriteResultCode.SemanticKeyMissing, "Semantic key is empty.");

            if (string.IsNullOrWhiteSpace(source.Id))
                return RejectScope(trace, ParamWriteResultCode.SourceIdMissing, "Submit source id is empty.");

            if (!target.IsReady)
                return RejectScope(trace, ParamWriteResultCode.InstanceMissing, "Target instance is not registered by MatDataTransferFeature.");

            if (MatDataTransferFeature.Instance == null)
                return RejectScope(trace, ParamWriteResultCode.FeatureMissing, "No active MatDataTransferFeature.");

            if (target.Bindings == null)
                return RejectScope(trace, ParamWriteResultCode.BindingMissing, "Target instance has no renderer/material bindings.");

            if (scope.Mode == ParamSubmitScopeMode.Shader && string.IsNullOrWhiteSpace(scope.ShaderName))
                return RejectScope(trace, ParamWriteResultCode.BindingMissing, "Shader scope needs a shader name.");

            return true;
        }

        private static bool RejectScope(
            ParamSubmitTrace trace,
            ParamWriteResultCode code,
            string message)
        {
            trace?.AddStep(ParamSubmitStep.Rejected(
                "Scope.Validate",
                code,
                message));
            return false;
        }

        private static bool IsBindingInsideScope(RendererMaterialBinding binding, ParamSubmitScope scope)
        {
            if (binding == null)
                return false;

            if (scope.Mode != ParamSubmitScopeMode.Shader)
                return true;

            return string.Equals(binding.ShaderName, scope.ShaderName, System.StringComparison.Ordinal);
        }

        private static bool BindingSupportsKey(
            RendererMaterialBinding binding,
            string semanticKey,
            MatDataTransferFeature feature)
        {
            if (binding == null || feature == null)
                return false;

            return TryFindCatalogProperty(
                binding,
                semanticKey,
                feature,
                out _,
                out _);
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
