namespace Rendering.MatDataTransfer.Runtime
{
    internal interface IMatDataTransferRequestProvider : System.IDisposable
    {
        string Name { get; }
        bool TrySubmit(ref ParamTransferPayload payload);
        bool TryClearRequests(MatDataTransferInstance instance);
        void ClearRequests();
        void SubmitRequests(ParamRequestContext context);
    }
}
