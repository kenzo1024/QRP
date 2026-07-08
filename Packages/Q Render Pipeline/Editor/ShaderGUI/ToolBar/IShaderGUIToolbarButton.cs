using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// Toolbar 按钮接口：定义一个 Toolbar 按钮模块的契约。
    ///
    /// 设计理念：
    /// - 按钮是插件化的，可以通过 ToolbarUtility.AddButton() 动态注册
    /// - 每个按钮包含主按钮（图标）和可选的子工具栏（展开后的选项区）
    /// - 按钮可以根据元数据状态动态启用/禁用/高亮
    ///
    /// 主要方法：
    /// - Content：按钮显示内容（图标和 Tooltip）
    /// - IsEnabled：是否可交互（灰色显示时返回 false）
    /// - IsHighlighted：是否高亮显示（如有子选项被启用时）
    /// - OnClick：按钮点击事件
    /// - ShouldDrawSubToolbar：是否显示子工具栏
    /// - DrawSubToolbar：绘制子工具栏内容
    ///
    /// 当前已实现的按钮：
    /// - DisplayModeToolbarButton：显示模式切换（眼睛图标）
    ///
    /// 待添加按钮（基于规划文档）：
    /// - 全部折叠/展开按钮
    /// - 搜索框（需要扩展接口支持文本输入）
    /// - 复制/粘贴按钮
    /// - 只看改动按钮
    /// - 重置默认值按钮
    /// </summary>
    public interface IShaderGUIToolbarButton : IShaderGUIButton<ShaderGUIToolbarContext>
    {
        /// <summary>
        /// 是否显示子工具栏：返回 true 时会在主工具栏下方绘制子工具栏。
        /// 通常与 OnClick 配合使用：点击按钮切换 ShouldDrawSubToolbar 的返回值。
        /// </summary>
        bool ShouldDrawSubToolbar(ShaderGUIToolbarContext context);

        /// <summary>
        /// 绘制子工具栏内容：在主工具栏下方绘制选项区。
        /// 参数：
        /// - toolbarRect：分配给子工具栏的 Rect 区域
        /// - context：工具栏上下文
        /// </summary>
        void DrawSubToolbar(Rect toolbarRect, ShaderGUIToolbarContext context);
    }

    /// <summary>
    /// Toolbar 按钮上下文：传递给按钮模块的运行时数据。
    ///
    /// 包含内容：
    /// - MetaData：完整的元数据（用于读取/修改 Inspector 状态）
    /// - ButtonStyle：主按钮样式（图标按钮风格）
    /// - OptionToggleStyle：子工具栏选项样式（圆角 Toggle 风格）
    ///
    /// 设计目的：
    /// - 避免在每个按钮中重复创建 GUIStyle
    /// - 提供统一的视觉风格
    /// - 集中管理上下文数据
    /// </summary>
    public readonly struct ShaderGUIToolbarContext
    {
        public ShaderGUIMetaData MetaData { get; }
        public ShaderGUIToolbarStyle ToolbarStyle { get; }
        public GUIStyle ButtonStyle { get; }
        public GUIStyle HighlightedButtonStyle { get; }
        public GUIStyle OptionToggleStyle { get; }

        public ShaderGUIToolbarContext(
            ShaderGUIMetaData metaData,
            ShaderGUIToolbarStyle toolbarStyle,
            GUIStyle buttonStyle,
            GUIStyle highlightedButtonStyle,
            GUIStyle optionToggleStyle)
        {
            MetaData = metaData;
            ToolbarStyle = toolbarStyle;
            ButtonStyle = buttonStyle;
            HighlightedButtonStyle = highlightedButtonStyle;
            OptionToggleStyle = optionToggleStyle;
        }
    }

    /// <summary>
    /// Toolbar 按钮基类：提供默认实现，简化按钮创建。
    ///
    /// 子类只需要重写：
    /// - Content：按钮内容（必须）
    /// - OnClick：点击事件（必须）
    /// - 其他方法按需重写
    ///
    /// 默认行为：
    /// - IsEnabled：始终返回 true（始终可交互）
    /// - IsHighlighted：始终返回 false（不高亮）
    /// - ShouldDrawSubToolbar：始终返回 false（无子工具栏）
    /// - DrawSubToolbar：空实现
    ///
    /// 使用示例：
    /// <code>
    /// public sealed class CollapseAllButton : ShaderGUIToolbarButtonBase
    /// {
    ///     public override GUIContent Content => EditorGUIUtility.IconContent("d_Toolbar Minus");
    ///
    ///     public override void OnClick(ShaderGUIToolbarContext context)
    ///     {
    ///         foreach (var data in context.MetaData.PerShaderData.Properties.Values)
    ///         {
    ///             if (data.IsMain) data.IsExpanded = false;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class ShaderGUIToolbarButtonBase : ShaderGUIButtonBase<ShaderGUIToolbarContext>, IShaderGUIToolbarButton
    {
        public virtual bool ShouldDrawSubToolbar(ShaderGUIToolbarContext context)
        {
            return false;
        }

        public virtual void DrawSubToolbar(Rect toolbarRect, ShaderGUIToolbarContext context)
        {
        }
    }
}
