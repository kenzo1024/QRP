using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public static class MatDataTransferAPI
    {
        public static ParamSubmitTrace ForMaterial(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            Renderer targetRenderer,
            int materialSlot,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
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

        public static ParamSubmitTrace ForInstance(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            return MaterialParameterSubmitter.ForInstance(
                target,
                semanticKey,
                value,
                source,
                layer,
                priority);
        }

        public static ParamSubmitTrace ForShader(
            MatDataTransferInstance target,
            string shaderName,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            ParamWriteLayer layer,
            int priority = 0)
        {
            return MaterialParameterSubmitter.ForShader(
                target,
                shaderName,
                semanticKey,
                value,
                source,
                layer,
                priority);
        }
    }
}
