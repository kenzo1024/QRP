using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public static class RendererGpuBufferIndexRegistry
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private sealed class RendererState
        {
            internal GpuBufferHandle Handle;
            internal float OriginalIndexValue;
            internal float AppliedIndexValue;
            internal readonly HashSet<object> Owners =
                new HashSet<object>(ReferenceComparer.Instance);
        }

        private static readonly GpuBufferSlotAllocator Allocator = new GpuBufferSlotAllocator();
        private static readonly Dictionary<Renderer, RendererState> RendererStates =
            new Dictionary<Renderer, RendererState>();
        private static readonly Dictionary<object, HashSet<Renderer>> OwnerRenderers =
            new Dictionary<object, HashSet<Renderer>>(ReferenceComparer.Instance);
        private static readonly HashSet<Renderer> Targets = new HashSet<Renderer>();
        private static readonly List<Renderer> RenderersToRelease = new List<Renderer>();
        private static readonly List<object> OwnersToRelease = new List<object>();

        public static int Capacity => Allocator.Capacity;
        public static int RendererCount => RendererStates.Count;

        public static bool CanBind(Renderer renderer)
        {
            return renderer != null && renderer.realtimeLightmapIndex < 0;
        }

        public static void Sync(object owner, Renderer renderer)
        {
            Targets.Clear();
            if (CanBind(renderer))
                Targets.Add(renderer);
            SyncTargets(owner);
        }

        public static void Sync(object owner, IReadOnlyList<Renderer> renderers)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            Targets.Clear();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer renderer = renderers[i];
                    if (CanBind(renderer))
                        Targets.Add(renderer);
                }
            }

            SyncTargets(owner);
        }

        public static void Release(object owner)
        {
            if (owner == null || !OwnerRenderers.TryGetValue(owner, out HashSet<Renderer> renderers))
                return;

            RenderersToRelease.Clear();
            foreach (Renderer renderer in renderers)
                RenderersToRelease.Add(renderer);
            for (int i = 0; i < RenderersToRelease.Count; i++)
                RemoveOwner(RenderersToRelease[i], owner);

            OwnerRenderers.Remove(owner);
            RenderersToRelease.Clear();
        }

        public static bool TryGetIndex(Renderer renderer, out int index)
        {
            if (renderer != null && RendererStates.TryGetValue(renderer, out RendererState state))
            {
                index = state.Handle.Index;
                return true;
            }

            index = -1;
            return false;
        }

        public static bool TryGetSlice(
            Renderer renderer,
            int elementsPerRenderer,
            out GpuBufferSlice slice)
        {
            if (elementsPerRenderer <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementsPerRenderer));

            if (!TryGetIndex(renderer, out int index))
            {
                slice = default;
                return false;
            }

            slice = GpuBufferSlice.FromIndex(index, elementsPerRenderer);
            return true;
        }

        internal static void CollectGarbage()
        {
            OwnersToRelease.Clear();
            foreach (object owner in OwnerRenderers.Keys)
            {
                if (owner is UnityEngine.Object unityObject && unityObject == null)
                    OwnersToRelease.Add(owner);
            }

            for (int i = 0; i < OwnersToRelease.Count; i++)
                Release(OwnersToRelease[i]);

            RenderersToRelease.Clear();
            foreach (Renderer renderer in RendererStates.Keys)
            {
                if (renderer == null)
                    RenderersToRelease.Add(renderer);
            }

            for (int i = 0; i < RenderersToRelease.Count; i++)
                ReleaseDestroyedRenderer(RenderersToRelease[i]);
            RenderersToRelease.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReleaseAll();
        }

        private static void SyncTargets(object owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (!OwnerRenderers.TryGetValue(owner, out HashSet<Renderer> ownedRenderers))
            {
                ownedRenderers = new HashSet<Renderer>();
                OwnerRenderers.Add(owner, ownedRenderers);
            }

            RenderersToRelease.Clear();
            foreach (Renderer renderer in ownedRenderers)
            {
                if (renderer == null || !Targets.Contains(renderer))
                    RenderersToRelease.Add(renderer);
            }

            for (int i = 0; i < RenderersToRelease.Count; i++)
                RemoveOwner(RenderersToRelease[i], owner);

            ownedRenderers.RemoveWhere(renderer => renderer == null || !Targets.Contains(renderer));

            foreach (Renderer renderer in Targets)
            {
                AcquireOwner(renderer, owner);
                ownedRenderers.Add(renderer);
            }

            RenderersToRelease.Clear();
        }

        private static void AcquireOwner(Renderer renderer, object owner)
        {
            if (!RendererStates.TryGetValue(renderer, out RendererState state))
            {
                GpuBufferHandle handle = Allocator.Allocate();
                float original = renderer.realtimeLightmapScaleOffset.x;
                float applied = Encode(handle.Index);
                SetRendererIndex(renderer, applied);
                state = new RendererState
                {
                    Handle = handle,
                    OriginalIndexValue = original,
                    AppliedIndexValue = applied
                };
                RendererStates.Add(renderer, state);
            }
            else if (!Mathf.Approximately(
                         renderer.realtimeLightmapScaleOffset.x,
                         state.AppliedIndexValue))
            {
                SetRendererIndex(renderer, state.AppliedIndexValue);
            }

            state.Owners.Add(owner);
        }

        private static void RemoveOwner(Renderer renderer, object owner)
        {
            if (ReferenceEquals(renderer, null)
                || !RendererStates.TryGetValue(renderer, out RendererState state))
            {
                return;
            }

            state.Owners.Remove(owner);
            if (state.Owners.Count > 0)
                return;

            if (renderer != null
                && Mathf.Approximately(
                    renderer.realtimeLightmapScaleOffset.x,
                    state.AppliedIndexValue))
            {
                SetRendererIndex(renderer, state.OriginalIndexValue);
            }

            Allocator.Release(state.Handle);
            RendererStates.Remove(renderer);
        }

        private static void ReleaseDestroyedRenderer(Renderer renderer)
        {
            if (!RendererStates.TryGetValue(renderer, out RendererState state))
                return;

            foreach (object owner in state.Owners)
            {
                if (OwnerRenderers.TryGetValue(owner, out HashSet<Renderer> renderers))
                    renderers.Remove(renderer);
            }

            Allocator.Release(state.Handle);
            RendererStates.Remove(renderer);
        }

        private static void ReleaseAll()
        {
            RenderersToRelease.Clear();
            foreach (Renderer renderer in RendererStates.Keys)
                RenderersToRelease.Add(renderer);

            for (int i = 0; i < RenderersToRelease.Count; i++)
            {
                Renderer renderer = RenderersToRelease[i];
                if (renderer == null || !RendererStates.TryGetValue(renderer, out RendererState state))
                    continue;

                if (Mathf.Approximately(
                        renderer.realtimeLightmapScaleOffset.x,
                        state.AppliedIndexValue))
                {
                    SetRendererIndex(renderer, state.OriginalIndexValue);
                }
            }

            RendererStates.Clear();
            OwnerRenderers.Clear();
            Allocator.Clear();
            Targets.Clear();
            RenderersToRelease.Clear();
            OwnersToRelease.Clear();
        }

        private static float Encode(int index)
        {
            return -(index + 1f);
        }

        private static void SetRendererIndex(Renderer renderer, float index)
        {
            Vector4 scaleOffset = renderer.realtimeLightmapScaleOffset;
            scaleOffset.x = index;
            renderer.realtimeLightmapScaleOffset = scaleOffset;
        }
    }
}
