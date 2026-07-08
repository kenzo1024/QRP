using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public class MaterialInspector : UnityEditor.ShaderGUI
    {
        private MaterialProperty[] _properties;
        
        public ShaderGUIMetaData MetaData { get; private set; }

        protected virtual bool DrawExternalDrawerProperties => MetaData?.PerInspectorData.DisplayModeData.DrawExternalDrawerProperties ?? true;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            var material = materialEditor.target as Material;
            if (material == null)
                return;

            var shader = material.shader;
            _properties = properties;
            MetaData = ShaderGUIMetaDataCache.Build(shader, material, materialEditor, this, properties);

            DrawToolbar(materialEditor);
            DrawProperties(materialEditor, _properties);
        }
        
        protected virtual void DrawToolbar(MaterialEditor materialEditor)
        {
            if (MetaData == null)
                return;

            ToolbarUtility.DrawToolbar(MetaData);
        }

        /// <summary>
        /// 绘制属性列表：遍历所有属性，过滤并绘制可见属性。
        ///
        /// 过滤规则（由 ShaderGUIUtility.ShouldDrawProperty 判断）：
        /// - 跳过隐藏属性（[HideInInspector]）
        /// - 跳过不可见属性（ShowIf 条件不满足，未完全实现）
        /// - 根据 DrawExternalDrawerProperties 决定是否跳过外部 Drawer 属性
        /// </summary>
        protected virtual void DrawProperties(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (MetaData?.PerShaderData.LayoutRoot != null)
            {
                ShaderGUILayoutRenderer.Draw(MetaData, this);
                return;
            }

            foreach (var property in properties)
            {
                // Skip properties that are hidden, inactive, or owned by unsupported external drawers.
                if (MetaData == null
                    || !MetaData.PerShaderData.Properties.TryGetValue(property.name, out var staticData)
                    || !MetaData.PerMaterialData.Properties.TryGetValue(property.name, out var dynamicData)
                    || !ShaderGUIUtility.ShouldDrawProperty(staticData, dynamicData, MetaData, DrawExternalDrawerProperties))
                {
                    continue;
                }

                DrawProperty(materialEditor, property);
            }
        }
        
        
        /* 绘制单个属性：根据属性类型选择绘制方式 */
        public void DrawLayoutProperty(Rect position, MaterialProperty property)
        {
            DrawProperty(position, MetaData.PerInspectorData.MaterialEditor, property);
        }

        protected virtual void DrawProperty(MaterialEditor materialEditor, MaterialProperty property)
        {
            var position = EditorGUILayout.GetControlRect(true, GetPropertyHeight(materialEditor, property));
            DrawProperty(position, materialEditor, property);
        }

        protected virtual void DrawProperty(Rect position, MaterialEditor materialEditor, MaterialProperty property)
        {
            MetaData.PerShaderData.Properties.TryGetValue(property.name, out var staticData);
            MetaData.PerMaterialData.Properties.TryGetValue(property.name, out var dynamicData);
            var row = ShaderGUIControlRow.FromContentRect(position);

            var displayName = staticData?.DisplayName ?? property.displayName;
            var tooltip = dynamicData?.Tooltip ?? property.name;
            var label = new GUIContent(displayName, tooltip);

            using (new EditorGUI.DisabledScope(!(dynamicData?.IsActive ?? true)))
            {
                DrawPropertyRowDecorations(row, staticData, MetaData.PerShaderData.Shader);

                var drawableDrawer = ShaderGUIReflectionUtility.GetDrawablePropertyDrawer(
                    MetaData.PerShaderData.Shader,
                    property,
                    materialEditor);
                if (drawableDrawer != null)
                {
                    DrawPropertyDrawer(drawableDrawer, row.ContentRect, property, label, materialEditor);
                    return;
                }

                var externalDrawer = ShaderGUIReflectionUtility.GetPropertyDrawer(
                    MetaData.PerShaderData.Shader,
                    property,
                    out _);
                if (externalDrawer != null && externalDrawer is not IShaderGUIDrawer)
                {
                    DrawPropertyDrawer(externalDrawer, row.ContentRect, property, label, materialEditor);
                    return;
                }

                DrawDefaultProperty(row.ContentRect, materialEditor, property, label);
            }
        }

        private static void DrawPropertyDrawer(
            MaterialPropertyDrawer drawer,
            Rect position,
            MaterialProperty property,
            GUIContent label,
            MaterialEditor materialEditor)
        {
            EditorGUI.BeginChangeCheck();
            drawer.OnGUI(position, property, label, materialEditor);
            if (EditorGUI.EndChangeCheck())
                drawer.Apply(property);
        }

        private static void DrawPropertyRowDecorations(ShaderGUIControlRow row, PropertyStaticData staticData, Shader shader)
        {
            if (staticData == null)
                return;

            ShaderGUIControlRowDecorator.Draw(row, staticData, shader);
        }

        private float GetPropertyHeight(MaterialEditor materialEditor, MaterialProperty property)
        {
            var drawableDrawer = ShaderGUIReflectionUtility.GetDrawablePropertyDrawer(MetaData.PerShaderData.Shader, property, materialEditor);
            if (drawableDrawer != null)
                return Mathf.Max(0f, drawableDrawer.GetPropertyHeight(property, property.displayName, materialEditor));

            var propertyDrawer = ShaderGUIReflectionUtility.GetPropertyDrawer(MetaData.PerShaderData.Shader, property, out _);
            if (propertyDrawer != null && !(propertyDrawer is IShaderGUIDrawer))
                return Mathf.Max(0f, propertyDrawer.GetPropertyHeight(property, property.displayName, materialEditor));

            return EditorGUIUtility.singleLineHeight;
        }

        private static void DrawDefaultProperty(Rect position, MaterialEditor materialEditor, MaterialProperty property, GUIContent label)
        {
            var propertyType = ShaderGUIUtility.GetPropertyType(property);

            // Texture keeps the compact material row when no drawer takes ownership.
            switch (propertyType)
            {
                case ShaderPropertyType.Texture:
                    materialEditor.TexturePropertyMiniThumbnail(position, property, label.text, label.tooltip);
                    break;
                default:
                    materialEditor.DefaultShaderProperty(position, property, label.text);
                    break;
            }
        }

        /* 材质验证：在材质值改变或 Shader 切换时调用。*/
        public override void ValidateMaterial(Material material)
        {
        }
        
        public override void OnClosed(Material material)
        {
            base.OnClosed(material);
            ShaderGUIMetaDataCache.Release(material);
            ShaderGUIMetaDataCache.Release(this);
        }
        
        public static void OnValidate(Object[] materials)
        {
        }
    }
}
