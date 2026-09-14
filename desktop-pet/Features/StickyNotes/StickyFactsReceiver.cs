using System;

namespace PennyPet
{
    // Pet-thread commit boundary for HWND facts. Callers validate their gesture,
    // topology lifetime and complete member set, then prepare every update before
    // committing any. Prepared updates never cross an await or invoke window code.
    internal sealed class StickyFactsReceiver
    {
        private readonly StickyNoteRepository _notes;
        private readonly StickyHostedRuntime _hosted;
        private readonly StickyPlacementRuntime _placement;

        internal StickyFactsReceiver(StickyNoteRepository notes,
            StickyHostedRuntime hosted, StickyPlacementRuntime placement)
        {
            _notes = notes;
            _hosted = hosted;
            _placement = placement;
        }

        internal bool TryPrepare(DockBatchMemberResult member,
            DisplayTopologySnapshot topology, out Update update,
            bool allowSessionCreation = false)
        {
            update = null;
            if (member == null || !_hosted.CanApplyBatchSequence(member, allowSessionCreation)) return false;
            StickyNoteData canonical = _notes.Find(member.NoteId);
            bool created = allowSessionCreation && member.SessionCreated;
            if (canonical == null ||
                (member.Snapshot != null && !String.Equals(member.NoteId,
                    member.Snapshot.NoteId, StringComparison.OrdinalIgnoreCase)) ||
                WindowFactsVersionRules.Classify(member.NoteId, member.WindowSequence,
                    member.Facts, topology, topology == null ? -1 : topology.Generation) !=
                    WindowFactsVersionDisposition.Current ||
                member.Facts.Dpi <= 0 || member.Facts.PhysicalBounds.Width <= 0 ||
                member.Facts.PhysicalBounds.Height <= 0 ||
                (!created && !_placement.CanAcceptEffective(member.NoteId, member.Facts))) return false;
            update = new Update(this, canonical, member, topology, allowSessionCreation);
            return true;
        }

        // Content can be newer even when the attached capture belongs to a
        // disconnected display. Accept that content without reinterpreting facts.
        internal bool TryApplySnapshot(StickyNoteUiSnapshot snapshot, long sequence,
            WindowFacts facts, DisplayTopologySnapshot topology,
            DisplayTopologySnapshot current, out bool tabsChanged)
        {
            tabsChanged = false;
            if (snapshot == null || !_hosted.CanApplySequence(snapshot.NoteId, sequence)) return false;
            StickyNoteData canonical = _notes.Find(snapshot.NoteId);
            if (canonical == null) return false;
            bool visible = canonical.Visible;
            string title = canonical.DisplayTitle;
            snapshot.ApplyContentTo(canonical);
            canonical.Visible = snapshot.Visible;
            canonical.AlwaysOnTop = snapshot.AlwaysOnTop;
            if (WindowFactsVersionRules.Classify(snapshot.NoteId, sequence, facts, topology,
                    current == null ? -1 : current.Generation) == WindowFactsVersionDisposition.Current &&
                _placement.TryUpdateEffective(snapshot.NoteId, facts, topology))
                ApplyGeometry(canonical, facts, topology);
            _hosted.RecordSequence(snapshot.NoteId, sequence);
            tabsChanged = visible != canonical.Visible || (!canonical.Visible &&
                !String.Equals(title, canonical.DisplayTitle, StringComparison.Ordinal));
            return true;
        }

        private static void ApplyGeometry(StickyNoteData canonical,
            WindowFacts facts, DisplayTopologySnapshot topology)
        {
            DisplaySurfaceSnapshot surface = topology.FindByTargetKey(facts.ActiveTargetKey) ??
                topology.FindByRuntimeGdiName(facts.RuntimeGdiName);
            StickyPlacementMath.FromPhysicalRect(surface.RuntimeGdiName,
                surface.Bounds.Left, surface.Bounds.Top, facts.Scale,
                facts.PhysicalBounds.Left, facts.PhysicalBounds.Top,
                facts.PhysicalBounds.Width, facts.PhysicalBounds.Height).ApplyTo(canonical);
        }

        internal sealed class Update
        {
            private readonly StickyFactsReceiver _owner;
            private readonly DisplayTopologySnapshot _topology;
            private readonly bool _allowSessionCreation;
            internal StickyNoteData Canonical { get; private set; }
            internal DockBatchMemberResult Member { get; private set; }

            internal Update(StickyFactsReceiver owner, StickyNoteData canonical,
                DockBatchMemberResult member, DisplayTopologySnapshot topology, bool allowSessionCreation)
            {
                _owner = owner;
                Canonical = canonical;
                Member = member;
                _topology = topology;
                _allowSessionCreation = allowSessionCreation;
            }

            internal void Commit(bool forceVisible = false)
            {
                if (Member.Snapshot != null)
                {
                    Member.Snapshot.ApplyContentTo(Canonical);
                    Canonical.Visible = forceVisible || Member.Snapshot.Visible;
                    Canonical.AlwaysOnTop = Member.Snapshot.AlwaysOnTop;
                }
                CommitGeometry();
            }

            internal void CommitGeometry()
            {
                ApplyGeometry(Canonical, Member.Facts, _topology);
                _owner._placement.AcceptEffective(Member.NoteId, Member.Facts, _topology);
                _owner._hosted.AcceptBatchSequence(Member, _allowSessionCreation);
            }
        }
    }
}
