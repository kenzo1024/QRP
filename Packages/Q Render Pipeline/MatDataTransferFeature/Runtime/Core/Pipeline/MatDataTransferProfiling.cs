using Unity.Profiling;
#if ENABLE_PROFILER
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
#endif

namespace Rendering.MatDataTransfer.Runtime
{
    internal static class MatDataTransferProfiling
    {
        internal static readonly ProfilerMarker SubmitValidate = new ProfilerMarker("MDT.Submit.Validate");
        internal static readonly ProfilerMarker SubmitExpandScope = new ProfilerMarker("MDT.Submit.ExpandScope");
        internal static readonly ProfilerMarker ModuleSubmitter = new ProfilerMarker("MDT.Module.Submitter");
        internal static readonly ProfilerMarker ModuleResolver = new ProfilerMarker("MDT.Module.Resolver");
        internal static readonly ProfilerMarker ModuleWriter = new ProfilerMarker("MDT.Module.Writer");
        internal static readonly ProfilerMarker PassSyncInstances = new ProfilerMarker("MDT.Pass.SyncInstances");
        internal static readonly ProfilerMarker PassPipeline = new ProfilerMarker("MDT.Pass.Pipeline");
        internal static readonly ProfilerMarker PipelineResolve = new ProfilerMarker("MDT.Pipeline.Resolve");
        internal static readonly ProfilerMarker PipelineWrite = new ProfilerMarker("MDT.Pipeline.Write");
        internal static readonly ProfilerMarker PipelineWriteResolveMaterial = new ProfilerMarker("MDT.Pipeline.Write.ResolveMaterial");
        internal static readonly ProfilerMarker PipelineWriteSetValue = new ProfilerMarker("MDT.Pipeline.Write.SetValue");
        internal static readonly ProfilerMarker LoggingCapture = new ProfilerMarker("MDT.Logging.Capture");
        internal static readonly ProfilerMarker LoggingCommitTimeline = new ProfilerMarker("MDT.Logging.CommitTimeline");

#if ENABLE_PROFILER
        private static readonly ProfilerCategory s_Category = new ProfilerCategory("MatDataTransfer");
        private const ProfilerCounterOptions CounterOptions =
            ProfilerCounterOptions.FlushOnEndOfFrame | ProfilerCounterOptions.ResetToZeroOnFlush;

        private static readonly IntCounter s_PayloadCount = new IntCounter("MDT Payload Count");
        private static readonly IntCounter s_TraceCount = new IntCounter("MDT Trace Count");
        private static readonly IntCounter s_StepCount = new IntCounter("MDT Step Count");
        private static readonly IntCounter s_TimelineRecordCount = new IntCounter("MDT Timeline Record Count");
        private static readonly IntCounter s_MaterialArrayReadCount = new IntCounter("MDT Material Array Read Count");

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddPayload() => s_PayloadCount.Increment();
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddTrace() => s_TraceCount.Increment();
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddStep() => s_StepCount.Increment();
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddTimelineRecord() => s_TimelineRecordCount.Increment();
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddMaterialArrayRead() => s_MaterialArrayReadCount.Increment();

        private sealed unsafe class IntCounter
        {
            private readonly int* m_Value;

            internal IntCounter(string name)
            {
                System.IntPtr marker;
                m_Value = (int*)ProfilerUnsafeUtility.CreateCounterValue(
                    out marker,
                    name,
                    s_Category,
                    MarkerFlags.Counter,
                    (byte)ProfilerMarkerDataType.Int32,
                    (byte)ProfilerMarkerDataUnit.Count,
                    sizeof(int),
                    CounterOptions);
            }

            internal void Increment()
            {
                if (m_Value != null)
                    (*m_Value)++;
            }
        }
#else
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddPayload() { }
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddTrace() { }
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddStep() { }
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddTimelineRecord() { }
        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        internal static void AddMaterialArrayRead() { }
#endif
    }
}
