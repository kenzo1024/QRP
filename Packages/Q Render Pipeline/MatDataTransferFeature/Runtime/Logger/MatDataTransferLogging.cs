using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public class MatDataTransferLogging : IMatDataTransferLogging, IDisposable
    {
        private const int MaxEditorTimelineRecords = 10000;
        private const int MaxFileRecordsPerSession = 10000;
        private const string FileDirectoryName = "MatDataTransferLogs";

        private readonly List<MatDataTransferTimelineRecord> m_TimelineRecords = new List<MatDataTransferTimelineRecord>();
        private readonly List<MatDataTransferTimelineRecord> m_FrameTimelineRecords = new List<MatDataTransferTimelineRecord>();
        private readonly List<ParamWriteResult> m_FrameReceipts = new List<ParamWriteResult>();
        private readonly MatDataTransferFileLogWriter m_FileWriter = new MatDataTransferFileLogWriter();
        private int m_TimelineVersion;

        public bool EnableLogging;
        public bool AllowReleaseFileLogging;

        public IReadOnlyList<MatDataTransferTimelineRecord> TimelineRecords => m_TimelineRecords;
        public IReadOnlyList<ParamWriteResult> LastReceipts => m_FrameReceipts;
        public int TimelineVersion => m_TimelineVersion;
        public string CurrentLogFilePath => m_FileWriter.FilePath;

        public List<MatDataTransferTimelineRecord> GetTimelineList()
        {
            return IsTimelineCaptureEnabled() ? m_FrameTimelineRecords : null;
        }

        public List<ParamWriteResult> GetReceiptList() => m_FrameReceipts;

        internal void TraceSubmit(
            ref MaterialParameterSubmitPayload payload,
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            if (payload.Result == null)
                payload.Result = new MaterialParameterSubmitResult();

            payload.Result.AddTrace(stage, type, code, message);
            WriteConsoleTrace(stage, type, code, message);
        }

        public void BeginFrame()
        {
            m_FrameReceipts.Clear();
            m_FrameTimelineRecords.Clear();
        }

        public void CompleteFrame()
        {
            if (!EnableLogging)
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

        internal void RecordSubmitResult(
            MaterialParameterSubmitPayload payload,
            string gameObjectPath)
        {
            BeginFrame();
            RecordResult(
                payload,
                default,
                ParamWriteMethod.None,
                payload.Result,
                gameObjectPath,
                string.Empty);
            CompleteFrame();
        }

        internal void RecordWriteResult(
            MaterialWriteCommand command,
            ParamWriteMethod writeMethod,
            MaterialParameterSubmitPayload payload)
        {
            RecordResult(
                payload,
                command.BindingResolution,
                writeMethod,
                payload.Result,
                command.GameObjectPath,
                BuildRendererPath(command.Renderer));
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
            return EnableLogging
                && (IsEditorTimelineEnabled() || IsFileTimelineAllowed());
        }

        private void AppendEditorTimeline()
        {
            if (!IsEditorTimelineEnabled())
            {
                ClearEditorTimelineRecords();
                return;
            }

            m_TimelineRecords.AddRange(m_FrameTimelineRecords);
            int overflow = m_TimelineRecords.Count - MaxEditorTimelineRecords;
            if (overflow > 0)
                m_TimelineRecords.RemoveRange(0, overflow);

            m_TimelineVersion++;
        }

        private void ClearEditorTimelineRecords()
        {
            if (m_TimelineRecords.Count == 0 && m_FrameTimelineRecords.Count == 0)
                return;

            m_TimelineRecords.Clear();
            m_FrameTimelineRecords.Clear();
            m_TimelineVersion++;
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

            return AllowReleaseFileLogging && MatDataTransferLogger.RuntimeFileOutputEnabled;
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
            MaterialParameterSubmitPayload payload,
            ResolvedMaterialBinding binding,
            ParamWriteMethod writeMethod,
            MaterialParameterSubmitResult submitResult,
            string gameObjectPath,
            string rendererPath)
        {
            ParamWriteResult result = ParamWriteResult.FromPayload(
                payload,
                binding,
                writeMethod,
                submitResult.ResultInfo);
            m_FrameReceipts.Add(result);

            List<MatDataTransferTimelineRecord> timeline = GetTimelineList();
            if (timeline == null)
                return;

            string preview = payload.Value.ToPreview();
            timeline.Add(new MatDataTransferTimelineRecord
            {
                FrameIndex = MatDataTransferRuntime.FrameIndex,
                TimeSinceStartup = MatDataTransferRuntime.TimeSinceStartup,
                Sequence = payload.Sequence,
                InstanceId = payload.InstanceId,
                GameObjectPath = gameObjectPath ?? string.Empty,
                RendererBinding = payload.RendererBinding,
                RendererPath = rendererPath ?? string.Empty,
                Identity = payload.Identity,
                Binding = binding,
                WriteConfig = payload.WriteConfig,
                WriteMethod = writeMethod,
                ResultInfo = submitResult.ResultInfo,
                Status = ToStatus(submitResult.ResultInfo),
                InspectorDisplayName = BuildDisplayName(payload, binding),
                ValuePreview = preview,
                ValueHash = MatDataTransferTimelineRecord.HashValuePreview(preview)
            });
        }

        private static ParamWriteStatus ToStatus(ParamWriteResultInfo resultInfo)
        {
            switch (resultInfo.Type)
            {
                case ParamWriteResultType.Applied:
                    return ParamWriteStatus.Applied;
                case ParamWriteResultType.Overridden:
                    return ParamWriteStatus.Overridden;
                case ParamWriteResultType.WriterFailed:
                    return ParamWriteStatus.WriterFailed;
                case ParamWriteResultType.Rejected:
                    return ParamWriteStatus.Rejected;
                default:
                    return ParamWriteStatus.Submitted;
            }
        }

        private static string BuildDisplayName(
            MaterialParameterSubmitPayload payload,
            ResolvedMaterialBinding binding)
        {
            string semanticKey = !string.IsNullOrEmpty(binding.MatchedSemanticKey)
                ? binding.MatchedSemanticKey
                : payload.Identity.SemanticKey;
            return string.IsNullOrEmpty(semanticKey)
                ? payload.Identity.SourceId
                : semanticKey;
        }

        private static string BuildRendererPath(Renderer renderer)
        {
            return renderer != null
                ? MatDataTransferFeature.GetTransformPath(renderer.transform)
                : string.Empty;
        }

        private static void WriteConsoleTrace(
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            if (type == ParamWriteResultType.Rejected || type == ParamWriteResultType.WriterFailed)
            {
                MatDataTransferLogger.LogWarning(
                    stage + " " + code + ": " + message);
            }
        }
    }
}
