using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly List<IMatDataTransferRequestProvider> m_Providers =
            new List<IMatDataTransferRequestProvider>();
        private readonly Dictionary<string, IMatDataTransferRequestProvider> m_ProviderMap =
            new Dictionary<string, IMatDataTransferRequestProvider>();

        private void InitializeProviders()
        {
            DisposeProviders();
            RegisterBuiltInProviders();
        }

        private IMatDataTransferRequestProvider GetProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
                return null;

            return m_ProviderMap.TryGetValue(providerName, out IMatDataTransferRequestProvider provider)
                ? provider
                : null;
        }

        private bool HasProvider(string providerName)
        {
            return GetProvider(providerName) != null;
        }

        private void SubmitRequests(ParamRequestContext context)
        {
            for (int i = 0; i < m_Providers.Count; i++)
                m_Providers[i].SubmitRequests(context);
        }

        private bool TrySubmitRequest(string providerName, ref ParamTransferPayload payload)
        {
            IMatDataTransferRequestProvider provider = GetProvider(providerName);
            if (provider == null)
            {
                MatDataTransferLogging.AppendSubmitStep(
                    ref payload,
                    ParamSubmitStep.Rejected(
                        "Submit.Queue",
                        ParamWriteResultCode.ProviderUnavailable,
                        "Request provider is unavailable."));
                return false;
            }

            return provider.TrySubmit(ref payload);
        }

        private void ClearQueuedRequests()
        {
            for (int i = 0; i < m_Providers.Count; i++)
                m_Providers[i].ClearRequests();
        }

        private void ClearRequestsForInstance(MatDataTransferInstance instance)
        {
            if (instance == null)
                return;

            for (int i = 0; i < m_Providers.Count; i++)
                m_Providers[i].TryClearRequests(instance);
        }

        private void DisposeProviders()
        {
            for (int i = 0; i < m_Providers.Count; i++)
                m_Providers[i]?.Dispose();

            m_Providers.Clear();
            m_ProviderMap.Clear();
            OnProvidersDisposed();
        }

        private void Register(IMatDataTransferRequestProvider provider)
        {
            if (provider == null || string.IsNullOrEmpty(provider.Name))
                return;

            if (m_ProviderMap.ContainsKey(provider.Name))
            {
                provider.Dispose();
                return;
            }

            m_Providers.Add(provider);
            m_ProviderMap.Add(provider.Name, provider);
        }

        partial void RegisterBuiltInProviders();
        partial void OnProvidersDisposed();

        internal bool TrySubmitRequestToProvider(string providerName, ref ParamTransferPayload payload)
        {
            return TrySubmitRequest(providerName, ref payload);
        }

        internal bool HasRequestProvider(string providerName)
        {
            return HasProvider(providerName);
        }
    }
}
