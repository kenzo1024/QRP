using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
        private void DrawCanvas(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect header = new Rect(rect.x, rect.y, rect.width, CanvasToolbarHeight);
            EditorGUI.DrawRect(header, new Color(0.19f, 0.19f, 0.19f));
            GUI.Label(new Rect(header.x + 10f, header.y + 7f, header.width - 330f, 16f),
                (m_ShowAllRecords ? "All Records" : "Single Record") + " in Frame " + m_FrameOffset
                + "  /  Selected payload highlighted", InspectorStyleLibrary.Description);

            Rect view = new Rect(header.xMax - 314f, header.y + 3f, 212f, 22f);
            GUI.Label(new Rect(view.x, view.y + 4f, 34f, 18f), "View", EditorStyles.miniLabel);
            if (DrawTimelineToggle(new Rect(view.x + 38f, view.y, 94f, view.height), !m_ShowAllRecords, "Single"))
                m_ShowAllRecords = false;
            if (DrawTimelineToggle(new Rect(view.x + 136f, view.y, 76f, view.height), m_ShowAllRecords, "All"))
                m_ShowAllRecords = true;

            m_ShowReceipt = DrawTimelineToggle(
                new Rect(view.xMax + 12f, view.y, 82f, view.height),
                m_ShowReceipt,
                "Receipt");

            DrawFlowViewport(new Rect(rect.x + 8f, rect.y + CanvasToolbarHeight + 8f, rect.width - 16f, rect.height - CanvasToolbarHeight - 16f));
        }

        private void DrawFlowViewport(Rect viewport)
        {
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            bool hasSelected = TryGetSelectedRecord(frame, out MatDataTransferTimelineRecord selected);
            List<MatDataTransferTimelineRecord> records = BuildVisibleRecords(frame, hasSelected, selected);
            float contentWidth = Mathf.Max(viewport.width - 18f, CalculateGraphWidth(viewport.width));
            float contentHeight = Mathf.Max(viewport.height - 18f, CalculateGraphHeight(records.Count));
            Rect content = new Rect(0f, 0f, contentWidth, contentHeight);
            m_CanvasScroll = GUI.BeginScrollView(viewport, m_CanvasScroll, content, true, true);
            Rect canvas = new Rect(0f, 0f, content.width - 16f, content.height - 16f);
            EditorGUI.DrawRect(canvas, new Color(0.18f, 0.18f, 0.18f));
            DrawGrid(canvas, 40f);
            DrawFlowGraph(canvas, frame, records, hasSelected, selected);
            DrawPlaybackOverlay(canvas);
            if (m_ShowReceipt)
                DrawReceiptOverlay(canvas, frame);
            GUI.EndScrollView();
        }

        private static void DrawPlaybackOverlay(Rect canvas)
        {
            const float playbackWidth = 400f;
            const float playbackHeight = 108f;
            Rect rect = new Rect(
                canvas.x + CanvasPadding,
                canvas.yMax - CanvasPadding - playbackHeight,
                playbackWidth,
                playbackHeight);
            DrawPlaybackModel(rect);
        }

        private static void DrawGrid(Rect rect, float spacing)
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.22f, 0.22f, 0.22f);
            for (float x = rect.x; x < rect.xMax; x += spacing)
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            for (float y = rect.y; y < rect.yMax; y += spacing)
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            Handles.EndGUI();
        }

        private void DrawFlowGraph(
            Rect canvas,
            MatDataTransferTimelineFrame frame,
            List<MatDataTransferTimelineRecord> records,
            bool hasSelected,
            MatDataTransferTimelineRecord selected)
        {
            FlowLayout layout = BuildFlowLayout(canvas);
            FlowNodeRects nodes = BuildFlowNodeRects(layout, records, hasSelected, selected);
            DrawLaneLabels(layout);
            DrawStageStatusStrip(layout, records, frame);
            DrawSubmitNode(nodes.Submit, records, hasSelected, selected);
            DrawInstanceNodes(nodes, records, hasSelected, selected);
            DrawFeatureNode(nodes.Feature, frame);
            DrawPipelineNodes(nodes, hasSelected, selected);
            DrawFlowConnections(nodes, records, hasSelected, selected);
        }

        private static void DrawLaneLabel(Rect rect, string title, string subtitle)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), title, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(subtitle))
                InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 112f, rect.y + 9f, rect.width - 118f, 16f), subtitle, TimelineLabel, false);
        }

        private static float CalculateGraphWidth(float viewportWidth)
        {
            float minimum = CanvasPadding * 2f
                + NodeWidth * 5f
                + FeatureNodeWidth
                + ColumnGap * 5f;
            return Mathf.Max(viewportWidth - 18f, minimum);
        }

        private static float CalculateGraphHeight(int recordCount)
        {
            int instanceRows = Mathf.Clamp(recordCount, 1, 6);
            float instanceHeight = 120f + instanceRows * 118f;
            return Mathf.Max(760f, instanceHeight + 190f);
        }

        private static FlowLayout BuildFlowLayout(Rect canvas)
        {
            float usableWidth = Mathf.Max(
                1f,
                canvas.width - CanvasPadding * 2f - ColumnGap * 5f);
            float scale = Mathf.Max(
                1f,
                usableWidth / (NodeWidth * 5f + FeatureNodeWidth));
            float nodeWidth = NodeWidth * scale;
            float featureWidth = FeatureNodeWidth * scale;
            float x = canvas.x + CanvasPadding;
            float top = canvas.y + CanvasPadding;

            FlowLayout layout = new FlowLayout
            {
                Top = top,
                Submit = new Rect(x, top, nodeWidth, LaneHeight)
            };
            x += nodeWidth + ColumnGap;
            layout.Instance = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + ColumnGap;
            layout.Feature = new Rect(x, top, featureWidth, LaneHeight);
            x += featureWidth + ColumnGap;
            layout.Resolve = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + ColumnGap;
            layout.Writer = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + ColumnGap;
            layout.Apply = new Rect(x, top, nodeWidth, LaneHeight);
            return layout;
        }

        private static FlowNodeRects BuildFlowNodeRects(
            FlowLayout layout,
            List<MatDataTransferTimelineRecord> records,
            bool hasSelected,
            MatDataTransferTimelineRecord selected)
        {
            float mainCenterY = layout.Top + 258f;
            float resultCenterY = layout.Top + 448f;
            FlowNodeRects nodes = new FlowNodeRects
            {
                Submit = NodeCenteredInColumn(layout.Submit, mainCenterY, NodeHeight),
                Feature = NodeCenteredInColumn(layout.Feature, mainCenterY, FeatureNodeHeight),
                Resolve = NodeCenteredInColumn(layout.Resolve, mainCenterY, NodeHeight),
                Writer = NodeCenteredInColumn(layout.Writer, mainCenterY, NodeHeight),
                Apply = NodeCenteredInColumn(layout.Apply, mainCenterY, NodeHeight),
                Conflict = NodeCenteredInColumn(layout.Resolve, resultCenterY, CompactNodeHeight),
                Failed = NodeCenteredInColumn(layout.Writer, resultCenterY, CompactNodeHeight),
                InstanceNodes = new List<Rect>()
            };

            List<InstanceBucket> buckets = BuildInstanceBuckets(records);
            float rowGap = records.Count > 4 ? 104f : 118f;
            float firstCenterY = mainCenterY - Mathf.Max(0, buckets.Count - 1) * rowGap * 0.5f;
            for (int i = 0; i < buckets.Count; i++)
                nodes.InstanceNodes.Add(NodeCenteredInColumn(layout.Instance, firstCenterY + i * rowGap, CompactNodeHeight));

            return nodes;
        }

        private static Rect NodeCenteredInColumn(Rect column, float centerY, float height)
        {
            return new Rect(column.x, centerY - height * 0.5f, column.width, height);
        }

        private static void DrawLaneLabels(FlowLayout layout)
        {
            DrawLaneLabel(layout.Submit, "Submit Start", "payload enters here");
            DrawLaneLabel(layout.Instance, "Instance Entrances", null);
            DrawLaneLabel(layout.Feature, "Feature Center", null);
            DrawLaneLabel(layout.Resolve, "Resolve", null);
            DrawLaneLabel(layout.Writer, "Writer", null);
            DrawLaneLabel(layout.Apply, "Apply", null);
        }

        private void DrawStageStatusStrip(
            FlowLayout layout,
            List<MatDataTransferTimelineRecord> records,
            MatDataTransferTimelineFrame frame)
        {
            CountRecords(records, out int applied, out int overridden, out int failed, out int rejected);
            int accepted = Mathf.Max(0, records.Count - rejected);
            int laneCount = records.Count > 0 ? BuildInstanceBuckets(records).Count : 0;
            float y = layout.Top + LaneHeight + 10f;
            DrawStageStatusChip(layout.Submit, y, records.Count + " entered", StatusColor(ParamWriteStatus.Submitted));
            DrawStageStatusChip(layout.Instance, y, laneCount + " lanes", new Color(0.48f, 0.38f, 0.86f));
            DrawStageStatusChip(layout.Feature, y, (frame != null ? frame.Records.Count : 0) + " in frame", StatusColor(ParamWriteStatus.Submitted));
            DrawStageStatusChip(layout.Resolve, y, accepted + " accepted", StatusColor(ParamWriteStatus.Applied));
            DrawStageStatusChip(layout.Writer, y, failed + " failed", StatusColor(ParamWriteStatus.WriterFailed));
            DrawStageStatusChip(layout.Apply, y, applied + " applied", StatusColor(ParamWriteStatus.Applied));

            Rect conflict = new Rect(layout.Resolve.x, y + 28f, layout.Resolve.width, 20f);
            DrawStageStatusChip(conflict, conflict.y, overridden + " overridden", StatusColor(ParamWriteStatus.Overridden));
        }

        private static void DrawStageStatusChip(Rect column, float y, string text, Color color)
        {
            float width = Mathf.Min(column.width - 20f, 142f);
            Rect rect = new Rect(column.x + 10f, y, width, 20f);
            DrawPayloadChip(rect, text, color, true);
        }

        private void DrawSubmitNode(Rect rect, List<MatDataTransferTimelineRecord> records, bool hasSelected, MatDataTransferTimelineRecord selected)
        {
            DrawNode(rect, "Submit", new[]
            {
                "RequestPayload[]",
                hasSelected ? selected.Identity.SourceId : "SourceId + SemanticKey",
                hasSelected ? selected.SubmitLogSummary : "Sequence assigned"
            }, StatusColor(ParamWriteStatus.Submitted), hasSelected, "START");
            for (int i = 0; i < Mathf.Min(records.Count, 5); i++)
            {
                MatDataTransferTimelineRecord record = records[i];
                DrawPayloadChip(
                    new Rect(rect.x + 16f, rect.yMax + 24f + i * 24f, 170f, 20f),
                    "Seq " + record.Sequence + " " + ShortSemantic(record),
                    StatusColor(record.Status),
                    hasSelected && IsSameRecord(record, selected));
            }
        }

        private void DrawInstanceNodes(FlowNodeRects nodes, List<MatDataTransferTimelineRecord> records, bool hasSelected, MatDataTransferTimelineRecord selected)
        {
            List<InstanceBucket> buckets = BuildInstanceBuckets(records);
            for (int i = 0; i < buckets.Count && i < nodes.InstanceNodes.Count; i++)
            {
                InstanceBucket bucket = buckets[i];
                bool selectedBucket = hasSelected && bucket.InstanceId == selected.InstanceId;
                DrawNode(
                    nodes.InstanceNodes[i],
                    bucket.Name,
                    new[] { bucket.Path, bucket.Count + " records in current frame" },
                    new Color(0.48f, 0.38f, 0.86f),
                    selectedBucket,
                    bucket.Count.ToString());
            }
        }

        private void DrawFeatureNode(Rect rect, MatDataTransferTimelineFrame frame)
        {
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.28f, 0.26f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 30f), new Color(0.30f, 0.34f, 0.38f));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 7f, rect.width - 32f, 20f), "MatDataTransferFeature", EditorStyles.boldLabel);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 18f, rect.y + 52f, rect.width - 36f, 16f), "Center node: owns frame buffer + routing", TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 18f, rect.y + 76f, rect.width - 36f, 16f), "BeginFrame -> collect payloads", TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 18f, rect.y + 100f, rect.width - 36f, 16f), "Dispatch by Instance / Renderer / Binding", TimelineLabel, false);
            InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 18f, rect.y + 124f, rect.width - 36f, 16f), "Timeline records written per payload", TimelineLabel, false);
            DrawPayloadChip(new Rect(rect.x + 18f, rect.y + 148f, 70f, 20f), "Frame " + m_FrameOffset, StatusColor(ParamWriteStatus.Submitted), true);
            DrawPayloadChip(new Rect(rect.x + 96f, rect.y + 148f, 88f, 20f), (frame != null ? frame.Records.Count : 0) + " records", StatusColor(ParamWriteStatus.Applied), true);
            DrawPayloadChip(new Rect(rect.x + 192f, rect.y + 148f, 82f, 20f), "buffer " + GetMaxRecordedFrames(), new Color(0.64f, 0.57f, 0.25f), false);
        }

        private void DrawPipelineNodes(FlowNodeRects nodes, bool hasSelected, MatDataTransferTimelineRecord selected)
        {
            ParamWriteStatus status = hasSelected ? selected.Status : ParamWriteStatus.Submitted;
            DrawNode(nodes.Resolve, "Resolve Binding", new[]
            {
                "semantic -> shader property",
                "catalog lookup",
                hasSelected ? Safe(selected.Binding.PropertyName) + " / id " + selected.Binding.PropertyId : "<none>"
            }, StatusColor(status), hasSelected, StageBadge(hasSelected, status, ParamWriteStatus.Submitted));
            DrawNode(nodes.Writer, "Writer", new[]
            {
                hasSelected ? selected.WriteMethod.ToString() : "<none>",
                "value payload forwarded",
                hasSelected ? Safe(selected.ValuePreview) : "<none>"
            }, StatusColor(status), hasSelected, StageBadge(hasSelected, status, ParamWriteStatus.Submitted));
            DrawNode(nodes.Apply, "Apply", new[]
            {
                hasSelected ? "Renderer slot " + selected.Identity.Binding.MaterialSlot : "<none>",
                hasSelected && selected.Status == ParamWriteStatus.Applied ? "MPB/material applied" : "not applied",
                "material trace updated"
            }, StatusColor(status), hasSelected, hasSelected && selected.Status == ParamWriteStatus.Applied ? "APPLIED" : "RESULT");
            DrawNode(
                nodes.Conflict,
                "Conflict Check",
                new[] { "priority + layer", "overridden routes dimmed" },
                StatusColor(ParamWriteStatus.Overridden),
                hasSelected && selected.Status == ParamWriteStatus.Overridden,
                hasSelected && selected.Status == ParamWriteStatus.Overridden ? "HIT" : null);
            DrawNode(
                nodes.Failed,
                "Rejected / Failed",
                new[] { "missing renderer", "writer error messages" },
                StatusColor(ParamWriteStatus.WriterFailed),
                hasSelected && IsFailedStatus(selected.Status),
                hasSelected && IsFailedStatus(selected.Status) ? "HIT" : null);
        }

        private void DrawFlowConnections(FlowNodeRects nodes, List<MatDataTransferTimelineRecord> records, bool hasSelected, MatDataTransferTimelineRecord selected)
        {
            List<InstanceBucket> buckets = BuildInstanceBuckets(records);
            Handles.BeginGUI();
            for (int i = 0; i < buckets.Count && records.Count > 0; i++)
            {
                if (i >= nodes.InstanceNodes.Count)
                    continue;

                InstanceBucket bucket = buckets[i];
                bool selectedBucket = hasSelected && bucket.InstanceId == selected.InstanceId;
                ParamWriteStatus status = selectedBucket
                    ? selected.Status
                    : ResolveBucketStatus(records, bucket.InstanceId);
                Rect instance = nodes.InstanceNodes[i];
                float width = selectedBucket ? 5f : 2.5f;
                DrawNodeConnection(nodes.Submit, instance, StatusColor(status), width);
                DrawNodeConnection(instance, nodes.Feature, StatusColor(status), width);
            }

            Color selectedColor = hasSelected ? StatusColor(selected.Status) : StatusColor(ParamWriteStatus.Submitted);
            DrawNodeConnection(nodes.Feature, nodes.Resolve, selectedColor, 5f);
            DrawNodeConnection(nodes.Resolve, nodes.Writer, selectedColor, 5f);
            DrawNodeConnection(nodes.Writer, nodes.Apply, selectedColor, 5f);
            DrawNodeConnection(nodes.Feature, nodes.Conflict, StatusColor(ParamWriteStatus.Overridden), 2.5f);
            DrawNodeConnection(nodes.Conflict, nodes.Failed, StatusColor(ParamWriteStatus.WriterFailed), 2.5f);
            Handles.EndGUI();

            if (records.Count == 0)
                return;

            DrawConnectionLabel(
                nodes.InstanceNodes.Count > 0 ? nodes.InstanceNodes[0] : nodes.Submit,
                nodes.Feature,
                hasSelected ? "Seq " + selected.Sequence + " selected payload" : "no selected payload",
                StatusColor(ParamWriteStatus.Applied),
                LabelLane.Above,
                true);
            DrawConnectionLabel(
                nodes.Feature,
                nodes.Resolve,
                "Payload carries value",
                StatusColor(ParamWriteStatus.Applied),
                LabelLane.Above,
                false);
            DrawConnectionLabel(
                nodes.Conflict,
                nodes.Failed,
                "terminal branch",
                StatusColor(ParamWriteStatus.WriterFailed),
                LabelLane.Below,
                false);
        }

        private static string StageBadge(bool hasSelected, ParamWriteStatus status, ParamWriteStatus fallback)
        {
            return StatusBadgeText(hasSelected ? status : fallback);
        }

        private static string StatusBadgeText(ParamWriteStatus status)
        {
            switch (status)
            {
                case ParamWriteStatus.Applied:
                    return "APPLY";
                case ParamWriteStatus.Overridden:
                    return "OVER";
                case ParamWriteStatus.Rejected:
                    return "REJECT";
                case ParamWriteStatus.WriterFailed:
                    return "FAIL";
                case ParamWriteStatus.Queued:
                    return "QUEUE";
                case ParamWriteStatus.Submitted:
                default:
                    return "START";
            }
        }

        private static bool IsFailedStatus(ParamWriteStatus status)
        {
            return status == ParamWriteStatus.Rejected
                || status == ParamWriteStatus.WriterFailed;
        }

        private static void CountRecords(
            List<MatDataTransferTimelineRecord> records,
            out int applied,
            out int overridden,
            out int failed,
            out int rejected)
        {
            applied = 0;
            overridden = 0;
            failed = 0;
            rejected = 0;

            for (int i = 0; i < records.Count; i++)
            {
                ParamWriteStatus status = records[i].Status;
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

        private static ParamWriteStatus ResolveBucketStatus(
            List<MatDataTransferTimelineRecord> records,
            int instanceId)
        {
            bool hasApplied = false;
            bool hasOverridden = false;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].InstanceId != instanceId)
                    continue;

                ParamWriteStatus status = records[i].Status;
                if (status == ParamWriteStatus.WriterFailed
                    || status == ParamWriteStatus.Rejected)
                    return status;

                if (status == ParamWriteStatus.Overridden)
                    hasOverridden = true;
                else if (status == ParamWriteStatus.Applied)
                    hasApplied = true;
            }

            if (hasOverridden)
                return ParamWriteStatus.Overridden;
            if (hasApplied)
                return ParamWriteStatus.Applied;
            return ParamWriteStatus.Submitted;
        }

        private static void DrawPlaybackModel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, 18f), "Frame Playback Model", EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 38f, rect.width - 28f, 16f), "0 is always the latest collected frame.", InspectorStyleLibrary.Description);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 60f, rect.width - 28f, 16f), "When new frames arrive, offsets shift right: 0 -> 1 -> 2.", InspectorStyleLibrary.Description);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 82f, rect.width - 28f, 16f), "Records older than Max Recorded Frames are discarded.", InspectorStyleLibrary.Description);
        }

        private void DrawReceiptOverlay(Rect canvas, MatDataTransferTimelineFrame frame)
        {
            bool hasSelected = TryGetSelectedRecord(frame, out MatDataTransferTimelineRecord selected);
            float receiptX = canvas.xMax - ReceiptWidth - CanvasPadding;
            float receiptY = canvas.yMax - ReceiptHeight - CanvasPadding;
            Rect rect = new Rect(receiptX, receiptY, ReceiptWidth, ReceiptHeight);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 28f), new Color(0.18f, 0.18f, 0.18f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 120f, 18f), "Receipt - selected payload " + (hasSelected ? "Seq " + selected.Sequence : "<none>"), EditorStyles.boldLabel);
            if (DrawTimelineButton(new Rect(rect.xMax - 24f, rect.y + 4f, 18f, 18f), "x"))
            {
                m_ShowReceipt = false;
                return;
            }

            if (hasSelected)
                DrawPayloadChip(new Rect(rect.xMax - 132f, rect.y + 5f, 100f, 18f), selected.Status.ToString(), StatusColor(selected.Status), true);

            ParamSubmitStep selectedStep = hasSelected ? selected.Step : null;
            float y = rect.y + 46f;
            DrawReceiptRow(rect.x + 16f, y, "Accepted", selectedStep != null ? selectedStep.IsAccepted.ToString() : "<none>", selectedStep != null && selectedStep.IsAccepted ? StatusColor(ParamWriteStatus.Applied) : InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Applied", selectedStep != null ? selectedStep.IsApplied.ToString() : "<none>", selectedStep != null && selectedStep.IsApplied ? StatusColor(ParamWriteStatus.Applied) : InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Write Method", hasSelected ? selected.WriteMethod.ToString() : "<none>", InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Layer", hasSelected ? selected.WriteConfig.Layer.ToString() : "<none>", InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Renderer", hasSelected ? "#" + selected.Identity.Binding.RendererId + " / Slot " + selected.Identity.Binding.MaterialSlot : "<none>", InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Property", hasSelected ? Safe(selected.Binding.PropertyName) + " / " + selected.Binding.PropertyId : "<none>", InspectorStyleLibrary.Description.normal.textColor); y += 22f;
            DrawReceiptRow(rect.x + 16f, y, "Value Hash", hasSelected ? selected.ValueHash.ToString("X16") : "<none>", InspectorStyleLibrary.Description.normal.textColor);
            DrawPayloadChip(new Rect(rect.x + 14f, rect.yMax - 46f, rect.width - 28f, 28f), selectedStep != null ? "Message: " + Safe(selectedStep.Message) : "Message: <none>", hasSelected ? StatusColor(selected.Status) : StatusColor(ParamWriteStatus.Submitted), false);
        }

        private static void DrawReceiptRow(float x, float y, string label, string value, Color valueColor)
        {
            GUI.Label(new Rect(x, y, 110f, 18f), label, InspectorStyleLibrary.Description);
            GUIStyle style = new GUIStyle(InspectorStyleLibrary.Description);
            style.normal.textColor = valueColor;
            style.wordWrap = false;
            InspectorStyleLibrary.DrawTailLabel(new Rect(x + 120f, y, 280f, 18f), value, style, false);
        }

    }
}
