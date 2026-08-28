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
                StickyNoteWindow root = null;
                if (ordered.Count > 0)
                    _noteWindows.TryGetValue(ordered[0].Id, out root);
                Point rootAnchor = root == null ? source.Location :
                    root.Location;
                int rootWidth = root == null ? source.Width : root.Width;
                source.HideAsDockGroupMember();
                StickyDockOperations.PreserveDockSlotForHiddenMember(
                    snapshot, source.Data);
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(snapshot, rootAnchor.X, rootAnchor.Y,
                        rootWidth);
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
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            ClearDockPreview();
            ClearSplitGuide();
            _activeNoteDragId = snapshot.NoteId;
            _activeNoteDragStartLocation = new Point(snapshot.X, snapshot.Y);
            _activeNoteDragLastLocation = new Point(snapshot.X, snapshot.Y);
            _activeNoteDragStartedUtc = DateTime.UtcNow;
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderSeed = null;
            SetActiveDockGroup(BuildDockComponent(seed));
            _activeDockOriginalLocations.Clear();
            foreach (StickyNoteData note in _activeDockGroup)
            {
                StickyNoteWindow member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed)
                    _activeDockOriginalLocations[note.Id] = member.Location;
            }
            RaiseActiveDockGroupForDrag(source);
            // The root header is the one unambiguous handle for moving the
            // whole stack.  Only a member that has a parent can be pulled out
            // after a deliberate hold.
            _activeNoteSplitEligible = StickyDockOperations.IsDockSplitEligible(
                seed.DockParentId, _activeDockGroup.Count);
            if (_activeNoteSplitEligible) ShowSplitGuide(seed);
        }

        private void StickyNoteHeaderDragMoved(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null) return;
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            if (_movingDockGroup ||
                !String.Equals(snapshot.NoteId, _activeNoteDragId,
                    StringComparison.OrdinalIgnoreCase)) return;
            Point current = source.Location;
            int dx = current.X - _activeNoteDragLastLocation.X;
            int dy = current.Y - _activeNoteDragLastLocation.Y;
            if (dx == 0 && dy == 0) return;

            TimeSpan held = DateTime.UtcNow - _activeNoteDragStartedUtc;
            int totalDx = current.X - _activeNoteDragStartLocation.X;
            int totalDy = current.Y - _activeNoteDragStartLocation.Y;
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
                StickyNoteWindow connected = null;
                if (!String.IsNullOrEmpty(seed.DockParentId))
                {
                    List<StickyNoteData> beforeSplit =
                        BuildDockChainOrderIncludingHidden(seed);
                    int splitIndex = beforeSplit.FindIndex(
                        delegate(StickyNoteData note)
                        {
                            return String.Equals(note.Id, snapshot.NoteId,
                                StringComparison.OrdinalIgnoreCase);
                        });
                    _noteWindows.TryGetValue(seed.DockParentId,
                        out connected);
                    if (splitIndex > 0)
                    {
                        // A held middle header extracts exactly that note.
                        // Reconnect the members before and after it into one
                        // stack instead of detaching every descendant.
                        StickyDockOperations.ExtractSingleDockMember(
                            beforeSplit, seed);
                    }
                    else StickyDockGroups.ClearMembership(seed);
                    _splitRemainderSeed = connected == null ? null :
                        connected.Data;
                    _activeNoteDetached = true;
                }
                if (_activeNoteDetached)
                {
                    ClearSplitGuide();
                    RestoreDockOriginalLocations(_splitRemainderSeed);
                    if (_splitRemainderSeed != null)
                        NormalizeDockComponent(_splitRemainderSeed);
                    SetActiveDockGroup(BuildDockComponent(seed));
                    RaiseActiveDockGroupForDrag(source);
                    RefreshDockResizeRoles();
                }
            }

            _movingDockGroup = true;
            try
            {
                foreach (StickyNoteData note in _activeDockGroup)
                {
                    if (Object.ReferenceEquals(note, source.Data)) continue;
                    StickyNoteWindow member;
                    if (!_noteWindows.TryGetValue(note.Id, out member) ||
                        member.IsDisposed || !member.Visible) continue;
                    member.Location = new Point(member.Left + dx, member.Top + dy);
                    note.X = member.Left;
                    note.Y = member.Top;
                }
                source.Data.X = source.Left;
                source.Data.Y = source.Top;
            }
            finally { _movingDockGroup = false; }
            _activeNoteDragLastLocation = current;
            if (!_activeNoteDetached && _activeNoteSplitEligible)
                UpdateSplitGuide(seed);
            UpdateDockPreview(seed);
        }

        private void StickyNoteHeaderDragCompleted(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null) return;
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            if (!String.Equals(snapshot.NoteId, _activeNoteDragId,
                StringComparison.OrdinalIgnoreCase)) return;
            DockTarget target = FindDockTarget(seed);
            if (target != null && !CanSafelyCombineDockComponents(
                target, seed)) target = null;
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
                Point targetRootAnchor = new Point(targetRoot.X, targetRoot.Y);
                int targetRootWidth = targetRoot.Width;
                StickyNoteData tail = FindActiveDockTail(seed);
                StickyNoteData tailData = tail ?? seed;
                List<StickyNoteData> mergedSnapshot =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        targetSnapshot, parent, sourceSnapshot);
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(mergedSnapshot, targetRootAnchor.X,
                        targetRootAnchor.Y, targetRootWidth);
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
                finally { _synchronizingDockLayout = false; }
                StickyNoteData existingChild =
                    _notes.Find(target.ExistingChildNoteId);
                if (existingChild != null)
                {
                    ShowTransientDockPulse(new Rectangle(tailData.X,
                        tailData.Y + tailData.Height - 3, tailData.Width, 6),
                        Color.FromArgb(32, 160, 255));
                }
                ShowTransientDockPulse(new Rectangle(parent.X,
                    parent.Y + parent.Height - 3, parent.Width, 6),
                    Color.FromArgb(32, 160, 255));
            }
            else
            {
                KeepDockHeaderReachable(source.Data, source.Data);
                if (_activeNoteDetached && _splitRemainderSeed != null)
                    KeepDockHeaderReachable(_splitRemainderSeed,
                        FindDockRoot(_splitRemainderSeed));
            }
            foreach (StickyNoteData note in _activeDockGroup)
            {
                StickyNoteWindow member;
                if (!_noteWindows.TryGetValue(note.Id, out member) ||
                    member.IsDisposed) continue;
                note.X = member.Left;
                note.Y = member.Top;
            }
            CommitVisibleDockOrder(source.Data);
            if (_splitRemainderSeed != null)
                CommitVisibleDockOrder(_splitRemainderSeed);
            ClearDockPreview();
            ClearSplitGuide();
            RefreshDockResizeRoles();
            _notes.Save();
            _activeNoteDragId = null;
            _activeDockGroup.Clear();
            _activeDockOriginalLocations.Clear();
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderSeed = null;
        }

        private void SetActiveDockGroup(List<StickyNoteData> notes)
        {
            _activeDockGroup.Clear();
            if (notes != null) _activeDockGroup.AddRange(notes);
        }

        private void RaiseActiveDockGroupForDrag(StickyNoteWindow source)
        {
            if (source == null || source.IsDisposed ||
                _activeDockGroup.Count <= 1) return;
            HashSet<string> activeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _activeDockGroup)
                if (note != null) activeIds.Add(note.Id);
            List<StickyNoteData> ordered =
                BuildDockChainOrderIncludingHidden(source.Data);
            // Raise tail-to-root so the entire moving stack occupies one
            // contiguous z-order band above unrelated notes. The captured
            // source is raised last to keep DragMove stable even if a middle
            // member initiated the drag.
            for (int index = ordered.Count - 1; index >= 0; index--)
            {
                StickyNoteData note = ordered[index];
                if (note == null || !activeIds.Contains(note.Id) ||
                    Object.ReferenceEquals(note, source.Data)) continue;
                StickyNoteWindow member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed && member.Visible)
                    member.RaiseForDockDragWithoutActivation();
            }
            source.RaiseForDockDragWithoutActivation();
        }

        private void MoveDockNotes(IEnumerable<StickyNoteData> notes,
            int dx, int dy)
        {
            if (notes == null) return;
            foreach (StickyNoteData note in notes)
            {
                StickyNoteWindow member;
                if (!_noteWindows.TryGetValue(note.Id, out member) ||
                    member.IsDisposed || !member.Visible) continue;
                member.Location = new Point(member.Left + dx,
                    member.Top + dy);
                note.X = member.Left;
                note.Y = member.Top;
            }
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
            int left, int top, int width)
        {
            List<DockWindowSnapshot> visibleSnapshots =
                new List<DockWindowSnapshot>();
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible) continue;
                DockWindowSnapshot snapshot = DockWindowSnapshot.FromData(note);
                visibleSnapshots.Add(snapshot);
                sizes.Add(new Size(snapshot.Width, snapshot.Height));
            }
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                left, top, width);
            for (int index = 0; index < visibleSnapshots.Count; index++)
            {
                DockWindowSnapshot snapshot = visibleSnapshots[index];
                ApplyDockTarget(snapshot.NoteId,
                    new DockLayoutTarget(snapshot.NoteId,
                        layout[index].Left, layout[index].Top,
                        layout[index].Width, layout[index].Height,
                        true, snapshot.TopMost));
            }
        }

        private void ApplyDockTarget(string noteId, DockLayoutTarget target)
        {
            if (target == null) return;
            StickyNoteData note = _notes.Find(noteId);
            if (note == null) return;
            note.X = target.X;
            note.Y = target.Y;
            note.Width = target.Width;
            note.Height = target.Height;
            note.Visible = target.Visible;
            note.AlwaysOnTop = target.TopMost;
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(noteId, out form) &&
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
            if (note == null) return null;
            StickyNoteWindow form;
            return _noteWindows.TryGetValue(note.Id, out form) &&
                form != null && !form.IsDisposed ? form : null;
        }

        private StickyNoteData ActiveDragNote()
        {
            return String.IsNullOrEmpty(_activeNoteDragId)
                ? null : _notes.Find(_activeNoteDragId);
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
            StickyNoteData sourceSeed)
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
                    heights.Add(note.Height);
            }
            foreach (StickyNoteData note in BuildDockChainOrder(sourceSeed))
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                    heights.Add(note.Height);
            }
            return StickyDockOperations.IsDockCoordinateRangeSafe(
                targetOrder[0].Y,
                heights, DockCoordinateSafetyLimit);
        }

        private void NormalizeDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            StickyNoteWindow root;
            if (!_noteWindows.TryGetValue(ordered[0].Id, out root) ||
                root.IsDisposed) return;
            NormalizeDockComponentAt(seed, root.Location, root.Width);
        }

        private void NormalizeDockComponentAt(StickyNoteData seed,
            Point rootAnchor, int rootWidth)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, rootAnchor.X, rootAnchor.Y,
                    rootWidth);
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
            source.Data.Width = source.Width;
            source.Data.Height = source.Height;
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
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
                source.Data.X = source.Left;
                source.Data.Width = source.Width;
                return;
            }
            StickyNoteWindow root;
            if (!_noteWindows.TryGetValue(ordered[0].Id, out root) ||
                root.IsDisposed) return;
            int top = root.Top;
            int left = e.WidthChanged && IsDockHorizontalResizeActive(source)
                ? DockHorizontalGroupLeft(source, snapshot.Width)
                : e.WidthChanged ? snapshot.X : root.Left;
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
                    StickyNoteWindow lower;
                    if (_noteWindows.TryGetValue(ordered[sourceIndex + 1].Id,
                        out lower) && lower != null && !lower.IsDisposed)
                    {
                        Size adjusted = CalculateDockDividerHeights(
                            (int)Math.Round(e.PreviousSize.Height),
                            snapshot.Height, lower.Height);
                        ApplyDockTarget(snapshot.NoteId,
                            new DockLayoutTarget(snapshot.NoteId,
                                snapshot.X, snapshot.Y, snapshot.Width,
                                adjusted.Width, true, snapshot.TopMost));
                        DockWindowSnapshot lowerSnapshot =
                            DockWindowSnapshot.FromData(lower.Data);
                        ApplyDockTarget(lowerSnapshot.NoteId,
                            new DockLayoutTarget(lowerSnapshot.NoteId,
                                lowerSnapshot.X, lowerSnapshot.Y,
                                lowerSnapshot.Width, adjusted.Height,
                                true, lowerSnapshot.TopMost));
                    }
                }
                LayoutDockChain(ordered, left, top, snapshot.Width);
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
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            if (IsDockHorizontalResizeActive(source))
            {
                seed.X = snapshot.X;
                seed.Width = snapshot.Width;
                return;
            }
            StickyNoteWindow root;
            if (!_noteWindows.TryGetValue(ordered[0].Id, out root) ||
                root.IsDisposed) return;
            int left = IsDockHorizontalResizeActive(source)
                ? DockHorizontalGroupLeft(source, snapshot.Width)
                : String.Equals(snapshot.NoteId, ordered[0].Id,
                    StringComparison.OrdinalIgnoreCase)
                    ? snapshot.X : root.Left;
            int top = String.Equals(snapshot.NoteId, ordered[0].Id,
                StringComparison.OrdinalIgnoreCase)
                ? snapshot.Y : root.Top;
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, left, top, snapshot.Width);
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
            DockWindowSnapshot snapshot =
                DockWindowSnapshot.FromData(source.Data);
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            int left = e.Left;
            int width = Math.Max(280, Math.Min(900, e.Width));
            _synchronizingDockLayout = true;
            try
            {
                foreach (StickyNoteData note in ordered)
                {
                    note.X = left;
                    note.Width = width;
                    StickyNoteWindow member;
                    if (!_noteWindows.TryGetValue(note.Id, out member) ||
                        member == null || member.IsDisposed) continue;
                    if (!Object.ReferenceEquals(member, source))
                    {
                        member.Left = left;
                        member.Width = width;
                    }
                }
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

        private void RestoreDockOriginalLocations(StickyNoteData seed)
        {
            if (seed == null || _activeDockOriginalLocations.Count == 0)
                return;
            List<StickyNoteData> component = BuildDockComponent(seed);
            _movingDockGroup = true;
            try
            {
                foreach (StickyNoteData note in component)
                {
                    Point original;
                    StickyNoteWindow member;
                    if (!_activeDockOriginalLocations.TryGetValue(note.Id,
                        out original) || !_noteWindows.TryGetValue(note.Id,
                        out member) || member == null || member.IsDisposed)
                        continue;
                    member.Location = original;
                    note.X = original.X;
                    note.Y = original.Y;
                }
            }
            finally { _movingDockGroup = false; }
        }

        private void KeepDockHeaderReachable(StickyNoteData seed,
            StickyNoteData focus)
        {
            List<StickyNoteData> component = BuildDockComponent(seed);
            Rectangle focusBounds = Rectangle.Empty;
            foreach (StickyNoteData note in component)
            {
                StickyNoteWindow form;
                if (!_noteWindows.TryGetValue(note.Id, out form) ||
                    form.IsDisposed || !form.Visible) continue;
                if (focus != null && String.Equals(note.Id, focus.Id,
                    StringComparison.OrdinalIgnoreCase)) focusBounds = form.Bounds;
            }
            if (focusBounds.IsEmpty) return;
            Rectangle header = new Rectangle(focusBounds.Left,
                focusBounds.Top, focusBounds.Width, 32);
            Rectangle work = Screen.FromRectangle(header).WorkingArea;
            Point delta = CalculateHeaderReachableTranslation(header, work);
            if (delta.X == 0 && delta.Y == 0) return;
            _movingDockGroup = true;
            try { MoveDockNotes(component, delta.X, delta.Y); }
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
            return StickyDockOperations.FindActiveDockTail(_notes.GetAll(),
                _activeDockGroup, seed);
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

        private void UpdateDockPreview(StickyNoteData source)
        {
            DockTarget target = FindDockTarget(source);
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
            _dockPreviewIndicator.ShowSeam(new Rectangle(parent.X,
                parent.Y + parent.Height - 3, parent.Width, 6));
        }

        private DockTarget FindDockTarget(StickyNoteData source)
        {
            if (source == null) return null;
            if (!String.IsNullOrEmpty(source.DockParentId) &&
                !_activeNoteDetached) return null;
            HashSet<string> activeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _activeDockGroup)
                activeIds.Add(note.Id);
            DockTarget best = null;
            int bestScore = Int32.MaxValue;
            foreach (StickyNoteData candidate in _notes.GetAll())
            {
                if (candidate == null || !candidate.Visible ||
                    String.Equals(candidate.Id, source.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    activeIds.Contains(candidate.Id))
                    continue;
                Rectangle sourceBounds = new Rectangle(source.X, source.Y,
                    source.Width, source.Height);
                Rectangle candidateBounds = new Rectangle(candidate.X,
                    candidate.Y, candidate.Width, candidate.Height);
                if (!CanDockBelow(sourceBounds, candidateBounds, 20)) continue;
                int score = Math.Abs(source.Y - candidateBounds.Bottom) * 10 +
                    Math.Min(Math.Abs(source.X - candidate.X),
                        Math.Abs(sourceBounds.Right - candidateBounds.Right));
                if (score >= bestScore) continue;
                best = new DockTarget();
                best.ParentNoteId = candidate.Id;
                best.ExistingChildNoteId = FindDockChild(candidate.Id,
                    activeIds);
                bestScore = score;
            }
            if (best != null && !CanSafelyCombineDockComponents(best, source))
                return null;
            return best;
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
            StickyNoteWindow active = GetLegacyWindow(ActiveDragNote());
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
            StickyNoteWindow root = null;
            if (snapshot.Count > 0)
                _noteWindows.TryGetValue(snapshot[0].Id, out root);
            Point rootAnchor = root == null ? new Point(note.X, note.Y) :
                root.Location;
            int rootWidth = root == null ? note.Width : root.Width;
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
                LayoutDockChain(snapshot, rootAnchor.X, rootAnchor.Y,
                    rootWidth);
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
