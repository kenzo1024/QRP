using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer.CharUnifiedShadow
{
    internal sealed class CharUnifiedShadowGpuPass : IGpuBufferPass
    {
        private const string ComputeResourceName = "CharUnifiedShadowGpuSampling";
        private const string ComputeKernelName = "GenerateSamples";

        private static readonly int InputsId = Shader.PropertyToID("_MdtCharUnifiedShadowInputs");
        private static readonly int RangesId = Shader.PropertyToID("_MdtCharUnifiedShadowRanges");
        private static readonly int SamplesId = Shader.PropertyToID("_MdtCharUnifiedShadowSamples");
        private static readonly int InstanceCapacityId =
            Shader.PropertyToID("_MdtCharUnifiedShadowInstanceCapacity");

        private static CharUnifiedShadowGpuPass s_Instance;

#if UNITY_EDITOR
        private readonly struct DebugReadbackTarget
        {
            internal readonly CharUnifiedShadowGpuSource Source;
            internal readonly Renderer Renderer;
            internal readonly GpuBufferSlice SampleSlice;

            internal DebugReadbackTarget(
                CharUnifiedShadowGpuSource source,
                Renderer renderer,
                GpuBufferSlice sampleSlice)
            {
                Source = source;
                Renderer = renderer;
                SampleSlice = sampleSlice;
            }
        }
#endif

        private readonly RendererIndexedGpuBufferProvider<CharUnifiedShadowGpuInput>
            m_InputProvider;
        private readonly GpuBufferStorage<CharUnifiedShadowGpuRange> m_RangeStorage;
        private readonly GpuBufferStorage<Vector4> m_SampleStorage;
        private readonly HashSet<CharUnifiedShadowGpuSource> m_Sources =
            new HashSet<CharUnifiedShadowGpuSource>();
        private readonly List<CharUnifiedShadowGpuSource> m_DeadSources =
            new List<CharUnifiedShadowGpuSource>();

        private readonly ComputeShader m_ComputeShader;
        private readonly int m_ComputeKernel;
        private int m_PreparedFrame = -1;

        public string Name => "Character Unified Shadow GPU";
        public bool HasWork => m_ComputeShader != null && m_Sources.Count > 0;

        private CharUnifiedShadowGpuPass()
        {
            m_InputProvider = new RendererIndexedGpuBufferProvider<CharUnifiedShadowGpuInput>(
                "Character Unified Shadow Inputs",
                InputsId);
            m_RangeStorage = new GpuBufferStorage<CharUnifiedShadowGpuRange>(
                "Character Unified Shadow Ranges");
            m_SampleStorage = new GpuBufferStorage<Vector4>(
                "Character Unified Shadow Samples");
            m_ComputeShader = Resources.Load<ComputeShader>(ComputeResourceName);
            if (m_ComputeShader != null)
                m_ComputeKernel = m_ComputeShader.FindKernel(ComputeKernelName);
            else
                Debug.LogError($"[CharUnifiedShadowGpu] Missing Resources/{ComputeResourceName}.compute.");

            GpuBufferRuntime.Register(this);
        }

        internal static void Register(CharUnifiedShadowGpuSource source)
        {
            if (source == null)
                return;

            s_Instance ??= new CharUnifiedShadowGpuPass();
            s_Instance.RegisterSource(source);
        }

        internal static void Unregister(CharUnifiedShadowGpuSource source)
        {
            if (s_Instance == null || ReferenceEquals(source, null))
                return;

            s_Instance.UnregisterSource(source);
            if (s_Instance.m_Sources.Count == 0)
            {
                s_Instance.Dispose();
                s_Instance = null;
            }
        }

        internal static void RefreshRendererBindings(CharUnifiedShadowGpuSource source)
        {
            if (s_Instance == null || source == null || !s_Instance.m_Sources.Contains(source))
                return;

            RendererGpuBufferIndexRegistry.Sync(source, source.Renderers);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        private void RegisterSource(CharUnifiedShadowGpuSource source)
        {
            if (!m_Sources.Add(source))
            {
                RendererGpuBufferIndexRegistry.Sync(source, source.Renderers);
                return;
            }

            RendererGpuBufferIndexRegistry.Sync(source, source.Renderers);
            source.ForceGpuReset();
            if (EnsureOutputCapacity())
            {
                foreach (CharUnifiedShadowGpuSource registeredSource in m_Sources)
                    registeredSource.ForceGpuReset();
            }
        }

        private void UnregisterSource(CharUnifiedShadowGpuSource source)
        {
            if (!m_Sources.Remove(source))
                return;

            RendererGpuBufferIndexRegistry.Release(source);
        }

        public void PrepareFrame(int frameIndex)
        {
            m_PreparedFrame = frameIndex;
            RemoveDestroyedSources();
            if (m_Sources.Count == 0)
                return;

            foreach (CharUnifiedShadowGpuSource source in m_Sources)
                RendererGpuBufferIndexRegistry.Sync(source, source.Renderers);

            m_InputProvider.BeginFrame();
            if (EnsureOutputCapacity())
            {
                foreach (CharUnifiedShadowGpuSource source in m_Sources)
                    source.ForceGpuReset();
            }

            foreach (CharUnifiedShadowGpuSource source in m_Sources)
                m_InputProvider.SetData(source.Renderers, source);
            m_InputProvider.Upload();
        }

        public void Execute(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
                throw new ArgumentNullException(nameof(commandBuffer));
            if (!HasWork)
                return;

            Dispatch(commandBuffer);
#if UNITY_EDITOR
            QueueDebugReadback(commandBuffer, m_PreparedFrame);
#endif
        }

        public void Bind(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
                throw new ArgumentNullException(nameof(commandBuffer));
            if (!HasWork)
                return;

            commandBuffer.SetGlobalBuffer(RangesId, m_RangeStorage.Buffer);
            commandBuffer.SetGlobalBuffer(SamplesId, m_SampleStorage.Buffer);
            commandBuffer.SetGlobalInt(InstanceCapacityId, m_RangeStorage.Capacity);
        }

        private bool EnsureOutputCapacity()
        {
            int instanceCapacity = Mathf.Max(1, RendererGpuBufferIndexRegistry.Capacity);
            bool resized = m_RangeStorage.EnsureCapacity(instanceCapacity);
            resized |= m_SampleStorage.EnsureCapacity(
                instanceCapacity * CharUnifiedShadowGpuSource.MaxSampleCount);
            return resized;
        }

        private void Dispatch(CommandBuffer commandBuffer)
        {
            int instanceCapacity = m_RangeStorage.Capacity;
            commandBuffer.SetComputeIntParam(m_ComputeShader, InstanceCapacityId, instanceCapacity);
            commandBuffer.SetComputeBufferParam(
                m_ComputeShader, m_ComputeKernel, InputsId, m_InputProvider.Buffer);
            commandBuffer.SetComputeBufferParam(
                m_ComputeShader, m_ComputeKernel, RangesId, m_RangeStorage.Buffer);
            commandBuffer.SetComputeBufferParam(
                m_ComputeShader, m_ComputeKernel, SamplesId, m_SampleStorage.Buffer);
            commandBuffer.DispatchCompute(
                m_ComputeShader,
                m_ComputeKernel,
                Mathf.CeilToInt(instanceCapacity / 64.0f),
                1,
                1);
        }

        private void RemoveDestroyedSources()
        {
            m_DeadSources.Clear();
            foreach (CharUnifiedShadowGpuSource source in m_Sources)
            {
                if (source == null)
                    m_DeadSources.Add(source);
            }

            for (int i = 0; i < m_DeadSources.Count; i++)
                UnregisterSource(m_DeadSources[i]);
        }

#if UNITY_EDITOR
        private void QueueDebugReadback(CommandBuffer commandBuffer, int frame)
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
                return;

            List<DebugReadbackTarget> targets = null;
            foreach (CharUnifiedShadowGpuSource source in m_Sources)
            {
                if (source == null
                    || !source.WantsGpuDebugReadback
                    || source.Renderers == null)
                {
                    continue;
                }

                for (int i = 0; i < source.Renderers.Count; i++)
                {
                    Renderer renderer = source.Renderers[i];
                    if (!RendererGpuBufferIndexRegistry.TryGetSlice(
                            renderer,
                            CharUnifiedShadowGpuSource.MaxSampleCount,
                            out GpuBufferSlice sampleSlice))
                    {
                        continue;
                    }

                    targets ??= new List<DebugReadbackTarget>();
                    targets.Add(new DebugReadbackTarget(source, renderer, sampleSlice));
                    break;
                }
            }

            if (targets == null || targets.Count == 0)
                return;

            commandBuffer.RequestAsyncReadback(
                m_SampleStorage.Buffer,
                request => CompleteDebugReadback(request, targets, frame));
        }

        private void CompleteDebugReadback(
            AsyncGPUReadbackRequest request,
            List<DebugReadbackTarget> targets,
            int frame)
        {
            if (request.hasError)
                return;

            var data = request.GetData<Vector4>();
            for (int i = 0; i < targets.Count; i++)
            {
                DebugReadbackTarget target = targets[i];
                if (target.Source == null
                    || !m_Sources.Contains(target.Source)
                    || target.Renderer == null
                    || !RendererGpuBufferIndexRegistry.TryGetSlice(
                        target.Renderer,
                        CharUnifiedShadowGpuSource.MaxSampleCount,
                        out GpuBufferSlice currentSlice)
                    || !currentSlice.Equals(target.SampleSlice))
                {
                    continue;
                }

                if (target.SampleSlice.Offset < 0
                    || target.SampleSlice.Offset + target.SampleSlice.Count > data.Length)
                {
                    continue;
                }

                var samples = new Vector4[target.SampleSlice.Count];
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                    samples[sampleIndex] = data[target.SampleSlice.Offset + sampleIndex];
                target.Source.SetGpuDebugSamples(
                    samples,
                    target.Source.DebugSampleCount,
                    frame);
            }
        }
#endif

        public void Dispose()
        {
            GpuBufferRuntime.Unregister(this);
            foreach (CharUnifiedShadowGpuSource source in m_Sources)
                RendererGpuBufferIndexRegistry.Release(source);
            m_InputProvider.Dispose();
            m_RangeStorage.Dispose();
            m_SampleStorage.Dispose();
            m_Sources.Clear();
            m_DeadSources.Clear();
        }
    }
}
