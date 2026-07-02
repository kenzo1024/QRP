using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferInstance
    {
#if UNITY_EDITOR
        private static readonly System.Collections.Generic.HashSet<int> s_PendingDestroyInstanceIds =
            new System.Collections.Generic.HashSet<int>();
#endif

        private bool ValidateInstanceHierarchyForRefresh()
        {
            MatDataTransferInstance topmostInstance = FindTopmostInstance();
            if (topmostInstance == null)
                return false;

            if (!ReferenceEquals(topmostInstance, this))
            {
                topmostInstance.RefreshRendererBindings();
                return false;
            }

            RemoveNestedInstances();
            return true;
        }

        private MatDataTransferInstance FindTopmostInstance()
        {
            MatDataTransferInstance topmostInstance = this;
            Transform current = transform.parent;
            while (current != null)
            {
                MatDataTransferInstance parentInstance = current.GetComponent<MatDataTransferInstance>();
                if (parentInstance != null)
                    topmostInstance = parentInstance;

                current = current.parent;
            }

            return topmostInstance;
        }

        private void RemoveNestedInstances()
        {
            MatDataTransferInstance[] childInstances = GetComponentsInChildren<MatDataTransferInstance>(true);
            for (int i = 0; i < childInstances.Length; i++)
            {
                MatDataTransferInstance childInstance = childInstances[i];
                if (childInstance == null || ReferenceEquals(childInstance, this))
                    continue;

                DestroyNestedInstance(childInstance);
            }
        }

        private static void DestroyNestedInstance(MatDataTransferInstance instance)
        {
            RemoveLiveInstance(instance);
            instance.SetRegisteredInstanceId(-1);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                int instanceId = instance.GetInstanceID();
                if (s_PendingDestroyInstanceIds.Add(instanceId))
                    EditorApplication.delayCall += () => DestroyNestedInstanceInEditor(instance, instanceId);

                return;
            }
#endif

            Destroy(instance);
        }

#if UNITY_EDITOR
        private static void DestroyNestedInstanceInEditor(MatDataTransferInstance instance, int instanceId)
        {
            s_PendingDestroyInstanceIds.Remove(instanceId);

            if (instance == null)
                return;

            Undo.DestroyObjectImmediate(instance);
            MatDataTransferRuntime.RequestEditorUpdate();
        }
#endif
    }
}
