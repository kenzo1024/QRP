using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MatDataTransferInstanceRegister
    {
        private int m_Capacity;
        private int m_NextAvailableId;
        private readonly Dictionary<int, MatDataTransferInstance> m_InstancesById;
        private readonly Dictionary<MatDataTransferInstance, int> m_IdsByInstance;
        private readonly List<int> m_PruneIds;
        private bool m_IsInitialized;
        private bool m_IsFillingAvailableSlots;

        internal event Action<MatDataTransferInstance> InstanceReleased;
        internal event Action RegisteredIdsInvalidated;

        internal int ActiveCount => m_InstancesById.Count;
        internal int Capacity => m_Capacity;

        internal MatDataTransferInstanceRegister(int capacity)
        {
            m_Capacity = Math.Max(0, capacity);
            m_InstancesById = new Dictionary<int, MatDataTransferInstance>(m_Capacity);
            m_IdsByInstance = new Dictionary<MatDataTransferInstance, int>(m_Capacity);
            m_PruneIds = new List<int>(m_Capacity);
        }

        internal void Initialize()
        {
            if (m_IsInitialized)
                return;

            MatDataTransferInstance.LiveInstanceEnabled += OnLiveInstanceEnabled;
            MatDataTransferInstance.LiveInstanceDisabled += OnLiveInstanceDisabled;
            m_IsInitialized = true;
            FillAvailableSlots();
        }

        internal void Dispose()
        {
            if (m_IsInitialized)
            {
                MatDataTransferInstance.LiveInstanceEnabled -= OnLiveInstanceEnabled;
                MatDataTransferInstance.LiveInstanceDisabled -= OnLiveInstanceDisabled;
                m_IsInitialized = false;
            }

            Clear();
        }

        internal bool TryRegister(MatDataTransferInstance instance, out int id)
        {
            id = -1;

            if (instance == null)
                return false;

            if (m_IdsByInstance.TryGetValue(instance, out id))
            {
                instance.SetRegisteredInstanceId(id);
                return true;
            }

            id = FindAvailableId();
            if (id < 0)
                return false;

            m_InstancesById.Add(id, instance);
            m_IdsByInstance.Add(instance, id);
            instance.SetRegisteredInstanceId(id);
            return true;
        }

        internal bool Release(MatDataTransferInstance instance, out int releasedId)
        {
            releasedId = -1;
            if (instance == null)
                return false;

            if (!m_IdsByInstance.TryGetValue(instance, out releasedId))
            {
                instance.SetRegisteredInstanceId(-1);
                return false;
            }

            m_IdsByInstance.Remove(instance);
            m_InstancesById.Remove(releasedId);
            m_NextAvailableId = Math.Min(m_NextAvailableId, releasedId);
            instance.SetRegisteredInstanceId(-1);
            return true;
        }

        internal bool TryGet(int id, out MatDataTransferInstance instance)
        {
            if (!m_InstancesById.TryGetValue(id, out instance))
                return false;

            if (instance != null)
                return true;

            m_IdsByInstance.Remove(instance);
            m_InstancesById.Remove(id);
            m_NextAvailableId = Math.Min(m_NextAvailableId, id);
            instance = null;
            FillAvailableSlots();
            return false;
        }

        internal bool TryGetId(MatDataTransferInstance instance, out int id)
        {
            if (instance == null)
            {
                id = -1;
                return false;
            }

            return m_IdsByInstance.TryGetValue(instance, out id);
        }

        internal bool IsOwner(int id, MatDataTransferInstance instance)
        {
            return instance != null
                && m_InstancesById.TryGetValue(id, out MatDataTransferInstance owner)
                && ReferenceEquals(owner, instance);
        }

        internal bool TrySetCapacity(int capacity, out bool remapped)
        {
            remapped = false;
            PruneMissingInstances();

            capacity = Math.Max(0, capacity);
            if (capacity < ActiveCount)
                return false;

            if (capacity == m_Capacity)
            {
                FillAvailableSlots();
                return true;
            }

            EnsureStorageCapacity(capacity);
            m_Capacity = capacity;
            if (HasIdsOutsideCapacity())
            {
                CompactActiveIds();
                remapped = true;
            }
            else
            {
                m_NextAvailableId = Math.Min(m_NextAvailableId, m_Capacity);
            }

            FillAvailableSlots();
            return true;
        }

        internal void CopyEntriesTo(List<MatDataTransferInstanceRegisterEntry> results)
        {
            if (results == null)
                return;

            PruneMissingInstances();
            FillAvailableSlots();
            results.Clear();
            foreach (KeyValuePair<int, MatDataTransferInstance> pair in m_InstancesById)
                results.Add(new MatDataTransferInstanceRegisterEntry(pair.Key, pair.Value));

            results.Sort(CompareEntriesById);
        }

        internal int PruneMissingInstances()
        {
            m_PruneIds.Clear();
            foreach (KeyValuePair<int, MatDataTransferInstance> pair in m_InstancesById)
            {
                if (pair.Value == null)
                    m_PruneIds.Add(pair.Key);
            }

            if (m_PruneIds.Count == 0)
                return 0;

            for (int i = 0; i < m_PruneIds.Count; i++)
            {
                int id = m_PruneIds[i];
                if (m_InstancesById.TryGetValue(id, out MatDataTransferInstance instance))
                    m_IdsByInstance.Remove(instance);
                m_InstancesById.Remove(id);
                m_NextAvailableId = Math.Min(m_NextAvailableId, id);
            }

            return m_PruneIds.Count;
        }

        internal void Clear()
        {
            foreach (MatDataTransferInstance instance in m_InstancesById.Values)
            {
                if (instance != null)
                    instance.SetRegisteredInstanceId(-1);
            }

            m_InstancesById.Clear();
            m_IdsByInstance.Clear();
            m_NextAvailableId = 0;
        }

        private void OnLiveInstanceEnabled(MatDataTransferInstance instance)
        {
            RegisterLiveInstance(instance);
        }

        private void OnLiveInstanceDisabled(MatDataTransferInstance instance)
        {
            if (Release(instance, out _))
                InstanceReleased?.Invoke(instance);

            FillAvailableSlots();
        }

        private void FillAvailableSlots()
        {
            if (!m_IsInitialized || m_IsFillingAvailableSlots)
                return;

            m_IsFillingAvailableSlots = true;
            try
            {
                PruneMissingInstances();
                if (ActiveCount >= m_Capacity)
                    return;

                IReadOnlyList<MatDataTransferInstance> liveInstances = MatDataTransferInstance.LiveInstances;
                for (int i = 0; i < liveInstances.Count && ActiveCount < m_Capacity; i++)
                    RegisterLiveInstance(liveInstances[i]);
            }
            finally
            {
                m_IsFillingAvailableSlots = false;
            }
        }

        private bool RegisterLiveInstance(MatDataTransferInstance instance)
        {
            if (instance == null || !instance.isActiveAndEnabled)
                return false;

            if (IsOwner(instance.InstanceId, instance))
                return true;

            if (instance.InstanceId >= 0)
            {
                RegisteredIdsInvalidated?.Invoke();
                instance.SetRegisteredInstanceId(-1);
            }

            if (!TryRegisterWithRecovery(instance))
                return false;

            instance.OnRegistered();
            return true;
        }

        private bool TryRegisterWithRecovery(MatDataTransferInstance instance)
        {
            if (TryRegister(instance, out _))
                return true;

            return PruneMissingInstances() > 0 && TryRegister(instance, out _);
        }

        private int FindAvailableId()
        {
            if (ActiveCount >= m_Capacity)
                return -1;

            int startId = Math.Min(m_NextAvailableId, m_Capacity);
            for (int id = startId; id < m_Capacity; id++)
            {
                if (m_InstancesById.ContainsKey(id))
                    continue;

                m_NextAvailableId = id + 1;
                return id;
            }

            // Safety fallback for data restored from an older registry state.
            for (int id = 0; id < startId; id++)
            {
                if (!m_InstancesById.ContainsKey(id))
                {
                    m_NextAvailableId = id + 1;
                    return id;
                }
            }

            return -1;
        }

        private void EnsureStorageCapacity(int capacity)
        {
            m_InstancesById.EnsureCapacity(capacity);
            m_IdsByInstance.EnsureCapacity(capacity);
            if (m_PruneIds.Capacity < capacity)
                m_PruneIds.Capacity = capacity;
        }

        private bool HasIdsOutsideCapacity()
        {
            foreach (int id in m_InstancesById.Keys)
            {
                if (id >= m_Capacity)
                    return true;
            }

            return false;
        }

        private void CompactActiveIds()
        {
            List<MatDataTransferInstanceRegisterEntry> entries = new List<MatDataTransferInstanceRegisterEntry>();
            foreach (KeyValuePair<int, MatDataTransferInstance> pair in m_InstancesById)
            {
                if (pair.Value != null)
                    entries.Add(new MatDataTransferInstanceRegisterEntry(pair.Key, pair.Value));
            }

            entries.Sort(CompareEntriesById);
            m_InstancesById.Clear();
            m_IdsByInstance.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                MatDataTransferInstance instance = entries[i].Instance;
                m_InstancesById.Add(i, instance);
                m_IdsByInstance.Add(instance, i);
                instance.SetRegisteredInstanceId(i);
            }

            m_NextAvailableId = entries.Count;
        }

        private static int CompareEntriesById(MatDataTransferInstanceRegisterEntry left, MatDataTransferInstanceRegisterEntry right)
        {
            return left.Id.CompareTo(right.Id);
        }
    }
}
