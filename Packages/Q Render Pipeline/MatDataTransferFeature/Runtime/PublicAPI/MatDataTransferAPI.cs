using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public static class MatDataTransferAPI
    {
        public static ParamSubmitTrace Submit(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer = ParamWriteLayer.Gameplay,
            int priority = 0)
        {
            return MaterialParameterSubmitter.Submit(
                target,
                semanticKey,
                value,
                binding,
                source,
                layer,
                priority);
        }

        public static ParamSubmitTrace ForMaterial(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            Renderer targetRenderer,
            int materialSlot,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer = ParamWriteLayer.Gameplay,
            int priority = 0)
        {
            return MaterialParameterSubmitter.ForMaterial(
                target,
                semanticKey,
                value,
                targetRenderer,
                materialSlot,
                source,
                layer,
                priority);
        }
    }
}
