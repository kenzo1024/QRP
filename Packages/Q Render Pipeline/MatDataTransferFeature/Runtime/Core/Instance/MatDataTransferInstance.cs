using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 渲染器绑定信息
    /// </summary>
    [System.Serializable]
    public sealed class RendererMaterialBinding
    {
        public Renderer Renderer;
        public int MaterialSlot;
        public string ShaderName;
        public string MaterialName;
        public int MaterialId;
        public string RendererPathId;
        public string MaterialTraceId;

        public int RendererId => Renderer != null ? Renderer.GetInstanceID() : 0;

        public RendererMaterialBinding()
        {
        }

        public RendererMaterialBinding(Renderer renderer, int materialSlot)
        {
            Renderer = renderer;
            MaterialSlot = materialSlot;
            UpdateShaderName();
        }

        public RendererMaterialBinding(Renderer renderer, int materialSlot, Transform instanceRoot)
        {
            Renderer = renderer;
            MaterialSlot = materialSlot;
            UpdateShaderName(instanceRoot);
        }

        public void UpdateShaderName()
        {
            UpdateShaderName(null);
        }

        public void UpdateShaderName(Transform instanceRoot)
        {
            if (Renderer == null || MaterialSlot < 0)
            {
                ShaderName = string.Empty;
                MaterialName = string.Empty;
                MaterialId = 0;
                RendererPathId = string.Empty;
                MaterialTraceId = string.Empty;
                return;
            }

            RendererPathId = BuildRendererPathId(Renderer, instanceRoot);
            Material[] materials = Renderer.sharedMaterials;
            if (materials != null && MaterialSlot < materials.Length)
            {
                Material mat = materials[MaterialSlot];
                ShaderName = mat != null && mat.shader != null ? mat.shader.name : string.Empty;
                MaterialName = mat != null ? mat.name : string.Empty;
                MaterialId = mat != null ? mat.GetInstanceID() : 0;
            }
            else
            {
                ShaderName = string.Empty;
                MaterialName = string.Empty;
                MaterialId = 0;
            }

            MaterialTraceId = BuildMaterialTraceId(RendererPathId, MaterialSlot, MaterialName);
        }

        private static string BuildRendererPathId(Renderer renderer, Transform instanceRoot)
        {
            if (renderer == null)
                return string.Empty;

            string path = BuildRelativeTransformPath(renderer.transform, instanceRoot);
            return renderer.GetType().Name + ":" + path;
        }

        private static string BuildRelativeTransformPath(Transform target, Transform root)
        {
            if (target == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder(BuildTransformSegment(target));
            while (target.parent != null && target.parent != root)
            {
                target = target.parent;
                builder.Insert(0, BuildTransformSegment(target) + "/");
            }

            return builder.ToString();
        }

        private static string BuildTransformSegment(Transform target)
        {
            return target != null
                ? target.name + "[" + target.GetSiblingIndex() + "]"
                : string.Empty;
        }

        private static string BuildMaterialTraceId(string rendererPathId, int slot, string materialName)
        {
            return rendererPathId
                + "|slot:"
                + slot
                + "|mat:"
                + (string.IsNullOrEmpty(materialName) ? "<none>" : materialName);
        }
    }

    /// <summary>
    /// 简化版 MatDataTransferInstance - 仅维护 renderer/material/shader 映射关系
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public partial class MatDataTransferInstance : MonoBehaviour
    {
        [SerializeField] private List<RendererMaterialBinding> bindings = new List<RendererMaterialBinding>();
        [System.NonSerialized] private readonly Dictionary<long, RendererMaterialBinding> m_BindingMap =
            new Dictionary<long, RendererMaterialBinding>();

        public IReadOnlyList<RendererMaterialBinding> Bindings => bindings;

        private static readonly List<MatDataTransferInstance> s_LiveInstances = new List<MatDataTransferInstance>();
        internal static event System.Action<MatDataTransferInstance> LiveInstanceEnabled;
        internal static event System.Action<MatDataTransferInstance> LiveInstanceDisabled;
        internal static int TrackedLiveInstanceCount => LiveInstances.Count;
        internal static IReadOnlyList<MatDataTransferInstance> LiveInstances
        {
            get
            {
                PruneLiveInstances();
                return s_LiveInstances;
            }
        }

        private void OnEnable()
        {
            EnsureSourceGuid(true);
            RefreshRendererBindings();
            TrackLiveInstance();
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void OnDisable()
        {
            RemoveLiveInstance(this);
            SetRegisteredInstanceId(-1);
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void OnDestroy()
        {
            RemoveLiveInstance(this);
            SetRegisteredInstanceId(-1);
        }

        private void OnValidate()
        {
            EnsureSourceGuid(true);
            RefreshRendererBindings();
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void Reset()
        {
            EnsureSourceGuid(true);
            RefreshRendererBindings();
        }

        [ContextMenu("Refresh Bindings")]
        public void RefreshBindings()
        {
            RefreshRendererBindings();
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        public void RefreshRendererBindings()
        {
            if (!ValidateInstanceHierarchyForRefresh())
                return;

            bindings.Clear();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    bindings.Add(new RendererMaterialBinding(renderer, slot, transform));
                }
            }

            RebuildBindingMap();
        }

        public RendererMaterialBinding QueryBinding(Renderer renderer, int materialSlot, string shaderName = null)
        {
            return QueryBinding(new ParamRendererBinding(renderer, materialSlot), shaderName);
        }

        public RendererMaterialBinding QueryBinding(ParamRendererBinding target, string shaderName = null)
        {
            if (!IsValidBindingTarget(target))
                return null;

            EnsureBindingMap();
            int rendererId = target.Renderer != null
                ? target.Renderer.GetInstanceID()
                : target.RendererId;
            if (!m_BindingMap.TryGetValue(GetBindingKey(rendererId, target.MaterialSlot), out RendererMaterialBinding binding))
                return null;

            return IsBindingMatch(binding, target, shaderName) ? binding : null;
        }

        private void EnsureBindingMap()
        {
            if (m_BindingMap.Count != bindings.Count)
                RebuildBindingMap();
        }

        private void RebuildBindingMap()
        {
            m_BindingMap.Clear();
            for (int i = 0; i < bindings.Count; i++)
            {
                RendererMaterialBinding binding = bindings[i];
                if (binding == null || binding.RendererId == 0 || binding.MaterialSlot < 0)
                    continue;

                m_BindingMap[GetBindingKey(binding.RendererId, binding.MaterialSlot)] = binding;
            }
        }

        private static long GetBindingKey(int rendererId, int materialSlot)
        {
            return ((long)rendererId << 32) ^ (uint)materialSlot;
        }

        private static bool IsValidBindingTarget(ParamRendererBinding target)
        {
            return target.MaterialSlot >= 0 && (target.Renderer != null || target.RendererId != 0);
        }

        private static bool IsBindingMatch(RendererMaterialBinding binding, ParamRendererBinding target, string shaderName)
        {
            if (binding == null)
                return false;

            if (binding.MaterialSlot != target.MaterialSlot)
                return false;

            if (!IsSameRenderer(binding, target))
                return false;

            return string.IsNullOrEmpty(shaderName)
                || string.Equals(binding.ShaderName, shaderName, System.StringComparison.Ordinal);
        }

        private static bool IsSameRenderer(RendererMaterialBinding binding, ParamRendererBinding target)
        {
            if (target.Renderer != null)
                return binding.Renderer == target.Renderer;

            return target.RendererId != 0 && binding.RendererId == target.RendererId;
        }

        public Dictionary<string, List<RendererMaterialBinding>> GetBindingsByShader()
        {
            Dictionary<string, List<RendererMaterialBinding>> result = new Dictionary<string, List<RendererMaterialBinding>>();

            foreach (var binding in bindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.ShaderName))
                    continue;

                if (!result.TryGetValue(binding.ShaderName, out var list))
                {
                    list = new List<RendererMaterialBinding>();
                    result[binding.ShaderName] = list;
                }

                list.Add(binding);
            }

            return result;
        }

        internal void OnRegistered()
        {
            RefreshRendererBindings();
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        #region Instance Management

        internal static void CopyLiveInstancesTo(List<MatDataTransferInstance> results)
        {
            if (results == null)
                return;

            PruneLiveInstances();
            results.Clear();
            foreach (var instance in s_LiveInstances)
                results.Add(instance);
        }

        private void TrackLiveInstance()
        {
            if (isActiveAndEnabled)
                AddLiveInstance(this);
        }

        private static void AddLiveInstance(MatDataTransferInstance instance)
        {
            if (instance == null || s_LiveInstances.Contains(instance))
                return;

            s_LiveInstances.Add(instance);
            LiveInstanceEnabled?.Invoke(instance);
        }

        private static void RemoveLiveInstance(MatDataTransferInstance instance)
        {
            if (instance == null)
                return;

            if (s_LiveInstances.Remove(instance))
                LiveInstanceDisabled?.Invoke(instance);
        }

        private static void PruneLiveInstances()
        {
            for (int i = s_LiveInstances.Count - 1; i >= 0; i--)
            {
                MatDataTransferInstance instance = s_LiveInstances[i];
                if (instance == null || !instance.isActiveAndEnabled)
                {
                    s_LiveInstances.RemoveAt(i);
                    if (instance != null)
                        LiveInstanceDisabled?.Invoke(instance);
                }
            }
        }

        #endregion
    }
}


