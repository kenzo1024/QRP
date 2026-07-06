using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public sealed partial class MatDataTransferTimelineViewer
    {
        private const float TimelineButtonRadius = 4f;

        private static GUIStyle s_TimelineButton;
        private static GUIStyle s_TimelineLabel;
        private static GUIStyle s_TimelineRightLabel;
        private static readonly Dictionary<int, Texture2D> s_RoundedTextureCache =
            new Dictionary<int, Texture2D>();

        private static void DrawNode(Rect rect, string title, string[] body, Color accent, bool selected, string badge = null)
        {
            EditorGUI.DrawRect(rect, selected ? new Color(0.24f, 0.28f, 0.33f) : new Color(0.22f, 0.25f, 0.28f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            if (selected)
                DrawRectOutline(rect, new Color(0.35f, 0.65f, 1f), 2f);

            Rect badgeRect = Rect.zero;
            if (!string.IsNullOrEmpty(badge))
            {
                float badgeWidth = Mathf.Clamp(TimelineLabel.CalcSize(new GUIContent(badge)).x + 20f, 58f, 96f);
                badgeRect = new Rect(rect.xMax - badgeWidth - 10f, rect.y + 8f, badgeWidth, 18f);
            }

            Rect titleRect = new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, 18f);
            if (!string.IsNullOrEmpty(badge))
                titleRect.width = Mathf.Max(0f, badgeRect.x - titleRect.x - 8f);

            InspectorStyleLibrary.DrawTailLabel(titleRect, title, EditorStyles.boldLabel, false);
            for (int i = 0; i < body.Length; i++)
            {
                Rect bodyRect = new Rect(rect.x + 14f, rect.y + 34f + i * 18f, rect.width - 28f, 16f);
                InspectorStyleLibrary.DrawTailLabel(bodyRect, body[i], TimelineLabel, false);
            }

            if (!string.IsNullOrEmpty(badge))
                DrawPayloadChip(badgeRect, badge, accent, selected);
        }

        private static void DrawPayloadChip(Rect rect, string text, Color color, bool active)
        {
            DrawRoundedBackground(
                rect,
                active ? new Color(0.22f, 0.26f, 0.24f) : new Color(0.18f, 0.18f, 0.18f),
                active ? new Color(0.36f, 0.42f, 0.38f) : new Color(0.29f, 0.29f, 0.29f));
            EditorGUI.DrawRect(new Rect(rect.x + 5f, rect.y + 5f, 4f, Mathf.Max(0f, rect.height - 10f)), color);
            Rect labelRect = new Rect(rect.x + 14f, rect.y + 1f, rect.width - 20f, rect.height - 2f);
            InspectorStyleLibrary.DrawTailLabel(labelRect, text, TimelineLabel, false);
        }

        private float DrawFilterChip(
            Rect rect,
            string text,
            Color color,
            TimelineStatusFilter filter)
        {
            bool active = m_StatusFilter == filter;
            Color previousColor = GUI.color;
            GUI.color = active ? Color.white : new Color(0.86f, 0.86f, 0.86f);
            bool clicked = DrawTimelineToggle(rect, active, text);
            GUI.color = previousColor;

            DrawFilterAccent(rect, color, active);
            if (clicked != active)
                SetStatusFilter(active ? TimelineStatusFilter.All : filter);

            return rect.width;
        }

        private static void DrawFilterAccent(Rect rect, Color color, bool active)
        {
            EditorGUI.DrawRect(new Rect(rect.x + 6f, rect.y + rect.height * 0.5f - 3f, 6f, 6f), color);
        }

        private void SetStatusFilter(TimelineStatusFilter filter)
        {
            if (m_StatusFilter == filter)
                return;

            m_StatusFilter = filter;
            ClearSelectedRecord();
            m_RecordScroll = Vector2.zero;
            Repaint();
        }

        private static void DrawNodeConnection(Rect from, Rect to, Color color, float width)
        {
            Vector2 start = new Vector2(from.xMax, from.center.y);
            Vector2 end = new Vector2(to.xMin, to.center.y);
            float midX = Mathf.Lerp(start.x, end.x, 0.5f);

            Handles.color = color;
            if (Mathf.Abs(start.y - end.y) < 0.5f)
            {
                Handles.DrawAAPolyLine(width, start, end);
                DrawArrowHead(end, Vector2.right, color);
                return;
            }

            Vector2 firstCorner = new Vector2(midX, start.y);
            Vector2 secondCorner = new Vector2(midX, end.y);
            Handles.DrawAAPolyLine(width, start, firstCorner, secondCorner, end);
            DrawArrowHead(end, Vector2.right, color);
        }

        private static void DrawArrowHead(Vector2 tip, Vector2 direction, Color color)
        {
            Vector2 dir = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector2.right;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                tip,
                tip - dir * 12f + side * 5f,
                tip - dir * 12f - side * 5f);
        }

        private static Rect CenterBetween(Rect left, Rect right, float width, float height)
        {
            float x = Mathf.Lerp(left.xMax, right.xMin, 0.5f) - width * 0.5f;
            float y = Mathf.Lerp(left.center.y, right.center.y, 0.5f) - height * 0.5f;
            return new Rect(x, y, width, height);
        }

        private static void DrawConnectionLabel(
            Rect left,
            Rect right,
            string text,
            Color color,
            LabelLane lane,
            bool active)
        {
            const float width = 170f;
            const float height = 20f;
            float gap = right.xMin - left.xMax;
            if (gap < width + 18f)
                return;

            Rect rect = CenterBetween(left, right, width, height);
            float baseY = Mathf.Lerp(left.center.y, right.center.y, 0.5f);
            rect.y = lane == LabelLane.Above
                ? baseY - 38f
                : baseY + 26f;
            DrawPayloadChip(rect, text, color, active);
        }

        private static bool DrawTimelineButton(Rect rect, string text)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            DrawButtonBackground(rect, false, hover);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            DrawTimelineButtonLabel(rect, text);
            return clicked;
        }

        private static bool DrawTimelineToggle(Rect rect, bool active, string text)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            DrawButtonBackground(rect, active, hover);
            bool next = GUI.Toggle(rect, active, GUIContent.none, GUIStyle.none);
            DrawTimelineButtonLabel(rect, text);
            return next;
        }

        private static void DrawButtonBackground(Rect rect, bool active, bool hover)
        {
            Color fill;
            Color border;
            if (active)
            {
                fill = new Color(0.30f, 0.36f, 0.42f);
                border = new Color(0.35f, 0.65f, 1f);
            }
            else if (hover)
            {
                fill = new Color(0.31f, 0.31f, 0.31f);
                border = new Color(0.45f, 0.45f, 0.45f);
            }
            else
            {
                fill = new Color(0.25f, 0.25f, 0.25f);
                border = new Color(0.34f, 0.34f, 0.34f);
            }

            DrawRoundedBackground(rect, fill, border);
        }

        private static void DrawTimelineButtonLabel(Rect rect, string text)
        {
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            InspectorStyleLibrary.DrawTailLabel(
                new Rect(rect.x + 6f, rect.y + 1f, rect.width - 12f, rect.height - 2f),
                text,
                TimelineButton,
                false);
            GUI.color = previousColor;
        }

        private static GUIStyle TimelineButton
        {
            get
            {
                if (s_TimelineButton == null)
                {
                    s_TimelineButton = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip,
                        fixedHeight = 0f,
                        margin = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                        wordWrap = false
                    };
                    SetButtonTextColors(s_TimelineButton);
                }

                return s_TimelineButton;
            }
        }

        private static GUIStyle TimelineLabel
        {
            get
            {
                if (s_TimelineLabel == null)
                {
                    s_TimelineLabel = new GUIStyle(InspectorStyleLibrary.Description)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        wordWrap = false
                    };
                }

                return s_TimelineLabel;
            }
        }

        private static GUIStyle TimelineRightLabel
        {
            get
            {
                if (s_TimelineRightLabel == null)
                {
                    s_TimelineRightLabel = new GUIStyle(TimelineLabel)
                    {
                        alignment = TextAnchor.MiddleRight
                    };
                }

                return s_TimelineRightLabel;
            }
        }

        private static void DrawRoundedBackground(Rect rect, Color fill, Color border)
        {
            Rect aligned = AlignToPixel(rect);
            int width = Mathf.Max(1, Mathf.RoundToInt(aligned.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(aligned.height));
            Texture2D texture = GetRoundedTexture(width, height, fill, border);
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(aligned, texture, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
        }

        private static Texture2D GetRoundedTexture(int width, int height, Color fill, Color border)
        {
            int key = width;
            key = (key * 397) ^ height;
            key = (key * 397) ^ ColorKey(fill);
            key = (key * 397) ^ ColorKey(border);
            if (!s_RoundedTextureCache.TryGetValue(key, out Texture2D texture))
            {
                texture = CreateRoundedTexture(width, height, fill, border);
                s_RoundedTextureCache.Add(key, texture);
            }

            return texture;
        }

        private static Texture2D CreateRoundedTexture(int width, int height, Color fill, Color border)
        {
            const float radius = TimelineButtonRadius;
            const float borderWidth = 1.25f;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float distance = GetRoundedDistance(x + 0.5f, y + 0.5f, width, height, radius);
                    float edgeAlpha = Mathf.Clamp01(radius + 0.5f - distance);
                    if (edgeAlpha <= 0f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float borderBlend = Mathf.Clamp01(distance - (radius - borderWidth));
                    Color color = Color.Lerp(fill, border, borderBlend);
                    color.a *= edgeAlpha;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }

        private static float GetRoundedDistance(float x, float y, int width, int height, float radius)
        {
            float cx = x < radius ? radius : x > width - radius ? width - radius : x;
            float cy = y < radius ? radius : y > height - radius ? height - radius : y;
            return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
        }

        private static Rect AlignToPixel(Rect rect)
        {
            float x = Mathf.Round(rect.x);
            float y = Mathf.Round(rect.y);
            float xMax = Mathf.Round(rect.xMax);
            float yMax = Mathf.Round(rect.yMax);
            return new Rect(x, y, Mathf.Max(1f, xMax - x), Mathf.Max(1f, yMax - y));
        }

        private static int ColorKey(Color color)
        {
            unchecked
            {
                int r = Mathf.RoundToInt(color.r * 255f);
                int g = Mathf.RoundToInt(color.g * 255f);
                int b = Mathf.RoundToInt(color.b * 255f);
                int a = Mathf.RoundToInt(color.a * 255f);
                return r | (g << 8) | (b << 16) | (a << 24);
            }
        }

        private static void SetButtonTextColors(GUIStyle style)
        {
            Color normal = EditorGUIUtility.isProSkin ? new Color(0.78f, 0.78f, 0.78f) : Color.black;
            Color active = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.96f, 1f) : Color.black;
            style.normal.textColor = normal;
            style.hover.textColor = active;
            style.active.textColor = active;
            style.focused.textColor = active;
            style.onNormal.textColor = active;
            style.onHover.textColor = active;
            style.onActive.textColor = active;
            style.onFocused.textColor = active;
        }

        private static void DrawRectOutline(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

    }
}
