using System.Collections.Generic;
using UnityEditor;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public static class ShaderGUILayoutBuilder
    {
        public static ShaderGUIRootNode Build(MaterialProperty[] properties, Dictionary<string, PropertyStaticData> staticDatas)
        {
            var root = new ShaderGUIRootNode();

            foreach (var property in properties)
            {
                if (!staticDatas.TryGetValue(property.name, out var staticData))
                    continue;

                if (staticData.Parent != null)
                    continue;

                root.AddChild(CreateLayoutNode(staticData));
            }

            return root;
        }

        private static ShaderGUILayoutNode CreateLayoutNode(PropertyStaticData staticData)
        {
            if (IsGroup(staticData))
                return CreateGroupTree(staticData);

            return CreatePropertyNode(staticData.Name, staticData);
        }

        private static ShaderGUIGroupNode CreateGroupTree(PropertyStaticData staticData)
        {
            var groupNode = CreateGroupNode(staticData);
            if (!staticData.IsLayoutMarker)
                groupNode.AddChild(CreatePropertyNode(staticData.Name, staticData));

            foreach (var child in staticData.Children)
            {
                groupNode.AddChild(CreateLayoutNode(child));
            }

            return groupNode;
        }

        private static bool IsGroup(PropertyStaticData staticData)
        {
            return staticData != null && (staticData.IsMain || !string.IsNullOrEmpty(staticData.LayerName));
        }

        private static ShaderGUIPropertyNode CreatePropertyNode(string propertyName, PropertyStaticData staticData)
        {
            return new ShaderGUIPropertyNode(propertyName)
            {
                StyleName = staticData.StyleName
            };
        }

        private static ShaderGUIGroupNode CreateGroupNode(PropertyStaticData staticData)
        {
            var title = string.IsNullOrEmpty(staticData.GroupName) ? staticData.DisplayName : staticData.GroupName;
            return new ShaderGUIGroupNode($"group:{staticData.Name}", title, staticData.Name, staticData.IsExpanded)
            {
                StyleName = staticData.StyleName
            };
        }
    }
}
