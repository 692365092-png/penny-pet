using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PennyPet
{
    internal enum DockInteractionPhase { Idle, Preparing, Dragging, Rebasing, Finalizing }

    internal enum DockSplitDecision { None, Cancelled, Detach }

    // Pet-thread owner of a complete gesture. Windows supply captures and
    // execute effects; they do not maintain a second source/phase/baseline.
    internal sealed class DockInteractionSession
    {
        private long _nextEpoch;
        private readonly List<string> _members = new List<string>();
        private readonly Dictionary<string, DockWindowFacts> _original =
            new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DockWindowFacts> _current =
            new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);

        internal DockInteractionSession()
        {
            SourceNoteId = RemainderNoteId = String.Empty;
            MemberIds = _members.AsReadOnly();
            BaselineFacts = new ReadOnlyDictionary<string, DockWindowFacts>(_original);
            PreviewFacts = new ReadOnlyDictionary<string, DockWindowFacts>(_current);
        }
        internal ReadOnlyCollection<string> MemberIds { get; private set; }
        internal IReadOnlyDictionary<string, DockWindowFacts> BaselineFacts { get; private set; }
        internal IReadOnlyDictionary<string, DockWindowFacts> PreviewFacts { get; private set; }
        internal DockWindowFacts StartFacts { get; private set; }
        internal DockWindowFacts LastFacts { get; private set; }
        internal DateTime StartedUtc { get; private set; }
        internal bool SplitEligible { get; private set; }
        internal bool Detached { get; private set; }
        internal string RemainderNoteId { get; private set; }
        internal DockMergePlan PendingMerge { get; private set; }

        internal void StageMerge(DockMergePlan merge) { PendingMerge = merge; }

        internal long BeginGesture(DockWindowFacts source, IList<string> members,
            IDictionary<string, DockWindowFacts> baseline, long generation, DateTime startedUtc)
        {
            if (source == null || members == null || members.Count == 0) return 0;
            int sourceIndex = -1;
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < members.Count; i++)
            {
                if (String.IsNullOrEmpty(members[i]) || !ids.Add(members[i])) return 0;
                if (String.Equals(members[i], source.NoteId,
                    StringComparison.OrdinalIgnoreCase)) sourceIndex = i;
            }
            if (sourceIndex < 0) return 0;
            long epoch = BeginPreparing(source.NoteId, generation);
            if (epoch == 0) return 0;
            _members.AddRange(members);
            StartFacts = LastFacts = source;
            StartedUtc = startedUtc;
            SplitEligible = sourceIndex > 0;
            Detached = false;
            if (baseline != null) AcceptCapturedFacts(baseline.Values, true);
            _original[source.NoteId] = _current[source.NoteId] = source;
            return epoch;
        }

        internal string[] CopyMemberIds() { return _members.ToArray(); }

        internal void AcceptCapturedFacts(IEnumerable<DockWindowFacts> facts, bool replaceBaseline)
        {
            if (replaceBaseline) { _original.Clear(); _current.Clear(); }
            foreach (DockWindowFacts fact in facts)
            {
                _current[fact.NoteId] = fact;
                if (replaceBaseline) _original[fact.NoteId] = fact;
            }
        }

        internal bool HasMoved(DockWindowFacts facts)
        {
            return LastFacts != null && (facts.X != LastFacts.X || facts.Y != LastFacts.Y);
        }

        internal void RecordMove(DockWindowFacts facts) { LastFacts = facts; }

        internal DockSplitDecision EvaluateSplit(DockWindowFacts facts, DateTime now)
        {
            if (Phase != DockInteractionPhase.Dragging || !SplitEligible || Detached)
                return DockSplitDecision.None;
            double held = (now - StartedUtc).TotalMilliseconds;
            if (StickyDockOperations.CancelsDockSplitHold(held,
                facts.X - StartFacts.X, facts.Y - StartFacts.Y))
            {
                SplitEligible = false;
                return DockSplitDecision.Cancelled;
            }
            return held >= StickyDockOperations.SplitHoldMilliseconds
                ? DockSplitDecision.Detach : DockSplitDecision.None;
        }

        internal void Detach(string remainderNoteId)
        {
            Detached = true;
            SplitEligible = false;
            RemainderNoteId = remainderNoteId;
            _members.Clear();
            _members.Add(SourceNoteId);
        }

        internal void RememberTargets(IEnumerable<DockLayoutTarget> targets)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                _current[target.NoteId] = DockWindowFacts.FromTarget(target);
        }

        internal DockInteractionPhase Phase { get; private set; }
        internal long Epoch { get; private set; }
        internal string SourceNoteId { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal string FinalRemainderNoteId { get { return RemainderNoteId; } }
        internal bool IsActive { get { return Phase != DockInteractionPhase.Idle; } }
        internal bool IsFinalizing { get { return Phase == DockInteractionPhase.Finalizing; } }

        internal long BeginPreparing(string sourceNoteId, long generation)
        {
            if (String.IsNullOrWhiteSpace(sourceNoteId)) throw new ArgumentException("A Dock source note id is required.", nameof(sourceNoteId));
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase != DockInteractionPhase.Idle) return 0;
            Epoch = NextEpoch(); SourceNoteId = sourceNoteId.Trim(); TopologyGeneration = generation;
            RemainderNoteId = String.Empty; Phase = DockInteractionPhase.Preparing; return Epoch;
        }
        internal long BeginRebase(long generation)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase == DockInteractionPhase.Idle || Phase == DockInteractionPhase.Finalizing) return 0;
            Epoch = NextEpoch(); TopologyGeneration = generation;
            SplitEligible = false; Phase = DockInteractionPhase.Rebasing; return Epoch;
        }
        internal bool TryEnterDragging(long epoch, long generation)
        {
            if (Epoch != epoch || TopologyGeneration != generation ||
                (Phase != DockInteractionPhase.Preparing && Phase != DockInteractionPhase.Rebasing)) return false;
            Phase = DockInteractionPhase.Dragging; return true;
        }
        internal long BeginFinalizing(long generation, string remainderNoteId,
            IEnumerable<string> members = null)
        {
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (Phase == DockInteractionPhase.Idle) return 0;
            if (members != null) { _members.Clear(); _members.AddRange(members); }
            Epoch = NextEpoch(); TopologyGeneration = generation;
            RemainderNoteId = remainderNoteId == null ? String.Empty : remainderNoteId.Trim();
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
        {
            Epoch = NextEpoch(); SourceNoteId = RemainderNoteId = String.Empty;
            TopologyGeneration = 0; Phase = DockInteractionPhase.Idle;
            _members.Clear(); _original.Clear(); _current.Clear();
            StartFacts = LastFacts = null; StartedUtc = default(DateTime);
            SplitEligible = Detached = false;
            PendingMerge = null;
            return Epoch;
        }

        // Check and clear together: an old final callback cannot finish a
        // rebased finalization or reset a later mouse gesture.
        internal bool TryFinish(long epoch, long generation, out long invalidatingEpoch)
        {
            invalidatingEpoch = Epoch;
            if (!Matches(epoch, generation, DockInteractionPhase.Finalizing)) return false;
            invalidatingEpoch = Reset();
            return true;
        }
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
