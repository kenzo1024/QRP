using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public enum ShaderGUIColorPreset
    {
        UnityDark,
        SoftDark,
        Graphite
    }

    public enum ShaderGUIButtonVariant
    {
        Standard,
        Icon,
        Chip
    }

    public enum ShaderGUIFrameShape
    {
        Rectangle,
        Rounded
    }

    public sealed class ShaderGUIFrameStyle
    {
        public Color BackgroundColor { get; }
        public Color BorderColor { get; }
        public float BorderWidth { get; }
        public float BorderRadius { get; }
        public ShaderGUIFrameShape Shape { get; }
        public GUIStyle GuiStyle { get; }

        public ShaderGUIFrameStyle(
            Color backgroundColor,
            Color borderColor,
            float borderWidth,
            float borderRadius,
            ShaderGUIFrameShape shape,
            GUIStyle guiStyle)
        {
            BackgroundColor = backgroundColor;
            BorderColor = borderColor;
            BorderWidth = borderWidth;
            BorderRadius = borderRadius;
            Shape = shape;
            GuiStyle = guiStyle;
        }
    }

    public sealed class ShaderGUIBoxStyle
    {
        public Color AccentColor { get; }
        public float HeaderHeight { get; }
        public float Padding { get; }
        public float Spacing { get; }
        public float BorderRadius { get; }
        public bool ShowFoldoutArrow { get; }
        public float ArrowColumnWidth { get; }
        public float HeaderTextInset { get; }
        public float BodyInset { get; }
        public GUIStyle OuterStyle { get; }
        public GUIStyle HeaderStyle { get; }
        public GUIStyle BodyStyle { get; }
        public GUIStyle TitleStyle { get; }

        public ShaderGUIBoxStyle(
            Color accentColor,
            float headerHeight,
            float padding,
            float spacing,
            float borderRadius,
            bool showFoldoutArrow,
            float arrowColumnWidth,
            float headerTextInset,
            float bodyInset,
            GUIStyle outerStyle,
            GUIStyle headerStyle,
            GUIStyle bodyStyle,
            GUIStyle titleStyle)
        {
            AccentColor = accentColor;
            HeaderHeight = headerHeight;
            Padding = padding;
            Spacing = spacing;
            BorderRadius = borderRadius;
            ShowFoldoutArrow = showFoldoutArrow;
            ArrowColumnWidth = arrowColumnWidth;
            HeaderTextInset = headerTextInset;
            BodyInset = bodyInset;
            OuterStyle = outerStyle;
            HeaderStyle = headerStyle;
            BodyStyle = bodyStyle;
            TitleStyle = titleStyle;
        }
    }

    public sealed class ShaderGUIToolbarStyle
    {
        public float MainHeight { get; }
        public float SubHeight { get; }
        public float ContentPadding { get; }
        public float ButtonSize { get; }
        public float ButtonSpacing { get; }
        public Color AccentColor { get; }
        public GUIStyle MainBarStyle { get; }
        public GUIStyle SubBarStyle { get; }
        public GUIStyle ButtonStyle { get; }
        public GUIStyle HighlightedButtonStyle { get; }
        public GUIStyle OptionToggleStyle { get; }

        public ShaderGUIToolbarStyle(
            float mainHeight,
            float subHeight,
            float contentPadding,
            float buttonSize,
            float buttonSpacing,
            Color accentColor,
            GUIStyle mainBarStyle,
            GUIStyle subBarStyle,
            GUIStyle buttonStyle,
            GUIStyle highlightedButtonStyle,
            GUIStyle optionToggleStyle)
        {
            MainHeight = mainHeight;
            SubHeight = subHeight;
            ContentPadding = contentPadding;
            ButtonSize = buttonSize;
            ButtonSpacing = buttonSpacing;
            AccentColor = accentColor;
            MainBarStyle = mainBarStyle;
            SubBarStyle = subBarStyle;
            ButtonStyle = buttonStyle;
            HighlightedButtonStyle = highlightedButtonStyle;
            OptionToggleStyle = optionToggleStyle;
        }
    }

    public static class ShaderGUIStyleRegistry
    {
        public const string DefaultGroupStyleName = "DefaultGroup";
        public const string TextureBoxStyleName = "TextureBox";
        public const float InspectorHorizontalMargin = 16f;

        private const float ToolbarHorizontalMargin = 4f;
        private const float InspectorRightReservedWidth = ShaderGUIControlRow.ReservedSideWidth;
        private const float FrameRadius = 3f;
        private const float FrameBorderWidth = 0.75f;

        public static ShaderGUIColorPreset CurrentColorPreset { get; set; } = ShaderGUIColorPreset.SoftDark;

        private static ShaderGUIBoxStyle _defaultGroupStyle;
        private static ShaderGUIBoxStyle _textureBoxStyle;
        private static ShaderGUIToolbarStyle _toolbarStyle;
        private static GUIStyle _standardButtonStyle;
        private static GUIStyle _iconButtonStyle;
        private static GUIStyle _chipButtonStyle;
        private static ShaderGUIFrameStyle _textureBoxFrameStyle;
        private static ShaderGUIFrameStyle _textureBoxHighlightedFrameStyle;
        private static ShaderGUIColorPreset _cachedPreset;

        public static ShaderGUIBoxStyle GetBoxStyle(string styleName)
        {
            if (_cachedPreset != CurrentColorPreset)
                ClearCache();

            if (styleName == TextureBoxStyleName)
                return _textureBoxStyle ??= CreateTextureBoxStyle();

            return _defaultGroupStyle ??= CreateDefaultGroupStyle();
        }

        public static ShaderGUIToolbarStyle GetToolbarStyle()
        {
            if (_cachedPreset != CurrentColorPreset)
                ClearCache();

            return _toolbarStyle ??= CreateToolbarStyle();
        }

        public static GUIStyle GetButtonStyle(ShaderGUIButtonVariant variant)
        {
            if (_cachedPreset != CurrentColorPreset)
                ClearCache();

            return variant switch
            {
                ShaderGUIButtonVariant.Icon => _iconButtonStyle ??= CreateButtonStyle(ShaderGUIButtonVariant.Icon),
                ShaderGUIButtonVariant.Chip => _chipButtonStyle ??= CreateButtonStyle(ShaderGUIButtonVariant.Chip),
                _ => _standardButtonStyle ??= CreateButtonStyle(ShaderGUIButtonVariant.Standard)
            };
        }

        public static ShaderGUIFrameStyle GetTextureBoxFrameStyle(bool highlighted)
        {
            if (_cachedPreset != CurrentColorPreset)
                ClearCache();

            return highlighted
                ? _textureBoxHighlightedFrameStyle ??= CreateTextureBoxFrameStyle(true)
                : _textureBoxFrameStyle ??= CreateTextureBoxFrameStyle(false);
        }

        public static void DrawFrame(Rect rect, ShaderGUIFrameStyle style)
        {
            if (style == null)
                return;

            rect = AlignToPixels(rect);

            if (style.Shape == ShaderGUIFrameShape.Rounded)
            {
                GUI.Box(rect, GUIContent.none, style.GuiStyle);
                return;
            }

            EditorGUI.DrawRect(rect, style.BackgroundColor);
            DrawRectBorder(rect, style.BorderColor, style.BorderWidth);
        }

        public static Color GetButtonHighlightColor()
        {
            var palette = GetPalette(EditorGUIUtility.isProSkin);
            return Color.Lerp(Color.white, palette.Accent, EditorGUIUtility.isProSkin ? 0.68f : 0.48f);
        }

        public static Rect GetInspectorContentRect(float height)
        {
            var rect = EditorGUILayout.GetControlRect(false, height, GUILayout.ExpandWidth(true));
            return GetInspectorContentRect(rect);
        }

        public static Rect GetInspectorContentRect(Rect rect)
        {
            return GetInspectorRect(rect, InspectorHorizontalMargin);
        }

        public static Rect GetInspectorToolbarRect(float height)
        {
            var rect = EditorGUILayout.GetControlRect(false, height, GUILayout.ExpandWidth(true));
            return GetInspectorRect(rect, ToolbarHorizontalMargin);
        }

        private static Rect GetInspectorRect(Rect rect, float horizontalMargin)
        {
            var viewWidth = EditorGUIUtility.currentViewWidth;
            if (viewWidth <= 0f)
                return AlignToPixels(rect);

            var width = Mathf.Max(0f, viewWidth - horizontalMargin * 2f - InspectorRightReservedWidth);
            return AlignToPixels(new Rect(horizontalMargin, rect.y, width, rect.height));
        }

        public static void SetColorPreset(ShaderGUIColorPreset preset)
        {
            if (CurrentColorPreset == preset)
                return;

            CurrentColorPreset = preset;
            ClearCache();
        }

        private static void ClearCache()
        {
            _cachedPreset = CurrentColorPreset;
            _defaultGroupStyle = null;
            _textureBoxStyle = null;
            _toolbarStyle = null;
            _standardButtonStyle = null;
            _iconButtonStyle = null;
            _chipButtonStyle = null;
            _textureBoxFrameStyle = null;
            _textureBoxHighlightedFrameStyle = null;
        }

        private static ShaderGUIBoxStyle CreateDefaultGroupStyle()
        {
            var pro = EditorGUIUtility.isProSkin;
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = 12
            };
            titleStyle.normal.textColor = pro
                ? new Color(0.86f, 0.88f, 0.91f)
                : new Color(0.14f, 0.14f, 0.14f);

            var palette = GetPalette(pro);

            return new ShaderGUIBoxStyle(
                palette.Accent,
                21f,
                5f,
                3f,
                FrameRadius,
                false,
                16f,
                13f,
                12f,
                CreateFrameStyle(palette.Outer, palette.Border),
                CreateFillStyle(palette.Header),
                CreateFillStyle(palette.Body),
                titleStyle);
        }

        private static ShaderGUIBoxStyle CreateTextureBoxStyle()
        {
            var style = CreateDefaultGroupStyle();
            return new ShaderGUIBoxStyle(
                new Color(0.36f, 0.57f, 0.9f),
                style.HeaderHeight,
                style.Padding,
                style.Spacing,
                style.BorderRadius,
                style.ShowFoldoutArrow,
                style.ArrowColumnWidth,
                style.HeaderTextInset,
                style.BodyInset,
                style.OuterStyle,
                style.HeaderStyle,
                style.BodyStyle,
                style.TitleStyle);
        }

        private static ShaderGUIFrameStyle CreateTextureBoxFrameStyle(bool highlighted)
        {
            var backgroundColor = new Color(0.22f, 0.23f, 0.25f);
            var borderColor = highlighted
                ? new Color(0.36f, 0.57f, 0.9f)
                : new Color(0.16f, 0.17f, 0.18f);
            const float borderWidth = 1f;
            const float borderRadius = 0f;
            const ShaderGUIFrameShape shape = ShaderGUIFrameShape.Rectangle;

            var guiStyle = shape == ShaderGUIFrameShape.Rounded
                ? CreateRoundedStyle(backgroundColor, borderColor, borderRadius, borderWidth)
                : null;

            return new ShaderGUIFrameStyle(
                backgroundColor,
                borderColor,
                borderWidth,
                borderRadius,
                shape,
                guiStyle);
        }

        private static GUIStyle CreateRoundedStyle(Color fillColor, Color borderColor, float radius, float borderWidth)
        {
            var style = new GUIStyle
            {
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.background = ShaderGUIRoundedRectUtility.GetTexture(fillColor, borderColor, radius, borderWidth);
            return style;
        }

        private static GUIStyle CreateFrameStyle(Color fillColor, Color borderColor)
        {
            return CreateRoundedStyle(fillColor, borderColor, FrameRadius, FrameBorderWidth);
        }

        private static void DrawRectBorder(Rect rect, Color color, float width)
        {
            if (width <= 0f || color.a <= 0f)
                return;

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static GUIStyle CreateFillStyle(Color fillColor)
        {
            return CreateRoundedStyle(fillColor, Color.clear, FrameRadius, 0f);
        }

        /* toolbar 样式 */
        private static ShaderGUIToolbarStyle CreateToolbarStyle()
        {
            var pro = EditorGUIUtility.isProSkin;
            var palette = GetPalette(pro);
            var textColor = pro
                ? new Color(0.86f, 0.88f, 0.91f)
                : new Color(0.14f, 0.14f, 0.14f);
            var activeTextColor = pro ? Color.white : new Color(0.08f, 0.08f, 0.08f);

            var mainBarStyle = CreateFrameStyle(palette.Outer, palette.Border);
            var subBarStyle = CreateFrameStyle(
                Color.Lerp(palette.Body, palette.Outer, 0.35f),
                palette.Border);

            return new ShaderGUIToolbarStyle(
                30f,
                28f,
                5f,
                24f,
                4f,
                palette.Accent,
                mainBarStyle,
                subBarStyle,
                GetButtonStyle(ShaderGUIButtonVariant.Icon),
                GetButtonStyle(ShaderGUIButtonVariant.Icon),
                GetButtonStyle(ShaderGUIButtonVariant.Chip));
        }

        private static GUIStyle CreateButtonStyle(ShaderGUIButtonVariant variant)
        {
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            switch (variant)
            {
                case ShaderGUIButtonVariant.Icon:
                    style.imagePosition = ImagePosition.ImageOnly;
                    style.padding = new RectOffset(3, 3, 3, 3);
                    style.fixedWidth = 0f;
                    style.fixedHeight = 0f;
                    style.stretchWidth = false;
                    style.stretchHeight = false;
                    break;
                case ShaderGUIButtonVariant.Chip:
                    style.fontStyle = FontStyle.Normal;
                    style.padding = new RectOffset(9, 9, 0, 1);
                    style.fixedHeight = 0f;
                    style.stretchHeight = true;
                    break;
                default:
                    style.fontStyle = FontStyle.Bold;
                    break;
            }

            return style;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        private static Rect AlignToPixels(Rect rect)
        {
            var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            if (pixelsPerPoint <= 0f)
                return rect;

            var xMin = Mathf.Ceil(rect.xMin * pixelsPerPoint) / pixelsPerPoint;
            var yMin = Mathf.Ceil(rect.yMin * pixelsPerPoint) / pixelsPerPoint;
            var xMax = Mathf.Floor(rect.xMax * pixelsPerPoint) / pixelsPerPoint;
            var yMax = Mathf.Floor(rect.yMax * pixelsPerPoint) / pixelsPerPoint;
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private static ShaderGUIBoxPalette GetPalette(bool pro)
        {
            if (!pro)
            {
                return new ShaderGUIBoxPalette(
                    new Color(0.78f, 0.78f, 0.78f),
                    new Color(0.70f, 0.70f, 0.70f),
                    new Color(0.74f, 0.74f, 0.74f),
                    new Color(0.52f, 0.52f, 0.52f, 0.55f),
                    new Color(0.20f, 0.38f, 0.70f));
            }

            return CurrentColorPreset switch
            {
                ShaderGUIColorPreset.Graphite => new ShaderGUIBoxPalette(
                    new Color(0.19f, 0.20f, 0.21f),
                    new Color(0.25f, 0.26f, 0.28f),
                    new Color(0.22f, 0.23f, 0.25f),
                    new Color(0.08f, 0.09f, 0.10f, 0.42f),
                    new Color(0.42f, 0.58f, 0.82f)),
                ShaderGUIColorPreset.UnityDark => new ShaderGUIBoxPalette(
                    new Color(0.24f, 0.24f, 0.24f),
                    new Color(0.30f, 0.30f, 0.30f),
                    new Color(0.27f, 0.27f, 0.27f),
                    new Color(0.12f, 0.12f, 0.12f, 0.36f),
                    new Color(0.45f, 0.63f, 0.92f)),
                _ => new ShaderGUIBoxPalette(
                    new Color(0.23f, 0.24f, 0.26f),
                    new Color(0.30f, 0.31f, 0.34f),
                    new Color(0.26f, 0.27f, 0.30f),
                    new Color(0.10f, 0.11f, 0.12f, 0.30f),
                    new Color(0.48f, 0.64f, 0.92f))
            };
        }

        private readonly struct ShaderGUIBoxPalette
        {
            public Color Outer { get; }
            public Color Header { get; }
            public Color Body { get; }
            public Color Border { get; }
            public Color Accent { get; }

            public ShaderGUIBoxPalette(Color outer, Color header, Color body, Color border, Color accent)
            {
                Outer = outer;
                Header = header;
                Body = body;
                Border = border;
                Accent = accent;
            }
        }
    }
}
