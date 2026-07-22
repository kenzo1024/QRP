using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal readonly struct MatDataTransferStageSample
    {
        internal readonly long ElapsedNanoseconds;
        internal readonly long GcAllocatedBytes;
        internal readonly int MaterialArrayReadCount;

        internal MatDataTransferStageSample(
            long elapsedNanoseconds,
            long gcAllocatedBytes,
            int materialArrayReadCount)
        {
            ElapsedNanoseconds = elapsedNanoseconds;
            GcAllocatedBytes = gcAllocatedBytes;
            MaterialArrayReadCount = materialArrayReadCount;
        }
    }

    internal sealed class MatDataTransferStageReportWriter
    {
        private const string MetricContract = "MDT.Module.v1";
        private readonly string m_OutputRoot;
        private readonly string m_RunStamp;

        internal MatDataTransferStageReportWriter(string outputRoot, string runStamp)
        {
            m_OutputRoot = outputRoot;
            m_RunStamp = runStamp;
            Directory.CreateDirectory(m_OutputRoot);
        }

        internal void Write(
            MatDataTransferBatchModePerformanceTests.StageScenario scenario,
            List<MatDataTransferStageSample> samples)
        {
            string reportPrefix = HasCommandLineFlag("-mdtStageGc")
                ? "MatDataTransfer_StagesGc_"
                : "MatDataTransfer_Stages_";
            string rawPath = Path.Combine(
                m_OutputRoot,
                reportPrefix + m_RunStamp + "_Raw.csv");
            string summaryPath = Path.Combine(
                m_OutputRoot,
                reportPrefix + m_RunStamp + "_Summary.csv");
            AppendRaw(rawPath, scenario, samples);
            AppendSummary(summaryPath, scenario, samples);
        }

        private static bool HasCommandLineFlag(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void AppendRaw(
            string path,
            MatDataTransferBatchModePerformanceTests.StageScenario scenario,
            List<MatDataTransferStageSample> samples)
        {
            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "MetricContract,ScenarioId,Stage,SampleIndex,InputCount,TargetCount,ElapsedNs,GcAllocatedBytes,MaterialArrayReadCount");
                }

                for (int i = 0; i < samples.Count; i++)
                {
                    MatDataTransferStageSample sample = samples[i];
                    writer.Write(MetricContract);
                    writer.Write(',');
                    writer.Write(scenario.Id);
                    writer.Write(',');
                    writer.Write(scenario.Stage);
                    writer.Write(',');
                    writer.Write(i);
                    writer.Write(',');
                    writer.Write(scenario.InputCount);
                    writer.Write(',');
                    writer.Write(scenario.TargetCount);
                    writer.Write(',');
                    writer.Write(sample.ElapsedNanoseconds);
                    writer.Write(',');
                    writer.Write(sample.GcAllocatedBytes);
                    writer.Write(',');
                    writer.Write(sample.MaterialArrayReadCount);
                    writer.WriteLine();
                }
            }
        }

        private static void AppendSummary(
            string path,
            MatDataTransferBatchModePerformanceTests.StageScenario scenario,
            List<MatDataTransferStageSample> samples)
        {
            long[] elapsed = SelectLong(samples, SampleValue.Elapsed);
            long[] gc = SelectLong(samples, SampleValue.Gc);
            long[] materialReads = SelectLong(samples, SampleValue.MaterialReads);
            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (writeHeader)
                {
                    writer.WriteLine(
                        "MetricContract,ScenarioId,ScenarioName,Description,Stage,ObjectCount,PropertyCount,SourceCount,WriteMode,IncludeDiagnostics,InputCount,TargetCount,SampleCount,MedianMs,P95Ms,MaxMs,GcMedianBytes,GcP95Bytes,GcMaxBytes,NsPerInput,BytesPerInput,MaterialArrayReadMedian,MaterialArrayReadsPerInput");
                }

                WriteCsv(writer, MetricContract);
                writer.Write(',');
                WriteCsv(writer, scenario.Id);
                writer.Write(',');
                WriteCsv(writer, scenario.Name);
                writer.Write(',');
                WriteCsv(writer, scenario.Description);
                writer.Write(',');
                WriteCsv(writer, scenario.Stage);
                writer.Write(',');
                writer.Write(scenario.ObjectCount);
                writer.Write(',');
                writer.Write(scenario.PropertyCount);
                writer.Write(',');
                writer.Write(scenario.SourceCount);
                writer.Write(',');
                WriteCsv(writer, scenario.WriteMode.ToString());
                writer.Write(',');
                writer.Write(scenario.IncludeDiagnostics ? "True" : "False");
                writer.Write(',');
                writer.Write(scenario.InputCount);
                writer.Write(',');
                writer.Write(scenario.TargetCount);
                writer.Write(',');
                writer.Write(samples.Count);
                writer.Write(',');
                WriteDouble(writer, Median(elapsed) / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, Percentile95(elapsed) / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, Maximum(elapsed) / 1000000.0);
                writer.Write(',');
                WriteDouble(writer, Median(gc));
                writer.Write(',');
                WriteDouble(writer, Percentile95(gc));
                writer.Write(',');
                WriteDouble(writer, Maximum(gc));
                writer.Write(',');
                WriteDouble(writer, PerInput(Median(elapsed), scenario.InputCount));
                writer.Write(',');
                WriteDouble(writer, PerInput(Median(gc), scenario.InputCount));
                writer.Write(',');
                WriteDouble(writer, Median(materialReads));
                writer.Write(',');
                WriteDouble(writer, PerInput(Median(materialReads), scenario.InputCount));
                writer.WriteLine();
            }
        }

        private static long[] SelectLong(
            List<MatDataTransferStageSample> samples,
            SampleValue value)
        {
            long[] result = new long[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                switch (value)
                {
                    case SampleValue.Elapsed:
                        result[i] = samples[i].ElapsedNanoseconds;
                        break;
                    case SampleValue.Gc:
                        result[i] = samples[i].GcAllocatedBytes;
                        break;
                    case SampleValue.MaterialReads:
                        result[i] = samples[i].MaterialArrayReadCount;
                        break;
                }
            }

            Array.Sort(result);
            return result;
        }

        private static double Median(long[] sorted)
        {
            if (sorted.Length == 0)
                return 0.0;
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5
                : sorted[middle];
        }

        private static double Percentile95(long[] sorted)
        {
            if (sorted.Length == 0)
                return 0.0;
            int index = Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
            return sorted[index];
        }

        private static long Maximum(long[] sorted)
        {
            return sorted.Length > 0 ? sorted[sorted.Length - 1] : 0L;
        }

        private static double PerInput(double value, int inputCount)
        {
            return inputCount > 0 ? value / inputCount : 0.0;
        }

        private static void WriteDouble(StreamWriter writer, double value)
        {
            writer.Write(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static void WriteCsv(StreamWriter writer, string value)
        {
            writer.Write('"');
            writer.Write((value ?? string.Empty).Replace("\"", "\"\""));
            writer.Write('"');
        }

        private enum SampleValue
        {
            Elapsed,
            Gc,
            MaterialReads
        }
    }
}
