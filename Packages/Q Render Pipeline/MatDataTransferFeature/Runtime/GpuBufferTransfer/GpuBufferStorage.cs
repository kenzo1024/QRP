using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public sealed class GpuBufferStorage<T> : IDisposable where T : unmanaged
    {
        private const int MinimumCapacity = 1;
        private const int MaximumStructuredStride = 2048;

        private readonly string m_Name;
        private GraphicsBuffer m_Buffer;

        public string Name => m_Name;
        public GraphicsBuffer Buffer => m_Buffer;
        public int Capacity => m_Buffer?.count ?? 0;

        public GpuBufferStorage(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Buffer name cannot be empty.", nameof(name));

            int stride = Marshal.SizeOf<T>();
            if ((stride & 3) != 0 || stride >= MaximumStructuredStride)
            {
                throw new ArgumentException(
                    $"{typeof(T).Name} has an invalid StructuredBuffer stride: {stride} bytes.");
            }

            m_Name = name;
        }

        public bool EnsureCapacity(int requiredCapacity)
        {
            requiredCapacity = Mathf.Max(MinimumCapacity, requiredCapacity);
            if (m_Buffer != null && m_Buffer.count >= requiredCapacity)
                return false;

            int newCapacity = Mathf.NextPowerOfTwo(requiredCapacity);
            m_Buffer?.Dispose();
            m_Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                newCapacity,
                Marshal.SizeOf<T>());
            m_Buffer.name = m_Name;
            return true;
        }

        public void SetData(T[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            EnsureCapacity(data.Length);
            m_Buffer.SetData(data);
        }

        public void BindGlobal(CommandBuffer commandBuffer, int shaderPropertyId)
        {
            if (commandBuffer == null)
                throw new ArgumentNullException(nameof(commandBuffer));
            if (m_Buffer == null)
                EnsureCapacity(MinimumCapacity);

            commandBuffer.SetGlobalBuffer(shaderPropertyId, m_Buffer);
        }

        public void Dispose()
        {
            m_Buffer?.Dispose();
            m_Buffer = null;
        }
    }
}
