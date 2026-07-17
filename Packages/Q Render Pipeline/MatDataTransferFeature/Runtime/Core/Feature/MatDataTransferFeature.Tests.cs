#if UNITY_INCLUDE_TESTS
namespace Rendering.MatDataTransfer.Runtime
{
    internal readonly struct MatDataTransferInstanceSyncStats
    {
        internal readonly int CallCount;
        internal readonly long ElapsedNanoseconds;
        internal readonly long GcAllocatedBytes;
        internal readonly int LiveInstanceCount;
        internal readonly int RegisteredInstanceCount;
        internal readonly int PipelineExecutionCount;

        internal MatDataTransferInstanceSyncStats(
            int callCount,
            long elapsedNanoseconds,
            long gcAllocatedBytes,
            int liveInstanceCount,
            int registeredInstanceCount,
            int pipelineExecutionCount)
        {
            CallCount = callCount;
            ElapsedNanoseconds = elapsedNanoseconds;
            GcAllocatedBytes = gcAllocatedBytes;
            LiveInstanceCount = liveInstanceCount;
            RegisteredInstanceCount = registeredInstanceCount;
            PipelineExecutionCount = pipelineExecutionCount;
        }
    }

    public partial class MatDataTransferFeature
    {
        #region Lifecycle

        internal void DisposeForTests()
        {
            if (!IsPrimaryInstance())
                return;

            CleanupPrimaryInstance();
            Instance = null;
        }

        #endregion

        #region Instance Sync Stats

        private int m_TestPipelineExecutionCount;

        internal void ResetInstanceSyncStats()
        {
            m_TestPipelineExecutionCount = 0;
        }

        internal MatDataTransferInstanceSyncStats GetInstanceSyncStats()
        {
            return new MatDataTransferInstanceSyncStats(
                0,
                0L,
                0L,
                MatDataTransferInstance.TrackedLiveInstanceCount,
                m_InstanceRegister != null ? m_InstanceRegister.ActiveCount : 0,
                m_TestPipelineExecutionCount);
        }

        partial void OnRequestPipelineExecuted()
        {
            m_TestPipelineExecutionCount++;
        }

        #endregion
    }
}
#endif
