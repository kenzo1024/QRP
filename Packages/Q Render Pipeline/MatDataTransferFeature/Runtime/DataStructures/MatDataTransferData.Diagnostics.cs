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

        public string RendererPath;
        public string ProviderName;

        public ParamRequestIdentity Identity;
        public ResolvedMaterialBinding Binding;
        public ParamWriteConfig WriteConfig;
        public ParamWriteMethod WriteMethod;
        public ParamSubmitStep Step;
        public ParamWriteStatus Status => Step != null ? Step.Status : ParamWriteStatus.Submitted;

        public string InspectorDisplayName;
        public string ValuePreview;
        public string SubmitLogSummary;
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
    public sealed class MatDataTransferTimelineFrame
    {
        public int FrameIndex;
        public double TimeSinceStartup;
        public readonly System.Collections.Generic.List<MatDataTransferTimelineRecord> Records =
            new System.Collections.Generic.List<MatDataTransferTimelineRecord>();

        public MatDataTransferTimelineFrame(
            int frameIndex,
            double timeSinceStartup,
            System.Collections.Generic.IReadOnlyList<MatDataTransferTimelineRecord> records)
        {
            FrameIndex = frameIndex;
            TimeSinceStartup = timeSinceStartup;
            if (records == null)
                return;

            for (int i = 0; i < records.Count; i++)
                Records.Add(records[i]);
        }

        public void AddRecords(System.Collections.Generic.IReadOnlyList<MatDataTransferTimelineRecord> records)
        {
            if (records == null)
                return;

            for (int i = 0; i < records.Count; i++)
                Records.Add(records[i]);
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
        public string Stage;
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
        public string ResultCode;
        public string OverriddenBySourceId;
        public string Message;
        public string InspectorDisplayName;
        public string ValuePreview;
        public string SubmitLogSummary;
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
                RendererId = record.Identity.Binding.RendererId,
                MaterialSlot = record.Identity.Binding.MaterialSlot,
                RendererPathId = record.Identity.Binding.RendererPathId,
                MaterialTraceId = record.Identity.Binding.MaterialTraceId,
                PropertyId = record.Binding.PropertyId,
                Stage = record.Step != null ? record.Step.Stage : string.Empty,
                GameObjectPath = record.GameObjectPath,
                RendererPath = record.RendererPath,
                SourceId = record.Identity.SourceId,
                ProviderName = record.ProviderName,
                SemanticKey = record.Identity.SemanticKey,
                MatchedSemanticKey = record.Binding.MatchedSemanticKey,
                ShaderName = record.Binding.ShaderName,
                CatalogName = record.Binding.CatalogName,
                PropertyName = record.Binding.PropertyName,
                WriteMethod = record.WriteMethod.ToString(),
                Layer = record.WriteConfig.Layer.ToString(),
                Priority = record.WriteConfig.Priority,
                Status = record.Status.ToString(),
                ResultCode = record.Step != null ? record.Step.Code.ToString() : ParamWriteResultCode.None.ToString(),
                OverriddenBySourceId = record.Step != null ? record.Step.OverriddenBySourceId : string.Empty,
                Message = record.Step != null ? record.Step.Message : string.Empty,
                InspectorDisplayName = record.InspectorDisplayName,
                ValuePreview = record.ValuePreview,
                SubmitLogSummary = record.SubmitLogSummary,
                ValueHash = record.ValueHash.ToString("X16")
            };
        }
    }
}
