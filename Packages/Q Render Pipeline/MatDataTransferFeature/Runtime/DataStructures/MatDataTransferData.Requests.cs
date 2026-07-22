using System;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public struct ParamRequestIdentity
    {
        [NonSerialized] public MatDataTransferInstance Target;
        public MatDataTransferSubmitSource Source;
        public string SemanticKey;
        public ParamValue Value;
        public ParamRendererBinding Binding;

        public string SourceId => Source.Id;

        public ParamRequestIdentity(
            MatDataTransferInstance target,
            MatDataTransferSubmitSource source,
            string semanticKey,
            ParamValue value,
            RendererMaterialBinding binding)
        {
            Target = target;
            Source = source;
            SemanticKey = semanticKey;
            Value = value;
            Binding = new ParamRendererBinding(binding);
        }
    }

    [Serializable]
    public struct ParamWriteConfig
    {
        public ParamWriteLayer Layer;
        public int Priority;

        public ParamWriteConfig(ParamWriteLayer layer, int priority = 0)
        {
            Layer = layer;
            Priority = priority;
        }
    }

    [Serializable]
    public struct ParamRendererBinding
    {
        public int RendererId;
        public int MaterialSlot;
        public string RendererPathId;
        public string MaterialTraceId;
        [NonSerialized] public Renderer Renderer;

        public ParamRendererBinding(int rendererId, int materialSlot)
        {
            RendererId = rendererId;
            MaterialSlot = materialSlot;
            RendererPathId = string.Empty;
            MaterialTraceId = string.Empty;
            Renderer = null;
        }

        public ParamRendererBinding(Renderer renderer, int materialSlot)
        {
            Renderer = renderer;
            RendererId = renderer != null ? renderer.GetInstanceID() : 0;
            MaterialSlot = materialSlot;
            RendererPathId = string.Empty;
            MaterialTraceId = string.Empty;
        }

        public ParamRendererBinding(string rendererPathId, string materialTraceId, int materialSlot)
        {
            Renderer = null;
            RendererId = 0;
            MaterialSlot = materialSlot;
            RendererPathId = rendererPathId ?? string.Empty;
            MaterialTraceId = materialTraceId ?? string.Empty;
        }

        public ParamRendererBinding(RendererMaterialBinding binding)
        {
            Renderer = binding?.Renderer;
            RendererId = binding != null ? binding.RendererId : 0;
            MaterialSlot = binding != null ? binding.MaterialSlot : -1;
            RendererPathId = binding != null ? binding.RendererPathId : string.Empty;
            MaterialTraceId = binding != null ? binding.MaterialTraceId : string.Empty;
        }
    }

    internal enum ParamSubmitScopeMode
    {
        SupportsKey,
        Shader
    }

    internal readonly struct ParamSubmitScope
    {
        public readonly ParamSubmitScopeMode Mode;
        public readonly string ShaderName;

        private ParamSubmitScope(
            ParamSubmitScopeMode mode,
            string shaderName)
        {
            Mode = mode;
            ShaderName = shaderName ?? string.Empty;
        }

        public static ParamSubmitScope SupportsKey()
        {
            return new ParamSubmitScope(
                ParamSubmitScopeMode.SupportsKey,
                string.Empty);
        }

        public static ParamSubmitScope Shader(string shaderName)
        {
            return new ParamSubmitScope(
                ParamSubmitScopeMode.Shader,
                shaderName);
        }
    }

    internal readonly struct ConflictKey : IEquatable<ConflictKey>
    {
        private readonly int m_InstanceId;
        private readonly int m_RendererId;
        private readonly int m_MaterialSlot;
        private readonly int m_PropertyId;

        public ConflictKey(int instanceId, int rendererId, int materialSlot, int propertyId)
        {
            m_InstanceId = instanceId;
            m_RendererId = rendererId;
            m_MaterialSlot = materialSlot;
            m_PropertyId = propertyId;
        }

        public bool Equals(ConflictKey other)
        {
            return m_InstanceId == other.m_InstanceId
                && m_RendererId == other.m_RendererId
                && m_MaterialSlot == other.m_MaterialSlot
                && m_PropertyId == other.m_PropertyId;
        }

        public override bool Equals(object obj)
        {
            return obj is ConflictKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = m_InstanceId;
                hash = (hash * 397) ^ m_RendererId;
                hash = (hash * 397) ^ m_MaterialSlot;
                return (hash * 397) ^ m_PropertyId;
            }
        }
    }

    internal readonly struct ParamWriteTarget
    {
        public readonly Renderer Renderer;
        public readonly int MaterialSlot;
        public readonly int PropertyId;

        public ParamWriteTarget(Renderer renderer, int materialSlot, int propertyId)
        {
            Renderer = renderer;
            MaterialSlot = materialSlot;
            PropertyId = propertyId;
        }
    }

    internal readonly struct RequestStrength
    {
        public readonly int Layer;
        public readonly int Priority;
        public readonly int SubmitFrameIndex;
        public readonly int Sequence;

        public RequestStrength(int layer, int priority, int submitFrameIndex, int sequence)
        {
            Layer = layer;
            Priority = priority;
            SubmitFrameIndex = submitFrameIndex;
            Sequence = sequence;
        }
    }

    internal readonly struct ValidatedParamRequest
    {
        public readonly int InstanceId;
        public readonly ConflictKey ConflictKey;
        public readonly ParamWriteTarget WriteTarget;
        public readonly ParamValue Value;
        public readonly RequestStrength Strength;
        public readonly int RequestId;
        public readonly ParamSubmitTrace Trace;
        public readonly int DiagnosticIndex;

        public ValidatedParamRequest(
            int instanceId,
            ConflictKey conflictKey,
            ParamWriteTarget writeTarget,
            ParamValue value,
            RequestStrength strength,
            int requestId)
            : this(
                instanceId,
                conflictKey,
                writeTarget,
                value,
                strength,
                requestId,
                null,
                requestId)
        {
        }

        public ValidatedParamRequest(
            int instanceId,
            ConflictKey conflictKey,
            ParamWriteTarget writeTarget,
            ParamValue value,
            RequestStrength strength,
            int requestId,
            ParamSubmitTrace trace,
            int diagnosticIndex)
        {
            InstanceId = instanceId;
            ConflictKey = conflictKey;
            WriteTarget = writeTarget;
            Value = value;
            Strength = strength;
            RequestId = requestId;
            Trace = trace;
            DiagnosticIndex = diagnosticIndex;
        }
    }

    internal readonly struct RequestDiagnosticContext
    {
        public readonly ParamTransferPayload Payload;
        public readonly ParamBindingResolution BindingResolution;

        public RequestDiagnosticContext(
            ParamTransferPayload payload,
            ParamBindingResolution bindingResolution)
        {
            Payload = payload;
            BindingResolution = bindingResolution;
        }
    }

    internal readonly struct ConflictDecision
    {
        public readonly int LoserDiagnosticIndex;
        public readonly int WinnerDiagnosticIndex;

        public ConflictDecision(int loserDiagnosticIndex, int winnerDiagnosticIndex)
        {
            LoserDiagnosticIndex = loserDiagnosticIndex;
            WinnerDiagnosticIndex = winnerDiagnosticIndex;
        }
    }

    internal struct ResolutionStats
    {
        public int InputCount;
        public int WinnerCount;
        public int OverriddenCount;

        public void Reset()
        {
            InputCount = 0;
            WinnerCount = 0;
            OverriddenCount = 0;
        }
    }

    internal struct WriterStats
    {
        public int AppliedCount;
        public int FailedCount;

        public void Reset()
        {
            AppliedCount = 0;
            FailedCount = 0;
        }

        public void Add(ParamWriteMethod writeMethod)
        {
            if (writeMethod == ParamWriteMethod.None)
                FailedCount++;
            else
                AppliedCount++;
        }
    }

    internal readonly struct ParamWriteCommand
    {
        public readonly ParamWriteTarget Target;
        public readonly ParamValue Value;
        public readonly int RequestId;
        public readonly ParamSubmitTrace Trace;
        public readonly int DiagnosticIndex;

        public ParamWriteCommand(ParamWriteTarget target, ParamValue value, int requestId)
            : this(target, value, requestId, null, requestId)
        {
        }

        public ParamWriteCommand(
            ParamWriteTarget target,
            ParamValue value,
            int requestId,
            ParamSubmitTrace trace,
            int diagnosticIndex)
        {
            Target = target;
            Value = value;
            RequestId = requestId;
            Trace = trace;
            DiagnosticIndex = diagnosticIndex;
        }
    }
}
