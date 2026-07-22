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

            return SubmitPayload(ref payload, binding, null, null);
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
            using (MatDataTransferProfiling.ModuleSubmitter.Auto())
            {
                RendererMaterialBinding binding = ResolveTargetBinding(target, targetRenderer, materialSlot);
                return Submit(target, semanticKey, value, binding, source, layer, priority);
            }
        }

        internal static ParamBatchSubmitResult ForMaterialBatch(
            MatDataTransferInstance target,
            Renderer targetRenderer,
            int materialSlot,
            IReadOnlyList<ParamBatchWrite> writes,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            using (MatDataTransferProfiling.ModuleSubmitter.Auto())
            {
                return ForMaterialBatchProfiled(
                    target,
                    targetRenderer,
                    materialSlot,
                    writes,
                    source,
                    layer,
                    priority);
            }
        }

        private static ParamBatchSubmitResult ForMaterialBatchProfiled(
            MatDataTransferInstance target,
            Renderer targetRenderer,
            int materialSlot,
            IReadOnlyList<ParamBatchWrite> writes,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority)
        {
            int totalCount = writes != null ? writes.Count : 0;
            if (totalCount == 0)
                return new ParamBatchSubmitResult(0, 0, 0, -1, ParamWriteResultCode.None);

            if (!TryPrepareBatchContext(
                    target,
                    targetRenderer,
                    materialSlot,
                    source,
                    out MatDataTransferFeature feature,
                    out RendererMaterialBinding binding,
                    out ShaderPropertyCatalog catalog,
                    out ParamWriteResultCode commonError))
            {
                if (MatDataTransferLogging.Instance.IsEnabled)
                    RecordRejectedBatch(target, binding, writes, source, layer, priority, commonError);

                return new ParamBatchSubmitResult(
                    totalCount,
                    0,
                    totalCount,
                    0,
                    commonError);
            }

            int acceptedCount = 0;
            int rejectedCount = 0;
            int firstRejectedIndex = -1;
            ParamWriteResultCode firstErrorCode = ParamWriteResultCode.None;
            bool loggingEnabled = MatDataTransferLogging.Instance.IsEnabled;
            for (int i = 0; i < totalCount; i++)
            {
                ParamBatchWrite write = writes[i];
                int sequence = MatDataTransferSubmitSequence.Next(out int submitFrameIndex);
                if (!TryValidateBatchItem(
                        catalog,
                        write,
                        out CatalogProperty property,
                        out ParamWriteResultCode itemError))
                {
                    rejectedCount++;
                    if (firstRejectedIndex < 0)
                    {
                        firstRejectedIndex = i;
                        firstErrorCode = itemError;
                    }

                    if (loggingEnabled)
                    {
                        RecordRejectedBatchItem(
                            target,
                            binding,
                            write,
                            source,
                            layer,
                            priority,
                            submitFrameIndex,
                            sequence,
                            itemError);
                    }
                    continue;
                }

                if (loggingEnabled)
                {
                    EnqueueLoggedBatchItem(
                        feature,
                        target,
                        binding,
                        catalog,
                        property,
                        write,
                        source,
                        layer,
                        priority,
                        submitFrameIndex,
                        sequence);
                }
                else
                {
                    feature.EnqueueValidatedBatchItem(
                        target,
                        binding,
                        write.Value,
                        layer,
                        priority,
                        submitFrameIndex,
                        sequence,
                        ParamBindingResolution.ResolvePropertyId(property.PropertyInfo.PropertyName));
                }

                acceptedCount++;
            }

            if (acceptedCount > 0)
                MatDataTransferRuntime.RequestEditorUpdate();

            return new ParamBatchSubmitResult(
                totalCount,
                acceptedCount,
                rejectedCount,
                firstRejectedIndex,
                firstErrorCode);
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
            using (MatDataTransferProfiling.ModuleSubmitter.Auto())
                return SubmitScope(target, semanticKey, value, scope, source, layer, priority);
        }

        private static ParamSubmitTrace SubmitScope(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            ParamSubmitScope scope,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority)
        {
            ParamSubmitTrace rootTrace = new ParamSubmitTrace();
            MatDataTransferLogging.RecordTraceStep(
                rootTrace,
                ParamSubmitStage.ScopeBegin,
                ParamWriteStatus.Submitted,
                ParamWriteResultCode.None,
                "Scope submit created.");

            bool isValid;
            using (MatDataTransferProfiling.SubmitValidate.Auto())
                isValid = TryValidateScopeRoot(target, semanticKey, scope, source, rootTrace);

            if (!isValid)
                return rootTrace;

            rootTrace.MarkBatchRoot();
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            IReadOnlyList<RendererMaterialBinding> bindings = target.Bindings;
            using (MatDataTransferProfiling.SubmitExpandScope.Auto())
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    RendererMaterialBinding binding = bindings[i];
                    if (!IsBindingInsideScope(binding, scope))
                        continue;

                    if (!TryResolveBindingProperty(
                            binding,
                            semanticKey,
                            feature,
                            out ShaderPropertyCatalog catalog,
                            out CatalogProperty property))
                    {
                        rootTrace.AddSkipped();
                        continue;
                    }

                    ParamSubmitTrace childTrace = SubmitResolved(
                        target,
                        semanticKey,
                        value,
                        binding,
                        catalog,
                        property,
                        source,
                        layer,
                        priority);
                    rootTrace.AddChild(childTrace);
                }

                MatDataTransferLogging.RecordScopeExpanded(
                    rootTrace,
                    rootTrace.Children.Count,
                    rootTrace.SkippedCount);
            }
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

        private static ParamSubmitTrace SubmitResolved(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding,
            ShaderPropertyCatalog catalog,
            CatalogProperty property,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority)
        {
            ParamRequestIdentity identity = new ParamRequestIdentity(
                target,
                source,
                semanticKey,
                value,
                binding);
            ParamTransferPayload payload = new ParamTransferPayload(
                identity,
                new ParamWriteConfig(layer, priority));
            return SubmitPayload(ref payload, binding, catalog, property);
        }

        private static ParamSubmitTrace SubmitPayload(
            ref ParamTransferPayload payload,
            RendererMaterialBinding knownBinding,
            ShaderPropertyCatalog knownCatalog,
            CatalogProperty knownProperty)
        {
            return SubmitPayloadProfiled(
                ref payload,
                knownBinding,
                knownCatalog,
                knownProperty);
        }

        private static ParamSubmitTrace SubmitPayloadProfiled(
            ref ParamTransferPayload payload,
            RendererMaterialBinding knownBinding,
            ShaderPropertyCatalog knownCatalog,
            CatalogProperty knownProperty)
        {
            payload.Sequence = MatDataTransferSubmitSequence.Next(out int submitFrameIndex);
            payload.SubmitFrameIndex = submitFrameIndex;
            MatDataTransferLogging.RecordSubmitStep(
                ref payload,
                ParamSubmitStage.SubmitBegin,
                ParamWriteStatus.Submitted,
                ParamWriteResultCode.None,
                "Submit payload created.");

            MatDataTransferFeature feature = MatDataTransferFeature.Instance;

            bool isValid;
            RendererMaterialBinding binding;
            ShaderPropertyCatalog catalog;
            CatalogProperty property;
            using (MatDataTransferProfiling.SubmitValidate.Auto())
                isValid = TryValidate(
                    ref payload,
                    feature,
                    knownBinding,
                    knownCatalog,
                    knownProperty,
                    out binding,
                    out catalog,
                    out property);

            if (!isValid)
            {
                MatDataTransferLogging.CaptureSubmitSnapshot(ref payload);
                return payload.Trace;
            }

            payload.Identity.Binding = new ParamRendererBinding(binding);
            payload.Identity.SemanticKey = property.SuggestedSemanticKey;
            ParamBindingResolution bindingResolution = ParamBindingResolution.FromCatalog(
                property,
                property.SuggestedSemanticKey,
                binding.ShaderName,
                catalog != null ? catalog.name : string.Empty);
            feature.EnqueueValidatedRequest(ref payload, binding, bindingResolution);
            return payload.Trace;
        }

        private static bool TryValidate(
            ref ParamTransferPayload payload,
            MatDataTransferFeature feature,
            RendererMaterialBinding knownBinding,
            ShaderPropertyCatalog knownCatalog,
            CatalogProperty knownProperty,
            out RendererMaterialBinding resolvedBinding,
            out ShaderPropertyCatalog resolvedCatalog,
            out CatalogProperty resolvedProperty)
        {
            resolvedBinding = knownBinding;
            resolvedCatalog = knownCatalog;
            resolvedProperty = knownProperty;

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

            if (!feature.CanAcceptRequests)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.ProviderUnavailable,
                    "Material parameter transfer pipeline is unavailable.");

            if (resolvedBinding == null)
                return RejectSubmit(
                    ref payload,
                    ParamWriteResultCode.BindingMissing,
                    "No renderer/material binding matches the submit target.");

            bool hasCatalog = resolvedCatalog != null;
            if (resolvedProperty == null
                && !TryFindCatalogProperty(
                    resolvedBinding,
                    payload.Identity.SemanticKey,
                    feature,
                    out resolvedCatalog,
                    out resolvedProperty,
                    out hasCatalog))
            {
                ParamWriteResultCode code = hasCatalog
                    ? ParamWriteResultCode.PropertyMissing
                    : ParamWriteResultCode.BindingMissing;
                string message = hasCatalog
                    ? $"No catalog property found for semantic key: {payload.Identity.SemanticKey}."
                    : "No shader catalog matches the submit target.";
                return RejectSubmit(ref payload, code, message);
            }

            ParamValueType expectedType = resolvedProperty.PropertyInfo.ValueType;
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
            MatDataTransferLogging.RecordSubmitStep(
                ref payload,
                ParamSubmitStage.SubmitValidate,
                ParamWriteStatus.Rejected,
                code,
                message);
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
            MatDataTransferLogging.RecordTraceStep(
                trace,
                ParamSubmitStage.ScopeValidate,
                ParamWriteStatus.Rejected,
                code,
                message);
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

        private static bool TryResolveBindingProperty(
            RendererMaterialBinding binding,
            string semanticKey,
            MatDataTransferFeature feature,
            out ShaderPropertyCatalog catalog,
            out CatalogProperty property)
        {
            catalog = null;
            property = null;
            if (binding == null || feature == null)
                return false;

            return TryFindCatalogProperty(
                binding,
                semanticKey,
                feature,
                out catalog,
                out property,
                out _);
        }

        private static bool TryFindCatalogProperty(
            RendererMaterialBinding binding,
            string semanticKey,
            MatDataTransferFeature feature,
            out ShaderPropertyCatalog catalog,
            out CatalogProperty property,
            out bool hasCatalog)
        {
            catalog = null;
            property = null;
            hasCatalog = false;

            if (binding == null || string.IsNullOrEmpty(binding.ShaderName))
                return false;

            if (!feature.TryGetCatalogForShader(binding.ShaderName, out catalog))
                return false;

            hasCatalog = true;
            if (catalog.TryGetProperty(semanticKey, out property))
                return property?.PropertyInfo != null && property.Status == CatalogPropertyStatus.Ok;

            return false;
        }

        private static bool TryPrepareBatchContext(
            MatDataTransferInstance target,
            Renderer targetRenderer,
            int materialSlot,
            MatDataTransferSubmitSource source,
            out MatDataTransferFeature feature,
            out RendererMaterialBinding binding,
            out ShaderPropertyCatalog catalog,
            out ParamWriteResultCode errorCode)
        {
            feature = MatDataTransferFeature.Instance;
            binding = null;
            catalog = null;
            errorCode = ParamWriteResultCode.None;

            if (target == null || !target.IsReady)
                errorCode = ParamWriteResultCode.InstanceMissing;
            else if (string.IsNullOrWhiteSpace(source.Id))
                errorCode = ParamWriteResultCode.SourceIdMissing;
            else if (targetRenderer == null || materialSlot < 0)
                errorCode = ParamWriteResultCode.RendererOrMaterialSlotMissing;
            else if (feature == null)
                errorCode = ParamWriteResultCode.FeatureMissing;
            else if (!feature.CanAcceptRequests)
                errorCode = ParamWriteResultCode.ProviderUnavailable;
            else if ((binding = target.QueryBinding(targetRenderer, materialSlot)) == null)
                errorCode = ParamWriteResultCode.BindingMissing;
            else if (!feature.TryGetCatalogForShader(binding.ShaderName, out catalog))
                errorCode = ParamWriteResultCode.BindingMissing;

            return errorCode == ParamWriteResultCode.None;
        }

        private static bool TryValidateBatchItem(
            ShaderPropertyCatalog catalog,
            ParamBatchWrite write,
            out CatalogProperty property,
            out ParamWriteResultCode errorCode)
        {
            property = null;
            errorCode = ParamWriteResultCode.None;
            if (string.IsNullOrWhiteSpace(write.SemanticKey))
                errorCode = ParamWriteResultCode.SemanticKeyMissing;
            else if (catalog == null
                || !catalog.TryGetProperty(write.SemanticKey, out property)
                || property?.PropertyInfo == null
                || property.Status != CatalogPropertyStatus.Ok)
                errorCode = ParamWriteResultCode.PropertyMissing;
            else if (property.PropertyInfo.ValueType != write.Value.Type)
                errorCode = ParamWriteResultCode.TypeMismatch;

            return errorCode == ParamWriteResultCode.None;
        }

        private static void EnqueueLoggedBatchItem(
            MatDataTransferFeature feature,
            MatDataTransferInstance target,
            RendererMaterialBinding binding,
            ShaderPropertyCatalog catalog,
            CatalogProperty property,
            ParamBatchWrite write,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority,
            int submitFrameIndex,
            int sequence)
        {
            ParamTransferPayload payload = CreateBatchPayload(
                target,
                binding,
                property.SuggestedSemanticKey,
                write.Value,
                source,
                layer,
                priority,
                submitFrameIndex,
                sequence);
            MatDataTransferLogging.RecordSubmitStep(
                ref payload,
                ParamSubmitStage.SubmitBegin,
                ParamWriteStatus.Submitted,
                ParamWriteResultCode.None,
                "Submit payload created.");
            ParamBindingResolution resolution = ParamBindingResolution.FromCatalog(
                property,
                property.SuggestedSemanticKey,
                binding.ShaderName,
                catalog != null ? catalog.name : string.Empty);
            feature.EnqueueValidatedRequest(ref payload, binding, resolution);
        }

        private static void RecordRejectedBatchItem(
            MatDataTransferInstance target,
            RendererMaterialBinding binding,
            ParamBatchWrite write,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority,
            int submitFrameIndex,
            int sequence,
            ParamWriteResultCode errorCode)
        {
            ParamTransferPayload payload = CreateBatchPayload(
                target,
                binding,
                write.SemanticKey,
                write.Value,
                source,
                layer,
                priority,
                submitFrameIndex,
                sequence);
            MatDataTransferLogging.RecordSubmitStep(
                ref payload,
                ParamSubmitStage.SubmitBegin,
                ParamWriteStatus.Submitted,
                ParamWriteResultCode.None,
                "Submit payload created.");
            RejectSubmit(ref payload, errorCode, errorCode.ToString());
            MatDataTransferLogging.CaptureSubmitSnapshot(ref payload);
        }

        private static void RecordRejectedBatch(
            MatDataTransferInstance target,
            RendererMaterialBinding binding,
            IReadOnlyList<ParamBatchWrite> writes,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority,
            ParamWriteResultCode errorCode)
        {
            for (int i = 0; i < writes.Count; i++)
            {
                int sequence = MatDataTransferSubmitSequence.Next(out int submitFrameIndex);
                RecordRejectedBatchItem(
                    target,
                    binding,
                    writes[i],
                    source,
                    layer,
                    priority,
                    submitFrameIndex,
                    sequence,
                    errorCode);
            }
        }

        private static ParamTransferPayload CreateBatchPayload(
            MatDataTransferInstance target,
            RendererMaterialBinding binding,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority,
            int submitFrameIndex,
            int sequence)
        {
            ParamTransferPayload payload = new ParamTransferPayload(
                new ParamRequestIdentity(target, source, semanticKey, value, binding),
                new ParamWriteConfig(layer, priority));
            payload.SubmitFrameIndex = submitFrameIndex;
            payload.Sequence = sequence;
            return payload;
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
