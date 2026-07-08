using System.Collections.Generic;

namespace QRenderPipeline.Editor.ShaderGUI
{
    public enum ShaderGUILayoutNodeType
    {
        Root,
        Group,
        Property,
        Split
    }

    public abstract class ShaderGUILayoutNode
    {
        public string Id { get; }
        public string StyleName { get; set; }
        public ShaderGUILayoutNode Parent { get; private set; }
        public List<ShaderGUILayoutNode> Children { get; }
        public abstract ShaderGUILayoutNodeType NodeType { get; }

        protected ShaderGUILayoutNode(string id)
        {
            Id = id;
            StyleName = ShaderGUIStyleRegistry.DefaultGroupStyleName;
            Children = new List<ShaderGUILayoutNode>();
        }

        public void AddChild(ShaderGUILayoutNode child)
        {
            if (child == null)
                return;

            child.Parent = this;
            Children.Add(child);
        }
    }

    public sealed class ShaderGUIRootNode : ShaderGUILayoutNode
    {
        public override ShaderGUILayoutNodeType NodeType => ShaderGUILayoutNodeType.Root;

        public ShaderGUIRootNode()
            : base("root")
        {
        }
    }

    public sealed class ShaderGUIGroupNode : ShaderGUILayoutNode
    {
        public override ShaderGUILayoutNodeType NodeType => ShaderGUILayoutNodeType.Group;
        public string Title { get; }
        public string HeaderPropertyName { get; }
        public bool DefaultExpanded { get; }

        public ShaderGUIGroupNode(string id, string title, string headerPropertyName, bool defaultExpanded)
            : base(id)
        {
            Title = string.IsNullOrEmpty(title) ? id : title;
            HeaderPropertyName = headerPropertyName;
            DefaultExpanded = defaultExpanded;
        }
    }

    public sealed class ShaderGUIPropertyNode : ShaderGUILayoutNode
    {
        public override ShaderGUILayoutNodeType NodeType => ShaderGUILayoutNodeType.Property;
        public string PropertyName { get; }

        public ShaderGUIPropertyNode(string propertyName)
            : base($"property:{propertyName}")
        {
            PropertyName = propertyName;
        }
    }

    public sealed class ShaderGUISplitNode : ShaderGUILayoutNode
    {
        public override ShaderGUILayoutNodeType NodeType => ShaderGUILayoutNodeType.Split;
        public float LeftRatio { get; }

        public ShaderGUISplitNode(string id, float leftRatio)
            : base(id)
        {
            LeftRatio = leftRatio;
        }
    }
}
