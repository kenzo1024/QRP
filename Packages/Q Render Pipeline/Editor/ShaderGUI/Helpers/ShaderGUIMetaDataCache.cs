using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QRenderPipeline.Editor.ShaderGUI
{
    /// <summary>
    /// 元数据缓存管理器：三级缓存系统（Shader/Material/Inspector）。
    ///
    /// 缓存架构：
    /// - Shader 级缓存：Shader → PerShaderData（静态元数据，如属性声明、分组结构、ShowIf 条件）
    /// - Material 级缓存：Material → PerMaterialData（动态数据，如当前可见性、激活状态）
    /// - Inspector 级缓存：MaterialInspector → PerInspectorData（UI 状态，如折叠状态、搜索过滤）
    /// - Active 缓存：MaterialEditor → ShaderGUIMetaData（当前正在绘制的元数据实例，供 Drawer 获取上下文）
    ///
    /// 性能优化：
    /// - Shader 级数据只在首次使用时构建（成本最高，包含反射和 Drawer 元数据解析）
    /// - Material 级数据每帧重建（成本可控，主要是属性可见性计算）
    /// - Inspector 级数据持久化到 Inspector 关闭（存储 UI 状态）
    ///
    /// 生命周期：
    /// - Build()：每帧在 MaterialInspector.OnGUI() 中调用，返回聚合后的 ShaderGUIMetaData
    /// - Release(Material)：Material 被销毁或 Inspector 关闭时调用
    /// - Release(Shader)：Shader 被重新编译时调用（当前未自动触发，需要手动调用）
    /// - Release(MaterialInspector)：Inspector 窗口关闭时调用
    /// - ReleaseAll()：全局清理（如插件卸载）
    ///
    /// 注意事项：
    /// - Shader 重编译时应该自动清理缓存，但当前未实现监听机制
    /// - 多 Inspector 场景下，同一 Material 可能有多份 PerMaterialData（每个 Shader 缓存桶都可能存一份）
    /// - ActiveMetaDatas 字典在 Inspector 关闭时需要手动清理，否则可能泄露
    ///
    /// 待优化：
    /// - 监听 Shader 重编译事件，自动清理 Shader 缓存
    /// - 分离 Material 数据中不需要每帧重建的部分（如分组关系）
    /// - 优化 Shader 缓存查找性能（当前用 Dictionary，考虑改用 Shader.GetInstanceID()）
    /// </summary>
    public static class ShaderGUIMetaDataCache
    {
        private sealed class PerMaterialCache
        {
            public PerMaterialData PerMaterialData;
        }

        private sealed class PerShaderCache
        {
            public PerShaderData PerShaderData;
            public string AttributeFingerprint;
            public readonly Dictionary<Material, PerMaterialCache> PerMaterialDataCaches = new();
        }

        // shader static properties
        private static readonly Dictionary<Shader, PerShaderCache> ShaderCaches = new();
        // multi inspector to one material
        private static readonly Dictionary<MaterialInspector, PerInspectorData> InspectorDataCaches = new();
        // for drawer fetch cache
        private static readonly Dictionary<MaterialEditor, ShaderGUIMetaData> ActiveMetaDatas = new();

        internal static readonly HashSet<string> PendingConsumedProperties = new();

        /// <summary>
        /// 构建元数据：三级缓存的统一入口，每帧在 MaterialInspector.OnGUI() 中调用。
        ///
        /// 执行流程：
        /// 1. 检查参数有效性（Shader/Material/Editor/Inspector 都不能为 null）
        /// 2. 查找或构建 Shader 级缓存（PerShaderData）
        ///    - 如果 Shader 首次使用，调用 BuildPerShaderData() 完整构建
        ///    - 否则从 ShaderCaches 字典中直接读取
        /// 3. 查找或重建 Material 级缓存（PerMaterialData）
        ///    - 每帧都会重建，因为可见性状态可能改变
        ///    - 存储在 Shader 缓存桶下的 PerMaterialDataCaches 中
        /// 4. 查找或构建 Inspector 级缓存（PerInspectorData）
        ///    - 如果 Inspector 首次使用，创建新实例
        ///    - 否则更新 MaterialEditor 引用（因为同一 Inspector 可能绑定不同的 Editor）
        /// 5. 聚合三层数据为 ShaderGUIMetaData 并设置为活动实例
        ///
        /// 性能特征：
        /// - 首次调用：慢（需要反射和 Drawer 解析）
        /// - 后续调用：快（Shader 数据来自缓存，只重建 Material 数据）
        ///
        /// 返回值：
        /// - 成功：ShaderGUIMetaData 实例
        /// - 失败：null（参数无效）
        /// </summary>
        public static ShaderGUIMetaData Build(
            Shader shader,
            Material material,
            MaterialEditor materialEditor,
            MaterialInspector materialInspector,
            MaterialProperty[] properties)
        {
            // Build is called every OnGUI; only cheap material data is rebuilt on the current path.
            if (shader == null || material == null || materialEditor == null || materialInspector == null || properties == null)
                return null;

            // build shader cache
            var attributeFingerprint = BuildShaderAttributeFingerprint(shader);
            if (!ShaderCaches.TryGetValue(shader, out var shaderCache)
                || shaderCache.AttributeFingerprint != attributeFingerprint)
            {
                shaderCache = new PerShaderCache
                {
                    PerShaderData = BuildPerShaderData(shader, properties),
                    AttributeFingerprint = attributeFingerprint
                };
                ShaderCaches[shader] = shaderCache;
            }

            // build material cache
            if (!shaderCache.PerMaterialDataCaches.TryGetValue(material, out var materialCache))
            {
                materialCache = new PerMaterialCache();
                shaderCache.PerMaterialDataCaches.Add(material, materialCache);
            }
            materialCache.PerMaterialData = BuildPerMaterialData(material, properties, shaderCache.PerShaderData);

            // build inspector cache
            if (!InspectorDataCaches.TryGetValue(materialInspector, out var inspectorData))
            {
                inspectorData = new PerInspectorData(materialEditor);
                InspectorDataCaches.Add(materialInspector, inspectorData);
            }
            else
            {
                inspectorData.MaterialEditor = materialEditor;
            }

            var metaData = new ShaderGUIMetaData(shaderCache.PerShaderData, materialCache.PerMaterialData, inspectorData);
            SetActive(materialEditor, metaData);
            return metaData;
        }

        /// <summary>
        /// 设置活动元数据：让 Drawer 可以通过 TryGetActive() 获取当前绘制上下文。
        ///
        /// 用途：
        /// - Unity 在调用 MaterialPropertyDrawer.OnGUI() 时不会传递完整的上下文信息
        /// - Drawer 需要知道当前的元数据（如其他属性的值、Keyword 状态）才能做条件判断
        /// - 通过这个字典，Drawer 可以用 MaterialEditor 实例获取当前活动的元数据
        ///
        /// 调用时机：
        /// - 在 MaterialInspector.OnGUI() 开始时调用 SetActive(editor, metaData)
        /// - 在 MaterialInspector.OnGUI() 结束时可以调用 SetActive(editor, null) 清理（可选）
        ///
        /// 注意：
        /// - MaterialEditor 实例在 Inspector 生命周期内会变化，需要每帧更新
        /// - 多 Inspector 场景下，每个 Editor 都有独立的 Active 实例
        /// </summary>
        public static void SetActive(MaterialEditor materialEditor, ShaderGUIMetaData metaData)
        {
            if (materialEditor == null)
                return;

            // Active data lets drawers ask for the current inspector context while Unity is drawing them.
            if (metaData == null)
                ActiveMetaDatas.Remove(materialEditor);
            else
                ActiveMetaDatas[materialEditor] = metaData;
        }

        public static bool TryGetActive(MaterialEditor materialEditor, out ShaderGUIMetaData metaData)
        {
            if (materialEditor == null)
            {
                metaData = null;
                return false;
            }

            return ActiveMetaDatas.TryGetValue(materialEditor, out metaData);
        }

        /// <summary>
        /// 释放 Material 缓存：当 Material 被销毁或 Inspector 关闭时调用。
        ///
        /// 清理范围：
        /// - 遍历所有 Shader 缓存桶，移除这个 Material 的 PerMaterialData
        /// - 因为同一 Material 可能在多个 Shader 缓存桶下有副本（如果材质曾经切换过 Shader）
        ///
        /// 调用时机：
        /// - MaterialInspector.OnClosed(Material)
        /// - Material 被销毁时（需要监听 OnDestroy，当前未实现）
        /// </summary>
        public static void Release(Material material)
        {
            if (material == null)
                return;

            // The same material may appear under any shader cache, so remove it from all buckets.
            foreach (var shaderCache in ShaderCaches.Values)
                shaderCache.PerMaterialDataCaches.Remove(material);
        }

        /// <summary>
        /// 释放 Shader 缓存：当 Shader 被重新编译或卸载时调用。
        ///
        /// 清理范围：
        /// - 移除这个 Shader 的 PerShaderData（包含属性元数据、分组结构等）
        /// - 同时清理这个 Shader 下所有 Material 的 PerMaterialData
        ///
        /// 调用时机：
        /// - 手动调用（Shader 重编译后）
        /// - 待实现：监听 Shader 重编译事件，自动调用
        ///
        /// 注意：
        /// - 如果不清理，Shader 重编译后属性签名变化，可能导致元数据过期
        /// - 建议监听 UnityEditor.Callbacks.DidReloadScripts 或 ShaderUtil 相关事件
        /// </summary>
        public static void Release(Shader shader)
        {
            if (shader != null)
                ShaderCaches.Remove(shader);
        }

        /// <summary>
        /// 释放 Inspector 缓存：当 Inspector 窗口关闭时调用。
        ///
        /// 清理范围：
        /// - 移除 PerInspectorData（UI 状态，如折叠状态、搜索过滤）
        /// - 同时清理 ActiveMetaDatas 中的对应项（防止泄露）
        ///
        /// 调用时机：
        /// - MaterialInspector.OnClosed(Material)
        ///
        /// 注意：
        /// - Inspector 窗口可能被复用，不一定每次都重新创建
        /// - 如果不清理，Inspector 关闭后 ActiveMetaDatas 中的引用会泄露
        /// </summary>
        public static void Release(MaterialInspector materialInspector)
        {
            if (materialInspector == null || !InspectorDataCaches.TryGetValue(materialInspector, out var inspectorData))
                return;

            ActiveMetaDatas.Remove(inspectorData.MaterialEditor);
            InspectorDataCaches.Remove(materialInspector);
        }

        /// <summary>
        /// 清空所有缓存：全局清理，用于插件卸载或编辑器状态重置。
        ///
        /// 清理范围：
        /// - 所有 Shader 级缓存
        /// - 所有 Material 级缓存（间接清理，因为存在 Shader 缓存下）
        /// - 所有 Inspector 级缓存
        /// - 所有活动元数据引用
        ///
        /// 调用时机：
        /// - 插件卸载
        /// - 编辑器 Domain Reload
        /// - 测试清理
        ///
        /// 注意：
        /// - 调用后所有元数据都会在下次 Build() 时重建
        /// - 成本较高，不建议频繁调用
        /// </summary>
        public static void ReleaseAll()
        {
            ShaderCaches.Clear();
            InspectorDataCaches.Clear();
            ActiveMetaDatas.Clear();
        }

        /// <summary>
        /// 构建 Shader 静态数据：解析 Shader 属性和 Drawer 元数据。
        ///
        /// 执行流程：
        /// 1. 遍历所有属性（MaterialProperty[]）
        /// 2. 为每个属性创建 PropertyStaticData（名称、显示名、是否隐藏）
        /// 3. 通过反射获取这个属性的所有 Drawer（PropertyDrawer + DecoratorDrawers）
        /// 4. 检查 Drawer 是否是 QRP 的（实现了 IShaderGUIDrawer 接口）
        /// 5. 调用每个 QRP Drawer 的 BuildStaticMetaData() 方法填充元数据
        ///    - MainDrawer：标记 IsMain = true，用于分组
        ///    - SubDrawer：标记 Parent，构建父子关系
        ///    - ShowIfDrawer：解析条件规则，填充 ShowIfConditions 列表
        /// 6. 返回 PerShaderData（包含所有属性的静态元数据字典）
        ///
        /// 性能特征：
        /// - 成本最高的操作（包含反射和 Drawer 元数据解析）
        /// - 只在 Shader 首次使用时调用，之后从缓存读取
        ///
        /// 注意事项：
        /// - 外部 Drawer（如 Unity 内置的 [Header]）也会被检测到，但不会调用 BuildStaticMetaData()
        /// - HasExternalDrawer 标记会影响属性是否在 Inspector 中显示
        /// </summary>
        private static PerShaderData BuildPerShaderData(Shader shader, MaterialProperty[] properties)
        {
            PendingConsumedProperties.Clear();
            var staticDatas = new Dictionary<string, PropertyStaticData>();
            foreach (var property in properties)
            {
                var staticData = new PropertyStaticData(
                    property.name,
                    property.displayName,
                    ShaderGUIUtility.HasPropertyFlag(property, UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector));

                var drawers = ShaderGUIReflectionUtility.GetDrawers(shader, property, out var hasExternalDrawer);
                staticData.HasExternalDrawer = hasExternalDrawer;
                staticData.HasShaderGUIDrawer = drawers.Count > 0;

                // Only QRP drawers contribute metadata here; external drawers are just recorded.
                foreach (var drawer in drawers)
                    drawer.BuildStaticMetaData(shader, property, properties, staticData);

                ApplyShaderGUIAttributes(shader, property, properties, staticData);
                staticDatas[property.name] = staticData;
            }

            MarkConsumedProperties(staticDatas);

            BuildPropertyHierarchy(properties, staticDatas);
            var layoutRoot = ShaderGUILayoutBuilder.Build(properties, staticDatas);
            return new PerShaderData(shader, staticDatas, layoutRoot);
        }

        private static void MarkConsumedProperties(Dictionary<string, PropertyStaticData> staticDatas)
        {
            foreach (var propertyName in PendingConsumedProperties)
            {
                if (staticDatas.TryGetValue(propertyName, out var staticData))
                    staticData.MarkConsumed();
            }

            PendingConsumedProperties.Clear();
        }

        private static string BuildShaderAttributeFingerprint(Shader shader)
        {
            if (shader == null)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                builder.Append(shader.GetPropertyName(i));
                builder.Append('|');
                var attributes = shader.GetPropertyAttributes(i);
                if (attributes != null)
                    builder.Append(string.Join(",", attributes));
                builder.Append(';');
            }

            return builder.ToString();
        }

        private static bool HasShaderAttribute(Shader shader, string propertyName, string attributeName)
        {
            if (shader == null || string.IsNullOrEmpty(propertyName))
                return false;

            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyName(i) != propertyName)
                    continue;

                var attributes = shader.GetPropertyAttributes(i);
                if (attributes == null)
                    return false;

                foreach (var attribute in attributes)
                {
                    if (AttributeNameEquals(attribute, attributeName))
                        return true;
                }

                return false;
            }

            return false;
        }

        private static void ApplyShaderGUIAttributes(
            Shader shader,
            MaterialProperty property,
            MaterialProperty[] properties,
            PropertyStaticData staticData)
        {
            if (shader == null || property == null || staticData == null)
                return;

            foreach (var attribute in GetShaderAttributes(shader, property.name))
            {
                var name = GetAttributeName(attribute);
                var args = GetAttributeArguments(attribute);
                switch (name)
                {
                    case "Main":
                        ApplyMainAttribute(property, staticData, args);
                        break;
                    case "Sub":
                        ApplySubAttribute(staticData, args);
                        break;
                    case "Tex":
                        ApplyTexAttribute(properties, property, staticData, args);
                        break;
                    case "ShowIf":
                        ApplyShowIfAttribute(staticData, args);
                        break;
                    case "MatDataTransfer":
                        staticData.MarkMatDataTransferWritable();
                        staticData.HasShaderGUIDrawer = true;
                        break;
                }
            }
        }

        private static IEnumerable<string> GetShaderAttributes(Shader shader, string propertyName)
        {
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyName(i) != propertyName)
                    continue;

                return shader.GetPropertyAttributes(i) ?? System.Array.Empty<string>();
            }

            return System.Array.Empty<string>();
        }

        private static void ApplyMainAttribute(MaterialProperty property, PropertyStaticData staticData, string[] args)
        {
            var layerName = args.Length > 0 ? MainDrawer.NormalizeLayerName(args[0]) : string.Empty;
            var parentGroupName = args.Length > 1 ? MainDrawer.NormalizeLayerName(args[1]) : string.Empty;

            staticData.IsMain = true;
            staticData.LayerName = layerName;
            staticData.ParentGroupName = parentGroupName;
            staticData.GroupName = FormatGroupName(string.IsNullOrEmpty(layerName) ? property.displayName : layerName);
            staticData.IsExpanded = true;
            staticData.IsLayoutMarker = property.name.StartsWith("_QGUI_");
            staticData.StyleName = ShaderGUIStyleRegistry.DefaultGroupStyleName;
            staticData.HasShaderGUIDrawer = true;
        }

        private static void ApplySubAttribute(PropertyStaticData staticData, string[] args)
        {
            var layerName = args.Length > 1 ? MainDrawer.NormalizeLayerName(args[0]) : string.Empty;
            var parentGroupName = args.Length > 1
                ? MainDrawer.NormalizeLayerName(args[1])
                : args.Length > 0 ? MainDrawer.NormalizeLayerName(args[0]) : string.Empty;

            staticData.IsSub = true;
            staticData.LayerName = layerName;
            staticData.ParentGroupName = parentGroupName;
            if (!string.IsNullOrEmpty(layerName))
                staticData.GroupName = FormatGroupName(layerName);
            staticData.HasShaderGUIDrawer = true;
        }

        private static void ApplyTexAttribute(
            MaterialProperty[] properties,
            MaterialProperty property,
            PropertyStaticData staticData,
            string[] args)
        {
            var groupName = args.Length > 0 ? args[0] : string.Empty;

            staticData.ParentGroupName = MainDrawer.NormalizeLayerName(groupName);
            staticData.StyleName = ShaderGUIStyleRegistry.TextureBoxStyleName;
            staticData.HasShaderGUIDrawer = true;

            if (args.Length == 2)
            {
                MarkConsumedProperty(properties, property.name, args[1], ShaderPropertyType.Color, ShaderPropertyType.Vector);
                MarkConsumedProperty(properties, property.name, args[1], ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
                return;
            }

            if (args.Length >= 3)
            {
                MarkConsumedProperty(properties, property.name, args[1], ShaderPropertyType.Color);
                MarkConsumedProperty(properties, property.name, args[2], ShaderPropertyType.Vector);
            }

            if (args.Length >= 4)
                MarkConsumedProperty(properties, property.name, args[3], ShaderPropertyType.Float, ShaderPropertyType.Range, ShaderPropertyType.Int);
        }

        private static void ApplyShowIfAttribute(PropertyStaticData staticData, string[] args)
        {
            if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
                return;

            var compareOperator = args.Length > 2 ? args[1] : "==";
            var rawValue = args.Length > 2 ? args[2] : args.Length > 1 ? args[1] : "1";
            if (!float.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                value = 1f;

            staticData.ShowIfConditions.Add(new ShowIfCondition(
                args[0],
                ShaderGUIUtility.ParseCompareFunction(compareOperator),
                value));
            staticData.HasShaderGUIDrawer = true;
        }

        private static void MarkConsumedProperty(
            MaterialProperty[] properties,
            string ownerPropertyName,
            string propertyName,
            params ShaderPropertyType[] allowedTypes)
        {
            if (string.IsNullOrEmpty(propertyName) || propertyName == "_" || propertyName == ownerPropertyName)
                return;

            foreach (var property in properties)
            {
                if (property.name != propertyName)
                    continue;

                var propertyType = ShaderGUIUtility.GetPropertyType(property);
                foreach (var allowedType in allowedTypes)
                {
                    if (propertyType != allowedType)
                        continue;

                    PendingConsumedProperties.Add(propertyName);
                    return;
                }
            }
        }

        private static string GetAttributeName(string attribute)
        {
            if (string.IsNullOrEmpty(attribute))
                return string.Empty;

            var openIndex = attribute.IndexOf('(');
            var name = openIndex >= 0 ? attribute.Substring(0, openIndex) : attribute;
            return name.Trim();
        }

        private static string[] GetAttributeArguments(string attribute)
        {
            if (string.IsNullOrEmpty(attribute))
                return System.Array.Empty<string>();

            var openIndex = attribute.IndexOf('(');
            var closeIndex = attribute.LastIndexOf(')');
            if (openIndex < 0 || closeIndex <= openIndex)
                return System.Array.Empty<string>();

            var content = attribute.Substring(openIndex + 1, closeIndex - openIndex - 1);
            if (string.IsNullOrWhiteSpace(content))
                return System.Array.Empty<string>();

            var rawArgs = content.Split(',');
            var args = new string[rawArgs.Length];
            for (var i = 0; i < rawArgs.Length; i++)
                args[i] = rawArgs[i].Trim();

            return args;
        }

        private static string FormatGroupName(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('_', ' ').Trim();
        }

        private static bool AttributeNameEquals(string attribute, string name)
        {
            if (string.IsNullOrEmpty(attribute))
                return false;

            var trimmed = attribute.Trim();
            return trimmed == name
                   || trimmed.StartsWith(name + "(", System.StringComparison.Ordinal)
                   && trimmed.EndsWith(")", System.StringComparison.Ordinal);
        }

        private static void BuildPropertyHierarchy(MaterialProperty[] properties, Dictionary<string, PropertyStaticData> staticDatas)
        {
            var groups = new Dictionary<string, PropertyStaticData>();
            foreach (var property in properties)
            {
                if (!staticDatas.TryGetValue(property.name, out var staticData))
                    continue;

                if (!IsGroup(staticData))
                    continue;

                var layerName = GetEffectiveLayerName(staticData);
                if (!string.IsNullOrEmpty(layerName) && !groups.ContainsKey(layerName))
                    groups.Add(layerName, staticData);
            }

            PropertyStaticData currentMain = null;
            foreach (var property in properties)
            {
                if (!staticDatas.TryGetValue(property.name, out var staticData))
                    continue;

                if (IsGroup(staticData))
                {
                    currentMain = staticData;
                    if (TryAttachToNamedParent(staticData, groups))
                        continue;
                    continue;
                }

                if (TryAttachToNamedParent(staticData, groups))
                    continue;

                if (!staticData.IsSub || currentMain == null)
                    continue;

                AttachChild(currentMain, staticData);
            }
        }

        private static bool TryAttachToNamedParent(PropertyStaticData staticData, Dictionary<string, PropertyStaticData> groups)
        {
            if (staticData == null || string.IsNullOrEmpty(staticData.ParentGroupName))
                return false;

            if (!groups.TryGetValue(staticData.ParentGroupName, out var parent))
                return false;

            AttachChild(parent, staticData);
            return true;
        }

        private static void AttachChild(PropertyStaticData parent, PropertyStaticData child)
        {
            if (parent == null || child == null || parent == child || child.Parent != null)
                return;

            child.Parent = parent;
            parent.Children.Add(child);
        }

        private static bool IsGroup(PropertyStaticData staticData)
        {
            return staticData != null && (staticData.IsMain || !string.IsNullOrEmpty(staticData.LayerName));
        }

        private static string GetEffectiveLayerName(PropertyStaticData staticData)
        {
            if (staticData == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(staticData.LayerName))
                return staticData.LayerName;

            return staticData.Name;
        }

        /// <summary>
        /// 构建 Material 动态数据：根据当前 Material 状态计算属性可见性和激活状态。
        ///
        /// 执行流程：
        /// 1. 遍历所有属性（MaterialProperty[]）
        /// 2. 为每个属性创建 PropertyDynamicData（属性引用、可见性、激活状态、Tooltip）
        /// 3. 调用 ShaderGUIUtility.EvaluateVisibility() 计算可见性
        ///    - 检查 HideInInspector 标记
        ///    - 检查 ShowIf 条件（待实现，当前只检查 HideInInspector）
        ///    - 检查 Keyword 状态（待实现）
        /// 4. 返回 PerMaterialData（包含所有属性的动态数据字典）
        ///
        /// 性能特征：
        /// - 每帧重建（但成本可控，主要是简单的字典操作和条件判断）
        /// - Material 数据重建是为了让可见性计算基于最新的属性值
        ///
        /// 待优化：
        /// - 只在属性值改变时重建，而不是每帧都重建
        /// - 分离不需要每帧更新的数据（如 Tooltip）
        ///
        /// Tooltip 说明：
        /// - 格式："Property Name: {属性名}"
        /// - 方便在 Inspector 中悬停查看真实的 Shader 属性名（用于调试材质）
        /// </summary>
        private static PerMaterialData BuildPerMaterialData(Material material, MaterialProperty[] properties, PerShaderData perShaderData)
        {
            var dynamicDatas = new Dictionary<string, PropertyDynamicData>();
            foreach (var property in properties)
            {
                perShaderData.Properties.TryGetValue(property.name, out var staticData);
                // Tooltip intentionally exposes the raw shader property name for debugging materials.
                var tooltip = staticData == null ? property.name : $"Property Name: {staticData.Name}";
                var dynamicData = new PropertyDynamicData(property, true, true, tooltip);

                if (staticData != null)
                    ShaderGUIUtility.EvaluateVisibility(staticData, dynamicData, material);

                dynamicDatas[property.name] = dynamicData;
            }

            return new PerMaterialData(material, dynamicDatas);
        }
    }
}
