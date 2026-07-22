using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public sealed class GpuBufferProvider<T> : IGpuBufferProvider where T : unmanaged
    {
        private sealed class SourceState
        {
            internal IGpuBufferDataSource<T> Source;
            internal GpuBufferHandle Handle;
        }

        private readonly int m_ElementsPerSource;
        private readonly int m_ShaderPropertyId;
        private readonly GpuBufferSlotAllocator m_Allocator = new GpuBufferSlotAllocator();
        private readonly Dictionary<IGpuBufferDataSource<T>, SourceState> m_SourceStates =
            new Dictionary<IGpuBufferDataSource<T>, SourceState>();
        private readonly List<IGpuBufferDataSource<T>> m_DeadSources =
            new List<IGpuBufferDataSource<T>>();
        private readonly GpuBufferStorage<T> m_Storage;

        private T[] m_Staging;
        private bool m_Dirty;
        private bool m_Disposed;

        public string Name => m_Storage.Name;
        public int ElementsPerSource => m_ElementsPerSource;
        public int SourceCapacity => m_Allocator.Capacity;
        public int SourceCount => m_SourceStates.Count;
        public GraphicsBuffer Buffer => m_Storage.Buffer;

        public GpuBufferProvider(
            string name,
            int shaderPropertyId,
            int elementsPerSource = 1,
            int initialSourceCapacity = 1)
        {
            if (elementsPerSource <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementsPerSource));
            if (initialSourceCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialSourceCapacity));

            m_ElementsPerSource = elementsPerSource;
            m_ShaderPropertyId = shaderPropertyId;
            m_Storage = new GpuBufferStorage<T>(name);
            EnsureCapacity(initialSourceCapacity);
        }

        public GpuBufferHandle Register(IGpuBufferDataSource<T> source)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(source, null))
                throw new ArgumentNullException(nameof(source));
            if (m_SourceStates.TryGetValue(source, out SourceState existing))
                return existing.Handle;

            GpuBufferHandle handle = m_Allocator.Allocate();
            EnsureCapacity(m_Allocator.Capacity);
            m_SourceStates.Add(source, new SourceState
            {
                Source = source,
                Handle = handle,
            });
            ClearSlot(handle.Index);
            return handle;
        }

        public bool Unregister(IGpuBufferDataSource<T> source)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(source, null) || !m_SourceStates.TryGetValue(source, out SourceState state))
                return false;

            ClearSlot(state.Handle.Index);
            m_Allocator.Release(state.Handle);
            m_SourceStates.Remove(source);
            return true;
        }

        public bool TryGetSlice(GpuBufferHandle handle, out GpuBufferSlice slice)
        {
            ThrowIfDisposed();
            if (!m_Allocator.IsAlive(handle))
            {
                slice = default;
                return false;
            }

            slice = new GpuBufferSlice(handle.Index * m_ElementsPerSource, m_ElementsPerSource);
            return true;
        }

        public void ClearInstance(GpuBufferHandle handle)
        {
            ThrowIfDisposed();
            if (m_Allocator.IsAlive(handle))
                ClearSlot(handle.Index);
        }

        public void CollectFrameData()
        {
            ThrowIfDisposed();
            m_DeadSources.Clear();

            foreach (SourceState state in m_SourceStates.Values)
            {
                if (IsDestroyedUnityObject(state.Source))
                {
                    m_DeadSources.Add(state.Source);
                    continue;
                }

                int offset = state.Handle.Index * m_ElementsPerSource;
                var context = new GpuBufferWriteContext<T>(m_Staging, offset, m_ElementsPerSource);
                context.Clear();
                if (!state.Source.TryWriteGpuBufferData(context))
                    context.Clear();
                m_Dirty = true;
            }

            for (int i = 0; i < m_DeadSources.Count; i++)
                Unregister(m_DeadSources[i]);
        }

        public void Upload()
        {
            ThrowIfDisposed();
            if (!m_Dirty)
                return;

            m_Storage.SetData(m_Staging);
            m_Dirty = false;
        }

        public void BindGlobal(CommandBuffer commandBuffer)
        {
            ThrowIfDisposed();
            m_Storage.BindGlobal(commandBuffer, m_ShaderPropertyId);
        }

        public void UploadAndBind(CommandBuffer commandBuffer)
        {
            Upload();
            BindGlobal(commandBuffer);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
            m_SourceStates.Clear();
            m_DeadSources.Clear();
            m_Staging = null;
            m_Storage.Dispose();
        }

        private void EnsureCapacity(int requiredSourceCapacity)
        {
            int requiredElementCapacity = requiredSourceCapacity * m_ElementsPerSource;
            if (m_Staging != null && m_Staging.Length >= requiredElementCapacity)
                return;

            int newElementCapacity = Mathf.NextPowerOfTwo(requiredElementCapacity);
            var expanded = new T[newElementCapacity];
            if (m_Staging != null)
                Array.Copy(m_Staging, expanded, m_Staging.Length);

            m_Staging = expanded;
            m_Storage.EnsureCapacity(newElementCapacity);
            m_Dirty = true;
        }

        private void ClearSlot(int slotIndex)
        {
            Array.Clear(m_Staging, slotIndex * m_ElementsPerSource, m_ElementsPerSource);
            m_Dirty = true;
        }

        private static bool IsDestroyedUnityObject(IGpuBufferDataSource<T> source)
        {
            return source is UnityEngine.Object unityObject && unityObject == null;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(Name);
        }
    }
}
