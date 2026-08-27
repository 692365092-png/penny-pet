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
                    member.ApplyGroupTopMost(alwaysOnTop);
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
            ClearDockPreview();
            ClearSplitGuide();
            _activeNoteDrag = source;
            _activeNoteDragStartLocation = source.Location;
            _activeNoteDragLastLocation = source.Location;
            _activeNoteDragStartedUtc = DateTime.UtcNow;
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderSeed = null;
            SetActiveDockGroup(BuildDockComponent(source.Data));
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
                source.Data.DockParentId, _activeDockGroup.Count);
            if (_activeNoteSplitEligible) ShowSplitGuide(source);
        }

        private void StickyNoteHeaderDragMoved(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_movingDockGroup || source == null ||
                !Object.ReferenceEquals(source, _activeNoteDrag)) return;
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
                if (!String.IsNullOrEmpty(source.Data.DockParentId))
                {
                    List<StickyNoteData> beforeSplit =
                        BuildDockChainOrderIncludingHidden(source.Data);
                    int splitIndex = beforeSplit.FindIndex(
                        delegate(StickyNoteData note)
                        {
                            return String.Equals(note.Id, source.Data.Id,
                                StringComparison.OrdinalIgnoreCase);
                        });
                    _noteWindows.TryGetValue(source.Data.DockParentId,
                        out connected);
                    if (splitIndex > 0)
                    {
                        // A held middle header extracts exactly that note.
                        // Reconnect the members before and after it into one
                        // stack instead of detaching every descendant.
                        StickyDockOperations.ExtractSingleDockMember(
                            beforeSplit, source.Data);
                    }
                    else StickyDockGroups.ClearMembership(source.Data);
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
                    SetActiveDockGroup(BuildDockComponent(source.Data));
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
                UpdateSplitGuide(source);
            UpdateDockPreview(source);
        }

        private void StickyNoteHeaderDragCompleted(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (source == null || !Object.ReferenceEquals(source,
                _activeNoteDrag)) return;
            DockTarget target = FindDockTarget(source);
            if (target != null && !CanSafelyCombineDockComponents(
                target, source)) target = null;
            if (target != null && target.Parent != null)
            {
                StickyNoteWindow parent = target.Parent;
                List<StickyNoteData> targetOrder = BuildDockChainOrder(
                    parent.Data);
                List<StickyNoteData> targetSnapshot =
                    BuildDockChainOrderIncludingHidden(parent.Data);
                List<StickyNoteData> sourceSnapshot =
                    BuildDockChainOrderIncludingHidden(source.Data);
                StickyNoteWindow targetRoot = targetOrder.Count == 0 ? parent :
                    _noteWindows[targetOrder[0].Id];
                Point targetRootAnchor = targetRoot.Location;
                int targetRootWidth = targetRoot.Width;
                StickyNoteData tail = FindActiveDockTail(source.Data);
                StickyNoteWindow tailForm;
                if (tail == null || !_noteWindows.TryGetValue(tail.Id,
                    out tailForm) || tailForm.IsDisposed) tailForm = source;
                List<StickyNoteData> mergedSnapshot =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        targetSnapshot, parent.Data, sourceSnapshot);
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(mergedSnapshot, targetRootAnchor.X,
                        targetRootAnchor.Y, targetRootWidth);
                    bool groupTopMost = targetSnapshot.Count == 0
                        ? parent.Data.AlwaysOnTop :
                        targetSnapshot[0].AlwaysOnTop;
                    foreach (StickyNoteData note in mergedSnapshot)
                    {
                        note.AlwaysOnTop = groupTopMost;
                        StickyNoteWindow member;
                        if (_noteWindows.TryGetValue(note.Id, out member) &&
                            member != null && !member.IsDisposed)
                            member.ApplyGroupTopMost(groupTopMost);
                    }
                }
                finally { _synchronizingDockLayout = false; }
                if (target.ExistingChild != null &&
                    !target.ExistingChild.IsDisposed)
                {
                    ShowTransientDockPulse(new Rectangle(tailForm.Left,
                        tailForm.Bounds.Bottom - 3, tailForm.Width, 6),
                        Color.FromArgb(32, 160, 255));
                }
                ShowTransientDockPulse(new Rectangle(parent.Left,
                    parent.Bounds.Bottom - 3, parent.Width, 6),
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
            _activeNoteDrag = null;
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
            List<StickyNoteData> visibleNotes = new List<StickyNoteData>();
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData note in ordered)
            {
                StickyNoteWindow form;
                if (!_noteWindows.TryGetValue(note.Id, out form) ||
                    form.IsDisposed || !form.Visible) continue;
                visibleNotes.Add(note);
                sizes.Add(new Size(form.Width, form.Height));
            }
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                left, top, width);
            for (int index = 0; index < visibleNotes.Count; index++)
            {
                StickyNoteData note = visibleNotes[index];
                StickyNoteWindow form = _noteWindows[note.Id];
                form.Bounds = layout[index];
                note.X = form.Left;
                note.Y = form.Top;
                note.Width = form.Width;
                note.Height = form.Height;
            }
        }

        internal static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width)
        {
            return CalculateUnifiedDockLayout(sizes, left, top, width, 1F);
        }

        private static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width, float scale)
        {
            List<Rectangle> result = new List<Rectangle>();
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int maximumHeight = Math.Max(minimumHeight,
                (int)Math.Round(700 * scale));
            int normalizedWidth = Math.Max(minimumWidth,
                Math.Min(maximumWidth, width));
            int y = top;
            if (sizes == null) return result;
            foreach (Size size in sizes)
            {
                int height = Math.Max(minimumHeight,
                    Math.Min(maximumHeight, size.Height));
                result.Add(new Rectangle(left, y, normalizedWidth, height));
                y += height;
            }
            return result;
        }

        private bool CanSafelyCombineDockComponents(DockTarget target,
            StickyNoteWindow source)
        {
            if (target == null || target.Parent == null || source == null)
                return false;
            List<StickyNoteData> targetOrder = BuildDockChainOrder(
                target.Parent.Data);
            if (targetOrder.Count == 0) return false;
            StickyNoteWindow root;
            if (!_noteWindows.TryGetValue(targetOrder[0].Id, out root) ||
                root == null || root.IsDisposed) return false;
            List<int> heights = new List<int>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in targetOrder)
            {
                StickyNoteWindow form;
                if (seen.Add(note.Id) && _noteWindows.TryGetValue(note.Id,
                    out form) && form != null && !form.IsDisposed &&
                    form.Visible) heights.Add(form.Height);
            }
            foreach (StickyNoteData note in BuildDockChainOrder(source.Data))
            {
                StickyNoteWindow form;
                if (seen.Add(note.Id) && _noteWindows.TryGetValue(note.Id,
                    out form) && form != null && !form.IsDisposed &&
                    form.Visible) heights.Add(form.Height);
            }
            return StickyDockOperations.IsDockCoordinateRangeSafe(root.Top,
                heights);
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
                        member.ApplyGroupTopMost(groupTopMost);
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
                _activeNoteDrag != null ||
                source == null || source.IsDisposed) return;
            source.Data.Width = source.Width;
            source.Data.Height = source.Height;
            List<StickyNoteData> ordered = BuildDockChainOrder(source.Data);
            if (ordered.Count <= 1)
            {
                source.SetDockResizeRole(false, true, true);
                return;
            }
            if (e.WidthChanged && source.DockHorizontalResizeActive)
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
            int left = e.WidthChanged && source.DockHorizontalResizeActive
                ? source.DockHorizontalGroupLeft(source.Width)
                : e.WidthChanged ? source.Left : root.Left;
            _synchronizingDockLayout = true;
            try
            {
                int sourceIndex = ordered.FindIndex(
                    delegate(StickyNoteData note)
                    {
                        return String.Equals(note.Id, source.Data.Id,
                            StringComparison.OrdinalIgnoreCase);
                    });
                if (e.HeightChanged && source.DockDividerResizeActive &&
                    sourceIndex >= 0 && sourceIndex < ordered.Count - 1)
                {
                    StickyNoteWindow lower;
                    if (_noteWindows.TryGetValue(ordered[sourceIndex + 1].Id,
                        out lower) && lower != null && !lower.IsDisposed)
                    {
                        Size adjusted = CalculateDockDividerHeights(
                            (int)Math.Round(e.PreviousSize.Height),
                            source.Height, lower.Height);
                        source.Height = adjusted.Width;
                        lower.Height = adjusted.Height;
                        source.Data.Height = source.Height;
                        lower.Data.Height = lower.Height;
                    }
                }
                LayoutDockChain(ordered, left, top, source.Width);
            }
            finally { _synchronizingDockLayout = false; }
            RefreshDockResizeRoles();
        }

        private void StickyNoteLocationChanged(object sender, EventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDrag != null || source == null ||
                source.IsDisposed) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(source.Data);
            if (ordered.Count <= 1) return;
            if (source.DockHorizontalResizeActive)
            {
                source.Data.X = source.Left;
                source.Data.Width = source.Width;
                return;
            }
            StickyNoteWindow root;
            if (!_noteWindows.TryGetValue(ordered[0].Id, out root) ||
                root.IsDisposed) return;
            int left = source.DockHorizontalResizeActive
                ? source.DockHorizontalGroupLeft(source.Width)
                : Object.ReferenceEquals(source, root) ? source.Left : root.Left;
            int top = Object.ReferenceEquals(source, root)
                ? source.Top : root.Top;
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, left, top, source.Width);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void StickyNoteDockHorizontalResizing(object sender,
            DockHorizontalResizeEventArgs e)
        {
            StickyNoteWindow source = sender as StickyNoteWindow;
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDrag != null || source == null ||
                source.IsDisposed || e == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(source.Data);
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
            const int minimum = 220;
            const int maximum = 700;
            int oldUpper = Math.Max(minimum, Math.Min(maximum,
                previousUpperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum,
                currentLowerHeight));
            int total = oldUpper + lower;
            int minimumUpper = Math.Max(minimum, total - maximum);
            int maximumUpper = Math.Min(maximum, total - minimum);
            int upper = Math.Max(minimumUpper, Math.Min(maximumUpper,
                requestedUpperHeight));
            return new Size(upper, total - upper);
        }

        internal static Size CalculateDockDividerRange(int upperHeight,
            int lowerHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            int upper = Math.Max(minimum, Math.Min(maximum, upperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum, lowerHeight));
            int total = upper + lower;
            return new Size(Math.Max(minimum, total - maximum),
                Math.Min(maximum, total - minimum));
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
            int dx = 0;
            int dy = 0;
            const int minimumVisibleWidth = 64;
            if (header.Right < work.Left + minimumVisibleWidth)
                dx = work.Left + minimumVisibleWidth - header.Right;
            else if (header.Left > work.Right - minimumVisibleWidth)
                dx = work.Right - minimumVisibleWidth - header.Left;
            if (header.Top < work.Top) dy = work.Top - header.Top;
            else if (header.Bottom > work.Bottom)
                dy = work.Bottom - header.Bottom;
            return new Point(dx, dy);
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

        private void ShowSplitGuide(StickyNoteWindow source)
        {
            ClearSplitGuide();
            if (source == null || source.IsDisposed) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.Data.DockParentId))
            {
                StickyNoteWindow parent;
                if (_noteWindows.TryGetValue(source.Data.DockParentId,
                    out parent) && parent != null && !parent.IsDisposed)
                    seam = new Rectangle(parent.Left,
                        parent.Bounds.Bottom - 3, parent.Width, 6);
            }
            if (seam.IsEmpty) return;
            _splitGuideIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(255, 151, 62), 0);
            _splitGuideIndicator.ShowSeam(seam);
        }

        private void UpdateSplitGuide(StickyNoteWindow source)
        {
            if (_splitGuideIndicator == null ||
                _splitGuideIndicator.IsDisposed || source == null) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.Data.DockParentId))
            {
                StickyNoteWindow parent;
                if (_noteWindows.TryGetValue(source.Data.DockParentId,
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

        private void UpdateDockPreview(StickyNoteWindow source)
        {
            DockTarget target = FindDockTarget(source);
            StickyNoteWindow parent = target == null ? null : target.Parent;
            StickyNoteWindow child = target == null ? null : target.ExistingChild;
            if (Object.ReferenceEquals(parent, _dockPreviewParent) &&
                Object.ReferenceEquals(child, _dockPreviewChild)) return;
            ClearDockPreview();
            _dockPreviewParent = parent;
            _dockPreviewChild = child;
            if (parent == null) return;
            source.SetDockPreview(true, false);
            parent.SetDockPreview(true, true);
            if (child != null && !child.IsDisposed)
                child.SetDockPreview(true, true);
            _dockPreviewIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(32, 160, 255), 0);
            _dockPreviewIndicator.ShowSeam(new Rectangle(parent.Left,
                parent.Bounds.Bottom - 3, parent.Width, 6));
        }

        private DockTarget FindDockTarget(StickyNoteWindow source)
        {
            if (source == null || source.IsDisposed) return null;
            if (!String.IsNullOrEmpty(source.Data.DockParentId) &&
                !_activeNoteDetached) return null;
            HashSet<string> activeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _activeDockGroup)
                activeIds.Add(note.Id);
            DockTarget best = null;
            int bestScore = Int32.MaxValue;
            foreach (StickyNoteWindow candidate in _noteWindows.Values)
            {
                if (candidate == null || candidate.IsDisposed ||
                    !candidate.Visible || Object.ReferenceEquals(candidate,
                        source) || activeIds.Contains(candidate.Data.Id))
                    continue;
                if (!CanDockBelow(source.Bounds, candidate.Bounds, 20)) continue;
                int score = Math.Abs(source.Top - candidate.Bounds.Bottom) * 10 +
                    Math.Min(Math.Abs(source.Left - candidate.Left),
                        Math.Abs(source.Bounds.Right - candidate.Bounds.Right));
                if (score >= bestScore) continue;
                best = new DockTarget();
                best.Parent = candidate;
                best.ExistingChild = FindDockChild(candidate.Data.Id,
                    activeIds);
                bestScore = score;
            }
            if (best != null && !CanSafelyCombineDockComponents(best,
                source)) return null;
            return best;
        }

        private StickyNoteWindow FindDockChild(string parentId,
            HashSet<string> ignoredIds)
        {
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (ignoredIds != null && ignoredIds.Contains(note.Id)) continue;
                if (String.Equals(note.DockParentId, parentId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    StickyNoteWindow child;
                    if (_noteWindows.TryGetValue(note.Id, out child) &&
                        child != null && !child.IsDisposed && child.Visible)
                        return child;
                }
            }
            return null;
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
            if (_activeNoteDrag != null && !_activeNoteDrag.IsDisposed)
                _activeNoteDrag.SetDockPreview(false, false);
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
