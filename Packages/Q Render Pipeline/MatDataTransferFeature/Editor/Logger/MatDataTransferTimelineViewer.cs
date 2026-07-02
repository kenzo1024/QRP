using System;
using System.Collections.Generic;
using System.IO;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed class MatDataTransferTimelineViewer : EditorWindow
    {
        private const float SidebarWidth = 320f;
        private const float NodeWidth = 190f;
        private const float NodeHeight = 74f;
        private const float NodeGap = 38f;
        private const float RowHeight = 22f;
        private const string WindowTitle = "MatDataTransfer Timeline";
        private const string LiveSessionId = "EditorLive";

        private readonly List<MatDataTransferTimelineLogLine> m_Records =
            new List<MatDataTransferTimelineLogLine>();
        private readonly List<int> m_Frames = new List<int>();
        private readonly Dictionary<int, int> m_RecordCountsByFrame = new Dictionary<int, int>();
        private Vector2 m_ListScroll;
        private Vector2 m_DetailScroll;
        private string m_LoadedPath;
        private int m_SelectedFrameIndex;
        private int m_SelectedRecordIndex = -1;
        private string m_SelectedRecordKey;
        private MatDataTransferTimelineLogLine m_SelectedRecordSnapshot;
        private bool m_UseLiveTimeline = true;
        private int m_LiveTimelineVersion = -1;

        [MenuItem("Window/Rendering/MatDataTransfer Timeline Viewer")]
        public static void OpenWindow()
        {
            MatDataTransferTimelineViewer window =
                GetWindow<MatDataTransferTimelineViewer>(WindowTitle);
            window.minSize = new Vector2(900f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += RepaintWhenLiveTimelineChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhenLiveTimelineChanged;
        }

        private void OnGUI()
        {
            if (m_UseLiveTimeline)
                LoadLiveTimeline();

            DrawToolbar();
            DrawContent();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool nextUseLiveTimeline = GUILayout.Toggle(
                    m_UseLiveTimeline,
                    "Live",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(48f));
                if (nextUseLiveTimeline != m_UseLiveTimeline)
                {
                    m_UseLiveTimeline = nextUseLiveTimeline;
                    if (m_UseLiveTimeline)
                        LoadLiveTimeline();
                }

                if (GUILayout.Button("Open JSONL", EditorStyles.toolbarButton, GUILayout.Width(92f)))
                    OpenLogFile();

                using (new EditorGUI.DisabledScope(m_UseLiveTimeline || string.IsNullOrEmpty(m_LoadedPath)))
                {
                    if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                        LoadLogFile(m_LoadedPath);
                }

                using (new EditorGUI.DisabledScope(!m_UseLiveTimeline))
                {
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                        ClearLiveTimeline();
                }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    GetToolbarStatusText(),
                    InspectorStyleLibrary.Description);
            }

            if (m_Frames.Count == 0)
                return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Unity Frame", InspectorStyleLibrary.ParameterName, GUILayout.Width(78f));
                int nextFrameIndex = Mathf.Clamp(
                    EditorGUILayout.IntSlider(m_SelectedFrameIndex, 0, m_Frames.Count - 1),
                    0,
                    m_Frames.Count - 1);
                if (nextFrameIndex != m_SelectedFrameIndex)
                {
                    m_SelectedFrameIndex = nextFrameIndex;
                    SelectFirstRecordInFrame();
                }

                int frame = m_Frames[m_SelectedFrameIndex];
                int count = m_RecordCountsByFrame.TryGetValue(frame, out int value) ? value : 0;
                EditorGUILayout.LabelField(
                    "Latest on left / Global Frame " + frame + " / Records " + count,
                    InspectorStyleLibrary.Description,
                    GUILayout.Width(280f));
            }
        }

        private void DrawContent()
        {
            if (m_Records.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    m_UseLiveTimeline
                        ? "Enable logging on MatDataTransferFeature to view the live timeline in Edit Mode or Play Mode."
                        : "Open a MatDataTransfer .jsonl timeline file exported from Player.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawRecordList();
                DrawRecordGraph();
            }
        }

        private void DrawRecordList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(SidebarWidth)))
            {
                InspectorStyleLibrary.DrawTitle("Records");
                m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll);
                int selectedFrame = GetSelectedFrame();
                for (int i = 0; i < m_Records.Count; i++)
                {
                    if (m_Records[i].FrameIndex != selectedFrame)
                        continue;

                    DrawRecordRow(i, m_Records[i]);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawRecordRow(int index, MatDataTransferTimelineLogLine record)
        {
            Rect row = EditorGUILayout.GetControlRect(false, RowHeight);
            bool selected = IsSelectedRecord(index, record);
            if (Event.current.type == EventType.Repaint)
            {
                Color color = selected
                    ? new Color(0.24f, 0.48f, 0.9f, 0.78f)
                    : GetStatusColor(record, 0.22f);
                EditorGUI.DrawRect(row, color);
            }

            Rect labelRect = new Rect(row.x + 6f, row.y, row.width - 12f, row.height);
            GUI.Label(labelRect, BuildRecordLabel(record), InspectorStyleLibrary.Description);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                SelectRecord(index, record);
                Repaint();
                Event.current.Use();
            }
        }

        private void DrawRecordGraph()
        {
            m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
            MatDataTransferTimelineLogLine record = GetSelectedRecord();
            if (record == null)
            {
                EditorGUILayout.HelpBox("Select a record to inspect the data flow.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            Rect graphRect = GUILayoutUtility.GetRect(
                NodeWidth * 3f + NodeGap * 2f + 60f,
                320f,
                GUILayout.ExpandWidth(true));
            DrawGraph(graphRect, record);
            DrawReceipt(record);
            EditorGUILayout.EndScrollView();
        }

        private void DrawGraph(Rect rect, MatDataTransferTimelineLogLine record)
        {
            Rect[] nodes = BuildNodeRects(rect);
            string[] titles =
            {
                "Source",
                "Instance",
                "Renderer",
                "Binding",
                "Writer",
                "Receipt"
            };
            string[] bodies =
            {
                Safe(record.SourceId),
                "#" + record.InstanceId + " " + Safe(record.GameObjectPath),
                "#" + record.RendererId + " Slot " + record.MaterialSlot + "\n" + Safe(record.RendererPathId),
                Safe(record.SemanticKey) + " -> " + Safe(record.PropertyName) + "\n" + Safe(record.ShaderName),
                Safe(record.WriteMethod) + "\n" + Safe(record.ValuePreview),
                Safe(record.Status) + "\n" + Safe(record.ResultType) + "/" + Safe(record.ResultCode)
            };

            Handles.BeginGUI();
            for (int i = 0; i < nodes.Length - 1; i++)
                DrawConnection(nodes[i], nodes[i + 1], GetStatusColor(record, 0.95f));
            Handles.EndGUI();

            for (int i = 0; i < nodes.Length; i++)
                DrawNode(nodes[i], titles[i], bodies[i], i == nodes.Length - 1 ? GetStatusColor(record, 0.38f) : new Color(0.18f, 0.22f, 0.28f, 1f));
        }

        private static Rect[] BuildNodeRects(Rect rect)
        {
            float x = rect.x + 18f;
            float y = rect.y + 28f;
            return new[]
            {
                new Rect(x, y, NodeWidth, NodeHeight),
                new Rect(x + NodeWidth + NodeGap, y, NodeWidth, NodeHeight),
                new Rect(x + (NodeWidth + NodeGap) * 2f, y, NodeWidth, NodeHeight),
                new Rect(x, y + NodeHeight + 72f, NodeWidth, NodeHeight),
                new Rect(x + NodeWidth + NodeGap, y + NodeHeight + 72f, NodeWidth, NodeHeight),
                new Rect(x + (NodeWidth + NodeGap) * 2f, y + NodeHeight + 72f, NodeWidth, NodeHeight)
            };
        }

        private static void DrawConnection(Rect from, Rect to, Color color)
        {
            Vector3 start = new Vector3(from.xMax, from.center.y, 0f);
            Vector3 end = new Vector3(to.xMin, to.center.y, 0f);
            if (to.y > from.yMax)
            {
                start = new Vector3(from.center.x, from.yMax, 0f);
                end = new Vector3(to.center.x, to.yMin, 0f);
            }

            Handles.color = color;
            Handles.DrawAAPolyLine(3f, start, end);
        }

        private static void DrawNode(Rect rect, string title, string body, Color color)
        {
            EditorGUI.DrawRect(rect, color);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            Rect titleRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 18f);
            Rect bodyRect = new Rect(rect.x + 8f, rect.y + 28f, rect.width - 16f, rect.height - 34f);
            GUI.Label(titleRect, title, InspectorStyleLibrary.Title);
            GUI.Label(bodyRect, body, InspectorStyleLibrary.Description);
        }

        private static void DrawReceipt(MatDataTransferTimelineLogLine record)
        {
            EditorGUILayout.Space(8f);
            InspectorStyleLibrary.DrawTitle("Receipt");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InspectorStyleLibrary.DrawParameterValue("Accepted", record.Accepted.ToString());
                InspectorStyleLibrary.DrawParameterValue("Applied", record.Applied.ToString());
                InspectorStyleLibrary.DrawCopyableParameterValue("Layer", Safe(record.Layer));
                InspectorStyleLibrary.DrawCopyableParameterValue("Write Method", Safe(record.WriteMethod));
                InspectorStyleLibrary.DrawCopyableParameterValue("Renderer Trace", Safe(record.RendererPathId), preferPathSegments: true);
                InspectorStyleLibrary.DrawCopyableParameterValue("Material Trace", Safe(record.MaterialTraceId), preferPathSegments: true);
                InspectorStyleLibrary.DrawCopyableParameterValue("Catalog", Safe(record.CatalogName));
                InspectorStyleLibrary.DrawCopyableParameterValue("Property Id", record.PropertyId.ToString());
                InspectorStyleLibrary.DrawCopyableParameterValue("Value Hash", Safe(record.ValueHash));
                if (!string.IsNullOrWhiteSpace(record.Message))
                    EditorGUILayout.HelpBox(record.Message, MessageType.None);
            }
        }

        private void OpenLogFile()
        {
            string path = EditorUtility.OpenFilePanel(
                "Open MatDataTransfer Timeline",
                Application.persistentDataPath,
                "jsonl");
            if (!string.IsNullOrEmpty(path))
                LoadLogFile(path);
        }

        private void LoadLiveTimeline()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            IReadOnlyList<MatDataTransferTimelineRecord> timelineRecords =
                feature != null ? feature.TimelineRecords : null;
            IMatDataTransferLogging logging = feature != null ? feature.Logging : null;
            int timelineVersion = logging != null ? logging.TimelineVersion : -1;
            if (timelineVersion == m_LiveTimelineVersion)
                return;

            m_UseLiveTimeline = true;
            m_LoadedPath = null;
            RebuildFromTimelineRecords(timelineRecords);
            m_LiveTimelineVersion = timelineVersion;
            Repaint();
        }

        private void RepaintWhenLiveTimelineChanged()
        {
            if (!m_UseLiveTimeline)
                return;

            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            IMatDataTransferLogging logging = feature != null ? feature.Logging : null;
            int timelineVersion = logging != null ? logging.TimelineVersion : -1;
            if (timelineVersion != m_LiveTimelineVersion)
                Repaint();
        }

        private void ClearLiveTimeline()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            feature?.Logging.ClearTimelineRecords();
            m_LiveTimelineVersion = -1;
            ClearSelectedRecord();
            RebuildFromTimelineRecords(null);
            Repaint();
        }

        private void LoadLogFile(string path)
        {
            m_UseLiveTimeline = false;
            m_Records.Clear();
            m_Frames.Clear();
            m_RecordCountsByFrame.Clear();
            m_SelectedFrameIndex = 0;
            ClearSelectedRecord();
            m_LoadedPath = path;

            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    while (!reader.EndOfStream)
                        AddLine(reader.ReadLine());
                }
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "MatDataTransfer Timeline",
                    "Failed to load timeline:\n" + exception.Message,
                    "OK");
            }

            m_Records.Sort(CompareRecords);
            RebuildFrames();
            m_SelectedFrameIndex = Mathf.Clamp(m_SelectedFrameIndex, 0, m_Frames.Count - 1);
            SelectFirstRecordInFrame();
            Repaint();
        }

        private void RebuildFromTimelineRecords(
            IReadOnlyList<MatDataTransferTimelineRecord> timelineRecords)
        {
            m_Records.Clear();
            m_Frames.Clear();
            m_RecordCountsByFrame.Clear();

            if (timelineRecords != null)
            {
                for (int i = 0; i < timelineRecords.Count; i++)
                    m_Records.Add(MatDataTransferTimelineLogLine.Create(
                        LiveSessionId,
                        i,
                        timelineRecords[i]));
            }

            m_Records.Sort(CompareRecords);
            RebuildFrames();
            m_SelectedFrameIndex = Mathf.Clamp(m_SelectedFrameIndex, 0, m_Frames.Count - 1);
            RestoreSelectedRecordIndex();
            if (m_SelectedRecordSnapshot == null)
                SelectFirstRecordInFrame();
        }

        private void AddLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            MatDataTransferTimelineLogLine record =
                JsonUtility.FromJson<MatDataTransferTimelineLogLine>(line);
            if (record != null && string.Equals(record.Schema, "MatDataTransferTimeline.v1", StringComparison.Ordinal))
                m_Records.Add(record);
        }

        private void RebuildFrames()
        {
            for (int i = 0; i < m_Records.Count; i++)
            {
                int frame = m_Records[i].FrameIndex;
                if (!m_RecordCountsByFrame.ContainsKey(frame))
                {
                    m_Frames.Add(frame);
                    m_RecordCountsByFrame.Add(frame, 0);
                }

                m_RecordCountsByFrame[frame]++;
            }
        }

        private int GetSelectedFrame()
        {
            if (m_Frames.Count == 0)
                return -1;

            m_SelectedFrameIndex = Mathf.Clamp(m_SelectedFrameIndex, 0, m_Frames.Count - 1);
            return m_Frames[m_SelectedFrameIndex];
        }

        private MatDataTransferTimelineLogLine GetSelectedRecord()
        {
            RestoreSelectedRecordIndex();
            if (m_SelectedRecordSnapshot != null)
                return m_SelectedRecordSnapshot;

            SelectFirstRecordInFrame();
            return m_SelectedRecordSnapshot;
        }

        private void SelectFirstRecordInFrame()
        {
            int frame = GetSelectedFrame();
            ClearSelectedRecord();
            for (int i = 0; i < m_Records.Count; i++)
            {
                if (m_Records[i].FrameIndex == frame)
                {
                    SelectRecord(i, m_Records[i]);
                    return;
                }
            }
        }

        private void SelectRecord(int index, MatDataTransferTimelineLogLine record)
        {
            m_SelectedRecordIndex = index;
            m_SelectedRecordSnapshot = record;
            m_SelectedRecordKey = BuildRecordKey(record);
        }

        private void ClearSelectedRecord()
        {
            m_SelectedRecordIndex = -1;
            m_SelectedRecordKey = null;
            m_SelectedRecordSnapshot = null;
        }

        private bool IsSelectedRecord(int index, MatDataTransferTimelineLogLine record)
        {
            if (m_SelectedRecordIndex >= 0)
                return index == m_SelectedRecordIndex;

            return !string.IsNullOrEmpty(m_SelectedRecordKey)
                && string.Equals(BuildRecordKey(record), m_SelectedRecordKey, StringComparison.Ordinal);
        }

        private void RestoreSelectedRecordIndex()
        {
            if (string.IsNullOrEmpty(m_SelectedRecordKey))
            {
                m_SelectedRecordIndex = -1;
                return;
            }

            for (int i = 0; i < m_Records.Count; i++)
            {
                if (!string.Equals(BuildRecordKey(m_Records[i]), m_SelectedRecordKey, StringComparison.Ordinal))
                    continue;

                m_SelectedRecordIndex = i;
                m_SelectedRecordSnapshot = m_Records[i];
                return;
            }

            m_SelectedRecordIndex = -1;
        }

        private static string BuildRecordKey(MatDataTransferTimelineLogLine record)
        {
            if (record == null)
                return string.Empty;

            return Safe(record.SessionId)
                + "|"
                + record.InstanceId
                + "|"
                + record.RendererId
                + "|"
                + record.MaterialSlot
                + "|"
                + record.PropertyId
                + "|"
                + Safe(record.SourceId)
                + "|"
                + Safe(record.ProviderName)
                + "|"
                + Safe(record.SemanticKey)
                + "|"
                + Safe(record.WriteMethod)
                + "|"
                + Safe(record.Layer);
        }

        private static int CompareRecords(
            MatDataTransferTimelineLogLine left,
            MatDataTransferTimelineLogLine right)
        {
            int frame = right.FrameIndex.CompareTo(left.FrameIndex);
            return frame != 0 ? frame : left.RecordIndex.CompareTo(right.RecordIndex);
        }

        private static string BuildRecordLabel(MatDataTransferTimelineLogLine record)
        {
            return "#"
                + record.RecordIndex
                + " Seq "
                + record.Sequence
                + "  "
                + Safe(record.Status)
                + "  "
                + Safe(record.SemanticKey)
                + "  "
                + Safe(record.ValuePreview);
        }

        private string GetToolbarStatusText()
        {
            if (m_UseLiveTimeline)
                return "Live Editor Timeline";

            return string.IsNullOrEmpty(m_LoadedPath) ? "No timeline loaded" : m_LoadedPath;
        }

        private static Color GetStatusColor(MatDataTransferTimelineLogLine record, float alpha)
        {
            if (record == null)
                return new Color(0.2f, 0.2f, 0.2f, alpha);

            if (string.Equals(record.Status, "Applied", StringComparison.Ordinal))
                return new Color(0.16f, 0.56f, 0.32f, alpha);
            if (string.Equals(record.Status, "Overridden", StringComparison.Ordinal))
                return new Color(0.8f, 0.55f, 0.16f, alpha);
            if (string.Equals(record.Status, "WriterFailed", StringComparison.Ordinal))
                return new Color(0.75f, 0.2f, 0.18f, alpha);
            if (string.Equals(record.Status, "Rejected", StringComparison.Ordinal))
                return new Color(0.62f, 0.2f, 0.22f, alpha);

            return new Color(0.25f, 0.35f, 0.55f, alpha);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }
    }
}
