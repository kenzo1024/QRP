using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public static class ShaderGUILayoutRenderer
    {
        public static void Draw(ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            if (metaData?.PerShaderData.LayoutRoot == null || inspector == null)
                return;

            ShaderGUILayoutEvaluator.Evaluate(metaData);

            var rootState = metaData.PerInspectorData.LayoutRuntime.GetState(metaData.PerShaderData.LayoutRoot);
            if (!rootState.IsVisible || rootState.Height <= 0f)
                return;

            var rootRect = ShaderGUIStyleRegistry.GetInspectorContentRect(rootState.Height);
            rootState.Rect = rootRect;
            DrawChildren(rootRect, metaData.PerShaderData.LayoutRoot, metaData, inspector);
        }

        private static void DrawChildren(Rect rect, ShaderGUILayoutNode node, ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            var cursorY = rect.y;
            var first = true;

            foreach (var child in node.Children)
            {
                var childState = metaData.PerInspectorData.LayoutRuntime.GetState(child);
                if (!childState.IsVisible)
                    continue;

                if (!first)
                    cursorY += EditorGUIUtility.standardVerticalSpacing;

                var childRect = new Rect(rect.x, cursorY, rect.width, childState.Height);
                childState.Rect = childRect;
                DrawNode(childRect, child, metaData, inspector);
                cursorY += childState.Height;
                first = false;
            }
        }

        private static void DrawNode(Rect rect, ShaderGUILayoutNode node, ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            switch (node)
            {
                case ShaderGUIPropertyNode propertyNode:
                    DrawProperty(rect, propertyNode, metaData, inspector);
                    break;
                case ShaderGUIGroupNode groupNode:
                    DrawGroup(rect, groupNode, metaData, inspector);
                    break;
                case ShaderGUISplitNode splitNode:
                    DrawSplit(rect, splitNode, metaData, inspector);
                    break;
            }
        }

        private static void DrawGroup(Rect rect, ShaderGUIGroupNode node, ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            var style = ShaderGUIStyleRegistry.GetBoxStyle(node.StyleName);
            var state = metaData.PerInspectorData.LayoutRuntime.GetState(node);
            var hasVisibleChildren = HasVisibleChildren(node, metaData);

            GUI.Box(rect, GUIContent.none, style.OuterStyle);

            var headerRect = new Rect(
                rect.x + style.Padding,
                rect.y + style.Padding,
                Mathf.Max(0f, rect.width - style.Padding * 2f),
                style.HeaderHeight);
            var headerRow = ShaderGUIControlRow.FromContentRect(headerRect);
            DrawHeaderDecorations(headerRow, node, metaData);
            GUI.Box(headerRow.ContentRect, GUIContent.none, style.HeaderStyle);

            var arrowRect = new Rect(
                headerRow.ContentRect.x + 4f,
                headerRow.ContentRect.y + 2f,
                Mathf.Max(0f, style.ArrowColumnWidth),
                Mathf.Max(0f, headerRow.ContentRect.height - 4f));
            var titleX = headerRow.ContentRect.x + style.HeaderTextInset + (style.ShowFoldoutArrow ? style.ArrowColumnWidth : 0f);
            var titleRect = new Rect(
                titleX,
                headerRow.ContentRect.y,
                Mathf.Max(0f, headerRow.ContentRect.xMax - titleX - 6f),
                headerRow.ContentRect.height);
            var nextExpanded = state.IsExpanded;
            if (style.ShowFoldoutArrow)
                nextExpanded = EditorGUI.Foldout(arrowRect, state.IsExpanded, GUIContent.none, true);
            GUI.Label(titleRect, node.Title, style.TitleStyle);
            HandleHeaderClick(headerRow.ContentRect, ref nextExpanded);
            if (nextExpanded != state.IsExpanded)
                state.IsExpanded = nextExpanded;

            if (!state.IsExpanded || !hasVisibleChildren)
                return;

            var contentRect = new Rect(
                headerRow.ContentRect.x,
                headerRow.ContentRect.yMax + style.Spacing + 1f,
                headerRow.ContentRect.width,
                Mathf.Max(0f, rect.yMax - headerRow.ContentRect.yMax - style.Spacing - style.Padding - 1f));
            GUI.Box(contentRect, GUIContent.none, style.BodyStyle);

            var innerRect = new Rect(
                contentRect.x + style.BodyInset,
                contentRect.y + style.Padding,
                Mathf.Max(0f, contentRect.width - style.BodyInset * 2f),
                Mathf.Max(0f, contentRect.height - style.Padding * 2f));
            DrawChildren(innerRect, node, metaData, inspector);
        }

        private static void DrawHeaderDecorations(ShaderGUIControlRow row, ShaderGUIGroupNode node, ShaderGUIMetaData metaData)
        {
            if (node == null || metaData == null || string.IsNullOrEmpty(node.HeaderPropertyName))
                return;

            if (!metaData.PerShaderData.Properties.TryGetValue(node.HeaderPropertyName, out var staticData))
                return;

            ShaderGUIControlRowDecorator.Draw(row, staticData, metaData.PerShaderData.Shader);
        }

        private static void HandleHeaderClick(Rect headerRect, ref bool expanded)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !headerRect.Contains(evt.mousePosition))
                return;

            expanded = !expanded;
            evt.Use();
        }

        private static void DrawSplit(Rect rect, ShaderGUISplitNode node, ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            if (node.Children.Count == 0)
                return;

            var gap = EditorGUIUtility.standardVerticalSpacing * 2f;
            var leftWidth = Mathf.Floor((rect.width - gap) * Mathf.Clamp01(node.LeftRatio));
            var leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            var rightRect = new Rect(leftRect.xMax + gap, rect.y, Mathf.Max(0f, rect.width - leftWidth - gap), rect.height);

            DrawChildren(leftRect, node.Children[0], metaData, inspector);
            if (node.Children.Count > 1)
                DrawChildren(rightRect, node.Children[1], metaData, inspector);
        }

        private static void DrawProperty(Rect rect, ShaderGUIPropertyNode node, ShaderGUIMetaData metaData, MaterialInspector inspector)
        {
            if (!metaData.PerMaterialData.Properties.TryGetValue(node.PropertyName, out var dynamicData)
                || dynamicData.Property == null)
            {
                return;
            }

            inspector.DrawLayoutProperty(rect, dynamicData.Property);
        }

        private static bool HasVisibleChildren(ShaderGUILayoutNode node, ShaderGUIMetaData metaData)
        {
            foreach (var child in node.Children)
            {
                if (metaData.PerInspectorData.LayoutRuntime.GetState(child).IsVisible)
                    return true;
            }

            return false;
        }
    }
}
