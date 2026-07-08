using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// 条件显示 Drawer：根据其他属性的值或 Keyword 状态决定属性是否显示。
    ///
    /// Shader 中使用：
    /// [Main] _Mode("Render Mode", Float) = 0
    /// [ShowIf(_Mode, Equal, 1)] _AlphaClip("Alpha Clip", Range(0,1)) = 0.5
    /// [ShowIf(_Mode, Greater, 0)] _Color("Color", Color) = (1,1,1,1)
    ///
    /// 含义：
    /// - _AlphaClip 只在 _Mode == 1 时显示
    /// - _Color 只在 _Mode > 0 时显示
    ///
    /// 设计特点：
    /// - 这是一个"元数据 Drawer"，不绘制 UI（GetPropertyHeight 返回 0，OnGUI 为空）
    /// - 只在元数据构建阶段解析参数，写入 PropertyStaticData.ShowIfConditions
    /// - 实际的可见性判断在 ShaderGUIUtility.EvaluateVisibility() 中执行
    /// - 实际的属性过滤在 ShaderGUIUtility.ShouldDrawProperty() 中执行
    ///
    /// 当前状态（待完整实现）：
    /// - ✅ Drawer 类已存在，可以被 Shader 引用
    /// - ✅ GetPropertyHeight 返回 0（不占用空间）
    /// - ❌ BuildStaticMetaData 未实现（应该解析 Attribute 参数并添加到 ShowIfConditions）
    /// - ❌ 构造函数未定义（应该接收目标属性、比较函数、值参数）
    ///
    /// 完整实现指南：
    /// <code>
    /// private string _targetNameOrKeyword;
    /// private string _compareOp;
    /// private float _value;
    ///
    /// // Unity 通过 Attribute 反射调用构造函数
    /// public ShowIfDrawer(string target, string compareOp, float value)
    /// {
    ///     _targetNameOrKeyword = target;
    ///     _compareOp = compareOp;
    ///     _value = value;
    /// }
    ///
    /// public void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
    /// {
    ///     var compareFunc = ShaderGUIUtility.ParseCompareFunction(_compareOp);
    ///     data.ShowIfConditions.Add(new ShowIfCondition(_targetNameOrKeyword, compareFunc, _value));
    /// }
    /// </code>
    ///
    /// 注意事项：
    /// - 多个 [ShowIf] 应该是 AND 关系（所有条件都满足才显示）
    /// - 需要考虑目标属性不存在的情况（Shader 升级后属性可能被删除）
    /// - Keyword 检查需要特殊处理（IsKeywordEnabled 而不是 GetFloat）
    /// </summary>
    public sealed class ShowIfDrawer : MaterialPropertyDrawer, IShaderGUIDrawer
    {
        private readonly string _targetNameOrKeyword;
        private readonly string _compareOperator;
        private readonly float _value;

        public ShowIfDrawer()
            : this(string.Empty, "==", 1f)
        {
        }

        public ShowIfDrawer(string targetNameOrKeyword, float value)
            : this(targetNameOrKeyword, "==", value)
        {
        }

        public ShowIfDrawer(string targetNameOrKeyword, string compareOperator, float value)
        {
            _targetNameOrKeyword = targetNameOrKeyword;
            _compareOperator = compareOperator;
            _value = value;
        }

        /// <summary>
        /// 构建静态元数据：解析 ShowIf 参数并添加到 PropertyStaticData.ShowIfConditions。
        /// 待实现：当前为空实现。
        /// </summary>
        public void BuildStaticMetaData(Shader shader, MaterialProperty property, MaterialProperty[] properties, PropertyStaticData data)
        {
            if (string.IsNullOrEmpty(_targetNameOrKeyword))
                return;

            data.ShowIfConditions.Add(new ShowIfCondition(
                _targetNameOrKeyword,
                ShaderGUIUtility.ParseCompareFunction(_compareOperator),
                _value));
        }

        /// <summary>
        /// 行高：返回 0（不占用 Inspector 空间）。
        /// ShowIfDrawer 是元数据 Drawer，不应该影响布局。
        /// </summary>
        public override float GetPropertyHeight(MaterialProperty property, string label, MaterialEditor editor)
        {
            return 0f;
        }

        /// <summary>
        /// OnGUI：空实现（不绘制任何内容）。
        /// 实际的可见性控制在 MaterialInspector.DrawProperties() 中通过过滤实现。
        /// </summary>
        public override void OnGUI(Rect position, MaterialProperty property, GUIContent label, MaterialEditor editor)
        {
        }

        /// <summary>
        /// Apply：空实现（ShowIf 不修改 Material 值）。
        /// </summary>
        public override void Apply(MaterialProperty property)
        {
        }
    }
}
