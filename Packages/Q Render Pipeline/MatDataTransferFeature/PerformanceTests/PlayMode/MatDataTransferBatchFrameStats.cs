namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal struct MatDataTransferBatchFrameStats
    {
        public int FrameIndex;
        public double FrameMilliseconds;
        public long GcAllocatedBytes;
        public long ManagedHeapBytes;
        public int ActiveInstanceCount;
        public int PayloadCount;
        public int GroupCount;
        public int CommandCount;
        public int AppliedCount;
        public int OverriddenCount;
        public int RejectedCount;
        public int WriterFailedCount;
        public int TraceCount;
        public int TimelineRecordCount;
        public int MaterialArrayReadCount;
        public long SubmitTotalNanoseconds;
        public long SubmitValidateNanoseconds;
        public long PassSyncInstancesNanoseconds;
        public long PassPipelineNanoseconds;
        public long PipelineDrainProvidersNanoseconds;
        public long PipelineResolveNanoseconds;
        public long PipelineResolveTargetNanoseconds;
        public long PipelineResolveConflictNanoseconds;
        public long PipelineWriteNanoseconds;
        public long PipelineWriteResolveMaterialNanoseconds;
        public long PipelineWriteSetValueNanoseconds;
        public long LoggingCaptureNanoseconds;
        public long LoggingCommitTimelineNanoseconds;
    }
}
