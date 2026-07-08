using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// 主属性 Drawer：标记一个属性作为分组的头节点。
    ///
    /// Shader 中使用：
    /// [Main] _MainSettings("主要设置", Float) = 0
    /// [Sub] _Color("颜色", Color) = (1,1,1,1)
    /// [Sub] _MainTex("主纹理", 2D) = "white" {}
    ///
    /// 含义：
    /// - _MainSettings 作为分组头，可以折叠/展开整个分组
    /// - _Color 和 _MainTex 作为 _MainSettings 的子属性，跟随分组的折叠状态
    ///
    /// 当前状态（待完整实现）：
    /// - ✅ Drawer 类已存在，可以被 Shader 引用
    /// - ✅ 可以通过反射检测到这个 Drawer
    /// - ❌ BuildStaticMetaData 未实现（应该设置 IsMain = true）
    /// - ❌ DrawProperty 未实现（应该绘制折叠框头）
    /// - ❌ 没有维护 Children 列表（应该收集后续的 SubDrawer 属性）
    /// - ❌ 折叠状态未持久化（应该使用 EditorPrefs）
    ///
    /// 完整实现指南：
    /// <code>
    /// public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
    /// {
    ///     data.IsMain = true;
    ///     data.GroupName = property.displayName;
    ///     data.IsExpanded = LoadFoldoutState(shader, property.name);
    /// }
    ///
    /// public override void DrawProperty(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
    /// {
    ///     // 1. 绘制折叠框头（带箭头）
    ///     // 2. 如果是 Toggle 类型，同时绘制开关
    ///     // 3. 点击后切换 IsExpanded 并保存到 EditorPrefs
    /// }
    /// </code>
    ///
    /// 注意事项：
    /// - Main 和 Sub 的关系是按 Shader 中的属性顺序确定的
    /// - 一个 Sub 属性属于上面最近的 Main 属性
    /// - 多级分组（Main > Sub > SubSub）需要扩展为多种 Drawer
    /// </summary>
    public sealed class MainDrawer : MaterialPropertyDrawerBase
    {
        private readonly string _layerName;
        private readonly string _parentGroupName;

        public MainDrawer()
        {
        }

        public MainDrawer(string layerName)
        {
            _layerName = layerName;
        }

        public MainDrawer(string layerName, string parentGroupName)
        {
            _layerName = layerName;
            _parentGroupName = parentGroupName;
        }

        /// <summary>
        /// 构建静态元数据：标记这个属性为主属性。
        /// 待实现：设置 IsMain、GroupName、初始折叠状态。
        /// </summary>
        public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        {
            data.IsMain = true;
            data.LayerName = NormalizeLayerName(_layerName);
            data.ParentGroupName = NormalizeLayerName(_parentGroupName);
            data.GroupName = FormatGroupName(string.IsNullOrEmpty(data.LayerName) ? property.displayName : data.LayerName);
            data.IsExpanded = true;
            data.IsLayoutMarker = property.name.StartsWith("_QGUI_");
            data.StyleName = ShaderGUIStyleRegistry.DefaultGroupStyleName;
        }

        internal static string NormalizeLayerName(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "_")
                return string.Empty;

            return value.Trim();
        }

        private static string FormatGroupName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace('_', ' ').Trim();
        }

        /// <summary>
        /// 检查类型匹配：Main 可以应用到任何类型的属性。
        /// 常见组合：
        /// - [Main] Float：纯分组头
        /// - [Main] [Toggle] Float：带开关的分组头
        /// - [Main] Color：带颜色选择器的分组头
        /// </summary>
        public override bool IsMatchPropertyType(ShaderPropertyType propertyType)
        {
            return true;
        }

        /// <summary>
        /// 行高：使用单行高度（Unity 默认）。
        /// 未来支持自定义高度时可以修改这里。
        /// </summary>
        public override float GetPropertyHeight(MaterialProperty property, string label, MaterialEditor editor)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// 绘制属性：当前为空实现（占位），待完整实现折叠框头绘制。
        /// </summary>
        public override void DrawProperty(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
            base.DrawProperty(position, property, label, editor);
        }

        /// <summary>
        /// 应用副作用：待实现（如折叠所有子属性时通知 Inspector 重绘）。
        /// </summary>
        public override void Apply(MaterialProperty property)
        {
        }
    }
}
