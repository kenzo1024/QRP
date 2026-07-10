namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private GenericMaterialParameterProvider m_GenericProvider;

        partial void RegisterBuiltInProviders()
        {
            if (IsGenericMaterialParameterProviderEnabled)
                EnsureGenericProviderRegistered();
        }

        partial void OnProvidersDisposed()
        {
            m_GenericProvider = null;
        }

        private bool IsGenericProviderReady()
        {
            return m_GenericProvider != null
                && ReferenceEquals(GetProvider(GenericMaterialParameterProvider.ProviderName), m_GenericProvider);
        }

        private void EnsureGenericProviderRegistered()
        {
            IMatDataTransferRequestProvider existing = GetProvider(GenericMaterialParameterProvider.ProviderName);
            if (existing is GenericMaterialParameterProvider genericProvider)
            {
                m_GenericProvider = genericProvider;
                return;
            }

            if (existing != null)
                return;

            m_GenericProvider = new GenericMaterialParameterProvider(m_GenericProviderSettings);
            Register(m_GenericProvider);
        }
    }
}
