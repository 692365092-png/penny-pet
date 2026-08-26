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

    // OLE DoDragDrop runs a nested Windows message loop. Posting BeginInvoke
    // from DragDrop can therefore execute before DoDragDrop has actually
    // returned and dispose the source tab while OLE still owns it. Keep the
    // accepted drop here and commit only after the source leaves DoDragDrop.
    internal sealed class StickyTabDropSession
    {
        private StickyNoteData _activeNote;
        private Action _pendingCommit;

        internal void Begin(StickyNoteData note)
        {
            _activeNote = note;
            _pendingCommit = null;
        }

        internal StickyNoteData ActiveNote(string id)
        {
            if (_activeNote == null || String.IsNullOrEmpty(id) ||
                !String.Equals(_activeNote.Id, id,
                    StringComparison.OrdinalIgnoreCase)) return null;
            return _activeNote;
        }

        internal StickyNoteData CurrentNote
        {
            get { return _activeNote; }
        }

        internal bool QueueCommit(StickyNoteData note, Action commit)
        {
            if (!ReferenceEquals(_activeNote, note) || commit == null)
                return false;
            _pendingCommit = commit;
            return true;
        }

        internal bool Complete(StickyNoteData note)
        {
            if (!ReferenceEquals(_activeNote, note)) return false;
            Action commit = _pendingCommit;
            _pendingCommit = null;
            _activeNote = null;
            if (commit != null) commit();
            return true;
        }

        internal static bool DefersCommitUntilCompletionForTest()
        {
            StickyTabDropSession session = new StickyTabDropSession();
            StickyNoteData note = new StickyNoteData();
            int commits = 0;
            session.Begin(note);
            bool queued = session.QueueCommit(note,
                delegate { commits++; });
            bool deferred = commits == 0;
            bool completed = session.Complete(note);
            return queued && deferred && completed && commits == 1 &&
                session.ActiveNote(note.Id) == null &&
                !session.Complete(note);
        }
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
        internal const int DragSourceVisualOffset = 5;
        internal const string DragDataFormat = "PennyPet.StickyNoteTabId";

        private static readonly StickyTabDropSession DragSession =
            new StickyTabDropSession();
        private static readonly List<StickyNoteTabsForm> LiveForms =
            new List<StickyNoteTabsForm>();

        private readonly StickyTabSide _side;
        private readonly Action<StickyNoteData> _openNote;
        private readonly Action<StickyNoteData> _deleteNote;
        private readonly Action<StickyNoteData, int> _reorderNote;
        private readonly ToolTip _toolTip;
        private readonly System.Windows.Forms.Timer _layoutAnimationTimer;
        private int _globalStartIndex;
        private int _dropIndex = -1;
        private int _normalHeight = 1;
        private int _dragPointerY;
        private StickyNoteData _previewDraggedNote;
        private bool _restoringLayout;
        private bool _ownedResourcesDisposed;

        public StickyNoteTabsForm(StickyTabSide side,
            Action<StickyNoteData> openNote)
            : this(side, openNote, null, null)
        {
        }

        public StickyNoteTabsForm(StickyTabSide side,
            Action<StickyNoteData> openNote,
            Action<StickyNoteData> deleteNote,
            Action<StickyNoteData, int> reorderNote)
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

        public void SetNotes(IList<StickyNoteData> notes)
        {
            SetNotes(notes, 0);
        }

        public void SetNotes(IList<StickyNoteData> notes, int globalStartIndex)
        {
            _globalStartIndex = Math.Max(0, globalStartIndex);
            _dropIndex = -1;
            _previewDraggedNote = null;
            _restoringLayout = false;
            _layoutAnimationTimer.Stop();
            SuspendLayout();
            foreach (Control control in new List<Control>(ControlsAsList()))
            {
                Controls.Remove(control);
                control.Dispose();
            }
            int count = notes == null ? 0 : notes.Count;
            _normalHeight = Math.Max(1,
                count * (TabHeight + TabGap) - TabGap);
            ClientSize = new Size(TabWidth, _normalHeight);
            for (int i = 0; i < count; i++)
            {
                StickyNoteData note = notes[i];
                StickyNoteTabControl tab = new StickyNoteTabControl(note, _side,
                    _openNote, _deleteNote);
                tab.ListIndex = i;
                tab.Bounds = new Rectangle(0, i * (TabHeight + TabGap),
                    TabWidth, TabHeight);
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
            BringToFront();
        }

        public void ShowNear(Rectangle petBounds, Rectangle workArea)
        {
            if (Controls.Count == 0) return;
            int visualOverlap = PetOverlapForWidth(petBounds.Width);
            int x = _side == StickyTabSide.Left
                ? petBounds.Left - Width + visualOverlap
                : petBounds.Right - visualOverlap;
            x = Math.Max(workArea.Left + 2,
                Math.Min(x, workArea.Right - Width - 2));
            int y = petBounds.Top + (petBounds.Height - Height) / 2;
            y = Math.Max(workArea.Top + 4,
                Math.Min(y, workArea.Bottom - Height - 4));
            Location = new Point(x, y);
            if (!Visible) Show();
            BringToFront();
        }

        internal static int PetOverlapForWidth(int petWidth)
        {
            // Idle art begins about 44 px inside its 192 px sprite cell. The
            // overlap scales with the pet so the visible tab-to-character gap
            // stays at half of the old fixed-20 px result at every zoom level.
            int transparentMargin = (int)Math.Round(
                Math.Max(0, petWidth) * 44.0 / 192.0);
            return (20 + transparentMargin) / 2;
        }

        internal static int ScreenCapacity(Rectangle workArea)
        {
            return Math.Max(1, (workArea.Height - 16) / (TabHeight + TabGap));
        }

        internal static int PreferredLeftCapacity(int petHeight,
            Rectangle workArea)
        {
            int fitScreen = ScreenCapacity(workArea);
            int alongsidePet = Math.Max(4,
                (Math.Max(TabHeight, petHeight) + TabGap) / (TabHeight + TabGap));
            return Math.Min(fitScreen, alongsidePet);
        }

        internal static int CalculateLeftCount(int totalCount, int petHeight,
            Rectangle workArea)
        {
            if (totalCount <= 0) return 0;
            int screenCapacity = ScreenCapacity(workArea);
            int preferred = PreferredLeftCapacity(petHeight, workArea);
            int left = Math.Min(totalCount, preferred);
            if (totalCount - left > screenCapacity)
                left = Math.Min(screenCapacity, totalCount - screenCapacity);
            return Math.Max(0, left);
        }

        private IEnumerable<Control> ControlsAsList()
        {
            foreach (Control control in Controls) yield return control;
        }

        private void TabsDragEnter(object sender, DragEventArgs e)
        {
            StickyNoteData moved = TryGetDraggedNote(e.Data);
            e.Effect = moved != null
                ? DragDropEffects.Move : DragDropEffects.None;
            if (moved != null) ActivateExclusiveDropTarget(this, moved);
        }

        private void TabsDragOver(object sender, DragEventArgs e)
        {
            StickyNoteData moved = TryGetDraggedNote(e.Data);
            if (moved == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            // OLE does not reliably deliver DragLeave to the old child/form
            // when the pointer crosses the transparent pet window. Clear the
            // other strip proactively so its source never overlaps an old
            // insertion animation or leaves a second purple guide behind.
            ActivateExclusiveDropTarget(this, moved);
            Point point = PointToClient(new Point(e.X, e.Y));
            _dragPointerY = point.Y;
            int next = CalculateDropIndex(point.Y, Controls.Count);
            if (next == _dropIndex && ReferenceEquals(moved,
                _previewDraggedNote)) return;
            _dropIndex = next;
            _previewDraggedNote = moved;
            _restoringLayout = false;
            if (ClientSize.Height != _normalHeight + PreviewInsertionGap)
                ClientSize = new Size(TabWidth, _normalHeight + PreviewInsertionGap);
            _layoutAnimationTimer.Start();
            Invalidate();
        }

        private void TabsDragLeave(object sender, EventArgs e)
        {
            // Moving between child tab controls also raises DragLeave.  Keep
            // the preview until the pointer really leaves the complete strip.
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return;
            ShowSourceOnly(DragSession.CurrentNote);
        }

        private void TabsDragDrop(object sender, DragEventArgs e)
        {
            StickyNoteData moved = TryGetDraggedNote(e.Data);
            int destination = _globalStartIndex +
                Math.Max(0, _dropIndex < 0 ? Controls.Count : _dropIndex);
            ResetDropPreview(false);
            if (moved == null || _reorderNote == null) return;

            // Do not post this with BeginInvoke: the OLE nested message loop
            // may dispatch it before DoDragDrop returns. The source control
            // completes the session after OLE has fully unwound.
            Action<StickyNoteData, int> reorder = _reorderNote;
            StickyNoteTabsForm target = this;
            DragSession.QueueCommit(moved, delegate
            {
                if (target.IsDisposed) return;
                reorder(moved, destination);
            });
        }

        private static StickyNoteData TryGetDraggedNote(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DragDataFormat, false))
                return null;
            string id = data.GetData(DragDataFormat, false) as string;
            return DragSession.ActiveNote(id);
        }

        internal static void BeginDragSession(StickyNoteData note)
        {
            DragSession.Begin(note);
            ShowSourceOnly(note);
        }

        private static void ActivateExclusiveDropTarget(
            StickyNoteTabsForm target, StickyNoteData note)
        {
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form == null || form.IsDisposed ||
                    Object.ReferenceEquals(form, target)) continue;
                form.HoldSourceVisual(note);
            }
        }

        private static void ShowSourceOnly(StickyNoteData note)
        {
            foreach (StickyNoteTabsForm form in
                new List<StickyNoteTabsForm>(LiveForms))
            {
                if (form != null && !form.IsDisposed)
                    form.HoldSourceVisual(note);
            }
        }

        internal static void EndDragSession(StickyNoteData note)
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
            DragSession.Complete(note);
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
            int compactIndex = listIndex > sourceIndex ? listIndex - 1 : listIndex;
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
            StickyNoteData sourceNote = DragSession.CurrentNote ??
                _previewDraggedNote;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && ReferenceEquals(tab.Note, sourceNote))
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
                    target = tab.ListIndex * (TabHeight + TabGap) +
                        (isSource ? DragSourceVisualOffset : 0);
                else if (isSource)
                {
                    // Keep the source visible in its original strip. A small
                    // vertical offset marks it as the item being dragged;
                    // moving it with the pointer can push it outside the thin
                    // side window when crossing to the opposite strip.
                    target = tab.ListIndex * (TabHeight + TabGap) +
                        DragSourceVisualOffset;
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
                bool keepSourceOffset = DragSession.CurrentNote != null &&
                    sourceIndex >= 0;
                _previewDraggedNote = keepSourceOffset
                    ? DragSession.CurrentNote : null;
                // Keep a stable transparent canvas for the whole OLE drag.
                // Resizing a TransparencyKey form while child tabs animate
                // can make Windows temporarily omit a moving child window.
                bool dragStillActive = DragSession.CurrentNote != null;
                ClientSize = new Size(TabWidth, _normalHeight +
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
                _previewDraggedNote = null;
                _restoringLayout = false;
                _layoutAnimationTimer.Stop();
                foreach (Control control in Controls)
                {
                    StickyNoteTabControl tab = control as StickyNoteTabControl;
                    if (tab == null) continue;
                    tab.Top = tab.ListIndex * (TabHeight + TabGap);
                    tab.IsDragSource = false;
                }
                ClientSize = new Size(TabWidth, _normalHeight);
                return;
            }
            _restoringLayout = true;
            _layoutAnimationTimer.Start();
        }

        private void HoldSourceVisual(StickyNoteData note)
        {
            _dropIndex = -1;
            _restoringLayout = false;
            _layoutAnimationTimer.Stop();
            StickyNoteTabControl source = null;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && ReferenceEquals(tab.Note, note))
                    source = tab;
            }
            _previewDraggedNote = source == null ? null : note;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                bool isSource = Object.ReferenceEquals(tab, source);
                tab.Top = tab.ListIndex * (TabHeight + TabGap) +
                    (isSource ? DragSourceVisualOffset : 0);
                tab.IsDragSource = isSource;
            }
            if (source != null) source.BringToFront();
            // Both strips reserve the same transparent insertion area until
            // DoDragDrop ends. Switching target sides therefore never shrinks
            // a top-level transparent form in the middle of the animation.
            bool dragActive = DragSession.CurrentNote != null;
            ClientSize = new Size(TabWidth, _normalHeight +
                (dragActive ? PreviewInsertionGap : 0));
            Invalidate();
            if (IsHandleCreated) Update();
        }

        internal void CancelDragPreview()
        {
            if (_dropIndex >= 0 || _previewDraggedNote != null)
                ResetDropPreview(true);
        }

        internal void ShowDropPreviewForTest(StickyNoteData note, int dropIndex)
        {
            ActivateExclusiveDropTarget(this, note);
            _previewDraggedNote = note;
            _dropIndex = Math.Max(0, Math.Min(Controls.Count, dropIndex));
            _dragPointerY = _dropIndex * (TabHeight + TabGap);
            ClientSize = new Size(TabWidth, _normalHeight + PreviewInsertionGap);
            int sourceIndex = -1;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && ReferenceEquals(tab.Note, note))
                    sourceIndex = tab.ListIndex;
            }
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null) continue;
                tab.IsDragSource = tab.ListIndex == sourceIndex;
                tab.Top = tab.ListIndex == sourceIndex
                    ? tab.ListIndex * (TabHeight + TabGap) +
                        DragSourceVisualOffset
                    : PreviewTargetTop(tab.ListIndex, sourceIndex, _dropIndex);
            }
            Invalidate();
        }

        internal bool HasDropPreviewForTest
        {
            get { return _dropIndex >= 0; }
        }

        internal bool HasDragSourceVisualForTest(StickyNoteData note)
        {
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab == null || !ReferenceEquals(tab.Note, note)) continue;
                return tab.IsDragSource &&
                    tab.Top == tab.ListIndex * (TabHeight + TabGap) +
                        DragSourceVisualOffset &&
                    Controls.GetChildIndex(tab) == 0;
            }
            return false;
        }

        internal bool HasStableDragCanvasForTest
        {
            get
            {
                return DragSession.CurrentNote == null
                    ? ClientSize.Height == _normalHeight
                    : ClientSize.Height == _normalHeight +
                        PreviewInsertionGap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_dropIndex < 0) return;
            int sourceIndex = -1;
            foreach (Control control in Controls)
            {
                StickyNoteTabControl tab = control as StickyNoteTabControl;
                if (tab != null && ReferenceEquals(tab.Note,
                    _previewDraggedNote)) sourceIndex = tab.ListIndex;
            }
            int insertion = _dropIndex;
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

        private readonly StickyNoteData _note;
        private readonly StickyTabSide _side;
        private readonly Action<StickyNoteData> _openNote;
        private readonly Action<StickyNoteData> _deleteNote;
        private readonly System.Windows.Forms.Timer _longPressTimer;
        private readonly ContextMenuStrip _menu;
        private bool _hover;
        private bool _dragStarted;
        private bool _isDragSource;

        internal int ListIndex { get; set; }

        internal StickyNoteData Note
        {
            get { return _note; }
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

        public StickyNoteTabControl(StickyNoteData note, StickyTabSide side,
            Action<StickyNoteData> openNote,
            Action<StickyNoteData> deleteNote)
        {
            _note = note;
            _side = side;
            _openNote = openNote;
            _deleteNote = deleteNote;
            Cursor = Cursors.Hand;
            Font = StickyNoteForm.CreateSafeFont("Microsoft YaHei UI", 8.5F,
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
                    BeginInvoke((MethodInvoker)delegate { _deleteNote(_note); });
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
            BeginInvoke((MethodInvoker)delegate { _openNote(_note); });
        }

        private void LongPressTimerTick(object sender, EventArgs e)
        {
            _longPressTimer.Stop();
            if ((Control.MouseButtons & MouseButtons.Left) == 0) return;
            _dragStarted = true;
            Capture = false;
            Cursor = Cursors.SizeAll;
            StickyNoteTabsForm.BeginDragSession(_note);
            DataObject payload = new DataObject();
            payload.SetData(StickyNoteTabsForm.DragDataFormat, false, _note.Id);
            try { DoDragDrop(payload, DragDropEffects.Move); }
            finally
            {
                StickyNoteTabsForm owner = Parent as StickyNoteTabsForm;
                if (owner != null) owner.CancelDragPreview();
                IsDragSource = false;
                Cursor = Cursors.Hand;
                // This is intentionally last: the commit rebuilds both tab
                // strips and can dispose this source control.
                StickyNoteTabsForm.EndDragSession(_note);
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
            Color paper = Color.FromArgb(_note.ColorArgb);
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
            TextRenderer.DrawText(e.Graphics, _note.DisplayTitle, Font, textArea,
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
            DrawTypeIcon(graphics, bounds, color, paper, _note);
        }

        private static void DrawTypeIcon(Graphics graphics, Rectangle bounds,
            Color color, Color paper, StickyNoteData note)
        {
            // The supplied pencil silhouette is used verbatim.  The receipt
            // and calendar keep the established vector metrics so all tabs
            // retain their previous alignment and stroke weight.
            if (!note.IsTodoList && !note.IsSchedule)
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
                if (note.IsTodoList) DrawTodoIcon(graphics, pen, bounds);
                else if (note.IsSchedule) DrawScheduleIcon(graphics, pen, bounds);
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
                    TypeIconColor(paper), paper, note);
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
