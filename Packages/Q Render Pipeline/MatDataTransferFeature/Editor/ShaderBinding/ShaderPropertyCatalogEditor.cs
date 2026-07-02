using Rendering.MatDataTransfer.Runtime;
using UnityEditor;

namespace Rendering.MatDataTransfer.Editor
{
    [CustomEditor(typeof(ShaderPropertyCatalog))]
    public sealed class ShaderPropertyCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ReadOnlyInspectorDrawer.DrawReadOnly(serializedObject, nameof(ShaderPropertyCatalog));
        }
    }
}
