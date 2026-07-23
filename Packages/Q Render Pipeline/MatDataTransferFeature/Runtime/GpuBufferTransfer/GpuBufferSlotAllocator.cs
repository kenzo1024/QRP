using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    internal sealed class GpuBufferSlotAllocator
    {
        private readonly List<int> m_Versions = new List<int>();
        private readonly Stack<int> m_FreeIndices = new Stack<int>();

        internal int Capacity => m_Versions.Count;

        internal GpuBufferHandle Allocate()
        {
            if (m_FreeIndices.Count > 0)
            {
                int index = m_FreeIndices.Pop();
                return new GpuBufferHandle(index, m_Versions[index]);
            }

            int newIndex = m_Versions.Count;
            m_Versions.Add(1);
            return new GpuBufferHandle(newIndex, 1);
        }

        internal bool Release(GpuBufferHandle handle)
        {
            if (!IsAlive(handle))
                return false;

            m_Versions[handle.Index]++;
            m_FreeIndices.Push(handle.Index);
            return true;
        }

        internal bool IsAlive(GpuBufferHandle handle)
        {
            return handle.IsValid
                && handle.Index < m_Versions.Count
                && m_Versions[handle.Index] == handle.Version;
        }

        internal void Clear()
        {
            m_Versions.Clear();
            m_FreeIndices.Clear();
        }
    }
}
