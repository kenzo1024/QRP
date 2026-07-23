using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public sealed class RendererIndexedGpuBufferProvider<T> : IDisposable where T : unmanaged
    {
        private readonly int shaderPropertyId;
        private readonly int elementsPerRenderer;
        private readonly GpuBufferStorage<T> storage;
        private T[] staging = new T[1];
        private T[] sourceStaging;

        public GraphicsBuffer Buffer => storage.Buffer;
        public int Capacity => storage.Capacity;
        public int ElementsPerRenderer => elementsPerRenderer;

        public RendererIndexedGpuBufferProvider(
            string name,
            int shaderPropertyId,
            int elementsPerRenderer = 1)
        {
            if (elementsPerRenderer <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementsPerRenderer));

            this.shaderPropertyId = shaderPropertyId;
            this.elementsPerRenderer = elementsPerRenderer;
            storage = new GpuBufferStorage<T>(name);
            sourceStaging = new T[elementsPerRenderer];
        }

        public void BeginFrame()
        {
            int requiredCapacity = Mathf.Max(
                1,
                RendererGpuBufferIndexRegistry.Capacity * elementsPerRenderer);
            if (staging.Length < requiredCapacity)
                staging = new T[Mathf.NextPowerOfTwo(requiredCapacity)];
            else
                Array.Clear(staging, 0, staging.Length);
        }

        public bool SetData(Renderer renderer, in T value)
        {
            if (elementsPerRenderer != 1)
            {
                throw new InvalidOperationException(
                    "Single-value writes require one element per Renderer.");
            }

            if (!TryGetSlice(renderer, out GpuBufferSlice slice))
                return false;

            staging[slice.Offset] = value;
            return true;
        }

        public bool SetData(
            IReadOnlyList<Renderer> renderers,
            IGpuBufferDataSource<T> source)
        {
            if (ReferenceEquals(source, null))
                throw new ArgumentNullException(nameof(source));
            if (!HasRegisteredRenderer(renderers))
                return false;

            var sourceSlice = new GpuBufferSlice(0, elementsPerRenderer);
            var context = new GpuBufferWriteContext<T>(sourceStaging, sourceSlice);
            context.Clear();
            if (!source.TryWriteGpuBufferData(context))
            {
                context.Clear();
                return false;
            }

            bool wroteData = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (!TryGetSlice(renderers[i], out GpuBufferSlice slice))
                    continue;

                Array.Copy(sourceStaging, 0, staging, slice.Offset, slice.Count);
                wroteData = true;
            }

            return wroteData;
        }

        public bool TryGetSlice(Renderer renderer, out GpuBufferSlice slice)
        {
            return RendererGpuBufferIndexRegistry.TryGetSlice(
                renderer,
                elementsPerRenderer,
                out slice);
        }

        public void Upload()
        {
            storage.SetData(staging);
        }

        public void Bind(CommandBuffer commandBuffer)
        {
            storage.BindGlobal(commandBuffer, shaderPropertyId);
        }

        public void Dispose()
        {
            storage.Dispose();
            staging = null;
            sourceStaging = null;
        }

        private bool HasRegisteredRenderer(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null)
                return false;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (TryGetSlice(renderers[i], out _))
                    return true;
            }

            return false;
        }
    }
}
