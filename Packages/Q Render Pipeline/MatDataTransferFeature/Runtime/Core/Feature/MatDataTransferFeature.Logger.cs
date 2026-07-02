using System;
using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private readonly MatDataTransferLogging m_Logging = new MatDataTransferLogging();

        public IMatDataTransferLogging Logging => m_Logging;
        public IReadOnlyList<MatDataTransferTimelineRecord> TimelineRecords => m_Logging.TimelineRecords;
        public IReadOnlyList<ParamWriteResult> LastReceipts => m_Logging.LastReceipts;

        private void ApplyLoggerSettings()
        {
            m_Logging.EnableLogging = m_LoggingSettings.EnableLogging;
            m_Logging.AllowReleaseFileLogging = m_LoggingSettings.AllowReleaseFileLogging;
            MatDataTransferLogger.ConfigureUnityConsole(true);
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void DisposeLogger()
        {
            m_Logging.Dispose();
        }

        internal void RecordSubmitResult(
            MaterialParameterSubmitPayload payload,
            string gameObjectPath)
        {
            m_Logging.RecordSubmitResult(payload, gameObjectPath);
        }

        internal void TraceSubmit(
            ref MaterialParameterSubmitPayload payload,
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            m_Logging.TraceSubmit(ref payload, stage, type, code, message);
        }
    }
}
