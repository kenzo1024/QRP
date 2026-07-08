using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// ShaderGUI 通用按钮接口：只描述按钮本身的行为，不绑定具体布局。
    /// Toolbar、属性行右侧按钮、弹窗里的按钮，都可以基于这个接口扩展。
    /// </summary>
    public interface IShaderGUIButton<in TContext>
    {
        /// <summary>
        /// 按钮显示内容：包含文字、图标和 Tooltip。
        /// </summary>
        GUIContent Content { get; }

        /// <summary>
        /// 是否可交互：返回 false 时按钮显示为灰色不可点击。
        /// </summary>
        bool IsEnabled(TContext context);

        /// <summary>
        /// 是否高亮显示：返回 true 时提示用户当前按钮处于活跃状态。
        /// </summary>
        bool IsHighlighted(TContext context);

        /// <summary>
        /// 按钮点击事件：用户点击按钮时触发。
        /// </summary>
        void OnClick(TContext context);
    }
    
    public abstract class ShaderGUIButtonBase<TContext> : IShaderGUIButton<TContext>
    {
        public abstract GUIContent Content { get; }

        public virtual bool IsEnabled(TContext context)
        {
            return true;
        }

        public virtual bool IsHighlighted(TContext context)
        {
            return false;
        }

        public abstract void OnClick(TContext context);
    }
}
