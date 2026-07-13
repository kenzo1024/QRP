using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
        private const float RecordGroupHeaderHeight = 26f;
        private const float RecordRowHeight = 78f;

        private void DrawToolbar()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, ToolbarHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f));

            Rect buttons = new Rect(rect.x + 8f, rect.y + 7f, 330f, 24f);
            Rect replay = new Rect(buttons.xMax + 14f, rect.y + 7f, rect.xMax - buttons.xMax - 22f, 24f);

            DrawToolbarButtons(buttons);
            DrawReplayControls(replay);
        }

        private void DrawToolbarButtons(Rect rect)
        {
            Rect button = new Rect(rect.x, rect.y, 62f, rect.height);
            bool live = DrawTimelineToggle(button, m_UseLiveTimeline, "Live");
            if (live != m_UseLiveTimeline)
            {
                m_UseLiveTimeline = live;
                m_FrameOffset = 0;
                ClearSelectedRecord();
            }

            button.x += 68f;
            button.width = 96f;
            if (DrawTimelineButton(button, "Open JSONL"))
                OpenLogFile();

            button.x += 102f;
            button.width = 68f;
            using (new EditorGUI.DisabledScope(m_UseLiveTimeline || string.IsNullOrEmpty(m_LoadedPath)))
            {
                if (DrawTimelineButton(button, "Reload"))
                    LoadLogFile(m_LoadedPath);
            }

            button.x += 74f;
            button.width = 56f;
            using (new EditorGUI.DisabledScope(!m_UseLiveTimeline))
            {
                if (DrawTimelineButton(button, "Clear"))
                    ClearLiveTimeline();
            }
        }

        private void DrawReplayControls(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y + 2f, 48f, 18f), "Replay", EditorStyles.miniLabel);
            Rect button = new Rect(rect.x + 58f, rect.y, 28f, rect.height);
            if (DrawTimelineButton(button, "|<"))
                SetFrameOffset(0);
            button.x += 32f;
            if (DrawTimelineButton(button, "<"))
                SetFrameOffset(m_FrameOffset - 1);

            Rect field = new Rect(button.xMax + 8f, rect.y, 126f, rect.height);
            EditorGUI.LabelField(new Rect(field.x, field.y + 3f, 88f, 18f), "Frame Offset", EditorStyles.miniLabel);
            int offset = EditorGUI.IntField(new Rect(field.x + 90f, field.y, 36f, rect.height), m_FrameOffset);
            if (offset != m_FrameOffset)
                SetFrameOffset(offset);

            int maxOffset = GetMaxFrameOffset();
            Rect latest = new Rect(field.xMax + 10f, rect.y + 2f, 58f, 18f);
            Rect oldest = new Rect(rect.xMax - 138f, rect.y + 2f, 70f, 18f);
            Rect slider = new Rect(latest.xMax + 8f, rect.y + 2f, Mathf.Max(80f, oldest.x - latest.xMax - 16f), 18f);
            int sliderOffset = Mathf.RoundToInt(GUI.HorizontalSlider(slider, m_FrameOffset, 0, maxOffset));
            if (sliderOffset != m_FrameOffset)
                SetFrameOffset(sliderOffset);
            InspectorStyleLibrary.DrawTailLabel(latest, "0 Latest", TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(oldest, maxOffset + " Oldest", TimelineRightLabel, false);

            button.x = oldest.xMax + 8f;
            if (DrawTimelineButton(button, ">"))
                SetFrameOffset(m_FrameOffset + 1);
            button.x += 32f;
            if (DrawTimelineButton(button, ">|"))
                SetFrameOffset(maxOffset);
        }

        private void DrawFrameBar()
        {
            Rect bar = GUILayoutUtility.GetRect(0f, FrameBarHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, new Color(0.19f, 0.19f, 0.19f));
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            string title = "Frame " + m_FrameOffset + " = " + (m_FrameOffset == 0 ? "Current Latest Frame" : "History Frame");
            GUI.Label(new Rect(bar.x + 14f, bar.y + 12f, 270f, 20f), title, EditorStyles.boldLabel);

            string status = frame == null
                ? "No timeline records"
                : "Unity Frame " + frame.FrameIndex
                    + "   /   Time " + frame.TimeSinceStartup.ToString("0.000") + "s"
                    + "   /   Records in frame " + frame.Records.Count
                    + "   /   Buffer " + GetMaxRecordedFrames() + " frames";
            GUI.Label(new Rect(bar.x + 292f, bar.y + 13f, 560f, 18f), status, InspectorStyleLibrary.Description);
            DrawFrameHistogram(new Rect(bar.x + 860f, bar.y + 8f, bar.width - 880f, 28f));
        }

        private void DrawFrameHistogram(Rect rect)
        {
            if (m_Frames.Count == 0 || rect.width <= 1f)
                return;

            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            int visible = Mathf.Min(m_Frames.Count, Mathf.Max(1, Mathf.FloorToInt(rect.width / 16f)));
            int maxRecords = 1;
            for (int i = 0; i < visible; i++)
                maxRecords = Mathf.Max(maxRecords, m_Frames[i].Records.Count);

            float barWidth = Mathf.Max(3f, rect.width / visible - 3f);
            for (int i = 0; i < visible; i++)
            {
                float height = Mathf.Lerp(3f, rect.height - 6f, m_Frames[i].Records.Count / (float)maxRecords);
                Rect item = new Rect(rect.x + 2f + i * (barWidth + 3f), rect.yMax - height - 2f, barWidth, height);
                EditorGUI.DrawRect(item, i == m_FrameOffset ? StatusColor(ParamWriteStatus.Submitted) : new Color(0.42f, 0.42f, 0.42f));
                if (Event.current.type == EventType.MouseDown && item.Contains(Event.current.mousePosition))
                {
                    SetFrameOffset(i);
                    Event.current.Use();
                }
            }
        }

        private void DrawMainLayout()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.height <= 1f)
                return;

            Rect sidebar = new Rect(rect.x + 8f, rect.y + 8f, SidebarWidth, rect.height - 16f);
            Rect canvas = new Rect(sidebar.xMax + 8f, rect.y + 8f, rect.xMax - sidebar.xMax - 16f, rect.height - 16f);
            DrawRecordSidebar(sidebar);
            DrawCanvas(canvas);
        }

        private void DrawRecordSidebar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), "Frame " + m_FrameOffset + " Records", EditorStyles.boldLabel);
            DrawRecordSearch(new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, 24f));
            DrawStatusFilters(new Rect(rect.x + 10f, rect.y + 74f, rect.width - 20f, 64f));
            DrawRecordOptions(new Rect(rect.x + 10f, rect.y + 148f, rect.width - 20f, 22f));

            DrawRecordRows(new Rect(rect.x + 10f, rect.y + 180f, rect.width - 20f, rect.height - 308f));
            DrawRecordSummary(new Rect(rect.x + 10f, rect.yMax - 118f, rect.width - 20f, 44f));
            DrawQueryHelp(new Rect(rect.x + 10f, rect.yMax - 66f, rect.width - 20f, 56f));
        }

        private void DrawRecordSearch(Rect rect)
        {
            Rect textRect = new Rect(rect.x, rect.y, Mathf.Max(1f, rect.width), rect.height);
            string next = GUI.TextField(textRect, m_RecordSearch ?? string.Empty, ToolbarSearchField);
            if (next != m_RecordSearch)
            {
                m_RecordSearch = next;
                ClearSelectedRecord();
                m_RecordScroll = Vector2.zero;
            }
        }

        private void DrawStatusFilters(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, 48f, 18f), "Status", EditorStyles.miniLabel);
            Rect chip = new Rect(rect.x, rect.y + 20f, 48f, 20f);
            DrawStatusChip(chip, "All", StatusColor(ParamWriteStatus.Submitted), TimelineStatusFilter.All);
            chip.x += 54f;
            chip.width = 78f;
            DrawStatusChip(chip, "Applied", StatusColor(ParamWriteStatus.Applied), TimelineStatusFilter.Applied);
            chip.x += 84f;
            chip.width = 78f;
            DrawStatusChip(chip, "Rejected", StatusColor(ParamWriteStatus.Rejected), TimelineStatusFilter.Rejected);

            chip = new Rect(rect.x, rect.y + 44f, 92f, 20f);
            DrawStatusChip(chip, "Overridden", StatusColor(ParamWriteStatus.Overridden), TimelineStatusFilter.Overridden);
            chip.x += 98f;
            chip.width = 96f;
            DrawStatusChip(chip, "WriterFailed", StatusColor(ParamWriteStatus.WriterFailed), TimelineStatusFilter.WriterFailed);
        }

        private void DrawRecordOptions(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y + 3f, 28f, 16f), "Sort", EditorStyles.miniLabel);
            TimelineSortMode nextSort = (TimelineSortMode)EditorGUI.EnumPopup(new Rect(rect.x + 32f, rect.y, 82f, rect.height), m_RecordSortMode);
            if (nextSort != m_RecordSortMode)
            {
                m_RecordSortMode = nextSort;
                m_RecordScroll = Vector2.zero;
            }

            GUI.Label(new Rect(rect.x + 122f, rect.y + 3f, 42f, 16f), "Group", EditorStyles.miniLabel);
            TimelineGroupMode nextGroup = (TimelineGroupMode)EditorGUI.EnumPopup(new Rect(rect.x + 168f, rect.y, rect.width - 168f, rect.height), m_RecordGroupMode);
            if (nextGroup != m_RecordGroupMode)
            {
                m_RecordGroupMode = nextGroup;
                m_RecordScroll = Vector2.zero;
            }
        }

        private void DrawRecordRows(Rect rect)
        {
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            if (frame == null || frame.Records.Count == 0)
            {
                EditorGUI.HelpBox(rect, m_UseLiveTimeline
                    ? "Enable logging on MatDataTransferFeature to view the live timeline."
                    : "Open a MatDataTransfer .jsonl timeline file.", MessageType.Info);
                return;
            }

            List<int> filteredIndices = BuildFilteredRecordIndices(frame);
            if (filteredIndices.Count == 0)
            {
                EditorGUI.HelpBox(rect, "No records match the current filters.", MessageType.Info);
                return;
            }

            List<RecordGroup> groups = BuildRecordGroups(frame, filteredIndices);
            float viewHeight = CalculateRecordListHeight(groups);
            Rect view = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            m_RecordScroll = GUI.BeginScrollView(rect, m_RecordScroll, view);
            float y = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                RecordGroup group = groups[i];
                DrawRecordGroupHeader(new Rect(0f, y, view.width, RecordGroupHeaderHeight), group);
                y += RecordGroupHeaderHeight + 6f;
                for (int j = 0; j < group.Indices.Count; j++)
                {
                    int recordIndex = group.Indices[j];
                    DrawRecordRow(
                        new Rect(0f, y, view.width, RecordRowHeight - 8f),
                        recordIndex,
                        frame.Records[recordIndex]);
                    y += RecordRowHeight;
                }
                y += 8f;
            }
            GUI.EndScrollView();
        }

        private static float CalculateRecordListHeight(List<RecordGroup> groups)
        {
            float height = 4f;
            for (int i = 0; i < groups.Count; i++)
                height += RecordGroupHeaderHeight + 14f + groups[i].Indices.Count * RecordRowHeight;
            return Mathf.Max(height, 1f);
        }

        private static void DrawRecordGroupHeader(Rect rect, RecordGroup group)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width * 0.62f, 16f), ShortText(group.Title, 34), EditorStyles.boldLabel);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.xMax - 92f, rect.y + 5f, 86f, 16f), group.Summary, TimelineRightLabel, false);
        }

        private void DrawRecordRow(Rect rect, int index, MatDataTransferTimelineRecord record)
        {
            bool selected = IsSelectedRecord(index, record);
            EditorGUI.DrawRect(rect, selected ? new Color(0.23f, 0.31f, 0.42f) : new Color(0.19f, 0.19f, 0.19f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (selected)
                DrawRectOutline(rect, new Color(0.35f, 0.65f, 1f), 2f);

            EditorGUI.DrawRect(new Rect(rect.x + 10f, rect.y + 12f, 10f, 10f), StatusColor(record.Status));
            GUI.Label(new Rect(rect.x + 28f, rect.y + 8f, 74f, 18f), "Seq " + record.Sequence, EditorStyles.boldLabel);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 112f, rect.y + 9f, rect.width - 122f, 16f), record.Status.ToString(), TimelineRightLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 16f), "Source: " + Safe(record.Identity.SourceId), TimelineLabel, true);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 10f, rect.y + 48f, rect.width - 20f, 16f), "Key: " + Safe(record.Identity.SemanticKey) + " -> " + Safe(record.Binding.PropertyName), TimelineLabel, false);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (selected)
                    ClearSelectedRecord();
                else
                    SelectRecord(index, record);
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawRecordSummary(Rect rect)
        {
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            List<int> filtered = BuildFilteredRecordIndices(frame);
            CountRecords(filtered, frame, out int applied, out int overridden, out int failed, out _);

            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), filtered.Count + " matched", EditorStyles.boldLabel);
            GUI.Label(
                new Rect(rect.x + 10f, rect.y + 26f, rect.width - 20f, 16f),
                (frame != null ? frame.Records.Count : 0) + " total / " + failed + " failed / " + applied + " applied / " + overridden + " overridden",
                InspectorStyleLibrary.Description);
        }

        private static void DrawQueryHelp(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), "Query tips", InspectorStyleLibrary.Description);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, 16f), "key: source: status: code: msg: seq:", InspectorStyleLibrary.Description);
        }

        private static void CountRecords(
            List<int> indices,
            MatDataTransferTimelineFrame frame,
            out int applied,
            out int overridden,
            out int failed,
            out int rejected)
        {
            applied = 0;
            overridden = 0;
            failed = 0;
            rejected = 0;
            if (frame == null || indices == null)
                return;

            for (int i = 0; i < indices.Count; i++)
            {
                ParamWriteStatus status = frame.Records[indices[i]].Status;
                if (status == ParamWriteStatus.Applied)
                    applied++;
                else if (status == ParamWriteStatus.Overridden)
                    overridden++;
                else if (status == ParamWriteStatus.Rejected)
                {
                    rejected++;
                    failed++;
                }
                else if (status == ParamWriteStatus.WriterFailed)
                {
                    failed++;
                }
            }
        }
    }
}
