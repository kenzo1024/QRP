using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// 元数据聚合器：将 Shader 静态数据、Material 动态数据、Inspector 临时数据打包在一起。
    /// 这个类是整个 QShaderGUI 的核心数据结构，所有绘制逻辑都通过它获取上下文信息。
    ///
    /// 设计理念：
    /// - Shader 级数据（PerShaderData）：与 Shader 声明相关，不随 Material 改变（如属性名、分组结构）
    /// - Material 级数据（PerMaterialData）：与当前 Material 实例相关，每帧可能改变（如可见性、激活状态）
    /// - Inspector 级数据（PerInspectorData）：与当前 Inspector 窗口相关，不随 Material 切换（如 UI 状态、折叠状态）
    ///
    /// 使用方式：
    /// - 由 ShaderGUIMetaDataCache.Build() 统一构建
    /// - 在 MaterialInspector.OnGUI() 中通过 MetaData 属性访问
    /// - Drawer 可以通过 ShaderGUIMetaDataCache.TryGetActive() 获取当前活动的元数据
    /// </summary>
    public sealed class ShaderGUIMetaData
    {
        // The inspector reads through this object so drawing code does not need to rebuild context per property.
        public PerShaderData PerShaderData { get; }
        public PerMaterialData PerMaterialData { get; }
        public PerInspectorData PerInspectorData { get; }

        public ShaderGUIMetaData(PerShaderData perShaderData, PerMaterialData perMaterialData, PerInspectorData perInspectorData)
        {
            PerShaderData = perShaderData;
            PerMaterialData = perMaterialData;
            PerInspectorData = perInspectorData;
        }
    }

    /// <summary>
    /// Shader 级静态数据：所有使用同一 Shader 的 Material 共享这份数据。
    ///
    /// 包含内容：
    /// - Shader 本身的引用
    /// - 每个属性的静态元数据（属性名、显示名、分组关系、ShowIf 条件等）
    ///
    /// 缓存策略：
    /// - 在 Shader 首次被使用时构建，之后直接从缓存读取
    /// - 只有当 Shader 重新编译或被 Release 时才会重建
    ///
    /// 注意事项：
    /// - 这里的数据是"静态"的，指的是不依赖 Material 实例的值
    /// - 但分组折叠状态理论上应该是用户级别的（未来可优化为持久化到 EditorPrefs）
    /// </summary>
    public sealed class PerShaderData
    {
        public Shader Shader { get; }
        public Dictionary<string, PropertyStaticData> Properties { get; }
        public ShaderGUIRootNode LayoutRoot { get; }

        public PerShaderData(Shader shader, Dictionary<string, PropertyStaticData> properties, ShaderGUIRootNode layoutRoot)
        {
            Shader = shader;
            Properties = properties;
            LayoutRoot = layoutRoot;
        }
    }

    /// <summary>
    /// 显示模式数据：控制 Inspector 的显示过滤选项。
    ///
    /// 功能说明：
    /// - DrawExternalDrawerProperties：是否显示使用外部 Drawer 的属性（如 Unity 内置的 [Header]、[Space] 等）
    /// - ShowDisplayModeBar：是否显示子 Toolbar（显示模式切换栏）
    ///
    /// 使用场景：
    /// - 当 QRP Drawer 和外部 Drawer 混用时，可以通过这个开关隐藏外部属性，只看 QRP 管理的属性
    /// - 方便调试和快速定位 QRP 特性
    ///
    /// 扩展方向：
    /// - 可以添加更多过滤选项，如"只显示改动的属性"、"只显示可见属性"等
    /// - 可以添加搜索过滤字段
    /// </summary>
    public sealed class DisplayModeData
    {
        public bool DrawExternalDrawerProperties { get; set; } = true;
        public bool ShowDisplayModeBar { get; set; }
    }

    public sealed class PerMaterialData
    {
        // Material-level data is rebuilt from the current MaterialProperty array.
        public Material Material { get; }
        public Dictionary<string, PropertyDynamicData> Properties { get; }

        public PerMaterialData(Material material, Dictionary<string, PropertyDynamicData> properties)
        {
            Material = material;
            Properties = properties;
        }
    }

    public sealed class PerInspectorData
    {
        // Inspector-level state. Keep this separate because one material can be viewed by multiple inspectors.
        public MaterialEditor MaterialEditor { get; internal set; }
        public DisplayModeData DisplayModeData { get; }
        public ShaderGUILayoutRuntime LayoutRuntime { get; }

        public PerInspectorData(MaterialEditor materialEditor)
        {
            MaterialEditor = materialEditor;
            DisplayModeData = new DisplayModeData();
            LayoutRuntime = new ShaderGUILayoutRuntime();
        }
    }

    /// <summary>
    /// 属性静态数据：与 Shader 属性声明相关的元数据。
    ///
    /// 字段说明：
    /// - Name：Shader 中的属性名（如 _MainTex）
    /// - DisplayName：Inspector 中显示的名称（如 "Main Texture"）
    /// - GroupName：分组名称（用于多级分组，未来扩展）
    /// - IsMain：是否是主属性（用 [Main] Drawer 标记的属性，可以作为分组头）
    /// - IsExpanded：折叠状态（未完全实现，应该从 EditorPrefs 读取）
    /// - IsHidden：是否隐藏（对应 Shader 的 [HideInInspector] 标记）
    /// - HasExternalDrawer：是否使用了非 QRP 的 Drawer（如 Unity 内置的 [Header]）
    /// - HasShaderGUIDrawer：是否由 QShaderGUI 自有 Drawer 接管绘制
    /// - Parent/Children：树形分组关系（Main 属性作为父节点，Sub 属性作为子节点）
    /// - ShowIfConditions：条件显示规则列表（如 [ShowIf(_Mode, Equal, 1)]）
    ///
    /// 构建时机：
    /// - 在 ShaderGUIMetaDataCache.BuildPerShaderData() 中构建
    /// - 由各个 Drawer 的 BuildStaticMetaData() 方法填充
    ///
    /// 待实现功能：
    /// - 多级分组（目前只支持 Main/Sub 两级）
    /// - 折叠状态持久化（存储到 EditorPrefs）
    /// - 分组内的缩进层级计算
    /// </summary>
    public sealed class PropertyStaticData
    {
        public string Name { get; internal set; }
        public string DisplayName { get; internal set; }
        public string GroupName { get; internal set; }
        public string LayerName { get; internal set; }
        public string ParentGroupName { get; internal set; }
        public bool IsMain { get; internal set; }
        public bool IsSub { get; internal set; }
        public bool IsExpanded { get; internal set; }
        public bool IsHidden { get; internal set; }
        public bool HasExternalDrawer { get; internal set; }
        public bool HasShaderGUIDrawer { get; internal set; }
        public bool SupportsMatDataTransfer { get; internal set; }
        public bool IsLayoutMarker { get; internal set; }
        public bool IsConsumed { get; internal set; }
        public string StyleName { get; internal set; }
        public PropertyStaticData Parent { get; internal set; }
        public List<PropertyStaticData> Children { get; }
        public List<ShowIfCondition> ShowIfConditions { get; }

        public PropertyStaticData(string name, string displayName, bool isHidden)
        {
            Name = name;
            DisplayName = displayName;
            IsHidden = isHidden;
            GroupName = string.Empty;
            LayerName = string.Empty;
            ParentGroupName = string.Empty;
            IsExpanded = true;
            StyleName = ShaderGUIStyleRegistry.DefaultGroupStyleName;
            Children = new List<PropertyStaticData>();
            ShowIfConditions = new List<ShowIfCondition>();
        }

        public void MarkMatDataTransferWritable()
        {
            SupportsMatDataTransfer = true;
        }

        public void MarkConsumed()
        {
            IsConsumed = true;
        }
    }

    public sealed class PropertyDynamicData
    {
        // Dynamic means this can change when the material or inspector state changes.
        public MaterialProperty Property { get; internal set; }
        public bool IsVisible { get; internal set; }
        public bool IsActive { get; internal set; }
        public string Tooltip { get; internal set; }

        public PropertyDynamicData(MaterialProperty property, bool isVisible, bool isActive, string tooltip)
        {
            Property = property;
            IsVisible = isVisible;
            IsActive = isActive;
            Tooltip = tooltip;
        }
    }

    /// <summary>
    /// 条件显示规则：定义属性在什么条件下显示。
    ///
    /// 工作原理：
    /// - 属性 A 可以依赖属性 B 或 Keyword C 的状态来决定是否显示
    /// - 支持多个条件（多个 ShowIfCondition）的 AND 关系
    ///
    /// 使用示例：
    /// Shader 中定义：
    ///   [Main] _Mode("Render Mode", Float) = 0
    ///   [ShowIf(_Mode, Equal, 1)] _AlphaClip("Alpha Clip", Range(0,1)) = 0.5
    ///
    /// 解析结果：
    ///   TargetNameOrKeyword = "_Mode"
    ///   CompareFunction = Equal
    ///   Value = 1
    ///
    /// 待实现功能：
    /// - 在 ShowIfDrawer.BuildStaticMetaData() 中解析 Drawer 参数
    /// - 在 ShaderGUIUtility.EvaluateVisibility() 中实现条件判断
    /// - 支持 Keyword 检查（如 [ShowIf(KEYWORD_ON, NotEqual, 0)]）
    /// - 支持 OR 逻辑（目前只支持 AND）
    /// </summary>
    public sealed class ShowIfCondition
    {
        public string TargetNameOrKeyword { get; internal set; }
        public CompareFunction CompareFunction { get; internal set; }
        public float Value { get; internal set; }

        public ShowIfCondition(string targetNameOrKeyword, CompareFunction compareFunction, float value)
        {
            TargetNameOrKeyword = targetNameOrKeyword;
            CompareFunction = compareFunction;
            Value = value;
        }
    }

    /// <summary>
    /// 比较函数枚举：对应常见的条件判断操作符。
    ///
    /// 使用示例：
    /// - Less: <  （小于）
    /// - Equal: == （等于）
    /// - NotEqual: != （不等于）
    ///
    /// 注意：
    /// - 解析时支持符号（<、==）和名称（Less、Equal）两种写法
    /// - 参见 ShaderGUIUtility.ParseCompareFunction()
    /// </summary>
    public enum CompareFunction
    {
        Less,
        Equal,
        LessEqual,
        Greater,
        NotEqual,
        GreaterEqual
    }
}
