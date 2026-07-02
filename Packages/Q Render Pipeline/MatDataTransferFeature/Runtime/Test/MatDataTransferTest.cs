using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    /// <summary>
    /// 最小测试：拿到 instance，组装一个参数，并在运行时每帧提交。
    /// </summary>
    [ExecuteAlways]
    public class MatDataTransferTest : MonoBehaviour
    {
        private const string SourceLabel = nameof(MatDataTransferTest);

        private enum TestValueKind
        {
            Color,
            Float
        }

        [Header("Target")]
        [SerializeField] private MatDataTransferInstance targetInstance;
        [SerializeField] private Renderer targetRenderer;
        [Min(0)]
        [SerializeField] private int materialSlot;

        [Header("Submit")]
        [SerializeField] private string semanticKey;
        [SerializeField] private int priority;
        [SerializeField] private TestValueKind valueKind = TestValueKind.Color;
        [SerializeField] private Color testColor = Color.white;
        [SerializeField] private float testFloat = 1.0f;

        [Header("Status")]
        [SerializeField] private bool lastSubmitSuccess;
        [SerializeField] private string resolvedTargetMessage;
        [SerializeField] private string statusMessage;

        private void Update()
        {
            EnsureTargetInstance();
            SubmitTestParameters();
        }

        private void SubmitTestParameters()
        {
            MaterialParameterSubmitResult result = MatDataTransferAPI.ForMaterial(
                targetInstance,
                semanticKey,
                BuildTestValue(),
                targetRenderer,
                materialSlot,
                MatDataTransferSubmitSource.From(this, SourceLabel),
                ParamWriteLayer.Gameplay,
                priority);

            lastSubmitSuccess = result.Accepted;
            statusMessage = result.ToString();
            UpdateResolvedTargetMessage();
        }

        private ParamValue BuildTestValue()
        {
            return valueKind == TestValueKind.Float
                ? ParamValue.Float(testFloat)
                : ParamValue.Color(testColor);
        }

        private void UpdateResolvedTargetMessage()
        {
            RendererMaterialBinding binding = ResolveTargetBinding();
            if (binding == null)
            {
                resolvedTargetMessage = "No binding matches current renderer and material slot.";
                return;
            }

            resolvedTargetMessage = $"{binding.ShaderName} | slot {binding.MaterialSlot} | {binding.MaterialName}";
        }

        private RendererMaterialBinding ResolveTargetBinding()
        {
            if (targetInstance == null || targetRenderer == null || materialSlot < 0)
                return null;

            var bindings = targetInstance.QueryBindings(targetRenderer.GetInstanceID(), materialSlot);
            return bindings != null && bindings.Count > 0 ? bindings[0] : null;
        }

        private void Reset()
        {
            EnsureTargetInstance();
            EnsureTargetRenderer();
        }

        private void OnValidate()
        {
            EnsureTargetInstance();
            EnsureTargetRenderer();
            UpdateResolvedTargetMessage();
        }

        private void EnsureTargetInstance()
        {
            if (targetInstance == null)
                targetInstance = GetComponent<MatDataTransferInstance>();
        }

        private void EnsureTargetRenderer()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>(true);
        }
    }
}
