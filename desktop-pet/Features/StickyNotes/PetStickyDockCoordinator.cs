using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    // HIGH RISK: persisted Dock group snapshots, parent-child links, drag
    // splitting, insertion and layout synchronization. Keep these algorithms
    // behavior-identical and run full Dock regression tests after changes.
    internal sealed partial class PetForm
    {
        private void ApplyDockComponentTopMost(StickyNoteData seed,
            bool alwaysOnTop, string alreadyAppliedNoteId)
        {
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(seed);
            if (component.Count == 0) component = BuildDockComponent(seed);
            foreach (StickyNoteData note in component)
            {
                note.AlwaysOnTop = alwaysOnTop;
                if (String.Equals(note.Id, alreadyAppliedNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsHostedSticky(note)) continue;
                PostHostedStickyCommand(StickyUiCommand.SetTopMost(
                    note.Id, alwaysOnTop),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, false);
                        else ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-topmost", result);
                    });
            }
        }

        private void CloseStickyDockNote(StickyNoteData sourceData,
            DockWindowFacts sourceFacts)
        {
            if (sourceData == null || sourceFacts == null) return;
            List<StickyNoteData> ordered =
                BuildAuthoritativeVisibleDockOrder(sourceData);
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(sourceData);
            snapshot = StickyDockOperations.SelectMoreCompleteDockOrder(
                ordered, snapshot);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, sourceData.Id,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (StickyDockOperations.ShouldCollapseWholeDockGroup(
                sourceIndex, ordered.Count))
            {
                // The top header is the group-level close handle. Preserve the
                // links so expanding all side tabs restores the same stack.
                StickyDockGroups.ApplyGroupSnapshot(snapshot);
                foreach (StickyNoteData note in snapshot)
                {
                    note.Visible = false;
                    PostHostedStickyHide(note);
                }
                StickyDockGroups.RebuildVisibleParentChain(snapshot);
            }
            else
            {
                // A lower X temporarily hides exactly that member.  Its group
                // identity and slot remain in the snapshot, while the live
                // visible parent chain skips across the hidden window.
                Dictionary<string, DockWindowFacts> facts =
                    CaptureDockFacts(snapshot);
                facts[sourceFacts.NoteId] = sourceFacts;
                DockWindowFacts rootFacts = sourceFacts;
                DockWindowFacts capturedRoot;
                if (ordered.Count > 0 && facts.TryGetValue(ordered[0].Id,
                    out capturedRoot)) rootFacts = capturedRoot;
                sourceData.Visible = false;
                PostHostedStickyHide(sourceData);
                StickyDockOperations.PreserveDockSlotForHiddenMember(
                    snapshot, sourceData);
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(snapshot, facts, rootFacts.X,
                        rootFacts.Y, rootFacts.Width);
                }
                finally { _synchronizingDockLayout = false; }
            }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void BeginStickyDockDrag(DockWindowFacts facts)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            ClearDockPreview();
            ClearSplitGuide();
            _activeNoteDragId = facts.NoteId;
            _activeNoteDragStartFacts = facts;
            _activeNoteDragLastFacts = facts;
            _activeNoteDragStartedUtc = DateTime.UtcNow;
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderNoteId = null;
            SetActiveDockGroup(BuildDockComponent(seed));
            _activeDockOriginalFacts.Clear();
            _activeDockCurrentFacts.Clear();
            Dictionary<string, DockWindowFacts> groupFacts =
                CaptureDockFacts(_activeDockGroupIds);
            groupFacts[facts.NoteId] = facts;
            foreach (KeyValuePair<string, DockWindowFacts> item in groupFacts)
            {
                _activeDockOriginalFacts[item.Key] = item.Value;
                _activeDockCurrentFacts[item.Key] = item.Value;
            }
            // The root header is the one unambiguous handle for moving the
            // whole stack.  Only a member that has a parent can be pulled out
            // after a deliberate hold.
            _activeNoteSplitEligible = StickyDockOperations
                .IsDockSplitEligible(seed.DockParentId,
                    _activeDockGroupIds.Count);
            if (_activeNoteSplitEligible)
                ShowSplitGuide(seed, groupFacts);
        }

        private void MoveStickyDockDrag(DockWindowFacts facts)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            if (_movingDockGroup ||
                !String.Equals(facts.NoteId, _activeNoteDragId,
                    StringComparison.OrdinalIgnoreCase)) return;
            int dx = facts.X - _activeNoteDragLastFacts.X;
            int dy = facts.Y - _activeNoteDragLastFacts.Y;
            if (dx == 0 && dy == 0) return;

            TimeSpan held = DateTime.UtcNow - _activeNoteDragStartedUtc;
            int totalDx = facts.X - _activeNoteDragStartFacts.X;
            int totalDy = facts.Y - _activeNoteDragStartFacts.Y;
            if (!_activeNoteDetached && _activeNoteSplitEligible &&
                StickyDockOperations.CancelsDockSplitHold(
                    held.TotalMilliseconds,
                    totalDx, totalDy))
            {
                // A drag that starts moving immediately means "move the
                // group".  Only a deliberate stationary hold may split it.
                _activeNoteSplitEligible = false;
                ClearSplitGuide();
            }

            if (!_activeNoteDetached && _activeNoteSplitEligible &&
                held.TotalMilliseconds >=
                    StickyDockOperations.SplitHoldMilliseconds)
            {
                if (!String.IsNullOrEmpty(seed.DockParentId))
                {
                    string connectedNoteId = seed.DockParentId;
                    List<StickyNoteData> beforeSplit =
                        BuildDockChainOrderIncludingHidden(seed);
                    int splitIndex = beforeSplit.FindIndex(
                        delegate(StickyNoteData note)
                        {
                            return String.Equals(note.Id, facts.NoteId,
                                StringComparison.OrdinalIgnoreCase);
                        });
                    if (splitIndex > 0)
                    {
                        // A held middle header extracts exactly that note.
                        // Reconnect the members before and after it into one
                        // stack instead of detaching every descendant.
                        StickyDockOperations.ExtractSingleDockMember(
                            beforeSplit, seed);
                    }
                    else StickyDockGroups.ClearMembership(seed);
                    _splitRemainderNoteId = connectedNoteId;
                    _activeNoteDetached = true;
                }
                if (_activeNoteDetached)
                {
                    ClearSplitGuide();
                    RestoreDockOriginalLocations(_splitRemainderNoteId);
                    StickyNoteData remainder =
                        _notes.Find(_splitRemainderNoteId);
                    if (remainder != null)
                        NormalizeDockComponent(remainder);
                    SetActiveDockGroup(BuildDockComponent(seed));
                    RefreshDockResizeRoles();
                }
            }

            List<DockLayoutTarget> moveTargets = CalculateDockTranslationTargets(
                _activeDockGroupIds, _activeDockCurrentFacts, facts,
                dx, dy);
            _movingDockGroup = true;
            try { ApplyDockTargets(moveTargets, facts.NoteId); }
            finally { _movingDockGroup = false; }
            RememberActiveDockFacts(moveTargets);
            _activeNoteDragLastFacts = facts;
            if (!_activeNoteDetached && _activeNoteSplitEligible)
                UpdateSplitGuide(seed, _activeDockCurrentFacts);
            Dictionary<string, DockWindowFacts> previewFacts =
                CaptureDockFacts(_notes.GetAll());
            previewFacts[facts.NoteId] = facts;
            UpdateDockPreview(seed, previewFacts);
        }

        private void CompleteStickyDockDrag(DockWindowFacts facts)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            if (!String.Equals(facts.NoteId, _activeNoteDragId,
                StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, DockWindowFacts> currentFacts =
                CaptureDockFacts(_notes.GetAll());
            currentFacts[facts.NoteId] = facts;
            ApplyDockTarget(facts.ToTarget(facts.X, facts.Y), facts.NoteId);
            DockTarget target = FindDockTarget(seed, currentFacts);
            if (target != null &&
                !String.IsNullOrEmpty(target.ParentNoteId))
            {
                StickyNoteData parent = _notes.Find(target.ParentNoteId);
                if (parent == null) target = null;
            }
            if (target != null &&
                !String.IsNullOrEmpty(target.ParentNoteId))
            {
                StickyNoteData parent = _notes.Find(target.ParentNoteId);
                List<StickyNoteData> targetOrder = BuildDockChainOrder(
                    parent);
                List<StickyNoteData> targetSnapshot =
                    BuildDockChainOrderIncludingHidden(parent);
                List<StickyNoteData> sourceSnapshot =
                    BuildDockChainOrderIncludingHidden(seed);
                StickyNoteData targetRoot = targetOrder.Count == 0 ? parent :
                    targetOrder[0];
                DockWindowFacts targetRootFacts;
                if (!currentFacts.TryGetValue(targetRoot.Id,
                    out targetRootFacts))
                    targetRootFacts = DockWindowFacts.FromData(targetRoot);
                StickyNoteData tail = FindActiveDockTail(seed);
                StickyNoteData tailData = tail ?? seed;
                List<StickyNoteData> mergedSnapshot =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        targetSnapshot, parent, sourceSnapshot);
                bool groupTopMost = targetSnapshot.Count == 0
                    ? parent.AlwaysOnTop : targetSnapshot[0].AlwaysOnTop;
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(mergedSnapshot, currentFacts,
                        targetRootFacts.X, targetRootFacts.Y,
                        targetRootFacts.Width);
                    ApplyDockComponentTopMost(parent, groupTopMost, null);
                }
                finally { _synchronizingDockLayout = false; }
                StickyNoteData existingChild =
                    _notes.Find(target.ExistingChildNoteId);
                if (existingChild != null)
                    ShowTransientDockPulse(CalculateDockVisualSeam(
                        DockWindowFacts.FromData(tailData)),
                        Color.FromArgb(32, 160, 255));
                ShowTransientDockPulse(CalculateDockVisualSeam(
                    DockWindowFacts.FromData(parent)),
                    Color.FromArgb(32, 160, 255));
            }
            CommitVisibleDockOrder(seed);
            StickyNoteData remainderSeed =
                _notes.Find(_splitRemainderNoteId);
            if (remainderSeed != null)
                CommitVisibleDockOrder(remainderSeed);
            ClearDockPreview();
            ClearSplitGuide();
            RefreshDockResizeRoles();
            _notes.Save();
            _activeNoteDragId = null;
            _activeDockGroupIds.Clear();
            _activeDockOriginalFacts.Clear();
            _activeDockCurrentFacts.Clear();
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderNoteId = null;
        }

        private void SetActiveDockGroup(List<StickyNoteData> notes)
        {
            _activeDockGroupIds.Clear();
            if (notes == null) return;
            foreach (StickyNoteData note in notes)
                if (note != null) _activeDockGroupIds.Add(note.Id);
        }

        private Dictionary<string, DockWindowFacts>
            CaptureDockFacts(IEnumerable<string> noteIds)
        {
            Dictionary<string, DockWindowFacts> facts =
                new Dictionary<string, DockWindowFacts>(
                    StringComparer.OrdinalIgnoreCase);
            if (noteIds == null) return facts;
            foreach (string noteId in noteIds)
            {
                StickyNoteData note = _notes.Find(noteId);
                if (note == null) continue;
                facts[noteId] = DockWindowFacts.FromData(note);
            }
            return facts;
        }

        private Dictionary<string, DockWindowFacts>
            CaptureDockFacts(IEnumerable<StickyNoteData> notes)
        {
            List<string> noteIds = new List<string>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null) noteIds.Add(note.Id);
            return CaptureDockFacts(noteIds);
        }

        internal static List<DockLayoutTarget> CalculateDockTranslationTargets(
            IList<string> noteIds,
            IDictionary<string, DockWindowFacts> currentFacts,
            DockWindowFacts movedSource, int dx, int dy)
        {
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (noteIds == null || currentFacts == null) return targets;
            foreach (string noteId in noteIds)
            {
                DockWindowFacts facts;
                if (movedSource != null && String.Equals(noteId,
                    movedSource.NoteId, StringComparison.OrdinalIgnoreCase))
                    facts = movedSource;
                else if (!currentFacts.TryGetValue(noteId, out facts))
                    continue;
                if (facts == null || !facts.Visible) continue;
                targets.Add(facts.ToTarget(
                    movedSource != null && String.Equals(noteId,
                        movedSource.NoteId,
                        StringComparison.OrdinalIgnoreCase)
                        ? facts.X : facts.X + dx,
                    movedSource != null && String.Equals(noteId,
                        movedSource.NoteId,
                        StringComparison.OrdinalIgnoreCase)
                        ? facts.Y : facts.Y + dy));
            }
            return targets;
        }

        private void RememberActiveDockFacts(
            IEnumerable<DockLayoutTarget> targets)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                _activeDockCurrentFacts[target.NoteId] =
                    DockWindowFacts.FromTarget(target);
        }

        private List<StickyNoteData> BuildDockChainOrder(StickyNoteData seed)
        {
            return BuildDockChainOrder(seed, true);
        }

        private List<StickyNoteData> BuildDockChainOrderIncludingHidden(
            StickyNoteData seed)
        {
            return StickyDockGroups.GetOrderedGroup(_notes.GetAll(), seed);
        }

        private List<StickyNoteData> BuildDockChainOrder(StickyNoteData seed,
            bool visibleOnly)
        {
            return StickyDockOperations.BuildDockChainOrderFromNotes(
                _notes.GetAll(), seed, visibleOnly);
        }

        private List<StickyNoteData> BuildAuthoritativeVisibleDockOrder(
            StickyNoteData seed)
        {
            List<StickyNoteData> live = BuildDockChainOrder(seed);
            List<StickyNoteData> stored =
                BuildDockChainOrderIncludingHidden(seed);
            stored.RemoveAll(delegate(StickyNoteData note)
            {
                return !note.Visible;
            });
            return StickyDockOperations.SelectMoreCompleteDockOrder(
                live, stored);
        }

        private void CommitVisibleDockOrder(StickyNoteData seed)
        {
            if (seed == null) return;
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(seed);
            if (snapshot.Count > 1)
            {
                StickyDockGroups.ApplyGroupSnapshot(snapshot);
                StickyDockGroups.RebuildVisibleParentChain(snapshot);
                return;
            }
            StickyDockGroups.ApplyOrderedGroup(
                BuildDockChainOrder(seed));
        }

        private void LayoutDockChain(List<StickyNoteData> ordered,
            IDictionary<string, DockWindowFacts> factsById,
            int left, int top, int width,
            string alreadyAppliedNoteId = null)
        {
            List<DockWindowFacts> visibleFacts =
                new List<DockWindowFacts>();
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible) continue;
                DockWindowFacts facts;
                if (factsById == null ||
                    !factsById.TryGetValue(note.Id, out facts))
                    facts = DockWindowFacts.FromData(note);
                visibleFacts.Add(facts);
                sizes.Add(new Size(facts.Width, facts.Height));
            }
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                left, top, width);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            for (int index = 0; index < visibleFacts.Count; index++)
            {
                DockWindowFacts facts = visibleFacts[index];
                targets.Add(new DockLayoutTarget(facts.NoteId,
                    layout[index].Left, layout[index].Top,
                    layout[index].Width, layout[index].Height,
                    true, facts.TopMost));
            }
            ApplyDockTargets(targets, alreadyAppliedNoteId);
        }

        // Hosted effect edge: canonical data is updated first; this boundary
        // schedules the typed Window effect on the Sticky STA.
        private void ApplyDockTargets(IEnumerable<DockLayoutTarget> targets,
            string alreadyAppliedNoteId)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                ApplyDockTarget(target, alreadyAppliedNoteId);
        }

        private void ApplyDockTarget(DockLayoutTarget target,
            string alreadyAppliedNoteId)
        {
            if (target == null) return;
            // ponytail: hosted effects are best-effort after canonical update;
            // add rollback only if real command failures justify it.
            StickyNoteData note = _notes.Find(target.NoteId);
            if (note == null) return;
            note.X = target.X;
            note.Y = target.Y;
            note.Width = target.Width;
            note.Height = target.Height;
            note.Visible = target.Visible;
            note.AlwaysOnTop = target.TopMost;
            if (String.Equals(target.NoteId, alreadyAppliedNoteId,
                StringComparison.OrdinalIgnoreCase)) return;
            if (!IsHostedSticky(note)) return;
            PostHostedStickyCommand(StickyUiCommand.SetBounds(
                target.NoteId, new StickyUiBounds(target.X, target.Y,
                    target.Width, target.Height)),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled)
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence, false);
                    else
                    {
                        ClearHostedDockResizeSessionIfMember(target.NoteId);
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-bounds", result);
                    }
                });
        }

        internal static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width)
        {
            return CalculateUnifiedDockLayout(sizes, left, top, width, 1F);
        }

        private static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width, float scale)
        {
            List<DockSize> dockSizes = new List<DockSize>();
            if (sizes != null)
            {
                foreach (Size size in sizes)
                    dockSizes.Add(new DockSize
                    {
                        Width = size.Width,
                        Height = size.Height
                    });
            }
            List<DockRect> dockLayout =
                StickyDockGeometry.CalculateUnifiedDockLayout(dockSizes,
                    left, top, width, scale);
            List<Rectangle> result = new List<Rectangle>();
            foreach (DockRect item in dockLayout)
                result.Add(new Rectangle(item.Left, item.Top,
                    item.Width, item.Height));
            return result;
        }

        private bool CanSafelyCombineDockComponents(DockTarget target,
            StickyNoteData sourceSeed,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (target == null || String.IsNullOrEmpty(target.ParentNoteId) ||
                sourceSeed == null)
                return false;
            StickyNoteData parent = _notes.Find(target.ParentNoteId);
            if (parent == null) return false;
            List<StickyNoteData> targetOrder = BuildDockChainOrder(
                parent);
            if (targetOrder.Count == 0) return false;
            List<int> heights = new List<int>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in targetOrder)
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                {
                    DockWindowFacts facts;
                    heights.Add(factsById != null &&
                        factsById.TryGetValue(note.Id, out facts)
                            ? facts.Height : note.Height);
                }
            }
            foreach (StickyNoteData note in BuildDockChainOrder(sourceSeed))
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                {
                    DockWindowFacts facts;
                    heights.Add(factsById != null &&
                        factsById.TryGetValue(note.Id, out facts)
                            ? facts.Height : note.Height);
                }
            }
            DockWindowFacts rootFacts;
            return StickyDockOperations.IsDockCoordinateRangeSafe(
                factsById != null && factsById.TryGetValue(
                    targetOrder[0].Id, out rootFacts)
                    ? rootFacts.Y : targetOrder[0].Y,
                heights, DockCoordinateSafetyLimit);
        }

        private void NormalizeDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(ordered);
            DockWindowFacts root;
            if (!facts.TryGetValue(ordered[0].Id, out root)) return;
            NormalizeDockComponentAt(seed, facts,
                new Point(root.X, root.Y), root.Width);
        }

        private void NormalizeDockComponentAt(StickyNoteData seed,
            IDictionary<string, DockWindowFacts> factsById,
            Point rootAnchor, int rootWidth)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, factsById, rootAnchor.X,
                    rootAnchor.Y, rootWidth);
                ApplyDockComponentTopMost(seed,
                    ordered[0].AlwaysOnTop, null);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void NormalizeAllDockGroups()
        {
            HashSet<string> normalized = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || normalized.Contains(note.Id)) continue;
                List<StickyNoteData> ordered = BuildDockChainOrder(note);
                if (ordered.Count > 1)
                {
                    NormalizeDockComponent(note);
                    foreach (StickyNoteData member in ordered)
                        normalized.Add(member.Id);
                }
            }
            RefreshDockResizeRoles();
        }

        private bool BeginHostedStickyDockDivider(DockWindowFacts snapshot)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || snapshot == null) return false;
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return false;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, snapshot.NoteId,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (sourceIndex < 0 || sourceIndex >= ordered.Count - 1)
                return false;
            List<DockWindowFacts> startFacts =
                new List<DockWindowFacts>();
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible) continue;
                startFacts.Add(String.Equals(note.Id, snapshot.NoteId,
                    StringComparison.OrdinalIgnoreCase)
                    ? snapshot : DockWindowFacts.FromData(note));
            }
            if (startFacts.Count != ordered.Count) return false;
            _activeHostedDockResizeSourceId = snapshot.NoteId;
            _activeHostedDockResizeFacts = startFacts;
            return true;
        }

        private bool ResizeHostedStickyDockDivider(string sourceNoteId,
            int requestedHeight)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null ||
                _activeHostedDockResizeFacts == null ||
                !String.Equals(_activeHostedDockResizeSourceId,
                    sourceNoteId, StringComparison.OrdinalIgnoreCase))
                return false;
            StickyNoteData source = _notes.Find(sourceNoteId);
            if (source == null) return false;
            List<StickyNoteData> ordered = BuildDockChainOrder(source);
            if (!MatchesHostedDockResizeSession(ordered)) return false;
            int sourceHeight;
            List<DockLayoutTarget> targets =
                CalculateDockMemberResizeTargets(
                    _activeHostedDockResizeFacts, sourceNoteId,
                    requestedHeight, out sourceHeight);
            source.Height = sourceHeight;
            List<DockLayoutTarget> changed = new List<DockLayoutTarget>();
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note = _notes.Find(target.NoteId);
                if (note == null) return false;
                if (note.X != target.X || note.Y != target.Y ||
                    note.Width != target.Width || note.Height != target.Height ||
                    note.Visible != target.Visible ||
                    note.AlwaysOnTop != target.TopMost)
                    changed.Add(target);
            }
            _synchronizingDockLayout = true;
            try
            {
                ApplyDockTargets(changed, sourceNoteId);
            }
            finally { _synchronizingDockLayout = false; }
            return true;
        }

        private bool MatchesHostedDockResizeSession(
            List<StickyNoteData> ordered)
        {
            if (ordered == null || _activeHostedDockResizeFacts == null ||
                ordered.Count != _activeHostedDockResizeFacts.Count)
                return false;
            for (int index = 0; index < ordered.Count; index++)
                if (ordered[index] == null || !ordered[index].Visible ||
                    !String.Equals(ordered[index].Id,
                        _activeHostedDockResizeFacts[index].NoteId,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        internal static List<DockLayoutTarget> CalculateDockDividerTargets(
            DockWindowFacts upper, DockWindowFacts lower)
        {
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (upper == null || lower == null) return targets;
            int upperHeight = CalculateDockDividerHeight(upper.Height);
            targets.Add(new DockLayoutTarget(upper.NoteId, upper.X, upper.Y,
                upper.Width, upperHeight, upper.Visible, upper.TopMost));
            targets.Add(new DockLayoutTarget(lower.NoteId, lower.X,
                upper.Y + upperHeight, lower.Width, lower.Height,
                lower.Visible, lower.TopMost));
            return targets;
        }

        internal static List<DockLayoutTarget>
            CalculateDockMemberResizeTargets(
                IList<DockWindowFacts> startFacts, string sourceNoteId,
                int requestedSourceHeight, out int sourceHeight)
        {
            sourceHeight = CalculateDockDividerHeight(requestedSourceHeight);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (startFacts == null) return targets;
            int sourceIndex = -1;
            List<DockRect> startBounds = new List<DockRect>();
            for (int index = 0; index < startFacts.Count; index++)
            {
                DockWindowFacts facts = startFacts[index];
                if (facts == null) return new List<DockLayoutTarget>();
                startBounds.Add(new DockRect(facts.X, facts.Y,
                    facts.Width, facts.Height));
                if (String.Equals(facts.NoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) sourceIndex = index;
            }
            List<DockRect> layout =
                StickyDockGeometry.CalculateDockMemberResizeTargets(
                    startBounds, sourceIndex, requestedSourceHeight,
                    out sourceHeight);
            for (int index = 0; index < layout.Count; index++)
            {
                DockWindowFacts facts = startFacts[sourceIndex + index + 1];
                DockRect bounds = layout[index];
                targets.Add(new DockLayoutTarget(facts.NoteId, bounds.Left,
                    bounds.Top, bounds.Width, bounds.Height, facts.Visible,
                    facts.TopMost));
            }
            return targets;
        }

        private void ResizeStickyDockGroup(DockWindowFacts snapshot,
            int requestedLeft, int requestedWidth)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || snapshot == null) return;
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            int left = requestedLeft;
            int width = Math.Max(280, Math.Min(900, requestedWidth));
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(ordered);
            facts[snapshot.NoteId] = snapshot;
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            foreach (StickyNoteData note in ordered)
            {
                DockWindowFacts memberFacts;
                if (!facts.TryGetValue(note.Id, out memberFacts)) continue;
                targets.Add(new DockLayoutTarget(note.Id, left,
                    memberFacts.Y, width, memberFacts.Height,
                    memberFacts.Visible, memberFacts.TopMost));
            }
            _synchronizingDockLayout = true;
            try
            {
                ApplyDockTargets(targets, snapshot.NoteId);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void RefreshDockResizeRoles()
        {
            foreach (StickyNoteData note in _notes.GetAll())
                ApplyDockResizeRole(note, false, true, true,
                    false, 220, 700);
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> ordered = BuildDockChainOrder(note);
                if (ordered.Count <= 1) continue;
                for (int index = 0; index < ordered.Count; index++)
                {
                    bool internalDivider = index < ordered.Count - 1;
                    ApplyDockResizeRole(ordered[index], true,
                        index == 0, true, internalDivider,
                        220, 700);
                    handled.Add(ordered[index].Id);
                }
            }
        }

        private void ApplyDockResizeRole(StickyNoteData note, bool grouped,
            bool resizeTop, bool resizeBottom, bool splitBottom,
            int dividerMinimumHeight, int dividerMaximumHeight)
        {
            if (note == null) return;
            if (!IsHostedSticky(note)) return;
            StickyUiDockResizeRole hostedRole = new StickyUiDockResizeRole(
                grouped, resizeTop, resizeBottom, splitBottom,
                dividerMinimumHeight, dividerMaximumHeight);
            PostHostedStickyCommand(StickyUiCommand.SetDockResizeRole(
                note.Id, hostedRole),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null || result.Status !=
                        StickyUiCommandStatus.Handled)
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-resize-role", result);
                });
        }

        internal static int CalculateDockDividerHeight(
            int requestedUpperHeight)
        {
            return StickyDockGeometry.CalculateDockDividerHeight(
                requestedUpperHeight);
        }

        private void RestoreDockOriginalLocations(string seedNoteId)
        {
            StickyNoteData seed = _notes.Find(seedNoteId);
            if (seed == null || _activeDockOriginalFacts.Count == 0)
                return;
            List<StickyNoteData> component = BuildDockComponent(seed);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            foreach (StickyNoteData note in component)
            {
                DockWindowFacts original;
                if (_activeDockOriginalFacts.TryGetValue(note.Id,
                    out original))
                    targets.Add(original.ToTarget(original.X, original.Y));
            }
            _movingDockGroup = true;
            try { ApplyDockTargets(targets, null); }
            finally { _movingDockGroup = false; }
        }

        internal static Point CalculateHeaderReachableTranslation(
            Rectangle header, Rectangle work)
        {
            DockPoint delta = StickyDockGeometry
                .CalculateHeaderReachableTranslation(
                    new DockRect
                    {
                        Left = header.Left,
                        Top = header.Top,
                        Width = header.Width,
                        Height = header.Height
                    },
                    new DockRect
                    {
                        Left = work.Left,
                        Top = work.Top,
                        Width = work.Width,
                        Height = work.Height
                    });
            return new Point(delta.X, delta.Y);
        }

        private List<StickyNoteData> BuildDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> all = _notes.GetAll();
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (seed == null) return result;
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            ids.Add(seed.Id);
            bool changed;
            do
            {
                changed = false;
                foreach (StickyNoteData note in all)
                {
                    if (!note.Visible) continue;
                    if (ids.Contains(note.Id) ||
                        (!String.IsNullOrEmpty(note.DockParentId) &&
                            ids.Contains(note.DockParentId)))
                    {
                        if (ids.Add(note.Id)) changed = true;
                        if (!String.IsNullOrEmpty(note.DockParentId) &&
                            ids.Add(note.DockParentId)) changed = true;
                    }
                }
            }
            while (changed);
            foreach (StickyNoteData note in all)
                if (note.Visible && ids.Contains(note.Id)) result.Add(note);
            return result;
        }

        private StickyNoteData FindActiveDockTail(StickyNoteData seed)
        {
            List<StickyNoteData> activeNotes = new List<StickyNoteData>();
            foreach (string noteId in _activeDockGroupIds)
            {
                StickyNoteData note = _notes.Find(noteId);
                if (note != null) activeNotes.Add(note);
            }
            return StickyDockOperations.FindActiveDockTail(_notes.GetAll(),
                activeNotes, seed);
        }

        private void ShowSplitGuide(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            ClearSplitGuide();
            if (source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(source.DockParentId, out parentFacts)
                ? CalculateDockVisualSeam(parentFacts) : Rectangle.Empty;
            if (seam.IsEmpty) return;
            _splitGuideIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(255, 151, 62), 0);
            _splitGuideIndicator.ShowSeam(seam);
        }

        private void UpdateSplitGuide(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (_splitGuideIndicator == null ||
                _splitGuideIndicator.IsDisposed || source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(source.DockParentId, out parentFacts)
                ? CalculateDockVisualSeam(parentFacts) : Rectangle.Empty;
            if (!seam.IsEmpty) _splitGuideIndicator.UpdateSeam(seam);
        }

        private void ClearSplitGuide()
        {
            if (_splitGuideIndicator != null &&
                !_splitGuideIndicator.IsDisposed)
                _splitGuideIndicator.Close();
            _splitGuideIndicator = null;
        }

        private void UpdateDockPreview(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            DockTarget target = FindDockTarget(source, factsById);
            StickyNoteData parent = target == null ? null :
                _notes.Find(target.ParentNoteId);
            StickyNoteData child = target == null ? null :
                _notes.Find(target.ExistingChildNoteId);
            if (String.Equals(parent == null ? String.Empty : parent.Id,
                _dockPreviewParentNoteId ?? String.Empty,
                StringComparison.OrdinalIgnoreCase) &&
                String.Equals(child == null ? String.Empty : child.Id,
                _dockPreviewChildNoteId ?? String.Empty,
                StringComparison.OrdinalIgnoreCase)) return;
            ClearDockPreview();
            if (parent == null) return;
            _dockPreviewParentNoteId = parent.Id;
            _dockPreviewChildNoteId = child == null ? String.Empty : child.Id;
            _dockPreviewIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(32, 160, 255), 0);
            DockWindowFacts parentFacts;
            if (factsById != null && factsById.TryGetValue(parent.Id,
                out parentFacts))
                _dockPreviewIndicator.ShowSeam(
                    CalculateDockVisualSeam(parentFacts));
        }

        internal static Rectangle CalculateDockVisualSeam(
            DockWindowFacts facts)
        {
            return facts == null ? Rectangle.Empty : new Rectangle(facts.X,
                facts.Y + facts.Height - 3, facts.Width, 6);
        }

        private DockTarget FindDockTarget(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (source == null) return null;
            if (!String.IsNullOrEmpty(source.DockParentId) &&
                !_activeNoteDetached) return null;
            HashSet<string> activeIds = new HashSet<string>(
                _activeDockGroupIds, StringComparer.OrdinalIgnoreCase);
            DockWindowFacts sourceFacts;
            if (factsById == null ||
                !factsById.TryGetValue(source.Id, out sourceFacts))
                return null;
            Rectangle sourceBounds = new Rectangle(sourceFacts.X,
                sourceFacts.Y, sourceFacts.Width, sourceFacts.Height);
            DockTarget best = null;
            int bestScore = Int32.MaxValue;
            foreach (StickyNoteData candidate in _notes.GetAll())
            {
                if (candidate == null || !candidate.Visible ||
                    String.Equals(candidate.Id, source.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    activeIds.Contains(candidate.Id))
                    continue;
                if (!CanUseDockComponents(source, candidate)) continue;
                DockWindowFacts candidateFacts;
                if (!factsById.TryGetValue(candidate.Id,
                    out candidateFacts) || !candidateFacts.Visible) continue;
                Rectangle candidateBounds = new Rectangle(candidateFacts.X,
                    candidateFacts.Y, candidateFacts.Width,
                    candidateFacts.Height);
                if (!CanDockBelow(sourceBounds, candidateBounds, 20)) continue;
                int score = Math.Abs(sourceFacts.Y -
                    candidateBounds.Bottom) * 10 +
                    Math.Min(Math.Abs(sourceFacts.X - candidateFacts.X),
                        Math.Abs(sourceBounds.Right - candidateBounds.Right));
                if (score >= bestScore) continue;
                best = new DockTarget();
                best.ParentNoteId = candidate.Id;
                best.ExistingChildNoteId = FindDockChild(candidate.Id,
                    activeIds);
                bestScore = score;
            }
            if (best != null && !CanSafelyCombineDockComponents(best, source,
                factsById))
                return null;
            return best;
        }

        private bool CanUseDockComponents(StickyNoteData source,
            StickyNoteData target)
        {
            if (!IsDockParticipant(source) ||
                !IsDockParticipant(target)) return false;
            foreach (string noteId in _activeDockGroupIds)
                if (!IsDockParticipant(_notes.Find(noteId)))
                    return false;
            foreach (StickyNoteData note in
                BuildDockChainOrderIncludingHidden(target))
                if (!IsDockParticipant(note)) return false;
            return true;
        }

        private bool IsDockParticipant(StickyNoteData note)
        {
            return note != null;
        }

        private string FindDockChild(string parentId,
            HashSet<string> ignoredIds)
        {
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (ignoredIds != null && ignoredIds.Contains(note.Id)) continue;
                if (String.Equals(note.DockParentId, parentId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (note.Visible) return note.Id;
                }
            }
            return String.Empty;
        }

        internal static bool CanDockBelow(Rectangle moving, Rectangle target,
            int threshold)
        {
            return StickyDockOperations.CanDockBelow(moving.Left, moving.Top,
                moving.Width, moving.Height, target.Left, target.Top,
                target.Width, target.Height, threshold);
        }

        private void ClearDockPreview()
        {
            if (_dockPreviewIndicator != null &&
                !_dockPreviewIndicator.IsDisposed)
                _dockPreviewIndicator.Close();
            _dockPreviewParentNoteId = null;
            _dockPreviewChildNoteId = null;
            _dockPreviewIndicator = null;
        }

        private static void ShowTransientDockPulse(Rectangle seam, Color color)
        {
            DockPulseIndicatorForm indicator = new DockPulseIndicatorForm(
                color, 720);
            indicator.ShowSeam(seam);
        }

        private void DetachDockRelations(StickyNoteData note)
        {
            if (note == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(note);
            int index = ordered.FindIndex(delegate(StickyNoteData candidate)
            {
                return String.Equals(candidate.Id, note.Id,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (index >= 0)
            {
                ordered.RemoveAt(index);
                StickyDockGroups.ApplyOrderedGroup(ordered);
            }
            else
            {
                foreach (StickyNoteData candidate in _notes.GetAll())
                    if (String.Equals(candidate.DockParentId, note.Id,
                        StringComparison.OrdinalIgnoreCase))
                        StickyDockGroups.ClearMembership(candidate);
            }
            StickyDockGroups.ClearMembership(note);
        }

        private void HideStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (PostHostedStickyHide(note)) return;
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(note);
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(snapshot);
            DockWindowFacts rootFacts = DockWindowFacts.FromData(note);
            DockWindowFacts capturedRoot;
            if (snapshot.Count > 0 && facts.TryGetValue(snapshot[0].Id,
                out capturedRoot)) rootFacts = capturedRoot;
            note.Visible = false;
            StickyDockOperations.PreserveDockSlotForHiddenMember(
                snapshot, note);
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(snapshot, facts, rootFacts.X, rootFacts.Y,
                    rootFacts.Width);
            }
            finally { _synchronizingDockLayout = false; }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
        }

        private void DeleteStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (IsHostedSticky(note))
            {
                BeginHostedStickyDelete(note);
                return;
            }
            DeleteStickyNoteAfterWindowClosed(note);
        }

        private void BeginHostedStickyDelete(StickyNoteData note)
        {
            string noteId = note.Id;
            if (!_hostedRuntime.TryBeginDelete(noteId)) return;
            PostHostedStickyCommand(StickyUiCommand.Close(noteId),
                delegate(StickyUiCommandResult result)
                {
                    _hostedRuntime.EndDelete(noteId);
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-delete", result);
                        ShowBubble("便利贴仍在编辑，删除已取消。");
                        return;
                    }
                    ApplyHostedStickySnapshot(result.Snapshot,
                        result.Sequence, false);
                    _hostedRuntime.RemoveNote(noteId);
                    StickyNoteData canonical = _notes.Find(noteId);
                    if (canonical != null)
                        DeleteStickyNoteAfterWindowClosed(canonical);
                });
        }

        private void DeleteStickyNoteAfterWindowClosed(StickyNoteData note)
        {
            DetachDockRelations(note);
            CancelReminderForNote(note, false);
            _notes.Remove(note);
            RefreshDockResizeRoles();
            RefreshMenuText();
            RefreshNoteTabs();
        }

        private void ConfirmDeleteStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (MessageBox.Show(this,
                "确定删除便签“" + note.DisplayTitle + "”吗？此操作无法撤销。",
                "删除侧边页签", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DeleteStickyNote(note);
        }
    }
}
