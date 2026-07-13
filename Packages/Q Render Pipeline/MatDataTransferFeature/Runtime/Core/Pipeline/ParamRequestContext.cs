using System.Collections.Generic;
namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class ParamRequestContext
    {
        private readonly List<ParamTransferPayload> m_Payloads;

        internal ParamRequestContext(List<ParamTransferPayload> payloads)
        {
            m_Payloads = payloads;
        }

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
