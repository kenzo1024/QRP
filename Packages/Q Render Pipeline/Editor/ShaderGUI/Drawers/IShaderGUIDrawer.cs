using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// QRP Drawer 接口：定义 Drawer 如何参与元数据构建和值变化响应。
    ///
    /// 接口设计理念：
    /// - Unity 的 MaterialPropertyDrawer 只负责绘制 UI
    /// - QShaderGUI 需要 Drawer 提供额外的元数据（如分组关系、ShowIf 条件）
    /// - 通过这个接口，QRP Drawer 可以在构建阶段声明自己的元数据需求
    ///
    /// 实现方式：
    /// - 继承 MaterialPropertyDrawerBase（已实现 IShaderGUIDrawer）
    /// - 或者实现 IShaderGUIDrawer 接口并继承 MaterialPropertyDrawer
    ///
    /// 典型流程：
    /// 1. Shader 编译后，Unity 创建 Drawer 实例（通过 Attribute 反射）
    /// 2. ShaderGUIMetaDataCache.BuildPerShaderData() 调用每个 Drawer 的 BuildStaticMetaData()
    /// 3. Drawer 填充 PropertyStaticData（如设置 IsMain、Parent、ShowIfConditions）
    /// 4. MaterialInspector.OnGUI() 绘制属性时，Unity 调用 Drawer.OnGUI()
    /// 5. 属性值改变后，Unity 调用 Drawer.Apply()（用于同步 Keyword、RenderQueue 等副作用）
    ///
    /// 对比外部 Drawer：
    /// - 外部 Drawer（如 Unity 内置的 [Header]、[Space]）：不实现这个接口，QRP 只能检测到它们存在
    /// - QRP Drawer：实现这个接口，可以参与元数据构建和条件判断
    /// </summary>
    public interface IShaderGUIDrawer
    {
        /// <summary>
        /// 构建静态元数据：在 Shader 首次使用时调用，填充属性的静态信息。
        ///
        /// 参数：
        /// - shader：Shader 实例
        /// - property：当前属性（MaterialProperty）
        /// - properties：所有属性数组（用于查找关联属性，如 Parent）
        /// - data：PropertyStaticData 实例（需要填充的元数据）
        ///
        /// 典型实现示例：
        ///
        /// MainDrawer（标记主属性）：
        /// <code>
        /// public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        /// {
        ///     data.IsMain = true;
        ///     data.GroupName = property.displayName; // 使用显示名作为分组名
        ///     data.IsExpanded = true; // 默认展开
        /// }
        /// </code>
        ///
        /// SubDrawer（标记子属性，构建父子关系）：
        /// <code>
        /// public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        /// {
        ///     // 向上查找最近的 Main 属性作为父节点
        ///     var parentData = FindNearestMainProperty(property, properties);
        ///     if (parentData != null)
        ///     {
        ///         data.Parent = parentData;
        ///         parentData.Children.Add(data);
        ///     }
        /// }
        /// </code>
        ///
        /// ShowIfDrawer（解析条件参数）：
        /// <code>
        /// // 构造函数接收 Attribute 参数：[ShowIf(_Mode, Greater, 0)]
        /// private string _targetProperty;
        /// private string _compareOp;
        /// private float _value;
        ///
        /// public ShowIfDrawer(string target, string op, float value)
        /// {
        ///     _targetProperty = target;
        ///     _compareOp = op;
        ///     _value = value;
        /// }
        ///
        /// public override void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        /// {
        ///     var compareFunc = ShaderGUIUtility.ParseCompareFunction(_compareOp);
        ///     data.ShowIfConditions.Add(new ShowIfCondition(_targetProperty, compareFunc, _value));
        /// }
        /// </code>
        ///
        /// 注意事项：
        /// - 这个方法只在 Shader 首次使用时调用，结果会被缓存
        /// - 不要在这里读取 Material 的值（此时还没有 Material 实例）
        /// - 可以安全地修改 data 的所有字段
        /// - 可以访问 properties 数组查找关联属性
        /// </summary>
        void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data);

        /// <summary>
        /// 应用副作用：在属性值改变后调用，用于同步 Keyword、RenderQueue 等状态。
        ///
        /// 调用时机：
        /// - Unity 在 MaterialPropertyDrawer.OnGUI() 返回后自动调用
        /// - 如果属性值改变，Unity 会调用这个方法
        ///
        /// 典型实现示例：
        ///
        /// ToggleDrawer（同步 Keyword）：
        /// <code>
        /// private string _keyword;
        ///
        /// public ToggleDrawer(string keyword)
        /// {
        ///     _keyword = keyword;
        /// }
        ///
        /// public override void Apply(MaterialProperty property)
        /// {
        ///     var keyword = ShaderGUIUtility.ResolveKeyword(_keyword, property.name);
        ///     var enabled = property.floatValue > 0.5f;
        ///     ShaderGUIUtility.SetKeywordEnabled(property.targets, keyword, enabled);
        /// }
        /// </code>
        ///
        /// EnumDrawer（同步多个 Keyword）：
        /// <code>
        /// private string[] _keywords;
        ///
        /// public EnumDrawer(params string[] keywords)
        /// {
        ///     _keywords = keywords;
        /// }
        ///
        /// public override void Apply(MaterialProperty property)
        /// {
        ///     var index = (int)property.floatValue;
        ///     for (var i = 0; i < _keywords.Length; i++)
        ///     {
        ///         ShaderGUIUtility.SetKeywordEnabled(property.targets, _keywords[i], i == index);
        ///     }
        /// }
        /// </code>
        ///
        /// 注意事项：
        /// - 需要配合 Undo 使用（在 OnGUI 中修改值前调用 Undo.RecordObjects）
        /// - Keyword 状态不会自动序列化，需要在 ValidateMaterial 中重新设置
        /// - 多材质编辑时，property.targets 包含所有选中的 Material
        /// </summary>
        void Apply(MaterialProperty property);
    }
}
