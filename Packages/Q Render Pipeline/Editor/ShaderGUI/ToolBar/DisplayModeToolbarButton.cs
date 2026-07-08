using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public sealed class DisplayModeToolbarButton : ShaderGUIToolbarButtonBase
    {
        private const string IconGuid = "9576e23a695b35d49a9fc55c9a948b4f";
        private const float OptionWidth = 128f;
        private const float OptionHeight = 20f;

        private static GUIContent _content;

        public override GUIContent Content => _content ??= CreateContent();

        public override bool IsHighlighted(ShaderGUIToolbarContext context)
        {
            var displayModeData = context.MetaData.PerInspectorData.DisplayModeData;
            return displayModeData.ShowDisplayModeBar || !displayModeData.DrawExternalDrawerProperties;
        }

        public override void OnClick(ShaderGUIToolbarContext context)
        {
            var displayModeData = context.MetaData.PerInspectorData.DisplayModeData;
            displayModeData.ShowDisplayModeBar = !displayModeData.ShowDisplayModeBar;
        }

        public override bool ShouldDrawSubToolbar(ShaderGUIToolbarContext context)
        {
            return context.MetaData.PerInspectorData.DisplayModeData.ShowDisplayModeBar;
        }

        public override void DrawSubToolbar(Rect toolbarRect, ShaderGUIToolbarContext context)
        {
            var displayModeData = context.MetaData.PerInspectorData.DisplayModeData;
            var optionWidth = Mathf.Min(OptionWidth, toolbarRect.width);
            var optionHeight = Mathf.Min(OptionHeight, toolbarRect.height);
            var optionRect = new Rect(
                toolbarRect.x,
                toolbarRect.y + Mathf.Max(0f, (toolbarRect.height - optionHeight) * 0.5f),
                optionWidth,
                optionHeight);
            var optionContent = new GUIContent(
                "External Drawers",
                "Draw properties that use MaterialPropertyDrawers not owned by this ShaderGUI.");

            ShaderGUIButtonUtility.DrawToggle(
                optionRect,
                displayModeData.DrawExternalDrawerProperties,
                optionContent,
                context.OptionToggleStyle,
                true,
                out var nextValue);

            if (nextValue != displayModeData.DrawExternalDrawerProperties)
                displayModeData.DrawExternalDrawerProperties = nextValue;
        }

        private static GUIContent CreateContent()
        {
            var iconPath = AssetDatabase.GUIDToAssetPath(IconGuid);
            var icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(iconPath);
            if (icon != null)
                return new GUIContent(string.Empty, icon, "Display Mode");

            var fallback = EditorGUIUtility.IconContent("d_scenevis_visible_hover");
            fallback.tooltip = "Display Mode";
            return fallback;
        }
    }
}
