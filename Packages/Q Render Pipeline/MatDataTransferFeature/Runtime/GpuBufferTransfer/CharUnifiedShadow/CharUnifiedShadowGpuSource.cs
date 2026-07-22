using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer.CharUnifiedShadow
{
    public enum CharUnifiedShadowSamplingMode
    {
        BoxProjectionHalton,
        BoneAnchored,
    }

    public enum CharUnifiedShadowAnchorRegion
    {
        Head,
        LeftHand,
        RightHand,
        Pelvis,
        LeftFoot,
        RightFoot,
    }

    [Serializable]
    public sealed class CharUnifiedShadowAnchorMapping
    {
        public CharUnifiedShadowAnchorRegion Region;
        public Transform Anchor;
        public float LightBias;
        [Range(0f, 2f)] public float Weight = 1f;
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class CharUnifiedShadowGpuSource : MonoBehaviour,
        IGpuBufferDataSource<CharUnifiedShadowGpuInput>
    {
        public const int MaxSampleCount = 6;

        private const int BoundsRefreshInterval = 3;
        private const uint UpdateFlag = 1u;
        private const uint ResetFlag = 2u;

        [SerializeField] private bool useUnifiedShadow = true;
        [SerializeField] private CharUnifiedShadowSamplingMode samplingMode =
            CharUnifiedShadowSamplingMode.BoxProjectionHalton;
        [SerializeField, Range(0f, 0.5f)] private float sampleSmoothTime = 0.12f;
        [SerializeField, Range(0f, 0.5f)] private float boxLightBias = 0.1f;
        [SerializeField, Range(0.3f, 1.2f)] private float boxExtentScale = 0.85f;
        [SerializeField, Range(0f, 20f)] private float teleportResetDistance = 5f;
        [SerializeField, Range(1, 4)] private int updateInterval = 1;
        [SerializeField] private Light mainLightOverride;
        [SerializeField] private List<CharUnifiedShadowAnchorMapping> anchorMappings =
            new List<CharUnifiedShadowAnchorMapping>();
        [SerializeField] private List<Renderer> renderers = new List<Renderer>();
        [SerializeField] private bool drawDebugBounds;
#if UNITY_EDITOR
        [SerializeField] private bool drawDebugSamples = true;
        [SerializeField, Min(0.001f)] private float debugGizmoRadius = 0.1f;
        [SerializeField, Min(0.001f)] private float debugGizmoSize = 0.03f;
#endif

        private Bounds m_CachedBounds;
        private Vector3 m_CachedBoundsAnchor;
        private bool m_CachedBoundsValid;
        private int m_BoundsRefreshCounter;
        private int m_FrameCounter;
        private int m_LastSampleCount = -1;
        private CharUnifiedShadowSamplingMode m_LastSamplingMode;
        private bool m_LastUsedBoxProjection;
        private bool m_ResetRequested = true;

#if UNITY_EDITOR
        private static readonly Vector2[] s_DebugHaltonSamples =
        {
            new Vector2(0.5f, 1f / 3f),
            new Vector2(0.25f, 2f / 3f),
            new Vector2(0.75f, 1f / 9f),
            new Vector2(0.125f, 4f / 9f),
            new Vector2(0.625f, 7f / 9f),
            new Vector2(0.375f, 2f / 9f),
        };

        private readonly Vector4[] m_DebugCpuSamples = new Vector4[MaxSampleCount];
        private readonly Vector4[] m_DebugGpuSamples = new Vector4[MaxSampleCount];
        private readonly Vector3[] m_DebugPreviousCpuPositions = new Vector3[MaxSampleCount];
        private readonly bool[] m_DebugPreviousCpuValid = new bool[MaxSampleCount];
        private readonly Vector3[] m_DebugProjectionCorners = new Vector3[4];
        private int m_DebugSampleCount;
        private int m_DebugGpuSampleCount;
        private int m_DebugGpuFrame = -1;
        private bool m_DebugProjectionValid;
#endif

        internal IReadOnlyList<Renderer> Renderers => renderers;
#if UNITY_EDITOR
        internal bool WantsGpuDebugReadback => drawDebugSamples && isActiveAndEnabled;
        internal int DebugSampleCount => useUnifiedShadow ? m_DebugSampleCount : 0;
#endif

        private void OnEnable()
        {
            SetupRenderers();
            RequestGpuReset();
            m_LastSamplingMode = samplingMode;
            CharUnifiedShadowGpuPass.Register(this);
        }

        private void OnDisable()
        {
            CharUnifiedShadowGpuPass.Unregister(this);
        }

        private void OnDestroy()
        {
            CharUnifiedShadowGpuPass.Unregister(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            updateInterval = Mathf.Clamp(updateInterval, 1, 4);
            m_CachedBoundsValid = false;
            RequestGpuReset();
            if (isActiveAndEnabled)
                SetupRenderers();
        }

        private void OnDrawGizmosSelected()
        {
            if (drawDebugBounds && TryGetCombinedRendererWorldBounds(out Bounds bounds))
            {
                Gizmos.color = new Color(0.35f, 0.9f, 0.4f, 0.9f);
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            if (!drawDebugSamples)
                return;

            DrawDebugProjection();
            DrawDebugSamples();
        }
#endif

        [ContextMenu("Setup GPU Shadow Source")]
        public void SetupRenderers()
        {
            EnsureDefaultMappings();
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
            m_CachedBoundsValid = false;
            CharUnifiedShadowGpuPass.RefreshRendererBindings(this);
        }

        public void RequestGpuReset()
        {
            m_ResetRequested = true;
        }

        public bool TryWriteGpuBufferData(GpuBufferWriteContext<CharUnifiedShadowGpuInput> context)
        {
            if (context.Count < 1)
                return false;

            Vector3 lightDirection = GetMainLightDirection();
            Bounds bounds = default;
            bool useBoxProjection = samplingMode == CharUnifiedShadowSamplingMode.BoxProjectionHalton
                && TryGetCombinedRendererWorldBounds(out bounds);

            var input = new CharUnifiedShadowGpuInput();
            int sampleCount = useBoxProjection
                ? MaxSampleCount
                : WriteAnchorInputs(ref input);

            bool layoutChanged = samplingMode != m_LastSamplingMode
                || useBoxProjection != m_LastUsedBoxProjection
                || sampleCount != m_LastSampleCount;
            bool reset = m_ResetRequested || layoutChanged;
            bool shouldUpdate = ShouldUpdate(reset);
            uint flags = shouldUpdate ? UpdateFlag : 0u;
            if (reset)
                flags |= ResetFlag;

            input.BoundsCenterAndMode = useBoxProjection
                ? new Vector4(bounds.center.x, bounds.center.y, bounds.center.z, 1f)
                : Vector4.zero;
            input.BoundsExtentsAndCount = useBoxProjection
                ? new Vector4(bounds.extents.x, bounds.extents.y, bounds.extents.z, sampleCount)
                : new Vector4(0f, 0f, 0f, sampleCount);
            input.LightDirectionAndDeltaTime = new Vector4(
                lightDirection.x, lightDirection.y, lightDirection.z, Time.deltaTime);
            input.Settings = new Vector4(
                boxLightBias, boxExtentScale, sampleSmoothTime, teleportResetDistance);
            input.Control = new Vector4(
                flags,
                useUnifiedShadow && sampleCount > 0 ? 1f : 0f,
                0f,
                0f);

            context.Set(0, input);
#if UNITY_EDITOR
            UpdateCpuDebugSamples(
                useBoxProjection,
                bounds,
                lightDirection,
                sampleCount,
                reset,
                shouldUpdate);
#endif
            if (shouldUpdate)
            {
                m_ResetRequested = false;
                m_LastSamplingMode = samplingMode;
                m_LastUsedBoxProjection = useBoxProjection;
                m_LastSampleCount = sampleCount;
            }

            return true;
        }

        internal void ForceGpuReset()
        {
            m_ResetRequested = true;
        }

#if UNITY_EDITOR
        internal void SetGpuDebugSamples(Vector4[] samples, int count, int frame)
        {
            if (samples == null || frame < m_DebugGpuFrame)
                return;

            m_DebugGpuSampleCount = Mathf.Min(count, MaxSampleCount);
            for (int i = 0; i < MaxSampleCount; i++)
                m_DebugGpuSamples[i] = i < m_DebugGpuSampleCount ? samples[i] : Vector4.zero;
            m_DebugGpuFrame = frame;
            SceneView.RepaintAll();
        }
#endif

        private bool ShouldUpdate(bool reset)
        {
            if (reset)
            {
                m_FrameCounter = 1;
                return true;
            }

            return updateInterval <= 1 || (m_FrameCounter++ % updateInterval) == 0;
        }

        private int WriteAnchorInputs(ref CharUnifiedShadowGpuInput input)
        {
            int count = 0;
            for (int i = 0; i < anchorMappings.Count && count < MaxSampleCount; i++)
            {
                CharUnifiedShadowAnchorMapping mapping = anchorMappings[i];
                if (mapping == null || mapping.Anchor == null)
                    continue;

                Vector3 position = mapping.Anchor.localToWorldMatrix.GetColumn(3);
                input.SetAnchor(
                    count,
                    new Vector4(position.x, position.y, position.z, mapping.Weight),
                    new Vector4(mapping.LightBias, 0f, 0f, 0f));
                count++;
            }

            return count;
        }

        private Vector3 GetMainLightDirection()
        {
            Light light = mainLightOverride != null ? mainLightOverride : RenderSettings.sun;
            return light != null ? -light.transform.forward : Vector3.up;
        }

        private bool TryGetCombinedRendererWorldBounds(out Bounds worldBounds)
        {
            bool refresh = !m_CachedBoundsValid
                || (m_BoundsRefreshCounter++ % BoundsRefreshInterval) == 0;
            if (!refresh)
            {
                Vector3 anchor = transform.position;
                worldBounds = m_CachedBounds;
                worldBounds.center += anchor - m_CachedBoundsAnchor;
                return true;
            }

            worldBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    worldBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderer.bounds);
                }
            }

            m_CachedBoundsValid = hasBounds;
            if (hasBounds)
            {
                m_CachedBounds = worldBounds;
                m_CachedBoundsAnchor = transform.position;
            }

            return hasBounds;
        }

        private void EnsureDefaultMappings()
        {
            anchorMappings ??= new List<CharUnifiedShadowAnchorMapping>();
            renderers ??= new List<Renderer>();
            foreach (CharUnifiedShadowAnchorRegion region in
                     Enum.GetValues(typeof(CharUnifiedShadowAnchorRegion)))
            {
                CharUnifiedShadowAnchorMapping mapping = anchorMappings.Find(
                    item => item != null && item.Region == region);
                if (mapping != null && mapping.Anchor != null)
                    continue;

                Transform anchor = FindChildByName(transform, GetDefaultAnchorName(region));
                if (mapping == null)
                {
                    anchorMappings.Add(new CharUnifiedShadowAnchorMapping
                    {
                        Region = region,
                        Anchor = anchor,
                        LightBias = GetDefaultLightBias(region),
                        Weight = 1f,
                    });
                }
                else
                {
                    mapping.Anchor = anchor;
                    if (Mathf.Abs(mapping.LightBias) <= 1e-6f)
                        mapping.LightBias = GetDefaultLightBias(region);
                }
            }
        }

        private static string GetDefaultAnchorName(CharUnifiedShadowAnchorRegion region)
        {
            switch (region)
            {
                case CharUnifiedShadowAnchorRegion.Head: return "Bip001_Head";
                case CharUnifiedShadowAnchorRegion.LeftHand: return "Bip001_L_Hand";
                case CharUnifiedShadowAnchorRegion.RightHand: return "Bip001_R_Hand";
                case CharUnifiedShadowAnchorRegion.Pelvis: return "root";
                case CharUnifiedShadowAnchorRegion.LeftFoot: return "Bip001_L_Toe0";
                case CharUnifiedShadowAnchorRegion.RightFoot: return "Bip001_R_Toe0";
                default: return null;
            }
        }

        private static float GetDefaultLightBias(CharUnifiedShadowAnchorRegion region)
        {
            switch (region)
            {
                case CharUnifiedShadowAnchorRegion.Head: return 0.23f;
                case CharUnifiedShadowAnchorRegion.Pelvis: return 0.19f;
                case CharUnifiedShadowAnchorRegion.LeftHand:
                case CharUnifiedShadowAnchorRegion.RightHand:
                case CharUnifiedShadowAnchorRegion.LeftFoot:
                case CharUnifiedShadowAnchorRegion.RightFoot:
                    return 0.08f;
                default:
                    return 0f;
            }
        }

        private static Transform FindChildByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
                return null;

            var queue = new Queue<Transform>();
            for (int i = 0; i < root.childCount; i++)
                queue.Enqueue(root.GetChild(i));

            while (queue.Count > 0)
            {
                Transform child = queue.Dequeue();
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                    return child;
                for (int i = 0; i < child.childCount; i++)
                    queue.Enqueue(child.GetChild(i));
            }

            return null;
        }

#if UNITY_EDITOR
        private void UpdateCpuDebugSamples(
            bool useBoxProjection,
            Bounds bounds,
            Vector3 lightDirection,
            int sampleCount,
            bool reset,
            bool shouldUpdate)
        {
            m_DebugSampleCount = useUnifiedShadow ? sampleCount : 0;
            if (!drawDebugSamples || !shouldUpdate)
                return;

            if (reset)
                Array.Clear(m_DebugPreviousCpuValid, 0, m_DebugPreviousCpuValid.Length);

            if (useBoxProjection)
                UpdateCpuBoxSamples(bounds, lightDirection);
            else
                UpdateCpuAnchorSamples(lightDirection, sampleCount);

            for (int i = sampleCount; i < MaxSampleCount; i++)
                m_DebugCpuSamples[i] = Vector4.zero;
        }

        private void UpdateCpuBoxSamples(Bounds bounds, Vector3 lightDirection)
        {
            BuildProjectionBasis(lightDirection, out Vector3 axisU, out Vector3 axisV);
            float extentU = ProjectExtent(bounds.extents, axisU) * Mathf.Max(0.01f, boxExtentScale);
            float extentV = ProjectExtent(bounds.extents, axisV) * Mathf.Max(0.01f, boxExtentScale);
            float halfDepth = ProjectExtent(bounds.extents, lightDirection);
            Vector3 boxAnchor = bounds.center + lightDirection * (halfDepth + boxLightBias);

            for (int i = 0; i < MaxSampleCount; i++)
            {
                Vector2 offset = s_DebugHaltonSamples[i] * 2f - Vector2.one;
                Vector3 target = boxAnchor
                    + axisU * (offset.x * extentU)
                    + axisV * (offset.y * extentV);
                Vector3 smoothed = SmoothCpuDebugPosition(i, target);
                m_DebugCpuSamples[i] = new Vector4(smoothed.x, smoothed.y, smoothed.z, 1f);
            }

            m_DebugProjectionCorners[0] = boxAnchor - axisU * extentU - axisV * extentV;
            m_DebugProjectionCorners[1] = boxAnchor + axisU * extentU - axisV * extentV;
            m_DebugProjectionCorners[2] = boxAnchor + axisU * extentU + axisV * extentV;
            m_DebugProjectionCorners[3] = boxAnchor - axisU * extentU + axisV * extentV;
            m_DebugProjectionValid = true;
        }

        private void UpdateCpuAnchorSamples(Vector3 lightDirection, int sampleCount)
        {
            int sampleIndex = 0;
            for (int i = 0; i < anchorMappings.Count && sampleIndex < sampleCount; i++)
            {
                CharUnifiedShadowAnchorMapping mapping = anchorMappings[i];
                if (mapping == null || mapping.Anchor == null)
                    continue;

                Vector3 anchorPosition = mapping.Anchor.localToWorldMatrix.GetColumn(3);
                Vector3 target = anchorPosition + lightDirection * mapping.LightBias;
                Vector3 smoothed = SmoothCpuDebugPosition(sampleIndex, target);
                m_DebugCpuSamples[sampleIndex] = new Vector4(
                    smoothed.x, smoothed.y, smoothed.z, mapping.Weight);
                sampleIndex++;
            }

            m_DebugProjectionValid = false;
        }

        private Vector3 SmoothCpuDebugPosition(int index, Vector3 target)
        {
            if (!m_DebugPreviousCpuValid[index])
            {
                m_DebugPreviousCpuValid[index] = true;
                m_DebugPreviousCpuPositions[index] = target;
                return target;
            }

            Vector3 previous = m_DebugPreviousCpuPositions[index];
            Vector3 delta = target - previous;
            if (teleportResetDistance > 0f
                && delta.sqrMagnitude > teleportResetDistance * teleportResetDistance)
            {
                m_DebugPreviousCpuPositions[index] = target;
                return target;
            }

            float factor = Time.deltaTime > 0f && sampleSmoothTime > 1e-4f
                ? 1f - Mathf.Exp(-Time.deltaTime / sampleSmoothTime)
                : 1f;
            Vector3 smoothed = Vector3.Lerp(previous, target, factor);
            m_DebugPreviousCpuPositions[index] = smoothed;
            return smoothed;
        }

        private void DrawDebugProjection()
        {
            if (!m_DebugProjectionValid)
                return;

            Handles.color = new Color(1f, 0.8f, 0.15f, 0.9f);
            for (int i = 0; i < m_DebugProjectionCorners.Length; i++)
                Handles.DrawLine(m_DebugProjectionCorners[i], m_DebugProjectionCorners[(i + 1) % 4]);
        }

        private void DrawDebugSamples()
        {
            int count = Mathf.Min(m_DebugSampleCount, MaxSampleCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 cpuPosition = m_DebugCpuSamples[i];
                Handles.color = new Color(1f, 0.75f, 0.15f, 0.95f);
                Handles.CubeHandleCap(0, cpuPosition, Quaternion.identity, debugGizmoSize, EventType.Repaint);

                if (i >= m_DebugGpuSampleCount)
                    continue;

                Vector3 gpuPosition = m_DebugGpuSamples[i];
                Handles.color = new Color(0.1f, 0.9f, 1f, 0.95f);
                Handles.SphereHandleCap(0, gpuPosition, Quaternion.identity, debugGizmoSize, EventType.Repaint);
                Handles.DrawWireDisc(gpuPosition, Vector3.up, debugGizmoRadius);

                Handles.color = Color.magenta;
                Handles.DrawLine(cpuPosition, gpuPosition);
            }
        }

        private static void BuildProjectionBasis(
            Vector3 lightDirection,
            out Vector3 axisU,
            out Vector3 axisV)
        {
            Vector3 referenceAxis = Mathf.Abs(Vector3.Dot(lightDirection, Vector3.up)) > 0.95f
                ? Vector3.right
                : Vector3.up;
            axisU = Vector3.Cross(lightDirection, referenceAxis).normalized;
            axisV = Vector3.Cross(lightDirection, axisU).normalized;
        }

        private static float ProjectExtent(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(extents.x * axis.x)
                + Mathf.Abs(extents.y * axis.y)
                + Mathf.Abs(extents.z * axis.z);
        }
#endif
    }
}
