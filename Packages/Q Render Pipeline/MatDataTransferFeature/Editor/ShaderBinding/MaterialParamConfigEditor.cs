using Rendering.MatDataTransfer.Runtime;
using UnityEditor;

namespace Rendering.MatDataTransfer.Editor
{
    [CustomEditor(typeof(MaterialParamConfig))]
    public sealed class MaterialParamConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ReadOnlyInspectorDrawer.DrawReadOnly(serializedObject, nameof(MaterialParamConfig));
        }
    }
}
