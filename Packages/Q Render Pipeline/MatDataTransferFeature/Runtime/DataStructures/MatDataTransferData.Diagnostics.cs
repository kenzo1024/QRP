using System;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public struct MatDataTransferTimelineRecord
    {
        public int FrameIndex;
        public double TimeSinceStartup;
        public int Sequence;
        public int InstanceId;
        public string GameObjectPath;

        public ParamRendererBinding RendererBinding;
        public string RendererPath;

        public ParamRequestIdentity Identity;
        public ResolvedMaterialBinding Binding;
        public ParamWriteConfig WriteConfig;
        public ParamWriteMethod WriteMethod;
        public ParamWriteResultInfo ResultInfo;
        public ParamWriteStatus Status;

        public string InspectorDisplayName;
        public string ValuePreview;
        public ulong ValueHash;

        public static ulong HashValuePreview(string text)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;

                if (string.IsNullOrEmpty(text))
                    return hash;

                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= prime;
                }

                return hash;
            }
        }
    }

    [Serializable]
    public sealed class MatDataTransferTimelineLogLine
    {
        public string Schema;
        public string SessionId;
        public int FrameIndex;
        public int RecordIndex;
        public int Sequence;
        public double TimeSinceStartup;
        public int InstanceId;
        public int RendererId;
        public int MaterialSlot;
        public string RendererPathId;
        public string MaterialTraceId;
        public int PropertyId;
        public bool Accepted;
        public bool Applied;
        public string GameObjectPath;
        public string RendererPath;
        public string SourceId;
        public string ProviderName;
        public string SemanticKey;
        public string MatchedSemanticKey;
        public string ShaderName;
        public string CatalogName;
        public string PropertyName;
        public string WriteMethod;
        public string Layer;
        public int Priority;
        public string Status;
        public string ResultType;
        public string ResultCode;
        public string OverriddenBySourceId;
        public string Message;
        public string InspectorDisplayName;
        public string ValuePreview;
        public string ValueHash;

        public static MatDataTransferTimelineLogLine Create(
            string sessionId,
            int recordIndex,
            MatDataTransferTimelineRecord record)
        {
            return new MatDataTransferTimelineLogLine
            {
                Schema = "MatDataTransferTimeline.v1",
                SessionId = sessionId,
                FrameIndex = record.FrameIndex,
                RecordIndex = recordIndex,
                Sequence = record.Sequence,
                TimeSinceStartup = record.TimeSinceStartup,
                InstanceId = record.InstanceId,
                RendererId = record.RendererBinding.RendererId,
                MaterialSlot = record.RendererBinding.MaterialSlot,
                RendererPathId = record.RendererBinding.RendererPathId,
                MaterialTraceId = record.RendererBinding.MaterialTraceId,
                PropertyId = record.Binding.PropertyId,
                Accepted = record.ResultInfo.Accepted,
                Applied = record.ResultInfo.Applied,
                GameObjectPath = record.GameObjectPath,
                RendererPath = record.RendererPath,
                SourceId = record.Identity.SourceId,
                ProviderName = record.Identity.ProviderName,
                SemanticKey = record.Identity.SemanticKey,
                MatchedSemanticKey = record.Binding.MatchedSemanticKey,
                ShaderName = record.Binding.ShaderName,
                CatalogName = record.Binding.CatalogName,
                PropertyName = record.Binding.PropertyName,
                WriteMethod = record.WriteMethod.ToString(),
                Layer = record.WriteConfig.Layer.ToString(),
                Priority = record.WriteConfig.Priority,
                Status = record.Status.ToString(),
                ResultType = record.ResultInfo.Type.ToString(),
                ResultCode = record.ResultInfo.Code.ToString(),
                OverriddenBySourceId = record.ResultInfo.OverriddenBySourceId,
                Message = record.ResultInfo.Message,
                InspectorDisplayName = record.InspectorDisplayName,
                ValuePreview = record.ValuePreview,
                ValueHash = record.ValueHash.ToString("X16")
            };
        }
    }
}
