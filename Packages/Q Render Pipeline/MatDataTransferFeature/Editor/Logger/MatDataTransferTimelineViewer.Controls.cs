using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
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
            GUI.Box(new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, 24f), "Search / Semantic / Source", EditorStyles.toolbarTextField);

            Rect chip = new Rect(rect.x + 10f, rect.y + 74f, 50f, 20f);
            DrawFilterChip(chip, "All", StatusColor(ParamWriteStatus.Submitted), TimelineStatusFilter.All);
            chip.x += 56f;
            chip.width = 76f;
            DrawFilterChip(chip, "Applied", StatusColor(ParamWriteStatus.Applied), TimelineStatusFilter.Applied);
            chip = new Rect(rect.x + 10f, rect.y + 98f, 92f, 20f);
            chip.width = 92f;
            DrawFilterChip(chip, "Overridden", StatusColor(ParamWriteStatus.Overridden), TimelineStatusFilter.Overridden);
            chip.x += 100f;
            chip.width = 68f;
            DrawFilterChip(chip, "Failed", StatusColor(ParamWriteStatus.WriterFailed), TimelineStatusFilter.Failed);

            DrawRecordRows(new Rect(rect.x + 10f, rect.y + 132f, rect.width - 20f, rect.height - 262f));
            DrawReplayHelp(new Rect(rect.x + 10f, rect.yMax - 118f, rect.width - 20f, 108f));
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
                EditorGUI.HelpBox(rect, "No records match the selected status filter.", MessageType.Info);
                return;
            }

            Rect view = new Rect(0f, 0f, rect.width - 16f, filteredIndices.Count * 72f + 4f);
            m_RecordScroll = GUI.BeginScrollView(rect, m_RecordScroll, view);
            for (int i = 0; i < filteredIndices.Count; i++)
            {
                int recordIndex = filteredIndices[i];
                DrawRecordRow(
                    new Rect(0f, i * 72f, view.width, 64f),
                    recordIndex,
                    frame.Records[recordIndex]);
            }
            GUI.EndScrollView();
        }

        private void DrawRecordRow(Rect rect, int index, MatDataTransferTimelineRecord record)
        {
            bool selected = IsSelectedRecord(index, record);
            EditorGUI.DrawRect(rect, selected ? new Color(0.23f, 0.31f, 0.42f) : new Color(0.19f, 0.19f, 0.19f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (selected)
                DrawRectOutline(rect, new Color(0.35f, 0.65f, 1f), 2f);

            EditorGUI.DrawRect(new Rect(rect.x + 10f, rect.y + 12f, 10f, 10f), StatusColor(record.Status));
            GUI.Label(new Rect(rect.x + 28f, rect.y + 8f, 72f, 18f), "Seq " + record.Sequence, EditorStyles.boldLabel);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 104f, rect.y + 9f, rect.width - 114f, 16f), record.Status.ToString(), TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 10f, rect.y + 32f, rect.width - 20f, 16f), Safe(record.Identity.SemanticKey), TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 10f, rect.y + 48f, rect.width - 20f, 16f), BuildInstanceLabel(record), TimelineLabel, true);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                SelectRecord(index, record);
                Event.current.Use();
                Repaint();
            }
        }

        private static void DrawReplayHelp(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 12f, rect.width - 20f, 18f), "Replay Meaning", EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, 16f), "Frame Offset 0: latest frame", InspectorStyleLibrary.Description);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 58f, rect.width - 20f, 16f), "Frame Offset 1: previous frame", InspectorStyleLibrary.Description);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 78f, rect.width - 20f, 16f), "Buffer drops frames > max limit", InspectorStyleLibrary.Description);
        }

    }
}
