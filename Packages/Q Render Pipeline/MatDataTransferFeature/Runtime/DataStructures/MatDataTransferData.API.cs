using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public readonly struct ParamBatchWrite
    {
        public readonly string SemanticKey;
        public readonly ParamValue Value;

        public ParamBatchWrite(string semanticKey, ParamValue value)
        {
            SemanticKey = semanticKey;
            Value = value;
        }
    }

    public readonly struct ParamBatchSubmitResult
    {
        public readonly int TotalCount;
        public readonly int AcceptedCount;
        public readonly int RejectedCount;
        public readonly int FirstRejectedIndex;
        public readonly ParamWriteResultCode FirstErrorCode;

        public ParamBatchSubmitResult(
            int totalCount,
            int acceptedCount,
            int rejectedCount,
            int firstRejectedIndex,
            ParamWriteResultCode firstErrorCode)
        {
            TotalCount = totalCount;
            AcceptedCount = acceptedCount;
            RejectedCount = rejectedCount;
            FirstRejectedIndex = firstRejectedIndex;
            FirstErrorCode = firstErrorCode;
        }
    }

    internal enum ParamSubmitStage
    {
        ScopeBegin,
        ScopeValidate,
        ScopeExpand,
        SubmitBegin,
        SubmitValidate,
        SubmitQueue,
        ResolveConflict,
        WriteApply
    }

    internal struct ParamTransferPayload
    {
        public ParamRequestIdentity Identity;
        public ParamWriteConfig WriteConfig;
        public int SubmitFrameIndex;
        public int Sequence;
        public ParamSubmitTrace Trace;

        public ParamTransferPayload(
            ParamRequestIdentity identity,
            ParamWriteConfig writeConfig)
        {
            MatDataTransferProfiling.AddPayload();
            Identity = identity;
            WriteConfig = writeConfig;
            SubmitFrameIndex = -1;
            Sequence = 0;
            Trace = new ParamSubmitTrace();
        }
    }

    [Serializable]
    public struct MatDataTransferSubmitSource
    {
        public string Id;
        [NonSerialized] public UnityEngine.Object Owner;

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
                ownerPath = BuildComponentPath(component);
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

        private static string BuildComponentPath(Component component)
        {
            if (component == null)
                return string.Empty;

            return BuildTransformPath(component.transform)
                + "."
                + component.GetType().Name
                + "["
                + GetSameTypeComponentIndex(component)
                + "]";
        }

        private static int GetSameTypeComponentIndex(Component component)
        {
            Component[] components = component.GetComponents(component.GetType());
            for (int i = 0; i < components.Length; i++)
            {
                if (ReferenceEquals(components[i], component))
                    return i;
            }

            return 0;
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
    public sealed class ParamSubmitStep
    {
        public string Stage;
        public ParamWriteStatus Status;
        public ParamWriteResultCode Code;
        public string OverriddenBySourceId;
        public string Message;

        public bool IsAccepted => Status != ParamWriteStatus.Rejected;
        public bool IsApplied => Status == ParamWriteStatus.Applied;

        public ParamSubmitStep()
        {
            MatDataTransferProfiling.AddStep();
            Stage = string.Empty;
            Status = ParamWriteStatus.Submitted;
            Code = ParamWriteResultCode.None;
            OverriddenBySourceId = string.Empty;
            Message = string.Empty;
        }

        public ParamSubmitStep(
            string stage,
            ParamWriteStatus status,
            ParamWriteResultCode code,
            string message,
            string overriddenBySourceId = null)
        {
            MatDataTransferProfiling.AddStep();
            Stage = stage;
            Status = status;
            Code = code;
            Message = message ?? string.Empty;
            OverriddenBySourceId = overriddenBySourceId;
        }

        public static ParamSubmitStep Submitted(string stage, string message = null)
        {
            return new ParamSubmitStep(stage, ParamWriteStatus.Submitted, ParamWriteResultCode.None, message);
        }

        public static ParamSubmitStep Queued(string stage, string message = null)
        {
            return new ParamSubmitStep(stage, ParamWriteStatus.Queued, ParamWriteResultCode.None, message);
        }

        public static ParamSubmitStep Applied(string stage, string message = null)
        {
            return new ParamSubmitStep(stage, ParamWriteStatus.Applied, ParamWriteResultCode.None, message);
        }

        public static ParamSubmitStep Rejected(
            string stage,
            ParamWriteResultCode code,
            string message)
        {
            return new ParamSubmitStep(stage, ParamWriteStatus.Rejected, code, message);
        }

        public static ParamSubmitStep Overridden(string stage, string overriddenBySourceId)
        {
            return new ParamSubmitStep(
                stage,
                ParamWriteStatus.Overridden,
                ParamWriteResultCode.OverriddenByStrongerRequest,
                ParamWriteResultCode.OverriddenByStrongerRequest.ToString(),
                overriddenBySourceId);
        }

        public static ParamSubmitStep WriterFailed(string stage, string message = null)
        {
            return new ParamSubmitStep(
                stage,
                ParamWriteStatus.WriterFailed,
                ParamWriteResultCode.WriterFailed,
                string.IsNullOrEmpty(message) ? "WriterFailed" : message);
        }
    }

    [Serializable]
    public sealed class ParamSubmitTrace
    {
        private List<ParamSubmitStep> m_Steps;
        private List<ParamSubmitTrace> m_Children;
        private ParamWriteStatus m_Status = ParamWriteStatus.Submitted;
        private ParamWriteResultCode m_Code = ParamWriteResultCode.None;
        private string m_Message = string.Empty;
        private bool m_HasResult;

        public List<ParamSubmitStep> Steps =>
            m_Steps ??= new List<ParamSubmitStep>();
        public List<ParamSubmitTrace> Children =>
            m_Children ??= new List<ParamSubmitTrace>();
        public ParamSubmitStep Current =>
            m_Steps != null && m_Steps.Count > 0 ? m_Steps[m_Steps.Count - 1] : null;
        public bool IsBatch => IsBatchRoot || (m_Children != null && m_Children.Count > 0) || SkippedCount > 0;
        public bool IsAccepted => IsBatch ? AcceptedCount > 0 : m_HasResult && m_Status != ParamWriteStatus.Rejected;
        public bool IsApplied => IsBatch ? AppliedCount > 0 : m_HasResult && m_Status == ParamWriteStatus.Applied;
        public ParamWriteStatus Status => m_Status;
        public ParamWriteResultCode Code => m_Code;
        public string Message => m_Message;
        public bool IsBatchRoot { get; private set; }
        public int SkippedCount { get; private set; }
        public int TotalCount => IsBatch ? GetChildCount() + SkippedCount : 1;
        public int AcceptedCount => CountChildren(true, false);
        public int AppliedCount => CountChildren(false, true);
        public int RejectedCount => IsBatch ? CountRejectedChildren() : (m_HasResult && !IsAccepted ? 1 : 0);

        public ParamSubmitTrace()
        {
            MatDataTransferProfiling.AddTrace();
        }

        public void AddStep(ParamSubmitStep step)
        {
            if (step == null)
                return;

            UpdateSummary(step.Status, step.Code, step.Message);
            Steps.Add(step);
        }

        public void AddChild(ParamSubmitTrace trace)
        {
            if (trace != null)
                Children.Add(trace);
        }

        public void MarkBatchRoot()
        {
            IsBatchRoot = true;
        }

        public void AddSkipped(int count = 1)
        {
            if (count > 0)
                SkippedCount += count;
        }

        internal void UpdateSummary(
            ParamWriteStatus status,
            ParamWriteResultCode code,
            string message = null)
        {
            m_Status = status;
            m_Code = code;
            m_Message = message ?? string.Empty;
            m_HasResult = true;
        }

        public override string ToString()
        {
            if (IsBatch)
            {
                return $"Batch submit: {AcceptedCount} accepted, {RejectedCount} rejected, {SkippedCount} skipped.";
            }

            if (IsAccepted)
                return string.IsNullOrEmpty(Message) ? "Submit accepted." : Message;

            return $"{Code}: {Message}";
        }

        private int CountChildren(bool accepted, bool applied)
        {
            if (m_Children == null)
                return 0;

            int count = 0;
            for (int i = 0; i < m_Children.Count; i++)
            {
                ParamSubmitTrace child = m_Children[i];
                if (child == null)
                    continue;

                if (accepted && child.IsAccepted)
                    count++;
                else if (applied && child.IsApplied)
                    count++;
            }

            return count;
        }

        private int CountRejectedChildren()
        {
            if (m_Children == null)
                return 0;

            int count = 0;
            for (int i = 0; i < m_Children.Count; i++)
            {
                ParamSubmitTrace child = m_Children[i];
                if (child != null && !child.IsAccepted)
                    count++;
            }

            return count;
        }

        private int GetChildCount()
        {
            return m_Children != null ? m_Children.Count : 0;
        }
    }
}
