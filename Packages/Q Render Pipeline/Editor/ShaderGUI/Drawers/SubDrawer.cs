namespace QRenderPipeline.Editor.ShaderGUI
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// 子属性 Drawer：标记一个属性属于上面最近的 Main 分组。
    ///
    /// Shader 中使用：
    /// [Main] _MainSettings("主要设置", Float) = 0
    /// [Sub] _Color("颜色", Color) = (1,1,1,1)
    /// [Sub] _MainTex("主纹理", 2D) = "white" {}
    ///
    /// 含义：
    /// - _Color 和 _MainTex 是 _MainSettings 的子属性
    /// - 跟随 Main 属性的折叠状态显示/隐藏
    /// - 在 Inspector 中应该有缩进显示
    ///
    /// 当前状态（待完整实现）：
    /// - ✅ Drawer 类已存在，可以被 Shader 引用
    /// - ✅ 可以通过反射检测到这个 Drawer
    /// - ❌ BuildStaticMetaData 未实现（应该向上查找 Main 属性建立父子关系）
    /// - ❌ 缩进绘制未实现（应该在 DrawProperty 中添加缩进）
    ///
    /// 完整实现指南：
    /// <code>
    /// public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
    /// {
    ///     // 向上查找最近的 Main 属性
    ///     var currentIndex = Array.IndexOf(properties, property);
    ///     for (var i = currentIndex - 1; i >= 0; i--)
    ///     {
    ///         var prevProperty = properties[i];
    ///         var prevData = staticDatas[prevProperty.name];
    ///         if (prevData.IsMain)
    ///         {
    ///             data.Parent = prevData;
    ///             prevData.Children.Add(data);
    ///             break;
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// 注意事项：
    /// - Sub 属性的可见性应该综合考虑：Main 是否折叠 + 自身的 ShowIf 条件
    /// - 一个 Sub 属性只能有一个 Main 父节点
    /// - 多级嵌套（Sub 下面的 Sub）需要扩展为 SubSubDrawer 或参数化（如 [Sub(2)]）
    /// </summary>
    public sealed class SubDrawer : MaterialPropertyDrawerBase
    {
        private readonly string _layerName;
        private readonly string _parentGroupName;

        public SubDrawer()
        {
        }

        public SubDrawer(string parentGroupName)
        {
            _parentGroupName = parentGroupName;
        }

        public SubDrawer(string layerName, string parentGroupName)
        {
            _layerName = layerName;
            _parentGroupName = parentGroupName;
        }

        public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        {
            data.IsSub = true;
            data.LayerName = MainDrawer.NormalizeLayerName(_layerName);
            data.ParentGroupName = MainDrawer.NormalizeLayerName(_parentGroupName);
            if (!string.IsNullOrEmpty(data.LayerName))
                data.GroupName = data.LayerName.Replace('_', ' ').Trim();
        }

        public override float GetPropertyHeight(MaterialProperty property, string label, MaterialEditor editor)
        {
            return 0f;
        }
    }
}
