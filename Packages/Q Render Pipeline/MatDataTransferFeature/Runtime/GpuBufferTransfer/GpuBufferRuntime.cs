using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public static class GpuBufferRuntime
    {
        private sealed class PassState
        {
            internal IGpuBufferPass Pass;
            internal int LastExecutedFrame = -1;
        }

        private static readonly List<PassState> s_Passes = new List<PassState>();

        private static object s_ExternalDriver;
        private static bool s_Subscribed;
#if UNITY_EDITOR
        private static int s_EditorFrameIndex;
        private static bool s_HasEditorFrameIndex;
#endif

        public static bool IsExternallyDriven => s_ExternalDriver != null;
        public static int PassCount => s_Passes.Count;

        public static void Register(IGpuBufferPass pass)
        {
            if (pass == null)
                throw new ArgumentNullException(nameof(pass));
            if (FindState(pass) != null)
                return;

            s_Passes.Add(new PassState { Pass = pass });
            UpdateSubscription();
        }

        public static void Unregister(IGpuBufferPass pass)
        {
            if (pass == null)
                return;

            for (int i = s_Passes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(s_Passes[i].Pass, pass))
                    s_Passes.RemoveAt(i);
            }

            UpdateSubscription();
        }

        public static bool TryAcquireExternalDriver(object owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (s_ExternalDriver != null && !ReferenceEquals(s_ExternalDriver, owner))
                return false;

            s_ExternalDriver = owner;
            UpdateSubscription();
            return true;
        }

        public static void ReleaseExternalDriver(object owner)
        {
            if (!ReferenceEquals(s_ExternalDriver, owner))
                return;

            s_ExternalDriver = null;
            UpdateSubscription();
        }

        public static void Record(CommandBuffer commandBuffer, int frameIndex)
        {
            if (commandBuffer == null)
                throw new ArgumentNullException(nameof(commandBuffer));

            RendererGpuBufferIndexRegistry.CollectGarbage();
            for (int i = 0; i < s_Passes.Count; i++)
            {
                PassState state = s_Passes[i];
                IGpuBufferPass pass = state.Pass;
                if (pass == null || !pass.HasWork)
                    continue;

                if (state.LastExecutedFrame != frameIndex)
                {
                    pass.PrepareFrame(frameIndex);
                    if (pass.HasWork)
                        pass.Execute(commandBuffer);
                    state.LastExecutedFrame = frameIndex;
                }

                if (pass.HasWork)
                    pass.Bind(commandBuffer);
            }
        }

#if UNITY_EDITOR
        internal static void SetEditorFrameIndex(int frameIndex)
        {
            s_EditorFrameIndex = frameIndex;
            s_HasEditorFrameIndex = true;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unsubscribe();
            s_Passes.Clear();
            s_ExternalDriver = null;
#if UNITY_EDITOR
            s_EditorFrameIndex = 0;
            s_HasEditorFrameIndex = false;
#endif
        }

        private static void OnBeginContextRendering(
            ScriptableRenderContext context,
            List<Camera> cameras)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !HasEligibleEditorCamera(cameras))
                return;
#endif
            if (!HasActivePass())
                return;

            var commandBuffer = new CommandBuffer { name = "GPU Buffer Runtime" };
            Record(commandBuffer, GetAutoFrameIndex());
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();
        }

        private static int GetAutoFrameIndex()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && s_HasEditorFrameIndex)
                return s_EditorFrameIndex;
#endif
            return Time.frameCount;
        }

#if UNITY_EDITOR
        private static bool HasEligibleEditorCamera(List<Camera> cameras)
        {
            if (cameras == null)
                return false;

            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                if (camera.cameraType == CameraType.Game
                    || camera.cameraType == CameraType.SceneView)
                {
                    return true;
                }
            }

            return false;
        }
#endif

        private static PassState FindState(IGpuBufferPass pass)
        {
            for (int i = 0; i < s_Passes.Count; i++)
            {
                if (ReferenceEquals(s_Passes[i].Pass, pass))
                    return s_Passes[i];
            }

            return null;
        }

        private static bool HasActivePass()
        {
            for (int i = 0; i < s_Passes.Count; i++)
            {
                if (s_Passes[i].Pass != null && s_Passes[i].Pass.HasWork)
                    return true;
            }

            return false;
        }

        private static void UpdateSubscription()
        {
            bool shouldSubscribe = s_ExternalDriver == null && s_Passes.Count > 0;
            if (shouldSubscribe && !s_Subscribed)
            {
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                s_Subscribed = true;
            }
            else if (!shouldSubscribe)
            {
                Unsubscribe();
            }
        }

        private static void Unsubscribe()
        {
            if (!s_Subscribed)
                return;

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            s_Subscribed = false;
        }
    }
}
