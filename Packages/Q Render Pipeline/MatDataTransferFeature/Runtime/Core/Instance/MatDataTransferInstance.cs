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

        public IReadOnlyList<RendererMaterialBinding> Bindings => bindings;

        private static readonly List<MatDataTransferInstance> s_LiveInstances = new List<MatDataTransferInstance>();

        private void OnEnable()
        {
            TrackLiveInstance();
            EnsureSourceGuid(true);
            RefreshRendererBindings();
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void OnDisable()
        {
            RemoveLiveInstance(this);
            SetRegisteredInstanceId(-1);
            MatDataTransferRuntime.RequestEditorUpdate();
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
        }

        public List<RendererMaterialBinding> QueryBindings(int rendererId = 0, int materialSlot = -1, string shaderName = null)
        {
            List<RendererMaterialBinding> results = new List<RendererMaterialBinding>();

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                if (rendererId != 0 && binding.RendererId != rendererId)
                    continue;

                if (materialSlot >= 0 && binding.MaterialSlot != materialSlot)
                    continue;

                if (!string.IsNullOrEmpty(shaderName) &&
                    !string.Equals(binding.ShaderName, shaderName, System.StringComparison.Ordinal))
                    continue;

                results.Add(binding);
            }

            return results;
        }

        public List<RendererMaterialBinding> QueryBindingsByTrace(
            string rendererPathId,
            int materialSlot = -1,
            string shaderName = null)
        {
            List<RendererMaterialBinding> results = new List<RendererMaterialBinding>();
            if (string.IsNullOrEmpty(rendererPathId))
                return results;

            foreach (var binding in bindings)
            {
                if (binding == null)
                    continue;

                if (!string.Equals(binding.RendererPathId, rendererPathId, System.StringComparison.Ordinal))
                    continue;

                if (materialSlot >= 0 && binding.MaterialSlot != materialSlot)
                    continue;

                if (!string.IsNullOrEmpty(shaderName) &&
                    !string.Equals(binding.ShaderName, shaderName, System.StringComparison.Ordinal))
                    continue;

                results.Add(binding);
            }

            return results;
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

        internal void OnRegisteredByFeature()
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
        }

        private static void RemoveLiveInstance(MatDataTransferInstance instance)
        {
            if (instance == null)
                return;

            s_LiveInstances.Remove(instance);
        }

        private static void PruneLiveInstances()
        {
            for (int i = s_LiveInstances.Count - 1; i >= 0; i--)
            {
                MatDataTransferInstance instance = s_LiveInstances[i];
                if (instance == null || !instance.isActiveAndEnabled)
                    s_LiveInstances.RemoveAt(i);
            }
        }

        #endregion
    }
}


