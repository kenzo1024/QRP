namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal struct MatDataTransferBatchFrameStats
    {
        public MatDataTransferBatchPhase Phase;
        public MatDataTransferBatchPipelineMode PipelineMode;
        public bool IsFirstFrameSample;
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
        public long ModuleSubmitterNanoseconds;
        public long SubmitValidateNanoseconds;
        public long PassSyncInstancesNanoseconds;
        public long PassPipelineNanoseconds;
        public long ModuleResolverNanoseconds;
        public long PipelineResolveGcAllocatedBytes;
        public long ModuleWriterNanoseconds;
        public long PipelineWriteResolveMaterialNanoseconds;
        public long PipelineWriteSetValueNanoseconds;
        public long LoggingCaptureNanoseconds;
        public long LoggingCommitTimelineNanoseconds;
        public int SyncCallCount;
        public long SyncElapsedNanoseconds;
        public long SyncGcAllocatedBytes;
        public int LiveInstanceCount;
        public int PipelineExecutionCount;
        public int DirectWriteCount;
        public long ComparableWorkNanoseconds;
    }
}
