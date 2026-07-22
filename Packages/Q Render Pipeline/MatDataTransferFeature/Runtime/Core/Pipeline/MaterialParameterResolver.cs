using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    internal sealed class MaterialParameterResolver
    {
        private readonly Dictionary<ConflictKey, int> m_WinnerIndices =
            new Dictionary<ConflictKey, int>();
        private readonly List<ValidatedParamRequest> m_Winners =
            new List<ValidatedParamRequest>();
        private readonly List<ParamWriteCommand> m_Commands = new List<ParamWriteCommand>();

        internal IReadOnlyList<ParamWriteCommand> ResolveWithoutDiagnostics(
            List<ValidatedParamRequest> requests,
            ref ResolutionStats stats)
        {
            using (MatDataTransferProfiling.ModuleResolver.Auto())
            {
                BeginResolve(requests, ref stats);
                if (requests != null)
                {
                    for (int i = 0; i < requests.Count; i++)
                        SelectWinner(requests[i], ref stats);
                }
                return BuildCommands(ref stats);
            }
        }

        internal IReadOnlyList<ParamWriteCommand> ResolveWithDiagnostics(
            List<ValidatedParamRequest> requests,
            List<ConflictDecision> conflictDecisions,
            ref ResolutionStats stats)
        {
            using (MatDataTransferProfiling.ModuleResolver.Auto())
            {
                BeginResolve(requests, ref stats);
                conflictDecisions?.Clear();
                if (requests != null)
                {
                    for (int i = 0; i < requests.Count; i++)
                        SelectWinnerWithDiagnostics(requests[i], conflictDecisions, ref stats);
                }
                return BuildCommands(ref stats);
            }
        }

        private void BeginResolve(List<ValidatedParamRequest> requests, ref ResolutionStats stats)
        {
            m_WinnerIndices.Clear();
            m_Winners.Clear();
            m_Commands.Clear();
            stats.Reset();
            stats.InputCount = requests != null ? requests.Count : 0;
        }

        private void SelectWinner(ValidatedParamRequest request, ref ResolutionStats stats)
        {
            if (!m_WinnerIndices.TryGetValue(request.ConflictKey, out int winnerIndex))
            {
                m_WinnerIndices.Add(request.ConflictKey, m_Winners.Count);
                m_Winners.Add(request);
                return;
            }

            stats.OverriddenCount++;
            ValidatedParamRequest current = m_Winners[winnerIndex];
            if (IsStronger(request.Strength, current.Strength))
            {
                MatDataTransferLogging.RecordOverriddenSummary(current.Trace);
                m_Winners[winnerIndex] = request;
            }
            else
            {
                MatDataTransferLogging.RecordOverriddenSummary(request.Trace);
            }
        }

        private void SelectWinnerWithDiagnostics(
            ValidatedParamRequest request,
            List<ConflictDecision> conflictDecisions,
            ref ResolutionStats stats)
        {
            if (!m_WinnerIndices.TryGetValue(request.ConflictKey, out int winnerIndex))
            {
                m_WinnerIndices.Add(request.ConflictKey, m_Winners.Count);
                m_Winners.Add(request);
                return;
            }

            stats.OverriddenCount++;
            ValidatedParamRequest current = m_Winners[winnerIndex];
            if (IsStronger(request.Strength, current.Strength))
            {
                MatDataTransferLogging.RecordOverriddenSummary(current.Trace);
                conflictDecisions?.Add(new ConflictDecision(
                    current.DiagnosticIndex,
                    request.DiagnosticIndex));
                m_Winners[winnerIndex] = request;
            }
            else
            {
                MatDataTransferLogging.RecordOverriddenSummary(request.Trace);
                conflictDecisions?.Add(new ConflictDecision(
                    request.DiagnosticIndex,
                    current.DiagnosticIndex));
            }
        }

        private IReadOnlyList<ParamWriteCommand> BuildCommands(ref ResolutionStats stats)
        {
            for (int i = 0; i < m_Winners.Count; i++)
            {
                ValidatedParamRequest winner = m_Winners[i];
                m_Commands.Add(new ParamWriteCommand(
                    winner.WriteTarget,
                    winner.Value,
                    winner.RequestId,
                    winner.Trace,
                    winner.DiagnosticIndex));
            }

            stats.WinnerCount = m_Commands.Count;
            return m_Commands;
        }

        private static bool IsStronger(RequestStrength candidate, RequestStrength current)
        {
            if (candidate.Layer != current.Layer)
                return candidate.Layer > current.Layer;
            if (candidate.Priority != current.Priority)
                return candidate.Priority > current.Priority;
            if (candidate.SubmitFrameIndex != current.SubmitFrameIndex)
                return candidate.SubmitFrameIndex > current.SubmitFrameIndex;
            return candidate.Sequence > current.Sequence;
        }
    }
}
