using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public static class ShaderGUIButtonUtility
    {
        public static bool DrawButton<TContext>(
            Rect rect,
            IShaderGUIButton<TContext> button,
            TContext context,
            GUIStyle style)
        {
            if (button == null)
                return false;

            var changed = DrawToggle(
                rect,
                button.IsHighlighted(context),
                button.Content,
                style,
                button.IsEnabled(context),
                out _);

            if (!changed)
                return false;

            button.OnClick(context);
            return true;
        }

        public static bool DrawMomentaryButton(
            Rect rect,
            GUIContent content,
            GUIStyle style,
            bool enabled = true)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                return GUI.Button(rect, content, style);
            }
        }

        public static bool DrawToggle(
            Rect rect,
            bool selected,
            GUIContent content,
            GUIStyle style,
            bool enabled,
            out bool nextSelected)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            using (new GUIBackgroundColorScope(selected ? ShaderGUIStyleRegistry.GetButtonHighlightColor() : GUI.backgroundColor))
            {
                EditorGUI.BeginChangeCheck();
                nextSelected = GUI.Toggle(rect, selected, content, style);
                return EditorGUI.EndChangeCheck();
            }
        }

        private readonly struct GUIBackgroundColorScope : System.IDisposable
        {
            private readonly Color _oldColor;

            public GUIBackgroundColorScope(Color color)
            {
                _oldColor = GUI.backgroundColor;
                GUI.backgroundColor = color;
            }

            public void Dispose()
            {
                GUI.backgroundColor = _oldColor;
            }
        }
    }
}
