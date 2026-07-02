using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    internal struct MaterialParameterSubmitPayload
    {
        public MatDataTransferInstance Target;
        public string SemanticKey;
        public ParamValue Value;
        public MatDataTransferSubmitSource Source;
        public RendererMaterialBinding Binding;
        public ParamWriteLayer Layer;
        public int Priority;
        public ParamRequestIdentity Identity;
        public ParamRendererBinding RendererBinding;
        public ParamWriteConfig WriteConfig;
        public int InstanceId;
        public int Sequence;
        public MaterialParameterSubmitResult Result;

        public MaterialParameterSubmitPayload(
            MatDataTransferInstance target,
            string semanticKey,
            ParamValue value,
            MatDataTransferSubmitSource source,
            RendererMaterialBinding binding,
            ParamWriteLayer layer,
            int priority)
        {
            Target = target;
            SemanticKey = semanticKey;
            Value = value;
            Source = source;
            Binding = binding;
            Layer = layer;
            Priority = priority;
            Identity = new ParamRequestIdentity(source.Id, null, semanticKey);
            RendererBinding = new ParamRendererBinding(binding);
            WriteConfig = new ParamWriteConfig(layer, priority);
            InstanceId = target != null ? target.InstanceId : -1;
            Sequence = 0;
            Result = new MaterialParameterSubmitResult();
        }

        internal void RefreshRouting()
        {
            Identity = new ParamRequestIdentity(Source.Id, Identity.ProviderName, SemanticKey);
            RendererBinding = new ParamRendererBinding(Binding);
            WriteConfig = new ParamWriteConfig(Layer, Priority);
            InstanceId = Target != null ? Target.InstanceId : -1;
        }
    }

    public struct MatDataTransferSubmitSource
    {
        public string Id;
        public UnityEngine.Object Owner;

        public static MatDataTransferSubmitSource From(UnityEngine.Object owner, string label = null)
        {
            return new MatDataTransferSubmitSource
            {
                Owner = owner,
                Id = BuildId(owner, label)
            };
        }

        private static string BuildId(UnityEngine.Object owner, string label)
        {
            string safeLabel = Sanitize(string.IsNullOrWhiteSpace(label) ? "Submitter" : label);
            if (owner == null)
                return "MDTS." + safeLabel;

            string ownerType = owner.GetType().Name;
            string ownerPath = owner.name;
            if (owner is Component component)
                ownerPath = BuildTransformPath(component.transform);
            else if (owner is GameObject gameObject)
                ownerPath = BuildTransformPath(gameObject.transform);

            return "MDTS."
                + safeLabel
                + "."
                + Sanitize(ownerType)
                + "."
                + Sanitize(ownerPath);
        }

        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder(BuildTransformSegment(transform));
            while (transform.parent != null)
            {
                transform = transform.parent;
                builder.Insert(0, BuildTransformSegment(transform) + "/");
            }

            return builder.ToString();
        }

        private static string BuildTransformSegment(Transform transform)
        {
            return transform != null
                ? transform.name + "[" + transform.GetSiblingIndex() + "]"
                : string.Empty;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Empty";

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                builder.Append(IsSafeChar(ch) ? ch : '_');
            }

            return builder.ToString();
        }

        private static bool IsSafeChar(char ch)
        {
            return char.IsLetterOrDigit(ch)
                || ch == '_'
                || ch == '-'
                || ch == '.'
                || ch == '/'
                || ch == '['
                || ch == ']';
        }
    }

    [Serializable]
    public sealed class MaterialParameterSubmitTrace
    {
        public string Stage;
        public ParamWriteResultType Type;
        public ParamWriteResultCode Code;
        public string Message;

        public MaterialParameterSubmitTrace(
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            Stage = stage;
            Type = type;
            Code = code;
            Message = message;
        }
    }

    [Serializable]
    public sealed class MaterialParameterSubmitResult
    {
        public bool Accepted;
        public string Message;
        public ParamWriteResultInfo ResultInfo;
        public readonly List<MaterialParameterSubmitTrace> Traces =
            new List<MaterialParameterSubmitTrace>();

        public void AddTrace(
            string stage,
            ParamWriteResultType type,
            ParamWriteResultCode code,
            string message)
        {
            Traces.Add(new MaterialParameterSubmitTrace(stage, type, code, message));
            Message = message;
            ResultInfo = new ParamWriteResultInfo
            {
                Accepted = type != ParamWriteResultType.Rejected,
                Applied = type == ParamWriteResultType.Applied,
                Type = type,
                Code = code,
                Message = message
            };
            Accepted = ResultInfo.Accepted;
        }

        public override string ToString()
        {
            if (Accepted)
                return string.IsNullOrEmpty(Message) ? "Submit accepted." : Message;

            return $"{ResultInfo.Code}: {Message}";
        }
    }
}
