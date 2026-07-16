using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rendering.MatDataTransfer.PerformanceTests
{
    [TestFixture]
    internal sealed class MatDataTransferBatchModePerformanceTests
    {
        private static readonly SampleGroup FrameMsGroup = new SampleGroup("MDT.Batch.Frame", SampleUnit.Millisecond);
        private static readonly SampleGroup GcBytesGroup = new SampleGroup("MDT.Batch.GCAllocated", SampleUnit.Byte);
        private static readonly SampleGroup SubmitMsGroup = new SampleGroup("MDT.Batch.Submit", SampleUnit.Millisecond);
        private static readonly SampleGroup ResolveMsGroup = new SampleGroup("MDT.Batch.Resolve", SampleUnit.Millisecond);
        private static readonly SampleGroup WriteMsGroup = new SampleGroup("MDT.Batch.Write", SampleUnit.Millisecond);

        private GameObject m_Root;
        private Camera m_Camera;
        private bool m_OwnsCamera;
        private RenderTexture m_RenderTarget;
        private UniversalRenderPipeline.SingleCameraRequest m_RenderRequest;
        private MatDataTransferBatchLoadDriver m_Driver;
        private int m_OriginalCapacity;
        private bool m_ManualPipelineFallback;
        private MethodInfo m_ExecutePipelineMethod;
        private RenderPipelineAsset m_OriginalRenderPipelineAsset;
        private bool m_RenderPipelineWasConfigured;

        private sealed class MeasurementState
        {
            public readonly List<MatDataTransferBatchFrameStats> Samples =
                new List<MatDataTransferBatchFrameStats>();
            public int LogicalFrame;
        }

        [TearDown]
        public void TearDown()
        {
            if (MatDataTransferFeature.Instance != null)
                MatDataTransferFeature.Instance.TrySetMaxInstanceCount(m_OriginalCapacity);

            MatDataTransferLogging.Instance.ApplySettings(new MatDataTransferLoggingSettings());
            MatDataTransferLogging.Instance.ClearTimelineRecords();

            if (m_Root != null)
                UnityEngine.Object.Destroy(m_Root);
            if (m_OwnsCamera && m_Camera != null)
                UnityEngine.Object.Destroy(m_Camera.gameObject);
            if (m_RenderTarget != null)
                UnityEngine.Object.Destroy(m_RenderTarget);

            RestoreRenderPipeline();
        }

        [UnityTest, Performance]
        public IEnumerator Smoke_10Objects()
        {
            yield return RunScenario(MatDataTransferBatchScenario.Smoke());
        }

        [UnityTest, Performance]
        public IEnumerator B00_PhasedBaseline()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B00_PhasedBaseline());
        }

        [UnityTest, Performance]
        public IEnumerator B01_SmallSingleSource()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B01_SmallSingleSource());
        }

        [UnityTest, Performance]
        public IEnumerator B02_SmallConflict()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B02_SmallConflict());
        }

        [UnityTest, Performance]
        public IEnumerator B03_ObjectScale()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B03_ObjectScale());
        }

        [UnityTest, Performance]
        public IEnumerator B04_MainRealLoad()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B04_MainRealLoad());
        }

        [UnityTest, Performance]
        public IEnumerator B05_LargeObjectPressure()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B05_LargeObjectPressure());
        }

        [UnityTest, Performance]
        public IEnumerator B06_ManySourcesPressure()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B06_ManySourcesPressure());
        }

        [UnityTest, Performance]
        public IEnumerator B07_ForInstance()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B07_ForInstance());
        }

        [UnityTest, Performance]
        public IEnumerator B08_NarrowProperties()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B08_NarrowProperties());
        }

        [UnityTest, Performance]
        public IEnumerator B09_Logging()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B09_Logging());
        }

        private IEnumerator RunScenario(MatDataTransferBatchScenario scenario)
        {
            yield return EnsureFeatureReady();
            ConfigureFeature(scenario);
            PrepareDriver(scenario);

            MeasurementState state = new MeasurementState();
            using (MetricRecorders recorders = new MetricRecorders())
            {
                yield return MeasurePhase(
                    scenario,
                    MatDataTransferBatchPhase.UrpBaseline,
                    false,
                    recorders,
                    state);

                m_Driver.CreateRenderObjects();
                yield return null;
                yield return MeasurePhase(
                    scenario,
                    MatDataTransferBatchPhase.RendererBaseline,
                    false,
                    recorders,
                    state);

                m_Driver.AttachInstances();
                yield return WaitForInstancesReady(scenario);
                yield return MeasurePhase(
                    scenario,
                    MatDataTransferBatchPhase.InstanceIdle,
                    false,
                    recorders,
                    state);

                if (scenario.SourceCount > 0)
                {
                    yield return MeasurePhase(
                        scenario,
                        MatDataTransferBatchPhase.Submission,
                        true,
                        recorders,
                        state);
                    m_Driver.AssertMaterialValues(state.LogicalFrame - 1, 16);
                }

                WriteReport(scenario, state.Samples.ToArray(), state.Samples.Count);
            }
        }

        private IEnumerator MeasurePhase(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchPhase phase,
            bool submit,
            MetricRecorders recorders,
            MeasurementState state)
        {
            for (int i = 0; i < scenario.WarmupFrames; i++)
            {
                if (submit)
                    m_Driver.SubmitFrame(state.LogicalFrame);

                RequestCameraRender();
                state.LogicalFrame++;
                yield return null;
                ExecuteManualPipelineIfNeeded();
            }

            for (int i = 0; i < scenario.MeasurementFrames; i++)
            {
                MatDataTransferFeature.Instance.ResetInstanceSyncStats();
                double start = Time.realtimeSinceStartupAsDouble;
                if (submit)
                    m_Driver.SubmitFrame(state.LogicalFrame);

                RequestCameraRender();
                yield return null;
                long manualPipelineNanoseconds = ExecuteManualPipelineIfNeeded();
                MatDataTransferInstanceSyncStats syncStats =
                    MatDataTransferFeature.Instance.GetInstanceSyncStats();

                MatDataTransferBatchFrameStats stats = CollectStats(
                    scenario,
                    phase,
                    PipelineMode,
                    recorders,
                    state.LogicalFrame,
                    (Time.realtimeSinceStartupAsDouble - start) * 1000.0,
                    manualPipelineNanoseconds,
                    syncStats);
                AssertFrameStats(scenario, phase, stats);
                state.Samples.Add(stats);

                if (phase == MatDataTransferBatchPhase.Submission
                    || (scenario.SourceCount == 0 && phase == MatDataTransferBatchPhase.InstanceIdle))
                {
                    Measure.Custom(FrameMsGroup, stats.FrameMilliseconds);
                    Measure.Custom(GcBytesGroup, stats.GcAllocatedBytes);
                    Measure.Custom(SubmitMsGroup, stats.SubmitTotalNanoseconds / 1000000.0);
                    Measure.Custom(ResolveMsGroup, stats.PipelineResolveNanoseconds / 1000000.0);
                    Measure.Custom(WriteMsGroup, stats.PipelineWriteNanoseconds / 1000000.0);
                }

                state.LogicalFrame++;
            }
        }

        private IEnumerator EnsureFeatureReady()
        {
            ConfigureRenderPipeline();
            EnsureCamera();
            for (int i = 0; i < 60; i++)
            {
                if (MatDataTransferFeature.Instance != null)
                    yield break;

                yield return null;
            }

            if (TryBootstrapFeatureFromRendererAsset(AllowManualPipelineFallback()))
            {
                yield return null;
                yield break;
            }

            Assert.Fail(
                "MatDataTransferFeature.Instance was not created by URP. "
                + "The batch test requires the real RenderGraph path. "
                + "Use -mdtAllowManualPipeline only for explicitly labelled diagnostic runs.");
        }

        private void ConfigureRenderPipeline()
        {
#if UNITY_EDITOR
            const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
            RenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelineAssetPath);
            Assert.That(pipelineAsset, Is.Not.Null, "PC RenderPipelineAsset is missing: " + PipelineAssetPath);

            m_OriginalRenderPipelineAsset = QualitySettings.renderPipeline;
            m_RenderPipelineWasConfigured = true;
            QualitySettings.renderPipeline = pipelineAsset;
            Assert.That(
                PrepareRenderPipeline(pipelineAsset),
                Is.True,
                "Unity failed to prepare the PC RenderPipeline.");
#endif
        }

        private void RestoreRenderPipeline()
        {
#if UNITY_EDITOR
            if (!m_RenderPipelineWasConfigured)
                return;

            QualitySettings.renderPipeline = m_OriginalRenderPipelineAsset;
            PrepareRenderPipeline(m_OriginalRenderPipelineAsset);
            m_OriginalRenderPipelineAsset = null;
            m_RenderPipelineWasConfigured = false;
#endif
        }

        private static bool PrepareRenderPipeline(RenderPipelineAsset pipelineAsset)
        {
            MethodInfo prepareMethod = typeof(RenderPipelineManager).GetMethod(
                "TryPrepareRenderPipeline",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(prepareMethod, Is.Not.Null, "Unity RenderPipeline preparation API is unavailable.");
            return (bool)prepareMethod.Invoke(null, new object[] { pipelineAsset });
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
            {
                m_Camera = Camera.main;
                EnsureRenderRequest();
                return;
            }

            GameObject cameraObject = new GameObject("MDT_Batch_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            m_Camera = camera;
            m_OwnsCamera = true;
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 12f, -24f);
            camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            EnsureRenderRequest();
        }

        private void EnsureRenderRequest()
        {
            if (m_RenderTarget == null)
            {
                m_RenderTarget = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32)
                {
                    name = "MDT_Batch_RenderTarget"
                };
                m_RenderTarget.Create();
            }

            if (m_RenderRequest == null)
            {
                m_RenderRequest = new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination = m_RenderTarget
                };
            }
        }

        private void RequestCameraRender()
        {
            Assert.That(m_Camera, Is.Not.Null, "Batch test camera is missing.");
            Assert.That(RenderPipelineManager.currentPipeline, Is.Not.Null, "URP is not active.");
            RenderPipeline.SubmitRenderRequest(m_Camera, m_RenderRequest);
        }

        private void ConfigureFeature(MatDataTransferBatchScenario scenario)
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            Assert.That(feature, Is.Not.Null);

            m_OriginalCapacity = feature.MaxInstanceCount;
            int requestedCapacity = Math.Max(feature.MaxInstanceCount, scenario.ObjectCount);
            Assert.That(feature.TrySetMaxInstanceCount(requestedCapacity), Is.True);

            MatDataTransferLogging.Instance.ApplySettings(new MatDataTransferLoggingSettings
            {
                EnableLogging = scenario.EnableLogging,
                MaxTimelineFrames = Math.Max(1, scenario.MeasurementFrames)
            });
        }

        private MatDataTransferBatchPipelineMode PipelineMode =>
            m_ManualPipelineFallback
                ? MatDataTransferBatchPipelineMode.Manual
                : MatDataTransferBatchPipelineMode.RenderGraph;

        private long ExecuteManualPipelineIfNeeded()
        {
            if (!m_ManualPipelineFallback)
                return 0L;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ExecutePipelineMethod?.Invoke(MatDataTransferFeature.Instance, null);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            return elapsed * 1000000000L / System.Diagnostics.Stopwatch.Frequency;
        }

        private static bool AllowManualPipelineFallback()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-mdtAllowManualPipeline", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool TryBootstrapFeatureFromRendererAsset(bool useManualPipeline)
        {
#if UNITY_EDITOR
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/PC_Renderer.asset");
            for (int i = 0; i < assets.Length; i++)
            {
                MatDataTransferFeature feature = assets[i] as MatDataTransferFeature;
                if (feature == null)
                    continue;

                feature.Create();
                m_ManualPipelineFallback = useManualPipeline;
                if (useManualPipeline)
                {
                    m_ExecutePipelineMethod = typeof(MatDataTransferFeature).GetMethod(
                        "ExecuteRequestPipeline",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                return MatDataTransferFeature.Instance != null
                    && (!useManualPipeline || m_ExecutePipelineMethod != null);
            }
#endif
            return false;
        }

        private void PrepareDriver(MatDataTransferBatchScenario scenario)
        {
            m_Root = new GameObject("MatDataTransferBatchModePerformance");
            m_Driver = m_Root.AddComponent<MatDataTransferBatchLoadDriver>();
            m_Driver.Prepare(scenario);
        }

        private IEnumerator WaitForInstancesReady(MatDataTransferBatchScenario scenario)
        {
            for (int i = 0; i < 60; i++)
            {
                _ = MatDataTransferFeature.Instance.ActiveInstanceCount;
                if (m_Driver.AreAllInstancesReady())
                {
                    Assert.That(MatDataTransferFeature.Instance.ActiveInstanceCount, Is.GreaterThanOrEqualTo(scenario.ObjectCount));
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("MatDataTransfer instances did not become ready.");
        }

        private static MatDataTransferBatchFrameStats CollectStats(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchPhase phase,
            MatDataTransferBatchPipelineMode pipelineMode,
            MetricRecorders recorders,
            int logicalFrame,
            double frameMilliseconds,
            long manualPipelineNanoseconds,
            MatDataTransferInstanceSyncStats syncStats)
        {
            IReadOnlyList<ParamWriteResult> receipts = MatDataTransferLogging.Instance.LastReceipts;
            MatDataTransferBatchFrameStats stats = new MatDataTransferBatchFrameStats
            {
                Phase = phase,
                PipelineMode = pipelineMode,
                FrameIndex = logicalFrame,
                FrameMilliseconds = frameMilliseconds,
                GcAllocatedBytes = recorders.GcAllocatedBytes,
                ManagedHeapBytes = recorders.ManagedHeapBytes,
                ActiveInstanceCount = syncStats.RegisteredInstanceCount,
                PayloadCount = receipts.Count,
                GroupCount = phase == MatDataTransferBatchPhase.Submission
                    ? scenario.ExpectedGroupCount
                    : 0,
                CommandCount = 0,
                TraceCount = scenario.EnableLogging ? MatDataTransferLogging.Instance.TimelineRecords.Count : 0,
                TimelineRecordCount = scenario.EnableLogging ? MatDataTransferLogging.Instance.TimelineRecords.Count : 0,
                SubmitTotalNanoseconds = recorders.SubmitTotalNanoseconds,
                SubmitValidateNanoseconds = recorders.SubmitValidateNanoseconds,
                PassSyncInstancesNanoseconds = recorders.PassSyncInstancesNanoseconds,
                PassPipelineNanoseconds = recorders.PassPipelineNanoseconds != 0
                    ? recorders.PassPipelineNanoseconds
                    : manualPipelineNanoseconds,
                PipelineDrainProvidersNanoseconds = recorders.PipelineDrainProvidersNanoseconds,
                PipelineResolveNanoseconds = recorders.PipelineResolveNanoseconds,
                PipelineResolveTargetNanoseconds = recorders.PipelineResolveTargetNanoseconds,
                PipelineResolveConflictNanoseconds = recorders.PipelineResolveConflictNanoseconds,
                PipelineWriteNanoseconds = recorders.PipelineWriteNanoseconds,
                PipelineWriteResolveMaterialNanoseconds = recorders.PipelineWriteResolveMaterialNanoseconds,
                PipelineWriteSetValueNanoseconds = recorders.PipelineWriteSetValueNanoseconds,
                LoggingCaptureNanoseconds = recorders.LoggingCaptureNanoseconds,
                LoggingCommitTimelineNanoseconds = recorders.LoggingCommitTimelineNanoseconds,
                SyncCallCount = syncStats.CallCount,
                SyncElapsedNanoseconds = syncStats.ElapsedNanoseconds,
                SyncGcAllocatedBytes = syncStats.GcAllocatedBytes,
                LiveInstanceCount = syncStats.LiveInstanceCount,
                PipelineExecutionCount = syncStats.PipelineExecutionCount
            };

            for (int i = 0; i < receipts.Count; i++)
            {
                switch (receipts[i].Status)
                {
                    case ParamWriteStatus.Applied:
                        stats.AppliedCount++;
                        stats.CommandCount++;
                        break;
                    case ParamWriteStatus.Overridden:
                        stats.OverriddenCount++;
                        break;
                    case ParamWriteStatus.Rejected:
                        stats.RejectedCount++;
                        break;
                    case ParamWriteStatus.WriterFailed:
                        stats.WriterFailedCount++;
                        break;
                }
            }

            return stats;
        }

        private static void AssertFrameStats(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchPhase phase,
            MatDataTransferBatchFrameStats stats)
        {
            bool isSubmission = phase == MatDataTransferBatchPhase.Submission;
            int expectedInstances = phase == MatDataTransferBatchPhase.UrpBaseline
                || phase == MatDataTransferBatchPhase.RendererBaseline
                    ? 0
                    : scenario.ObjectCount;

            Assert.That(stats.ActiveInstanceCount, Is.EqualTo(expectedInstances));
            Assert.That(stats.PayloadCount, Is.EqualTo(isSubmission ? scenario.ExpectedPayloadCount : 0), scenario.TestName + " payload count mismatch.");
            Assert.That(stats.GroupCount, Is.EqualTo(isSubmission ? scenario.ExpectedGroupCount : 0), scenario.TestName + " group count mismatch.");
            Assert.That(stats.CommandCount, Is.EqualTo(isSubmission ? scenario.ExpectedCommandCount : 0), scenario.TestName + " command count mismatch.");
            Assert.That(stats.AppliedCount, Is.EqualTo(isSubmission ? scenario.ExpectedAppliedCount : 0), scenario.TestName + " applied count mismatch.");
            Assert.That(stats.OverriddenCount, Is.EqualTo(isSubmission ? scenario.ExpectedOverriddenCount : 0), scenario.TestName + " overridden count mismatch.");
            Assert.That(stats.RejectedCount, Is.EqualTo(0), scenario.TestName + " has rejected requests.");
            Assert.That(stats.WriterFailedCount, Is.EqualTo(0), scenario.TestName + " has writer failures.");

            if (phase == MatDataTransferBatchPhase.InstanceIdle || isSubmission)
            {
                Assert.That(stats.SyncCallCount, Is.GreaterThan(0), scenario.TestName + " did not execute instance sync.");
                Assert.That(stats.PipelineExecutionCount, Is.GreaterThan(0), scenario.TestName + " did not execute the request pipeline.");
            }
        }

        private static void WriteReport(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            MatDataTransferBatchReportWriter writer = new MatDataTransferBatchReportWriter(
                MatDataTransferBatchReportWriter.ResolveOutputRootFromCommandLine(),
                MatDataTransferBatchReportWriter.ResolveRunStampFromCommandLine());
            writer.Write(scenario, samples, sampleCount);
        }

        private sealed class MetricRecorders : IDisposable
        {
            private readonly ProfilerRecorder m_GcAllocated;
            private readonly ProfilerRecorder m_ManagedHeap;
            private readonly ProfilerRecorder m_SubmitTotal;
            private readonly ProfilerRecorder m_SubmitValidate;
            private readonly ProfilerRecorder m_PassSyncInstances;
            private readonly ProfilerRecorder m_PassPipeline;
            private readonly ProfilerRecorder m_PipelineDrainProviders;
            private readonly ProfilerRecorder m_PipelineResolve;
            private readonly ProfilerRecorder m_PipelineResolveTarget;
            private readonly ProfilerRecorder m_PipelineResolveConflict;
            private readonly ProfilerRecorder m_PipelineWrite;
            private readonly ProfilerRecorder m_PipelineWriteResolveMaterial;
            private readonly ProfilerRecorder m_PipelineWriteSetValue;
            private readonly ProfilerRecorder m_LoggingCapture;
            private readonly ProfilerRecorder m_LoggingCommitTimeline;

            public MetricRecorders()
            {
                m_GcAllocated = Start(ProfilerCategory.Memory, "GC Allocated In Frame");
                m_ManagedHeap = Start(ProfilerCategory.Memory, "Managed Heap Used Size");
                m_SubmitTotal = Start(ProfilerCategory.Scripts, "MDT.Submit.Total");
                m_SubmitValidate = Start(ProfilerCategory.Scripts, "MDT.Submit.Validate");
                m_PassSyncInstances = Start(ProfilerCategory.Scripts, "MDT.Pass.SyncInstances");
                m_PassPipeline = Start(ProfilerCategory.Scripts, "MDT.Pass.Pipeline");
                m_PipelineDrainProviders = Start(ProfilerCategory.Scripts, "MDT.Pipeline.DrainProviders");
                m_PipelineResolve = Start(ProfilerCategory.Scripts, "MDT.Pipeline.Resolve");
                m_PipelineResolveTarget = Start(ProfilerCategory.Scripts, "MDT.Pipeline.ResolveTarget");
                m_PipelineResolveConflict = Start(ProfilerCategory.Scripts, "MDT.Pipeline.ResolveConflict");
                m_PipelineWrite = Start(ProfilerCategory.Scripts, "MDT.Pipeline.Write");
                m_PipelineWriteResolveMaterial = Start(ProfilerCategory.Scripts, "MDT.Pipeline.Write.ResolveMaterial");
                m_PipelineWriteSetValue = Start(ProfilerCategory.Scripts, "MDT.Pipeline.Write.SetValue");
                m_LoggingCapture = Start(ProfilerCategory.Scripts, "MDT.Logging.Capture");
                m_LoggingCommitTimeline = Start(ProfilerCategory.Scripts, "MDT.Logging.CommitTimeline");
            }

            public long GcAllocatedBytes => LastValue(m_GcAllocated);
            public long ManagedHeapBytes => LastValue(m_ManagedHeap);
            public long SubmitTotalNanoseconds => LastValue(m_SubmitTotal);
            public long SubmitValidateNanoseconds => LastValue(m_SubmitValidate);
            public long PassSyncInstancesNanoseconds => LastValue(m_PassSyncInstances);
            public long PassPipelineNanoseconds => LastValue(m_PassPipeline);
            public long PipelineDrainProvidersNanoseconds => LastValue(m_PipelineDrainProviders);
            public long PipelineResolveNanoseconds => LastValue(m_PipelineResolve);
            public long PipelineResolveTargetNanoseconds => LastValue(m_PipelineResolveTarget);
            public long PipelineResolveConflictNanoseconds => LastValue(m_PipelineResolveConflict);
            public long PipelineWriteNanoseconds => LastValue(m_PipelineWrite);
            public long PipelineWriteResolveMaterialNanoseconds => LastValue(m_PipelineWriteResolveMaterial);
            public long PipelineWriteSetValueNanoseconds => LastValue(m_PipelineWriteSetValue);
            public long LoggingCaptureNanoseconds => LastValue(m_LoggingCapture);
            public long LoggingCommitTimelineNanoseconds => LastValue(m_LoggingCommitTimeline);

            public void Dispose()
            {
                DisposeRecorder(m_GcAllocated);
                DisposeRecorder(m_ManagedHeap);
                DisposeRecorder(m_SubmitTotal);
                DisposeRecorder(m_SubmitValidate);
                DisposeRecorder(m_PassSyncInstances);
                DisposeRecorder(m_PassPipeline);
                DisposeRecorder(m_PipelineDrainProviders);
                DisposeRecorder(m_PipelineResolve);
                DisposeRecorder(m_PipelineResolveTarget);
                DisposeRecorder(m_PipelineResolveConflict);
                DisposeRecorder(m_PipelineWrite);
                DisposeRecorder(m_PipelineWriteResolveMaterial);
                DisposeRecorder(m_PipelineWriteSetValue);
                DisposeRecorder(m_LoggingCapture);
                DisposeRecorder(m_LoggingCommitTimeline);
            }

            private static ProfilerRecorder Start(ProfilerCategory category, string statName)
            {
                try
                {
                    return ProfilerRecorder.StartNew(category, statName, 1);
                }
                catch
                {
                    return default;
                }
            }

            private static long LastValue(ProfilerRecorder recorder)
            {
                return recorder.Valid ? recorder.LastValue : 0L;
            }

            private static void DisposeRecorder(ProfilerRecorder recorder)
            {
                if (recorder.Valid)
                    recorder.Dispose();
            }
        }
    }
}
