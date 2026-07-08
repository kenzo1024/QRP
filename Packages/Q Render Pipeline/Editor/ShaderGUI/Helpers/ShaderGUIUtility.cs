using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// ShaderGUI 通用工具函数：提供属性判断、Keyword 管理、条件解析等实用方法。
    ///
    /// 主要功能分类：
    /// 1. 属性过滤和可见性判断（ShouldDrawProperty、EvaluateVisibility）
    /// 2. 属性类型和值操作（GetPropertyType、GetNumericValue、SetNumericValue）
    /// 3. Keyword 管理（ResolveKeyword、SetKeywordEnabled）
    /// 4. 条件解析（ParseCompareFunction）
    ///
    /// Unity 版本兼容：
    /// - Unity 6000.1+：使用 property.propertyType/propertyFlags
    /// - Unity 2019.4~6000.0：使用 property.type/flags（已废弃但仍可用）
    /// - 通过条件编译指令实现跨版本兼容
    /// </summary>
    public static class ShaderGUIUtility
    {
        /// <summary>
        /// 判断属性是否应该绘制：核心的属性过滤逻辑。
        ///
        /// 过滤规则（按优先级）：
        /// 1. staticData 或 dynamicData 为 null：跳过（数据缺失）
        /// 2. IsHidden = true：跳过（[HideInInspector] 标记）
        /// 3. drawExternalDrawerProperties = false 且没有 QShaderGUI Drawer：跳过（非 QShaderGUI 绘制属性被过滤）
        /// 4. IsVisible = false：跳过（ShowIf 条件不满足，未完全实现）
        ///
        /// 参数：
        /// - staticData：Shader 静态数据（属性声明、分组、ShowIf 条件）
        /// - dynamicData：Material 动态数据（当前可见性、激活状态）
        /// - metaData：完整元数据（未使用，预留扩展）
        /// - drawExternalDrawerProperties：是否绘制外部 Drawer 属性（从 DisplayModeData 读取）
        ///
        /// 使用场景：
        /// - 在 MaterialInspector.DrawProperties() 中调用，决定是否绘制每个属性
        ///
        /// 待优化：
        /// - 添加搜索过滤逻辑（检查属性名是否匹配搜索关键词）
        /// - 添加"只看改动"过滤（对比默认材质）
        /// </summary>
        public static bool ShouldDrawProperty(
            PropertyStaticData staticData,
            PropertyDynamicData dynamicData,
            ShaderGUIMetaData metaData,
            bool drawExternalDrawerProperties)
        {
            // MatDataTransfer is a feature marker, so keep those properties visible with QShaderGUI-only filtering.
            return staticData != null
                   && dynamicData != null
                   && !staticData.IsConsumed
                   && !staticData.IsHidden
                   && (drawExternalDrawerProperties || staticData.HasShaderGUIDrawer || staticData.SupportsMatDataTransfer)
                   && dynamicData.IsVisible;
        }

        /// <summary>
        /// 计算属性可见性：根据 ShowIf 条件判断属性是否应该显示。
        ///
        /// 当前实现：
        /// - 只检查 HideInInspector 标记
        /// - ShowIf 条件判断尚未实现
        ///
        /// 待实现逻辑：
        /// 1. 遍历 staticData.ShowIfConditions 列表
        /// 2. 对于每个条件：
        ///    a. 获取目标属性的值（material.GetFloat/GetInt）
        ///    b. 或者检查 Keyword 状态（material.IsKeywordEnabled）
        ///    c. 根据 CompareFunction 比较值
        /// 3. 所有条件都满足（AND 关系）时，IsVisible = true
        ///
        /// 示例实现：
        /// <code>
        /// foreach (var condition in staticData.ShowIfConditions)
        /// {
        ///     var targetValue = material.GetFloat(condition.TargetNameOrKeyword);
        ///     var satisfied = condition.CompareFunction switch
        ///     {
        ///         CompareFunction.Equal => targetValue == condition.Value,
        ///         CompareFunction.NotEqual => targetValue != condition.Value,
        ///         CompareFunction.Greater => targetValue > condition.Value,
        ///         // ... 其他比较函数
        ///         _ => true
        ///     };
        ///     if (!satisfied)
        ///     {
        ///         dynamicData.IsVisible = false;
        ///         return;
        ///     }
        /// }
        /// </code>
        ///
        /// 注意事项：
        /// - 需要处理目标属性不存在的情况（GetFloat 会抛异常）
        /// - Keyword 检查需要用 IsKeywordEnabled 而不是 GetFloat
        /// - 考虑支持 OR 逻辑（目前只支持 AND）
        /// </summary>
        public static void EvaluateVisibility(PropertyStaticData staticData, PropertyDynamicData dynamicData, Material material)
        {
            if (staticData == null || dynamicData == null)
                return;

            dynamicData.IsVisible = !staticData.IsHidden && EvaluateShowIfConditions(staticData, material);
            dynamicData.IsActive = true;
        }

        private static bool EvaluateShowIfConditions(PropertyStaticData staticData, Material material)
        {
            if (staticData.ShowIfConditions.Count == 0)
                return true;

            foreach (var condition in staticData.ShowIfConditions)
            {
                if (!EvaluateShowIfCondition(condition, material))
                    return false;
            }

            return true;
        }

        private static bool EvaluateShowIfCondition(ShowIfCondition condition, Material material)
        {
            if (condition == null || material == null || string.IsNullOrEmpty(condition.TargetNameOrKeyword))
                return true;

            var targetValue = material.HasProperty(condition.TargetNameOrKeyword)
                ? material.GetFloat(condition.TargetNameOrKeyword)
                : material.IsKeywordEnabled(condition.TargetNameOrKeyword) ? 1f : 0f;

            return Compare(targetValue, condition.CompareFunction, condition.Value);
        }

        private static bool Compare(float currentValue, CompareFunction compareFunction, float expectedValue)
        {
            const float epsilon = 0.0001f;
            return compareFunction switch
            {
                CompareFunction.Less => currentValue < expectedValue,
                CompareFunction.LessEqual => currentValue <= expectedValue,
                CompareFunction.Greater => currentValue > expectedValue,
                CompareFunction.NotEqual => Mathf.Abs(currentValue - expectedValue) > epsilon,
                CompareFunction.GreaterEqual => currentValue >= expectedValue,
                _ => Mathf.Abs(currentValue - expectedValue) <= epsilon
            };
        }

        /// <summary>
        /// 应用 MaterialPropertyDrawer 的副作用：预留的扩展点。
        ///
        /// 用途：
        /// - 在属性值改变后，调用所有 Drawer 的 Apply() 方法
        /// - 用于 Keyword 同步、RenderQueue 设置等副作用
        ///
        /// 待实现：
        /// - 获取属性的所有 Drawer（通过 ShaderGUIReflectionUtility）
        /// - 调用每个 Drawer 的 Apply() 方法
        /// </summary>
        public static void ApplyMaterialPropertyDrawers(Object[] targets)
        {
        }
        
        public static ShaderPropertyType GetPropertyType(MaterialProperty property)
        {
            // Unity 6 renamed the backing API, so centralize version differences here.
#if UNITY_6000_1_OR_NEWER
            return property.propertyType;
#else
            return (ShaderPropertyType)property.type;
#endif
        }

        public static ShaderPropertyFlags GetPropertyFlags(MaterialProperty property)
        {
#if UNITY_6000_1_OR_NEWER
            return property.propertyFlags;
#else
            return (ShaderPropertyFlags)property.flags;
#endif
        }

        /// <summary>
        /// 检查属性是否有指定的标记：通用的标记检查方法。
        ///
        /// 常用标记：
        /// - HideInInspector：隐藏属性
        /// - PerRendererData：每渲染器数据（如 SRP Batcher）
        /// - HDR：HDR 颜色
        /// - Gamma：Gamma 空间颜色
        /// - NoScaleOffset：纹理不显示 Tiling/Offset
        ///
        /// 使用示例：
        /// <code>
        /// if (ShaderGUIUtility.HasPropertyFlag(property, ShaderPropertyFlags.HDR))
        /// {
        ///     // 使用 HDR 颜色选择器
        /// }
        /// </code>
        /// </summary>
        public static bool HasPropertyFlag(MaterialProperty property, ShaderPropertyFlags flag)
        {
            return (GetPropertyFlags(property) & flag) != 0;
        }

        /// <summary>
        /// 获取属性的数值：将 Float/Range/Int 类型统一转换为 float。
        ///
        /// 用途：
        /// - ShowIf 条件判断（需要比较属性值）
        /// - Enum Drawer（Int 值映射到枚举）
        /// - 自定义逻辑需要读取属性值
        ///
        /// 返回值：
        /// - Float/Range：直接返回 floatValue
        /// - Int：返回 intValue（自动转换为 float）
        /// - 其他类型：返回 0（Color/Vector/Texture 没有单一数值）
        /// </summary>
        public static float GetNumericValue(MaterialProperty property)
        {
            return GetPropertyType(property) switch
            {
                ShaderPropertyType.Float => property.floatValue,
                ShaderPropertyType.Range => property.floatValue,
                ShaderPropertyType.Int => property.intValue,
                _ => 0f
            };
        }

        /// <summary>
        /// 设置属性的数值：将 float 值写入 Float/Range/Int 类型属性。
        ///
        /// 用途：
        /// - Enum Drawer 设置选中的枚举值
        /// - Toggle Drawer 设置开关状态（0/1）
        /// - 代码自动设置属性值
        ///
        /// 类型转换：
        /// - Float/Range：直接设置 floatValue
        /// - Int：四舍五入后设置 intValue
        /// - 其他类型：忽略（Color/Vector/Texture 需要用专用方法）
        /// </summary>
        public static void SetNumericValue(MaterialProperty property, float value)
        {
            switch (GetPropertyType(property))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    property.floatValue = value;
                    break;
                case ShaderPropertyType.Int:
                    property.intValue = Mathf.RoundToInt(value);
                    break;
            }
        }

        /// <summary>
        /// 解析 Keyword 名称：处理 Drawer Attribute 中的 Keyword 参数。
        ///
        /// Unity Keyword 命名规则：
        /// - 空字符串：使用默认命名规则 "{属性名}_ON"（自动转大写）
        ///   - 示例：属性 _AlphaTest → Keyword "ALPHATEST_ON" 或 "_ALPHATEST_ON"
        /// - "_"：不使用 Keyword（Toggle 不影响 Shader 分支）
        /// - 其他：使用指定的 Keyword 名称（自动转大写）
        ///
        /// 使用示例：
        /// Shader 中定义：
        ///   [Toggle] _AlphaTest("Alpha Test", Float) = 0        → Keyword: "_ALPHATEST_ON"
        ///   [Toggle(_ALPHATEST)] _AlphaTest("...", Float) = 0   → Keyword: "_ALPHATEST"
        ///   [Toggle(_)] _DebugMode("...", Float) = 0            → 无 Keyword（仅 UI 开关）
        ///
        /// 参数：
        /// - rawKeyword：Drawer Attribute 中的 Keyword 参数（可能为空）
        /// - propertyName：Shader 属性名（用于生成默认 Keyword）
        ///
        /// 返回值：
        /// - 解析后的 Keyword 名称（大写，或空字符串表示不使用 Keyword）
        /// </summary>
        public static string ResolveKeyword(string rawKeyword, string propertyName)
        {
            // Empty means Unity's default toggle keyword; "_" means no keyword should be touched.
            if (string.IsNullOrEmpty(rawKeyword))
                return $"{propertyName}_ON".ToUpperInvariant();

            return rawKeyword == "_" ? string.Empty : rawKeyword.ToUpperInvariant();
        }

        /// <summary>
        /// 批量设置 Keyword 状态：在多材质编辑时启用/禁用 Keyword。
        ///
        /// 执行流程：
        /// 1. 检查 keyword 是否为空（空字符串表示不使用 Keyword，直接返回）
        /// 2. 遍历所有 targets（MaterialEditor.targets 可能包含多个 Material）
        /// 3. 过滤掉非 Material 对象（多选时可能混入其他类型）
        /// 4. 对每个 Material 调用 EnableKeyword 或 DisableKeyword
        ///
        /// 用途：
        /// - Toggle Drawer：根据开关状态同步 Keyword
        /// - Enum Drawer：根据选中值启用/禁用对应的 Keyword
        /// - 自动设置 RenderState 时同步 Keyword
        ///
        /// 使用示例：
        /// <code>
        /// // Toggle Drawer 的 Apply() 方法
        /// public override void Apply(MaterialProperty property)
        /// {
        ///     var keyword = ShaderGUIUtility.ResolveKeyword(_keyword, property.name);
        ///     var enabled = property.floatValue > 0.5f;
        ///     ShaderGUIUtility.SetKeywordEnabled(property.targets, keyword, enabled);
        /// }
        /// </code>
        ///
        /// 注意事项：
        /// - 需要配合 Undo 使用（在调用前 Undo.RecordObjects）
        /// - Keyword 状态不会自动序列化到 .mat 文件，需要在 ValidateMaterial 中重新设置
        /// </summary>
        public static void SetKeywordEnabled(Object[] targets, string keyword, bool enabled)
        {
            if (string.IsNullOrEmpty(keyword) || targets == null)
                return;

            // Multi-object editing can pass non-material targets, so ignore anything unexpected.
            foreach (var target in targets)
            {
                if (target is not Material material)
                    continue;

                if (enabled)
                    material.EnableKeyword(keyword);
                else
                    material.DisableKeyword(keyword);
            }
        }

        /// <summary>
        /// 解析比较函数：将字符串转换为 CompareFunction 枚举。
        ///
        /// 支持的格式（两种都支持）：
        /// 1. 符号格式（Shader 友好）：<, <=, >, >=, ==, !=
        /// 2. 名称格式（代码友好）：Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual
        ///
        /// 默认值：Equal（当输入无法识别时）
        ///
        /// 用途：
        /// - ShowIfDrawer 解析 Attribute 参数（如 [ShowIf(_Mode, Greater, 0)]）
        /// - 自定义条件判断逻辑
        ///
        /// 使用示例：
        /// Shader 中定义：
        ///   [ShowIf(_Mode, >, 0)] _AlphaClip("Alpha Clip", Range(0,1)) = 0.5
        ///   [ShowIf(_Mode, Greater, 0)] _AlphaClip("Alpha Clip", Range(0,1)) = 0.5
        /// 两种写法等价，都会解析为 CompareFunction.Greater
        ///
        /// 注意：
        /// - 大小写敏感（"less" 不会被识别，会返回 Equal）
        /// - 建议在 Drawer 中使用符号格式（与 Shader 语法一致）
        /// </summary>
        public static CompareFunction ParseCompareFunction(string value)
        {
            // Accept both shader-friendly symbols and readable names.
            return value switch
            {
                "<" or "Less" => CompareFunction.Less,
                "<=" or "LessEqual" => CompareFunction.LessEqual,
                ">" or "Greater" => CompareFunction.Greater,
                "!=" or "NotEqual" => CompareFunction.NotEqual,
                ">=" or "GreaterEqual" => CompareFunction.GreaterEqual,
                _ => CompareFunction.Equal
            };
        }
    }
}
