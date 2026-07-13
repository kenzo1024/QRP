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
                || !PassesRecordFilters(frame.Records[m_SelectedRecordIndex]))
            {
                ClearSelectedRecord();
                return false;
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

            if (hasSelected)
            {
                if (PassesRecordFilters(selected))
                    records.Add(selected);
                return records;
            }

            List<int> filteredIndices = BuildFilteredRecordIndices(frame);
            for (int i = 0; i < filteredIndices.Count; i++)
                records.Add(frame.Records[filteredIndices[i]]);

            return records;
        }

        private List<int> BuildFilteredRecordIndices(MatDataTransferTimelineFrame frame)
        {
            List<int> indices = new List<int>();
            if (frame == null)
                return indices;

            for (int i = 0; i < frame.Records.Count; i++)
            {
                if (PassesRecordFilters(frame.Records[i]))
                    indices.Add(i);
            }

            SortRecordIndices(frame, indices);
            return indices;
        }

        private List<RecordGroup> BuildRecordGroups(MatDataTransferTimelineFrame frame, List<int> filteredIndices)
        {
            List<RecordGroup> groups = new List<RecordGroup>();
            if (frame == null || filteredIndices == null || filteredIndices.Count == 0)
                return groups;

            if (m_RecordGroupMode == TimelineGroupMode.None)
            {
                RecordGroup group = new RecordGroup { Title = "Records", Summary = filteredIndices.Count + " records" };
                group.Indices.AddRange(filteredIndices);
                groups.Add(group);
                return groups;
            }

            Dictionary<string, int> lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < filteredIndices.Count; i++)
            {
                int index = filteredIndices[i];
                MatDataTransferTimelineRecord record = frame.Records[index];
                string title = BuildGroupTitle(record);
                string key = title ?? string.Empty;
                if (!lookup.TryGetValue(key, out int groupIndex))
                {
                    groupIndex = groups.Count;
                    lookup.Add(key, groupIndex);
                    groups.Add(new RecordGroup { Title = title });
                }

                groups[groupIndex].Indices.Add(index);
            }

            for (int i = 0; i < groups.Count; i++)
            {
                RecordGroup group = groups[i];
                group.Summary = BuildGroupSummary(frame, group.Indices);
                groups[i] = group;
            }

            return groups;
        }

        private bool PassesRecordFilters(MatDataTransferTimelineRecord record)
        {
            return PassesStatusFilter(record) && PassesSearchFilter(record);
        }

        private bool PassesStatusFilter(MatDataTransferTimelineRecord record)
        {
            TimelineStatusFilter flag = StatusToFilter(record.Status);
            return (m_StatusFilter & flag) != 0;
        }

        private bool PassesSearchFilter(MatDataTransferTimelineRecord record)
        {
            if (string.IsNullOrWhiteSpace(m_RecordSearch))
                return true;

            string[] tokens = m_RecordSearch.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!PassesSearchToken(record, tokens[i]))
                    return false;
            }

            return true;
        }

        private bool PassesSearchToken(MatDataTransferTimelineRecord record, string token)
        {
            int separator = token.IndexOf(':');
            if (separator <= 0 || separator >= token.Length - 1)
                return ContainsIgnoreCase(BuildSearchBlob(record), token);

            string key = token.Substring(0, separator).Trim();
            string value = token.Substring(separator + 1).Trim();
            if (string.IsNullOrEmpty(value))
                return true;

            switch (key.ToLowerInvariant())
            {
                case "key":
                case "semantic":
                    return ContainsIgnoreCase(Safe(record.Identity.SemanticKey), value)
                        || ContainsIgnoreCase(Safe(record.Binding.MatchedSemanticKey), value);
                case "source":
                    return ContainsIgnoreCase(Safe(record.Identity.SourceId), value);
                case "provider":
                    return ContainsIgnoreCase(Safe(record.ProviderName), value);
                case "instance":
                    return ContainsIgnoreCase(record.InstanceId.ToString(), value)
                        || ContainsIgnoreCase(BuildInstanceLabel(record), value);
                case "renderer":
                    return ContainsIgnoreCase(record.Identity.Binding.RendererId.ToString(), value)
                        || ContainsIgnoreCase(Safe(record.RendererPath), value)
                        || ContainsIgnoreCase(Safe(record.Identity.Binding.RendererPathId), value);
                case "slot":
                    return ContainsIgnoreCase(record.Identity.Binding.MaterialSlot.ToString(), value);
                case "shader":
                    return ContainsIgnoreCase(Safe(record.Binding.ShaderName), value);
                case "property":
                case "prop":
                    return ContainsIgnoreCase(Safe(record.Binding.PropertyName), value)
                        || ContainsIgnoreCase(record.Binding.PropertyId.ToString(), value);
                case "stage":
                    return ContainsIgnoreCase(GetStage(record), value);
                case "code":
                    return ContainsIgnoreCase(GetResultCode(record), value);
                case "msg":
                case "message":
                    return ContainsIgnoreCase(GetMessage(record), value);
                case "seq":
                    return ContainsIgnoreCase(record.Sequence.ToString(), value);
                case "status":
                    return ContainsIgnoreCase(record.Status.ToString(), value)
                        || (IsFailedStatus(record.Status) && ContainsIgnoreCase("failed", value));
                case "value":
                    return ContainsIgnoreCase(Safe(record.ValuePreview), value)
                        || ContainsIgnoreCase(record.ValueHash.ToString("X16"), value);
                case "path":
                    return ContainsIgnoreCase(Safe(record.GameObjectPath), value)
                        || ContainsIgnoreCase(Safe(record.RendererPath), value);
                default:
                    return ContainsIgnoreCase(BuildSearchBlob(record), value);
            }
        }

        private void SortRecordIndices(MatDataTransferTimelineFrame frame, List<int> indices)
        {
            indices.Sort((left, right) => CompareRecords(frame.Records[left], frame.Records[right]));
        }

        private int CompareRecords(MatDataTransferTimelineRecord left, MatDataTransferTimelineRecord right)
        {
            int result;
            switch (m_RecordSortMode)
            {
                case TimelineSortMode.Status:
                    result = StatusSortRank(left.Status).CompareTo(StatusSortRank(right.Status));
                    break;
                case TimelineSortMode.SemanticKey:
                    result = CompareText(left.Identity.SemanticKey, right.Identity.SemanticKey);
                    break;
                case TimelineSortMode.Source:
                    result = CompareText(left.Identity.SourceId, right.Identity.SourceId);
                    break;
                case TimelineSortMode.Instance:
                    result = left.InstanceId.CompareTo(right.InstanceId);
                    break;
                case TimelineSortMode.Property:
                    result = CompareText(left.Binding.PropertyName, right.Binding.PropertyName);
                    break;
                case TimelineSortMode.Sequence:
                default:
                    result = left.Sequence.CompareTo(right.Sequence);
                    break;
            }

            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        }

        private static int CompareText(string left, string right)
        {
            return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static int StatusSortRank(ParamWriteStatus status)
        {
            switch (status)
            {
                case ParamWriteStatus.Rejected:
                case ParamWriteStatus.WriterFailed:
                    return 0;
                case ParamWriteStatus.Overridden:
                    return 1;
                case ParamWriteStatus.Applied:
                    return 2;
                case ParamWriteStatus.Queued:
                    return 3;
                case ParamWriteStatus.Submitted:
                default:
                    return 4;
            }
        }

        private string BuildGroupTitle(MatDataTransferTimelineRecord record)
        {
            switch (m_RecordGroupMode)
            {
                case TimelineGroupMode.Status:
                    return record.Status.ToString();
                case TimelineGroupMode.SemanticKey:
                    return Safe(record.Identity.SemanticKey);
                case TimelineGroupMode.Source:
                    return Safe(record.Identity.SourceId);
                case TimelineGroupMode.Instance:
                    return BuildInstanceLabel(record);
                case TimelineGroupMode.Renderer:
                    return "#" + record.Identity.Binding.RendererId + " / Slot " + record.Identity.Binding.MaterialSlot;
                case TimelineGroupMode.Property:
                    return Safe(record.Binding.PropertyName);
                case TimelineGroupMode.ResultCode:
                    return GetResultCode(record);
                case TimelineGroupMode.None:
                default:
                    return "Records";
            }
        }

        private static string BuildGroupSummary(MatDataTransferTimelineFrame frame, List<int> indices)
        {
            int failed = 0;
            int applied = 0;
            int overridden = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                ParamWriteStatus status = frame.Records[indices[i]].Status;
                if (IsFailedStatus(status))
                    failed++;
                else if (status == ParamWriteStatus.Applied)
                    applied++;
                else if (status == ParamWriteStatus.Overridden)
                    overridden++;
            }

            if (failed > 0)
                return indices.Count + " / " + failed + " failed";
            if (overridden > 0)
                return indices.Count + " / " + overridden + " overridden";
            if (applied > 0)
                return indices.Count + " / " + applied + " applied";
            return indices.Count + " records";
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
            if (m_SelectedRecordIndex < 0
                || m_SelectedRecordIndex >= frame.Records.Count
                || !PassesRecordFilters(frame.Records[m_SelectedRecordIndex]))
            {
                ClearSelectedRecord();
            }
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
                Binding = new ParamBindingResolution(
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

        private static string ShortText(string text, int maxLength)
        {
            text = Safe(text);
            return text.Length <= maxLength ? text : text.Substring(0, Mathf.Max(0, maxLength - 3)) + "...";
        }

        private static string GetStage(MatDataTransferTimelineRecord record)
        {
            return record.Step != null ? Safe(record.Step.Stage) : "<none>";
        }

        private static string GetResultCode(MatDataTransferTimelineRecord record)
        {
            return record.Step != null ? record.Step.Code.ToString() : ParamWriteResultCode.None.ToString();
        }

        private static string GetMessage(MatDataTransferTimelineRecord record)
        {
            return record.Step != null ? Safe(record.Step.Message) : "<empty>";
        }

        private static string BuildSearchBlob(MatDataTransferTimelineRecord record)
        {
            return Safe(record.Identity.SemanticKey)
                + " " + Safe(record.Binding.MatchedSemanticKey)
                + " " + Safe(record.Identity.SourceId)
                + " " + Safe(record.ProviderName)
                + " " + Safe(record.GameObjectPath)
                + " " + Safe(record.RendererPath)
                + " " + Safe(record.Binding.ShaderName)
                + " " + Safe(record.Binding.CatalogName)
                + " " + Safe(record.Binding.PropertyName)
                + " " + record.Binding.PropertyId
                + " " + record.Sequence
                + " " + record.InstanceId
                + " " + record.Identity.Binding.RendererId
                + " " + record.Identity.Binding.MaterialSlot
                + " " + record.Status
                + " " + GetStage(record)
                + " " + GetResultCode(record)
                + " " + GetMessage(record)
                + " " + Safe(record.ValuePreview);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return (text ?? string.Empty).IndexOf(value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
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
                    return new Color(0.48f, 0.38f, 0.86f);
                case ParamWriteStatus.Submitted:
                default:
                    return new Color(0.35f, 0.64f, 1f);
            }
        }

        private static TimelineStatusFilter StatusToFilter(ParamWriteStatus status)
        {
            switch (status)
            {
                case ParamWriteStatus.Queued:
                    return TimelineStatusFilter.Queued;
                case ParamWriteStatus.Applied:
                    return TimelineStatusFilter.Applied;
                case ParamWriteStatus.Overridden:
                    return TimelineStatusFilter.Overridden;
                case ParamWriteStatus.Rejected:
                    return TimelineStatusFilter.Rejected;
                case ParamWriteStatus.WriterFailed:
                    return TimelineStatusFilter.WriterFailed;
                case ParamWriteStatus.Submitted:
                default:
                    return TimelineStatusFilter.Submitted;
            }
        }

        private static bool IsFailedStatus(ParamWriteStatus status)
        {
            return status == ParamWriteStatus.Rejected
                || status == ParamWriteStatus.WriterFailed;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }

        private sealed class RecordGroup
        {
            public string Title;
            public string Summary;
            public readonly List<int> Indices = new List<int>();
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

        [Flags]
        private enum TimelineStatusFilter
        {
            Submitted = 1 << 0,
            Queued = 1 << 1,
            Applied = 1 << 2,
            Overridden = 1 << 3,
            Rejected = 1 << 4,
            WriterFailed = 1 << 5,
            All = Submitted | Queued | Applied | Overridden | Rejected | WriterFailed
        }

        private enum TimelineSortMode
        {
            Sequence,
            Status,
            SemanticKey,
            Source,
            Instance,
            Property
        }

        private enum TimelineGroupMode
        {
            None,
            Status,
            SemanticKey,
            Source,
            Instance,
            Renderer,
            Property,
            ResultCode
        }

        private enum LabelLane
        {
            Above,
            Below
        }
    }
}
