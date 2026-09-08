using System;

namespace PennyPet
{
    internal enum DockInteractionPhase { Idle, Preparing, Dragging, Rebasing, Finalizing }

    // Pet-UI-thread-owned validity token for one gesture. It has no window,
    // persistence or executor responsibilities.
    internal sealed class DockInteractionSession
    {
        private long _nextEpoch;
        internal DockInteractionSession() { SourceNoteId = String.Empty; }
        internal DockInteractionPhase Phase { get; private set; }
        internal long Epoch { get; private set; }
        internal string SourceNoteId { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal string FinalRemainderNoteId { get; private set; }
        internal bool IsActive { get { return Phase != DockInteractionPhase.Idle; } }
        internal bool IsFinalizing { get { return Phase == DockInteractionPhase.Finalizing; } }

        internal long BeginPreparing(string sourceNoteId, long generation)
        {
            if (String.IsNullOrWhiteSpace(sourceNoteId)) throw new ArgumentException("A Dock source note id is required.", nameof(sourceNoteId));
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase != DockInteractionPhase.Idle) return 0;
            Epoch = NextEpoch(); SourceNoteId = sourceNoteId.Trim(); TopologyGeneration = generation;
            FinalRemainderNoteId = String.Empty; Phase = DockInteractionPhase.Preparing; return Epoch;
        }
        internal long BeginRebase(long generation)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase == DockInteractionPhase.Idle || Phase == DockInteractionPhase.Finalizing) return 0;
            Epoch = NextEpoch(); TopologyGeneration = generation; Phase = DockInteractionPhase.Rebasing; return Epoch;
        }
        internal bool TryEnterDragging(long epoch, long generation)
        {
            if (Epoch != epoch || TopologyGeneration != generation ||
                (Phase != DockInteractionPhase.Preparing && Phase != DockInteractionPhase.Rebasing)) return false;
            Phase = DockInteractionPhase.Dragging; return true;
        }
        internal long BeginFinalizing(long generation, string remainderNoteId)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase == DockInteractionPhase.Idle) return 0;
            Epoch = NextEpoch(); TopologyGeneration = generation;
            FinalRemainderNoteId = remainderNoteId == null ? String.Empty : remainderNoteId.Trim();
            Phase = DockInteractionPhase.Finalizing; return Epoch;
        }
        internal long RestartFinalizing(long generation)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase != DockInteractionPhase.Finalizing) return 0;
            Epoch = NextEpoch(); TopologyGeneration = generation; return Epoch;
        }
        internal bool Matches(long epoch, long generation, DockInteractionPhase phase)
        { return Epoch == epoch && TopologyGeneration == generation && Phase == phase; }
        internal bool CanPlan(string sourceNoteId, long generation)
        { return Phase == DockInteractionPhase.Dragging && Epoch > 0 && TopologyGeneration == generation && String.Equals(SourceNoteId, sourceNoteId ?? String.Empty, StringComparison.OrdinalIgnoreCase); }
        internal long Reset()
        { Epoch = NextEpoch(); SourceNoteId = String.Empty; TopologyGeneration = 0; FinalRemainderNoteId = String.Empty; Phase = DockInteractionPhase.Idle; return Epoch; }
        private long NextEpoch() { _nextEpoch = _nextEpoch == Int64.MaxValue ? 1 : _nextEpoch + 1; return _nextEpoch <= 0 ? _nextEpoch = 1 : _nextEpoch; }
    }

    internal static class DockExecutionRules
    {
        internal static bool IsSameGeneration(WindowFacts facts, DisplayTopologySnapshot topology)
        { return facts != null && topology != null && facts.TopologyGeneration == topology.Generation; }
        internal static bool CanExecute(DockPlacementPlan plan, long currentGeneration, long currentEpoch)
        { return plan != null && plan.InteractionEpoch > 0 && plan.InteractionEpoch == currentEpoch && plan.TopologyGeneration == currentGeneration; }
    }
}
