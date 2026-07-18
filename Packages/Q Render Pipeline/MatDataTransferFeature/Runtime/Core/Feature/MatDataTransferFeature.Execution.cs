using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly List<ValidatedParamRequest> m_ValidatedRequests =
            new List<ValidatedParamRequest>();
        private readonly List<RequestDiagnosticContext> m_RequestDiagnostics =
            new List<RequestDiagnosticContext>();
        private readonly List<ConflictDecision> m_ConflictDecisions =
            new List<ConflictDecision>();

        private MaterialParameterResolver m_Resolver;
        private MaterialParameterWriter m_Writer;
        private string m_LastNotReadyReason;
        private ResolutionStats m_ResolutionStats;

        internal bool CanAcceptRequests => IsTransferPipelineReady(out _);

        private void InitializeTransferPipeline()
        {
            ClearQueuedRequests();
            m_Resolver = new MaterialParameterResolver();
            m_Writer = new MaterialParameterWriter();
            ApplyWriterSettings();
            m_LastNotReadyReason = null;
        }

        private void ExecuteTransferPipeline()
        {
            if (!IsPrimaryInstance())
                return;

            OnTransferPipelineExecuted();

            Logging.BeginFrame();
            try
            {
                if (!TryValidateReady())
                    return;

                ApplyWriterSettings();
                m_ResolutionStats.Reset();
                if (m_ValidatedRequests.Count == 0)
                    return;

                IReadOnlyList<ParamWriteCommand> commands;
                bool diagnosticsEnabled = Logging.IsEnabled;
                using (MatDataTransferProfiling.PipelineResolve.Auto())
                {
                    commands = diagnosticsEnabled
                        ? m_Resolver.ResolveWithDiagnostics(
                            m_ValidatedRequests,
                            m_ConflictDecisions,
                            ref m_ResolutionStats)
                        : m_Resolver.ResolveWithoutDiagnostics(
                            m_ValidatedRequests,
                            ref m_ResolutionStats);
                }

                if (diagnosticsEnabled)
                    CaptureConflictDecisions();

                using (MatDataTransferProfiling.PipelineWrite.Auto())
                    ApplyWriteCommands(commands, m_RequestDiagnostics);
            }
            finally
            {
                Logging.CompleteFrame();
                ClearQueuedRequests();
            }
        }

        private bool IsTransferPipelineReady(out string notReadyReason)
        {
            if (!IsPrimaryInstance())
            {
                notReadyReason = "Transfer pipeline is not ready: feature is not the primary instance.";
                return false;
            }

            if (m_InstanceRegister == null)
            {
                notReadyReason = "Transfer pipeline is not ready: instance id registry is missing.";
                return false;
            }

            if (m_Resolver == null)
            {
                notReadyReason = "Transfer pipeline is not ready: request resolver is missing.";
                return false;
            }

            if (m_Writer == null)
            {
                notReadyReason = "Transfer pipeline is not ready: material parameter writer is missing.";
                return false;
            }

            notReadyReason = null;
            return true;
        }

        private void DisposeTransferPipeline()
        {
            ClearQueuedRequests();
            ClearWrittenState();
            m_Resolver = null;
            m_Writer = null;
            m_LastNotReadyReason = null;
        }

        internal void EnqueueValidatedRequest(
            ref ParamTransferPayload payload,
            RendererMaterialBinding binding,
            ParamBindingResolution bindingResolution)
        {
            int requestId = m_RequestDiagnostics.Count;
            int instanceId = payload.Identity.Target != null
                ? payload.Identity.Target.InstanceId
                : -1;
            int propertyId = bindingResolution.PropertyId;
            ParamWriteTarget writeTarget = new ParamWriteTarget(
                binding.Renderer,
                binding.MaterialSlot,
                propertyId);
            RequestStrength strength = new RequestStrength(
                ParamWriteLayers.GetStrength(payload.WriteConfig.Layer),
                payload.WriteConfig.Priority,
                payload.SubmitFrameIndex,
                payload.Sequence);

            m_RequestDiagnostics.Add(new RequestDiagnosticContext(payload, bindingResolution));
            m_ValidatedRequests.Add(new ValidatedParamRequest(
                instanceId,
                new ConflictKey(instanceId, binding.RendererId, binding.MaterialSlot, propertyId),
                writeTarget,
                payload.Identity.Value,
                strength,
                requestId));
            MatDataTransferLogging.AppendSubmitStep(
                ref payload,
                ParamSubmitStep.Queued("Submit.Queue", "Submit accepted."));
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void CaptureConflictDecisions()
        {
            for (int i = 0; i < m_ConflictDecisions.Count; i++)
            {
                ConflictDecision decision = m_ConflictDecisions[i];
                if (!TryGetDiagnostic(decision.LoserRequestId, out RequestDiagnosticContext loser)
                    || !TryGetDiagnostic(decision.WinnerRequestId, out RequestDiagnosticContext winner))
                    continue;

                ParamTransferPayload payload = loser.Payload;
                MatDataTransferLogging.CaptureResolvedSnapshot(
                    ref payload,
                    loser.BindingResolution,
                    ParamSubmitStep.Overridden(
                        "Resolve.Conflict",
                        winner.Payload.Identity.SourceId),
                    string.Empty,
                    payload.Identity.Binding.Renderer);
            }
        }

        private bool TryGetDiagnostic(int requestId, out RequestDiagnosticContext diagnostic)
        {
            if (requestId >= 0 && requestId < m_RequestDiagnostics.Count)
            {
                diagnostic = m_RequestDiagnostics[requestId];
                return true;
            }

            diagnostic = default;
            return false;
        }

        private void ClearQueuedRequests()
        {
            m_ValidatedRequests.Clear();
            m_RequestDiagnostics.Clear();
            m_ConflictDecisions.Clear();
        }

        private void ClearRequestsForInstance(MatDataTransferInstance instance)
        {
            if (instance == null)
                return;

            for (int i = m_ValidatedRequests.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(m_RequestDiagnostics[i].Payload.Identity.Target, instance))
                    continue;

                int lastIndex = m_ValidatedRequests.Count - 1;
                m_ValidatedRequests.RemoveAt(i);
                m_RequestDiagnostics.RemoveAt(i);
                for (int requestIndex = i; requestIndex < lastIndex; requestIndex++)
                {
                    ValidatedParamRequest request = m_ValidatedRequests[requestIndex];
                    m_ValidatedRequests[requestIndex] = new ValidatedParamRequest(
                        request.InstanceId,
                        request.ConflictKey,
                        request.WriteTarget,
                        request.Value,
                        request.Strength,
                        requestIndex);
                }
            }

        }

        private bool TryValidateReady()
        {
            if (IsTransferPipelineReady(out string reason))
            {
                m_LastNotReadyReason = null;
                return true;
            }

            LogNotReady(reason);
            return false;
        }

        private void LogNotReady(string reason)
        {
            if (string.Equals(m_LastNotReadyReason, reason, StringComparison.Ordinal))
                return;

            m_LastNotReadyReason = reason;
            MatDataTransferLogger.LogError(reason);
        }

        partial void OnTransferPipelineExecuted();
    }
}
