using System;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public readonly struct GpuBufferHandle : IEquatable<GpuBufferHandle>
    {
        public static readonly GpuBufferHandle Invalid = new GpuBufferHandle(-1, 0);

        public int Index { get; }
        public int Version { get; }
        public bool IsValid => Index >= 0;

        internal GpuBufferHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public bool Equals(GpuBufferHandle other)
        {
            return Index == other.Index && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is GpuBufferHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ Version;
            }
        }
    }

    public readonly struct GpuBufferSlice
    {
        public int Offset { get; }
        public int Count { get; }

        internal GpuBufferSlice(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }
    }

    public readonly struct GpuBufferWriteContext<T> where T : unmanaged
    {
        private readonly T[] m_Data;
        private readonly int m_Offset;

        public int Count { get; }

        internal GpuBufferWriteContext(T[] data, int offset, int count)
        {
            m_Data = data;
            m_Offset = offset;
            Count = count;
        }

        public void Set(int index, in T value)
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            m_Data[m_Offset + index] = value;
        }

        public void Clear()
        {
            Array.Clear(m_Data, m_Offset, Count);
        }
    }

    public interface IGpuBufferDataSource<T> where T : unmanaged
    {
        bool TryWriteGpuBufferData(GpuBufferWriteContext<T> context);
    }

    public interface IGpuBufferProvider : IDisposable
    {
        string Name { get; }
        void CollectFrameData();
        void UploadAndBind(CommandBuffer commandBuffer);
    }

    public interface IGpuBufferPass : IDisposable
    {
        string Name { get; }
        bool HasWork { get; }
        void PrepareFrame(int frameIndex);
        void Execute(CommandBuffer commandBuffer);
        void Bind(CommandBuffer commandBuffer);
    }
}
