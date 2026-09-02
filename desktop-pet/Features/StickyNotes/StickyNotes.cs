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
        internal Func<StickyNotesImportPreview> PrepareImport;
        internal Func<StickyNotesImportPreview, bool> ConfirmImport;
        internal Action FullRestore;
    }

    internal sealed class StickyNotesImportPreview
    {
        internal StickyNotesImportPreview(StickyImportMergeResult merge,
            List<StickyNoteData> importedNotes)
        {
            Merge = merge;
            ImportedNotes = importedNotes ?? new List<StickyNoteData>();
        }

        internal StickyImportMergeResult Merge { get; private set; }
        internal List<StickyNoteData> ImportedNotes { get; private set; }
    }

    internal sealed class StickyNotesManagerForm : Form
    {
        private readonly Func<List<StickyNoteData>> _getNotes;
        private readonly StickyNotesManagerCommands _commands;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Button _deleteButton;
        private readonly Button _createButton;
        private readonly Button _showButton;
        private readonly Button _hideButton;
        private readonly Button _selectAllButton;
        private readonly Button _closeButton;
        private readonly Button _exportButton;
        private readonly Button _importButton;
        private readonly GroupBox _desktopGroup;
        private readonly Label _multiHint;
        private readonly Button _confirmImportButton;
        private ManagerMode _mode;
        private StickyImportMergeResult _importPlan;
        private List<StickyNoteData> _importedNotes;
        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private enum ManagerMode
        {
            Normal,
            ImportPreview,
            Busy
        }

        private sealed class ManagerRow
        {
            internal StickyNoteData Note;
            internal string StatusText;
            internal StickyImportActionKind PreviewKind;
        }

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

            _createButton = Button("新建", 446, delegate
            {
                if (_mode != ManagerMode.Normal) return;
                CreateRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            });
            _createButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _showButton = Button("显示/编辑", 526, delegate
            {
                if (_mode != ManagerMode.Normal) return;
                StickyNoteData note = SelectedNote();
                if (note == null) return;
                ShowRequested = note;
                DialogResult = DialogResult.OK;
                Close();
            });
            _showButton.Width = 88;
            _showButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _hideButton = Button("收起", 620, delegate
            {
                if (_mode != ManagerMode.Normal) return;
                StickyNoteData note = SelectedNote();
                if (note != null && _commands.HideNote != null)
                    _commands.HideNote(note);
                RefreshList();
            });
            _hideButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

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
            _list.ColumnClick += ManagerColumnClick;
            _list.DoubleClick += delegate
            {
                if (_mode != ManagerMode.Normal) return;
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
                if (_mode != ManagerMode.Normal) return;
                DeleteSelectedNotes();
            });
            _deleteButton.Width = 100;
            _deleteButton.Top = 420;
            _deleteButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _selectAllButton = Button("全选", 124, delegate
            {
                foreach (ListViewItem item in _list.Items) item.Selected = true;
            });
            _selectAllButton.Top = 420;
            _selectAllButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _multiHint = new Label();
            _multiHint.Text = "在空白处按住鼠标拖拽可框选多张；也支持 Ctrl/Shift 多选和 Delete。";
            _multiHint.AutoSize = false;
            _multiHint.AutoEllipsis = true;
            _multiHint.Location = new Point(210, 429);
            _multiHint.Size = new Size(180, 24);
            _multiHint.Anchor = AnchorStyles.Left | AnchorStyles.Right |
                AnchorStyles.Bottom;
            _closeButton = Button("关闭", 604, delegate { Close(); });
            _closeButton.Top = 420;
            _closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            _desktopGroup = new GroupBox();
            _desktopGroup.Text = "桌面整理";
            _desktopGroup.Location = new Point(16, 350);
            _desktopGroup.Size = new Size(568, 62);
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
            _desktopGroup.Controls.Add(collapseAll);
            _desktopGroup.Controls.Add(expandAll);
            _desktopGroup.Controls.Add(tileAll);
            LinkLabel fullRestore = new LinkLabel();
            fullRestore.Text = "高级：完整恢复…";
            fullRestore.AutoSize = true;
            fullRestore.Location = new Point(390, 31);
            fullRestore.Click += delegate
            {
                if (_mode == ManagerMode.Normal &&
                    _commands.FullRestore != null)
                    _commands.FullRestore();
            };
            _desktopGroup.Controls.Add(fullRestore);

            _exportButton = Button("导出备份…", 400, delegate
            {
                if (_mode != ManagerMode.Normal) return;
                if (_commands.ExportBackup != null)
                    _commands.ExportBackup();
                RefreshList();
            });
            _exportButton.Top = 420;
            _exportButton.Width = 90;
            _exportButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _importButton = Button("导入…", 495, delegate
            {
                if (_mode != ManagerMode.Normal) return;
                StickyNotesImportPreview preview = _commands.PrepareImport == null
                    ? null : _commands.PrepareImport();
                if (preview != null) BeginImportPreview(preview);
                return;
            });
            _importButton.Top = 420;
            _importButton.Width = 90;
            _importButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _confirmImportButton = Button("确认导入", 526, delegate
            {
                if (_mode != ManagerMode.ImportPreview || _importPlan == null ||
                    _commands.ConfirmImport == null) return;
                _mode = ManagerMode.Busy;
                UpdateModeControls();
                bool succeeded = _commands.ConfirmImport(
                    new StickyNotesImportPreview(_importPlan, _importedNotes));
                if (succeeded)
                {
                    _mode = ManagerMode.Normal;
                    _importPlan = null;
                    _importedNotes = null;
                    Text = "便利贴管理";
                    RefreshList();
                }
                else _mode = ManagerMode.ImportPreview;
                UpdateModeControls();
            });
            _confirmImportButton.Top = 420;
            _confirmImportButton.Width = 74;
            _confirmImportButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _confirmImportButton.Visible = false;

            Controls.Add(searchLabel);
            Controls.Add(_search);
            Controls.Add(_createButton);
            Controls.Add(_showButton);
            Controls.Add(_hideButton);
            Controls.Add(_list);
            Controls.Add(_deleteButton);
            Controls.Add(_selectAllButton);
            Controls.Add(_multiHint);
            Controls.Add(_desktopGroup);
            Controls.Add(_exportButton);
            Controls.Add(_importButton);
            Controls.Add(_confirmImportButton);
            Controls.Add(_closeButton);
            Shown += delegate { RefreshList(); };
            FormClosing += ManagerFormClosing;
            UpdateModeControls();
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
            List<ManagerRow> visibleRows = new List<ManagerRow>();
            foreach (ManagerRow row in BuildRows())
            {
                if (query.Length > 0 && row.Note.SearchText.IndexOf(query,
                    StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                visibleRows.Add(row);
            }
            if (_sortColumn >= 0)
                visibleRows.Sort(CompareRowsForView);
            UpdateSortIndicators();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (ManagerRow row in visibleRows)
            {
                StickyNoteData note = row.Note;
                DateTime? reminder = note.ReminderUtc;
                string reminderText = reminder.HasValue && reminder.Value > DateTime.UtcNow
                    ? reminder.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
                ListViewItem item = new ListViewItem(note.DisplayTitle);
                item.SubItems.Add(row.StatusText);
                item.SubItems.Add(reminderText);
                item.SubItems.Add(note.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm"));
                item.Tag = note;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            RefreshSelectionState();
        }

        private List<ManagerRow> BuildRows()
        {
            List<ManagerRow> rows = new List<ManagerRow>();
            if (_mode == ManagerMode.ImportPreview && _importPlan != null)
            {
                Dictionary<string, StickyNoteData> current = new Dictionary<
                    string, StickyNoteData>(StringComparer.OrdinalIgnoreCase);
                foreach (StickyNoteData note in _getNotes() ??
                    new List<StickyNoteData>())
                    if (note != null && !current.ContainsKey(note.Id))
                        current.Add(note.Id, note);
                Dictionary<string, StickyNoteData> imported = new Dictionary<
                    string, StickyNoteData>(StringComparer.OrdinalIgnoreCase);
                foreach (StickyNoteData note in _importedNotes ??
                    new List<StickyNoteData>())
                    if (note != null && !imported.ContainsKey(note.Id))
                        imported.Add(note.Id, note);
                foreach (StickyImportAction action in _importPlan.Actions)
                {
                    StickyNoteData note;
                    if (action.Kind == StickyImportActionKind.SkipIdentical)
                        current.TryGetValue(action.ImportedNoteId,
                            out note);
                    else imported.TryGetValue(action.ImportedNoteId,
                        out note);
                    if (note == null) continue;
                    rows.Add(new ManagerRow
                    {
                        Note = note,
                        StatusText = PreviewStatus(action.Kind),
                        PreviewKind = action.Kind
                    });
                }
                return rows;
            }

            foreach (StickyNoteData note in _getNotes() ??
                new List<StickyNoteData>())
            {
                if (note == null) continue;
                rows.Add(new ManagerRow
                {
                    Note = note,
                    StatusText = NormalStatus(note),
                    PreviewKind = StickyImportActionKind.Add
                });
            }
            return rows;
        }

        private static string NormalStatus(StickyNoteData note)
        {
            string status = note.Visible ? "显示中" : "已收起";
            if (note.IsTodoList)
                status = "待办 " + CompletedTodoCount(note) + "/" +
                    note.TodoItems.Count;
            else if (note.IsSchedule)
                status = "日程 " + note.ScheduleItems.Count + "项";
            return status;
        }

        private static int CompletedTodoCount(StickyNoteData note)
        {
            int completed = 0;
            foreach (StickyTodoItem todo in note.TodoItems)
                if (todo != null && todo.Completed) completed++;
            return completed;
        }

        private static string PreviewStatus(StickyImportActionKind kind)
        {
            switch (kind)
            {
                case StickyImportActionKind.SkipIdentical: return "已存在";
                case StickyImportActionKind.PreserveConflictCopy:
                    return "冲突副本";
                default: return "待导入";
            }
        }

        private void ManagerColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }
            RefreshList();
        }

        private void UpdateSortIndicators()
        {
            string[] headings = { "名称 / 摘要", "状态", "提醒", "修改时间" };
            for (int i = 0; i < headings.Length && i < _list.Columns.Count; i++)
            {
                string indicator = _sortColumn == i
                    ? (_sortAscending ? " ▲" : " ▼") : String.Empty;
                _list.Columns[i].Text = headings[i] + indicator;
            }
        }

        private int CompareRowsForView(ManagerRow left, ManagerRow right)
        {
            int result;
            switch (_sortColumn)
            {
                case 0:
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(
                        left.Note.DisplayTitle, right.Note.DisplayTitle);
                    break;
                case 1:
                    result = _mode == ManagerMode.ImportPreview
                        ? left.PreviewKind.CompareTo(right.PreviewKind)
                        : CompareStatus(left.Note, right.Note);
                    break;
                case 2:
                    result = CompareReminder(left.Note, right.Note);
                    break;
                case 3:
                    result = DateTime.Compare(left.Note.ModifiedUtc,
                        right.Note.ModifiedUtc);
                    break;
                default:
                    result = 0;
                    break;
            }
            if (result == 0)
                result = StringComparer.OrdinalIgnoreCase.Compare(
                    left.Note.Id, right.Note.Id);
            return _sortAscending ? result : -result;
        }

        private static int CompareStatus(StickyNoteData left,
            StickyNoteData right)
        {
            int kind = ContentKind(left).CompareTo(ContentKind(right));
            if (kind != 0) return kind;
            int result;
            if (left.IsTodoList)
            {
                result = CompletedTodoCount(left).CompareTo(
                    CompletedTodoCount(right));
                if (result != 0) return result;
                result = left.TodoItems.Count.CompareTo(right.TodoItems.Count);
                if (result != 0) return result;
            }
            else if (left.IsSchedule)
            {
                result = left.ScheduleItems.Count.CompareTo(
                    right.ScheduleItems.Count);
                if (result != 0) return result;
            }
            return left.Visible.CompareTo(right.Visible);
        }

        private static int ContentKind(StickyNoteData note)
        {
            return note.IsTodoList ? 1 : note.IsSchedule ? 2 : 0;
        }

        private static int CompareReminder(StickyNoteData left,
            StickyNoteData right)
        {
            DateTime? leftReminder = left.ReminderUtc;
            DateTime? rightReminder = right.ReminderUtc;
            if (!leftReminder.HasValue || !rightReminder.HasValue)
            {
                if (leftReminder.HasValue == rightReminder.HasValue) return 0;
                return leftReminder.HasValue ? -1 : 1;
            }
            return DateTime.Compare(leftReminder.Value, rightReminder.Value);
        }

        // Focused runtime hooks keep sorting tests independent of canonical
        // order. They only operate on the form's view list.
        internal void RefreshForTest()
        {
            RefreshList();
        }

        internal void SortColumnForTest(int columnIndex)
        {
            ManagerColumnClick(this, new ColumnClickEventArgs(columnIndex));
        }

        internal void SearchForTest(string query)
        {
            _search.Text = query ?? String.Empty;
        }

        internal List<string> DisplayedTitlesForTest()
        {
            List<string> titles = new List<string>();
            foreach (ListViewItem item in _list.Items)
                titles.Add(item.Text);
            return titles;
        }

        internal List<string> DisplayedStatusesForTest()
        {
            List<string> statuses = new List<string>();
            foreach (ListViewItem item in _list.Items)
                statuses.Add(item.SubItems.Count > 1
                    ? item.SubItems[1].Text : String.Empty);
            return statuses;
        }

        internal string SortIndicatorForTest(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < _list.Columns.Count
                ? _list.Columns[columnIndex].Text : String.Empty;
        }

        private void BeginImportPreview(StickyNotesImportPreview preview)
        {
            if (preview == null || preview.Merge == null ||
                preview.Merge.Actions == null) return;
            _importPlan = preview.Merge;
            _importedNotes = new List<StickyNoteData>(
                preview.ImportedNotes ?? new List<StickyNoteData>());
            _mode = ManagerMode.ImportPreview;
            _sortColumn = -1;
            _sortAscending = true;
            Text = "便利贴管理 — 导入预览";
            UpdateModeControls();
            RefreshList();
        }

        internal void BeginImportPreviewForTest(StickyNotesImportPreview preview)
        {
            BeginImportPreview(preview);
        }

        internal bool IsImportPreviewForTest
        {
            get { return _mode == ManagerMode.ImportPreview; }
        }

        private void ManagerFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_mode == ManagerMode.Busy)
            {
                e.Cancel = true;
                return;
            }
            if (_mode == ManagerMode.ImportPreview)
                ClearImportPreview();
        }

        private void ClearImportPreview()
        {
            _mode = ManagerMode.Normal;
            _importPlan = null;
            _importedNotes = null;
        }

        internal void CancelImportPreviewForTest()
        {
            ClearImportPreview();
            Text = "便利贴管理";
            UpdateModeControls();
            RefreshList();
        }

        private void UpdateModeControls()
        {
            bool normal = _mode == ManagerMode.Normal;
            bool preview = _mode == ManagerMode.ImportPreview;
            bool busy = _mode == ManagerMode.Busy;
            _search.Enabled = !busy;
            _list.Enabled = !busy;
            _createButton.Enabled = normal;
            _showButton.Enabled = normal;
            _hideButton.Enabled = normal;
            _selectAllButton.Enabled = normal;
            _desktopGroup.Enabled = normal;
            _exportButton.Enabled = normal;
            _importButton.Enabled = normal;
            _confirmImportButton.Visible = preview;
            _confirmImportButton.Enabled = preview && _importPlan != null &&
                _importPlan.AddedCount > 0;
            _closeButton.Enabled = !busy;
            _closeButton.Text = preview ? "取消" : "关闭";
            if (busy)
                _multiHint.Text = "正在导入…";
            else if (preview)
            {
                _multiHint.Text = "新增 " + _importPlan.AddedCount +
                    " · 跳过 " + _importPlan.SkippedIdenticalCount +
                    " · 冲突副本 " + _importPlan.ConflictCount;
            }
            else
                _multiHint.Text = "在空白处按住鼠标拖拽可框选多张；也支持 Ctrl/Shift 多选和 Delete。";
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
            _deleteButton.Enabled = _mode == ManagerMode.Normal && count > 0;
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
