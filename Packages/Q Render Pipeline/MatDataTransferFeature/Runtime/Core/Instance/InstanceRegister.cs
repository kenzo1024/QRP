using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class InstanceRegister
    {
        private int m_Capacity;
        private readonly Dictionary<int, MatDataTransferInstance> m_InstancesById =
            new Dictionary<int, MatDataTransferInstance>();
        private readonly Dictionary<MatDataTransferInstance, int> m_IdsByInstance =
            new Dictionary<MatDataTransferInstance, int>();
        private readonly List<MatDataTransferInstance> m_IterationInstances =
            new List<MatDataTransferInstance>();
        private readonly List<int> m_PruneIds = new List<int>();

        internal int ActiveCount => m_InstancesById.Count;
        internal int Capacity => m_Capacity;

        internal InstanceRegister(int capacity)
        {
            m_Capacity = Math.Max(0, capacity);
        }

        internal bool TryRegister(MatDataTransferInstance instance, out int id)
        {
            id = -1;
            PruneMissingInstances();

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
            instance.SetRegisteredInstanceId(-1);
            return true;
        }

        internal bool TryGet(int id, out MatDataTransferInstance instance)
        {
            return m_InstancesById.TryGetValue(id, out instance);
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
                return true;

            m_Capacity = capacity;
            if (HasIdsOutsideCapacity())
            {
                CompactActiveIds();
                remapped = true;
            }

            return true;
        }

        internal void CopyEntriesTo(List<InstanceRegisterEntry> results)
        {
            if (results == null)
                return;

            PruneMissingInstances();
            results.Clear();
            foreach (KeyValuePair<int, MatDataTransferInstance> pair in m_InstancesById)
                results.Add(new InstanceRegisterEntry(pair.Key, pair.Value));

            results.Sort(CompareEntriesById);
        }

        internal void ForEach(Action<MatDataTransferInstance> action)
        {
            if (action == null)
                return;

            m_IterationInstances.Clear();
            foreach (MatDataTransferInstance instance in m_InstancesById.Values)
                m_IterationInstances.Add(instance);

            for (int i = 0; i < m_IterationInstances.Count; i++)
            {
                MatDataTransferInstance instance = m_IterationInstances[i];
                if (instance != null)
                    action(instance);
            }
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
        }

        private int FindAvailableId()
        {
            if (ActiveCount >= m_Capacity)
                return -1;

            for (int id = 0; id < m_Capacity; id++)
            {
                if (!m_InstancesById.ContainsKey(id))
                    return id;
            }

            return -1;
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
            List<InstanceRegisterEntry> entries = new List<InstanceRegisterEntry>();
            foreach (KeyValuePair<int, MatDataTransferInstance> pair in m_InstancesById)
            {
                if (pair.Value != null)
                    entries.Add(new InstanceRegisterEntry(pair.Key, pair.Value));
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
        }

        private static int CompareEntriesById(InstanceRegisterEntry left, InstanceRegisterEntry right)
        {
            return left.Id.CompareTo(right.Id);
        }
    }
}
