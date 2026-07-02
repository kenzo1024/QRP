using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    public static class InspectorTailLabel
    {
        private const string Ellipsis = "...";
        private const string PathEllipsis = ".../";

        public static void DrawLayout(string text, GUIStyle style, bool preferPathSegments)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Draw(row, text, style, preferPathSegments);
        }

        public static void Draw(Rect rect, string text, GUIStyle style, bool preferPathSegments)
        {
            string fullText = text ?? string.Empty;
            string displayText = BuildText(fullText, style, rect.width, preferPathSegments);
            GUI.Label(rect, new GUIContent(displayText, fullText), style);
        }

        public static string BuildText(
            string text,
            GUIStyle style,
            float maxWidth,
            bool preferPathSegments)
        {
            if (string.IsNullOrEmpty(text) || style == null)
                return text ?? string.Empty;

            if (FitsWidth(text, style, maxWidth))
                return text;

            if (!preferPathSegments)
                return BuildTailText(text, style, maxWidth);

            string[] segments = text.Split('/');
            if (segments.Length <= 1)
                return BuildTailText(text, style, maxWidth);

            string best = BuildTailText(text, style, maxWidth);
            string tail = string.Empty;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                tail = string.IsNullOrEmpty(tail) ? segments[i] : segments[i] + "/" + tail;
                string candidate = i == 0 ? tail : PathEllipsis + tail;
                if (!FitsWidth(candidate, style, maxWidth))
                    break;

                best = candidate;
            }

            return best;
        }

        private static string BuildTailText(string text, GUIStyle style, float maxWidth)
        {
            for (int length = text.Length; length > 0; length--)
            {
                string candidate = Ellipsis + text.Substring(text.Length - length);
                if (FitsWidth(candidate, style, maxWidth))
                    return candidate;
            }

            return Ellipsis;
        }

        private static bool FitsWidth(string text, GUIStyle style, float maxWidth)
        {
            return style.CalcSize(new GUIContent(text)).x <= maxWidth;
        }
    }
}
