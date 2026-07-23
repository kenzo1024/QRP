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

    public readonly struct GpuBufferSlice : IEquatable<GpuBufferSlice>
    {
        public int Offset { get; }
        public int Count { get; }

        internal GpuBufferSlice(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }

        internal static GpuBufferSlice FromIndex(int index, int elementsPerIndex)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (elementsPerIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementsPerIndex));

            return new GpuBufferSlice(checked(index * elementsPerIndex), elementsPerIndex);
        }

        public GpuBufferSlice Scale(int factor)
        {
            if (factor <= 0)
                throw new ArgumentOutOfRangeException(nameof(factor));

            return new GpuBufferSlice(checked(Offset * factor), checked(Count * factor));
        }

        public bool Equals(GpuBufferSlice other)
        {
            return Offset == other.Offset && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is GpuBufferSlice other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Offset * 397) ^ Count;
            }
        }
    }

    public readonly struct GpuBufferWriteContext<T> where T : unmanaged
    {
        private readonly T[] m_Data;
        private readonly GpuBufferSlice m_Slice;

        public int Count => m_Slice.Count;

        internal GpuBufferWriteContext(T[] data, int offset, int count)
            : this(data, new GpuBufferSlice(offset, count))
        {
        }

        internal GpuBufferWriteContext(T[] data, GpuBufferSlice slice)
        {
            m_Data = data;
            m_Slice = slice;
        }

        public void Set(int index, in T value)
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            m_Data[m_Slice.Offset + index] = value;
        }

        public void Clear()
        {
            Array.Clear(m_Data, m_Slice.Offset, m_Slice.Count);
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
