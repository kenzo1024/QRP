using Rendering.MatDataTransfer.Runtime.GpuBuffer;
using UnityEditor;

namespace Rendering.MatDataTransfer.Editor.GpuBuffer
{
    [InitializeOnLoad]
    internal static class GpuBufferEditorFrameClock
    {
        private static int s_FrameIndex;

        static GpuBufferEditorFrameClock()
        {
            GpuBufferRuntime.SetEditorFrameIndex(s_FrameIndex);
            EditorApplication.update -= AdvanceFrame;
            EditorApplication.update += AdvanceFrame;
        }

        private static void AdvanceFrame()
        {
            unchecked
            {
                s_FrameIndex++;
            }

            GpuBufferRuntime.SetEditorFrameIndex(s_FrameIndex);
        }
    }
}
