using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly List<ParamTransferPayload> m_FramePayloads =
            new List<ParamTransferPayload>();

        private ParamRequestContext m_RequestContext;
        private MaterialParameterResolver m_Resolver;
        private MaterialParameterWriter m_Writer;
        private bool m_GenericProviderWasEnabled;
        private string m_LastNotReadyReason;

        private void InitializeRequestPipeline()
        {
            m_FramePayloads.Clear();
            m_RequestContext = new ParamRequestContext(m_FramePayloads);
            m_Resolver = new MaterialParameterResolver();
            m_Writer = new MaterialParameterWriter();
            ApplyWriterSettings();
            m_GenericProviderWasEnabled = IsGenericMaterialParameterProviderEnabled;
            m_LastNotReadyReason = null;
        }

        private void ExecuteRequestPipeline()
        {
            if (!IsPrimaryInstance())
                return;

            Logging.BeginFrame();
            try
            {
                if (HandleGenericProviderState(out bool shouldClearWrittenState))
                {
                    if (shouldClearWrittenState)
                        ClearWrittenState();

                    return;
                }

                if (!TryValidateReady())
                    return;

                ApplyWriterSettings();
                using (MatDataTransferProfiling.PassSyncInstances.Auto())
                    SyncLiveInstances();
                m_RequestContext.BeginFrame();
                using (MatDataTransferProfiling.PipelineDrainProviders.Auto())
                    SubmitRequests(m_RequestContext);

                if (m_FramePayloads.Count == 0)
                    return;

                IReadOnlyList<ParamWriteCommand> commands;
                using (MatDataTransferProfiling.PipelineResolve.Auto())
                {
                    commands = m_Resolver.Resolve(
                        Catalogs,
                        MatDataTransferInstanceRegister,
                        m_FramePayloads);
                }

                using (MatDataTransferProfiling.PipelineWrite.Auto())
                    ApplyWriteCommands(commands);
            }
            finally
            {
                Logging.CompleteFrame();
            }
        }

        private bool IsRequestPipelineReady(out string notReadyReason)
        {
            if (!IsPrimaryInstance())
            {
                notReadyReason = "Request pipeline is not ready: feature is not the primary instance.";
                return false;
            }

            if (MatDataTransferInstanceRegister == null)
            {
                notReadyReason = "Request pipeline is not ready: instance id registry is missing.";
                return false;
            }

            if (m_RequestContext == null)
            {
                notReadyReason = "Request pipeline is not ready: request context is missing.";
                return false;
            }

            if (m_Resolver == null)
            {
                notReadyReason = "Request pipeline is not ready: request resolver is missing.";
                return false;
            }

            if (m_Writer == null)
            {
                notReadyReason = "Request pipeline is not ready: material parameter writer is missing.";
                return false;
            }

            if (IsGenericMaterialParameterProviderEnabled && !IsGenericProviderReady())
            {
                notReadyReason = "Request pipeline is not ready: generic material parameter provider is enabled but not registered.";
                return false;
            }

            notReadyReason = null;
            return true;
        }

        private void DisposeRequestPipeline()
        {
            m_FramePayloads.Clear();
            ClearWrittenState();
            m_RequestContext = null;
            m_Resolver = null;
            m_Writer = null;
            m_GenericProviderWasEnabled = false;
            m_LastNotReadyReason = null;
        }

        private bool HandleGenericProviderState(out bool shouldClearWrittenState)
        {
            shouldClearWrittenState = false;
            if (IsGenericMaterialParameterProviderEnabled)
            {
                EnsureGenericProviderRegistered();
                m_GenericProviderWasEnabled = true;
                return false;
            }

            ClearQueuedRequests();
            shouldClearWrittenState = m_GenericProviderWasEnabled;
            m_GenericProviderWasEnabled = false;
            m_FramePayloads.Clear();
            return true;
        }

        private bool TryValidateReady()
        {
            if (IsRequestPipelineReady(out string reason))
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

    }
}
