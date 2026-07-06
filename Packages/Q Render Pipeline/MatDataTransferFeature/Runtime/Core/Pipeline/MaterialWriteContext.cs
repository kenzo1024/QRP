using System.Collections.Generic;
namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MaterialWriteContext
    {
        private readonly MatDataTransferFeature m_Feature;
        private readonly List<ParamTransferPayload> m_Payloads;

        internal MaterialWriteContext(MatDataTransferFeature feature, List<ParamTransferPayload> payloads)
        {
            m_Feature = feature;
            m_Payloads = payloads;
        }

        internal IReadOnlyList<ShaderPropertyCatalog> Catalogs => m_Feature?.Catalogs;

        internal void BeginFrame()
        {
            m_Payloads.Clear();
        }

        internal void Submit(ParamTransferPayload payload)
        {
            m_Payloads.Add(payload);
        }
    }
}
