using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private void ApplyWriteCommands(IReadOnlyList<MaterialWriteCommand> commands)
        {
            if (commands == null)
                return;

            BeginWriteTargetFrame();
            for (int i = 0; i < commands.Count; i++)
            {
                MaterialWriteCommand command = commands[i];
                ParamWriteMethod writeMethod = ApplyWriteCommand(command, out string failureReason);
                ParamSubmitStep step = writeMethod == ParamWriteMethod.None
                    ? ParamSubmitStep.WriterFailed("Write.Apply", failureReason)
                    : ParamSubmitStep.Applied("Write.Apply", "Write applied.");

                ParamTransferPayload payload = command.Payload;
                MatDataTransferLogging.AppendSubmitStep(
                    ref payload,
                    step);
                MatDataTransferLogging.CaptureWriteSnapshot(
                    ref payload,
                    command,
                    writeMethod);
            }

            FlushPropertyBlocks();
        }

        private ParamWriteMethod ApplyWriteCommand(MaterialWriteCommand command, out string failureReason)
        {
            if (!TryCreateWriteTarget(command, out MaterialWriteTarget target, out failureReason))
                return ParamWriteMethod.None;

#if UNITY_EDITOR
            if (TryApplyPropertyBlock(command, target, out failureReason))
            {
                failureReason = string.Empty;
                return ParamWriteMethod.MaterialPropertyBlock;
            }
            if (TryApplyMaterial(command, target, false, out failureReason))
            {
                failureReason = string.Empty;
                return ParamWriteMethod.MaterialInstance;
            }
#else
            if (TryApplyMaterial(command, target, false, out failureReason))
            {
                failureReason = string.Empty;
                return ParamWriteMethod.MaterialInstance;
            }
            if (TryApplyPropertyBlock(command, target, out failureReason))
            {
                failureReason = string.Empty;
                return ParamWriteMethod.MaterialPropertyBlock;
            }
#endif
            if (TryApplyMaterial(command, target, true, out failureReason))
            {
                failureReason = string.Empty;
                return ParamWriteMethod.SharedMaterial;
            }

            if (string.IsNullOrEmpty(failureReason))
                failureReason = "Write target resource became unavailable.";
            return ParamWriteMethod.None;
        }

        private bool TryApplyPropertyBlock(
            MaterialWriteCommand command,
            MaterialWriteTarget target,
            out string failureReason)
        {
            if (!TryGetPropertyBlock(target, out MaterialPropertyBlock block, out failureReason))
                return false;

            SetValue(block, target.PropertyId, command.Payload.Identity.Value);
            return true;
        }

        private static bool TryApplyMaterial(
            MaterialWriteCommand command,
            MaterialWriteTarget target,
            bool shared,
            out string failureReason)
        {
            if (!TryGetMaterial(target, shared, out Material material, out failureReason))
                return false;

            SetValue(material, target.PropertyId, command.Payload.Identity.Value);
            return true;
        }

        private static void SetValue(MaterialPropertyBlock block, int propertyId, ParamValue value)
        {
            switch (value.Type)
            {
                case ParamValueType.Float:
                    block.SetFloat(propertyId, value.FloatValue);
                    break;
                case ParamValueType.Color:
                    block.SetColor(propertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    block.SetVector(propertyId, value.VectorValue);
                    break;
                case ParamValueType.Texture:
                    block.SetTexture(propertyId, value.TextureValue);
                    break;
            }
        }

        private static void SetValue(Material material, int propertyId, ParamValue value)
        {
            switch (value.Type)
            {
                case ParamValueType.Float:
                    material.SetFloat(propertyId, value.FloatValue);
                    break;
                case ParamValueType.Color:
                    material.SetColor(propertyId, value.ColorValue);
                    break;
                case ParamValueType.Vector:
                    material.SetVector(propertyId, value.VectorValue);
                    break;
                case ParamValueType.Texture:
                    material.SetTexture(propertyId, value.TextureValue);
                    break;
            }
        }

    }
}
