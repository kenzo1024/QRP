using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public sealed class ShaderGUILayoutNodeState
    {
        public bool IsVisible { get; set; }
        public bool IsExpanded { get; set; }
        public float Height { get; set; }
        public Rect Rect { get; set; }
    }

    public sealed class ShaderGUILayoutRuntime
    {
        private readonly Dictionary<string, ShaderGUILayoutNodeState> _states = new();

        public ShaderGUILayoutNodeState GetState(ShaderGUILayoutNode node)
        {
            if (!_states.TryGetValue(node.Id, out var state))
            {
                state = new ShaderGUILayoutNodeState
                {
                    IsVisible = true,
                    IsExpanded = node is not ShaderGUIGroupNode groupNode || groupNode.DefaultExpanded
                };
                _states.Add(node.Id, state);
            }

            return state;
        }
    }

    public static class ShaderGUILayoutEvaluator
    {
        public static void Evaluate(ShaderGUIMetaData metaData)
        {
            if (metaData?.PerShaderData.LayoutRoot == null)
                return;

            EvaluateNode(metaData.PerShaderData.LayoutRoot, metaData);
        }

        private static bool EvaluateNode(ShaderGUILayoutNode node, ShaderGUIMetaData metaData)
        {
            var state = metaData.PerInspectorData.LayoutRuntime.GetState(node);

            switch (node)
            {
                case ShaderGUIPropertyNode propertyNode:
                    state.IsVisible = ShouldDrawProperty(propertyNode, metaData);
                    state.Height = state.IsVisible ? GetPropertyHeight(propertyNode, metaData) : 0f;
                    return state.IsVisible;
                case ShaderGUIGroupNode groupNode:
                    var hasVisibleChild = EvaluateChildren(node, metaData);
                    state.IsVisible = ShouldDrawGroupHeader(groupNode, metaData) || hasVisibleChild;
                    state.Height = state.IsVisible ? GetGroupHeight(groupNode, metaData, state) : 0f;
                    return state.IsVisible;
                case ShaderGUISplitNode:
                case ShaderGUIRootNode:
                    state.IsVisible = EvaluateChildren(node, metaData);
                    state.Height = state.IsVisible ? GetStackHeight(node, metaData) : 0f;
                    return state.IsVisible;
                default:
                    state.IsVisible = false;
                    state.Height = 0f;
                    return false;
            }
        }

        private static bool EvaluateChildren(ShaderGUILayoutNode node, ShaderGUIMetaData metaData)
        {
            var hasVisibleChild = false;
            foreach (var child in node.Children)
                hasVisibleChild |= EvaluateNode(child, metaData);

            return hasVisibleChild;
        }

        private static bool ShouldDrawProperty(ShaderGUIPropertyNode node, ShaderGUIMetaData metaData)
        {
            return metaData.PerShaderData.Properties.TryGetValue(node.PropertyName, out var staticData)
                   && metaData.PerMaterialData.Properties.TryGetValue(node.PropertyName, out var dynamicData)
                   && ShaderGUIUtility.ShouldDrawProperty(
                       staticData,
                       dynamicData,
                       metaData,
                       metaData.PerInspectorData.DisplayModeData.DrawExternalDrawerProperties);
        }

        private static bool ShouldDrawGroupHeader(ShaderGUIGroupNode node, ShaderGUIMetaData metaData)
        {
            if (string.IsNullOrEmpty(node.HeaderPropertyName))
                return true;

            return metaData.PerShaderData.Properties.TryGetValue(node.HeaderPropertyName, out var staticData)
                   && metaData.PerMaterialData.Properties.TryGetValue(node.HeaderPropertyName, out var dynamicData)
                   && !staticData.IsHidden
                   && dynamicData.IsVisible;
        }

        private static float GetPropertyHeight(ShaderGUIPropertyNode node, ShaderGUIMetaData metaData)
        {
            if (!metaData.PerMaterialData.Properties.TryGetValue(node.PropertyName, out var dynamicData)
                || dynamicData.Property == null)
            {
                return 0f;
            }

            var drawer = ShaderGUIReflectionUtility.GetDrawablePropertyDrawer(
                metaData.PerShaderData.Shader,
                dynamicData.Property,
                metaData.PerInspectorData.MaterialEditor);

            if (drawer != null)
                return Mathf.Max(0f, drawer.GetPropertyHeight(dynamicData.Property, dynamicData.Property.displayName, metaData.PerInspectorData.MaterialEditor));

            var propertyDrawer = ShaderGUIReflectionUtility.GetPropertyDrawer(
                metaData.PerShaderData.Shader,
                dynamicData.Property,
                out _);
            if (propertyDrawer != null && !(propertyDrawer is IShaderGUIDrawer))
                return Mathf.Max(0f, propertyDrawer.GetPropertyHeight(dynamicData.Property, dynamicData.Property.displayName, metaData.PerInspectorData.MaterialEditor));

            return EditorGUIUtility.singleLineHeight;
        }

        private static float GetGroupHeight(ShaderGUIGroupNode node, ShaderGUIMetaData metaData, ShaderGUILayoutNodeState state)
        {
            var style = ShaderGUIStyleRegistry.GetBoxStyle(node.StyleName);
            var height = style.Padding + style.HeaderHeight + style.Padding;
            if (!state.IsExpanded)
                return height;

            var childHeight = GetStackHeight(node, metaData);
            if (childHeight <= 0f)
                return height;

            return height + style.Spacing + 1f + style.Padding + childHeight + style.Padding;
        }

        private static float GetStackHeight(ShaderGUILayoutNode node, ShaderGUIMetaData metaData)
        {
            var height = 0f;
            var visibleCount = 0;
            foreach (var child in node.Children)
            {
                var childState = metaData.PerInspectorData.LayoutRuntime.GetState(child);
                if (!childState.IsVisible)
                    continue;

                if (visibleCount > 0)
                    height += EditorGUIUtility.standardVerticalSpacing;

                height += childState.Height;
                visibleCount++;
            }

            return height;
        }
    }
}
