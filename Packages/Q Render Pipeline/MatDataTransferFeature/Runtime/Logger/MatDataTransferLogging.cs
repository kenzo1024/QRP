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
        internal bool IsEnabled => m_EnableLogging;

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
            bool wasEnabled = m_EnableLogging;
            m_EnableLogging = settings != null && settings.EnableLogging;
            m_AllowReleaseFileLogging = settings != null && settings.AllowReleaseFileLogging;
            Configure(settings != null ? settings.MaxTimelineFrames : DefaultMaxTimelineFramesValue);

            if (wasEnabled && !m_EnableLogging)
                DisableCapture();
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

        internal static void RecordSubmitStep(
            ref ParamTransferPayload payload,
            ParamSubmitStage stage,
            ParamWriteStatus status,
            ParamWriteResultCode code = ParamWriteResultCode.None,
            string message = null)
        {
            if (payload.Trace == null)
                payload.Trace = new ParamSubmitTrace();

            Instance.RecordTraceStepInternal(
                payload.Trace,
                stage,
                status,
                code,
                message,
                null);
        }

        internal static void RecordTraceStep(
            ParamSubmitTrace trace,
            ParamSubmitStage stage,
            ParamWriteStatus status,
            ParamWriteResultCode code = ParamWriteResultCode.None,
            string message = null)
        {
            Instance.RecordTraceStepInternal(
                trace,
                stage,
                status,
                code,
                message,
                null);
        }

        internal static void RecordScopeExpanded(
            ParamSubmitTrace trace,
            int submittedCount,
            int skippedCount)
        {
            trace?.UpdateSummary(
                ParamWriteStatus.Queued,
                ParamWriteResultCode.None);

            if (!Instance.m_EnableLogging || trace == null)
                return;

            Instance.AddDetailedStep(
                trace,
                ParamSubmitStage.ScopeExpand,
                ParamWriteStatus.Queued,
                ParamWriteResultCode.None,
                $"Scope expanded: {submittedCount} submitted, {skippedCount} skipped.",
                null);
        }

        internal static int AddRequestDiagnostic(
            ref ParamTransferPayload payload,
            ParamBindingResolution bindingResolution,
            List<RequestDiagnosticContext> diagnostics)
        {
            if (!Instance.m_EnableLogging || diagnostics == null)
                return -1;

            int diagnosticIndex = diagnostics.Count;
            diagnostics.Add(new RequestDiagnosticContext(payload, bindingResolution));
            return diagnosticIndex;
        }

        internal static void CaptureSubmitSnapshot(ref ParamTransferPayload payload)
        {
            if (!Instance.m_EnableLogging)
                return;

            using (MatDataTransferProfiling.LoggingCapture.Auto())
            {
                Instance.CaptureSubmitSnapshotInternal(
                    ref payload,
                    default,
                    ParamWriteMethod.None,
                    string.Empty,
                    string.Empty);
            }
        }

        internal static void RecordWrite(
            ParamSubmitTrace trace,
            int diagnosticIndex,
            IReadOnlyList<RequestDiagnosticContext> diagnostics,
            Renderer renderer,
            ParamWriteMethod writeMethod,
            string failureReason)
        {
            MatDataTransferLogging logging = Instance;
            bool isEnabled = logging.m_EnableLogging;
            if (trace == null && !isEnabled)
                return;

            bool hasDiagnostic = false;
            RequestDiagnosticContext diagnostic = default;
            if (trace == null && isEnabled)
            {
                hasDiagnostic = TryGetDiagnostic(
                    diagnosticIndex,
                    diagnostics,
                    out diagnostic);
                if (hasDiagnostic)
                    trace = diagnostic.Payload.Trace;
            }

            ParamWriteStatus status = writeMethod == ParamWriteMethod.None
                ? ParamWriteStatus.WriterFailed
                : ParamWriteStatus.Applied;
            ParamWriteResultCode code = writeMethod == ParamWriteMethod.None
                ? ParamWriteResultCode.WriterFailed
                : ParamWriteResultCode.None;
            string message = writeMethod == ParamWriteMethod.None
                ? (string.IsNullOrEmpty(failureReason) ? "WriterFailed" : failureReason)
                : "Write applied.";
            trace?.UpdateSummary(status, code, message);

            if (!isEnabled)
                return;

            if (!hasDiagnostic
                && !TryGetDiagnostic(diagnosticIndex, diagnostics, out diagnostic))
                return;
            if (trace == null)
                return;

            ParamTransferPayload payload = diagnostic.Payload;
            payload.Trace = trace;
            logging.AddDetailedStep(
                trace,
                ParamSubmitStage.WriteApply,
                status,
                code,
                message,
                null);

            using (MatDataTransferProfiling.LoggingCapture.Auto())
            {
                logging.CaptureSubmitSnapshotInternal(
                    ref payload,
                    diagnostic.BindingResolution,
                    writeMethod,
                    string.Empty,
                    BuildRendererPath(renderer));
            }
        }

        internal static void RecordResolvedSnapshot(
            ref ParamTransferPayload payload,
            ParamBindingResolution binding,
            string overriddenBySourceId,
            Renderer renderer)
        {
            if (payload.Trace == null)
                payload.Trace = new ParamSubmitTrace();

            if (!Instance.RecordTraceStepInternal(
                    payload.Trace,
                    ParamSubmitStage.ResolveConflict,
                    ParamWriteStatus.Overridden,
                    ParamWriteResultCode.OverriddenByStrongerRequest,
                    "OverriddenByStrongerRequest",
                    overriddenBySourceId))
                return;

            using (MatDataTransferProfiling.LoggingCapture.Auto())
            {
                Instance.CaptureSubmitSnapshotInternal(
                    ref payload,
                    binding,
                    ParamWriteMethod.None,
                    string.Empty,
                    BuildRendererPath(renderer));
            }
        }

        internal static void RecordOverriddenSummary(ParamSubmitTrace trace)
        {
            trace?.UpdateSummary(
                ParamWriteStatus.Overridden,
                ParamWriteResultCode.OverriddenByStrongerRequest,
                "OverriddenByStrongerRequest");
        }

        private bool RecordTraceStepInternal(
            ParamSubmitTrace trace,
            ParamSubmitStage stage,
            ParamWriteStatus status,
            ParamWriteResultCode code,
            string message,
            string overriddenBySourceId)
        {
            trace?.UpdateSummary(status, code, message);
            if (!m_EnableLogging || trace == null)
                return false;

            AddDetailedStep(
                trace,
                stage,
                status,
                code,
                message,
                overriddenBySourceId);
            return true;
        }

        private void AddDetailedStep(
            ParamSubmitTrace trace,
            ParamSubmitStage stage,
            ParamWriteStatus status,
            ParamWriteResultCode code,
            string message,
            string overriddenBySourceId)
        {
            ParamSubmitStep step = new ParamSubmitStep(
                GetStageName(stage),
                status,
                code,
                message,
                overriddenBySourceId);
            trace.AddStep(step);
            WriteConsoleStep(step);
        }

        private void CaptureSubmitSnapshotInternal(
            ref ParamTransferPayload payload,
            ParamBindingResolution binding,
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

        private static bool TryGetDiagnostic(
            int diagnosticIndex,
            IReadOnlyList<RequestDiagnosticContext> diagnostics,
            out RequestDiagnosticContext diagnostic)
        {
            if (diagnostics != null
                && diagnosticIndex >= 0
                && diagnosticIndex < diagnostics.Count)
            {
                diagnostic = diagnostics[diagnosticIndex];
                return true;
            }

            diagnostic = default;
            return false;
        }

        public void BeginFrame()
        {
            if (!m_EnableLogging)
                return;

            m_FrameReceipts.Clear();
            m_FrameTimelineRecords.Clear();
            m_IsFrameOpen = true;
        }

        public void CompleteFrame()
        {
            if (!m_EnableLogging)
                return;

            using (MatDataTransferProfiling.LoggingCommitTimeline.Auto())
                CompleteFrameProfiled();
        }

        private void CompleteFrameProfiled()
        {
            m_IsFrameOpen = false;
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

        private void DisableCapture()
        {
            m_IsFrameOpen = false;
            m_FrameReceipts.Clear();
            m_FrameTimelineRecords.Clear();
            ClearEditorTimelineRecords();
            m_FileWriter.Close();
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
            ParamBindingResolution binding,
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
                ProviderName = string.Empty,
                Binding = binding,
                WriteConfig = payload.WriteConfig,
                WriteMethod = writeMethod,
                Step = currentStep,
                InspectorDisplayName = BuildDisplayName(payload, binding),
                ValuePreview = preview,
                SubmitLogSummary = BuildLogSummary(submitTrace),
                ValueHash = MatDataTransferTimelineRecord.HashValuePreview(preview)
            });
            MatDataTransferProfiling.AddTimelineRecord();
        }

        private static int ResolveTimelineFrameIndex(ParamTransferPayload payload)
        {
            return payload.SubmitFrameIndex >= 0
                ? payload.SubmitFrameIndex
                : MatDataTransferRuntime.FrameIndex;
        }

        private static string BuildDisplayName(
            ParamTransferPayload payload,
            ParamBindingResolution binding)
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

        private static string GetStageName(ParamSubmitStage stage)
        {
            switch (stage)
            {
                case ParamSubmitStage.ScopeBegin:
                    return "Scope.Begin";
                case ParamSubmitStage.ScopeValidate:
                    return "Scope.Validate";
                case ParamSubmitStage.ScopeExpand:
                    return "Scope.Expand";
                case ParamSubmitStage.SubmitBegin:
                    return "Submit.Begin";
                case ParamSubmitStage.SubmitValidate:
                    return "Submit.Validate";
                case ParamSubmitStage.SubmitQueue:
                    return "Submit.Queue";
                case ParamSubmitStage.ResolveConflict:
                    return "Resolve.Conflict";
                case ParamSubmitStage.WriteApply:
                    return "Write.Apply";
                default:
                    return string.Empty;
            }
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
