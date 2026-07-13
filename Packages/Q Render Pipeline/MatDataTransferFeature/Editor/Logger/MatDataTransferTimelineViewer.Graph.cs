using System.Collections.Generic;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
        private const float DynamicNodeMinHeight = 92f;
        private const float DynamicNodeGap = 34f;
        private const float ContainerInset = 10f;
        private const float ContainerContentGap = 10f;

        private void DrawCanvas(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect header = new Rect(rect.x, rect.y, rect.width, CanvasToolbarHeight);
            EditorGUI.DrawRect(header, new Color(0.19f, 0.19f, 0.19f));
            GUI.Label(
                new Rect(header.x + 10f, header.y + 7f, header.width - 20f, 16f),
                "Dynamic Graph",
                InspectorStyleLibrary.Description);

            DrawFlowViewport(new Rect(rect.x + 8f, rect.y + CanvasToolbarHeight + 8f, rect.width - 16f, rect.height - CanvasToolbarHeight - 16f));
        }

        private void DrawFlowViewport(Rect viewport)
        {
            MatDataTransferTimelineFrame frame = GetSelectedFrame();
            bool hasSelected = TryGetSelectedRecord(frame, out MatDataTransferTimelineRecord selected);
            List<MatDataTransferTimelineRecord> records = BuildVisibleRecords(frame, hasSelected, selected);
            TimelineGraphModel model = BuildTimelineGraphModel(frame, records, hasSelected, selected);
            float contentWidth = Mathf.Max(viewport.width - 18f, CalculateGraphWidth(viewport.width));
            float contentHeight = Mathf.Max(viewport.height - 18f, CalculateGraphHeight(model, contentWidth));
            Rect content = new Rect(0f, 0f, contentWidth, contentHeight);
            m_CanvasScroll = GUI.BeginScrollView(viewport, m_CanvasScroll, content, true, true);
            Rect canvas = new Rect(0f, 0f, content.width - 16f, content.height - 16f);
            EditorGUI.DrawRect(canvas, new Color(0.18f, 0.18f, 0.18f));
            DrawGrid(canvas, 40f);
            DrawFlowGraph(canvas, model);
            GUI.EndScrollView();
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

        private void DrawFlowGraph(Rect canvas, TimelineGraphModel model)
        {
            FlowLayout layout = BuildFlowLayout(canvas);
            LayoutGraphNodes(layout, model);
            DrawLaneLabels(layout);
            DrawStageStatusStrip(layout, model);
            DrawGraphContainers(model);
            DrawGraphEdges(model);
            DrawGraphPayloadNodes(model);
        }

        private static void DrawLaneLabel(Rect rect, string title, string subtitle)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 18f), title, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(subtitle))
                InspectorStyleLibrary.DrawTailLabel(new Rect(rect.x + 120f, rect.y + 9f, rect.width - 126f, 16f), subtitle, TimelineLabel, false);
        }

        private static float CalculateGraphWidth(float viewportWidth)
        {
            float minimum = CanvasPadding * 2f
                + NodeWidth * 6f
                + ColumnGap * 5f;
            return Mathf.Max(viewportWidth - 18f, minimum);
        }

        private static float CalculateGraphHeight(TimelineGraphModel model, float contentWidth)
        {
            float canvasWidth = Mathf.Max(1f, contentWidth - 16f);
            float usableWidth = Mathf.Max(1f, canvasWidth - CanvasPadding * 2f);
            float nodeWidth = Mathf.Clamp((usableWidth - ColumnGap * 5f) / 6f, 190f, 250f);
            return CanvasPadding + 112f
                + CalculateGraphRowsHeight(model, nodeWidth)
                + CanvasPadding;
        }

        private static float CalculateGraphRowsHeight(TimelineGraphModel model, float nodeWidth)
        {
            float height = 0f;
            TimelineGraphNode currentInstance = null;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                TimelineGraphRow row = model.Rows[i];
                if (!ReferenceEquals(currentInstance, row.InstanceContainer))
                {
                    if (currentInstance != null)
                        height += DynamicNodeGap;

                    float headerHeight = CalculateContainerHeaderHeight(row.InstanceContainer, nodeWidth);
                    if (i == 0)
                        headerHeight = Mathf.Max(headerHeight, CalculateContainerHeaderHeight(model.FeatureContainer, nodeWidth));
                    height += headerHeight + ContainerContentGap;
                    currentInstance = row.InstanceContainer;
                }

                height += CalculateRowHeight(row, nodeWidth) + DynamicNodeGap;
            }

            return height;
        }

        private static float CalculateRowHeight(TimelineGraphRow row, float nodeWidth)
        {
            float nestedWidth = Mathf.Max(1f, nodeWidth - ContainerInset * 2f);
            float height = CalculateNodeHeight(row.Source, nodeWidth);
            height = Mathf.Max(height, CalculateNodeHeight(row.InstancePayload, nestedWidth));
            height = Mathf.Max(height, CalculateNodeHeight(row.FeaturePayload, nestedWidth));
            height = Mathf.Max(height, CalculateNodeHeight(row.Resolve, nodeWidth));
            height = Mathf.Max(height, CalculateNodeHeight(row.Writer, nodeWidth));
            return Mathf.Max(height, CalculateNodeHeight(row.Apply, nodeWidth));
        }

        private static float CalculateContainerHeaderHeight(TimelineGraphNode node, float nodeWidth)
        {
            return node != null ? CalculateNodeHeight(node, nodeWidth) : 0f;
        }

        private static FlowLayout BuildFlowLayout(Rect canvas)
        {
            float usableWidth = Mathf.Max(1f, canvas.width - CanvasPadding * 2f);
            float nodeWidth = Mathf.Clamp((usableWidth - ColumnGap * 5f) / 6f, 190f, 250f);
            float gap = Mathf.Max(ColumnGap, (usableWidth - nodeWidth * 6f) / 5f);
            float x = canvas.x + CanvasPadding;
            float top = canvas.y + CanvasPadding;

            FlowLayout layout = new FlowLayout
            {
                Top = top,
                Submit = new Rect(x, top, nodeWidth, LaneHeight)
            };
            x += nodeWidth + gap;
            layout.Instance = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + gap;
            layout.Feature = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + gap;
            layout.Resolve = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + gap;
            layout.Writer = new Rect(x, top, nodeWidth, LaneHeight);
            x += nodeWidth + gap;
            layout.Apply = new Rect(x, top, nodeWidth, LaneHeight);
            return layout;
        }

        private static void DrawLaneLabels(FlowLayout layout)
        {
            DrawLaneLabel(layout.Submit, "Submit Sources", null);
            DrawLaneLabel(layout.Instance, "Instance Lanes", null);
            DrawLaneLabel(layout.Feature, "Feature Hub", null);
            DrawLaneLabel(layout.Resolve, "Resolve Results", null);
            DrawLaneLabel(layout.Writer, "Writer Results", null);
            DrawLaneLabel(layout.Apply, "Apply Results", null);
        }

        private void DrawStageStatusStrip(FlowLayout layout, TimelineGraphModel model)
        {
            float y = layout.Top + LaneHeight + 10f;
            DrawStageStatusChip(layout.Submit, y, model.GetStageCount(GraphStage.Submit) + " payloads", StatusColor(ParamWriteStatus.Submitted));
            DrawStageStatusChip(layout.Instance, y, model.GetStageCount(GraphStage.Instance) + " instances", new Color(0.48f, 0.38f, 0.86f));
            DrawStageStatusChip(layout.Feature, y, model.GetStageCount(GraphStage.Feature) + " feature / " + model.VisibleRecordCount + " payloads", StatusColor(ParamWriteStatus.Submitted));
            DrawStageStatusChip(layout.Resolve, y, model.GetStageCount(GraphStage.Resolve) + " payloads", StatusColor(ParamWriteStatus.Rejected));
            DrawStageStatusChip(layout.Writer, y, model.GetStageCount(GraphStage.Writer) + " payloads", StatusColor(ParamWriteStatus.Applied));
            DrawStageStatusChip(layout.Apply, y, model.GetStageCount(GraphStage.Apply) + " results", StatusColor(ParamWriteStatus.Applied));
        }

        private static void DrawStageStatusChip(Rect column, float y, string text, Color color)
        {
            float width = Mathf.Min(column.width - 20f, 158f);
            Rect rect = new Rect(column.x + 10f, y, width, 20f);
            DrawPayloadChip(rect, text, color, true);
        }

        private static void LayoutGraphNodes(FlowLayout layout, TimelineGraphModel model)
        {
            float y = layout.Top + 112f;
            float featureTop = y;
            TimelineGraphNode currentInstance = null;
            float instanceTop = y;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                TimelineGraphRow row = model.Rows[i];
                if (!ReferenceEquals(currentInstance, row.InstanceContainer))
                {
                    if (currentInstance != null)
                        y += DynamicNodeGap;

                    currentInstance = row.InstanceContainer;
                    instanceTop = y;
                    float headerHeight = CalculateContainerHeaderHeight(currentInstance, layout.Instance.width);
                    if (i == 0)
                        headerHeight = Mathf.Max(headerHeight, CalculateContainerHeaderHeight(model.FeatureContainer, layout.Feature.width));
                    y += headerHeight + ContainerContentGap;
                }

                float rowHeight = CalculateRowHeight(row, layout.Submit.width);
                row.Source.Rect = new Rect(layout.Submit.x, y, layout.Submit.width, rowHeight);
                row.InstancePayload.Rect = NestedRect(layout.Instance, y, rowHeight, row.InstancePayload);
                row.FeaturePayload.Rect = NestedRect(layout.Feature, y, rowHeight, row.FeaturePayload);
                row.Resolve.Rect = new Rect(layout.Resolve.x, y, layout.Resolve.width, rowHeight);
                row.Writer.Rect = new Rect(layout.Writer.x, y, layout.Writer.width, rowHeight);
                row.Apply.Rect = new Rect(layout.Apply.x, y, layout.Apply.width, rowHeight);

                float rowBottom = y + rowHeight + ContainerInset;
                currentInstance.Rect = new Rect(
                    layout.Instance.x,
                    instanceTop,
                    layout.Instance.width,
                    rowBottom - instanceTop);
                if (model.FeatureContainer != null)
                {
                    model.FeatureContainer.Rect = new Rect(
                        layout.Feature.x,
                        featureTop,
                        layout.Feature.width,
                        rowBottom - featureTop);
                }

                y += rowHeight + DynamicNodeGap;
            }
        }

        private static Rect NestedRect(
            Rect column,
            float y,
            float rowHeight,
            TimelineGraphNode node)
        {
            float width = column.width - ContainerInset * 2f;
            float height = CalculateNodeHeight(node, width);
            return new Rect(
                column.x + ContainerInset,
                y + (rowHeight - height) * 0.5f,
                width,
                height);
        }

        private static void DrawGraphContainers(TimelineGraphModel model)
        {
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                if (model.Nodes[i].IsContainer)
                    DrawGraphNode(model.Nodes[i]);
            }
        }

        private static void DrawGraphPayloadNodes(TimelineGraphModel model)
        {
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                if (!model.Nodes[i].IsContainer)
                    DrawGraphNode(model.Nodes[i]);
            }
        }

        private static void DrawGraphNode(TimelineGraphNode node)
        {
            DrawNode(
                node.Rect,
                node.Title,
                node.Lines.ToArray(),
                node.Accent,
                node.Selected,
                node.IsPayloadChild ? null : node.Count.ToString(),
                node.IsPayloadChild);
        }

        private static void DrawGraphEdges(TimelineGraphModel model)
        {
            Handles.BeginGUI();
            for (int i = 0; i < model.Edges.Count; i++)
            {
                TimelineGraphEdge edge = model.Edges[i];
                if (edge.From == null || edge.To == null)
                    continue;

                float width = edge.Selected ? 5f : Mathf.Clamp(1.5f + edge.Count * 0.05f, 2f, 4f);
                DrawNodeConnection(edge.From.Rect, edge.To.Rect, StatusColor(edge.Status), width);
            }
            Handles.EndGUI();
        }

        private TimelineGraphModel BuildTimelineGraphModel(
            MatDataTransferTimelineFrame frame,
            List<MatDataTransferTimelineRecord> records,
            bool hasSelected,
            MatDataTransferTimelineRecord selected)
        {
            TimelineGraphModel model = new TimelineGraphModel
            {
                FrameRecordCount = frame != null ? frame.Records.Count : 0,
                VisibleRecordCount = records != null ? records.Count : 0
            };

            if (records == null)
                return model;

            List<MatDataTransferTimelineRecord> orderedRecords = GroupRecordsByInstance(records);
            if (orderedRecords.Count > 0)
                model.FeatureContainer = AddFeatureContainer(model, orderedRecords[0], hasSelected);

            for (int i = 0; i < orderedRecords.Count; i++)
            {
                MatDataTransferTimelineRecord record = orderedRecords[i];
                bool selectedRecord = hasSelected && IsSameRecord(record, selected);
                TimelineGraphNode source = AddSourceNode(model, record, selectedRecord, hasSelected);
                TimelineGraphNode instanceContainer = AddInstanceContainer(model, record, selectedRecord, hasSelected);
                TimelineGraphNode instancePayload = AddInstancePayloadNode(model, instanceContainer, record, selectedRecord, hasSelected);
                TimelineGraphNode featurePayload = AddFeaturePayloadNode(model, model.FeatureContainer, record, selectedRecord, hasSelected);
                TimelineGraphNode resolve = AddResolveNode(model, record, selectedRecord, hasSelected);
                TimelineGraphNode writer = AddWriterNode(model, record, selectedRecord, hasSelected);
                TimelineGraphNode apply = AddApplyNode(model, record, selectedRecord, hasSelected);

                model.Rows.Add(new TimelineGraphRow
                {
                    Source = source,
                    InstanceContainer = instanceContainer,
                    InstancePayload = instancePayload,
                    FeaturePayload = featurePayload,
                    Resolve = resolve,
                    Writer = writer,
                    Apply = apply
                });

                model.AddEdge(source, instancePayload, record.Status, selectedRecord);
                model.AddEdge(instancePayload, featurePayload, record.Status, selectedRecord);
                model.AddEdge(featurePayload, resolve, record.Status, selectedRecord);
                model.AddEdge(resolve, writer, record.Status, selectedRecord);
                model.AddEdge(writer, apply, record.Status, selectedRecord);
            }

            return model;
        }

        private static List<MatDataTransferTimelineRecord> GroupRecordsByInstance(
            List<MatDataTransferTimelineRecord> records)
        {
            List<MatDataTransferTimelineRecord> ordered = new List<MatDataTransferTimelineRecord>();
            List<string> instanceOrder = new List<string>();
            Dictionary<string, List<MatDataTransferTimelineRecord>> groups =
                new Dictionary<string, List<MatDataTransferTimelineRecord>>();
            for (int i = 0; i < records.Count; i++)
            {
                MatDataTransferTimelineRecord record = records[i];
                string key = BuildInstanceGroupKey(record);
                if (!groups.TryGetValue(key, out List<MatDataTransferTimelineRecord> group))
                {
                    group = new List<MatDataTransferTimelineRecord>();
                    groups.Add(key, group);
                    instanceOrder.Add(key);
                }

                group.Add(record);
            }

            for (int i = 0; i < instanceOrder.Count; i++)
                ordered.AddRange(groups[instanceOrder[i]]);
            return ordered;
        }

        private static string BuildInstanceGroupKey(MatDataTransferTimelineRecord record)
        {
            return record.InstanceId + "|" + Safe(record.GameObjectPath);
        }

        private static TimelineGraphNode AddSourceNode(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            TimelineGraphNode node = CreateSequenceNode(
                model,
                GraphStage.Submit,
                record,
                "Submit Payload",
                selected);
            node.Lines.Add("Source ID: " + Safe(record.Identity.SourceId));
            node.Lines.Add("Semantic Key: " + Safe(record.Identity.SemanticKey));
            node.Lines.Add("Value: " + Safe(record.ValuePreview));
            if (detailed)
            {
                node.Lines.Add("Provider: " + Safe(record.ProviderName));
                node.Lines.Add("Value Hash: " + record.ValueHash.ToString("X16"));
                node.Lines.Add("Layer: " + record.WriteConfig.Layer);
                node.Lines.Add("Priority: " + record.WriteConfig.Priority);
            }
            return node;
        }

        private static TimelineGraphNode AddInstanceContainer(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            string key = BuildInstanceGroupKey(record);
            TimelineGraphNode node = model.GetOrAddContainerNode(
                GraphStage.Instance,
                key,
                "Instance #" + record.InstanceId,
                new Color(0.48f, 0.38f, 0.86f),
                record.Status);
            if (node.Count == 0)
            {
                node.Lines.Add("Instance ID: " + record.InstanceId);
                node.Lines.Add("GameObject Path: " + (detailed
                    ? Safe(record.GameObjectPath)
                    : ShortText(Safe(record.GameObjectPath), 32)));
            }

            node.AddRecord(record.Status, selected);
            return node;
        }

        private static TimelineGraphNode AddInstancePayloadNode(
            TimelineGraphModel model,
            TimelineGraphNode container,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            TimelineGraphNode node = model.AddPayloadChild(
                container,
                GraphStage.Instance,
                record,
                selected);
            node.Lines.Add("Renderer ID: " + record.Identity.Binding.RendererId);
            node.Lines.Add("Material Slot: " + record.Identity.Binding.MaterialSlot);
            if (detailed)
            {
                node.Lines.Add("Renderer Path: " + Safe(record.RendererPath));
                node.Lines.Add("Renderer Path ID: " + Safe(record.Identity.Binding.RendererPathId));
                node.Lines.Add("Material Trace ID: " + Safe(record.Identity.Binding.MaterialTraceId));
            }
            return node;
        }

        private static TimelineGraphNode AddFeatureContainer(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool detailed)
        {
            TimelineGraphNode node = model.GetOrAddContainerNode(
                GraphStage.Feature,
                "feature",
                "MatDataTransfer Feature",
                StatusColor(ParamWriteStatus.Submitted),
                record.Status);
            node.Lines.Add("Frame: " + record.FrameIndex);
            if (detailed)
                node.Lines.Add("Time: " + record.TimeSinceStartup.ToString("0.000") + "s");
            return node;
        }

        private static TimelineGraphNode AddFeaturePayloadNode(
            TimelineGraphModel model,
            TimelineGraphNode container,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            container.AddRecord(record.Status, selected);
            TimelineGraphNode node = model.AddPayloadChild(
                container,
                GraphStage.Feature,
                record,
                selected);
            node.Lines.Add("Provider: " + Safe(record.ProviderName));
            node.Lines.Add("Semantic Key: " + Safe(record.Identity.SemanticKey));
            if (detailed)
            {
                node.Lines.Add("Display Name: " + Safe(record.InspectorDisplayName));
                node.Lines.Add("Layer: " + record.WriteConfig.Layer);
                node.Lines.Add("Priority: " + record.WriteConfig.Priority);
                node.Lines.Add("Submit Trace: " + Safe(record.SubmitLogSummary));
            }
            return node;
        }

        private static TimelineGraphNode AddResolveNode(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            TimelineGraphNode node = CreateSequenceNode(
                model,
                GraphStage.Resolve,
                record,
                "Resolve Payload",
                selected);
            node.Lines.Add("Status: " + record.Status);
            node.Lines.Add("Result Code: " + GetResultCode(record));
            node.Lines.Add("Property: " + Safe(record.Binding.PropertyName));
            if (detailed)
            {
                node.Lines.Add("Stage: " + GetStage(record));
                node.Lines.Add("Message: " + GetMessage(record));
                node.Lines.Add("Overridden By: " + Safe(record.Step != null ? record.Step.OverriddenBySourceId : null));
                node.Lines.Add("Matched Semantic: " + Safe(record.Binding.MatchedSemanticKey));
                node.Lines.Add("Shader: " + Safe(record.Binding.ShaderName));
                node.Lines.Add("Catalog: " + Safe(record.Binding.CatalogName));
                node.Lines.Add("Property ID: " + record.Binding.PropertyId);
            }
            return node;
        }

        private static TimelineGraphNode AddWriterNode(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            TimelineGraphNode node = CreateSequenceNode(
                model,
                GraphStage.Writer,
                record,
                "Writer Payload",
                selected);
            node.Lines.Add("Write Method: " + record.WriteMethod);
            node.Lines.Add("Property: " + Safe(record.Binding.PropertyName));
            node.Lines.Add("Value: " + Safe(record.ValuePreview));
            if (detailed)
            {
                node.Lines.Add("Renderer ID: " + record.Identity.Binding.RendererId);
                node.Lines.Add("Material Slot: " + record.Identity.Binding.MaterialSlot);
                node.Lines.Add("Property ID: " + record.Binding.PropertyId);
                node.Lines.Add("Value Hash: " + record.ValueHash.ToString("X16"));
            }
            return node;
        }

        private static TimelineGraphNode AddApplyNode(
            TimelineGraphModel model,
            MatDataTransferTimelineRecord record,
            bool selected,
            bool detailed)
        {
            TimelineGraphNode node = CreateSequenceNode(
                model,
                GraphStage.Apply,
                record,
                "Apply Result",
                selected);
            node.Lines.Add("Status: " + record.Status);
            node.Lines.Add("Applied: " + (record.Step != null && record.Step.IsApplied));
            if (detailed)
            {
                node.Lines.Add("Stage: " + GetStage(record));
                node.Lines.Add("Result Code: " + GetResultCode(record));
                node.Lines.Add("Message: " + GetMessage(record));
                node.Lines.Add("Write Method: " + record.WriteMethod);
            }
            return node;
        }

        private static TimelineGraphNode CreateSequenceNode(
            TimelineGraphModel model,
            GraphStage stage,
            MatDataTransferTimelineRecord record,
            string title,
            bool selected,
            Color? accent = null)
        {
            TimelineGraphNode node = model.GetOrAddNode(
                stage,
                BuildRecordKey(record),
                "Seq " + record.Sequence + " - " + title,
                accent ?? StatusColor(record.Status),
                record.Status,
                null);
            node.AddRecord(record.Status, selected);
            return node;
        }


        private enum GraphStage
        {
            Submit,
            Instance,
            Feature,
            Resolve,
            Writer,
            Apply
        }

        private sealed class TimelineGraphModel
        {
            public readonly List<TimelineGraphNode> Nodes = new List<TimelineGraphNode>();
            public readonly List<TimelineGraphEdge> Edges = new List<TimelineGraphEdge>();
            public readonly List<TimelineGraphRow> Rows = new List<TimelineGraphRow>();
            public readonly List<TimelineGraphNode>[] StageNodes =
            {
                new List<TimelineGraphNode>(),
                new List<TimelineGraphNode>(),
                new List<TimelineGraphNode>(),
                new List<TimelineGraphNode>(),
                new List<TimelineGraphNode>(),
                new List<TimelineGraphNode>()
            };

            private readonly Dictionary<string, TimelineGraphNode> m_NodeLookup =
                new Dictionary<string, TimelineGraphNode>();
            private readonly Dictionary<string, TimelineGraphEdge> m_EdgeLookup =
                new Dictionary<string, TimelineGraphEdge>();

            public int FrameRecordCount;
            public int VisibleRecordCount;
            public TimelineGraphNode FeatureContainer;

            public int GetStageCount(GraphStage stage)
            {
                return StageNodes[(int)stage].Count;
            }

            public TimelineGraphNode GetOrAddNode(
                GraphStage stage,
                string key,
                string title,
                Color accent,
                ParamWriteStatus status,
                string filterToken)
            {
                string lookupKey = stage + ":" + key;
                if (m_NodeLookup.TryGetValue(lookupKey, out TimelineGraphNode node))
                    return node;

                node = new TimelineGraphNode
                {
                    Stage = stage,
                    Key = lookupKey,
                    Title = title,
                    Accent = accent,
                    Status = status,
                    FilterToken = filterToken
                };
                m_NodeLookup.Add(lookupKey, node);
                Nodes.Add(node);
                StageNodes[(int)stage].Add(node);
                return node;
            }

            public TimelineGraphNode GetOrAddContainerNode(
                GraphStage stage,
                string key,
                string title,
                Color accent,
                ParamWriteStatus status)
            {
                TimelineGraphNode node = GetOrAddNode(
                    stage,
                    key,
                    title,
                    accent,
                    status,
                    null);
                node.IsContainer = true;
                return node;
            }

            public TimelineGraphNode AddPayloadChild(
                TimelineGraphNode parent,
                GraphStage stage,
                MatDataTransferTimelineRecord record,
                bool selected)
            {
                TimelineGraphNode node = new TimelineGraphNode
                {
                    Stage = stage,
                    Key = stage + ":payload:" + BuildRecordKey(record),
                    Title = "Seq " + record.Sequence + " Payload",
                    Accent = StatusColor(record.Status),
                    Status = record.Status,
                    Parent = parent,
                    IsPayloadChild = true
                };
                node.AddRecord(record.Status, selected);
                parent.Children.Add(node);
                Nodes.Add(node);
                return node;
            }

            public void AddEdge(TimelineGraphNode from, TimelineGraphNode to, ParamWriteStatus status, bool selected)
            {
                if (from == null || to == null)
                    return;

                string key = from.Key + "->" + to.Key;
                if (!m_EdgeLookup.TryGetValue(key, out TimelineGraphEdge edge))
                {
                    edge = new TimelineGraphEdge
                    {
                        From = from,
                        To = to,
                        Status = status
                    };
                    m_EdgeLookup.Add(key, edge);
                    Edges.Add(edge);
                }

                edge.Count++;
                edge.Selected |= selected;
                edge.Status = MergeStatus(edge.Status, status);
            }
        }

        private sealed class TimelineGraphNode
        {
            public GraphStage Stage;
            public string Key;
            public string Title;
            public Color Accent;
            public ParamWriteStatus Status;
            public string FilterToken;
            public Rect Rect;
            public int Count;
            public bool Selected;
            public bool IsContainer;
            public bool IsPayloadChild;
            public TimelineGraphNode Parent;
            public readonly List<string> Lines = new List<string>();
            public readonly List<TimelineGraphNode> Children = new List<TimelineGraphNode>();

            public void AddRecord(ParamWriteStatus status, bool selected)
            {
                Count++;
                Selected |= selected;
                Status = MergeStatus(Status, status);
            }
        }

        private sealed class TimelineGraphRow
        {
            public TimelineGraphNode Source;
            public TimelineGraphNode InstanceContainer;
            public TimelineGraphNode InstancePayload;
            public TimelineGraphNode FeaturePayload;
            public TimelineGraphNode Resolve;
            public TimelineGraphNode Writer;
            public TimelineGraphNode Apply;
        }

        private sealed class TimelineGraphEdge
        {
            public TimelineGraphNode From;
            public TimelineGraphNode To;
            public ParamWriteStatus Status;
            public int Count;
            public bool Selected;
        }

        private static ParamWriteStatus MergeStatus(ParamWriteStatus current, ParamWriteStatus next)
        {
            if (IsFailedStatus(current) || IsFailedStatus(next))
                return IsFailedStatus(current) ? current : next;
            if (current == ParamWriteStatus.Overridden || next == ParamWriteStatus.Overridden)
                return ParamWriteStatus.Overridden;
            if (current == ParamWriteStatus.Applied || next == ParamWriteStatus.Applied)
                return ParamWriteStatus.Applied;
            if (current == ParamWriteStatus.Queued || next == ParamWriteStatus.Queued)
                return ParamWriteStatus.Queued;
            return ParamWriteStatus.Submitted;
        }
    }
}
