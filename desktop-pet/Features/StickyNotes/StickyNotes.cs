using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PennyPet
{

    internal sealed class ImeFriendlyRichTextBox : RichTextBox
    {
        private const int WmImeStartComposition = 0x010D;
        private const int WmImeEndComposition = 0x010E;
        private const int WmImeComposition = 0x010F;
        private bool _compositionActive;

        public event EventHandler CompositionStarted;
        public event EventHandler CompositionCommitted;

        internal bool IsImeComposing
        {
            get { return _compositionActive; }
        }

        internal static bool StartsOrUpdatesComposition(int message)
        {
            return message == WmImeStartComposition ||
                message == WmImeComposition;
        }

        protected override void WndProc(ref Message message)
        {
            // Tell the pet to stop layered-frame rendering before Windows lets
            // the IME process the composition message.  Doing this after
            // base.WndProc leaves a short race in which pinyin can be committed
            // as literal Latin text on slower machines.
            if (StartsOrUpdatesComposition(message.Msg) && !_compositionActive)
            {
                _compositionActive = true;
                EventHandler started = CompositionStarted;
                if (started != null) started(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
            if (IsDisposed || !IsHandleCreated) return;
            if (message.Msg != WmImeEndComposition) return;
            _compositionActive = false;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    // A fast typist may already have started the next IME
                    // composition before this queued callback runs.  Never let
                    // an old END message cancel that newer composition.
                    if (_compositionActive) return;
                    EventHandler handler = CompositionCommitted;
                    if (handler != null) handler(this, EventArgs.Empty);
                });
            }
            catch { }
        }
    }

    internal sealed class ImeFriendlyTextBox : TextBox
    {
        private const int WmImeStartComposition = 0x010D;
        private const int WmImeEndComposition = 0x010E;
        private const int WmImeComposition = 0x010F;
        private bool _compositionActive;

        public event EventHandler CompositionStarted;
        public event EventHandler CompositionCommitted;

        internal bool IsImeComposing
        {
            get { return _compositionActive; }
        }

        protected override void WndProc(ref Message message)
        {
            bool composingMessage = message.Msg == WmImeStartComposition ||
                message.Msg == WmImeComposition;
            if (composingMessage && !_compositionActive)
            {
                _compositionActive = true;
                EventHandler started = CompositionStarted;
                if (started != null) started(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
            if (IsDisposed || !IsHandleCreated ||
                message.Msg != WmImeEndComposition) return;
            _compositionActive = false;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_compositionActive) return;
                    EventHandler committed = CompositionCommitted;
                    if (committed != null) committed(this, EventArgs.Empty);
                });
            }
            catch { }
        }
    }

    internal sealed class ImeCompositionEventArgs : EventArgs
    {
        public ImeCompositionEventArgs(bool active)
        {
            Active = active;
        }

        public bool Active { get; private set; }
    }

    internal sealed class DockHorizontalResizeEventArgs : EventArgs
    {
        public DockHorizontalResizeEventArgs(int left, int width)
        {
            Left = left;
            Width = width;
        }

        public int Left { get; private set; }
        public int Width { get; private set; }
    }

    internal sealed class DockDividerResizeEventArgs : EventArgs
    {
        public DockDividerResizeEventArgs(int height)
        {
            Height = height;
        }

        public int Height { get; private set; }
    }

    internal sealed class NoteTitleDialog : Form
    {
        private readonly TextBox _title;

        public NoteTitleDialog(string currentTitle)
        {
            Text = "命名便利贴";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(390, 150);
            Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);
            ImeMode = ImeMode.NoControl;

            Label hint = new Label();
            hint.Text = "便利贴名称（支持多语言；留空时使用内容摘要）：";
            hint.AutoSize = true;
            hint.Location = new Point(20, 18);
            _title = new TextBox();
            _title.ImeMode = ImeMode.NoControl;
            _title.MaxLength = StickyNoteLimits.MaximumTitleCharacters;
            _title.Text = currentTitle ?? String.Empty;
            _title.Location = new Point(20, 50);
            _title.Size = new Size(350, 28);

            Button ok = new Button();
            ok.Text = "保存";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(204, 98);
            ok.Size = new Size(78, 32);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(292, 98);
            cancel.Size = new Size(78, 32);
            Controls.Add(hint);
            Controls.Add(_title);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ActiveControl = _title;
            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Activate();
                    ActiveControl = _title;
                    _title.Select();
                    _title.Focus();
                    _title.SelectAll();
                });
            };
        }

        public string NoteTitle
        {
            get { return (_title.Text ?? String.Empty).Trim(); }
        }

        internal bool TitleInputIsInitialActive
        {
            get { return Object.ReferenceEquals(ActiveControl, _title); }
        }

        internal bool UsesUnforcedMultilingualIme
        {
            get { return ImeMode == ImeMode.NoControl &&
                _title.ImeMode == ImeMode.NoControl; }
        }
    }

    internal sealed class MarqueeListView : ListView
    {
        private bool _marqueeSelecting;
        private Point _marqueeStart;
        private Rectangle _reversibleFrame = Rectangle.Empty;
        private readonly HashSet<ListViewItem> _initialSelection =
            new HashSet<ListViewItem>();

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || HitTest(e.Location).Item != null)
                return;
            _marqueeSelecting = true;
            _marqueeStart = e.Location;
            _initialSelection.Clear();
            if ((ModifierKeys & Keys.Control) != 0)
            {
                foreach (ListViewItem item in SelectedItems)
                    _initialSelection.Add(item);
            }
            else
            {
                foreach (ListViewItem item in Items) item.Selected = false;
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_marqueeSelecting || (MouseButtons & MouseButtons.Left) == 0)
            {
                base.OnMouseMove(e);
                return;
            }
            EraseReversibleFrame();
            Rectangle clientFrame = NormalizeRectangle(_marqueeStart, e.Location);
            _reversibleFrame = RectangleToScreen(clientFrame);
            if (clientFrame.Width > 2 || clientFrame.Height > 2)
                ControlPaint.DrawReversibleFrame(_reversibleFrame,
                    Color.FromArgb(70, 110, 170), FrameStyle.Dashed);
            foreach (ListViewItem item in Items)
            {
                Rectangle fullRow = new Rectangle(0, item.Bounds.Top,
                    Math.Max(ClientSize.Width, item.Bounds.Width), item.Bounds.Height);
                bool hit = clientFrame.IntersectsWith(fullRow);
                item.Selected = hit || _initialSelection.Contains(item);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            EndMarqueeSelection();
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) EndMarqueeSelection();
            base.OnMouseCaptureChanged(e);
        }

        private void EndMarqueeSelection()
        {
            if (!_marqueeSelecting) return;
            EraseReversibleFrame();
            _marqueeSelecting = false;
            Capture = false;
            _initialSelection.Clear();
        }

        private void EraseReversibleFrame()
        {
            if (_reversibleFrame == Rectangle.Empty) return;
            ControlPaint.DrawReversibleFrame(_reversibleFrame,
                Color.FromArgb(70, 110, 170), FrameStyle.Dashed);
            _reversibleFrame = Rectangle.Empty;
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            return Rectangle.FromLTRB(Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y), Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) EndMarqueeSelection();
            base.Dispose(disposing);
        }
    }

    // Concrete command bundle for the management surface. It keeps the form
    // as a UI command aggregator without introducing a generic service layer.
    internal sealed class StickyNotesManagerCommands
    {
        internal Action<StickyNoteData> HideNote;
        internal Action<StickyNoteData> DeleteNote;
        internal Action CollapseAll;
        internal Action ExpandAll;
        internal Action TileAll;
        internal Action ExportBackup;
        internal Action ImportBackup;
    }

    internal sealed class StickyNotesManagerForm : Form
    {
        private readonly Func<List<StickyNoteData>> _getNotes;
        private readonly StickyNotesManagerCommands _commands;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Button _deleteButton;

        internal bool CreateRequested { get; private set; }
        internal StickyNoteData ShowRequested { get; private set; }

        public StickyNotesManagerForm(Func<List<StickyNoteData>> getNotes,
            StickyNotesManagerCommands commands)
        {
            if (getNotes == null) throw new ArgumentNullException("getNotes");
            if (commands == null) throw new ArgumentNullException("commands");
            _getNotes = getNotes;
            _commands = commands;
            Text = "便利贴管理";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            MinimumSize = new Size(620, 420);
            ClientSize = new Size(700, 470);
            Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);

            Label searchLabel = new Label();
            searchLabel.Text = "搜索：";
            searchLabel.AutoSize = true;
            searchLabel.Location = new Point(16, 18);
            _search = new TextBox();
            _search.ImeMode = ImeMode.NoControl;
            _search.Location = new Point(70, 14);
            _search.Size = new Size(360, 28);
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _search.TextChanged += delegate { RefreshList(); };

            Button create = Button("新建", 446, delegate
            {
                CreateRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            });
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button show = Button("显示/编辑", 526, delegate
            {
                StickyNoteData note = SelectedNote();
                if (note == null) return;
                ShowRequested = note;
                DialogResult = DialogResult.OK;
                Close();
            });
            show.Width = 88;
            show.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button hide = Button("收起", 620, delegate
            {
                StickyNoteData note = SelectedNote();
                if (note != null && _commands.HideNote != null)
                    _commands.HideNote(note);
                RefreshList();
            });
            hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _list = new MarqueeListView();
            _list.View = View.Details;
            _list.MultiSelect = true;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Location = new Point(16, 54);
            _list.Size = new Size(668, 290);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                AnchorStyles.Right;
            _list.Columns.Add("名称 / 摘要", 310);
            _list.Columns.Add("状态", 80);
            _list.Columns.Add("提醒", 150);
            _list.Columns.Add("修改时间", 120);
            _list.DoubleClick += delegate
            {
                StickyNoteData note = SelectedNote();
                if (note == null) return;
                ShowRequested = note;
                DialogResult = DialogResult.OK;
                Close();
            };
            _list.SelectedIndexChanged += delegate { RefreshSelectionState(); };
            _list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedNotes();
                    e.SuppressKeyPress = true;
                }
            };

            _deleteButton = Button("删除所选", 16, delegate
            {
                DeleteSelectedNotes();
            });
            _deleteButton.Width = 100;
            _deleteButton.Top = 420;
            _deleteButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Button selectAll = Button("全选", 124, delegate
            {
                foreach (ListViewItem item in _list.Items) item.Selected = true;
            });
            selectAll.Top = 420;
            selectAll.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Label multiHint = new Label();
            multiHint.Text = "在空白处按住鼠标拖拽可框选多张；也支持 Ctrl/Shift 多选和 Delete。";
            multiHint.AutoSize = false;
            multiHint.AutoEllipsis = true;
            multiHint.Location = new Point(210, 429);
            multiHint.Size = new Size(180, 24);
            multiHint.Anchor = AnchorStyles.Left | AnchorStyles.Right |
                AnchorStyles.Bottom;
            Button close = Button("关闭", 604, delegate { Close(); });
            close.Top = 420;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            GroupBox desktop = new GroupBox();
            desktop.Text = "桌面整理";
            desktop.Location = new Point(16, 350);
            desktop.Size = new Size(568, 62);
            Button collapseAll = Button("收起全部", 10, delegate
            {
                if (_commands.CollapseAll != null) _commands.CollapseAll();
                RefreshList();
            });
            collapseAll.Top = 23;
            collapseAll.Width = 100;
            Button expandAll = Button("展开全部", 120, delegate
            {
                if (_commands.ExpandAll != null) _commands.ExpandAll();
                RefreshList();
            });
            expandAll.Top = 23;
            expandAll.Width = 100;
            Button tileAll = Button("平铺到当前屏幕", 230, delegate
            {
                if (_commands.TileAll != null) _commands.TileAll();
            });
            tileAll.Top = 23;
            tileAll.Width = 130;
            desktop.Controls.Add(collapseAll);
            desktop.Controls.Add(expandAll);
            desktop.Controls.Add(tileAll);

            Button export = Button("导出备份…", 400, delegate
            {
                if (_commands.ExportBackup != null)
                    _commands.ExportBackup();
                RefreshList();
            });
            export.Top = 420;
            export.Width = 90;
            export.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            Button import = Button("导入…", 495, delegate
            {
                if (_commands.ImportBackup != null)
                    _commands.ImportBackup();
                RefreshList();
            });
            import.Top = 420;
            import.Width = 90;
            import.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            Controls.Add(searchLabel);
            Controls.Add(_search);
            Controls.Add(create);
            Controls.Add(show);
            Controls.Add(hide);
            Controls.Add(_list);
            Controls.Add(_deleteButton);
            Controls.Add(selectAll);
            Controls.Add(multiHint);
            Controls.Add(desktop);
            Controls.Add(export);
            Controls.Add(import);
            Controls.Add(close);
            Shown += delegate { RefreshList(); };
        }

        private Button Button(string text, int left, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(left, 12);
            button.Size = new Size(74, 32);
            button.Click += click;
            return button;
        }

        private void RefreshList()
        {
            string query = (_search.Text ?? String.Empty).Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (StickyNoteData note in _getNotes())
            {
                if (query.Length > 0 && note.SearchText.IndexOf(query,
                    StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                DateTime? reminder = note.ReminderUtc;
                string reminderText = reminder.HasValue && reminder.Value > DateTime.UtcNow
                    ? reminder.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
                ListViewItem item = new ListViewItem(note.Summary);
                string status = note.Visible ? "显示中" : "已收起";
                if (note.IsTodoList)
                {
                    int completed = 0;
                    foreach (StickyTodoItem todo in note.TodoItems)
                    {
                        if (todo.Completed) completed++;
                    }
                    status = "待办 " + completed + "/" + note.TodoItems.Count;
                }
                else if (note.IsSchedule)
                    status = "日程 " + note.ScheduleItems.Count + "项";
                item.SubItems.Add(status);
                item.SubItems.Add(reminderText);
                item.SubItems.Add(note.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm"));
                item.Tag = note;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            RefreshSelectionState();
        }

        private void DeleteSelectedNotes()
        {
            List<StickyNoteData> selected = SelectedNotes();
            if (selected.Count == 0) return;
            string message = selected.Count == 1
                ? "确定删除这张便利贴吗？此操作无法撤销。"
                : "确定一次删除选中的 " + selected.Count +
                    " 张便利贴吗？此操作无法撤销。";
            if (MessageBox.Show(this, message, "批量删除便利贴",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) !=
                DialogResult.Yes) return;
            foreach (StickyNoteData note in selected)
                if (_commands.DeleteNote != null) _commands.DeleteNote(note);
            RefreshList();
        }

        private List<StickyNoteData> SelectedNotes()
        {
            List<StickyNoteData> selected = new List<StickyNoteData>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                StickyNoteData note = item.Tag as StickyNoteData;
                if (note != null) selected.Add(note);
            }
            return selected;
        }

        private void RefreshSelectionState()
        {
            if (_deleteButton == null) return;
            int count = _list.SelectedItems.Count;
            _deleteButton.Enabled = count > 0;
            _deleteButton.Text = count > 1
                ? "删除所选（" + count + "）" : "删除所选";
        }

        private StickyNoteData SelectedNote()
        {
            return _list.SelectedItems.Count == 0
                ? null : _list.SelectedItems[0].Tag as StickyNoteData;
        }

        internal bool SupportsMarqueeBatchDelete
        {
            get
            {
                return _list is MarqueeListView && _list.MultiSelect &&
                    _deleteButton != null;
            }
        }
    }
}
