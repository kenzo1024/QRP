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

        [Header("Target")]
        [SerializeField] private MatDataTransferInstance targetInstance;
        [SerializeField] private Renderer targetRenderer;
        [Min(0)]
        [SerializeField] private int materialSlot;

        [Header("Submit")]
        [SerializeField] private string semanticKey;
        [SerializeField] private int priority;
        [SerializeField] private ParamValue testValue = ParamValue.Color(Color.white);

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
            // ParamSubmitTrace result = MatDataTransferAPI.ForMaterial(
            //     targetInstance,
            //     semanticKey,
            //     testValue,
            //     targetRenderer,
            //     materialSlot,
            //     MatDataTransferSubmitSource.From(this, SourceLabel),
            //     ParamWriteLayer.Gameplay,
            //     priority);

            ParamSubmitTrace result = MatDataTransferAPI.ForInstance(
                targetInstance,
                semanticKey,
                testValue,
                MatDataTransferSubmitSource.From(this, SourceLabel),
                ParamWriteLayer.Gameplay,
                priority
            );

            lastSubmitSuccess = result.IsAccepted;
            statusMessage = result.ToString();
            UpdateResolvedTargetMessage();
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

            return targetInstance.QueryBinding(targetRenderer, materialSlot);
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
