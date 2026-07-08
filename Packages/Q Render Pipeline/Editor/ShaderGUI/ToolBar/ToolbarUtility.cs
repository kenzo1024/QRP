using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// Handles QShaderGUI toolbar layout, rendering and button registration.
    /// </summary>
    public static class ToolbarUtility
    {
        private static readonly List<IShaderGUIToolbarButton> ToolbarButtons = new()
        {
            new DisplayModeToolbarButton()
        };

        public static void AddButton(IShaderGUIToolbarButton button)
        {
            if (button == null || ToolbarButtons.Contains(button))
                return;

            ToolbarButtons.Add(button);
        }

        public static bool RemoveButton(IShaderGUIToolbarButton button)
        {
            return button != null && ToolbarButtons.Remove(button);
        }

        public static void DrawToolbar(ShaderGUIMetaData metaData)
        {
            if (metaData == null)
                return;

            var toolbarStyle = ShaderGUIStyleRegistry.GetToolbarStyle();
            DrawToolbarButtons(GetFullWidthToolbarRect(toolbarStyle.MainHeight), metaData);
            DrawSubToolbars(metaData);
        }

        public static void DrawToolbarButtons(Rect toolbarRect, ShaderGUIMetaData metaData)
        {
            var context = CreateContext(metaData);
            var style = context.ToolbarStyle;
            var barRect = GetBarRect(toolbarRect);

            GUI.Box(barRect, GUIContent.none, style.MainBarStyle);

            var buttonTop = barRect.y + Mathf.Max(0f, (barRect.height - style.ButtonSize) * 0.5f);
            var cursorX = barRect.x + style.ContentPadding;
            foreach (var button in ToolbarButtons)
            {
                var buttonRect = new Rect(cursorX, buttonTop, style.ButtonSize, style.ButtonSize);
                if (buttonRect.xMax > barRect.xMax)
                    break;

                DrawButton(buttonRect, button, context);
                cursorX += style.ButtonSize + style.ButtonSpacing;
            }
        }

        private static void DrawSubToolbars(ShaderGUIMetaData metaData)
        {
            var context = CreateContext(metaData);
            var style = context.ToolbarStyle;

            foreach (var button in ToolbarButtons)
            {
                if (!button.ShouldDrawSubToolbar(context))
                    continue;

                var subBarRect = GetBarRect(GetFullWidthToolbarRect(style.SubHeight));
                GUI.Box(subBarRect, GUIContent.none, style.SubBarStyle);

                var contentRect = new Rect(
                    subBarRect.x + style.ContentPadding,
                    subBarRect.y + 3f,
                    Mathf.Max(0f, subBarRect.width - style.ContentPadding * 2f),
                    Mathf.Max(0f, subBarRect.height - 6f));

                button.DrawSubToolbar(contentRect, context);
            }
        }

        private static void DrawButton(Rect buttonRect, IShaderGUIToolbarButton button, ShaderGUIToolbarContext context)
        {
            ShaderGUIButtonUtility.DrawButton(buttonRect, button, context, context.ButtonStyle);
        }

        private static Rect GetFullWidthToolbarRect(float height)
        {
            return ShaderGUIStyleRegistry.GetInspectorToolbarRect(height);
        }

        private static Rect GetBarRect(Rect rect)
        {
            return rect;
        }

        private static ShaderGUIToolbarContext CreateContext(ShaderGUIMetaData metaData)
        {
            var toolbarStyle = ShaderGUIStyleRegistry.GetToolbarStyle();
            return new ShaderGUIToolbarContext(
                metaData,
                toolbarStyle,
                toolbarStyle.ButtonStyle,
                toolbarStyle.HighlightedButtonStyle,
                toolbarStyle.OptionToggleStyle);
        }
    }
}
