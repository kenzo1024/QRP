using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer
{
    public sealed class RendererGpuBufferIndexBinder : IDisposable
    {
        private sealed class BindingState
        {
            internal object Owner;
            internal Vector4 OriginalValue;
            internal Vector4 AppliedValue;
        }

        private readonly Dictionary<Renderer, BindingState> m_Bindings =
            new Dictionary<Renderer, BindingState>();
        private readonly HashSet<Renderer> m_Targets = new HashSet<Renderer>();
        private readonly List<Renderer> m_ReleaseList = new List<Renderer>();

        public void Sync(object owner, int instanceIndex, IReadOnlyList<Renderer> renderers)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (instanceIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(instanceIndex));

            CollectTargets(renderers);
            CollectRemovedBindings(owner);
            ReleaseCollectedRenderers();

            foreach (Renderer renderer in m_Targets)
                Bind(owner, renderer, instanceIndex);
        }

        public void Release(object owner)
        {
            if (owner == null)
                return;

            m_ReleaseList.Clear();
            foreach (KeyValuePair<Renderer, BindingState> pair in m_Bindings)
            {
                if (ReferenceEquals(pair.Value.Owner, owner))
                    m_ReleaseList.Add(pair.Key);
            }
            ReleaseCollectedRenderers();
        }

        public void Dispose()
        {
            m_ReleaseList.Clear();
            foreach (Renderer renderer in m_Bindings.Keys)
                m_ReleaseList.Add(renderer);
            ReleaseCollectedRenderers();
            m_Targets.Clear();
        }

        public static bool TryDecode(float encodedValue, out int instanceIndex)
        {
            if (encodedValue > -0.5f)
            {
                instanceIndex = -1;
                return false;
            }

            instanceIndex = Mathf.RoundToInt(-encodedValue) - 1;
            return instanceIndex >= 0;
        }

        private void CollectTargets(IReadOnlyList<Renderer> renderers)
        {
            m_Targets.Clear();
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    m_Targets.Add(renderer);
            }
        }

        private void CollectRemovedBindings(object owner)
        {
            m_ReleaseList.Clear();
            foreach (KeyValuePair<Renderer, BindingState> pair in m_Bindings)
            {
                if (ReferenceEquals(pair.Value.Owner, owner)
                    && (pair.Key == null || !m_Targets.Contains(pair.Key)))
                {
                    m_ReleaseList.Add(pair.Key);
                }
            }
        }

        private void Bind(object owner, Renderer renderer, int instanceIndex)
        {
            if (renderer.realtimeLightmapIndex >= 0)
            {
                if (m_Bindings.TryGetValue(renderer, out BindingState realtimeGiState)
                    && ReferenceEquals(realtimeGiState.Owner, owner))
                {
                    ReleaseRenderer(renderer);
                }

                Debug.LogWarning(
                    $"[GpuBuffer] Renderer '{renderer.name}' uses Realtime GI; instance index binding was skipped.",
                    renderer);
                return;
            }

            if (m_Bindings.TryGetValue(renderer, out BindingState existing))
            {
                if (!ReferenceEquals(existing.Owner, owner))
                {
                    Debug.LogWarning(
                        $"[GpuBuffer] Renderer '{renderer.name}' is already owned by another index binding.",
                        renderer);
                    return;
                }

                Vector4 nextValue = Encode(existing.OriginalValue, instanceIndex);
                if (existing.AppliedValue != nextValue)
                {
                    renderer.realtimeLightmapScaleOffset = nextValue;
                    existing.AppliedValue = nextValue;
                }
                return;
            }

            Vector4 originalValue = renderer.realtimeLightmapScaleOffset;
            Vector4 appliedValue = Encode(originalValue, instanceIndex);
            renderer.realtimeLightmapScaleOffset = appliedValue;
            m_Bindings.Add(renderer, new BindingState
            {
                Owner = owner,
                OriginalValue = originalValue,
                AppliedValue = appliedValue,
            });
        }

        private void ReleaseCollectedRenderers()
        {
            for (int i = 0; i < m_ReleaseList.Count; i++)
                ReleaseRenderer(m_ReleaseList[i]);
            m_ReleaseList.Clear();
        }

        private void ReleaseRenderer(Renderer renderer)
        {
            if (ReferenceEquals(renderer, null) || !m_Bindings.TryGetValue(renderer, out BindingState state))
            {
                m_Bindings.Remove(renderer);
                return;
            }

            if (renderer != null && renderer.realtimeLightmapScaleOffset == state.AppliedValue)
                renderer.realtimeLightmapScaleOffset = state.OriginalValue;
            m_Bindings.Remove(renderer);
        }

        private static Vector4 Encode(Vector4 originalValue, int instanceIndex)
        {
            originalValue.x = -(instanceIndex + 1.0f);
            return originalValue;
        }
    }
}
