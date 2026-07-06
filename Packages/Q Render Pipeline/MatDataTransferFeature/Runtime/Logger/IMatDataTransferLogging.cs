using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public interface IMatDataTransferLogging
    {
        IReadOnlyList<MatDataTransferTimelineRecord> TimelineRecords { get; }
        IReadOnlyList<MatDataTransferTimelineFrame> TimelineFrames { get; }
        IReadOnlyList<ParamWriteResult> LastReceipts { get; }
        int TimelineVersion { get; }
        int MaxTimelineFrames { get; }

        List<MatDataTransferTimelineRecord> GetTimelineList();
        List<ParamWriteResult> GetReceiptList();

        void BeginFrame();
        void CompleteFrame();
        void ClearTimelineRecords();
    }
}
