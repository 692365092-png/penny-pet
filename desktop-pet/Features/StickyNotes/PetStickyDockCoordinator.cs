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
        private void RestoreStickyDockComponent(
            List<StickyNoteData> ordered, StickyNoteData focus,
            bool focusEditor, bool persistVisibility)
        {
            if (ordered == null || ordered.Count == 0) return;
            StickyNoteData rootData = ordered[0];
            int rootWidth = Math.Max(280, Math.Min(900, rootData.Width));
            int rootLeft = rootData.X;
            int rootTop = rootData.Y;
            Rectangle rootHeader = new Rectangle(rootLeft, rootTop,
                rootWidth, 32);
            Rectangle work = Screen.FromRectangle(rootHeader).WorkingArea;
            Point translation = CalculateHeaderReachableTranslation(
                rootHeader, work);
            rootLeft += translation.X;
            rootTop += translation.Y;
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData member in ordered)
            {
                sizes.Add(new Size(member.Width, member.Height));
                member.Visible = true;
                member.AlwaysOnTop = rootData.AlwaysOnTop;
            }
            StickyDockGroups.ApplyOrderedGroup(ordered);
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                rootLeft, rootTop, rootWidth);
            _synchronizingDockLayout = true;
            try
            {
                for (int index = 0; index < ordered.Count; index++)
                {
                    StickyNoteData member = ordered[index];
                    StickyNoteWindow form = GetOrCreateStickyNoteWindow(member);
                    form.ShowRestoredDocked(layout[index]);
                    form.EnableWinFormsKeyboardInterop();
                }
            }
            finally { _synchronizingDockLayout = false; }
            StickyNoteWindow focusForm;
            if (focusEditor && focus != null &&
                _noteWindows.TryGetValue(focus.Id, out focusForm) &&
                focusForm != null && !focusForm.IsDisposed)
                focusForm.FocusPrimaryInputForTest();
            RefreshDockResizeRoles();
            if (persistVisibility) _notes.Save();
            RefreshMenuText();
            RefreshNoteTabs();
        }

        private void StickyNotePinStateChanged(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || source.IsDisposed) return;
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(source.Data);
            if (component.Count == 0)
                component = BuildDockComponent(source.Data);
            bool alwaysOnTop = source.Data.AlwaysOnTop;
            foreach (StickyNoteData note in component)
            {
                note.AlwaysOnTop = alwaysOnTop;
                StickyNoteWindow member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed)
                    member.ApplyTopMostWindowState(alwaysOnTop);
            }
            _notes.Save();
        }

        private void StickyNoteCloseRequested(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || source.IsDisposed) return;
            List<StickyNoteData> ordered =
                BuildAuthoritativeVisibleDockOrder(source.Data);
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(source.Data);
            snapshot = StickyDockOperations.SelectMoreCompleteDockOrder(
                ordered, snapshot);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, source.Data.Id,
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
                    StickyNoteWindow member;
                    if (_noteWindows.TryGetValue(note.Id, out member) &&
                        member != null && !member.IsDisposed)
                        member.HideAsDockGroupMember();
                    else note.Visible = false;
                }
                StickyDockGroups.RebuildVisibleParentChain(snapshot);
            }
            else
            {
                // A lower X temporarily hides exactly that member.  Its group
                // identity and slot remain in the snapshot, while the live
                // visible parent chain skips across the hidden window.
                Dictionary<string, DockWindowFacts> facts =
                    CaptureLegacyDockWindowFacts(snapshot);
                DockWindowFacts sourceFacts =
                    CaptureLegacyDockWindowFacts(source);
                facts[sourceFacts.NoteId] = sourceFacts;
                DockWindowFacts rootFacts = sourceFacts;
                DockWindowFacts capturedRoot;
                if (ordered.Count > 0 && facts.TryGetValue(ordered[0].Id,
                    out capturedRoot)) rootFacts = capturedRoot;
                source.HideAsDockGroupMember();
                StickyDockOperations.PreserveDockSlotForHiddenMember(
                    snapshot, source.Data);
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

        private void StickyNoteHeaderDragStarted(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || source.IsDisposed) return;
            BeginStickyDockDrag(CaptureLegacyDockWindowFacts(source), source);
        }

        private void BeginStickyDockDrag(DockWindowFacts facts,
            StickyNoteWindow legacySource)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            ClearDockPreview();
            ClearSplitGuide();
            _activeNoteDragId = facts.NoteId;
            _activeNoteDragHosted = IsHostedSticky(seed);
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
                CaptureLegacyDockWindowFacts(_activeDockGroupIds);
            groupFacts[facts.NoteId] = facts;
            foreach (KeyValuePair<string, DockWindowFacts> item in groupFacts)
            {
                _activeDockOriginalFacts[item.Key] = item.Value;
                _activeDockCurrentFacts[item.Key] = item.Value;
            }
            if (!_activeNoteDragHosted)
                RaiseActiveDockGroupForDrag(legacySource);
            // The root header is the one unambiguous handle for moving the
            // whole stack.  Only a member that has a parent can be pulled out
            // after a deliberate hold.
            _activeNoteSplitEligible = !_activeNoteDragHosted &&
                StickyDockOperations.IsDockSplitEligible(seed.DockParentId,
                    _activeDockGroupIds.Count);
            if (_activeNoteSplitEligible) ShowSplitGuide(seed);
        }

        private void StickyNoteHeaderDragMoved(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || source.IsDisposed) return;
            MoveStickyDockDrag(CaptureLegacyDockWindowFacts(source), source);
        }

        private void MoveStickyDockDrag(DockWindowFacts facts,
            StickyNoteWindow legacySource)
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
                    RaiseActiveDockGroupForDrag(legacySource);
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
                UpdateSplitGuide(seed);
            if (!_activeNoteDragHosted)
                UpdateDockPreview(seed,
                    CaptureLegacyDockWindowFacts(_notes.GetAll()));
        }

        private void StickyNoteHeaderDragCompleted(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || source.IsDisposed) return;
            CompleteStickyDockDrag(CaptureLegacyDockWindowFacts(source));
        }

        private void CompleteStickyDockDrag(DockWindowFacts facts)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            if (!String.Equals(facts.NoteId, _activeNoteDragId,
                StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, DockWindowFacts> currentFacts =
                CaptureLegacyDockWindowFacts(_notes.GetAll());
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
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(mergedSnapshot, currentFacts,
                        targetRootFacts.X, targetRootFacts.Y,
                        targetRootFacts.Width);
                    if (!_activeNoteDragHosted)
                    {
                        bool groupTopMost = targetSnapshot.Count == 0
                            ? parent.AlwaysOnTop :
                            targetSnapshot[0].AlwaysOnTop;
                        foreach (StickyNoteData note in mergedSnapshot)
                        {
                            note.AlwaysOnTop = groupTopMost;
                            StickyNoteWindow member;
                            if (_noteWindows.TryGetValue(note.Id, out member) &&
                                member != null && !member.IsDisposed)
                                member.ApplyTopMostWindowState(groupTopMost);
                        }
                    }
                }
                finally { _synchronizingDockLayout = false; }
                if (!_activeNoteDragHosted)
                {
                    StickyNoteData existingChild =
                        _notes.Find(target.ExistingChildNoteId);
                    if (existingChild != null)
                        ShowTransientDockPulse(new Rectangle(tailData.X,
                            tailData.Y + tailData.Height - 3,
                            tailData.Width, 6), Color.FromArgb(32, 160, 255));
                    ShowTransientDockPulse(new Rectangle(parent.X,
                        parent.Y + parent.Height - 3, parent.Width, 6),
                        Color.FromArgb(32, 160, 255));
                }
            }
            else if (!_activeNoteDragHosted)
            {
                KeepDockHeaderReachable(seed, seed, currentFacts);
                StickyNoteData remainder =
                    _notes.Find(_splitRemainderNoteId);
                if (_activeNoteDetached && remainder != null)
                    KeepDockHeaderReachable(remainder,
                        FindDockRoot(remainder), currentFacts);
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
            _activeNoteDragHosted = false;
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

        private void RaiseActiveDockGroupForDrag(StickyNoteWindow source)
        {
            if (source == null || source.IsDisposed ||
                _activeDockGroupIds.Count <= 1) return;
            HashSet<string> activeIds = new HashSet<string>(
                _activeDockGroupIds, StringComparer.OrdinalIgnoreCase);
            StickyNoteData sourceNote = _notes.Find(_activeNoteDragId);
            List<StickyNoteData> ordered =
                BuildDockChainOrderIncludingHidden(sourceNote);
            // Raise tail-to-root so the entire moving stack occupies one
            // contiguous z-order band above unrelated notes. The captured
            // source is raised last to keep DragMove stable even if a middle
            // member initiated the drag.
            for (int index = ordered.Count - 1; index >= 0; index--)
            {
                StickyNoteData note = ordered[index];
                if (note == null || !activeIds.Contains(note.Id) ||
                    String.Equals(note.Id, _activeNoteDragId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                StickyNoteWindow member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed && member.Visible)
                    member.RaiseForDockDragWithoutActivation();
            }
            source.RaiseForDockDragWithoutActivation();
        }

        // Legacy input edge: copy live WPF geometry once, then pass detached
        // facts through Dock session and rule code.
        private DockWindowFacts CaptureLegacyDockWindowFacts(
            StickyNoteWindow form)
        {
            if (form == null || form.IsDisposed) return null;
            return new DockWindowFacts(form.Data.Id, form.Left, form.Top,
                form.Width, form.Height, form.Visible,
                form.Data.AlwaysOnTop);
        }

        private Dictionary<string, DockWindowFacts>
            CaptureLegacyDockWindowFacts(IEnumerable<string> noteIds)
        {
            Dictionary<string, DockWindowFacts> facts =
                new Dictionary<string, DockWindowFacts>(
                    StringComparer.OrdinalIgnoreCase);
            if (noteIds == null) return facts;
            foreach (string noteId in noteIds)
            {
                StickyNoteData note = _notes.Find(noteId);
                if (note == null) continue;
                StickyNoteWindow form;
                DockWindowFacts value = null;
                if (_noteWindows.TryGetValue(noteId, out form))
                    value = CaptureLegacyDockWindowFacts(form);
                facts[noteId] = value ?? DockWindowFacts.FromData(note);
            }
            return facts;
        }

        private Dictionary<string, DockWindowFacts>
            CaptureLegacyDockWindowFacts(IEnumerable<StickyNoteData> notes)
        {
            List<string> noteIds = new List<string>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null) noteIds.Add(note.Id);
            return CaptureLegacyDockWindowFacts(noteIds);
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
            int left, int top, int width)
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
            ApplyDockTargets(targets, null);
        }

        // Legacy effect edge: canonical data is updated first; only this
        // boundary resolves a typed target back to a legacy Window.
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
            if (IsHostedSticky(note))
            {
                PostHostedStickyCommand(new StickyUiCommand(
                    StickyUiCommandKind.SetBounds, target.NoteId, false, null,
                    new StickyUiBounds(target.X, target.Y, target.Width,
                        target.Height)),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, false);
                        else ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-bounds", result);
                    });
                return;
            }
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(target.NoteId, out form) &&
                form != null && !form.IsDisposed)
            {
                form.Bounds = new Rectangle(target.X, target.Y,
                    target.Width, target.Height);
                form.ApplyTopMostWindowState(target.TopMost);
            }
        }

        private static bool IsDockHorizontalResizeActive(
            StickyNoteWindow source)
        {
            return source != null && !source.IsDisposed &&
                source.DockHorizontalResizeActive;
        }

        private static bool IsDockDividerResizeActive(
            StickyNoteWindow source)
        {
            return source != null && !source.IsDisposed &&
                source.DockDividerResizeActive;
        }

        private static int DockHorizontalGroupLeft(
            StickyNoteWindow source, int width)
        {
            return source == null || source.IsDisposed
                ? 0 : source.DockHorizontalGroupLeft(width);
        }

        private StickyNoteWindow GetLegacyWindow(StickyNoteData note)
        {
            return note == null ? null : GetLegacyWindow(note.Id);
        }

        private StickyNoteWindow GetLegacyWindow(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return null;
            StickyNoteWindow form;
            return _noteWindows.TryGetValue(noteId, out form) &&
                form != null && !form.IsDisposed ? form : null;
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
                CaptureLegacyDockWindowFacts(ordered);
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
                bool groupTopMost = ordered[0].AlwaysOnTop;
                foreach (StickyNoteData note in ordered)
                {
                    note.AlwaysOnTop = groupTopMost;
                    StickyNoteWindow member;
                    if (_noteWindows.TryGetValue(note.Id, out member) &&
                        member != null && !member.IsDisposed)
                        member.ApplyTopMostWindowState(groupTopMost);
                }
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

        private void StickyNoteSizeChanged(object sender,
            System.Windows.SizeChangedEventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null ||
                source == null || source.IsDisposed) return;
            DockWindowFacts snapshot = CaptureLegacyDockWindowFacts(source);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            ApplyDockTarget(snapshot.ToTarget(snapshot.X, snapshot.Y),
                snapshot.NoteId);
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1)
            {
                source.SetDockResizeRole(false, true, true);
                return;
            }
            if (e.WidthChanged && IsDockHorizontalResizeActive(source))
            {
                // WM_SIZING already synchronized the complete logical group.
                // Do not run a second, event-order-dependent layout here.
                return;
            }
            Dictionary<string, DockWindowFacts> facts =
                CaptureLegacyDockWindowFacts(ordered);
            facts[snapshot.NoteId] = snapshot;
            DockWindowFacts root;
            if (!facts.TryGetValue(ordered[0].Id, out root)) return;
            int top = root.Y;
            int left = e.WidthChanged && IsDockHorizontalResizeActive(source)
                ? DockHorizontalGroupLeft(source, snapshot.Width)
                : e.WidthChanged ? snapshot.X : root.X;
            _synchronizingDockLayout = true;
            try
            {
                int sourceIndex = ordered.FindIndex(
                    delegate(StickyNoteData note)
                    {
                        return String.Equals(note.Id, snapshot.NoteId,
                            StringComparison.OrdinalIgnoreCase);
                    });
                if (e.HeightChanged && IsDockDividerResizeActive(source) &&
                    sourceIndex >= 0 && sourceIndex < ordered.Count - 1)
                {
                    DockWindowFacts lower;
                    if (facts.TryGetValue(ordered[sourceIndex + 1].Id,
                        out lower))
                    {
                        Size adjusted = CalculateDockDividerHeights(
                            (int)Math.Round(e.PreviousSize.Height),
                            snapshot.Height, lower.Height);
                        DockLayoutTarget upperTarget =
                            new DockLayoutTarget(snapshot.NoteId,
                                snapshot.X, snapshot.Y, snapshot.Width,
                                adjusted.Width, true, snapshot.TopMost);
                        DockLayoutTarget lowerTarget =
                            new DockLayoutTarget(lower.NoteId,
                                lower.X, lower.Y, lower.Width,
                                adjusted.Height, true, lower.TopMost);
                        ApplyDockTarget(upperTarget, snapshot.NoteId);
                        ApplyDockTarget(lowerTarget, null);
                        facts[snapshot.NoteId] =
                            DockWindowFacts.FromTarget(upperTarget);
                        facts[lower.NoteId] =
                            DockWindowFacts.FromTarget(lowerTarget);
                    }
                }
                LayoutDockChain(ordered, facts, left, top, snapshot.Width);
            }
            finally { _synchronizingDockLayout = false; }
            RefreshDockResizeRoles();
        }

        private void StickyNoteLocationChanged(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || source == null ||
                source.IsDisposed) return;
            DockWindowFacts snapshot = CaptureLegacyDockWindowFacts(source);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            if (IsDockHorizontalResizeActive(source))
            {
                ApplyDockTarget(snapshot.ToTarget(snapshot.X, snapshot.Y),
                    snapshot.NoteId);
                return;
            }
            Dictionary<string, DockWindowFacts> facts =
                CaptureLegacyDockWindowFacts(ordered);
            facts[snapshot.NoteId] = snapshot;
            DockWindowFacts root;
            if (!facts.TryGetValue(ordered[0].Id, out root)) return;
            int left = IsDockHorizontalResizeActive(source)
                ? DockHorizontalGroupLeft(source, snapshot.Width)
                : String.Equals(snapshot.NoteId, ordered[0].Id,
                    StringComparison.OrdinalIgnoreCase)
                    ? snapshot.X : root.X;
            int top = String.Equals(snapshot.NoteId, ordered[0].Id,
                StringComparison.OrdinalIgnoreCase)
                ? snapshot.Y : root.Y;
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, facts, left, top, snapshot.Width);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void StickyNoteDockHorizontalResizing(object sender,
            DockHorizontalResizeEventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || source == null ||
                source.IsDisposed || e == null) return;
            DockWindowFacts snapshot = CaptureLegacyDockWindowFacts(source);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            int left = e.Left;
            int width = Math.Max(280, Math.Min(900, e.Width));
            Dictionary<string, DockWindowFacts> facts =
                CaptureLegacyDockWindowFacts(ordered);
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
            foreach (StickyNoteWindow form in _noteWindows.Values)
                if (form != null && !form.IsDisposed)
                    form.SetDockResizeRole(false, true, true);
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> ordered = BuildDockChainOrder(note);
                if (ordered.Count <= 1) continue;
                for (int index = 0; index < ordered.Count; index++)
                {
                    StickyNoteWindow form;
                    if (_noteWindows.TryGetValue(ordered[index].Id, out form) &&
                        form != null && !form.IsDisposed)
                    {
                        bool internalDivider = index < ordered.Count - 1;
                        int dividerMinimum = 220;
                        int dividerMaximum = 700;
                        if (internalDivider)
                        {
                            StickyNoteWindow lower;
                            if (_noteWindows.TryGetValue(ordered[index + 1].Id,
                                out lower) && lower != null &&
                                !lower.IsDisposed)
                            {
                                Size range = CalculateDockDividerRange(
                                    form.Height, lower.Height);
                                dividerMinimum = range.Width;
                                dividerMaximum = range.Height;
                            }
                        }
                        form.SetDockResizeRole(true, index == 0, true,
                            internalDivider, dividerMinimum,
                            dividerMaximum);
                    }
                    handled.Add(ordered[index].Id);
                }
            }
        }

        internal static Size CalculateDockDividerHeights(
            int previousUpperHeight, int requestedUpperHeight,
            int currentLowerHeight)
        {
            DockSize adjusted = StickyDockGeometry.CalculateDockDividerHeights(
                previousUpperHeight, requestedUpperHeight,
                currentLowerHeight);
            return new Size(adjusted.Width, adjusted.Height);
        }

        internal static Size CalculateDockDividerRange(int upperHeight,
            int lowerHeight)
        {
            DockSize range = StickyDockGeometry.CalculateDockDividerRange(
                upperHeight, lowerHeight);
            return new Size(range.Width, range.Height);
        }

        private StickyNoteData FindDockRoot(StickyNoteData seed)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            return ordered.Count == 0 ? seed : ordered[0];
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

        private void KeepDockHeaderReachable(StickyNoteData seed,
            StickyNoteData focus,
            IDictionary<string, DockWindowFacts> factsById)
        {
            List<StickyNoteData> component = BuildDockComponent(seed);
            Rectangle focusBounds = Rectangle.Empty;
            foreach (StickyNoteData note in component)
            {
                DockWindowFacts facts;
                if (factsById == null ||
                    !factsById.TryGetValue(note.Id, out facts) ||
                    !facts.Visible) continue;
                if (focus != null && String.Equals(note.Id, focus.Id,
                    StringComparison.OrdinalIgnoreCase))
                    focusBounds = new Rectangle(facts.X, facts.Y,
                        facts.Width, facts.Height);
            }
            if (focusBounds.IsEmpty) return;
            Rectangle header = new Rectangle(focusBounds.Left,
                focusBounds.Top, focusBounds.Width, 32);
            Rectangle work = Screen.FromRectangle(header).WorkingArea;
            Point delta = CalculateHeaderReachableTranslation(header, work);
            if (delta.X == 0 && delta.Y == 0) return;
            List<string> noteIds = new List<string>();
            foreach (StickyNoteData note in component) noteIds.Add(note.Id);
            List<DockLayoutTarget> targets = CalculateDockTranslationTargets(
                noteIds, factsById, null, delta.X, delta.Y);
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

        private void ShowSplitGuide(StickyNoteData source)
        {
            ClearSplitGuide();
            if (source == null) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.DockParentId))
            {
                StickyNoteWindow parent;
                if (_noteWindows.TryGetValue(source.DockParentId,
                    out parent) && parent != null && !parent.IsDisposed)
                    seam = new Rectangle(parent.Left,
                        parent.Bounds.Bottom - 3, parent.Width, 6);
            }
            if (seam.IsEmpty) return;
            _splitGuideIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(255, 151, 62), 0);
            _splitGuideIndicator.ShowSeam(seam);
        }

        private void UpdateSplitGuide(StickyNoteData source)
        {
            if (_splitGuideIndicator == null ||
                _splitGuideIndicator.IsDisposed || source == null) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.DockParentId))
            {
                StickyNoteWindow parent;
                if (_noteWindows.TryGetValue(source.DockParentId,
                    out parent) && parent != null && !parent.IsDisposed)
                    seam = new Rectangle(parent.Left,
                        parent.Bounds.Bottom - 3, parent.Width, 6);
            }
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
                _dockPreviewParent == null ? String.Empty :
                    _dockPreviewParent.Data.Id,
                StringComparison.OrdinalIgnoreCase) &&
                String.Equals(child == null ? String.Empty : child.Id,
                _dockPreviewChild == null ? String.Empty :
                    _dockPreviewChild.Data.Id,
                StringComparison.OrdinalIgnoreCase)) return;
            ClearDockPreview();
            _dockPreviewParent = GetLegacyWindow(parent);
            _dockPreviewChild = GetLegacyWindow(child);
            if (parent == null) return;
            GetLegacyWindow(source)?.SetDockPreview(true, false);
            GetLegacyWindow(parent)?.SetDockPreview(true, true);
            if (child != null) GetLegacyWindow(child)?.SetDockPreview(true, true);
            _dockPreviewIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(32, 160, 255), 0);
            DockWindowFacts parentFacts;
            if (factsById != null && factsById.TryGetValue(parent.Id,
                out parentFacts))
                _dockPreviewIndicator.ShowSeam(new Rectangle(parentFacts.X,
                    parentFacts.Y + parentFacts.Height - 3,
                    parentFacts.Width, 6));
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
                if (_activeNoteDragHosted)
                {
                    if (!CanUseHostedOrdinaryDockPair(source, candidate))
                        continue;
                }
                else if (IsHostedSticky(candidate)) continue;
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

        private bool CanUseHostedOrdinaryDockPair(StickyNoteData source,
            StickyNoteData target)
        {
            return _activeDockGroupIds.Count == 1 &&
                IsHostedSticky(source) && IsHostedSticky(target) &&
                !source.IsTodoList && !source.IsSchedule &&
                source.ReminderUtcTicks <= 0 &&
                !target.IsTodoList && !target.IsSchedule &&
                target.ReminderUtcTicks <= 0 &&
                String.IsNullOrEmpty(source.DockGroupId) &&
                String.IsNullOrEmpty(source.DockParentId) &&
                String.IsNullOrEmpty(target.DockGroupId) &&
                String.IsNullOrEmpty(target.DockParentId);
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
            StickyNoteWindow active = GetLegacyWindow(_activeNoteDragId);
            if (active != null) active.SetDockPreview(false, false);
            if (_dockPreviewParent != null && !_dockPreviewParent.IsDisposed)
                _dockPreviewParent.SetDockPreview(false, false);
            if (_dockPreviewChild != null && !_dockPreviewChild.IsDisposed)
                _dockPreviewChild.SetDockPreview(false, false);
            if (_dockPreviewIndicator != null &&
                !_dockPreviewIndicator.IsDisposed)
                _dockPreviewIndicator.Close();
            _dockPreviewParent = null;
            _dockPreviewChild = null;
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
                CaptureLegacyDockWindowFacts(snapshot);
            DockWindowFacts rootFacts = DockWindowFacts.FromData(note);
            DockWindowFacts capturedRoot;
            if (snapshot.Count > 0 && facts.TryGetValue(snapshot[0].Id,
                out capturedRoot)) rootFacts = capturedRoot;
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.HideNote();
            else
            {
                note.Visible = false;
            }
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
            if (!_hostedDeletePending.Add(noteId)) return;
            PostHostedStickyCommand(new StickyUiCommand(
                StickyUiCommandKind.Close, noteId, false),
                delegate(StickyUiCommandResult result)
                {
                    _hostedDeletePending.Remove(noteId);
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-delete", result);
                        ShowBriefBubble("便利贴仍在编辑，删除已取消。");
                        return;
                    }
                    ApplyHostedStickySnapshot(result.Snapshot,
                        result.Sequence, false);
                    _hostedNoteIds.Remove(noteId);
                    ForgetHostedStickyState(noteId);
                    StickyNoteData canonical = _notes.Find(noteId);
                    if (canonical != null)
                        DeleteStickyNoteAfterWindowClosed(canonical);
                });
        }

        private void DeleteStickyNoteAfterWindowClosed(StickyNoteData note)
        {
            DetachDockRelations(note);
            CancelReminderForNote(note, false);
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.CloseForApplicationExit();
            _noteWindows.Remove(note.Id);
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
