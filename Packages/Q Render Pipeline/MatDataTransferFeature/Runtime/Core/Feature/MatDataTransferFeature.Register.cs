using System.Collections.Generic;
using UnityEngine;
#if UNITY_INCLUDE_TESTS
using System;
using System.Diagnostics;
#endif

namespace Rendering.MatDataTransfer.Runtime
{
#if UNITY_INCLUDE_TESTS
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
#endif

    public partial class MatDataTransferFeature
    {
        private readonly List<MatDataTransferInstance> m_LiveInstances =
            new List<MatDataTransferInstance>();
        private MatDataTransferInstanceRegister m_InstanceRegister;
#if UNITY_INCLUDE_TESTS
        private int m_TestSyncCallCount;
        private long m_TestSyncElapsedNanoseconds;
        private long m_TestSyncGcAllocatedBytes;
        private int m_TestPipelineExecutionCount;
#endif

        private MatDataTransferInstanceRegister MatDataTransferInstanceRegister => m_InstanceRegister;

        internal static int NormalizeCapacity(int capacity)
        {
            return Mathf.Max(MatDataTransferFeature.MinInstanceCount, capacity);
        }

        private void InitializeInstanceRegister(int capacity)
        {
            capacity = NormalizeCapacity(capacity);
            m_InstanceRegister = new MatDataTransferInstanceRegister(capacity);
            m_LiveInstances.Clear();
        }

        // Called from OnValidate — no live-instance sync, just clamp and apply.
        private int ApplyInstanceCapacity(int maxInstanceCount)
        {
            maxInstanceCount = NormalizeCapacity(maxInstanceCount);
            EnsureRegistry(maxInstanceCount);

            int minimum = GetActiveInstanceCount();
            if (maxInstanceCount < minimum)
                maxInstanceCount = minimum;

            if (m_InstanceRegister.TrySetCapacity(maxInstanceCount, out bool remapped) && remapped)
                ClearQueuedRequests();

            return m_InstanceRegister.Capacity;
        }

        // Called at runtime when capacity is changed externally — syncs live instances first.
        private bool TrySetInstanceCapacity(int maxInstanceCount, out int appliedCapacity)
        {
            maxInstanceCount = NormalizeCapacity(maxInstanceCount);
            EnsureRegistry(maxInstanceCount);
            SyncLiveInstances();

            maxInstanceCount = Mathf.Max(maxInstanceCount, m_LiveInstances.Count);

            if (!m_InstanceRegister.TrySetCapacity(maxInstanceCount, out bool remapped))
            {
                appliedCapacity = m_InstanceRegister.Capacity;
                return false;
            }

            appliedCapacity = m_InstanceRegister.Capacity;
            if (remapped)
                ClearQueuedRequests();

            SyncLiveInstances();
            return true;
        }

        internal void CopyInstanceRegisterEntries(List<MatDataTransferInstanceRegisterEntry> results)
        {
            if (m_InstanceRegister == null)
            {
                results?.Clear();
                return;
            }

            SyncLiveInstances();
            m_InstanceRegister.CopyEntriesTo(results);
        }

        private void SyncLiveInstances()
        {
#if UNITY_INCLUDE_TESTS
            long startTimestamp = Stopwatch.GetTimestamp();
            long startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            m_TestSyncCallCount++;
            try
            {
                SyncLiveInstancesCore();
            }
            finally
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                m_TestSyncElapsedNanoseconds +=
                    elapsedTicks * 1000000000L / Stopwatch.Frequency;
                m_TestSyncGcAllocatedBytes += Math.Max(
                    0L,
                    GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes);
            }
#else
            SyncLiveInstancesCore();
#endif
        }

        private void SyncLiveInstancesCore()
        {
            if (!IsPrimaryInstance() || m_InstanceRegister == null)
                return;

            MatDataTransferInstance.CopyLiveInstancesTo(m_LiveInstances);
            ReleaseInactiveRegisteredInstances();
            RegisterLiveInstances();
        }

#if UNITY_INCLUDE_TESTS
        internal void ResetInstanceSyncStats()
        {
            m_TestSyncCallCount = 0;
            m_TestSyncElapsedNanoseconds = 0L;
            m_TestSyncGcAllocatedBytes = 0L;
            m_TestPipelineExecutionCount = 0;
        }

        internal MatDataTransferInstanceSyncStats GetInstanceSyncStats()
        {
            return new MatDataTransferInstanceSyncStats(
                m_TestSyncCallCount,
                m_TestSyncElapsedNanoseconds,
                m_TestSyncGcAllocatedBytes,
                m_LiveInstances.Count,
                m_InstanceRegister != null ? m_InstanceRegister.ActiveCount : 0,
                m_TestPipelineExecutionCount);
        }

        private void RecordTestPipelineExecution()
        {
            m_TestPipelineExecutionCount++;
        }
#endif

        private void ClearInstanceRegister()
        {
            m_InstanceRegister?.Clear();
            m_InstanceRegister = null;
            m_LiveInstances.Clear();
        }

        private int GetSyncedActiveInstanceCount()
        {
            SyncLiveInstances();
            return GetActiveInstanceCount();
        }

        private void EnsureRegistry(int capacity)
        {
            if (m_InstanceRegister == null)
                m_InstanceRegister = new MatDataTransferInstanceRegister(NormalizeCapacity(capacity));
        }

        private int GetActiveInstanceCount()
        {
            if (m_InstanceRegister == null)
                return 0;

            m_InstanceRegister.PruneMissingInstances();
            return m_InstanceRegister.ActiveCount;
        }

        private void ReleaseInactiveRegisteredInstances()
        {
            m_InstanceRegister.ForEach(ReleaseIfNotLive);
        }

        private void ReleaseIfNotLive(MatDataTransferInstance instance)
        {
            if (m_LiveInstances.Contains(instance))
                return;

            if (m_InstanceRegister.Release(instance, out _))
                ClearRequestsForInstance(instance);
        }

        private void RegisterLiveInstances()
        {
            for (int i = 0; i < m_LiveInstances.Count; i++)
                RegisterLiveInstance(m_LiveInstances[i]);
        }

        private void RegisterLiveInstance(MatDataTransferInstance instance)
        {
            if (instance == null || m_InstanceRegister.IsOwner(instance.InstanceId, instance))
                return;

            int previousId = instance.InstanceId;
            if (previousId >= 0)
            {
                ClearQueuedRequests();
                instance.SetRegisteredInstanceId(-1);
            }

            if (!m_InstanceRegister.TryRegister(instance, out _))
                return;

            instance.OnRegisteredByFeature();
        }

        internal bool TrySetMaxInstanceCount(int maxInstanceCount)
        {
            if (!TrySetInstanceCapacity(maxInstanceCount, out int appliedCapacity))
                return false;

            m_MaxInstanceCount = appliedCapacity;
            return true;
        }
    }
}
