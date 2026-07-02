using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public interface IMatDataTransferLogging
    {
        IReadOnlyList<MatDataTransferTimelineRecord> TimelineRecords { get; }
        IReadOnlyList<ParamWriteResult> LastReceipts { get; }
        int TimelineVersion { get; }

        List<MatDataTransferTimelineRecord> GetTimelineList();
        List<ParamWriteResult> GetReceiptList();

        void BeginFrame();
        void CompleteFrame();
        void ClearTimelineRecords();
    }
}
