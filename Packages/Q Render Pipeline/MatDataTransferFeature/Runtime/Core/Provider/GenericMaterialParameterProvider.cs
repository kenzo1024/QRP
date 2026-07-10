using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public sealed class GenericMaterialParameterProviderSettings
    {
        public bool Enabled = true;
    }

    internal sealed class GenericMaterialParameterProvider : IMatDataTransferRequestProvider
    {
        internal const string ProviderName = MatDataTransferProviderNames.GenericMaterialParameter;

        private readonly List<ParamTransferPayload> m_Requests =
            new List<ParamTransferPayload>();
        private readonly GenericMaterialParameterProviderSettings m_Settings;

        internal GenericMaterialParameterProvider(GenericMaterialParameterProviderSettings settings)
        {
            m_Settings = settings;
        }

        public string Name => ProviderName;

        public bool TrySubmit(ref ParamTransferPayload payload)
        {
            if (!IsEnabled())
            {
                ClearRequests();
                MatDataTransferLogging.AppendSubmitStep(
                    ref payload,
                    ParamSubmitStep.Rejected(
                        "Submit.Queue",
                        ParamWriteResultCode.ProviderUnavailable,
                        "Generic material parameter provider rejected the request."));
                return false;
            }

            payload.ProviderName = ProviderName;
            MatDataTransferLogging.AppendSubmitStep(
                ref payload,
                ParamSubmitStep.Queued(
                    "Submit.Queue",
                    "Submit accepted."));
            m_Requests.Add(payload);

            return true;
        }

        public bool TryClearRequests(MatDataTransferInstance instance)
        {
            if (!IsEnabled() || instance == null)
                return false;

            bool removed = false;
            for (int i = m_Requests.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(m_Requests[i].Identity.Target, instance))
                    continue;

                m_Requests.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        public void Dispose()
        {
            ClearRequests();
        }

        public void SubmitRequests(ParamRequestContext context)
        {
            if (context == null || !IsEnabled())
            {
                ClearRequests();
                return;
            }

            for (int i = 0; i < m_Requests.Count; i++)
                context.Submit(m_Requests[i]);

            m_Requests.Clear();
        }

        public void ClearRequests()
        {
            m_Requests.Clear();
        }

        private bool IsEnabled()
        {
            return m_Settings != null && m_Settings.Enabled;
        }
    }
}
