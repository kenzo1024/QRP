using System.Collections.Generic;
namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MaterialWriteContext
    {
        private readonly MatDataTransferFeature m_Feature;
        private readonly List<MaterialParameterSubmitPayload> m_Payloads;
        private int m_Sequence;

        internal MaterialWriteContext(MatDataTransferFeature feature, List<MaterialParameterSubmitPayload> payloads)
        {
            m_Feature = feature;
            m_Payloads = payloads;
        }

        internal IReadOnlyList<ShaderPropertyCatalog> Catalogs => m_Feature?.Catalogs;

        internal void BeginFrame()
        {
            m_Payloads.Clear();
            m_Sequence = 0;
        }

        internal void Submit(MaterialParameterSubmitPayload payload)
        {
            payload.Sequence = ++m_Sequence;
            m_Payloads.Add(payload);
        }
    }
}
