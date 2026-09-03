using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PennyPet
{
    internal enum StickyTabSide
    {
        Left,
        Right
    }

    internal sealed class StickyNoteTabsForm : Form
    {
        internal const int TabWidth = 146;
        internal const int TabHeight = 34;
        internal const int TabGap = 2;
        // The sprite canvas contains roughly 40 px of transparent padding on
        // each side.  A small negative window gap moves tabs into that empty
        // canvas and halves the visible distance to the character silhouette.
        internal const int PetGap = -20;
        internal const int PreviewInsertionGap = 14;
        // Pull the source tab toward the pet/target strip while retaining its
        // original row. This is a horizontal cue, not a vertical list move.
        internal const int DragSourceVisualOffset = 10;
        internal const string DragDataFormat = "PennyPet.StickyNoteTabId";

        private DisplayScale _displayScale = new DisplayScale(1.0, 1.0);

        private static readonly StickyTabDropSession DragSession =
            new StickyTabDropSession();
        private static readonly List<StickyNoteTabsForm> LiveForms =
            new List<StickyNoteTabsForm>();

        private readonly StickyTabSide _side;
        private readonly Action<string> _openNote;
        private readonly Action<string> _deleteNote;
        private readonly Action<string, int> _reorderNote;
        private readonly ToolTip _toolTip;
        private readonly System.Windows.Forms.Timer _layoutAnimationTimer;
        private int _globalStartIndex;
        private int _dropIndex = -1;
        private int _normalHeight = 1;
        private int _dragPointerY;
        private string _previewDraggedNoteId;
        private StickyNoteTabControl _rolloverPreviewTab;
        private StickyNoteTabControl _hiddenBoundaryTab;
        private int _crossSideVisualDropIndex = -1;
        private bool _restoringLayout;
        private bool _sourceHorizontallyOffset;
        private int _sourceNormalLeft;
        private bool _ownedResourcesDisposed;

        internal void SetDisplayScale(DisplayScale scale)
        {
            _displayScale = scale;
        }

        private int PhysicalTabWidth
        {
            get { return (int)Math.Round(TabWidth * _displayScale.X,
                MidpointRounding.AwayFromZero); }
        }

        private int PhysicalTabHeight
        {
            get { return (int)Math.Round(TabHeight * _displayScale.Y,
                MidpointRounding.AwayFromZero); }
        }

        private int PhysicalTabGap
        {
            get { return (int)Math.Round(TabGap * _displayScale.Y,
                MidpointRounding.AwayFromZero); }
        }

        internal static int PhysicalTabWidthFor(DisplayScale scale)
        {
            return (int)Math.Round(TabWidth * scale.X,
                MidpointRounding.AwayFromZero);
        }

        internal static int PhysicalOverlapForWidth(DisplayScale scale,
            int physicalPetWidth)
        {
            int logicalPetWidth = (int)Math.Round(
                physicalPetWidth / scale.X, MidpointRounding.AwayFromZero);
            int logicalOverlap =
                StickyDockGeometry.CalculateSideTabOverlap(logicalPetWidth);
            return (int)Math.Round(logicalOverlap * scale.X,
                MidpointRounding.AwayFromZero);
        }

        public StickyNoteTabsForm(StickyTabSide side,
            Action<string> openNote)
            : this(side, openNote, null, null)
        {
        }

        public StickyNoteTabsForm(StickyTabSide side,
            Action<string> openNote,
            Action<string> deleteNote,
            Action<string, int> reorderNote)
        {
            _side = side;
            _openNote = openNote;
            _deleteNote = deleteNote;
            _reorderNote = reorderNote;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            ClientSize = new Size(TabWidth, 1);
            AllowDrop = true;
            DragEnter += TabsDragEnter;
            DragOver += TabsDragOver;
            DragLeave += TabsDragLeave;
            DragDrop += TabsDragDrop;
            _toolTip = new ToolTip();
            _toolTip.ShowAlways = true;
            _layoutAnimationTimer = new System.Windows.Forms.Timer();
            _layoutAnimationTimer.Interval = 16;
            _layoutAnimationTimer.Tick += LayoutAnimationTick;
            LiveForms.Add(this);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                value.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return value;
            }
        }

        public void SetNotes(IList<SideTabSnapshot> notes)
        {
            SetNotes(notes, 0);
        }

        public void SetNotes(IList<SideTabSnapshot> notes,
            int globalStartIndex)
        {
            ClearCrossSideBoundaryPreview();
            RestoreSourceHorizontalOffset();
            _globalStartIndex = Math.Max(0, globalStartIndex);
            _dropIndex = -1;
            _previewDraggedNoteId = null;
            _restoringLayout = false;
            _layoutAnimationTimer.Stop();
            SuspendLayout();
            foreach (Control control in new List<Control>(ControlsAsList()))
            {
                Controls.Remove(control);
                control.Dispose();
            }
            int count = notes == null ? 0 : notes.Count;
            int tabWidth = PhysicalTabWidth;
            int tabHeight = PhysicalTabHeight;
            int tabGap = PhysicalTabGap;
            _normalHeight = Math.Max(1,
                count * (tabHeight + tabGap) - tabGap);
            ClientSize = new Size(tabWidth, _normalHeight);
            for (int i = 0; i < count; i++)
            {
                SideTabSnapshot note = notes[i];
                StickyNoteTabControl tab = new StickyNoteTabControl(note, _side,
                    _openNote, _deleteNote);
                tab.ListIndex = i;
                tab.Bounds = new Rectangle(0, i * (tabHeight + tabGap),
                    tabWidth, tabHeight);
                tab.AllowDrop = true;
                tab.DragEnter += TabsDragEnter;
                tab.DragOver += TabsDragOver;
                tab.DragLeave += TabsDragLeave;
                tab.DragDrop += TabsDragDrop;
                _toolTip.SetToolTip(tab, note.DisplayTitle + "\n单击展开便利贴");
                Controls.Add(tab);
            }
            ResumeLayout(false);
            if (count == 0)
            {
                Hide();
                return;
            }
            if (!Visible) Show();
        }

        public void ShowNear(Rectangle petBounds, Rectangle workArea)
        {
            if (Controls.Count == 0) return;
            double sx = _displayScale.X;
            double sy = _displayScale.Y;
            int logicalStripHeight = Math.Max(1,
                Controls.Count * (TabHeight + TabGap) - TabGap);
            DockPoint logicalLocation =
                StickyDockGeometry.CalculateSideTabLocation(
                new DockRect(Round(petBounds.Left / sx),
                    Round(petBounds.Top / sy), Round(petBounds.Width / sx),
                    Round(petBounds.Height / sy)),
                new DockRect(Round(workArea.Left / sx),
                    Round(workArea.Top / sy), Round(workArea.Width / sx),
                    Round(workArea.Height / sy)),
                new DockSize(TabWidth, logicalStripHeight),
                _side == StickyTabSide.Left,
                StickyDockGeometry.CalculateSideTabOverlap(
                    Round(petBounds.Width / sx)),
                _sourceHorizontallyOffset && _side == StickyTabSide.Right
                    ? DragSourceVisualOffset : 0);
            Point physical = new Point(Round(logicalLocation.X * sx),
                Round(logicalLocation.Y * sy));
            Location = physical;
            if (_sourceHorizontallyOffset)
                _sourceNormalLeft = _side == StickyTabSide.Right
                    ? physical.X + Round(DragSourceVisualOffset * sx)
                    : physical.X;
            if (!Visible) Show();
        }

        private static int Round(double value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        internal static int PetOverlapForWidth(int petWidth)
        {
            // Idle art begins about 44 px inside its 192 px sprite cell. The
            // overlap scales with the pet so the visible tab-to-character gap
            // stays at half of the old fixed-20 px result at every zoom level.
            return StickyDockGeometry.CalculateSideTabOverlap(petWidth);
        }

        internal static int ScreenCapacity(Rectangle workArea)
        {
            return StickyDockGeometry.CalculateSideTabScreenCapacity(workArea.Height,
                TabHeight, TabGap);
        }

        internal static int PreferredLeftCapacity(int petHeight,
            Rectangle workArea)
        {
            return StickyDockGeometry.CalculatePreferredSideTabCount(petHeight,
                workArea.Height, TabHeight, TabGap);
        }

        internal static int CalculateLeftCount(int totalCount, int petHeight,
            Rectangle workArea)
        {
            return StickyDockGeometry.CalculateLeftSideTabCount(totalCount,
                petHeight, workArea.Height, TabHeight, TabGap);
        }

        internal static bool IsLayoutSplitCurrent(int leftCount,
            int rightCount, int petHeight, Rectangle workArea)
        {
            int totalCount = Math.Max(0, leftCount) + Math.Max(0, rightCount);
            int desiredLeftCount = CalculateLeftCount(totalCount, petHeight,
                workArea);
            return leftCount == desiredLeftCount &&
                rightCount == totalCount - desiredLeftCount;
        }

        private IEnumerable<Control> ControlsAsList()
        {
            foreach (Control control in Controls) yield return control;
        }

        private void TabsDragEnter(object sender, DragEventArgs e)
        {
            string movedId = TryGetDraggedNoteId(e.Data);
            e.Effect = !String.IsNullOrEmpty(movedId)
                ? DragDropEffects.Move : DragDropEffects.None;
            if (!String.IsNullOrEmpty(movedId))
                ActivateExclusiveDropTarget(this, movedId);
        }

        private void TabsDragOver(object sender, DragEventArgs e)
        {
            string movedId = TryGetDraggedNoteId(e.Data);
            if (String.IsNullOrEmpty(movedId))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            // OLE does not reliably deliver DragLeave to the old child/form
            // when the pointer crosses the transparent pet window. Clear the
            // other strip proactively so its source never overlaps an old
            // insertion animation or leaves a second purple guide behind.
            ActivateExclusiveDropTarget(this, movedId);
            if (DragSession.IsSource(this))
                ClearRolloverPreviewTab();
            Point point = PointToClient(new Point(e.X, e.Y));
            _dragPointerY = point.Y;
            int next = CalculateDropIndex(point.Y, Controls.Count);
            if (next == _dropIndex && IsSameNote(_previewDraggedNoteId,
                movedId)) return;
            _dropIndex = next;
            _previewDraggedNoteId = movedId;
            _restoringLayout = false;
            if (ClientSize.Height != _normalHeight + PreviewInsertionGap)
                ClientSize = new Size(CurrentCanvasWidth,
                    _normalHeight + PreviewInsertionGap);
            // A cross-side target must show its existing tabs continuously.
            // Snapping that short preview avoids the WinForms transparent-
            // child repaint hole that made the first target tab disappear.
            if (DragSession.IsSource(this))
            {
                _layoutAnimationTimer.Start();
            }
            else ApplyCrossSidePreviewImmediately();
            Invalidate();
            if (IsHandleCreated) Update();
        }

        private static bool IsSameNote(string leftId, string rightId)
        {
            return !String.IsNullOrEmpty(leftId) &&
                !String.IsNullOrEmpty(rightId) &&
                String.Equals(leftId, rightId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool ContainsNote(string noteId)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && IsSameNote(tab.Snapshot.NoteId, noteId))
                    return true;
            }
            return false;
        }

        private void ApplyCrossSidePreviewImmediately()
        {
            _layoutAnimationTimer.Stop();
            ClearHiddenBoundaryTab();
            _crossSideVisualDropIndex = -1;
            StickyNoteTabsForm sourceForm = DragSession.Source as
                StickyNoteTabsForm;
            StickyNoteTabControl boundary = null;
            int visualInsertion = _dropIndex;
            bool crossesBoundary = sourceForm != null &&
                !Object.ReferenceEquals(sourceForm, this) &&
                ((_side == StickyTabSide.Right && _dropIndex > 0) ||
                 (_side == StickyTabSide.Left &&
                    _dropIndex < Controls.Count));
            if (crossesBoundary)
            {
                int boundaryIndex = _side == StickyTabSide.Right
                    ? 0 : Controls.Count - 1;
                boundary = TabAtListIndex(boundaryIndex);
                if (boundary != null)
                {
                    _hiddenBoundaryTab = boundary;
                    boundary.Visible = false;
                    visualInsertion = _side == StickyTabSide.Right
                        ? Math.Max(0, _dropIndex - 1) : _dropIndex;
                    _crossSideVisualDropIndex = visualInsertion;
                    sourceForm.ShowBoundaryRollover(boundary.Snapshot,
                        sourceForm._side == StickyTabSide.Right);
                }
            }
            if (boundary == null && sourceForm != null)
                sourceForm.ClearRolloverPreviewTab();
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null || Object.ReferenceEquals(tab, boundary)) continue;
                int compactIndex = tab.ListIndex;
                if (boundary != null && _side == StickyTabSide.Right)
                    compactIndex--;
                tab.Top = compactIndex * (TabHeight + TabGap);
                if (compactIndex >= visualInsertion)
                    tab.Top += PreviewInsertionGap;
                tab.Visible = true;
                tab.IsDragSource = false;
                tab.Invalidate();
            }
        }

        private StickyNoteTabControl TabAtListIndex(int index)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && tab.ListIndex == index) return tab;
            }
            return null;
        }

        private void TabsDragLeave(object sender, EventArgs e)
        {
            // Moving between child tab controls also raises DragLeave.  Keep
            // the preview until the pointer really leaves the complete strip.
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return;
            ShowSourceOnly(DragSession.ActiveNoteId);
        }

        private void TabsDragDrop(object sender, DragEventArgs e)
        {
            string movedId = TryGetDraggedNoteId(e.Data);
            int destination = _globalStartIndex +
                Math.Max(0, _dropIndex < 0 ? Controls.Count : _dropIndex);
            ResetDropPreview(false);
            if (String.IsNullOrEmpty(movedId) || _reorderNote == null) return;

            // Do not post this with BeginInvoke: the OLE nested message loop
            // may dispatch it before DoDragDrop returns. The source control
            // completes the session after OLE has fully unwound.
            Action<string, int> reorder = _reorderNote;
            StickyNoteTabsForm target = this;
            DragSession.QueueCommit(movedId, delegate
            {
                if (target.IsDisposed) return;
                reorder(movedId, destination);
            });
        }

        private static string TryGetDraggedNoteId(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DragDataFormat, false))
                return String.Empty;
            string id = data.GetData(DragDataFormat, false) as string;
            return DragSession.IsActiveNote(id) ? id : String.Empty;
        }

        internal static void BeginDragSession(string noteId)
        {
            StickyNoteTabsForm source = null;
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form != null && !form.IsDisposed &&
                    form.ContainsNote(noteId))
                {
                    source = form;
                    break;
                }
            }
            BeginDragSession(noteId, source);
        }

        internal static void BeginDragSession(string noteId,
            StickyNoteTabsForm source)
        {
            DragSession.Begin(noteId, source);
            ShowSourceOnly(noteId);
        }

        private static void ActivateExclusiveDropTarget(
            StickyNoteTabsForm target, string noteId)
        {
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form == null || form.IsDisposed ||
                    Object.ReferenceEquals(form, target)) continue;
                form.HoldSourceVisual(noteId,
                    !DragSession.IsSource(form));
            }
        }

        private static void ShowSourceOnly(string noteId)
        {
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form != null && !form.IsDisposed)
                    form.HoldSourceVisual(noteId, true);
            }
        }

        internal static void EndDragSession(string noteId)
        {
            // Clear both strips before a successful reorder rebuilds them.
            // This also covers cancelled drops and prevents a stale insertion
            // line from surviving on the strip that did not own the source.
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form != null && !form.IsDisposed)
                    form.ResetDropPreview(false);
            }
            DragSession.Complete(noteId);
        }

        internal static int CalculateDropIndex(int pointerY, int count)
        {
            return Math.Max(0, Math.Min(count,
                (pointerY + (TabHeight + TabGap) / 2) /
                    (TabHeight + TabGap)));
        }

        internal static int PreviewTargetTop(int listIndex, int sourceIndex,
            int dropIndex)
        {
            if (listIndex == sourceIndex)
                return listIndex * (TabHeight + TabGap);
            // sourceIndex == -1 means the source belongs to the opposite
            // strip. Nothing in this target strip may be compacted away.
            int compactIndex = sourceIndex >= 0 && listIndex > sourceIndex
                ? listIndex - 1 : listIndex;
            int insertion = dropIndex;
            if (sourceIndex >= 0 && sourceIndex < insertion) insertion--;
            insertion = Math.Max(0, insertion);
            int top = compactIndex * (TabHeight + TabGap);
            if (compactIndex >= insertion) top += PreviewInsertionGap;
            return top;
        }

        private void LayoutAnimationTick(object sender, EventArgs e)
        {
            int sourceIndex = -1;
            StickyNoteTabControl sourceTab = null;
            string sourceNoteId = DragSession.ActiveNoteId ??
                _previewDraggedNoteId;
            bool canOwnSource = String.IsNullOrEmpty(DragSession.ActiveNoteId) ||
                DragSession.IsSource(this);
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (canOwnSource && tab != null &&
                    IsSameNote(tab.Snapshot.NoteId, sourceNoteId))
                {
                    sourceIndex = tab.ListIndex;
                    sourceTab = tab;
                }
            }
            // Keep the source above every sibling even if Windows misses a
            // DragLeave and the old strip is still finishing its animation.
            if (sourceTab != null && Controls.GetChildIndex(sourceTab) != 0)
                sourceTab.BringToFront();
            bool settled = true;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                int target;
                bool isSource = sourceIndex >= 0 &&
                    tab.ListIndex == sourceIndex;
                if (_restoringLayout || _dropIndex < 0)
                    target = tab.ListIndex * (TabHeight + TabGap);
                else if (isSource)
                {
                    // Keep the source visible in its original strip. A small
                    // horizontal pull marks it as the item being dragged;
                    // its row never collapses into a neighbouring tab.
                    target = tab.ListIndex * (TabHeight + TabGap);
                }
                else
                    target = PreviewTargetTop(tab.ListIndex, sourceIndex,
                        _dropIndex);
                int next = AnimateCoordinate(tab.Top, target);
                if (next != target) settled = false;
                if (tab.Top != next) tab.Top = next;
                tab.IsDragSource = isSource;
            }
            if (!settled) return;
            if (_restoringLayout)
            {
                _restoringLayout = false;
                bool keepSourceOffset = !String.IsNullOrEmpty(
                    DragSession.ActiveNoteId) &&
                    sourceIndex >= 0;
                _previewDraggedNoteId = keepSourceOffset
                    ? DragSession.ActiveNoteId : null;
                // Keep a stable transparent canvas for the whole OLE drag.
                // Resizing a TransparencyKey form while child tabs animate
                // can make Windows temporarily omit a moving child window.
                bool dragStillActive = !String.IsNullOrEmpty(
                    DragSession.ActiveNoteId);
                ClientSize = new Size(CurrentCanvasWidth, _normalHeight +
                    (dragStillActive ? PreviewInsertionGap : 0));
                foreach (Control control in Controls)
                {
                    StickyNoteTabControl tab = control as StickyNoteTabControl;
                    if (tab != null)
                        tab.IsDragSource = keepSourceOffset &&
                            tab.ListIndex == sourceIndex;
                }
            }
            _layoutAnimationTimer.Stop();
        }

        private static int AnimateCoordinate(int current, int target)
        {
            int difference = target - current;
            if (difference == 0) return target;
            int step = Math.Max(2, (int)Math.Ceiling(Math.Abs(difference) * 0.45));
            return current + Math.Sign(difference) * Math.Min(step,
                Math.Abs(difference));
        }

        private void ResetDropPreview(bool animateBack)
        {
            _dropIndex = -1;
            Invalidate();
            if (IsHandleCreated) Update();
            if (!animateBack)
            {
                _previewDraggedNoteId = null;
                _restoringLayout = false;
                _layoutAnimationTimer.Stop();
                ClearCrossSideBoundaryPreview();
                RestoreSourceHorizontalOffset();
                foreach (Control control in Controls)
                {
                    StickyNoteTabControl tab = control as StickyNoteTabControl;
                    if (tab == null) continue;
                    tab.Top = tab.ListIndex * (TabHeight + TabGap);
                    tab.Left = 0;
                    tab.IsDragSource = false;
                }
                ClientSize = new Size(TabWidth, _normalHeight);
                return;
            }
            _restoringLayout = true;
            _layoutAnimationTimer.Start();
        }

        private void HoldSourceVisual(string noteId,
            bool clearBoundaryRollover)
        {
            if (clearBoundaryRollover)
                ClearCrossSideBoundaryPreview();
            _dropIndex = -1;
            _restoringLayout = false;
            _layoutAnimationTimer.Stop();
            StickyNoteTabControl source = null;
            if (DragSession.IsSource(this))
            {
                foreach (Control control in Controls)
                {
                    StickyNoteTabControl tab = control as StickyNoteTabControl;
                    if (tab != null && IsSameNote(tab.Snapshot.NoteId,
                        noteId))
                        source = tab;
                }
            }
            _previewDraggedNoteId = source == null ? null : noteId;
            if (source != null) ApplySourceHorizontalOffset(source);
            else RestoreSourceHorizontalOffset();
            bool preserveRollover = !clearBoundaryRollover &&
                _rolloverPreviewTab != null;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                bool isSource = Object.ReferenceEquals(tab, source);
                if (!preserveRollover)
                    tab.Top = tab.ListIndex * (TabHeight + TabGap);
                tab.IsDragSource = isSource;
            }
            if (source != null) source.BringToFront();
            // Both strips reserve the same transparent insertion area until
            // DoDragDrop ends. Switching target sides therefore never shrinks
            // a top-level transparent form in the middle of the animation.
            bool dragActive = !String.IsNullOrEmpty(DragSession.ActiveNoteId);
            if (!preserveRollover)
                ClientSize = new Size(CurrentCanvasWidth, _normalHeight +
                    (dragActive ? PreviewInsertionGap : 0));
            Invalidate();
            if (IsHandleCreated) Update();
        }

        private int CurrentCanvasWidth
        {
            get
            {
                return TabWidth + (_sourceHorizontallyOffset
                    ? DragSourceVisualOffset : 0);
            }
        }

        private void ApplySourceHorizontalOffset(StickyNoteTabControl source)
        {
            if (!_sourceHorizontallyOffset)
            {
                _sourceNormalLeft = Left;
                _sourceHorizontallyOffset = true;
                if (_side == StickyTabSide.Right)
                    Left -= DragSourceVisualOffset;
            }
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                bool isSource = Object.ReferenceEquals(tab, source);
                tab.Left = _side == StickyTabSide.Left
                    ? (isSource ? DragSourceVisualOffset : 0)
                    : (isSource ? 0 : DragSourceVisualOffset);
            }
        }

        private void RestoreSourceHorizontalOffset()
        {
            if (_sourceHorizontallyOffset)
            {
                Left = _sourceNormalLeft;
                _sourceHorizontallyOffset = false;
            }
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null) tab.Left = 0;
            }
        }

        private void ShowBoundaryRollover(SideTabSnapshot note, bool atTop)
        {
            if (note == null) return;
            if (_rolloverPreviewTab == null ||
                !IsSameNote(_rolloverPreviewTab.Snapshot.NoteId, note.NoteId))
            {
                ClearRolloverPreviewTab();
                _rolloverPreviewTab = new StickyNoteTabControl(note, _side,
                    _openNote, _deleteNote);
                _rolloverPreviewTab.ListIndex = -1;
                _rolloverPreviewTab.AllowDrop = true;
                _rolloverPreviewTab.DragEnter += TabsDragEnter;
                _rolloverPreviewTab.DragOver += TabsDragOver;
                _rolloverPreviewTab.DragLeave += TabsDragLeave;
                _rolloverPreviewTab.DragDrop += TabsDragDrop;
                _rolloverPreviewTab.Size = new Size(TabWidth, TabHeight);
                _toolTip.SetToolTip(_rolloverPreviewTab,
                    note.DisplayTitle + "\n单击展开便利贴");
                Controls.Add(_rolloverPreviewTab);
            }
            int normalLeft = _side == StickyTabSide.Right &&
                _sourceHorizontallyOffset ? DragSourceVisualOffset : 0;
            _rolloverPreviewTab.Left = normalLeft;
            _rolloverPreviewTab.IsDragSource = false;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null || Object.ReferenceEquals(tab,
                    _rolloverPreviewTab)) continue;
                tab.Top = (tab.ListIndex + (atTop ? 1 : 0)) *
                    (TabHeight + TabGap);
            }
            _rolloverPreviewTab.Top = atTop ? 0 :
                Math.Max(0, Controls.Count - 1) * (TabHeight + TabGap);
            StickyNoteTabControl activeSource = null;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && IsSameNote(tab.Snapshot.NoteId,
                    DragSession.ActiveNoteId) && !Object.ReferenceEquals(tab,
                    _rolloverPreviewTab)) activeSource = tab;
            }
            if (activeSource != null) activeSource.BringToFront();
            ClientSize = new Size(CurrentCanvasWidth, _normalHeight +
                TabHeight + TabGap + PreviewInsertionGap);
            Invalidate();
            if (IsHandleCreated) Update();
        }

        private void ClearHiddenBoundaryTab()
        {
            if (_hiddenBoundaryTab != null &&
                !_hiddenBoundaryTab.IsDisposed)
                _hiddenBoundaryTab.Visible = true;
            _hiddenBoundaryTab = null;
            _crossSideVisualDropIndex = -1;
        }

        private void ClearRolloverPreviewTab()
        {
            if (_rolloverPreviewTab == null) return;
            Controls.Remove(_rolloverPreviewTab);
            _rolloverPreviewTab.Dispose();
            _rolloverPreviewTab = null;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null)
                    tab.Top = tab.ListIndex * (TabHeight + TabGap);
            }
            bool dragActive = !String.IsNullOrEmpty(DragSession.ActiveNoteId);
            ClientSize = new Size(CurrentCanvasWidth, _normalHeight +
                (dragActive ? PreviewInsertionGap : 0));
        }

        private void ClearCrossSideBoundaryPreview()
        {
            ClearHiddenBoundaryTab();
            ClearRolloverPreviewTab();
        }

        internal void CancelDragPreview()
        {
            if (_dropIndex >= 0 || _previewDraggedNoteId != null)
                ResetDropPreview(true);
        }

        internal void ShowDropPreviewForTest(string noteId, int dropIndex)
        {
            ActivateExclusiveDropTarget(this, noteId);
            _previewDraggedNoteId = noteId;
            _dropIndex = Math.Max(0, Math.Min(Controls.Count, dropIndex));
            _dragPointerY = _dropIndex * (TabHeight + TabGap);
            ClientSize = new Size(CurrentCanvasWidth,
                _normalHeight + PreviewInsertionGap);
            if (!String.IsNullOrEmpty(DragSession.ActiveNoteId) &&
                !DragSession.IsSource(this))
            {
                ApplyCrossSidePreviewImmediately();
                Invalidate();
                return;
            }
            int sourceIndex = -1;
            bool canOwnSource = String.IsNullOrEmpty(DragSession.ActiveNoteId) ||
                DragSession.IsSource(this);
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (canOwnSource && tab != null &&
                    IsSameNote(tab.Snapshot.NoteId, noteId))
                    sourceIndex = tab.ListIndex;
            }
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                tab.IsDragSource = tab.ListIndex == sourceIndex;
                tab.Top = tab.ListIndex == sourceIndex
                    ? tab.ListIndex * (TabHeight + TabGap)
                    : PreviewTargetTop(tab.ListIndex, sourceIndex, _dropIndex);
            }
            Invalidate();
        }

        internal bool HasDropPreviewForTest
        {
            get { return _dropIndex >= 0; }
        }

        internal bool HasDragSourceVisualForTest(string noteId)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null || !IsSameNote(tab.Snapshot.NoteId, noteId))
                    continue;
                int expectedLeft = _side == StickyTabSide.Left
                    ? DragSourceVisualOffset : 0;
                return tab.IsDragSource &&
                    _sourceHorizontallyOffset &&
                    tab.Left == expectedLeft &&
                    tab.Top == tab.ListIndex * (TabHeight + TabGap) &&
                    Controls.GetChildIndex(tab) == 0;
            }
            return false;
        }

        internal int TabTopForTest(string noteId)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && IsSameNote(tab.Snapshot.NoteId, noteId))
                    return tab.Top;
            }
            return Int32.MinValue;
        }

        internal bool HasBoundaryRolloverForTest(string noteId,
            bool atTop)
        {
            if (_rolloverPreviewTab == null ||
                !IsSameNote(_rolloverPreviewTab.Snapshot.NoteId, noteId))
                return false;
            int expectedTop = atTop ? 0 :
                Math.Max(0, Controls.Count - 1) * (TabHeight + TabGap);
            return _rolloverPreviewTab.Top == expectedTop;
        }

        internal bool TabVisibleForTest(string noteId)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && !Object.ReferenceEquals(tab,
                    _rolloverPreviewTab) &&
                    IsSameNote(tab.Snapshot.NoteId, noteId))
                    return !Object.ReferenceEquals(tab,
                        _hiddenBoundaryTab);
            }
            return false;
        }

        internal bool HasStableDragCanvasForTest
        {
            get
            {
                int rolloverHeight = _rolloverPreviewTab == null ? 0 :
                    TabHeight + TabGap;
                return String.IsNullOrEmpty(DragSession.ActiveNoteId)
                    ? ClientSize.Height == _normalHeight &&
                        ClientSize.Width == TabWidth
                    : ClientSize.Height == _normalHeight +
                        PreviewInsertionGap + rolloverHeight &&
                        ClientSize.Width == CurrentCanvasWidth;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_dropIndex < 0) return;
            int sourceIndex = -1;
            bool canOwnSource = String.IsNullOrEmpty(DragSession.ActiveNoteId) ||
                DragSession.IsSource(this);
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (canOwnSource && tab != null &&
                    IsSameNote(tab.Snapshot.NoteId, _previewDraggedNoteId))
                    sourceIndex = tab.ListIndex;
            }
            int insertion = _crossSideVisualDropIndex >= 0
                ? _crossSideVisualDropIndex : _dropIndex;
            if (sourceIndex >= 0 && sourceIndex < insertion) insertion--;
            int y = Math.Max(1, insertion * (TabHeight + TabGap) + 2);
            Rectangle slot = new Rectangle(5, y, Width - 10,
                PreviewInsertionGap - 4);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(125, 70, 150, 245)))
            using (Pen pen = new Pen(Color.FromArgb(225, 40, 105, 220), 2F))
            {
                e.Graphics.FillRectangle(glow, slot);
                e.Graphics.DrawLine(pen, slot.Left, slot.Top,
                    slot.Right, slot.Top);
                e.Graphics.DrawLine(pen, slot.Left, slot.Bottom,
                    slot.Right, slot.Bottom);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_ownedResourcesDisposed)
            {
                _ownedResourcesDisposed = true;
                LiveForms.Remove(this);
                ClearCrossSideBoundaryPreview();
                _layoutAnimationTimer.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class StickyNoteTabControl : Control
    {
        private const string TypeIconResourceName =
            "PennyPet.TabIcons.Reference";
        private static readonly object TypeIconMaskSync = new object();
        private static Bitmap[] _typeIconMasks;

        private readonly SideTabSnapshot _snapshot;
        private readonly StickyTabSide _side;
        private readonly Action<string> _openNote;
        private readonly Action<string> _deleteNote;
        private readonly System.Windows.Forms.Timer _longPressTimer;
        private readonly ContextMenuStrip _menu;
        private bool _hover;
        private bool _dragStarted;
        private bool _isDragSource;

        internal int ListIndex { get; set; }

        internal SideTabSnapshot Snapshot
        {
            get { return _snapshot; }
        }

        internal bool IsDragSource
        {
            get { return _isDragSource; }
            set
            {
                if (_isDragSource == value) return;
                _isDragSource = value;
                Invalidate();
            }
        }

        public StickyNoteTabControl(SideTabSnapshot snapshot,
            StickyTabSide side,
            Action<string> openNote,
            Action<string> deleteNote)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _snapshot = snapshot;
            _side = side;
            _openNote = openNote;
            _deleteNote = deleteNote;
            Cursor = Cursors.Hand;
            Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 8.5F,
                FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            _longPressTimer = new System.Windows.Forms.Timer();
            _longPressTimer.Interval = 360;
            _longPressTimer.Tick += LongPressTimerTick;
            _menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("展开此便签");
            open.Click += delegate { OpenNoteDeferred(); };
            ToolStripMenuItem delete = new ToolStripMenuItem("删除此便签…");
            delete.Click += delegate
            {
                if (_deleteNote != null && !IsDisposed)
                    BeginInvoke((MethodInvoker)delegate
                    { _deleteNote(_snapshot.NoteId); });
            };
            _menu.Items.Add(open);
            _menu.Items.Add(delete);
            ContextMenuStrip = _menu;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStarted = false;
                _longPressTimer.Stop();
                _longPressTimer.Start();
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _longPressTimer.Stop();
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_dragStarted)
            {
                _dragStarted = false;
                return;
            }
            OpenNoteDeferred();
            base.OnClick(e);
        }

        private void OpenNoteDeferred()
        {
            if (_openNote == null || IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            { _openNote(_snapshot.NoteId); });
        }

        private void LongPressTimerTick(object sender, EventArgs e)
        {
            _longPressTimer.Stop();
            if ((Control.MouseButtons & MouseButtons.Left) == 0) return;
            _dragStarted = true;
            Capture = false;
            Cursor = Cursors.SizeAll;
            StickyNoteTabsForm owner = Parent as StickyNoteTabsForm;
            StickyNoteTabsForm.BeginDragSession(_snapshot.NoteId, owner);
            DataObject payload = new DataObject();
            payload.SetData(StickyNoteTabsForm.DragDataFormat, false,
                _snapshot.NoteId);
            try { DoDragDrop(payload, DragDropEffects.Move); }
            finally
            {
                if (owner != null) owner.CancelDragPreview();
                IsDragSource = false;
                Cursor = Cursors.Hand;
                // This is intentionally last: the commit rebuilds both tab
                // strips and can dispose this source control.
                StickyNoteTabsForm.EndDragSession(_snapshot.NoteId);
            }
        }

        internal bool HasDeleteCommand
        {
            get
            {
                foreach (ToolStripItem item in _menu.Items)
                {
                    if (item.Text.StartsWith("删除此便签",
                        StringComparison.Ordinal)) return true;
                }
                return false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region old = Region;
            using (GraphicsPath path = CreateShape(ClientRectangle, _side))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color paper = Color.FromArgb(_snapshot.ColorArgb);
            if (_hover) paper = ControlPaint.Light(paper, 0.12F);
            if (_isDragSource) paper = ControlPaint.Light(paper, 0.35F);
            Color border = ControlPaint.Dark(paper, 0.20F);
            using (GraphicsPath path = CreateShape(ClientRectangle, _side))
            using (SolidBrush fill = new SolidBrush(paper))
            using (Pen outline = new Pen(border, 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(outline, path);
                if (_isDragSource)
                {
                    using (Pen dragOutline = new Pen(Color.FromArgb(60, 110, 220), 2F))
                    {
                        dragOutline.DashStyle = DashStyle.Dash;
                        e.Graphics.DrawPath(dragOutline, path);
                    }
                }
            }
            Rectangle iconArea = TypeIconBounds();
            Rectangle textArea = _side == StickyTabSide.Left
                ? new Rectangle(iconArea.Right + 7, 2, Math.Max(10,
                    Width - iconArea.Right - 25), Height - 4)
                : new Rectangle(18, 2, Math.Max(10,
                    iconArea.Left - 24), Height - 4);
            Color textColor = paper.GetBrightness() > 0.52F
                ? Color.FromArgb(58, 52, 48) : Color.White;
            TextRenderer.DrawText(e.Graphics, _snapshot.DisplayTitle, Font,
                textArea,
                textColor, TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);
            DrawTypeIcon(e.Graphics, iconArea,
                TypeIconColor(paper), paper);
        }

        internal static Color TypeIconColor(Color paper)
        {
            return ControlPaint.Dark(paper, 0.10F);
        }

        private Rectangle TypeIconBounds()
        {
            // Keep the 24 px reference geometry: shrinking the supplied
            // pencil mask changed its proportions and made it look unlike
            // the approved artwork.
            const int size = 24;
            int x = _side == StickyTabSide.Left
                ? 10 : Width - size - 10;
            return new Rectangle(x, (Height - size) / 2, size, size);
        }

        private void DrawTypeIcon(Graphics graphics, Rectangle bounds,
            Color color, Color paper)
        {
            DrawTypeIcon(graphics, bounds, color, paper,
                _snapshot.IsTodoList, _snapshot.IsSchedule);
        }

        private static void DrawTypeIcon(Graphics graphics, Rectangle bounds,
            Color color, Color paper, bool isTodoList, bool isSchedule)
        {
            // The supplied pencil silhouette is used verbatim.  The receipt
            // and calendar keep the established vector metrics so all tabs
            // retain their previous alignment and stroke weight.
            if (!isTodoList && !isSchedule)
            {
                Bitmap referenceMask = GetTypeIconMask(2);
                if (referenceMask != null)
                {
                    DrawTintedTypeIcon(graphics, referenceMask, bounds, color);
                    return;
                }
            }
            using (Pen pen = new Pen(color, 2.4F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (isTodoList) DrawTodoIcon(graphics, pen, bounds);
                else if (isSchedule) DrawScheduleIcon(graphics, pen, bounds);
                else DrawOrdinaryIcon(graphics, pen, bounds, paper);
            }
        }

        internal static Bitmap CreateTypeIconBitmap(StickyNoteData note,
            Color paper, int size)
        {
            int safeSize = Math.Max(12, Math.Min(48, size));
            Bitmap bitmap = new Bitmap(safeSize, safeSize,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawTypeIcon(graphics,
                    new Rectangle(0, 0, safeSize, safeSize),
                    TypeIconColor(paper), paper, note.IsTodoList,
                    note.IsSchedule);
            }
            return bitmap;
        }

        private static Bitmap GetTypeIconMask(int index)
        {
            lock (TypeIconMaskSync)
            {
                if (_typeIconMasks == null)
                    _typeIconMasks = LoadTypeIconMasks();
                return _typeIconMasks != null && index >= 0 &&
                    index < _typeIconMasks.Length
                    ? _typeIconMasks[index] : null;
            }
        }

        private static Bitmap[] LoadTypeIconMasks()
        {
            try
            {
                Assembly assembly = typeof(StickyNoteTabControl).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(
                    TypeIconResourceName))
                {
                    if (stream == null) return null;
                    using (Bitmap source = new Bitmap(stream))
                    {
                        if (source.Width < 3 || source.Height < 3) return null;
                        int rowHeight = source.Height / 3;
                        Bitmap[] masks = new Bitmap[3];
                        for (int row = 0; row < 3; row++)
                            masks[row] = ExtractTypeIconMask(source,
                                new Rectangle(0, row * rowHeight,
                                    source.Width, row == 2
                                        ? source.Height - row * rowHeight
                                        : rowHeight));
                        return masks;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap ExtractTypeIconMask(Bitmap source,
            Rectangle crop)
        {
            Bitmap mask = new Bitmap(crop.Width, crop.Height,
                PixelFormat.Format32bppArgb);
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = source.GetPixel(crop.Left + x, crop.Top + y);
                    int luminance = (pixel.R * 299 + pixel.G * 587 +
                        pixel.B * 114) / 1000;
                    int alpha;
                    if (luminance <= 62) alpha = 255;
                    else if (luminance >= 145) alpha = 0;
                    else alpha = (145 - luminance) * 255 / 83;
                    alpha = alpha * pixel.A / 255;
                    mask.SetPixel(x, y, Color.FromArgb(alpha, 0, 0, 0));
                }
            }
            Bitmap normalized = NormalizeTypeIconMask(mask);
            mask.Dispose();
            return normalized;
        }

        private static Bitmap NormalizeTypeIconMask(Bitmap source)
        {
            int minX = source.Width;
            int minY = source.Height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (source.GetPixel(x, y).A < 12) continue;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (maxX < minX || maxY < minY)
                return new Bitmap(1, 1, PixelFormat.Format32bppArgb);

            const int padding = 2;
            int contentWidth = maxX - minX + 1;
            int contentHeight = maxY - minY + 1;
            Bitmap result = new Bitmap(contentWidth + padding * 2,
                contentHeight + padding * 2, PixelFormat.Format32bppArgb);
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    int sourceCenterX = minX + x - padding;
                    int sourceCenterY = minY + y - padding;
                    int alpha = 0;
                    // One-source-pixel dilation restores the same apparent
                    // weight as the former 2.4 px vector at 24 px display.
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int sourceX = sourceCenterX + offsetX;
                            int sourceY = sourceCenterY + offsetY;
                            if (sourceX < 0 || sourceY < 0 ||
                                sourceX >= source.Width ||
                                sourceY >= source.Height) continue;
                            alpha = Math.Max(alpha,
                                source.GetPixel(sourceX, sourceY).A);
                        }
                    }
                    result.SetPixel(x, y, Color.FromArgb(alpha, 0, 0, 0));
                }
            }
            return result;
        }

        private static void DrawTintedTypeIcon(Graphics graphics, Bitmap mask,
            Rectangle bounds, Color color)
        {
            float red = color.R / 255F;
            float green = color.G / 255F;
            float blue = color.B / 255F;
            ColorMatrix tint = new ColorMatrix(new float[][] {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { red, green, blue, 0, 1 }
            });
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(tint, ColorMatrixFlag.Default,
                    ColorAdjustType.Bitmap);
                InterpolationMode previousInterpolation =
                    graphics.InterpolationMode;
                PixelOffsetMode previousPixelOffset = graphics.PixelOffsetMode;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min(bounds.Width / (float)mask.Width,
                    bounds.Height / (float)mask.Height);
                int width = Math.Max(1, (int)Math.Round(mask.Width * scale));
                int height = Math.Max(1, (int)Math.Round(mask.Height * scale));
                Rectangle destination = new Rectangle(
                    bounds.Left + (bounds.Width - width) / 2,
                    bounds.Top + (bounds.Height - height) / 2,
                    width, height);
                graphics.DrawImage(mask, destination, 0, 0,
                    mask.Width, mask.Height,
                    GraphicsUnit.Pixel, attributes);
                graphics.InterpolationMode = previousInterpolation;
                graphics.PixelOffsetMode = previousPixelOffset;
            }
        }

        private static void DrawTodoIcon(Graphics graphics, Pen pen,
            Rectangle area)
        {
            // Faithfully follows art/svg.png: a tall receipt with a soft
            // three-fold bottom, one check mark, and one short text line.
            using (GraphicsPath receipt = new GraphicsPath())
            {
                float left = area.Left + 2.7F;
                float right = area.Right - 2.7F;
                float top = area.Top + 1.2F;
                float bottom = area.Bottom - 1.2F;
                receipt.StartFigure();
                receipt.AddBezier(
                    new PointF(left + 3.1F, top),
                    new PointF(left + 1.2F, top),
                    new PointF(left, top + 1.2F),
                    new PointF(left, top + 3.1F));
                receipt.AddLine(new PointF(left, top + 3.1F),
                    new PointF(left, bottom - 3.1F));
                receipt.AddBezier(
                    new PointF(left, bottom - 3.1F),
                    new PointF(left, bottom - 1.2F),
                    new PointF(left + 1.6F, bottom),
                    new PointF(left + 3.2F, bottom - 1.0F));
                receipt.AddBezier(
                    new PointF(left + 3.2F, bottom - 1.0F),
                    new PointF(left + 4.3F, bottom - 1.8F),
                    new PointF(left + 5.3F, bottom - 1.8F),
                    new PointF(left + 6.4F, bottom - 1.0F));
                receipt.AddBezier(
                    new PointF(left + 6.4F, bottom - 1.0F),
                    new PointF(left + 7.7F, bottom),
                    new PointF(left + 8.8F, bottom),
                    new PointF(left + 10.0F, bottom - 1.0F));
                receipt.AddBezier(
                    new PointF(left + 10.0F, bottom - 1.0F),
                    new PointF(left + 11.1F, bottom - 1.8F),
                    new PointF(left + 12.1F, bottom - 1.8F),
                    new PointF(left + 13.2F, bottom - 1.0F));
                receipt.AddBezier(
                    new PointF(left + 13.2F, bottom - 1.0F),
                    new PointF(left + 14.8F, bottom),
                    new PointF(right, bottom - 1.2F),
                    new PointF(right, bottom - 3.1F));
                receipt.AddLine(new PointF(right, bottom - 3.1F),
                    new PointF(right, top + 3.1F));
                receipt.AddBezier(
                    new PointF(right, top + 3.1F),
                    new PointF(right, top + 1.2F),
                    new PointF(right - 1.2F, top),
                    new PointF(right - 3.1F, top));
                receipt.AddLine(new PointF(right - 3.1F, top),
                    new PointF(left + 3.1F, top));
                receipt.CloseFigure();
                graphics.DrawPath(pen, receipt);
            }
            graphics.DrawLines(pen, new PointF[] {
                new PointF(area.Left + 7.1F, area.Top + 8.4F),
                new PointF(area.Left + 10.4F, area.Top + 11.6F),
                new PointF(area.Left + 16.7F, area.Top + 5.7F) });
            graphics.DrawLine(pen, area.Left + 9.1F, area.Top + 16.1F,
                area.Left + 16.0F, area.Top + 16.1F);
        }

        private static void DrawScheduleIcon(Graphics graphics, Pen pen,
            Rectangle area)
        {
            // Same proportions as the reference calendar: two protruding
            // binding posts, a low header rule, and a broad rounded body.
            RectangleF body = new RectangleF(area.Left + 1.6F,
                area.Top + 4.7F, area.Width - 3.2F, area.Height - 6.3F);
            using (GraphicsPath rounded = RoundedRectangle(body, 5.2F))
                graphics.DrawPath(pen, rounded);
            graphics.DrawLine(pen, body.Left + 0.5F, body.Top + 5.6F,
                body.Right - 0.5F, body.Top + 5.6F);
            graphics.DrawLine(pen, area.Left + 7.0F, area.Top + 1.2F,
                area.Left + 7.0F, area.Top + 7.1F);
            graphics.DrawLine(pen, area.Right - 7.0F, area.Top + 1.2F,
                area.Right - 7.0F, area.Top + 7.1F);
        }

        private static void DrawOrdinaryIcon(Graphics graphics, Pen sourcePen,
            Rectangle area, Color paper)
        {
            // The reference uses a noticeably finer pencil than the other
            // two symbols.  Its solid paper-coloured interior masks the note
            // edge underneath, keeping it an actual pencil rather than a
            // prohibition-sign-looking slash.
            using (Pen pen = new Pen(sourcePen.Color, 1.75F))
            using (SolidBrush paperBrush = new SolidBrush(paper))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                RectangleF page = new RectangleF(area.Left + 1.7F,
                    area.Top + 3.8F, area.Width - 6.5F, area.Height - 5.4F);
                using (GraphicsPath rounded = RoundedRectangle(page, 3.3F))
                    graphics.DrawPath(pen, rounded);

                using (GraphicsPath pencil = new GraphicsPath())
                {
                    PointF tip = new PointF(area.Left + 5.7F,
                        area.Bottom - 2.0F);
                    PointF upperBase = new PointF(area.Left + 7.5F,
                        area.Bottom - 6.5F);
                    PointF upperEnd = new PointF(area.Right - 5.8F,
                        area.Top + 5.0F);
                    PointF lowerEnd = new PointF(area.Right - 1.4F,
                        area.Top + 8.7F);
                    PointF lowerBase = new PointF(area.Left + 10.9F,
                        area.Bottom - 3.0F);
                    pencil.StartFigure();
                    pencil.AddLine(tip, upperBase);
                    pencil.AddLine(upperBase, upperEnd);
                    pencil.AddBezier(upperEnd,
                        new PointF(area.Right - 4.4F, area.Top + 3.6F),
                        new PointF(area.Right - 1.8F, area.Top + 4.3F),
                        lowerEnd);
                    pencil.AddLine(lowerEnd, lowerBase);
                    pencil.AddLine(lowerBase, tip);
                    pencil.CloseFigure();
                    graphics.FillPath(paperBrush, pencil);
                    graphics.DrawPath(pen, pencil);
                }
                graphics.DrawLine(pen, area.Left + 7.5F,
                    area.Bottom - 6.5F, area.Left + 10.9F,
                    area.Bottom - 3.0F);
                graphics.DrawLine(pen, area.Right - 5.8F,
                    area.Top + 5.0F, area.Right - 1.4F,
                    area.Top + 8.7F);
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds,
            float radius)
        {
            float diameter = Math.Max(1F, radius * 2F);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter,
                270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter,
                diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath CreateShape(Rectangle bounds,
            StickyTabSide side)
        {
            int right = Math.Max(1, bounds.Width - 1);
            int bottom = Math.Max(1, bounds.Height - 1);
            int arrow = Math.Min(10, Math.Max(4, bounds.Width / 6));
            Point[] points = side == StickyTabSide.Left
                ? new Point[] {
                    new Point(0, 0), new Point(right - arrow, 0),
                    new Point(right, bottom / 2),
                    new Point(right - arrow, bottom), new Point(0, bottom)
                }
                : new Point[] {
                    new Point(arrow, 0), new Point(right, 0),
                    new Point(right, bottom), new Point(arrow, bottom),
                    new Point(0, bottom / 2)
                };
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(points);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _longPressTimer.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
