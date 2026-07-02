namespace Rendering.MatDataTransfer.Runtime
{
    internal interface IMatDataTransferRequestProvider : System.IDisposable
    {
        string Name { get; }
        void SubmitRequests(MaterialWriteContext context);
    }
}
