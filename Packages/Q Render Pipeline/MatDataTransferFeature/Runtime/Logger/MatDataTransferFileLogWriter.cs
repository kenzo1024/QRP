using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MatDataTransferFileLogWriter : IDisposable
    {
        private readonly string m_SessionId = Guid.NewGuid().ToString("N");
        private StreamWriter m_Writer;
        private string m_FilePath;
        private string m_DirectoryName;
        private int m_WrittenRecords;
        private bool m_LimitWarningLogged;

        internal string FilePath => m_FilePath ?? string.Empty;

        internal void WriteRecords(
            IReadOnlyList<MatDataTransferTimelineRecord> records,
            string directoryName,
            int maxRecordsPerSession)
        {
            if (records == null || records.Count == 0)
                return;

            EnsureWriter(directoryName);
            if (m_Writer == null)
                return;

            for (int i = 0; i < records.Count; i++)
            {
                if (IsRecordLimitReached(maxRecordsPerSession))
                    break;

                MatDataTransferTimelineLogLine line =
                    MatDataTransferTimelineLogLine.Create(m_SessionId, m_WrittenRecords, records[i]);
                m_Writer.WriteLine(JsonUtility.ToJson(line));
                m_WrittenRecords++;
            }

            m_Writer.Flush();
        }

        internal void Close()
        {
            if (m_Writer == null)
                return;

            m_Writer.Dispose();
            m_Writer = null;
        }

        public void Dispose()
        {
            Close();
        }

        private void EnsureWriter(string directoryName)
        {
            directoryName = NormalizeDirectoryName(directoryName);
            if (m_Writer != null && string.Equals(m_DirectoryName, directoryName, StringComparison.Ordinal))
                return;

            Close();
            m_DirectoryName = directoryName;

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, directoryName);
                Directory.CreateDirectory(directory);
                m_FilePath = Path.Combine(directory, BuildFileName());
                m_Writer = new StreamWriter(m_FilePath, false, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                m_FilePath = string.Empty;
                MatDataTransferLogger.LogError("Timeline file writer failed: " + exception.Message);
            }
        }

        private bool IsRecordLimitReached(int maxRecordsPerSession)
        {
            if (maxRecordsPerSession <= 0 || m_WrittenRecords < maxRecordsPerSession)
                return false;

            if (!m_LimitWarningLogged)
            {
                m_LimitWarningLogged = true;
                MatDataTransferLogger.LogWarning(
                    "Timeline file record limit reached: " + maxRecordsPerSession);
            }

            return true;
        }

        private static string NormalizeDirectoryName(string directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName))
                return "MatDataTransferLogs";

            StringBuilder builder = new StringBuilder(directoryName.Length);
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < directoryName.Length; i++)
            {
                char ch = directoryName[i];
                builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
            }

            return builder.Length > 0 ? builder.ToString() : "MatDataTransferLogs";
        }

        private static string BuildFileName()
        {
            return "mat-data-transfer_"
                + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")
                + ".jsonl";
        }
    }
}
