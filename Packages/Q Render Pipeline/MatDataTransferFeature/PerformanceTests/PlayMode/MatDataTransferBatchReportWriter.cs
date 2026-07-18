using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal sealed class MatDataTransferBatchReportWriter
    {
        private const string DefaultOutputRoot =
            @"D:\_QData\QOBData\望月 Work\工具\角色材质统一参数管理 Feature\PerformanceResults\BatchMode";

        private readonly string m_OutputRoot;
        private readonly string m_RunStamp;

        public MatDataTransferBatchReportWriter(string outputRoot, string runStamp)
        {
            m_OutputRoot = string.IsNullOrWhiteSpace(outputRoot) ? DefaultOutputRoot : outputRoot;
            m_RunStamp = string.IsNullOrWhiteSpace(runStamp)
                ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                : runStamp;
            Directory.CreateDirectory(m_OutputRoot);
        }

        public string Write(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            if (IsResolverGcReport())
                return WriteResolverGcReport(scenario, samples, sampleCount);

            if (IsResolverOnlyReport())
                return WriteResolverReport(scenario, samples, sampleCount);

            string summaryPath = Path.Combine(
                m_OutputRoot,
                "MatDataTransfer_BatchMode_" + m_RunStamp + "_Summary.csv");

            AppendSummary(summaryPath, scenario, samples, sampleCount);
            return summaryPath;
        }

        private string WriteResolverGcReport(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            MatDataTransferBatchFrameStats[] submissionSamples = SelectPhase(
                samples,
                sampleCount,
                MatDataTransferBatchPhase.Submission);
            if (submissionSamples.Length == 0)
                return string.Empty;

            string rawPath = Path.Combine(
                m_OutputRoot,
                "MatDataTransfer_ResolverGc_" + m_RunStamp + "_Raw.csv");
            string summaryPath = Path.Combine(
                m_OutputRoot,
                "MatDataTransfer_ResolverGc_" + m_RunStamp + "_Summary.csv");

            AppendResolverGcRaw(rawPath, scenario, submissionSamples);
            AppendResolverGcSummary(summaryPath, scenario, submissionSamples);
            return summaryPath;
        }

        private static bool IsResolverGcReport()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-mdtResolverGc", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void AppendResolverGcRaw(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples)
        {
            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "ScenarioId,SourceScenarioId,FrameIndex,Input,Group,Winner,Overridden,ResolverGcAllocatedBytes");
                }

                string resolverScenarioId = GetResolverScenarioId(scenario.Id);
                for (int i = 0; i < samples.Length; i++)
                {
                    MatDataTransferBatchFrameStats sample = samples[i];
                    writer.Write(resolverScenarioId);
                    writer.Write(',');
                    writer.Write(scenario.Id);
                    writer.Write(',');
                    writer.Write(sample.FrameIndex);
                    writer.Write(',');
                    writer.Write(sample.PayloadCount);
                    writer.Write(',');
                    writer.Write(sample.GroupCount);
                    writer.Write(',');
                    writer.Write(sample.CommandCount);
                    writer.Write(',');
                    writer.Write(sample.OverriddenCount);
                    writer.Write(',');
                    writer.Write(sample.PipelineResolveGcAllocatedBytes);
                    writer.WriteLine();
                }
            }
        }

        private static void AppendResolverGcSummary(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples)
        {
            bool writeHeader = !File.Exists(path);
            Summary resolveGc = Summarize(samples, samples.Length, ValueKind.ResolveGcBytes);
            double bytesPerInput = scenario.ExpectedPayloadCount > 0
                ? resolveGc.Median / scenario.ExpectedPayloadCount
                : 0.0;

            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "ScenarioId,SourceScenarioId,Input,Group,Winner,Overridden,SampleCount,ResolverGcMedian,ResolverGcP95,ResolverGcMax,BytesPerInput");
                }

                writer.Write(GetResolverScenarioId(scenario.Id));
                writer.Write(',');
                writer.Write(scenario.Id);
                writer.Write(',');
                writer.Write(scenario.ExpectedPayloadCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedGroupCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedCommandCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedOverriddenCount);
                writer.Write(',');
                writer.Write(samples.Length);
                writer.Write(',');
                WriteDouble(writer, resolveGc.Median);
                writer.Write(',');
                WriteDouble(writer, resolveGc.P95);
                writer.Write(',');
                WriteDouble(writer, resolveGc.Max);
                writer.Write(',');
                WriteDouble(writer, bytesPerInput);
                writer.WriteLine();
            }
        }

        private string WriteResolverReport(
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            MatDataTransferBatchFrameStats[] submissionSamples = SelectPhase(
                samples,
                sampleCount,
                MatDataTransferBatchPhase.Submission);
            if (submissionSamples.Length == 0)
                return string.Empty;

            string rawPath = Path.Combine(
                m_OutputRoot,
                "MatDataTransfer_Resolver_" + m_RunStamp + "_Raw.csv");
            string summaryPath = Path.Combine(
                m_OutputRoot,
                "MatDataTransfer_Resolver_" + m_RunStamp + "_Summary.csv");

            AppendResolverRaw(rawPath, scenario, submissionSamples);
            AppendResolverSummary(summaryPath, scenario, submissionSamples);
            return summaryPath;
        }

        private static bool IsResolverOnlyReport()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-mdtResolverOnly", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void AppendResolverRaw(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples)
        {
            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "ScenarioId,SourceScenarioId,FrameIndex,Input,Group,Winner,Overridden,ResolverElapsedNs");
                }

                string resolverScenarioId = GetResolverScenarioId(scenario.Id);
                for (int i = 0; i < samples.Length; i++)
                {
                    MatDataTransferBatchFrameStats sample = samples[i];
                    writer.Write(resolverScenarioId);
                    writer.Write(',');
                    writer.Write(scenario.Id);
                    writer.Write(',');
                    writer.Write(sample.FrameIndex);
                    writer.Write(',');
                    writer.Write(sample.PayloadCount);
                    writer.Write(',');
                    writer.Write(sample.GroupCount);
                    writer.Write(',');
                    writer.Write(sample.CommandCount);
                    writer.Write(',');
                    writer.Write(sample.OverriddenCount);
                    writer.Write(',');
                    writer.Write(sample.PipelineResolveNanoseconds);
                    writer.WriteLine();
                }
            }
        }

        private static void AppendResolverSummary(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples)
        {
            bool writeHeader = !File.Exists(path);
            Summary resolve = Summarize(samples, samples.Length, ValueKind.ResolveNs);
            double nanosecondsPerInput = scenario.ExpectedPayloadCount > 0
                ? resolve.Median / scenario.ExpectedPayloadCount
                : 0.0;

            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "ScenarioId,SourceScenarioId,ObjectCount,PropertyCount,SourceCount,Input,Group,Winner,Overridden,SampleCount,ResolverMedianMs,ResolverP95Ms,ResolverMaxMs,NsPerInput");
                }

                writer.Write(GetResolverScenarioId(scenario.Id));
                writer.Write(',');
                writer.Write(scenario.Id);
                writer.Write(',');
                writer.Write(scenario.ObjectCount);
                writer.Write(',');
                writer.Write(scenario.PropertyCount);
                writer.Write(',');
                writer.Write(scenario.SourceCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedPayloadCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedGroupCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedCommandCount);
                writer.Write(',');
                writer.Write(scenario.ExpectedOverriddenCount);
                writer.Write(',');
                writer.Write(samples.Length);
                writer.Write(',');
                WriteDouble(writer, resolve.Median / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, resolve.P95 / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, resolve.Max / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, nanosecondsPerInput);
                writer.WriteLine();
            }
        }

        private static string GetResolverScenarioId(string scenarioId)
        {
            switch (scenarioId)
            {
                case "B01":
                    return "R01";
                case "B02":
                    return "R02";
                case "B03":
                    return "R03";
                case "B04":
                    return "R04";
                case "B06":
                    return "R05";
                case "B05":
                    return "R06";
                default:
                    return scenarioId;
            }
        }

        public static string ResolveOutputRootFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-mdtOutputRoot", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            string env = Environment.GetEnvironmentVariable("MDT_PERF_OUTPUT_ROOT");
            return string.IsNullOrWhiteSpace(env) ? DefaultOutputRoot : env;
        }

        public static string ResolveRunStampFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-mdtRunStamp", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            string env = Environment.GetEnvironmentVariable("MDT_PERF_RUN_STAMP");
            return string.IsNullOrWhiteSpace(env)
                ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                : env;
        }

        private static void WriteRaw(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            StringBuilder builder = new StringBuilder(4096 + sampleCount * 512);
            builder.AppendLine(
                "ScenarioId,Phase,PipelineMode,FrameIndex,FrameMs,GcAllocatedBytes,ManagedHeapBytes,ActiveInstance,LiveInstance,Payload,Group,Command,Applied,Overridden,Rejected,WriterFailed,Trace,TimelineRecord,MaterialArrayRead,SyncCallCount,SyncElapsedNs,SyncGcAllocatedBytes,PipelineExecutionCount,SubmitTotalNs,SubmitValidateNs,PassSyncInstancesNs,PassPipelineNs,PipelineResolveNs,PipelineWriteNs,PipelineWriteResolveMaterialNs,PipelineWriteSetValueNs,LoggingCaptureNs,LoggingCommitTimelineNs");

            for (int i = 0; i < sampleCount; i++)
            {
                MatDataTransferBatchFrameStats sample = samples[i];
                builder.Append(scenario.Id).Append(',');
                builder.Append(sample.Phase).Append(',');
                builder.Append(sample.PipelineMode).Append(',');
                builder.Append(sample.FrameIndex).Append(',');
                AppendDouble(builder, sample.FrameMilliseconds).Append(',');
                builder.Append(sample.GcAllocatedBytes).Append(',');
                builder.Append(sample.ManagedHeapBytes).Append(',');
                builder.Append(sample.ActiveInstanceCount).Append(',');
                builder.Append(sample.LiveInstanceCount).Append(',');
                builder.Append(sample.PayloadCount).Append(',');
                builder.Append(sample.GroupCount).Append(',');
                builder.Append(sample.CommandCount).Append(',');
                builder.Append(sample.AppliedCount).Append(',');
                builder.Append(sample.OverriddenCount).Append(',');
                builder.Append(sample.RejectedCount).Append(',');
                builder.Append(sample.WriterFailedCount).Append(',');
                builder.Append(sample.TraceCount).Append(',');
                builder.Append(sample.TimelineRecordCount).Append(',');
                builder.Append(sample.MaterialArrayReadCount).Append(',');
                builder.Append(sample.SyncCallCount).Append(',');
                builder.Append(sample.SyncElapsedNanoseconds).Append(',');
                builder.Append(sample.SyncGcAllocatedBytes).Append(',');
                builder.Append(sample.PipelineExecutionCount).Append(',');
                builder.Append(sample.SubmitTotalNanoseconds).Append(',');
                builder.Append(sample.SubmitValidateNanoseconds).Append(',');
                builder.Append(sample.PassSyncInstancesNanoseconds).Append(',');
                builder.Append(sample.PassPipelineNanoseconds).Append(',');
                builder.Append(sample.PipelineResolveNanoseconds).Append(',');
                builder.Append(sample.PipelineWriteNanoseconds).Append(',');
                builder.Append(sample.PipelineWriteResolveMaterialNanoseconds).Append(',');
                builder.Append(sample.PipelineWriteSetValueNanoseconds).Append(',');
                builder.Append(sample.LoggingCaptureNanoseconds).Append(',');
                builder.Append(sample.LoggingCommitTimelineNanoseconds).AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static void AppendSummary(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount)
        {
            Array phases = Enum.GetValues(typeof(MatDataTransferBatchPhase));
            for (int i = 0; i < phases.Length; i++)
            {
                MatDataTransferBatchPhase phase = (MatDataTransferBatchPhase)phases.GetValue(i);
                MatDataTransferBatchFrameStats[] phaseSamples = SelectPhase(
                    samples,
                    sampleCount,
                    phase);
                if (phaseSamples.Length == 0)
                    continue;

                AppendPhaseSummary(path, scenario, phaseSamples, phaseSamples.Length, phase);
            }
        }

        private static void AppendPhaseSummary(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount,
            MatDataTransferBatchPhase phase)
        {
            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "UnityVersion,OperatingSystem,Processor,ScenarioId,ScenarioName,Phase,PipelineMode,ObjectCount,PropertyCount,SourceCount,ApiMode,Logging,WarmupFrames,MeasurementFrames,ExpectedPayload,ExpectedCommand,FrameMsMedian,FrameMsP95,FrameMsMax,GcBytesMedian,GcBytesP95,GcBytesMax,SyncCallsMedian,SyncMsMedian,SyncMsP95,SyncMsMax,SyncGcBytesMedian,SyncGcBytesP95,SyncGcBytesMax,PipelineExecutionsMedian,SubmitMsMedian,ResolveMsMedian,WriteMsMedian,AppliedMedian,OverriddenMedian,RejectedMax,WriterFailedMax");
                }

                Summary frame = Summarize(samples, sampleCount, ValueKind.FrameMs);
                Summary gc = Summarize(samples, sampleCount, ValueKind.GcBytes);
                Summary submit = Summarize(samples, sampleCount, ValueKind.SubmitNs);
                Summary resolve = Summarize(samples, sampleCount, ValueKind.ResolveNs);
                Summary write = Summarize(samples, sampleCount, ValueKind.WriteNs);
                Summary applied = Summarize(samples, sampleCount, ValueKind.Applied);
                Summary overridden = Summarize(samples, sampleCount, ValueKind.Overridden);
                Summary rejected = Summarize(samples, sampleCount, ValueKind.Rejected);
                Summary writerFailed = Summarize(samples, sampleCount, ValueKind.WriterFailed);
                Summary syncCalls = Summarize(samples, sampleCount, ValueKind.SyncCalls);
                Summary sync = Summarize(samples, sampleCount, ValueKind.SyncNs);
                Summary syncGc = Summarize(samples, sampleCount, ValueKind.SyncGcBytes);
                Summary pipelineExecutions = Summarize(samples, sampleCount, ValueKind.PipelineExecutions);
                bool isSubmission = phase == MatDataTransferBatchPhase.Submission;

                writer.Write(Escape(Application.unityVersion));
                writer.Write(',');
                writer.Write(Escape(SystemInfo.operatingSystem));
                writer.Write(',');
                writer.Write(Escape(SystemInfo.processorType));
                writer.Write(',');
                writer.Write(scenario.Id);
                writer.Write(',');
                writer.Write(scenario.Name);
                writer.Write(',');
                writer.Write(phase);
                writer.Write(',');
                writer.Write(samples[0].PipelineMode);
                writer.Write(',');
                writer.Write(scenario.ObjectCount);
                writer.Write(',');
                writer.Write(scenario.PropertyCount);
                writer.Write(',');
                writer.Write(scenario.SourceCount);
                writer.Write(',');
                writer.Write(scenario.ApiMode);
                writer.Write(',');
                writer.Write(scenario.EnableLogging);
                writer.Write(',');
                writer.Write(scenario.WarmupFrames);
                writer.Write(',');
                writer.Write(scenario.MeasurementFrames);
                writer.Write(',');
                writer.Write(isSubmission ? scenario.ExpectedPayloadCount : 0);
                writer.Write(',');
                writer.Write(isSubmission ? scenario.ExpectedCommandCount : 0);
                writer.Write(',');
                WriteDouble(writer, frame.Median);
                writer.Write(',');
                WriteDouble(writer, frame.P95);
                writer.Write(',');
                WriteDouble(writer, frame.Max);
                writer.Write(',');
                WriteDouble(writer, gc.Median);
                writer.Write(',');
                WriteDouble(writer, gc.P95);
                writer.Write(',');
                WriteDouble(writer, gc.Max);
                writer.Write(',');
                WriteDouble(writer, syncCalls.Median);
                writer.Write(',');
                WriteDouble(writer, sync.Median / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, sync.P95 / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, sync.Max / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, syncGc.Median);
                writer.Write(',');
                WriteDouble(writer, syncGc.P95);
                writer.Write(',');
                WriteDouble(writer, syncGc.Max);
                writer.Write(',');
                WriteDouble(writer, pipelineExecutions.Median);
                writer.Write(',');
                WriteDouble(writer, submit.Median / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, resolve.Median / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, write.Median / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, applied.Median);
                writer.Write(',');
                WriteDouble(writer, overridden.Median);
                writer.Write(',');
                WriteDouble(writer, rejected.Max);
                writer.Write(',');
                WriteDouble(writer, writerFailed.Max);
                writer.WriteLine();
            }
        }

        private static void AppendMarkdown(
            string path,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount,
            string rawPath)
        {
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                Array phases = Enum.GetValues(typeof(MatDataTransferBatchPhase));
                for (int i = 0; i < phases.Length; i++)
                {
                    MatDataTransferBatchPhase phase = (MatDataTransferBatchPhase)phases.GetValue(i);
                    MatDataTransferBatchFrameStats[] phaseSamples = SelectPhase(
                        samples,
                        sampleCount,
                        phase);
                    if (phaseSamples.Length > 0)
                        AppendPhaseMarkdown(writer, scenario, phaseSamples, phase, rawPath);
                }
            }
        }

        private static void AppendPhaseMarkdown(
            TextWriter writer,
            MatDataTransferBatchScenario scenario,
            MatDataTransferBatchFrameStats[] samples,
            MatDataTransferBatchPhase phase,
            string rawPath)
        {
            int sampleCount = samples.Length;
            Summary frame = Summarize(samples, sampleCount, ValueKind.FrameMs);
            Summary gc = Summarize(samples, sampleCount, ValueKind.GcBytes);
            Summary submit = Summarize(samples, sampleCount, ValueKind.SubmitNs);
            Summary resolve = Summarize(samples, sampleCount, ValueKind.ResolveNs);
            Summary write = Summarize(samples, sampleCount, ValueKind.WriteNs);
            Summary syncCalls = Summarize(samples, sampleCount, ValueKind.SyncCalls);
            Summary sync = Summarize(samples, sampleCount, ValueKind.SyncNs);
            Summary syncGc = Summarize(samples, sampleCount, ValueKind.SyncGcBytes);
            Summary pipelineExecutions = Summarize(samples, sampleCount, ValueKind.PipelineExecutions);

            writer.WriteLine("## " + scenario.TestName + " / " + phase);
            writer.WriteLine();
            writer.WriteLine("- Pipeline mode: `" + samples[0].PipelineMode + "`");
            writer.WriteLine();
            writer.WriteLine("| Metric | Median | P95 | Max |");
            writer.WriteLine("|---|---:|---:|---:|");
            WriteMarkdownRow(writer, "Frame ms", frame);
            WriteMarkdownRow(writer, "GC bytes", gc);
            WriteMarkdownRow(writer, "Sync calls", syncCalls);
            WriteMarkdownRow(writer, "Sync ms", sync.Scale(1.0 / 1000000.0));
            WriteMarkdownRow(writer, "Sync GC bytes", syncGc);
            WriteMarkdownRow(writer, "Pipeline executions", pipelineExecutions);
            WriteMarkdownRow(writer, "Submit ms", submit.Scale(1.0 / 1000000.0));
            WriteMarkdownRow(writer, "Resolve ms", resolve.Scale(1.0 / 1000000.0));
            WriteMarkdownRow(writer, "Write ms", write.Scale(1.0 / 1000000.0));
            writer.WriteLine();
            writer.WriteLine("- Raw CSV: `" + rawPath + "`");
            writer.WriteLine("- Expected Payload: `" + (phase == MatDataTransferBatchPhase.Submission ? scenario.ExpectedPayloadCount : 0) + "`");
            writer.WriteLine("- Expected Command: `" + (phase == MatDataTransferBatchPhase.Submission ? scenario.ExpectedCommandCount : 0) + "`");
            writer.WriteLine();
        }

        private static MatDataTransferBatchFrameStats[] SelectPhase(
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount,
            MatDataTransferBatchPhase phase)
        {
            int count = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                if (samples[i].Phase == phase)
                    count++;
            }

            MatDataTransferBatchFrameStats[] result =
                new MatDataTransferBatchFrameStats[count];
            int targetIndex = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                if (samples[i].Phase == phase)
                    result[targetIndex++] = samples[i];
            }

            return result;
        }

        private static Summary Summarize(
            MatDataTransferBatchFrameStats[] samples,
            int sampleCount,
            ValueKind kind)
        {
            if (sampleCount <= 0)
                return default;

            double[] values = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                values[i] = ReadValue(samples[i], kind);

            Array.Sort(values);
            return new Summary(
                values[0],
                Percentile(values, 0.5),
                Average(values),
                Percentile(values, 0.95),
                values[values.Length - 1]);
        }

        private static double ReadValue(MatDataTransferBatchFrameStats sample, ValueKind kind)
        {
            switch (kind)
            {
                case ValueKind.GcBytes:
                    return sample.GcAllocatedBytes;
                case ValueKind.SubmitNs:
                    return sample.SubmitTotalNanoseconds;
                case ValueKind.ResolveNs:
                    return sample.PipelineResolveNanoseconds;
                case ValueKind.ResolveGcBytes:
                    return sample.PipelineResolveGcAllocatedBytes;
                case ValueKind.WriteNs:
                    return sample.PipelineWriteNanoseconds;
                case ValueKind.Applied:
                    return sample.AppliedCount;
                case ValueKind.Overridden:
                    return sample.OverriddenCount;
                case ValueKind.Rejected:
                    return sample.RejectedCount;
                case ValueKind.WriterFailed:
                    return sample.WriterFailedCount;
                case ValueKind.SyncCalls:
                    return sample.SyncCallCount;
                case ValueKind.SyncNs:
                    return sample.SyncElapsedNanoseconds;
                case ValueKind.SyncGcBytes:
                    return sample.SyncGcAllocatedBytes;
                case ValueKind.PipelineExecutions:
                    return sample.PipelineExecutionCount;
                case ValueKind.FrameMs:
                default:
                    return sample.FrameMilliseconds;
            }
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0.0;

            double position = (sortedValues.Length - 1) * percentile;
            int left = Mathf.FloorToInt((float)position);
            int right = Mathf.CeilToInt((float)position);
            if (left == right)
                return sortedValues[left];

            double t = position - left;
            return sortedValues[left] * (1.0 - t) + sortedValues[right] * t;
        }

        private static double Average(double[] values)
        {
            double total = 0.0;
            for (int i = 0; i < values.Length; i++)
                total += values[i];

            return total / values.Length;
        }

        private static void WriteMarkdownRow(TextWriter writer, string name, Summary summary)
        {
            writer.Write("| ");
            writer.Write(name);
            writer.Write(" | ");
            WriteDouble(writer, summary.Median);
            writer.Write(" | ");
            WriteDouble(writer, summary.P95);
            writer.Write(" | ");
            WriteDouble(writer, summary.Max);
            writer.WriteLine(" |");
        }

        private static StringBuilder AppendDouble(StringBuilder builder, double value)
        {
            return builder.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static void WriteDouble(TextWriter writer, double value)
        {
            writer.Write(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private enum ValueKind
        {
            FrameMs,
            GcBytes,
            SubmitNs,
            ResolveNs,
            ResolveGcBytes,
            WriteNs,
            Applied,
            Overridden,
            Rejected,
            WriterFailed,
            SyncCalls,
            SyncNs,
            SyncGcBytes,
            PipelineExecutions
        }

        private readonly struct Summary
        {
            public readonly double Min;
            public readonly double Median;
            public readonly double Average;
            public readonly double P95;
            public readonly double Max;

            public Summary(double min, double median, double average, double p95, double max)
            {
                Min = min;
                Median = median;
                Average = average;
                P95 = p95;
                Max = max;
            }

            public Summary Scale(double value)
            {
                return new Summary(
                    Min * value,
                    Median * value,
                    Average * value,
                    P95 * value,
                    Max * value);
            }
        }
    }
}
