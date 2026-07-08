using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly List<ParamTransferPayload> m_FramePayloads =
            new List<ParamTransferPayload>();

        private MaterialWriteContext m_WriteContext;
        private MatDataTransferResolver m_Resolver;
        private bool m_GenericProviderWasEnabled;
        private string m_LastNotReadyReason;

        private static void ClearQueuedRequests()
        {
            GenericMaterialParameterProvider.ClearAllRequests();
        }

        private static void ClearRequestsForInstance(MatDataTransferInstance instance)
        {
            GenericMaterialParameterProvider.TryClearQueuedRequests(instance);
        }

        private void InitializeRequestPipeline()
        {
            m_FramePayloads.Clear();
            m_WriteContext = new MaterialWriteContext(this, m_FramePayloads);
            m_Resolver = new MatDataTransferResolver();
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

                SyncLiveInstances();
                m_WriteContext.BeginFrame();
                SubmitRequests(m_WriteContext);

                if (m_FramePayloads.Count == 0)
                    return;

                IReadOnlyList<MaterialWriteCommand> commands = m_Resolver.Resolve(
                    Catalogs,
                    InstanceRegister,
                    m_FramePayloads);

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

            if (InstanceRegister == null)
            {
                notReadyReason = "Request pipeline is not ready: instance id registry is missing.";
                return false;
            }

            if (m_WriteContext == null)
            {
                notReadyReason = "Request pipeline is not ready: write context is missing.";
                return false;
            }

            if (m_Resolver == null)
            {
                notReadyReason = "Request pipeline is not ready: request resolver is missing.";
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
            m_WriteContext = null;
            m_Resolver = null;
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

        internal IMatDataTransferRequestProvider GetRequestProvider(string providerName)
        {
            return GetProvider(providerName);
        }
    }
}
