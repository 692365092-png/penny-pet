using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Pet-thread owner of one native divider gesture. The mailbox transports
    // effects only; finishing a host apply does not finish this gesture.
    internal sealed class DockDividerResizeSession
    {
        private readonly WindowFacts[] _members;
        private readonly DockRect[] _startBounds;
        private readonly int _sourceIndex;
        private long _lastSourceSequence;
        private bool _finished;
        private bool _corrected;
        private DockDividerFollowerBatch _finalBatch;
        private PhysicalRect _finalSource;

        private DockDividerResizeSession(WindowFacts[] members, int sourceIndex)
        {
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
            Mailbox = new DockDividerFollowerMailbox();
        }

        internal string SourceNoteId { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal DockDividerFollowerMailbox Mailbox { get; private set; }
        internal bool IsResizing { get { return !_finished && _finalBatch == null; } }

        internal static DockDividerResizeSession TryStart(string sourceId,
            IList<WindowFacts> orderedFacts)
        {
            if (orderedFacts == null || orderedFacts.Count < 2) return null;
            int sourceIndex = -1;
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long generation = orderedFacts[0] == null ? -1 : orderedFacts[0].TopologyGeneration;
            for (int index = 0; index < orderedFacts.Count; index++)
            {
                WindowFacts facts = orderedFacts[index];
                if (facts == null || String.IsNullOrEmpty(facts.WindowId) ||
                    !ids.Add(facts.WindowId) || facts.TopologyGeneration != generation ||
                    facts.PhysicalBounds.Width <= 0 || facts.PhysicalBounds.Height <= 0) return null;
                if (String.Equals(facts.WindowId, sourceId, StringComparison.OrdinalIgnoreCase))
                    sourceIndex = index;
            }
            if (sourceIndex < 0 || sourceIndex == orderedFacts.Count - 1) return null;
            return new DockDividerResizeSession(new List<WindowFacts>(orderedFacts).ToArray(), sourceIndex);
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
                    !String.Equals(ordered[index].Id, _members[index].WindowId,
                        StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private bool MatchesSource(StickyUiEvent value)
        {
            return IsResizing && value != null && value.Facts != null &&
                String.Equals(value.NoteId, SourceNoteId, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(value.Facts.WindowId, SourceNoteId, StringComparison.OrdinalIgnoreCase) &&
                value.Sequence == value.Facts.WindowSequence &&
                value.Facts.TopologyGeneration == TopologyGeneration;
        }

        internal bool QueueLive(StickyUiEvent value, out bool post)
        {
            post = false;
            if (!MatchesSource(value) || value.Sequence <= _lastSourceSequence || value.Height <= 0)
                return false;
            List<DockRect> layout = StickyDockGeometry.CalculateDockMemberResizeTargetsExact(
                _startBounds, _sourceIndex, value.Height);
            _lastSourceSequence = value.Sequence;
            post = Mailbox.QueueLive(BatchFromLayout(layout));
            return true;
        }

        // Equality permits a completion-only recovery after a topology barrier:
        // all baselines then come from current facts, including the final source.
        internal DockDividerFollowerBatch BeginFinal(StickyUiEvent value)
        {
            if (!MatchesSource(value) || value.Sequence < _lastSourceSequence ||
                value.Facts.PhysicalBounds.Width <= 0 || value.Facts.PhysicalBounds.Height <= 0) return null;
            _lastSourceSequence = value.Sequence;
            _finalSource = value.Facts.PhysicalBounds;
            _finalBatch = BatchFromLayout(StickyDockGeometry.CalculateDockDividerFinalTargets(
                _startBounds, _sourceIndex, _finalSource.Top, _finalSource.Height));
            Mailbox.QueueFinal(_finalBatch);
            return _finalBatch;
        }

        private DockDividerFollowerBatch BatchFromLayout(IList<DockRect> layout)
        {
            List<DockWindowTarget> targets = new List<DockWindowTarget>(layout.Count);
            for (int index = 0; index < layout.Count; index++)
            {
                DockRect rect = layout[index];
                targets.Add(new DockWindowTarget(_members[_sourceIndex + index + 1].WindowId,
                    new PhysicalRect(rect.Left, rect.Top, rect.Width, rect.Height)));
            }
            return new DockDividerFollowerBatch(TopologyGeneration, targets);
        }

        internal bool IsCurrentFinal(DockDividerFollowerBatch batch)
        {
            return !_finished && batch != null && ReferenceEquals(_finalBatch, batch);
        }

        // Validate the whole returned membership before using even its rects
        // for a correction. A reordered/partial response cannot move another note.
        internal bool HasExpectedFollowers(DockBatchResult batch)
        {
            if (_finished || batch == null || batch.TopologyGeneration != TopologyGeneration ||
                batch.Members.Count != _members.Length - _sourceIndex - 1) return false;
            for (int index = 0; index < batch.Members.Count; index++)
            {
                DockBatchMemberResult member = batch.Members[index];
                if (member == null || member.Facts == null ||
                    !String.Equals(member.NoteId, _members[_sourceIndex + index + 1].WindowId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(member.NoteId, member.Facts.WindowId, StringComparison.OrdinalIgnoreCase) ||
                    member.WindowSequence != member.Facts.WindowSequence ||
                    member.Facts.TopologyGeneration != TopologyGeneration ||
                    member.Facts.PhysicalBounds.Width <= 0 || member.Facts.PhysicalBounds.Height <= 0) return false;
            }
            return true;
        }

        internal bool SeamIsExact(DockBatchResult batch)
        {
            if (_finalBatch == null || !HasExpectedFollowers(batch)) return false;
            List<PhysicalRect> rects = new List<PhysicalRect>(batch.Members.Count);
            foreach (DockBatchMemberResult member in batch.Members) rects.Add(member.Facts.PhysicalBounds);
            return StickyDockGeometry.DividerStackSeamIsExact(_finalSource.Top, _finalSource.Height, rects, 2);
        }

        internal DockDividerFollowerBatch TryCorrect(DockDividerFollowerBatch expected,
            DockBatchResult actual)
        {
            if (!IsCurrentFinal(expected) || _corrected || !HasExpectedFollowers(actual) || SeamIsExact(actual))
                return null;
            List<DockWindowTarget> targets = new List<DockWindowTarget>(actual.Members.Count);
            int bottom = _finalSource.Bottom;
            foreach (DockBatchMemberResult member in actual.Members)
            {
                PhysicalRect rect = member.Facts.PhysicalBounds;
                targets.Add(new DockWindowTarget(member.NoteId,
                    new PhysicalRect(rect.Left, bottom, rect.Width, rect.Height)));
                bottom += rect.Height;
            }
            _corrected = true;
            _finalBatch = new DockDividerFollowerBatch(TopologyGeneration, targets);
            Mailbox.QueueFinal(_finalBatch);
            return _finalBatch;
        }

        internal void Finish()
        {
            _finished = true;
            Mailbox.Cancel();
        }
    }
}
