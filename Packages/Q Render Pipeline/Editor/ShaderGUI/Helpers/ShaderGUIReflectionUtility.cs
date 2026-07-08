using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// Shader 属性反射工具：通过反射获取 Unity 内部的 MaterialPropertyDrawer。
    ///
    /// 背景知识：
    /// - Unity 的 MaterialPropertyDrawer 系统是内部实现，外部代码无法直接访问
    /// - Unity 使用 MaterialPropertyHandler 类管理每个属性的 Drawer 列表
    /// - 每个属性可以有 1 个 PropertyDrawer（主 Drawer）和多个 DecoratorDrawer（装饰 Drawer）
    ///
    /// 为什么需要反射：
    /// - 需要知道一个属性是否使用了 QRP 的 Drawer 还是外部 Drawer（如 Unity 内置的 [Header]）
    /// - 需要在 QRP Drawer 之外保留其他 Drawer 的功能（如 [Space]、[Toggle] 等）
    /// - Unity 没有提供公开 API 获取这些信息
    ///
    /// 参考实现：
    /// - 这个方案来自 LWGUI（一个开源的 Unity ShaderGUI 框架）
    /// - 类似的反射技术在多个 ShaderGUI 实现中使用
    ///
    /// 使用方式：
    /// - GetDrawers(shader, property)：获取属性的所有 QRP Drawer
    /// - GetPropertyDrawer(shader, property, out decorators)：获取主 Drawer 和装饰 Drawer 列表
    ///
    /// 注意事项：
    /// - 反射可能在 Unity 版本更新后失效（如果 Unity 重命名了内部类/方法）
    /// - 当前支持 Unity 2019.4+ 到 Unity 6000.1+
    /// - 如果反射失败，相关功能会降级但不会崩溃
    /// </summary>
    public static class ShaderGUIReflectionUtility
    {
        // Unity keeps shader property drawer handlers internal, so this mirrors LWGUI's reflection approach.

        /// <summary>
        /// Unity 内部类型：MaterialPropertyHandler（管理一个属性的所有 Drawer）。
        /// 反射路径：UnityEditor.MaterialPropertyHandler
        /// </summary>
        private static readonly Type MaterialPropertyHandlerType =
            Assembly.GetAssembly(typeof(UnityEditor.Editor))?.GetType("UnityEditor.MaterialPropertyHandler");

        /// <summary>
        /// 静态方法：GetHandler(Shader shader, string propertyName)。
        /// 返回指定属性的 MaterialPropertyHandler 实例。
        /// </summary>
        private static readonly MethodInfo GetHandlerMethod =
            MaterialPropertyHandlerType?.GetMethod("GetHandler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// 属性：propertyDrawer（主 Drawer，一个属性只能有一个）。
        /// 类型：MaterialPropertyDrawer
        /// </summary>
        private static readonly PropertyInfo PropertyDrawerProperty =
            MaterialPropertyHandlerType?.GetProperty("propertyDrawer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// 字段：m_DecoratorDrawers（装饰 Drawer 列表，一个属性可以有多个）。
        /// 类型：List&lt;MaterialPropertyDrawer&gt;
        /// 示例：[Space(10)] [Header("Main Settings")] 都是装饰 Drawer
        /// </summary>
        private static readonly FieldInfo DecoratorDrawersField =
            MaterialPropertyHandlerType?.GetField("m_DecoratorDrawers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// 获取属性的所有 QRP Drawer（过滤掉外部 Drawer）。
        ///
        /// 返回值：
        /// - 只包含实现了 IShaderGUIDrawer 接口的 Drawer
        /// - 外部 Drawer（如 Unity 内置的 [Header]）会被过滤掉
        ///
        /// 使用场景：
        /// - 在 BuildPerShaderData() 中调用，获取需要构建元数据的 Drawer 列表
        /// </summary>
        public static IReadOnlyList<IShaderGUIDrawer> GetDrawers(Shader shader, MaterialProperty property)
        {
            return GetDrawers(shader, property, out _);
        }

        /// <summary>
        /// 获取属性的所有 QRP Drawer，并返回是否有外部 Drawer。
        ///
        /// 执行流程：
        /// 1. 通过反射获取这个属性的主 Drawer 和装饰 Drawer 列表
        /// 2. 遍历所有 Drawer，检查是否实现了 IShaderGUIDrawer 接口
        /// 3. QRP Drawer：添加到返回列表
        /// 4. 外部 Drawer：记录到 hasAnyDrawer，但不添加到列表
        /// 5. 如果有任何 Drawer（包括外部的），但 QRP Drawer 列表为空，说明这个属性使用了外部 Drawer
        ///
        /// 参数：
        /// - shader：Shader 实例
        /// - property：MaterialProperty 实例
        /// - hasExternalDrawer：输出参数，指示是否有外部 Drawer
        ///
        /// 返回值：
        /// - QRP Drawer 列表（实现了 IShaderGUIDrawer 接口的 Drawer）
        ///
        /// 外部 Drawer 判断逻辑：
        /// - hasExternalDrawer = true：属性有 Drawer，但都不是 QRP 的（如 [Header]、[Space]）
        /// - hasExternalDrawer = false：属性没有 Drawer，或者至少有一个 QRP Drawer
        ///
        /// 使用场景：
        /// - 在 BuildPerShaderData() 中调用，用于设置 PropertyStaticData.HasExternalDrawer
        /// - 这个标记会影响属性是否在 Inspector 中显示（根据 DrawExternalDrawerProperties 开关）
        /// </summary>
        public static IReadOnlyList<IShaderGUIDrawer> GetDrawers(Shader shader, MaterialProperty property, out bool hasExternalDrawer)
        {
            var drawers = new List<IShaderGUIDrawer>();
            var hasAnyDrawer = false;
            var propertyDrawer = GetPropertyDrawer(shader, property, out var decoratorDrawers);

            AddDrawer(propertyDrawer, drawers, ref hasAnyDrawer);

            if (decoratorDrawers != null)
            {
                foreach (var decoratorDrawer in decoratorDrawers)
                    AddDrawer(decoratorDrawer, drawers, ref hasAnyDrawer);
            }

            // If a property has a QRP drawer, the property belongs to this inspector even if it also has decorators.
            hasExternalDrawer = hasAnyDrawer && drawers.Count == 0;
            return drawers;
        }

        /// <summary>
        /// 添加 Drawer 到列表：只添加 QRP Drawer，但记录所有 Drawer 的存在。
        ///
        /// 逻辑：
        /// - 如果 drawer 不为 null，设置 hasAnyDrawer = true（表示这个属性有 Drawer）
        /// - 如果 drawer 实现了 IShaderGUIDrawer 接口，添加到 drawers 列表
        ///
        /// 参数：
        /// - drawer：MaterialPropertyDrawer 实例（可能是 QRP 的，也可能是外部的）
        /// - drawers：QRP Drawer 列表
        /// - hasAnyDrawer：是否有任何 Drawer（引用参数）
        /// </summary>
        private static void AddDrawer(MaterialPropertyDrawer drawer, List<IShaderGUIDrawer> drawers, ref bool hasAnyDrawer)
        {
            if (drawer == null)
                return;

            // Track all drawers, but only QRP-compatible drawers can write metadata.
            hasAnyDrawer = true;

            if (drawer is IShaderGUIDrawer shaderGUIDrawer)
            {
                drawers.Add(shaderGUIDrawer);
            }
        }

        /// <summary>
        /// 获取属性的主 Drawer 和装饰 Drawer 列表（包括 QRP 和外部的）。
        ///
        /// 执行流程：
        /// 1. 通过反射调用 MaterialPropertyHandler.GetHandler(shader, propertyName)
        /// 2. 如果 handler 为 null，返回 null（这个属性没有任何 Drawer）
        /// 3. 从 handler 中读取 propertyDrawer（主 Drawer）
        /// 4. 从 handler 中读取 m_DecoratorDrawers（装饰 Drawer 列表）
        ///
        /// 参数：
        /// - shader：Shader 实例
        /// - property：MaterialProperty 实例
        /// - decoratorDrawers：输出参数，装饰 Drawer 列表
        ///
        /// 返回值：
        /// - 主 Drawer（MaterialPropertyDrawer 实例，可能为 null）
        ///
        /// Drawer 类型说明：
        /// - 主 Drawer：一个属性只能有一个，用于控制属性的主要绘制逻辑
        ///   - 示例：[Main]、[Sub]、[Toggle]、[Enum]
        /// - 装饰 Drawer：一个属性可以有多个，用于添加装饰或修改布局
        ///   - 示例：[Header("标题")]、[Space(10)]、[Tooltip("提示")]
        ///
        /// 注意事项：
        /// - 如果反射失败（Unity 版本不兼容），会返回 null
        /// - 调用方需要处理 null 的情况
        /// </summary>
        public static MaterialPropertyDrawer GetPropertyDrawer(Shader shader, MaterialProperty property, out List<MaterialPropertyDrawer> decoratorDrawers)
        {
            decoratorDrawers = null;

            if (shader == null || property == null)
                return null;

            // Unity stores the drawer list in an internal handler per shader property.
            var handler = GetHandlerMethod?.Invoke(null, new object[] { shader, property.name });
            if (handler == null)
                return null;

            decoratorDrawers = DecoratorDrawersField?.GetValue(handler) as List<MaterialPropertyDrawer>;
            return PropertyDrawerProperty?.GetValue(handler) as MaterialPropertyDrawer;
        }

        public static MaterialPropertyDrawer GetDrawablePropertyDrawer(Shader shader, MaterialProperty property, MaterialEditor editor)
        {
            var propertyDrawer = GetPropertyDrawer(shader, property, out var decoratorDrawers);
            if (IsDrawablePropertyDrawer(propertyDrawer, property, editor))
                return propertyDrawer;

            if (decoratorDrawers == null)
                return null;

            foreach (var decoratorDrawer in decoratorDrawers)
            {
                if (IsDrawablePropertyDrawer(decoratorDrawer, property, editor))
                    return decoratorDrawer;
            }

            return null;
        }

        private static bool IsDrawablePropertyDrawer(MaterialPropertyDrawer drawer, MaterialProperty property, MaterialEditor editor)
        {
            return drawer is IShaderGUIDrawer
                   && drawer.GetPropertyHeight(property, property.displayName, editor) > 0f;
        }
    }
}
