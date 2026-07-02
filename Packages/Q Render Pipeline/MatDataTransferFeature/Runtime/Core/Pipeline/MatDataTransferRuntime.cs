using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rendering.MatDataTransfer.Runtime
{
    internal static class MatDataTransferRuntime
    {
        private static int s_EditModeRenderStep;

        internal static int FrameIndex
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return s_EditModeRenderStep;
#endif
                return Time.frameCount;
            }
        }

        internal static double TimeSinceStartup
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return EditorApplication.timeSinceStartup;
#endif
                return Time.realtimeSinceStartupAsDouble;
            }
        }

        internal static float TimeSeconds
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return (float)EditorApplication.timeSinceStartup;
#endif
                return Time.time;
            }
        }

        internal static int BeginRenderPass()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return unchecked(++s_EditModeRenderStep);
#endif
            return Time.frameCount;
        }

        internal static void RequestEditorUpdate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
#endif
        }
    }
}
