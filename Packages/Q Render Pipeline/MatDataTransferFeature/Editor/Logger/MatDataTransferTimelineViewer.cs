using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer : EditorWindow
    {
        private const string WindowTitle = "MatDataTransfer Timeline";
        private const float MinWindowWidth = 1280f;
        private const float MinWindowHeight = 620f;
        private const float SidebarWidth = 270f;
        private const float ToolbarHeight = 38f;
        private const float FrameBarHeight = 44f;
        private const float CanvasToolbarHeight = 28f;
        private const float ReceiptWidth = 430f;
        private const float ReceiptHeight = 252f;
        private const float CanvasPadding = 36f;
        private const float ColumnGap = 34f;
        private const float LaneHeight = 34f;
        private const float NodeWidth = 220f;
        private const float NodeHeight = 94f;
        private const float CompactNodeHeight = 78f;
        private const float FeatureNodeWidth = 290f;
        private const float FeatureNodeHeight = 176f;
        private const int DefaultFileMaxFrames = 128;

        private readonly List<MatDataTransferTimelineFrame> m_Frames =
            new List<MatDataTransferTimelineFrame>();
        private readonly Dictionary<int, int> m_FileFrameLookup = new Dictionary<int, int>();

        private Vector2 m_RecordScroll;
        private Vector2 m_CanvasScroll;
        private string m_LoadedPath;
        private bool m_UseLiveTimeline = true;
        private bool m_ShowAllRecords = true;
        private bool m_ShowReceipt = true;
        private TimelineStatusFilter m_StatusFilter = TimelineStatusFilter.All;
        private int m_LiveTimelineVersion = -1;
        private int m_FrameOffset;
        private int m_SelectedRecordIndex = -1;
        private string m_SelectedRecordKey;

        [MenuItem("TA/角色模型工具/材质传输系统/MatDataTransfer Timeline Viewer")]
        public static void OpenWindow()
        {
            MatDataTransferTimelineViewer window = GetWindow<MatDataTransferTimelineViewer>(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);
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
            DrawFrameBar();
            DrawMainLayout();
        }
    }
}
