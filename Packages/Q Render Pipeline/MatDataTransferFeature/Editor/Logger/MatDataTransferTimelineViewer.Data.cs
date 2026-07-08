using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
        private void OpenLogFile()
        {
            string path = EditorUtility.OpenFilePanel("Open MatDataTransfer Timeline", Application.persistentDataPath, "jsonl");
            if (!string.IsNullOrEmpty(path))
                LoadLogFile(path);
        }

        private void LoadLiveTimeline()
        {
            IMatDataTransferLogging logging = MatDataTransferLogging.Instance;
            int version = logging != null ? logging.TimelineVersion : -1;
            if (version == m_LiveTimelineVersion)
                return;

            m_UseLiveTimeline = true;
            m_LoadedPath = null;
            m_Frames.Clear();
            if (logging?.TimelineFrames != null)
            {
                for (int i = 0; i < logging.TimelineFrames.Count; i++)
                    m_Frames.Add(logging.TimelineFrames[i]);
            }

            m_LiveTimelineVersion = version;
            ClampSelection();
            Repaint();
        }

        private void LoadLogFile(string path)
        {
            m_UseLiveTimeline = false;
            m_LoadedPath = path;
            m_Frames.Clear();
            m_FileFrameLookup.Clear();
            ClearSelectedRecord();
            m_FrameOffset = 0;

            try
            {
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
                {
                    while (!reader.EndOfStream)
                        AddFileLine(reader.ReadLine());
                }
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("MatDataTransfer Timeline", "Failed to load timeline:\n" + exception.Message, "OK");
            }

            m_Frames.Sort((left, right) => right.FrameIndex.CompareTo(left.FrameIndex));
            ClampSelection();
            Repaint();
        }

        private void AddFileLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            MatDataTransferTimelineLogLine logLine = JsonUtility.FromJson<MatDataTransferTimelineLogLine>(line);
            if (logLine == null || !string.Equals(logLine.Schema, "MatDataTransferTimeline.v1", StringComparison.Ordinal))
                return;

            MatDataTransferTimelineRecord record = CreateRecordFromLogLine(logLine);
            if (!m_FileFrameLookup.TryGetValue(record.FrameIndex, out int index))
            {
                index = m_Frames.Count;
                m_FileFrameLookup.Add(record.FrameIndex, index);
                m_Frames.Add(new MatDataTransferTimelineFrame(record.FrameIndex, record.TimeSinceStartup, null));
            }

            m_Frames[index].Records.Add(record);
        }

        private void RepaintWhenLiveTimelineChanged()
        {
            if (!m_UseLiveTimeline)
                return;

            IMatDataTransferLogging logging = MatDataTransferLogging.Instance;
            int version = logging != null ? logging.TimelineVersion : -1;
            if (version != m_LiveTimelineVersion)
                Repaint();
        }

        private void ClearLiveTimeline()
        {
            MatDataTransferLogging.Instance.ClearTimelineRecords();
            m_Frames.Clear();
            m_LiveTimelineVersion = -1;
            ClearSelectedRecord();
            Repaint();
        }

        private void SetFrameOffset(int offset)
        {
            int next = Mathf.Clamp(offset, 0, GetMaxFrameOffset());
            if (next == m_FrameOffset)
                return;

            m_FrameOffset = next;
            ClearSelectedRecord();
        }

        private int GetMaxFrameOffset()
        {
            return Mathf.Max(0, m_Frames.Count - 1);
        }

        private int GetMaxRecordedFrames()
        {
            IMatDataTransferLogging logging = MatDataTransferLogging.Instance;
            return logging != null ? logging.MaxTimelineFrames : DefaultFileMaxFrames;
        }

        private MatDataTransferTimelineFrame GetSelectedFrame()
        {
            if (m_Frames.Count == 0)
                return null;

            m_FrameOffset = Mathf.Clamp(m_FrameOffset, 0, m_Frames.Count - 1);
            return m_Frames[m_FrameOffset];
        }

        private bool TryGetSelectedRecord(MatDataTransferTimelineFrame frame, out MatDataTransferTimelineRecord record)
        {
            record = default;
            if (frame == null || frame.Records.Count == 0)
                return false;

            RestoreSelectedRecordIndex(frame);
            if (m_SelectedRecordIndex < 0
                || m_SelectedRecordIndex >= frame.Records.Count
                || !PassesStatusFilter(frame.Records[m_SelectedRecordIndex]))
            {
                List<int> indices = BuildFilteredRecordIndices(frame);
                if (indices.Count == 0)
                    return false;

                int firstIndex = indices[0];
                SelectRecord(firstIndex, frame.Records[firstIndex]);
            }

            record = frame.Records[Mathf.Clamp(m_SelectedRecordIndex, 0, frame.Records.Count - 1)];
            return true;
        }

        private List<MatDataTransferTimelineRecord> BuildVisibleRecords(
            MatDataTransferTimelineFrame frame,
            bool hasSelected,
            MatDataTransferTimelineRecord selected)
        {
            List<MatDataTransferTimelineRecord> records = new List<MatDataTransferTimelineRecord>();
            if (frame == null)
                return records;

            if (!m_ShowAllRecords)
            {
                if (hasSelected && PassesStatusFilter(selected))
                    records.Add(selected);
                return records;
            }

            for (int i = 0; i < frame.Records.Count && records.Count < 24; i++)
            {
                MatDataTransferTimelineRecord record = frame.Records[i];
                if (PassesStatusFilter(record))
                    records.Add(record);
            }
            return records;
        }

        private static List<InstanceBucket> BuildInstanceBuckets(List<MatDataTransferTimelineRecord> records)
        {
            List<InstanceBucket> buckets = new List<InstanceBucket>();
            for (int i = 0; i < records.Count; i++)
            {
                MatDataTransferTimelineRecord record = records[i];
                int index = FindBucketIndex(buckets, record.InstanceId);
                if (index < 0)
                {
                    buckets.Add(new InstanceBucket
                    {
                        InstanceId = record.InstanceId,
                        Name = "Instance " + (buckets.Count + 1),
                        Path = BuildInstanceLabel(record),
                        Count = 1
                    });
                }
                else
                {
                    InstanceBucket bucket = buckets[index];
                    bucket.Count++;
                    buckets[index] = bucket;
                }
            }

            if (buckets.Count == 0)
            {
                buckets.Add(new InstanceBucket
                {
                    InstanceId = -1,
                    Name = "Instance",
                    Path = "<empty>",
                    Count = 0
                });
            }

            while (buckets.Count > 4)
                buckets.RemoveAt(buckets.Count - 1);
            return buckets;
        }

        private static int GetInstanceBucketIndex(List<MatDataTransferTimelineRecord> records, int instanceId)
        {
            return GetInstanceBucketIndex(BuildInstanceBuckets(records), instanceId);
        }

        private static int GetInstanceBucketIndex(List<InstanceBucket> buckets, int instanceId)
        {
            int index = FindBucketIndex(buckets, instanceId);
            return Mathf.Max(0, index);
        }

        private static int FindBucketIndex(List<InstanceBucket> buckets, int instanceId)
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].InstanceId == instanceId)
                    return i;
            }

            return -1;
        }

        private List<int> BuildFilteredRecordIndices(MatDataTransferTimelineFrame frame)
        {
            List<int> indices = new List<int>();
            if (frame == null)
                return indices;

            for (int i = 0; i < frame.Records.Count; i++)
            {
                if (PassesStatusFilter(frame.Records[i]))
                    indices.Add(i);
            }

            return indices;
        }

        private bool PassesStatusFilter(MatDataTransferTimelineRecord record)
        {
            switch (m_StatusFilter)
            {
                case TimelineStatusFilter.Applied:
                    return record.Status == ParamWriteStatus.Applied;
                case TimelineStatusFilter.Overridden:
                    return record.Status == ParamWriteStatus.Overridden;
                case TimelineStatusFilter.Failed:
                    return record.Status == ParamWriteStatus.Rejected
                        || record.Status == ParamWriteStatus.WriterFailed;
                case TimelineStatusFilter.All:
                default:
                    return true;
            }
        }

        private void SelectRecord(int index, MatDataTransferTimelineRecord record)
        {
            m_SelectedRecordIndex = index;
            m_SelectedRecordKey = BuildRecordKey(record);
        }

        private void ClearSelectedRecord()
        {
            m_SelectedRecordIndex = -1;
            m_SelectedRecordKey = null;
        }

        private bool IsSelectedRecord(int index, MatDataTransferTimelineRecord record)
        {
            if (m_SelectedRecordIndex >= 0)
                return index == m_SelectedRecordIndex;

            return !string.IsNullOrEmpty(m_SelectedRecordKey)
                && string.Equals(BuildRecordKey(record), m_SelectedRecordKey, StringComparison.Ordinal);
        }

        private void RestoreSelectedRecordIndex(MatDataTransferTimelineFrame frame)
        {
            if (frame == null || string.IsNullOrEmpty(m_SelectedRecordKey))
                return;

            for (int i = 0; i < frame.Records.Count; i++)
            {
                if (string.Equals(BuildRecordKey(frame.Records[i]), m_SelectedRecordKey, StringComparison.Ordinal))
                {
                    m_SelectedRecordIndex = i;
                    return;
                }
            }
        }

        private void ClampSelection()
        {
            m_FrameOffset = Mathf.Clamp(m_FrameOffset, 0, GetMaxFrameOffset());
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            if (frame == null || frame.Records.Count == 0)
            {
                ClearSelectedRecord();
                return;
            }

            RestoreSelectedRecordIndex(frame);
            if (m_SelectedRecordIndex < 0 || m_SelectedRecordIndex >= frame.Records.Count)
                SelectRecord(0, frame.Records[0]);
        }

        private static MatDataTransferTimelineRecord CreateRecordFromLogLine(MatDataTransferTimelineLogLine line)
        {
            return new MatDataTransferTimelineRecord
            {
                FrameIndex = line.FrameIndex,
                TimeSinceStartup = line.TimeSinceStartup,
                Sequence = line.Sequence,
                InstanceId = line.InstanceId,
                GameObjectPath = Safe(line.GameObjectPath),
                RendererPath = Safe(line.RendererPath),
                ProviderName = line.ProviderName,
                Identity = CreateIdentityFromLogLine(line),
                Binding = new ResolvedMaterialBinding(
                    line.MatchedSemanticKey,
                    line.ShaderName,
                    line.CatalogName,
                    line.PropertyName,
                    line.PropertyId),
                WriteConfig = new ParamWriteConfig(ParseEnum(line.Layer, ParamWriteLayer.Default), line.Priority),
                WriteMethod = ParseEnum(line.WriteMethod, ParamWriteMethod.None),
                Step = new ParamSubmitStep(
                    line.Stage,
                    ParseEnum(line.Status, ParamWriteStatus.Submitted),
                    ParseEnum(line.ResultCode, ParamWriteResultCode.None),
                    line.Message,
                    line.OverriddenBySourceId),
                InspectorDisplayName = line.InspectorDisplayName,
                ValuePreview = line.ValuePreview,
                SubmitLogSummary = line.SubmitLogSummary,
                ValueHash = ParseHex(line.ValueHash)
            };
        }

        private static ParamRequestIdentity CreateIdentityFromLogLine(MatDataTransferTimelineLogLine line)
        {
            ParamRequestIdentity identity = new ParamRequestIdentity(
                null,
                new MatDataTransferSubmitSource { Id = line.SourceId },
                line.SemanticKey,
                default,
                null);
            identity.Binding = new ParamRendererBinding(line.RendererPathId, line.MaterialTraceId, line.MaterialSlot)
            {
                RendererId = line.RendererId
            };
            return identity;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return string.IsNullOrEmpty(value) || !Enum.TryParse(value, out T result)
                ? fallback
                : result;
        }

        private static ulong ParseHex(string value)
        {
            return string.IsNullOrEmpty(value)
                || !ulong.TryParse(value, NumberStyles.HexNumber, null, out ulong result)
                    ? 0UL
                    : result;
        }

        private static string BuildRecordKey(MatDataTransferTimelineRecord record)
        {
            return record.FrameIndex
                + "|" + record.Sequence
                + "|" + record.InstanceId
                + "|" + record.Identity.Binding.RendererId
                + "|" + record.Identity.Binding.MaterialSlot
                + "|" + Safe(record.Identity.SourceId)
                + "|" + Safe(record.Identity.SemanticKey)
                + "|" + record.Binding.PropertyId
                + "|" + record.WriteMethod;
        }

        private static bool IsSameRecord(
            MatDataTransferTimelineRecord left,
            MatDataTransferTimelineRecord right)
        {
            return string.Equals(BuildRecordKey(left), BuildRecordKey(right), StringComparison.Ordinal);
        }

        private static string BuildInstanceLabel(MatDataTransferTimelineRecord record)
        {
            if (!string.IsNullOrEmpty(record.GameObjectPath))
                return record.GameObjectPath;

            return record.InstanceId >= 0 ? "Instance #" + record.InstanceId : "Instance <none>";
        }

        private static string ShortSemantic(MatDataTransferTimelineRecord record)
        {
            string semantic = Safe(record.Identity.SemanticKey);
            int dot = semantic.LastIndexOf('.');
            if (dot >= 0 && dot < semantic.Length - 1)
                semantic = semantic.Substring(dot + 1);
            return semantic.Length > 18 ? semantic.Substring(0, 17) + "..." : semantic;
        }

        private static Color StatusColor(ParamWriteStatus status)
        {
            switch (status)
            {
                case ParamWriteStatus.Applied:
                    return new Color(0.24f, 0.70f, 0.42f);
                case ParamWriteStatus.Overridden:
                    return new Color(0.83f, 0.57f, 0.22f);
                case ParamWriteStatus.Rejected:
                case ParamWriteStatus.WriterFailed:
                    return new Color(0.82f, 0.35f, 0.34f);
                case ParamWriteStatus.Queued:
                case ParamWriteStatus.Submitted:
                default:
                    return new Color(0.35f, 0.64f, 1f);
            }
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }

        private struct InstanceBucket
        {
            public int InstanceId;
            public string Name;
            public string Path;
            public int Count;
        }

        private struct FlowLayout
        {
            public float Top;
            public Rect Submit;
            public Rect Instance;
            public Rect Feature;
            public Rect Resolve;
            public Rect Writer;
            public Rect Apply;
        }

        private struct FlowNodeRects
        {
            public Rect Submit;
            public Rect Feature;
            public Rect Resolve;
            public Rect Writer;
            public Rect Apply;
            public Rect Conflict;
            public Rect Failed;
            public List<Rect> InstanceNodes;
        }

        private enum TimelineStatusFilter
        {
            All,
            Applied,
            Overridden,
            Failed
        }

        private enum LabelLane
        {
            Above,
            Below
        }
    }
}
