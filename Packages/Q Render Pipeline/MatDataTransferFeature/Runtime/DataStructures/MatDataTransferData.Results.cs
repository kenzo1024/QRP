using System;

namespace Rendering.MatDataTransfer.Runtime
{
    public enum ParamWriteStatus
    {
        Submitted,
        Applied,
        Overridden,
        Rejected,
        WriterFailed
    }

    public enum ParamWriteResultType
    {
        Accepted,
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
    public struct ParamWriteResultInfo
    {
        public bool Accepted;
        public bool Applied;
        public ParamWriteResultType Type;
        public ParamWriteResultCode Code;
        public string OverriddenBySourceId;
        public string Message;

        public static ParamWriteResultInfo Rejected(ParamWriteResultCode code, string message, string overriddenBy = null)
        {
            return new ParamWriteResultInfo
            {
                Accepted = false,
                Applied = false,
                Type = ParamWriteResultType.Rejected,
                Code = code,
                OverriddenBySourceId = overriddenBy,
                Message = message
            };
        }

        public static ParamWriteResultInfo Accept(ParamWriteResultCode code = ParamWriteResultCode.None, string message = null)
        {
            return new ParamWriteResultInfo
            {
                Accepted = true,
                Applied = false,
                Type = ParamWriteResultType.Accepted,
                Code = code,
                Message = message ?? string.Empty
            };
        }

        public static ParamWriteResultInfo CreateApplied()
        {
            return new ParamWriteResultInfo
            {
                Accepted = true,
                Applied = true,
                Type = ParamWriteResultType.Applied,
                Code = ParamWriteResultCode.None
            };
        }

        public static ParamWriteResultInfo WriterFailed()
        {
            return new ParamWriteResultInfo
            {
                Accepted = true,
                Applied = false,
                Type = ParamWriteResultType.WriterFailed,
                Code = ParamWriteResultCode.WriterFailed,
                Message = "WriterFailed"
            };
        }

        public static ParamWriteResultInfo Overridden(string overriddenBy)
        {
            return new ParamWriteResultInfo
            {
                Accepted = true,
                Applied = false,
                Type = ParamWriteResultType.Overridden,
                Code = ParamWriteResultCode.OverriddenByStrongerRequest,
                OverriddenBySourceId = overriddenBy,
                Message = ParamWriteResultCode.OverriddenByStrongerRequest.ToString()
            };
        }
    }

    [Serializable]
    public struct ParamWriteResult
    {
        public ParamRequestIdentity Identity;
        public ResolvedMaterialBinding Binding;
        public ParamWriteConfig WriteConfig;
        public ParamWriteMethod WriteMethod;
        public ParamWriteResultInfo ResultInfo;

        public bool Accepted => ResultInfo.Accepted;
        public bool Applied  => ResultInfo.Applied;

        internal static ParamWriteResult FromPayload(
            MaterialParameterSubmitPayload payload,
            ResolvedMaterialBinding binding,
            ParamWriteMethod writeMethod,
            ParamWriteResultInfo resultInfo)
        {
            return new ParamWriteResult
            {
                Identity = payload.Identity,
                Binding = binding,
                WriteConfig = payload.WriteConfig,
                WriteMethod = writeMethod,
                ResultInfo = resultInfo
            };
        }
    }
}
