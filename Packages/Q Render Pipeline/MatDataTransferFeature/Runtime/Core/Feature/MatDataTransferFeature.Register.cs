using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private MatDataTransferInstanceRegister m_InstanceRegister;

        #region Registry Lifecycle

        private void InitializeInstanceRegister(int capacity)
        {
            capacity = NormalizeCapacity(capacity);
            m_InstanceRegister = new MatDataTransferInstanceRegister(capacity);
            m_InstanceRegister.InstanceReleased += OnInstanceReleased;
            m_InstanceRegister.RegisteredIdsInvalidated += OnRegisteredIdsInvalidated;
            m_InstanceRegister.Initialize();
        }

        private void ClearInstanceRegister()
        {
            if (m_InstanceRegister == null)
                return;

            m_InstanceRegister.InstanceReleased -= OnInstanceReleased;
            m_InstanceRegister.RegisteredIdsInvalidated -= OnRegisteredIdsInvalidated;
            m_InstanceRegister.Dispose();
            m_InstanceRegister = null;
        }

        private void EnsureRegistry(int capacity)
        {
            if (m_InstanceRegister == null)
                InitializeInstanceRegister(capacity);
        }

        #endregion

        #region Capacity

        internal static int NormalizeCapacity(int capacity)
        {
            return Mathf.Max(MatDataTransferFeature.MinInstanceCount, capacity);
        }

        private bool TryApplyInstanceCapacity(int maxInstanceCount, out int appliedCapacity)
        {
            maxInstanceCount = NormalizeCapacity(maxInstanceCount);
            EnsureRegistry(maxInstanceCount);

            if (!m_InstanceRegister.TrySetCapacity(maxInstanceCount, out bool remapped))
            {
                appliedCapacity = m_InstanceRegister.Capacity;
                return false;
            }

            appliedCapacity = m_InstanceRegister.Capacity;
            if (remapped)
                ClearQueuedRequests();
            return true;
        }

        internal bool TrySetMaxInstanceCount(int maxInstanceCount)
        {
            bool succeeded = TryApplyInstanceCapacity(maxInstanceCount, out int appliedCapacity);
            m_MaxInstanceCount = appliedCapacity;
            return succeeded;
        }

        #endregion

        #region Registry Queries

        internal void GetRegisteredInstances(List<MatDataTransferInstanceRegisterEntry> results)
        {
            if (m_InstanceRegister == null)
            {
                results?.Clear();
                return;
            }

            m_InstanceRegister.CopyEntriesTo(results);
        }

        private int GetActiveInstanceCount()
        {
            return m_InstanceRegister != null ? m_InstanceRegister.ActiveCount : 0;
        }

        #endregion

        #region Registry Events

        private void OnInstanceReleased(MatDataTransferInstance instance)
        {
            ClearRequestsForInstance(instance);
        }

        private void OnRegisteredIdsInvalidated()
        {
            ClearQueuedRequests();
        }

        #endregion
    }
}
