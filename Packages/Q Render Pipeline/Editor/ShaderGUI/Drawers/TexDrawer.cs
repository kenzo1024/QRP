using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public sealed class TexDrawer : MaterialPropertyDrawerBase
    {
        private const float BoxHeight = 96f;
        private const float PackedBoxHeight = 112f;
        private const float Padding = 6f;
        private const float RowHeight = 16f;
        private const float ControlHeight = 16f;
        private const float Gap = 6f;
        private const float LabelWidth = 48f;
        private const float RowStep = 19f;
        private const float HeaderHeight = 20f;
        private const float PreviewMinSize = 56f;
        private const float PreviewContentGap = 10f;
        private const float PreviewInnerPadding = 3f;
        private const float ImportInfoHeight = 14f;
        private const float ChannelLabelOffset = 15f;
        private const float ClearButtonWidth = 42f;
        private const float ClearButtonMinRowWidth = 230f;
        private const float ChannelButtonWidth = 28f;
        private const float ResetButtonWidth = 38f;
        private const string ObjectSelectorUpdatedCommand = "ObjectSelectorUpdated";
        private const string ObjectSelectorClosedCommand = "ObjectSelectorClosed";

        private static readonly IShaderGUIButton<TexButtonContext>[] Buttons =
        {
            new ChannelButton(PreviewChannel.R, "R"),
            new ChannelButton(PreviewChannel.G, "G"),
            new ChannelButton(PreviewChannel.B, "B"),
            new ChannelButton(PreviewChannel.A, "A"),
            new ResetButton()
        };

        private static Material _previewMaterial;
        private static Texture2D _checkerTexture;
        private static GUIStyle _titleStyle;
        private static GUIStyle _subtitleStyle;
        private static GUIStyle _smallStyle;
        private static GUIStyle _mutedStyle;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _buttonStyle;

        private readonly string _groupName;
        private readonly string _colorPropertyName;
        private readonly string _channelPropertyName;
        private readonly string _strengthPropertyName;
        private readonly string _typedHelperPropertyName;
        private readonly string[] _channelLabels;

        public TexDrawer()
            : this(string.Empty, string.Empty, string.Empty)
        {
        }

        public TexDrawer(string groupName)
            : this(groupName, string.Empty, string.Empty)
        {
        }

        public TexDrawer(string groupName, string helperPropertyName)
        {
            _groupName = groupName;
            _colorPropertyName = string.Empty;
            _channelPropertyName = string.Empty;
            _strengthPropertyName = string.Empty;
            _typedHelperPropertyName = helperPropertyName;
            _channelLabels = null;
        }

        public TexDrawer(string groupName, string colorPropertyName, string channelPropertyName)
        {
            _groupName = groupName;
            _colorPropertyName = colorPropertyName;
            _channelPropertyName = channelPropertyName;
            _strengthPropertyName = string.Empty;
            _typedHelperPropertyName = string.Empty;
            _channelLabels = null;
        }

        public TexDrawer(string groupName, string colorPropertyName, string channelPropertyName, string strengthPropertyName)
        {
            _groupName = groupName;
            _colorPropertyName = colorPropertyName;
            _channelPropertyName = channelPropertyName;
            _strengthPropertyName = strengthPropertyName;
            _typedHelperPropertyName = string.Empty;
            _channelLabels = null;
        }

        public TexDrawer(string groupName, string helperPropertyName, string redLabel, string greenLabel, string blueLabel, string alphaLabel)
        {
            _groupName = groupName;
            _colorPropertyName = string.Empty;
            _channelPropertyName = helperPropertyName;
            _strengthPropertyName = string.Empty;
            _typedHelperPropertyName = string.Empty;
            _channelLabels = new[] { redLabel, greenLabel, blueLabel, alphaLabel };
        }

        public override bool IsMatchPropertyType(ShaderPropertyType propertyType)
        {
            return propertyType == ShaderPropertyType.Texture;
        }

        public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        {
            data.ParentGroupName = MainDrawer.NormalizeLayerName(_groupName);
            data.StyleName = ShaderGUIStyleRegistry.TextureBoxStyleName;

            MarkConsumedProperty(properties, property.name, _colorPropertyName, ShaderPropertyType.Color);
            MarkConsumedProperty(properties, property.name, _channelPropertyName, ShaderPropertyType.Vector);
            MarkConsumedProperty(properties, property.name, _strengthPropertyName, ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
            MarkConsumedProperty(properties, property.name, _typedHelperPropertyName, ShaderPropertyType.Color, ShaderPropertyType.Vector);
            MarkConsumedProperty(properties, property.name, _typedHelperPropertyName, ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
        }

        public override float GetPropertyHeight(MaterialProperty property, string label, MaterialEditor editor)
        {
            return GetBoxHeight() + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void DrawProperty(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            EnsureStyles();

            if (!ShaderGUIMetaDataCache.TryGetActive(editor, out var metaData))
            {
                editor.TexturePropertySingleLine(label, property);
                return;
            }

            var boxRect = EditorGUI.IndentedRect(position);
            boxRect.height = GetBoxHeight();

            var colorProperty = ResolveColorProperty(property, metaData);
            var channelProperty = ResolveChannelProperty(property, metaData);
            var strengthProperty = ResolveStrengthProperty(property, metaData);
            var context = new TexButtonContext(this, property, colorProperty, channelProperty, editor, metaData);

            DrawBoxBackground(boxRect, property);
            var rowsTop = boxRect.y + HeaderHeight;
            var viewY = rowsTop + RowStep * 2f;
            var importY = HasExplicitChannelLabels() ? viewY + 31f : viewY + RowStep;
            var previewSize = Mathf.Max(PreviewMinSize, importY + ImportInfoHeight - rowsTop);
            var previewRect = new Rect(boxRect.x + Padding, rowsTop, previewSize, previewSize);
            var contentX = previewRect.xMax + PreviewContentGap;
            var contentWidth = Mathf.Max(0f, boxRect.xMax - contentX - Padding);

            DrawHeader(boxRect, property, label);
            DrawPreview(previewRect, property, colorProperty, channelProperty);
            DrawTextureRow(new Rect(contentX, rowsTop, contentWidth, RowHeight), property);
            DrawValueRow(new Rect(contentX, rowsTop + RowStep, contentWidth, RowHeight), colorProperty, strengthProperty);
            DrawButtonRow(new Rect(contentX, viewY, contentWidth, RowHeight), context, property, channelProperty);
        }

        private static void DrawBoxBackground(Rect rect, MaterialProperty property)
        {
            var style = ShaderGUIStyleRegistry.GetTextureBoxFrameStyle(property.textureValue != null);
            ShaderGUIStyleRegistry.DrawFrame(rect, style);
        }
                                                                                                                       

        private float GetBoxHeight()
        {
            return HasExplicitChannelLabels() ? PackedBoxHeight : BoxHeight;
        }

        private void DrawHeader(Rect boxRect, MaterialProperty property, GUIContent label)
        {
            GUI.Label(new Rect(boxRect.x + Padding, boxRect.y + 1f, 190f, 18f), label.text, _titleStyle);

            var subtitleWidth = Mathf.Min(220f, Mathf.Max(0f, boxRect.width - 210f));
            if (subtitleWidth > 80f)
            {
                var subtitleRect = new Rect(boxRect.xMax - Padding - subtitleWidth, boxRect.y + 3f, subtitleWidth, 13f);
                GUI.Label(subtitleRect, BuildSubtitle(property), _subtitleStyle);
            }
        }

        private void DrawPreview(Rect previewRect, MaterialProperty property, MaterialProperty colorProperty, MaterialProperty channelProperty)
        {
            DrawChecker(previewRect);
            DrawBorder(previewRect, new Color(0.11f, 0.12f, 0.13f));

            var innerRect = new Rect(
                previewRect.x + PreviewInnerPadding,
                previewRect.y + PreviewInnerPadding,
                previewRect.width - PreviewInnerPadding * 2f,
                previewRect.height - PreviewInnerPadding * 2f);
            var texture = property.textureValue;
            if (texture != null)
            {
                var previewMask = GetPreviewChannelMask(property, channelProperty);
                var tint = colorProperty != null ? colorProperty.colorValue : Color.white;
                var material = GetPreviewMaterial(previewMask, tint);

                if (material != null)
                    EditorGUI.DrawPreviewTexture(innerRect, texture, material, ScaleMode.ScaleToFit);
                else
                    EditorGUI.DrawPreviewTexture(innerRect, texture, null, ScaleMode.ScaleToFit);

                DrawPreviewBadge(innerRect, previewMask);
            }
            else
            {
                GUI.Label(innerRect, "None", _mutedStyle);
            }

            HandlePreviewInput(previewRect, property);
        }

        private static void DrawTextureRow(Rect rowRect, MaterialProperty property)
        {
            DrawLabel(rowRect, "Texture");

            var clearWidth = rowRect.width > ClearButtonMinRowWidth ? ClearButtonWidth : 0f;
            var fieldWidth = Mathf.Max(90f, rowRect.width - LabelWidth - Gap - clearWidth - (clearWidth > 0f ? Gap : 0f));
            var fieldRect = new Rect(rowRect.x + LabelWidth + Gap, rowRect.y, fieldWidth, ControlHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMixedValue;
            var nextTexture = (Texture)EditorGUI.ObjectField(fieldRect, property.textureValue, typeof(Texture), false);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                property.textureValue = nextTexture;

            if (clearWidth <= 0f)
                return;

            var clearRect = new Rect(fieldRect.xMax + Gap, rowRect.y, clearWidth, ControlHeight);
            using (new EditorGUI.DisabledScope(property.textureValue == null))
            {
                if (GUI.Button(clearRect, new GUIContent("Clear", "Clear texture."), _buttonStyle))
                    property.textureValue = null;
            }
        }

        private static void DrawValueRow(Rect rowRect, MaterialProperty colorProperty, MaterialProperty strengthProperty)
        {
            DrawLabel(rowRect, "Color");

            var fieldX = rowRect.x + LabelWidth + Gap;
            if (fieldX >= rowRect.xMax)
                return;

            var colorWidth = Mathf.Min(58f, rowRect.xMax - fieldX);
            var colorRect = new Rect(fieldX, rowRect.y, colorWidth, ControlHeight);
            if (colorProperty != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = colorProperty.hasMixedValue;
                var nextColor = EditorGUI.ColorField(colorRect, GUIContent.none, colorProperty.colorValue, true, true, ShaderGUIUtility.HasPropertyFlag(colorProperty, ShaderPropertyFlags.HDR));
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                    colorProperty.colorValue = nextColor;
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.ColorField(colorRect, GUIContent.none, Color.white, false, false, false);
            }

            const float strengthSpacing = 18f;
            const float minStrengthFieldWidth = 42f;
            var strengthLabelX = colorRect.xMax + strengthSpacing;
            if (strengthLabelX + LabelWidth + Gap + minStrengthFieldWidth > rowRect.xMax)
                return;

            var strengthLabelRect = new Rect(strengthLabelX, rowRect.y, LabelWidth, rowRect.height);
            DrawLabel(strengthLabelRect, "Strength");
            var strengthWidth = Mathf.Min(58f, rowRect.xMax - strengthLabelRect.xMax - Gap);
            var strengthRect = new Rect(strengthLabelRect.xMax + Gap, rowRect.y, strengthWidth, ControlHeight);
            DrawNumericField(strengthRect, strengthProperty, 1f);
        }

        private void DrawButtonRow(Rect rowRect, TexButtonContext context, MaterialProperty property, MaterialProperty channelProperty)
        {
            DrawLabel(rowRect, "View");

            var cursorX = rowRect.x + LabelWidth + Gap;
            using (new EditorGUI.DisabledScope(channelProperty == null))
            {
                foreach (var button in Buttons)
                {
                    var width = button is ChannelButton ? ChannelButtonWidth : ResetButtonWidth;
                    if (cursorX + width > rowRect.xMax)
                        break;

                    var buttonRect = new Rect(cursorX, rowRect.y, width, rowRect.height);
                    DrawButton(buttonRect, button, context);
                    cursorX += width + Gap;
                }
            }

            var channelLabels = ResolveChannelLabels(property);
            var hasChannelLabels = HasAnyChannelLabel(channelLabels);
            for (var i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(channelLabels[i]))
                    continue;

                var labelRect = new Rect(
                    rowRect.x + LabelWidth + Gap + i * (ChannelButtonWidth + Gap),
                    rowRect.yMax,
                    ChannelButtonWidth + 6f,
                    ImportInfoHeight);
                GUI.Label(labelRect, channelLabels[i], _badgeStyle);
            }

            var importY = hasChannelLabels ? rowRect.yMax + ChannelLabelOffset : rowRect.yMax + 3f;
            var infoLabelRect = new Rect(rowRect.x, importY, LabelWidth, ImportInfoHeight);
            var importRect = new Rect(rowRect.x + LabelWidth + Gap, importY, rowRect.width - LabelWidth - Gap, ImportInfoHeight);
            GUI.Label(infoLabelRect, "Info", _smallStyle);
            if (importRect.width > 120f)
                GUI.Label(importRect, BuildImportInfo(property), _mutedStyle);

        }

        private static void DrawButton(Rect rect, IShaderGUIButton<TexButtonContext> button, TexButtonContext context)
        {
            ShaderGUIButtonUtility.DrawButton(rect, button, context, _buttonStyle);
        }

        private static void DrawNumericField(Rect rect, MaterialProperty property, float fallbackValue)
        {
            if (property == null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.FloatField(rect, fallbackValue);
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMixedValue;
            var value = EditorGUI.FloatField(rect, ShaderGUIUtility.GetNumericValue(property));
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                ShaderGUIUtility.SetNumericValue(property, value);
        }

        private static void DrawLabel(Rect rect, string text)
        {
            GUI.Label(new Rect(rect.x, rect.y + 1f, LabelWidth, rect.height), text, _smallStyle);
        }

        private static void HandlePreviewInput(Rect previewRect, MaterialProperty property)
        {
            var evt = Event.current;
            var pickerControlId = GUIUtility.GetControlID(FocusType.Passive, previewRect);
            if (HandleObjectPickerEvent(evt, pickerControlId, property))
                return;

            if (!previewRect.Contains(evt.mousePosition))
                return;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (property.textureValue != null)
                    SelectTexture(property.textureValue);
                else
                    EditorGUIUtility.ShowObjectPicker<Texture>(null, false, string.Empty, pickerControlId);

                evt.Use();
                return;
            }

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            var texture = FindDraggedTexture();
            if (texture == null)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                property.textureValue = texture;
            }

            evt.Use();
        }

        private static bool HandleObjectPickerEvent(Event evt, int pickerControlId, MaterialProperty property)
        {
            if (evt.type != EventType.ExecuteCommand
                || EditorGUIUtility.GetObjectPickerControlID() != pickerControlId
                || (evt.commandName != ObjectSelectorUpdatedCommand && evt.commandName != ObjectSelectorClosedCommand))
            {
                return false;
            }

            if (EditorGUIUtility.GetObjectPickerObject() is Texture texture)
                property.textureValue = texture;

            evt.Use();
            return true;
        }

        private static void SelectTexture(Texture texture)
        {
            Selection.activeObject = texture;
            EditorGUIUtility.PingObject(texture);
        }

        private static Texture FindDraggedTexture()
        {
            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject is Texture texture)
                    return texture;
            }

            return null;
        }

        private static void DrawPreviewBadge(Rect rect, Vector4 channelMask)
        {
            var text = BuildChannelMaskLabel(channelMask);
            var textSize = _badgeStyle.CalcSize(new GUIContent(text));
            var badgeWidth = Mathf.Ceil(textSize.x + 10f);
            var badgeHeight = Mathf.Ceil(textSize.y + 4f);
            var badgeRect = new Rect(rect.xMax - badgeWidth - 4f, rect.y + 4f, badgeWidth, badgeHeight);
            EditorGUI.DrawRect(badgeRect, new Color(0.12f, 0.13f, 0.15f, 0.82f));
            GUI.Label(badgeRect, text, _badgeStyle);
        }

        private static void DrawChecker(Rect rect)
        {
            GUI.DrawTexture(rect, GetCheckerTexture(), ScaleMode.StretchToFill);
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static Texture2D GetCheckerTexture()
        {
            if (_checkerTexture != null)
                return _checkerTexture;

            _checkerTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };

            var dark = new Color(0.18f, 0.19f, 0.21f, 1f);
            var light = new Color(0.27f, 0.29f, 0.32f, 1f);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var useLight = (x < 8 && y < 8) || (x >= 8 && y >= 8);
                    _checkerTexture.SetPixel(x, y, useLight ? light : dark);
                }
            }

            _checkerTexture.Apply();
            return _checkerTexture;
        }

        private static Material GetPreviewMaterial(Vector4 channelMask, Color tint)
        {
            if (_previewMaterial == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/Q Render Pipeline/Editor/ShaderGUI/Drawers/TexPreview.shader")
                             ?? Shader.Find("Hidden/QRP/Editor/TexPreview");
                if (shader == null)
                    return null;

                _previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            _previewMaterial.SetColor("_Tint", tint);
            _previewMaterial.SetVector("_ChannelMask", channelMask);
            return _previewMaterial;
        }

        private MaterialProperty ResolveColorProperty(MaterialProperty property, ShaderGUIMetaData metaData)
        {
            return FindProperty(metaData, new[] { _colorPropertyName, _typedHelperPropertyName }, ShaderPropertyType.Color);
        }

        private MaterialProperty ResolveChannelProperty(MaterialProperty property, ShaderGUIMetaData metaData)
        {
            return FindProperty(metaData, new[] { _channelPropertyName, _typedHelperPropertyName }, ShaderPropertyType.Vector);
        }

        private MaterialProperty ResolveStrengthProperty(MaterialProperty property, ShaderGUIMetaData metaData)
        {
            var explicitProperty = FindProperty(metaData, new[] { _strengthPropertyName }, ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
            if (explicitProperty != null)
                return explicitProperty;

            var typedHelperProperty = FindProperty(metaData, new[] { _typedHelperPropertyName }, ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
            if (typedHelperProperty != null)
                return typedHelperProperty;

            var names = new List<string>
            {
                property.name.Replace("Map", "Scale"),
                property.name.Replace("Map", "Strength"),
                property.name.Replace("Tex", "Scale"),
                property.name.Replace("Tex", "Strength"),
                property.name + "Scale",
                property.name + "Strength",
            };

            var lowerName = GetLowerPropertyName(property);
            if (ContainsAny(lowerName, "normal", "bump"))
                names.Add(lowerName.Contains("detail") ? "_DetailNormalMapScale" : "_BumpScale");

            if (lowerName.Contains("occlusion"))
                names.Add("_OcclusionStrength");

            if (lowerName.Contains("detail") && lowerName.Contains("albedo"))
                names.Add("_DetailAlbedoMapScale");

            if (ContainsAny(lowerName, "height", "parallax"))
                names.Add("_Parallax");

            if (lowerName.Contains("metallic"))
                names.Add("_Metallic");

            if (lowerName.Contains("smooth"))
                names.Add("_Smoothness");

            return FindProperty(metaData, names, ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
        }

        private static void MarkConsumedProperty(MaterialProperty[] properties, string ownerPropertyName, string propertyName, params ShaderPropertyType[] acceptedTypes)
        {
            if (string.IsNullOrEmpty(propertyName) || propertyName == "_" || propertyName == ownerPropertyName)
                return;

            foreach (var property in properties)
            {
                if (property.name == propertyName && IsAcceptedPropertyType(property, acceptedTypes))
                {
                    ShaderGUIMetaDataCache.PendingConsumedProperties.Add(propertyName);
                    return;
                }
            }
        }

        private static bool IsAcceptedPropertyType(MaterialProperty property, ShaderPropertyType[] acceptedTypes)
        {
            var propertyType = ShaderGUIUtility.GetPropertyType(property);
            foreach (var acceptedType in acceptedTypes)
            {
                if (propertyType == acceptedType)
                    return true;
            }

            return false;
        }

        private static MaterialProperty FindProperty(ShaderGUIMetaData metaData, IEnumerable<string> names, params ShaderPropertyType[] acceptedTypes)
        {
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)
                    || !metaData.PerMaterialData.Properties.TryGetValue(name, out var data)
                    || data.Property == null)
                {
                    continue;
                }

                var type = ShaderGUIUtility.GetPropertyType(data.Property);
                foreach (var acceptedType in acceptedTypes)
                {
                    if (type == acceptedType)
                        return data.Property;
                }
            }

            return null;
        }

        private string[] ResolveChannelLabels(MaterialProperty property)
        {
            if (_channelLabels != null && _channelLabels.Length == 4)
                return _channelLabels;

            return new[] { string.Empty, string.Empty, string.Empty, string.Empty };
        }

        private bool HasExplicitChannelLabels()
        {
            return _channelLabels != null
                   && _channelLabels.Length == 4
                   && HasAnyChannelLabel(_channelLabels);
        }

        private static bool HasAnyChannelLabel(string[] labels)
        {
            foreach (var label in labels)
            {
                if (!string.IsNullOrEmpty(label))
                    return true;
            }

            return false;
        }

        private static string BuildSubtitle(MaterialProperty property)
        {
            var name = GetLowerPropertyName(property);
            if (name.Contains("normal") || name.Contains("bump"))
                return "Normal / Tint / Channel";
            if (name.Contains("base") || name.Contains("albedo"))
                return "Texture / Tint / Channels";
            return "Texture / Tint / Channels";
        }

        private static string BuildImportInfo(MaterialProperty property)
        {
            var texture = property.textureValue;
            if (texture == null)
                return "No texture assigned";

            var info = $"{texture.width}x{texture.height}";
            var path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                info += importer.sRGBTexture ? " sRGB" : " Linear";
                info += $" {texture.graphicsFormat}";
                info += importer.mipmapEnabled ? " Mip On" : " Mip Off";

                if (HasAlpha(importer))
                    info += " Alpha";

                var warning = BuildImportWarning(property, importer);
                if (!string.IsNullOrEmpty(warning))
                    info += $"  ! {warning}";

                return info;
            }

            return $"{info} {texture.graphicsFormat}";
        }

        private static string BuildImportWarning(MaterialProperty property, TextureImporter importer)
        {
            var name = GetLowerPropertyName(property);
            if ((name.Contains("normal") || name.Contains("bump")) && importer.textureType != TextureImporterType.NormalMap)
                return "Normal type";

            if ((name.Contains("mask") || name.Contains("metal") || name.Contains("rough") || name.Contains("occlusion"))
                && importer.sRGBTexture)
            {
                return "Use Linear";
            }

            return string.Empty;
        }

        private static string GetLowerPropertyName(MaterialProperty property)
        {
            return $"{property.name} {property.displayName}".ToLowerInvariant();
        }

        private static bool ContainsAny(string value, params string[] parts)
        {
            foreach (var part in parts)
            {
                if (value.Contains(part))
                    return true;
            }

            return false;
        }

        private static bool HasAlpha(TextureImporter importer)
        {
            return importer.alphaSource != TextureImporterAlphaSource.None && importer.DoesSourceTextureHaveAlpha();
        }

        internal Vector4 GetPreviewChannelMask(MaterialProperty property, MaterialProperty channelProperty)
        {
            if (channelProperty != null)
                return NormalizeChannelMask(channelProperty.vectorValue);

            return Vector4.zero;
        }

        internal void SetPreviewChannelMask(MaterialProperty property, MaterialProperty channelProperty, Vector4 mask)
        {
            if (channelProperty != null)
                channelProperty.vectorValue = NormalizeChannelMask(mask);
        }

        internal void ResetTexState(MaterialProperty property, MaterialProperty colorProperty, MaterialProperty channelProperty)
        {
            SetPreviewChannelMask(property, channelProperty, Vector4.zero);
            if (colorProperty != null)
                colorProperty.colorValue = Color.white;
        }

        private static Vector4 NormalizeChannelMask(Vector4 value)
        {
            return new Vector4(
                value.x > 0.5f ? 1f : 0f,
                value.y > 0.5f ? 1f : 0f,
                value.z > 0.5f ? 1f : 0f,
                value.w > 0.5f ? 1f : 0f);
        }

        private static string BuildChannelMaskLabel(Vector4 mask)
        {
            mask = NormalizeChannelMask(mask);
            if (mask == Vector4.zero)
                return "Tint";

            var label = string.Empty;
            if (mask.x > 0.5f) label += "R";
            if (mask.y > 0.5f) label += "G";
            if (mask.z > 0.5f) label += "B";
            if (mask.w > 0.5f) label += "A";
            return label;
        }

        private static void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _titleStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.92f, 0.95f) : new Color(0.1f, 0.1f, 0.1f);

            _smallStyle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _smallStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.72f, 0.76f, 0.82f) : new Color(0.25f, 0.27f, 0.3f);

            _subtitleStyle ??= new GUIStyle(_smallStyle)
            {
                fontSize = 9
            };

            _mutedStyle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _mutedStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.52f, 0.57f, 0.65f) : new Color(0.38f, 0.4f, 0.44f);

            _badgeStyle ??= new GUIStyle(_mutedStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            _buttonStyle ??= new GUIStyle(ShaderGUIStyleRegistry.GetButtonStyle(ShaderGUIButtonVariant.Standard))
            {
                fontSize = 10,
                padding = new RectOffset(2, 2, 0, 1),
                fixedHeight = 0f,
                margin = new RectOffset(0, 0, 0, 0)
            };
        }
    }

    public enum PreviewChannel
    {
        R = 0,
        G = 1,
        B = 2,
        A = 3
    }

    public readonly struct TexButtonContext
    {
        public TexDrawer Drawer { get; }
        public MaterialProperty TextureProperty { get; }
        public MaterialProperty ColorProperty { get; }
        public MaterialProperty ChannelProperty { get; }
        public MaterialEditor Editor { get; }
        public ShaderGUIMetaData MetaData { get; }

        public TexButtonContext(
            TexDrawer drawer,
            MaterialProperty textureProperty,
            MaterialProperty colorProperty,
            MaterialProperty channelProperty,
            MaterialEditor editor,
            ShaderGUIMetaData metaData)
        {
            Drawer = drawer;
            TextureProperty = textureProperty;
            ColorProperty = colorProperty;
            ChannelProperty = channelProperty;
            Editor = editor;
            MetaData = metaData;
        }
    }

    internal sealed class ChannelButton : ShaderGUIButtonBase<TexButtonContext>
    {
        private readonly PreviewChannel _channel;
        private readonly GUIContent _content;

        public ChannelButton(PreviewChannel channel, string label)
        {
            _channel = channel;
            _content = new GUIContent(label, $"Toggle {label} channel preview.");
        }

        public override GUIContent Content => _content;

        public override bool IsHighlighted(TexButtonContext context)
        {
            var mask = context.Drawer.GetPreviewChannelMask(context.TextureProperty, context.ChannelProperty);
            return GetChannelValue(mask, _channel) > 0.5f;
        }

        public override void OnClick(TexButtonContext context)
        {
            var mask = context.Drawer.GetPreviewChannelMask(context.TextureProperty, context.ChannelProperty);
            SetChannelValue(ref mask, _channel, GetChannelValue(mask, _channel) > 0.5f ? 0f : 1f);
            context.Drawer.SetPreviewChannelMask(context.TextureProperty, context.ChannelProperty, mask);
        }

        private static float GetChannelValue(Vector4 mask, PreviewChannel channel)
        {
            return channel switch
            {
                PreviewChannel.R => mask.x,
                PreviewChannel.G => mask.y,
                PreviewChannel.B => mask.z,
                PreviewChannel.A => mask.w,
                _ => 0f
            };
        }

        private static void SetChannelValue(ref Vector4 mask, PreviewChannel channel, float value)
        {
            switch (channel)
            {
                case PreviewChannel.R:
                    mask.x = value;
                    break;
                case PreviewChannel.G:
                    mask.y = value;
                    break;
                case PreviewChannel.B:
                    mask.z = value;
                    break;
                case PreviewChannel.A:
                    mask.w = value;
                    break;
            }
        }
    }

    internal sealed class ResetButton : ShaderGUIButtonBase<TexButtonContext>
    {
        private static readonly GUIContent ButtonContent = new("Reset", "Reset preview channel and tint.");

        public override GUIContent Content => ButtonContent;

        public override void OnClick(TexButtonContext context)
        {
            context.Drawer.ResetTexState(context.TextureProperty, context.ColorProperty, context.ChannelProperty);
        }
    }

}
