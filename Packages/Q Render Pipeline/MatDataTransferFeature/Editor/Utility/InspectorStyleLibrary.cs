using UnityEditor;
using UnityEngine;
using System;

namespace Rendering.MatDataTransfer.Editor
{
    public static class InspectorStyleLibrary
    {
        private const float DefaultLabelWidth = 88f;
        private const float FoldoutSummaryGap = 8f;
        public const float FoldoutPanelLeftPadding = 14f;
        private static readonly Color ListItemEvenColor = EditorGUIUtility.isProSkin
            ? new Color(0.20f, 0.20f, 0.20f, 1f)
            : new Color(0.89f, 0.89f, 0.89f, 1f);
        private static readonly Color ListItemOddColor = EditorGUIUtility.isProSkin
            ? new Color(0.30f, 0.30f, 0.30f, 1f)
            : new Color(0.83f, 0.83f, 0.83f, 1f);
        private static readonly Color DescriptionColor = EditorGUIUtility.isProSkin
            ? new Color(0.64f, 0.64f, 0.64f)
            : new Color(0.42f, 0.42f, 0.42f);

        private static GUIStyle s_Title;
        private static GUIStyle s_ParameterName;
        private static GUIStyle s_Description;
        private static GUIStyle s_RightAlignedDescription;
        private static GUIStyle s_Foldout;
        private static GUIStyle s_ListContainer;
        private static GUIStyle s_ListItemEven;
        private static GUIStyle s_ListItemOdd;
        private static Texture2D s_ListItemEvenTexture;
        private static Texture2D s_ListItemOddTexture;

        public static GUIStyle Title
        {
            get
            {
                if (s_Title == null)
                {
                    s_Title = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        clipping = TextClipping.Clip
                    };
                }

                return s_Title;
            }
        }

        public static GUIStyle ParameterName
        {
            get
            {
                if (s_ParameterName == null)
                {
                    s_ParameterName = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = FontStyle.Normal,
                        clipping = TextClipping.Clip
                    };
                }

                return s_ParameterName;
            }
        }

        public static GUIStyle Description
        {
            get
            {
                if (s_Description == null)
                {
                    s_Description = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = DescriptionColor },
                        active = { textColor = DescriptionColor },
                        focused = { textColor = DescriptionColor },
                        hover = { textColor = DescriptionColor },
                        wordWrap = true,
                        clipping = TextClipping.Clip
                    };
                }

                return s_Description;
            }
        }

        public static GUIStyle RightAlignedDescription
        {
            get
            {
                if (s_RightAlignedDescription == null)
                {
                    s_RightAlignedDescription = new GUIStyle(Description)
                    {
                        alignment = TextAnchor.MiddleRight,
                        wordWrap = false
                    };
                }

                return s_RightAlignedDescription;
            }
        }

        public static GUIStyle Foldout
        {
            get
            {
                if (s_Foldout == null)
                {
                    s_Foldout = new GUIStyle(EditorStyles.foldout)
                    {
                        fontStyle = FontStyle.Bold,
                        clipping = TextClipping.Clip
                    };
                }

                return s_Foldout;
            }
        }

        public static void DrawTitle(string text)
        {
            EditorGUILayout.LabelField(text, Title);
        }

        public static Rect GetIndentedControlRectLayout()
        {
            return GetIndentedControlRect();
        }

        public static void DrawDescription(string text)
        {
            Rect row = GetIndentedControlRect();
            GUI.Label(row, text, Description);
        }

        public static void DrawParameterValue(string label, string value, float labelWidth = DefaultLabelWidth)
        {
            Rect row = GetIndentedControlRect();
            DrawParameterValue(row, label, value, labelWidth);
        }

        public static void DrawParameterValue(
            Rect row,
            string label,
            string value,
            float labelWidth = DefaultLabelWidth)
        {
            DrawValueRow(row, label, value, labelWidth, EditorStyles.label, false, false, false);
        }

        public static void DrawCopyableParameterValue(
            string label,
            string value,
            float labelWidth = DefaultLabelWidth,
            bool preferPathSegments = false)
        {
            Rect row = GetIndentedControlRect();
            DrawCopyableParameterValue(row, label, value, labelWidth, preferPathSegments);
        }

        public static void DrawCopyableParameterValue(
            Rect row,
            string label,
            string value,
            float labelWidth = DefaultLabelWidth,
            bool preferPathSegments = false)
        {
            DrawValueRow(row, label, value, labelWidth, EditorStyles.label, preferPathSegments, true, false);
        }

        public static void DrawTailLabel(
            Rect rect,
            string text,
            GUIStyle style,
            bool preferPathSegments)
        {
            InspectorTailLabel.Draw(rect, text, style, preferPathSegments);
        }

        public static void DrawCopyableTailLabelLayout(
            string text,
            GUIStyle style,
            bool preferPathSegments)
        {
            Rect row = GetIndentedControlRect();
            DrawCopyableTailLabel(row, text, style, preferPathSegments);
        }

        public static void DrawCopyableTailLabel(
            Rect rect,
            string text,
            GUIStyle style,
            bool preferPathSegments)
        {
            DrawTailLabel(rect, text, style, preferPathSegments);
            HandleCopyContextMenu(rect, text);
        }

        public static void DrawCopyableReadOnlyTailText(string label, string value)
        {
            Rect row = GetIndentedControlRect();
            DrawCopyableReadOnlyTailText(row, label, value, DefaultLabelWidth);
        }

        public static void DrawCopyableReadOnlyTailText(
            string label,
            string value,
            float labelWidth)
        {
            Rect row = GetIndentedControlRect();
            DrawCopyableReadOnlyTailText(row, label, value, labelWidth);
        }

        public static void DrawCopyableReadOnlyTailText(
            Rect row,
            string label,
            string value,
            float labelWidth = DefaultLabelWidth)
        {
            DrawValueRow(row, label, value, labelWidth, EditorStyles.textField, false, true, true);
        }

        private static void DrawValueRow(
            Rect row,
            string label,
            string value,
            float labelWidth,
            GUIStyle valueStyle,
            bool preferPathSegments,
            bool copyable,
            bool readOnly)
        {
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect valueRect = new Rect(
                labelRect.xMax,
                row.y,
                Mathf.Max(0f, row.xMax - labelRect.xMax),
                row.height);

            GUI.Label(labelRect, label, ParameterName);
            if (readOnly)
            {
                using (new EditorGUI.DisabledScope(true))
                    DrawTailLabel(valueRect, value, valueStyle, preferPathSegments);
            }
            else
            {
                DrawTailLabel(valueRect, value, valueStyle, preferPathSegments);
            }

            if (copyable)
                HandleCopyContextMenu(valueRect, value);
        }

        public static bool DrawFoldoutLayout(
            bool expanded,
            string text,
            bool preferPathSegments,
            float leftPadding = 0f)
        {
            return DrawFoldoutLayout(expanded, text, null, preferPathSegments, leftPadding);
        }

        public static bool DrawFoldoutLayout(
            bool expanded,
            string text,
            string summary = null,
            bool preferPathSegments = false,
            float leftPadding = 0f)
        {
            Rect row = GetIndentedControlRect();
            return DrawFoldout(AddLeftPadding(row, leftPadding), expanded, text, summary, preferPathSegments);
        }

        public static bool DrawFoldout(
            Rect rect,
            bool expanded,
            string text,
            bool preferPathSegments)
        {
            return DrawFoldout(rect, expanded, text, null, preferPathSegments);
        }

        public static bool DrawFoldout(
            Rect rect,
            bool expanded,
            string text,
            string summary = null,
            bool preferPathSegments = false)
        {
            string summaryText = summary ?? string.Empty;
            float summaryWidth = GetSummaryWidth(summaryText, rect.width);
            float gap = summaryWidth > 0f ? FoldoutSummaryGap : 0f;
            Rect summaryRect = new Rect(
                rect.xMax - summaryWidth,
                rect.y,
                summaryWidth,
                rect.height);
            Rect foldoutRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rect.width - summaryWidth - gap),
                rect.height);

            bool nextExpanded = DrawFoldoutContent(foldoutRect, expanded, text, preferPathSegments);
            if (!string.IsNullOrEmpty(summaryText))
                GUI.Label(summaryRect, new GUIContent(summaryText, summaryText), RightAlignedDescription);

            return nextExpanded;
        }

        private static bool DrawFoldoutContent(
            Rect rect,
            bool expanded,
            string text,
            bool preferPathSegments)
        {
            string fullText = text ?? string.Empty;
            GUIStyle style = Foldout;
            string displayText = InspectorTailLabel.BuildText(
                fullText,
                style,
                rect.width,
                preferPathSegments);

            return EditorGUI.Foldout(
                rect,
                expanded,
                new GUIContent(displayText, fullText),
                true,
                style);
        }

        private static Rect AddLeftPadding(Rect rect, float leftPadding)
        {
            float padding = Mathf.Max(0f, leftPadding);
            rect.x += padding;
            rect.width = Mathf.Max(0f, rect.width - padding);
            return rect;
        }

        public static IDisposable BeginPanelLayout()
        {
            return new PanelScope();
        }

        public static IDisposable BeginAlternatingListLayout()
        {
            return new ListScope();
        }

        public static IDisposable BeginAlternatingListItemLayout(int itemIndex)
        {
            return new ListItemScope(itemIndex);
        }

        public static IDisposable BeginIndentedPanelLayout(float leftPadding)
        {
            return new IndentedPanelScope(leftPadding, null);
        }

        public static IDisposable BeginIndentedPanelLayout(float leftPadding, int contentLeftPadding)
        {
            return new IndentedPanelScope(leftPadding, Mathf.Max(0, contentLeftPadding));
        }

        private static Rect GetIndentedControlRect()
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            return EditorGUI.IndentedRect(row);
        }

        private static void HandleCopyContextMenu(Rect rect, string value)
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.ContextClick ||
                !rect.Contains(current.mousePosition))
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            if (string.IsNullOrEmpty(value))
                menu.AddDisabledItem(new GUIContent("Copy"));
            else
                menu.AddItem(new GUIContent("Copy"), false, CopyText, value);

            menu.ShowAsContext();
            current.Use();
        }

        private static void CopyText(object userData)
        {
            EditorGUIUtility.systemCopyBuffer = userData as string ?? string.Empty;
        }

        private static float GetSummaryWidth(string summary, float maxWidth)
        {
            if (string.IsNullOrEmpty(summary))
                return 0f;

            float desiredWidth = Mathf.Ceil(Description.CalcSize(new GUIContent(summary)).x);
            float availableWidth = Mathf.Max(0f, maxWidth * 0.45f);
            return Mathf.Min(desiredWidth, availableWidth);
        }

        private static GUIStyle ListContainer
        {
            get
            {
                if (s_ListContainer == null)
                {
                    s_ListContainer = new GUIStyle(GUIStyle.none)
                    {
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0)
                    };
                }

                return s_ListContainer;
            }
        }

        private static GUIStyle GetListItemStyle(int itemIndex)
        {
            bool isEven = itemIndex % 2 == 0;
            GUIStyle style = isEven ? s_ListItemEven : s_ListItemOdd;
            if (style != null)
                return style;

            style = new GUIStyle(GUIStyle.none)
            {
                padding = new RectOffset(0, 0, 2, 2),
                margin = new RectOffset(0, 0, 0, 1),
                normal =
                {
                    background = isEven
                        ? GetListItemEvenTexture()
                        : GetListItemOddTexture()
                }
            };

            if (isEven)
                s_ListItemEven = style;
            else
                s_ListItemOdd = style;

            return style;
        }

        private static Texture2D GetListItemEvenTexture()
        {
            if (s_ListItemEvenTexture == null)
                s_ListItemEvenTexture = CreateSolidTexture(ListItemEvenColor);

            return s_ListItemEvenTexture;
        }

        private static Texture2D GetListItemOddTexture()
        {
            if (s_ListItemOddTexture == null)
                s_ListItemOddTexture = CreateSolidTexture(ListItemOddColor);

            return s_ListItemOddTexture;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private sealed class PanelScope : IDisposable
        {
            private readonly EditorGUILayout.VerticalScope m_Scope;
            private readonly int m_IndentLevel;

            public PanelScope()
            {
                m_IndentLevel = EditorGUI.indentLevel;
                m_Scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                EditorGUI.indentLevel = 0;
            }

            public void Dispose()
            {
                EditorGUI.indentLevel = m_IndentLevel;
                m_Scope.Dispose();
            }
        }

        private sealed class ListScope : IDisposable
        {
            private readonly EditorGUILayout.VerticalScope m_Scope;

            public ListScope()
            {
                m_Scope = new EditorGUILayout.VerticalScope(ListContainer, GUILayout.ExpandWidth(true));
            }

            public void Dispose()
            {
                m_Scope.Dispose();
            }
        }

        private sealed class ListItemScope : IDisposable
        {
            private readonly EditorGUILayout.VerticalScope m_Scope;

            public ListItemScope(int itemIndex)
            {
                m_Scope = new EditorGUILayout.VerticalScope(GetListItemStyle(itemIndex), GUILayout.ExpandWidth(true));
            }

            public void Dispose()
            {
                m_Scope.Dispose();
            }
        }

        private sealed class IndentedPanelScope : IDisposable
        {
            private readonly EditorGUILayout.HorizontalScope m_HorizontalScope;
            private readonly EditorGUILayout.VerticalScope m_PanelScope;
            private readonly int m_IndentLevel;

            public IndentedPanelScope(float leftPadding, int? contentLeftPadding)
            {
                m_IndentLevel = EditorGUI.indentLevel;
                m_HorizontalScope = new EditorGUILayout.HorizontalScope();
                GUILayout.Space(Mathf.Max(0f, leftPadding));
                m_PanelScope = new EditorGUILayout.VerticalScope(CreatePanelStyle(contentLeftPadding));
                EditorGUI.indentLevel = 0;
            }

            public void Dispose()
            {
                EditorGUI.indentLevel = m_IndentLevel;
                m_PanelScope.Dispose();
                m_HorizontalScope.Dispose();
            }

            private static GUIStyle CreatePanelStyle(int? contentLeftPadding)
            {
                if (!contentLeftPadding.HasValue)
                    return EditorStyles.helpBox;

                GUIStyle style = new GUIStyle(EditorStyles.helpBox);
                style.padding.left = contentLeftPadding.Value;
                return style;
            }
        }
    }
}
