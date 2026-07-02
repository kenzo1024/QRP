using UnityEditor;
using UnityEngine;

namespace Rendering.MatDataTransfer.Editor
{
    internal static class ReadOnlyInspectorDrawer
    {
        public static void DrawReadOnly(SerializedObject serializedObject, string title)
        {
            if (serializedObject == null)
                return;

            serializedObject.Update();
            EditorGUILayout.HelpBox(
                title + " is generated and controlled by MatDataTransfer editor code. Direct asset editing is disabled by default.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
        }
    }
}
