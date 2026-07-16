using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
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
        private MatDataTransferBatchLoadDriver m_Driver;
        private int m_OriginalCapacity;
        private bool m_ManualPipelineFallback;
        private MethodInfo m_ExecutePipelineMethod;

        [TearDown]
        public void TearDown()
        {
            if (MatDataTransferFeature.Instance != null)
                MatDataTransferFeature.Instance.TrySetMaxInstanceCount(m_OriginalCapacity);

            MatDataTransferLogging.Instance.ApplySettings(new MatDataTransferLoggingSettings());
            MatDataTransferLogging.Instance.ClearTimelineRecords();

            if (m_Root != null)
                UnityEngine.Object.Destroy(m_Root);
            if (m_Camera != null)
                UnityEngine.Object.Destroy(m_Camera.gameObject);
        }

        [UnityTest, Performance]
        public IEnumerator Smoke_10Objects()
        {
            yield return RunScenario(MatDataTransferBatchScenario.Smoke());
        }

        [UnityTest, Performance]
        public IEnumerator B00_EmptyDriver()
        {
            yield return RunScenario(MatDataTransferBatchScenario.B00_EmptyDriver());
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
            BuildDriver(scenario);
            yield return WaitForInstancesReady(scenario);

            using (MetricRecorders recorders = new MetricRecorders())
            {
                int logicalFrame = 0;
                for (int i = 0; i < scenario.WarmupFrames; i++)
                {
                    m_Driver.SubmitFrame(logicalFrame++);
                    yield return null;
                    ExecuteManualPipelineIfNeeded(scenario);
                }

                MatDataTransferBatchFrameStats[] samples =
                    new MatDataTransferBatchFrameStats[scenario.MeasurementFrames];

                for (int i = 0; i < scenario.MeasurementFrames; i++)
                {
                    double start = Time.realtimeSinceStartupAsDouble;
                    m_Driver.SubmitFrame(logicalFrame);
                    yield return null;
                    long manualPipelineNanoseconds = ExecuteManualPipelineIfNeeded(scenario);

                    MatDataTransferBatchFrameStats stats = CollectStats(
                        scenario,
                        recorders,
                        logicalFrame,
                        (Time.realtimeSinceStartupAsDouble - start) * 1000.0,
                        manualPipelineNanoseconds);
                    AssertFrameStats(scenario, stats);
                    samples[i] = stats;

                    Measure.Custom(FrameMsGroup, stats.FrameMilliseconds);
                    Measure.Custom(GcBytesGroup, stats.GcAllocatedBytes);
                    Measure.Custom(SubmitMsGroup, stats.SubmitTotalNanoseconds / 1000000.0);
                    Measure.Custom(ResolveMsGroup, stats.PipelineResolveNanoseconds / 1000000.0);
                    Measure.Custom(WriteMsGroup, stats.PipelineWriteNanoseconds / 1000000.0);

                    logicalFrame++;
                }

                m_Driver.AssertMaterialValues(logicalFrame - 1, 16);
                WriteReport(scenario, samples, samples.Length);
            }
        }

        private IEnumerator EnsureFeatureReady()
        {
            EnsureCamera();
            for (int i = 0; i < 60; i++)
            {
                if (MatDataTransferFeature.Instance != null)
                    yield break;

                yield return null;
            }

            if (TryBootstrapFeatureFromRendererAsset())
            {
                yield return null;
                yield break;
            }

            Assert.Fail("MatDataTransferFeature.Instance is missing. Check PC_Renderer asset and URP settings.");
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
                return;

            GameObject cameraObject = new GameObject("MDT_Batch_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            m_Camera = camera;
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 12f, -24f);
            camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
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

        private long ExecuteManualPipelineIfNeeded(MatDataTransferBatchScenario scenario)
        {
            if (!m_ManualPipelineFallback || scenario.ExpectedPayloadCount == 0)
                return 0L;
            if (MatDataTransferLogging.Instance.LastReceipts.Count == scenario.ExpectedPayloadCount)
                return 0L;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ExecutePipelineMethod?.Invoke(MatDataTransferFeature.Instance, null);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            return elapsed * 1000000000L / System.Diagnostics.Stopwatch.Frequency;
        }

        private bool TryBootstrapFeatureFromRendererAsset()
        {
#if UNITY_EDITOR
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/PC_Renderer.asset");
            for (int i = 0; i < assets.Length; i++)
            {
                MatDataTransferFeature feature = assets[i] as MatDataTransferFeature;
                if (feature == null)
                    continue;

                feature.Create();
                m_ManualPipelineFallback = true;
                m_ExecutePipelineMethod = typeof(MatDataTransferFeature).GetMethod(
                    "ExecuteRequestPipeline",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return MatDataTransferFeature.Instance != null && m_ExecutePipelineMethod != null;
            }
#endif
            return false;
        }

        private void BuildDriver(MatDataTransferBatchScenario scenario)
        {
            m_Root = new GameObject("MatDataTransferBatchModePerformance");
            m_Driver = m_Root.AddComponent<MatDataTransferBatchLoadDriver>();
            m_Driver.Build(scenario);
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
            MetricRecorders recorders,
            int logicalFrame,
            double frameMilliseconds,
            long manualPipelineNanoseconds)
        {
            IReadOnlyList<ParamWriteResult> receipts = MatDataTransferLogging.Instance.LastReceipts;
            MatDataTransferBatchFrameStats stats = new MatDataTransferBatchFrameStats
            {
                FrameIndex = logicalFrame,
                FrameMilliseconds = frameMilliseconds,
                GcAllocatedBytes = recorders.GcAllocatedBytes,
                ManagedHeapBytes = recorders.ManagedHeapBytes,
                ActiveInstanceCount = MatDataTransferFeature.Instance.ActiveInstanceCount,
                PayloadCount = receipts.Count,
                GroupCount = scenario.SourceCount > 0 ? scenario.ExpectedGroupCount : 0,
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
                PipelineResolveNanoseconds = recorders.PipelineResolveNanoseconds != 0
                    ? recorders.PipelineResolveNanoseconds
                    : manualPipelineNanoseconds,
                PipelineResolveTargetNanoseconds = recorders.PipelineResolveTargetNanoseconds,
                PipelineResolveConflictNanoseconds = recorders.PipelineResolveConflictNanoseconds,
                PipelineWriteNanoseconds = recorders.PipelineWriteNanoseconds,
                PipelineWriteResolveMaterialNanoseconds = recorders.PipelineWriteResolveMaterialNanoseconds,
                PipelineWriteSetValueNanoseconds = recorders.PipelineWriteSetValueNanoseconds,
                LoggingCaptureNanoseconds = recorders.LoggingCaptureNanoseconds,
                LoggingCommitTimelineNanoseconds = recorders.LoggingCommitTimelineNanoseconds
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
            MatDataTransferBatchFrameStats stats)
        {
            Assert.That(stats.ActiveInstanceCount, Is.GreaterThanOrEqualTo(scenario.ObjectCount));
            Assert.That(stats.PayloadCount, Is.EqualTo(scenario.ExpectedPayloadCount), scenario.TestName + " payload count mismatch.");
            Assert.That(stats.GroupCount, Is.EqualTo(scenario.SourceCount > 0 ? scenario.ExpectedGroupCount : 0), scenario.TestName + " group count mismatch.");
            Assert.That(stats.CommandCount, Is.EqualTo(scenario.ExpectedCommandCount), scenario.TestName + " command count mismatch.");
            Assert.That(stats.AppliedCount, Is.EqualTo(scenario.ExpectedAppliedCount), scenario.TestName + " applied count mismatch.");
            Assert.That(stats.OverriddenCount, Is.EqualTo(scenario.ExpectedOverriddenCount), scenario.TestName + " overridden count mismatch.");
            Assert.That(stats.RejectedCount, Is.EqualTo(0), scenario.TestName + " has rejected requests.");
            Assert.That(stats.WriterFailedCount, Is.EqualTo(0), scenario.TestName + " has writer failures.");
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
