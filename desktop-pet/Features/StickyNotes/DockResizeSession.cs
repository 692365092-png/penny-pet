using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Physical resize targets: below the source for a divider, every other
    // visible member for a horizontal resize. Windows owns the source HWND.
    internal sealed class DockResizeBatch
    {
        internal DockResizeBatch(
            long topologyGeneration,
            IList<DockWindowTarget> targets, DockInput input = null)
        {
            TopologyGeneration = topologyGeneration;
            Input = input;
            Targets = new List<DockWindowTarget>(
                targets == null
                    ? new DockWindowTarget[0]
                    : targets).AsReadOnly();
        }

        internal long TopologyGeneration { get; private set; }
        internal DockInput Input { get; private set; }
        internal IReadOnlyList<DockWindowTarget> Targets { get; private set; }
    }

    // One Pet-thread resize owner. Mailbox acknowledgment is transport state,
    // not completion of the gesture or its deferred user operations.
    internal sealed class DockResizeSession
    {
        private readonly WindowFacts[] _members;
        private readonly DockRect[] _startBounds;
        private readonly int _sourceIndex;
        private long _lastSourceSequence;
        private bool _finished;
        private bool _corrected;
        private DockResizeBatch _finalBatch;
        private PhysicalRect _finalSource;
        private readonly DockMutationQueue _mutations;

        private DockResizeSession(DockResizeKind kind, WindowFacts[] members, int sourceIndex,
            IEnumerable<StickyNoteData> affectedMembers, DockInput input)
        {
            Kind = kind;
            Input = input;
            _members = members;
            _sourceIndex = sourceIndex;
            WindowFacts source = members[sourceIndex];
            SourceNoteId = source.WindowId;
            TopologyGeneration = source.TopologyGeneration;
            _lastSourceSequence = source.WindowSequence;
            _startBounds = new DockRect[members.Length];
            for (int index = 0; index < members.Length; index++)
            {
                PhysicalRect rect = members[index].PhysicalBounds;
                _startBounds[index] = new DockRect(rect.Left, rect.Top, rect.Width, rect.Height);
            }
            Mailbox = new DockFrameMailbox<DockResizeBatch>();
            _mutations = new DockMutationQueue(new List<WindowFacts>(members).ConvertAll(member => member.WindowId), affectedMembers);
        }

        internal string SourceNoteId { get; private set; }
        internal DockInput Input { get; private set; }
        internal DockResizeKind Kind { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal DockFrameMailbox<DockResizeBatch> Mailbox { get; private set; }
        internal bool IsResizing { get { return !_finished && _finalBatch == null; } }
        internal bool IsFinalizing { get { return !_finished && _finalBatch != null; } }
        internal DockMutationQueue Mutations { get { return IsFinalizing ? _mutations : null; } }
        private int FollowerCount { get { return Kind == DockResizeKind.Divider
            ? _members.Length - _sourceIndex - 1 : _members.Length - 1; } }

        internal static DockResizeSession TryStart(DockResizeKind kind, string sourceId,
            IList<WindowFacts> orderedFacts, IEnumerable<StickyNoteData> affectedMembers = null,
            DockInput input = null)
        {
            if (orderedFacts == null || orderedFacts.Count < 2) return null;
            int sourceIndex = -1;
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long generation = orderedFacts[0] == null ? -1 : orderedFacts[0].TopologyGeneration;
            for (int index = 0; index < orderedFacts.Count; index++)
            {
                WindowFacts facts = orderedFacts[index];
                if (facts == null || String.IsNullOrEmpty(facts.WindowId) || !ids.Add(facts.WindowId) ||
                    facts.TopologyGeneration != generation || facts.PhysicalBounds.Width <= 0 ||
                    facts.PhysicalBounds.Height <= 0) return null;
                if (String.Equals(facts.WindowId, sourceId, StringComparison.OrdinalIgnoreCase)) sourceIndex = index;
            }
            if (sourceIndex < 0 || (kind == DockResizeKind.Divider && sourceIndex == orderedFacts.Count - 1)) return null;
            return new DockResizeSession(kind, new List<WindowFacts>(orderedFacts).ToArray(), sourceIndex, affectedMembers, input);
        }

        internal bool Contains(string noteId)
        {
            foreach (WindowFacts member in _members)
                if (String.Equals(member.WindowId, noteId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal bool MatchesMembers(IList<StickyNoteData> ordered)
        {
            if (_finished || ordered == null || ordered.Count != _members.Length) return false;
            for (int index = 0; index < ordered.Count; index++)
                if (ordered[index] == null || !ordered[index].Visible ||
                    !String.Equals(ordered[index].Id, _members[index].WindowId, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private bool MatchesSource(StickyUiEvent value)
        {
            return IsResizing && value != null && value.Facts != null &&
                ReferenceEquals(value.Input, Input) &&
                String.Equals(value.NoteId, SourceNoteId, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(value.Facts.WindowId, SourceNoteId, StringComparison.OrdinalIgnoreCase) &&
                value.Sequence == value.Facts.WindowSequence && value.Facts.TopologyGeneration == TopologyGeneration;
        }

        internal bool QueueLive(StickyUiEvent value, out bool post)
        {
            post = false;
            if (!MatchesSource(value) || value.Sequence <= _lastSourceSequence) return false;
            DockResizeBatch batch;
            if (Kind == DockResizeKind.Horizontal)
            {
                if (value.Kind != StickyUiEventKind.DockHorizontalResizing || value.Width <= 0) return false;
                batch = HorizontalBatch(value.Left, value.Width);
            }
            else
            {
                if (value.Kind != StickyUiEventKind.DockDividerResizing || value.Height <= 0) return false;
                batch = BatchFromLayout(StickyDockGeometry.CalculateDockMemberResizeTargetsExact(
                    _startBounds, _sourceIndex, value.Height));
            }
            _lastSourceSequence = value.Sequence;
            post = Mailbox.QueueLive(batch);
            return true;
        }

        // Equality permits completion-only recovery from current-generation
        // facts after a topology barrier, including the final source itself.
        internal DockResizeBatch BeginFinal(StickyUiEvent value)
        {
            if (!MatchesSource(value) || value.Sequence < _lastSourceSequence ||
                value.Facts.PhysicalBounds.Width <= 0 || value.Facts.PhysicalBounds.Height <= 0) return null;
            if (value.Kind != (Kind == DockResizeKind.Horizontal ? StickyUiEventKind.DockHorizontalResizeCompleted :
                StickyUiEventKind.DockDividerResizeCompleted)) return null;
            _lastSourceSequence = value.Sequence;
            _finalSource = value.Facts.PhysicalBounds;
            _finalBatch = Kind == DockResizeKind.Horizontal ? HorizontalBatch(_finalSource.Left, _finalSource.Width)
                : BatchFromLayout(StickyDockGeometry.CalculateDockDividerFinalTargets(
                    _startBounds, _sourceIndex, _finalSource.Top, _finalSource.Height));
            Mailbox.QueueFinal(_finalBatch);
            return _finalBatch;
        }

        private DockResizeBatch BatchFromLayout(IList<DockRect> layout)
        {
            List<DockWindowTarget> targets = new List<DockWindowTarget>(layout.Count);
            for (int index = 0; index < layout.Count; index++)
            {
                DockRect rect = layout[index];
                targets.Add(new DockWindowTarget(_members[_sourceIndex + index + 1].WindowId,
                    new PhysicalRect(rect.Left, rect.Top, rect.Width, rect.Height)));
            }
            return new DockResizeBatch(TopologyGeneration, targets, Input);
        }

        private int FollowerIndex(int index)
        { return Kind == DockResizeKind.Divider ? _sourceIndex + index + 1 : index < _sourceIndex ? index : index + 1; }

        private DockResizeBatch HorizontalBatch(int left, int width)
        {
            List<DockWindowTarget> targets = new List<DockWindowTarget>(FollowerCount);
            for (int index = 0; index < FollowerCount; index++)
            {
                WindowFacts facts = _members[FollowerIndex(index)];
                targets.Add(new DockWindowTarget(facts.WindowId,
                    new PhysicalRect(left, facts.PhysicalBounds.Top, width, facts.PhysicalBounds.Height)));
            }
            return new DockResizeBatch(TopologyGeneration, targets, Input);
        }

        internal bool IsCurrentFinal(DockResizeBatch batch)
        { return !_finished && batch != null && ReferenceEquals(_finalBatch, batch); }

        // Verify identities before using any returned rectangle for correction.
        internal bool HasExpectedFollowers(DockBatchResult batch)
        {
            if (_finished || batch == null || batch.TopologyGeneration != TopologyGeneration ||
                batch.Members.Count != FollowerCount) return false;
            for (int index = 0; index < batch.Members.Count; index++)
            {
                DockBatchMemberResult member = batch.Members[index];
                if (member == null || member.Facts == null ||
                    !String.Equals(member.NoteId, _members[FollowerIndex(index)].WindowId, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(member.NoteId, member.Facts.WindowId, StringComparison.OrdinalIgnoreCase) ||
                    member.WindowSequence != member.Facts.WindowSequence || member.Facts.TopologyGeneration != TopologyGeneration ||
                    member.Facts.PhysicalBounds.Width <= 0 || member.Facts.PhysicalBounds.Height <= 0) return false;
            }
            return true;
        }

        internal bool LayoutIsExact(DockBatchResult batch)
        {
            if (_finalBatch == null || !HasExpectedFollowers(batch)) return false;
            if (Kind == DockResizeKind.Horizontal)
            {
                foreach (DockBatchMemberResult member in batch.Members)
                    if (Math.Abs((long)member.Facts.PhysicalBounds.Left - _finalSource.Left) > 2 ||
                        Math.Abs((long)member.Facts.PhysicalBounds.Width - _finalSource.Width) > 2) return false;
                return true;
            }
            List<PhysicalRect> rects = new List<PhysicalRect>(batch.Members.Count);
            foreach (DockBatchMemberResult member in batch.Members) rects.Add(member.Facts.PhysicalBounds);
            return StickyDockGeometry.DividerStackSeamIsExact(_finalSource.Top, _finalSource.Height, rects, 2);
        }

        internal DockResizeBatch TryCorrect(DockResizeBatch expected, DockBatchResult actual)
        {
            if (!IsCurrentFinal(expected) || _corrected || !HasExpectedFollowers(actual) || LayoutIsExact(actual)) return null;
            List<DockWindowTarget> targets = new List<DockWindowTarget>(actual.Members.Count);
            int bottom = _finalSource.Bottom;
            foreach (DockBatchMemberResult member in actual.Members)
            {
                PhysicalRect rect = member.Facts.PhysicalBounds;
                targets.Add(new DockWindowTarget(member.NoteId, Kind == DockResizeKind.Horizontal
                    ? new PhysicalRect(_finalSource.Left, rect.Top, _finalSource.Width, rect.Height)
                    : new PhysicalRect(rect.Left, bottom, rect.Width, rect.Height)));
                bottom += rect.Height;
            }
            _corrected = true;
            _finalBatch = new DockResizeBatch(TopologyGeneration, targets, Input);
            Mailbox.QueueFinal(_finalBatch);
            return _finalBatch;
        }

        internal Action[] Finish()
        {
            _finished = true;
            Mailbox.Cancel();
            return _mutations.Release();
        }
    }
}
