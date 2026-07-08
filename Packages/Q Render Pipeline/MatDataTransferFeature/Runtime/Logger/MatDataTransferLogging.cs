using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public sealed class MatDataTransferLogging : IMatDataTransferLogging, IDisposable
    {
        public const int DefaultMaxTimelineFramesValue = 128;

        private const int MaxEditorTimelineRecords = 10000;
        private const int MaxFileRecordsPerSession = 10000;
        private const string FileDirectoryName = "MatDataTransferLogs";

        private readonly List<MatDataTransferTimelineRecord> m_TimelineRecords = new List<MatDataTransferTimelineRecord>();
        private readonly List<MatDataTransferTimelineFrame> m_TimelineFrames =
            new List<MatDataTransferTimelineFrame>();
        private readonly List<MatDataTransferTimelineRecord> m_FrameTimelineRecords = new List<MatDataTransferTimelineRecord>();
        private readonly List<ParamWriteResult> m_FrameReceipts = new List<ParamWriteResult>();
        private readonly MatDataTransferFileLogWriter m_FileWriter = new MatDataTransferFileLogWriter();
        private static readonly MatDataTransferLogging s_Instance = new MatDataTransferLogging();
        private MatDataTransferFeature m_Feature;
        private int m_TimelineVersion;
        private int m_MaxTimelineFrames = DefaultMaxTimelineFramesValue;
        private bool m_EnableLogging;
        private bool m_AllowReleaseFileLogging;
        private bool m_IsFrameOpen;

        public static MatDataTransferLogging Instance => s_Instance;
        public IReadOnlyList<MatDataTransferTimelineRecord> TimelineRecords => m_TimelineRecords;
        public IReadOnlyList<MatDataTransferTimelineFrame> TimelineFrames => m_TimelineFrames;
        public IReadOnlyList<ParamWriteResult> LastReceipts => m_FrameReceipts;
        public int TimelineVersion => m_TimelineVersion;
        public int MaxTimelineFrames => m_MaxTimelineFrames;
        public string CurrentLogFilePath => m_FileWriter.FilePath;

        private MatDataTransferLogging()
        {
        }

        internal void BindFeature(MatDataTransferFeature feature)
        {
            if (feature != null)
                m_Feature = feature;
        }

        internal void UnbindFeature(MatDataTransferFeature feature)
        {
            if (!ReferenceEquals(m_Feature, feature))
                return;

            m_Feature = null;
            m_FileWriter.Close();
        }

        internal void ApplySettings(MatDataTransferLoggingSettings settings)
        {
            m_EnableLogging = settings != null && settings.EnableLogging;
            m_AllowReleaseFileLogging = settings != null && settings.AllowReleaseFileLogging;
            Configure(settings != null ? settings.MaxTimelineFrames : DefaultMaxTimelineFramesValue);
        }

        public void Configure(int maxTimelineFrames)
        {
            m_MaxTimelineFrames = Mathf.Max(1, maxTimelineFrames);
            TrimTimelineFrames();
        }

        public List<MatDataTransferTimelineRecord> GetTimelineList()
        {
            return IsTimelineCaptureEnabled() ? m_FrameTimelineRecords : null;
        }

        public List<ParamWriteResult> GetReceiptList() => m_FrameReceipts;

        internal static void AppendSubmitStep(
            ref ParamTransferPayload payload,
            ParamSubmitStep step)
        {
            Instance.AppendSubmitStepInternal(ref payload, step);
        }

        internal static void CaptureSubmitSnapshot(ref ParamTransferPayload payload)
        {
            Instance.CaptureSubmitSnapshotInternal(
                ref payload,
                default,
                ParamWriteMethod.None,
                string.Empty,
                string.Empty);
        }

        internal static void CaptureWriteSnapshot(
            ref ParamTransferPayload payload,
            MaterialWriteCommand command,
            ParamWriteMethod writeMethod)
        {
            Instance.CaptureSubmitSnapshotInternal(
                ref payload,
                command.BindingResolution,
                writeMethod,
                command.GameObjectPath,
                BuildRendererPath(command.Renderer));
        }

        internal static void CaptureResolvedSnapshot(
            ref ParamTransferPayload payload,
            ResolvedMaterialBinding binding,
            ParamSubmitStep step,
            string gameObjectPath,
            Renderer renderer)
        {
            Instance.CaptureResolvedSnapshotInternal(
                ref payload,
                binding,
                step,
                gameObjectPath,
                BuildRendererPath(renderer));
        }

        private void AppendSubmitStepInternal(
            ref ParamTransferPayload payload,
            ParamSubmitStep step)
        {
            if (payload.Trace == null)
                payload.Trace = new ParamSubmitTrace();

            payload.Trace.AddStep(step);
            WriteConsoleStep(step);
        }

        private void CaptureSubmitSnapshotInternal(
            ref ParamTransferPayload payload,
            ResolvedMaterialBinding binding,
            ParamWriteMethod writeMethod,
            string gameObjectPath,
            string rendererPath)
        {
            if (payload.Trace == null)
                payload.Trace = new ParamSubmitTrace();

            bool ownsFrame = !m_IsFrameOpen;
            if (ownsFrame)
                BeginFrame();

            RecordResult(
                payload,
                binding,
                writeMethod,
                payload.Trace,
                ResolveGameObjectPath(payload, gameObjectPath),
                rendererPath);

            if (ownsFrame)
                CompleteFrame();
        }

        private void CaptureResolvedSnapshotInternal(
            ref ParamTransferPayload payload,
            ResolvedMaterialBinding binding,
            ParamSubmitStep step,
            string gameObjectPath,
            string rendererPath)
        {
            AppendSubmitStepInternal(
                ref payload,
                step);

            CaptureSubmitSnapshotInternal(
                ref payload,
                binding,
                ParamWriteMethod.None,
                gameObjectPath,
                rendererPath);
        }

        public void BeginFrame()
        {
            m_FrameReceipts.Clear();
            m_FrameTimelineRecords.Clear();
            m_IsFrameOpen = true;
        }

        public void CompleteFrame()
        {
            m_IsFrameOpen = false;
            if (!m_EnableLogging)
            {
                ClearEditorTimelineRecords();
                m_FileWriter.Close();
                return;
            }

            if (m_FrameTimelineRecords.Count == 0)
            {
                if (!IsFileTimelineAllowed())
                    m_FileWriter.Close();
                return;
            }

            AppendEditorTimeline();
            WriteTimelineToFile();
            m_FrameTimelineRecords.Clear();
        }

        public void Dispose()
        {
            m_FileWriter.Dispose();
        }

        public void ClearTimelineRecords()
        {
            ClearEditorTimelineRecords();
        }

        private bool IsTimelineCaptureEnabled()
        {
            return m_EnableLogging
                && (IsEditorTimelineEnabled() || IsFileTimelineAllowed());
        }

        private void AppendEditorTimeline()
        {
            if (!IsEditorTimelineEnabled())
            {
                ClearEditorTimelineRecords();
                return;
            }

            AppendEditorTimelineFrames();
            m_TimelineRecords.AddRange(m_FrameTimelineRecords);
            int overflow = m_TimelineRecords.Count - MaxEditorTimelineRecords;
            if (overflow > 0)
                m_TimelineRecords.RemoveRange(0, overflow);

            m_TimelineVersion++;
        }

        private void ClearEditorTimelineRecords()
        {
            if (m_TimelineRecords.Count == 0
                && m_TimelineFrames.Count == 0
                && m_FrameTimelineRecords.Count == 0)
                return;

            m_TimelineRecords.Clear();
            m_TimelineFrames.Clear();
            m_FrameTimelineRecords.Clear();
            m_TimelineVersion++;
        }

        private void AppendEditorTimelineFrames()
        {
            for (int i = 0; i < m_FrameTimelineRecords.Count; i++)
                AppendEditorTimelineRecord(m_FrameTimelineRecords[i]);

            TrimTimelineFrames();
        }

        private void AppendEditorTimelineRecord(MatDataTransferTimelineRecord record)
        {
            int frameIndex = record.FrameIndex;
            int existingIndex = FindEditorTimelineFrameIndex(frameIndex);
            if (existingIndex >= 0)
            {
                m_TimelineFrames[existingIndex].Records.Add(record);
                return;
            }

            MatDataTransferTimelineFrame frame = new MatDataTransferTimelineFrame(
                frameIndex,
                record.TimeSinceStartup,
                null);
            frame.Records.Add(record);
            m_TimelineFrames.Insert(FindEditorTimelineInsertIndex(frameIndex), frame);
        }

        private int FindEditorTimelineFrameIndex(int frameIndex)
        {
            for (int i = 0; i < m_TimelineFrames.Count; i++)
            {
                if (m_TimelineFrames[i].FrameIndex == frameIndex)
                    return i;
            }

            return -1;
        }

        private int FindEditorTimelineInsertIndex(int frameIndex)
        {
            for (int i = 0; i < m_TimelineFrames.Count; i++)
            {
                if (frameIndex > m_TimelineFrames[i].FrameIndex)
                    return i;
            }

            return m_TimelineFrames.Count;
        }

        private void TrimTimelineFrames()
        {
            int maxFrames = Mathf.Max(1, m_MaxTimelineFrames);
            int overflow = m_TimelineFrames.Count - maxFrames;
            if (overflow > 0)
                m_TimelineFrames.RemoveRange(maxFrames, overflow);
        }

        private void WriteTimelineToFile()
        {
            if (!IsFileTimelineAllowed())
            {
                m_FileWriter.Close();
                return;
            }

            m_FileWriter.WriteRecords(
                m_FrameTimelineRecords,
                FileDirectoryName,
                MaxFileRecordsPerSession);
        }

        private bool IsFileTimelineAllowed()
        {
#if UNITY_EDITOR
            return false;
#else
            if (Debug.isDebugBuild)
                return true;

            return m_AllowReleaseFileLogging && MatDataTransferLogger.RuntimeFileOutputEnabled;
#endif
        }

        private static bool IsEditorTimelineEnabled()
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void RecordResult(
            ParamTransferPayload payload,
            ResolvedMaterialBinding binding,
            ParamWriteMethod writeMethod,
            ParamSubmitTrace submitTrace,
            string gameObjectPath,
            string rendererPath)
        {
            ParamSubmitStep currentStep = submitTrace != null ? submitTrace.Current : null;
            ParamWriteResult result = ParamWriteResult.FromPayload(
                payload,
                binding,
                writeMethod,
                currentStep);
            m_FrameReceipts.Add(result);

            List<MatDataTransferTimelineRecord> timeline = GetTimelineList();
            if (timeline == null)
                return;

            string preview = payload.Identity.Value.ToPreview();
            timeline.Add(new MatDataTransferTimelineRecord
            {
                FrameIndex = ResolveTimelineFrameIndex(payload),
                TimeSinceStartup = MatDataTransferRuntime.TimeSinceStartup,
                Sequence = payload.Sequence,
                InstanceId = payload.Identity.Target != null ? payload.Identity.Target.InstanceId : -1,
                GameObjectPath = gameObjectPath ?? string.Empty,
                RendererPath = rendererPath ?? string.Empty,
                Identity = payload.Identity,
                ProviderName = payload.ProviderName,
                Binding = binding,
                WriteConfig = payload.WriteConfig,
                WriteMethod = writeMethod,
                Step = currentStep,
                InspectorDisplayName = BuildDisplayName(payload, binding),
                ValuePreview = preview,
                SubmitLogSummary = BuildLogSummary(submitTrace),
                ValueHash = MatDataTransferTimelineRecord.HashValuePreview(preview)
            });
        }

        private static int ResolveTimelineFrameIndex(ParamTransferPayload payload)
        {
            return payload.SubmitFrameIndex >= 0
                ? payload.SubmitFrameIndex
                : MatDataTransferRuntime.FrameIndex;
        }

        private static string BuildDisplayName(
            ParamTransferPayload payload,
            ResolvedMaterialBinding binding)
        {
            string semanticKey = !string.IsNullOrEmpty(binding.MatchedSemanticKey)
                ? binding.MatchedSemanticKey
                : payload.Identity.SemanticKey;
            return string.IsNullOrEmpty(semanticKey)
                ? payload.Identity.SourceId
                : semanticKey;
        }

        private static string BuildLogSummary(ParamSubmitTrace submitTrace)
        {
            if (submitTrace == null || submitTrace.Steps == null || submitTrace.Steps.Count == 0)
                return string.Empty;

            const int maxLogCount = 5;
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            int count = Mathf.Min(maxLogCount, submitTrace.Steps.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(" > ");

                builder.Append(submitTrace.Steps[i].Stage);
            }

            if (submitTrace.Steps.Count > maxLogCount)
                builder.Append(" > ...");

            return builder.ToString();
        }

        private static string ResolveGameObjectPath(
            ParamTransferPayload payload,
            string gameObjectPath)
        {
            if (!string.IsNullOrEmpty(gameObjectPath))
                return gameObjectPath;

            return payload.Identity.Target != null
                ? MatDataTransferFeature.GetTransformPath(payload.Identity.Target.transform)
                : string.Empty;
        }

        private static string BuildRendererPath(Renderer renderer)
        {
            return renderer != null
                ? MatDataTransferFeature.GetTransformPath(renderer.transform)
                : string.Empty;
        }

        private static void WriteConsoleStep(ParamSubmitStep step)
        {
            if (step == null)
                return;

            if (step.Status == ParamWriteStatus.Rejected
                || step.Status == ParamWriteStatus.WriterFailed)
            {
                MatDataTransferLogger.LogWarning(
                    step.Stage + " " + step.Code + ": " + step.Message);
            }
        }
    }
}
