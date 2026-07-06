using System;

namespace Rendering.MatDataTransfer.Runtime
{
    public enum ParamWriteStatus
    {
        Submitted,
        Queued,
        Applied,
        Overridden,
        Rejected,
        WriterFailed
    }

    public enum ParamWriteResultCode
    {
        None,
        ProfileMissing,
        SemanticKeyMissing,
        SourceIdMissing,
        FeatureMissing,
        ProviderUnavailable,
        PropertyMissing,
        BindingMissing,
        BindingNotActive,
        TypeMismatch,
        InstanceMissing,
        RendererOrMaterialSlotMissing,
        OverriddenByStrongerRequest,
        WriterFailed
    }

    [Serializable]
    public struct ParamWriteResult
    {
        public ParamRequestIdentity Identity;
        public ResolvedMaterialBinding Binding;
        public ParamWriteConfig WriteConfig;
        public ParamWriteMethod WriteMethod;
        public ParamSubmitStep Step;

        public bool IsAccepted => Step != null && Step.IsAccepted;
        public bool IsApplied => Step != null && Step.IsApplied;
        public ParamWriteStatus Status => Step != null ? Step.Status : ParamWriteStatus.Submitted;
        public ParamWriteResultCode Code => Step != null ? Step.Code : ParamWriteResultCode.None;
        public string Message => Step != null ? Step.Message : string.Empty;

        internal static ParamWriteResult FromPayload(
            ParamTransferPayload payload,
            ResolvedMaterialBinding binding,
            ParamWriteMethod writeMethod,
            ParamSubmitStep step)
        {
            return new ParamWriteResult
            {
                Identity = payload.Identity,
                Binding = binding,
                WriteConfig = payload.WriteConfig,
                WriteMethod = writeMethod,
                Step = step
            };
        }
    }
}
