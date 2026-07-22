using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rendering.MatDataTransfer.Runtime;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.Profiling;
using UnityEditorInternal;
#endif

namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal sealed partial class MatDataTransferBatchModePerformanceTests
    {
        private static readonly SampleGroup StageTimeGroup =
            new SampleGroup("MDT.Stage.Time", SampleUnit.Millisecond);
        private static readonly SampleGroup StageGcGroup =
            new SampleGroup("MDT.Stage.GC", SampleUnit.Byte);
        private static readonly ProfilerMarker StageSubmitMarker =
            new ProfilerMarker("MDT.Stage.Submit");
        private static readonly ProfilerMarker StageResolverMarker =
            new ProfilerMarker("MDT.Stage.Resolver");
        private static readonly ProfilerMarker StageWriterMarker =
            new ProfilerMarker("MDT.Stage.Writer");

        private MaterialParameterWriter m_StageWriter;
        private bool m_StageGcProfilingEnabled;
#if UNITY_EDITOR
        private int m_LastStageGcProfilerFrame = -1;
#endif

        [UnityTest, Performance]
        public IEnumerator S01_SubmitSingle()
        {
            yield return RunSubmitStage(StageScenario.Submit(
                "S01", "SubmitSingle", "单个 Payload 的 Submit 固定开销", 1, 1, 1));
        }

        [UnityTest, Performance]
        public IEnumerator S02_SubmitSmallSingleSource()
        {
            yield return RunSubmitStage(StageScenario.Submit(
                "S02", "SubmitSmallSingleSource", "1200 Payload 无冲突 Submit 基线", 100, 12, 1));
        }

        [UnityTest, Performance]
        public IEnumerator S03_SubmitSmallConflict()
        {
            yield return RunSubmitStage(StageScenario.Submit(
                "S03", "SubmitSmallConflict", "3600 Payload 三来源 Submit 压力", 100, 12, 3));
        }

        [UnityTest, Performance]
        public IEnumerator S04_SubmitMainRealLoad()
        {
            yield return RunSubmitStage(StageScenario.Submit(
                "S04", "SubmitMainRealLoad", "10800 Payload 主真实负载 Submit 压力", 300, 12, 3));
        }

        [UnityTest, Performance]
        public IEnumerator S05_SubmitMainRealLoadBatch()
        {
            yield return RunSubmitStage(StageScenario.SubmitBatch(
                "S05", "SubmitMainRealLoadBatch", "10800 逻辑写入合并为 900 个同目标 Batch", 300, 12, 3));
        }

        [UnityTest, Performance]
        public IEnumerator R00_ResolverEmpty()
        {
            yield return RunResolverStage(StageScenario.Resolver(
                "R00", "ResolverEmpty", "Resolver 空请求固定开销", 1, 1, 0));
        }

        [UnityTest, Performance]
        public IEnumerator R01_ResolverSingle()
        {
            yield return RunResolverStage(StageScenario.Resolver(
                "R01", "ResolverSingle", "Resolver 单请求固定开销", 1, 1, 1));
        }

        [UnityTest, Performance]
        public IEnumerator R03_ResolverMainSingleSource()
        {
            yield return RunResolverStage(StageScenario.Resolver(
                "R03", "ResolverMainSingleSource", "3600 请求无冲突 Resolver 基线", 300, 12, 1));
        }

        [UnityTest, Performance]
        public IEnumerator R04_ResolverMainConflict()
        {
            yield return RunResolverStage(StageScenario.Resolver(
                "R04", "ResolverMainConflict", "10800 请求和 7200 次冲突 Resolver 基线", 300, 12, 3));
        }

        [UnityTest, Performance]
        public IEnumerator W00_WriterEmpty()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W00", "WriterEmpty", "Writer 空命令固定开销", 1, 1, true, ParamWriteMethod.MaterialInstance));
        }

        [UnityTest, Performance]
        public IEnumerator W01_WriterSingle()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W01", "WriterSingle", "Writer 单命令固定开销", 1, 1, false, ParamWriteMethod.MaterialInstance));
        }

        [UnityTest, Performance]
        public IEnumerator W02_WriterSmallBatch()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W02", "WriterSmallBatch", "1200 Command 和 100 个材质目标", 100, 12, false, ParamWriteMethod.MaterialInstance));
        }

        [UnityTest, Performance]
        public IEnumerator W03_WriterMainBatch()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W03", "WriterMainBatch", "3600 Command 和 300 个材质目标", 300, 12, false, ParamWriteMethod.MaterialInstance));
        }

        [UnityTest, Performance]
        public IEnumerator W04_WriterLargeBatch()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W04", "WriterLargeBatch", "6000 Command 和 500 个材质目标", 500, 12, false, ParamWriteMethod.MaterialInstance));
        }

        [UnityTest, Performance]
        public IEnumerator W05_WriterPropertyBlock()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W05", "WriterPropertyBlock", "3600 Command 的 MaterialPropertyBlock 对照", 300, 12, false, ParamWriteMethod.MaterialPropertyBlock));
        }

        [UnityTest, Performance]
        public IEnumerator W06_WriterMainBatchWithDiagnostics()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W06", "WriterMainBatchWithDiagnostics", "3600 Command 携带诊断上下文的完整 Writer 路径", 300, 12, false, ParamWriteMethod.MaterialInstance, true));
        }

        [UnityTest, Performance]
        public IEnumerator W07_WriterMainBatchInterleavedTargets()
        {
            yield return RunWriterStage(StageScenario.Writer(
                "W07", "WriterMainBatchInterleavedTargets", "3600 Command 按属性交错 300 个材质目标", 300, 12, false, ParamWriteMethod.MaterialInstance, false, true));
        }

        private IEnumerator RunSubmitStage(StageScenario stage)
        {
            ConfigureStageGcProfiling();
            MatDataTransferBatchScenario batchScenario = stage.CreateBatchScenario();
            yield return PrepareStageFixture(batchScenario);
            if (stage.UseBatchSubmit)
                m_Driver.CacheBatchSubmitOperations(0);
            else
                m_Driver.CacheSubmitOperations(0);

            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            feature.ClearQueuedRequestsForTests();
            for (int i = 0; i < stage.WarmupCount; i++)
            {
                int submitted = stage.UseBatchSubmit
                    ? m_Driver.SubmitCachedBatches()
                    : m_Driver.SubmitCachedOperations();
                Assert.That(submitted, Is.EqualTo(stage.InputCount));
                Assert.That(feature.GetQueuedRequestCountForTests(), Is.EqualTo(stage.InputCount));
                feature.ClearQueuedRequestsForTests();
            }

            List<MatDataTransferStageSample> samples =
                new List<MatDataTransferStageSample>(stage.MeasurementCount);
            for (int i = 0; i < stage.MeasurementCount; i++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                int submitted;
                using (StageSubmitMarker.Auto())
                {
                    submitted = stage.UseBatchSubmit
                        ? m_Driver.SubmitCachedBatches()
                        : m_Driver.SubmitCachedOperations();
                }
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;

                Assert.That(submitted, Is.EqualTo(stage.InputCount));
                Assert.That(feature.GetQueuedRequestCountForTests(), Is.EqualTo(stage.InputCount));
                feature.ClearQueuedRequestsForTests();
                long allocated = 0L;
                if (m_StageGcProfilingEnabled)
                {
                    allocated = -1L;
                    for (int waitFrame = 0; waitFrame < 5 && allocated < 0L; waitFrame++)
                    {
                        RequestCameraRender();
                        yield return null;
                        ExecuteManualPipelineIfNeeded();
                        allocated = ReadStageGcAllocatedBytes("MDT.Stage.Submit");
                    }
                    Assert.That(allocated, Is.GreaterThanOrEqualTo(0L));
                }
                samples.Add(new MatDataTransferStageSample(
                    ToNanoseconds(elapsed),
                    allocated,
                    0));
            }

            WriteStageReport(stage, samples);
        }

        private IEnumerator RunWriterStage(StageScenario stage)
        {
            ConfigureStageGcProfiling();
            MatDataTransferBatchScenario batchScenario = stage.CreateBatchScenario();
            yield return PrepareStageFixture(batchScenario);

            List<ParamWriteCommand> commands = new List<ParamWriteCommand>(stage.InputCount);
            if (stage.InterleaveTargets)
                m_Driver.BuildInterleavedWriteCommands(commands, 0);
            else
                m_Driver.BuildWriteCommands(commands, 0);
            if (stage.InputCount == 0)
                commands.Clear();
            Assert.That(commands.Count, Is.EqualTo(stage.InputCount));

            m_Driver.WarmMaterialInstances();
            m_StageWriter = new MaterialParameterWriter();
            m_StageWriter.SetWriteMode(stage.WriteMode);
            List<RequestDiagnosticContext> diagnostics = stage.IncludeDiagnostics
                ? new List<RequestDiagnosticContext>(stage.InputCount)
                : null;
            for (int i = 0; i < stage.WarmupCount; i++)
            {
                if (stage.IncludeDiagnostics)
                {
                    m_Driver.BuildWriteDiagnostics(diagnostics, 0);
                    MatDataTransferLogging.Instance.BeginFrame();
                }
                m_StageWriter.Apply(commands, diagnostics);
                if (stage.IncludeDiagnostics)
                    MatDataTransferLogging.Instance.CompleteFrame();
            }

            List<MatDataTransferStageSample> samples =
                new List<MatDataTransferStageSample>(stage.MeasurementCount);
            for (int i = 0; i < stage.MeasurementCount; i++)
            {
                if (stage.IncludeDiagnostics)
                {
                    m_Driver.BuildWriteDiagnostics(diagnostics, 0);
                    MatDataTransferLogging.Instance.BeginFrame();
                }
                MaterialParameterWriter.ResetMaterialArrayReadCountForTests();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (StageWriterMarker.Auto())
                    m_StageWriter.Apply(commands, diagnostics);
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                if (stage.IncludeDiagnostics)
                    MatDataTransferLogging.Instance.CompleteFrame();
                int materialArrayReads = MaterialParameterWriter.GetMaterialArrayReadCountForTests();
                long allocated = 0L;
                if (m_StageGcProfilingEnabled)
                {
                    allocated = -1L;
                    for (int waitFrame = 0; waitFrame < 5 && allocated < 0L; waitFrame++)
                    {
                        RequestCameraRender();
                        yield return null;
                        ExecuteManualPipelineIfNeeded();
                        allocated = ReadStageGcAllocatedBytes("MDT.Stage.Writer");
                    }
                    Assert.That(allocated, Is.GreaterThanOrEqualTo(0L));
                }
                samples.Add(new MatDataTransferStageSample(
                    ToNanoseconds(elapsed),
                    allocated,
                    materialArrayReads));
            }

            if (stage.InputCount > 0)
            {
                if (stage.WriteMode == ParamWriteMethod.MaterialPropertyBlock)
                    m_Driver.AssertPropertyBlockValues(0, 16);
                else
                    m_Driver.AssertWriterValues(0, 16);
            }
            WriteStageReport(stage, samples);
        }

        private IEnumerator RunResolverStage(StageScenario stage)
        {
            ConfigureStageGcProfiling();
            yield return PrepareStageFixture(stage.CreateBatchScenario());

            List<ValidatedParamRequest> requests =
                new List<ValidatedParamRequest>(stage.InputCount);
            m_Driver.BuildValidatedRequests(requests, 0);
            Assert.That(requests.Count, Is.EqualTo(stage.InputCount));

            MaterialParameterResolver resolver = new MaterialParameterResolver();
            ResolutionStats stats = default;
            for (int i = 0; i < stage.WarmupCount; i++)
            {
                resolver.ResolveWithoutDiagnostics(requests, ref stats);
                AssertResolverStats(stage, stats);
            }

            List<MatDataTransferStageSample> samples =
                new List<MatDataTransferStageSample>(stage.MeasurementCount);
            for (int i = 0; i < stage.MeasurementCount; i++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (StageResolverMarker.Auto())
                    resolver.ResolveWithoutDiagnostics(requests, ref stats);
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;

                AssertResolverStats(stage, stats);
                long allocated = 0L;
                if (m_StageGcProfilingEnabled)
                {
                    allocated = -1L;
                    for (int waitFrame = 0; waitFrame < 5 && allocated < 0L; waitFrame++)
                    {
                        RequestCameraRender();
                        yield return null;
                        ExecuteManualPipelineIfNeeded();
                        allocated = ReadStageGcAllocatedBytes("MDT.Stage.Resolver");
                    }
                    Assert.That(allocated, Is.GreaterThanOrEqualTo(0L));
                }

                samples.Add(new MatDataTransferStageSample(
                    ToNanoseconds(elapsed),
                    allocated,
                    0));
            }

            WriteStageReport(stage, samples);
        }

        private static void AssertResolverStats(StageScenario stage, ResolutionStats stats)
        {
            int winnerCount = stage.SourceCount > 0
                ? stage.ObjectCount * stage.PropertyCount
                : 0;
            Assert.That(stats.InputCount, Is.EqualTo(stage.InputCount));
            Assert.That(stats.WinnerCount, Is.EqualTo(winnerCount));
            Assert.That(stats.OverriddenCount, Is.EqualTo(stage.InputCount - winnerCount));
        }

        private void ConfigureStageGcProfiling()
        {
#if UNITY_EDITOR
            if (m_StageGcProfilingEnabled || !HasCommandLineFlag("-mdtStageGc"))
                return;

            m_StageGcProfilingEnabled = true;
            m_ProfilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
            m_ProfilerDriverWasEnabled = ProfilerDriver.enabled;
            ProfilerDriver.enabled = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            m_LastStageGcProfilerFrame = ProfilerDriver.lastFrameIndex;
#endif
        }

        private void RestoreStageGcProfiling()
        {
#if UNITY_EDITOR
            if (!m_StageGcProfilingEnabled)
                return;

            UnityEngine.Profiling.Profiler.enabled = m_ProfilerWasEnabled;
            ProfilerDriver.enabled = m_ProfilerDriverWasEnabled;
            m_StageGcProfilingEnabled = false;
            m_LastStageGcProfilerFrame = -1;
#endif
        }

        private long ReadStageGcAllocatedBytes(string markerName)
        {
#if UNITY_EDITOR
            int lastFrameIndex = ProfilerDriver.lastFrameIndex;
            int firstFrameIndex = Math.Max(
                ProfilerDriver.firstFrameIndex,
                m_LastStageGcProfilerFrame + 1);
            for (int frameIndex = lastFrameIndex; frameIndex >= firstFrameIndex; frameIndex--)
            {
                if (!TryReadStageGcAllocatedBytes(frameIndex, markerName, out long allocatedBytes))
                    continue;

                m_LastStageGcProfilerFrame = frameIndex;
                return allocatedBytes;
            }
#endif
            return -1L;
        }

#if UNITY_EDITOR
        private static bool TryReadStageGcAllocatedBytes(
            int frameIndex,
            string markerName,
            out long allocatedBytes)
        {
            allocatedBytes = 0L;
            using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                frameIndex,
                0,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnDontSort,
                false))
            {
                if (!frameData.valid)
                    return false;

                int markerId = frameData.GetMarkerId(markerName);
                if (markerId == FrameDataView.invalidMarkerId)
                    return false;

                int itemId = FindHierarchyItem(frameData, frameData.GetRootItemID(), markerId);
                if (itemId == HierarchyFrameDataView.invalidSampleId)
                    return false;

                allocatedBytes = (long)frameData.GetItemColumnDataAsDouble(
                    itemId,
                    HierarchyFrameDataView.columnGcMemory);
                return true;
            }
        }
#endif

        private IEnumerator PrepareStageFixture(MatDataTransferBatchScenario scenario)
        {
            yield return EnsureFeatureReady();
            ConfigureFeature(scenario);
            PrepareDriver(scenario);
            m_Driver.CreateRenderObjects();
            yield return null;
            m_Driver.AttachInstances();
            yield return WaitForInstancesReady(scenario);
        }

        private static long ToNanoseconds(long stopwatchTicks)
        {
            return (long)(stopwatchTicks * (1000000000.0 / System.Diagnostics.Stopwatch.Frequency));
        }

        private static void WriteStageReport(
            StageScenario stage,
            List<MatDataTransferStageSample> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                Measure.Custom(StageTimeGroup, samples[i].ElapsedNanoseconds / 1000000.0);
                Measure.Custom(StageGcGroup, samples[i].GcAllocatedBytes);
            }

            MatDataTransferStageReportWriter writer = new MatDataTransferStageReportWriter(
                MatDataTransferBatchReportWriter.ResolveOutputRootFromCommandLine(),
                MatDataTransferBatchReportWriter.ResolveRunStampFromCommandLine());
            writer.Write(stage, samples);
        }

        internal readonly struct StageScenario
        {
            internal readonly string Id;
            internal readonly string Name;
            internal readonly string Description;
            internal readonly string Stage;
            internal readonly int ObjectCount;
            internal readonly int PropertyCount;
            internal readonly int SourceCount;
            internal readonly int InputCount;
            internal readonly int TargetCount;
            internal readonly ParamWriteMethod WriteMode;
            internal readonly bool IncludeDiagnostics;
            internal readonly bool UseBatchSubmit;
            internal readonly bool InterleaveTargets;
            internal readonly int WarmupCount;
            internal readonly int MeasurementCount;

            private StageScenario(
                string id,
                string name,
                string description,
                string stage,
                int objectCount,
                int propertyCount,
                int sourceCount,
                int inputCount,
                int targetCount,
                ParamWriteMethod writeMode,
                bool includeDiagnostics,
                bool useBatchSubmit,
                bool interleaveTargets)
            {
                Id = id;
                Name = name;
                Description = description;
                Stage = stage;
                ObjectCount = objectCount;
                PropertyCount = propertyCount;
                SourceCount = sourceCount;
                InputCount = inputCount;
                TargetCount = targetCount;
                WriteMode = writeMode;
                IncludeDiagnostics = includeDiagnostics;
                UseBatchSubmit = useBatchSubmit;
                InterleaveTargets = interleaveTargets;
                WarmupCount = 10;
                MeasurementCount = 100;
            }

            internal static StageScenario Submit(
                string id,
                string name,
                string description,
                int objectCount,
                int propertyCount,
                int sourceCount)
            {
                return new StageScenario(
                    id,
                    name,
                    description,
                    "Submit",
                    objectCount,
                    propertyCount,
                    sourceCount,
                    objectCount * propertyCount * sourceCount,
                    objectCount,
                    ParamWriteMethod.None,
                    false,
                    false,
                    false);
            }

            internal static StageScenario SubmitBatch(
                string id,
                string name,
                string description,
                int objectCount,
                int propertyCount,
                int sourceCount)
            {
                return new StageScenario(
                    id,
                    name,
                    description,
                    "Submit",
                    objectCount,
                    propertyCount,
                    sourceCount,
                    objectCount * propertyCount * sourceCount,
                    objectCount,
                    ParamWriteMethod.None,
                    false,
                    true,
                    false);
            }

            internal static StageScenario Writer(
                string id,
                string name,
                string description,
                int objectCount,
                int propertyCount,
                bool empty,
                ParamWriteMethod writeMode,
                bool includeDiagnostics = false,
                bool interleaveTargets = false)
            {
                return new StageScenario(
                    id,
                    name,
                    description,
                    "Writer",
                    objectCount,
                    propertyCount,
                    1,
                    empty ? 0 : objectCount * propertyCount,
                    empty ? 0 : objectCount,
                    writeMode,
                    includeDiagnostics,
                    false,
                    interleaveTargets);
            }

            internal static StageScenario Resolver(
                string id,
                string name,
                string description,
                int objectCount,
                int propertyCount,
                int sourceCount)
            {
                int inputCount = objectCount * propertyCount * sourceCount;
                return new StageScenario(
                    id,
                    name,
                    description,
                    "Resolver",
                    objectCount,
                    propertyCount,
                    sourceCount,
                    inputCount,
                    sourceCount > 0 ? objectCount * propertyCount : 0,
                    ParamWriteMethod.None,
                    false,
                    false,
                    false);
            }

            internal MatDataTransferBatchScenario CreateBatchScenario()
            {
                return new MatDataTransferBatchScenario(
                    Id,
                    Name,
                    ObjectCount,
                    PropertyCount,
                    SourceCount,
                    UseBatchSubmit
                        ? MatDataTransferBatchApiMode.ForMaterialBatch
                        : MatDataTransferBatchApiMode.ForMaterial,
                    IncludeDiagnostics,
                    1,
                    1);
            }
        }
    }
}
