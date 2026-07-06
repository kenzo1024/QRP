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

        private void SubmitRequests(MaterialWriteContext context)
        {
            for (int i = 0; i < m_Providers.Count; i++)
                m_Providers[i].SubmitRequests(context);
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
    }
}
