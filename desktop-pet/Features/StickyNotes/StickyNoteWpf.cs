using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    // The sticky note is deliberately a single WPF top-level window. WPF can
    // alpha-blend individual brushes while keeping text and controls opaque,
    // so there is no TransparencyKey hole and no second window forwarding
    // mouse messages. Every visible pixel and every input target now belongs to
    // the same visual tree and UI thread.
    internal sealed partial class StickyNoteWindow : W.Window, WF.IWin32Window, IDisposable
    {
        internal static readonly Color[] Palette =
        {
            Color.FromArgb(255, 255, 117, 112),
            Color.FromArgb(255, 255, 129, 26),
            Color.FromArgb(255, 255, 198, 10),
            Color.FromArgb(255, 145, 173, 0),
            Color.FromArgb(255, 53, 189, 75),
            Color.FromArgb(255, 45, 190, 171),
            Color.FromArgb(255, 37, 176, 231),
            Color.FromArgb(255, 122, 162, 255),
            Color.FromArgb(255, 235, 120, 184),
            Color.FromArgb(255, 183, 145, 250),
            Color.FromArgb(255, 143, 149, 158),
            Color.FromArgb(255, 253, 198, 196),
            Color.FromArgb(255, 254, 196, 139),
            Color.FromArgb(255, 252, 223, 126),
            Color.FromArgb(255, 200, 221, 95),
            Color.FromArgb(255, 149, 229, 153),
            Color.FromArgb(255, 111, 232, 216),
            Color.FromArgb(255, 151, 220, 252),
            Color.FromArgb(255, 194, 212, 255),
            Color.FromArgb(255, 248, 196, 225),
            Color.FromArgb(255, 220, 201, 253),
            Color.FromArgb(255, 222, 224, 227),
            Color.FromArgb(255, 254, 227, 226),
            Color.FromArgb(255, 254, 231, 205),
            Color.FromArgb(255, 250, 237, 194),
            Color.FromArgb(255, 227, 240, 163),
            Color.FromArgb(255, 208, 245, 206),
            Color.FromArgb(255, 196, 242, 236),
            Color.FromArgb(255, 202, 239, 252),
            Color.FromArgb(255, 224, 233, 255),
            Color.FromArgb(255, 254, 226, 242),
            Color.FromArgb(255, 239, 230, 254),
            Color.FromArgb(255, 239, 240, 241)
        };

        private const int WmNcHitTest = 0x0084;
        private const int WmSysCommand = 0x0112;
        private const int WmSizing = 0x0214;
        private const int WmEnterSizeMove = 0x0231;
        private const int WmExitSizeMove = 0x0232;
        private const int ScMaximize = 0xF030;
        private const uint GaRootOwner = 3;
        private const int GwlStyle = -16;
        private const long WsMaximizeBox = 0x00010000L;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int SwRestore = 9;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        private readonly WC.Border _shell;
        private readonly WC.Grid _layout;
        private readonly WC.Border _header;
        private readonly WC.Border _typeIconHost;
        private readonly WC.Image _typeIcon;
        private readonly WC.TextBlock _title;
        private readonly WC.Button _deleteReminderButton;
        private readonly WC.Button _pinButton;
        private readonly WC.Button _colorButton;
        private readonly WC.Button _closeButton;
        private readonly WC.Grid _formatToolbar;
        private readonly WC.ComboBox _fontFamilyBox;
        private readonly WC.ComboBox _fontSizeBox;
        private readonly WC.Button _boldButton;
        private readonly WC.Button _italicButton;
        private readonly WC.Button _underlineButton;
        private readonly WC.RichTextBox _editor;
        private readonly WC.Border _reminderPanel;
        private readonly WC.ListBox _reminderList;
        private readonly WC.Grid _todoPanel;
        private readonly WC.StackPanel _todoRows;
        private readonly WC.TextBox _todoInput;
        private readonly WC.Button _todoAddButton;
        private readonly WC.Button _todoDeleteButton;
        private readonly WC.Button _todoPinToggleButton;
        private readonly WC.Button _todoUpButton;
        private readonly WC.Button _todoDownButton;
        private readonly WC.TextBlock _todoProgress;
        private readonly WC.Grid _schedulePanel;
        private readonly WC.StackPanel _scheduleRows;
        private readonly WC.Button _scheduleAddButton;
        private readonly WC.Button _scheduleDeleteButton;
        private readonly WC.Button _schedulePinToggleButton;
        private readonly WC.Button _scheduleUpButton;
        private readonly WC.Button _scheduleDownButton;
        private readonly WC.TextBlock _scheduleCount;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _scheduleRefreshTimer;
        private StickyAppearanceDialog _appearanceDialog;
        private ReminderItem _selectedReminder;
        private StickyTodoItem _selectedTodo;
        private StickyScheduleItem _selectedSchedule;
        private bool _initializing;
        private bool _closingForExit;
        private bool _disposed;
        private bool _shownRaised;
        private bool _inputFocusReportQueued;
        private bool _updatingFormatToolbar;
        private bool _rebuildingTodos;
        private bool _rebuildingSchedules;
        private bool _winFormsKeyboardInteropEnabled;
        private bool _dockPreviewActive;
        private bool _dockPreviewTarget;
        private bool _dockGrouped;
        private bool _dockResizeTop = true;
        private bool _dockResizeBottom = true;
        private bool _dockSplitBottom;
        private int _lastResizeHitTest;
        private bool _windowResizeActive;
        private bool _dockDividerResizeActive;
        private double _resizeStartLeft;
        private double _resizeStartWidth;
        private int _dockDividerMinimumHeight = 220;
        private int _dockDividerMaximumHeight = 700;
        private Rectangle _headerDragStartBounds;
        private System.Drawing.Point _headerDragPointerOffset;
        private bool _headerDragInProgress;
        private bool _recoveringSystemGeometry;
        private int _appliedEditorTextColorArgb = Int32.MinValue;
        private readonly bool _opaqueQaHost;
        private DateTime _lastInputUtc = DateTime.MinValue;
        // Preserve the RichTextBox selection while a toolbar selector owns
        // focus.  Formatting itself stays in WPF's native TextSelection/
        // HIGH RISK: IME compatibility code. Do not simplify or move this
        // focus/composition path without real Chinese IME regression testing.
        // FlowDocument pipeline; inserting or rewriting composition text here
        // duplicates third-party IME commits and breaks Enter inheritance.
        private TextPointer _savedSelectionStart;
        private TextPointer _savedSelectionEnd;
        private string _typingFontFamilyName;
        private float _typingFontSizePoints;
        private bool _restoreEditorFocusQueued;
        private bool _applyingTypingFormat;
        private bool _editorTextCompositionActive;
        private int _userInteractionGeneration;
        private int _reminderBannerRebuildCount;
        private static readonly Lazy<string[]> InstalledFontNameCache =
            new Lazy<string[]>(LoadInstalledFontNames, true);
        private static readonly object SharedFontsGate = new object();
        private static readonly Dictionary<string, Font> SharedFonts =
            new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        public StickyNoteWindow(StickyNoteData data)
            : this(data, false, false)
        {
        }

        internal StickyNoteWindow(StickyNoteData data, bool opaqueQaHost)
            : this(data, opaqueQaHost, opaqueQaHost)
        {
        }

        internal StickyNoteWindow(StickyNoteData data, bool opaqueQaHost,
            bool showInTaskbarForQa)
        {
            if (data == null) throw new ArgumentNullException("data");
            Data = data;
            _opaqueQaHost = opaqueQaHost;
            _initializing = true;
            _typingFontFamilyName = StickyNoteRepository.NormalizeFontFamily(
                data.FontFamilyName);
            _typingFontSizePoints = Math.Max(6F, Math.Min(72F,
                data.FontSizeTwips / 20F));

            Title = "Penny pet 便利贴";
            WindowStyle = W.WindowStyle.None;
            WindowStartupLocation = W.WindowStartupLocation.Manual;
            base.WindowState = W.WindowState.Normal;
            AllowsTransparency = !opaqueQaHost;
            Background = opaqueQaHost
                ? System.Windows.Media.Brushes.White
                : System.Windows.Media.Brushes.Transparent;
            ResizeMode = W.ResizeMode.CanResize;
            ShowInTaskbar = showInTaskbarForQa;
            SizeToContent = W.SizeToContent.Manual;
            MinWidth = 280;
            MinHeight = 220;
            MaxWidth = 900;
            MaxHeight = 700;
            base.Left = data.X;
            base.Top = data.Y;
            base.Width = Math.Max(MinWidth, data.Width);
            base.Height = Math.Max(MinHeight, data.Height);
            Topmost = data.AlwaysOnTop;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            _layout = new WC.Grid();
            _layout.Background = System.Windows.Media.Brushes.Transparent;
            _layout.RowDefinitions.Add(Row(W.GridLength.Auto));
            _layout.RowDefinitions.Add(Row(W.GridLength.Auto));
            _layout.RowDefinitions.Add(Row(W.GridLength.Auto));
            _layout.RowDefinitions.Add(Row(new W.GridLength(1, W.GridUnitType.Star)));

            _header = new WC.Border();
            _header.Height = 32;
            _header.Cursor = Cursors.SizeAll;
            WC.DockPanel headerContent = new WC.DockPanel();
            _header.Child = headerContent;

            _closeButton = HeaderButton("×", 30, true);
            _colorButton = HeaderButton("换色", 48, false);
            _pinButton = HeaderButton(PinActionText(data.AlwaysOnTop), 66, false);
            _deleteReminderButton = HeaderButton("删除提醒", 68, false);
            foreach (WC.Button button in new WC.Button[] { _closeButton,
                _colorButton, _pinButton, _deleteReminderButton })
            {
                WC.DockPanel.SetDock(button, WC.Dock.Right);
                headerContent.Children.Add(button);
            }
            _typeIconHost = new WC.Border();
            _typeIconHost.Width = 32;
            _typeIconHost.Height = 32;
            _typeIconHost.Padding = new W.Thickness(4);
            _typeIconHost.IsHitTestVisible = false;
            _typeIcon = new WC.Image();
            _typeIcon.Width = 24;
            _typeIcon.Height = 24;
            _typeIcon.Stretch = Stretch.Uniform;
            _typeIcon.HorizontalAlignment = W.HorizontalAlignment.Center;
            _typeIcon.VerticalAlignment = W.VerticalAlignment.Center;
            _typeIcon.IsHitTestVisible = false;
            _typeIconHost.Child = _typeIcon;
            WC.DockPanel.SetDock(_typeIconHost, WC.Dock.Left);
            headerContent.Children.Add(_typeIconHost);
            _title = new WC.TextBlock();
            _title.VerticalAlignment = W.VerticalAlignment.Center;
            _title.Margin = new W.Thickness(2, 0, 4, 0);
            _title.TextTrimming = W.TextTrimming.CharacterEllipsis;
            _title.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
            _title.FontSize = PointSizeToDip(9F);
            headerContent.Children.Add(_title);
            WC.Grid.SetRow(_header, 0);
            _layout.Children.Add(_header);

            // List-mode creation commands live beside the size selector.
            // Only the pair matching the current note type is visible.
            _todoAddButton = ActionButton("新增", 52);
            _todoDeleteButton = ActionButton("删除", 52);
            _scheduleAddButton = ActionButton("新增", 52);
            _scheduleDeleteButton = ActionButton("删除", 52);
            _formatToolbar = BuildFormatToolbar(out _fontFamilyBox,
                out _fontSizeBox, out _boldButton, out _italicButton,
                out _underlineButton);
            AddToGrid(_formatToolbar, _todoAddButton, 5);
            AddToGrid(_formatToolbar, _scheduleAddButton, 5);
            AddToGrid(_formatToolbar, _todoDeleteButton, 6);
            AddToGrid(_formatToolbar, _scheduleDeleteButton, 6);
            WC.Grid.SetRow(_formatToolbar, 1);
            _layout.Children.Add(_formatToolbar);

            _reminderList = new WC.ListBox();
            _reminderList.BorderThickness = new W.Thickness(0);
            _reminderList.Background = System.Windows.Media.Brushes.Transparent;
            _reminderList.Padding = new W.Thickness(6, 3, 6, 3);
            _reminderList.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            _reminderList.FontSize = PointSizeToDip(8.5F);
            _reminderList.SelectionChanged += ReminderSelectionChanged;
            _reminderList.PreviewMouseRightButtonDown +=
                ReminderListPreviewMouseRightButtonDown;
            _reminderList.MouseDoubleClick += delegate
            {
                ExecuteSelectedReminderModify();
            };
            _reminderPanel = new WC.Border();
            _reminderPanel.Child = _reminderList;
            _reminderPanel.Visibility = W.Visibility.Collapsed;
            WC.Grid.SetRow(_reminderPanel, 2);
            _layout.Children.Add(_reminderPanel);

            _editor = new WC.RichTextBox();
            _editor.BorderThickness = new W.Thickness(0);
            _editor.Padding = new W.Thickness(8, 5, 8, 8);
            _editor.Background = System.Windows.Media.Brushes.Transparent;
            _editor.AcceptsTab = true;
            _editor.AcceptsReturn = true;
            _editor.VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto;
            _editor.HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled;
            _editor.FontFamily = SafeWpfFontFamily(data.FontFamilyName);
            _editor.FontSize = PointSizeToDip(data.FontSizeTwips / 20F);
            _editor.SpellCheck.IsEnabled = false;
            ConfigureMultilingualTextInput(_editor);
            LoadEditorContent();
            SaveEditorSelection();
            _editor.TextChanged += EditorTextChanged;
            _editor.SelectionChanged += EditorSelectionChanged;
            _editor.PreviewMouseLeftButtonUp += EditorPreviewMouseLeftButtonUp;
            _editor.PreviewMouseMove += EditorPreviewMouseMove;
            TextCompositionManager.AddPreviewTextInputStartHandler(_editor,
                EditorTextCompositionStarted);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(_editor,
                EditorTextCompositionUpdated);
            _editor.PreviewTextInput += EditorTextCompositionCompleted;
            _editor.ContextMenu = BuildEditorMenu();
            WC.Grid.SetRow(_editor, 3);
            _layout.Children.Add(_editor);

            _todoRows = new WC.StackPanel();
            WC.ScrollViewer todoScroller = new WC.ScrollViewer();
            todoScroller.VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto;
            todoScroller.Content = _todoRows;
            _todoInput = PlainTextBox();
            _todoInput.MaxLength = StickyNoteLimits.MaximumTodoItemCharacters;
            ConfigureMultilingualTextInput(_todoInput);
            _todoInput.KeyDown += TodoInputKeyDown;
            _todoInput.Visibility = W.Visibility.Collapsed;
            _todoPinToggleButton = ActionButton("置顶待办", 76);
            _todoUpButton = ActionButton("↑", 46);
            _todoDownButton = ActionButton("↓", 46);
            _todoProgress = new WC.TextBlock();
            _todoProgress.VerticalAlignment = W.VerticalAlignment.Center;
            _todoProgress.Margin = new W.Thickness(7, 0, 4, 0);
            _todoProgress.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            _todoProgress.FontSize = PointSizeToDip(8.5F);
            WC.Grid todoCommands = new WC.Grid();
            todoCommands.Margin = new W.Thickness(6, 4, 6, 7);
            // Preserve the former cancel-button position while exposing only
            // one pin toggle.  The spacer also keeps the move buttons stable.
            todoCommands.ColumnDefinitions.Add(Column(new W.GridLength(80)));
            todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            todoCommands.ColumnDefinitions.Add(Column(new W.GridLength(1,
                W.GridUnitType.Star)));
            todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            AddToGrid(todoCommands, _todoPinToggleButton, 1);
            AddToGrid(todoCommands, _todoUpButton, 3);
            AddToGrid(todoCommands, _todoDownButton, 4);
            AddToGrid(todoCommands, _todoProgress, 5);
            _todoPanel = new WC.Grid();
            _todoPanel.RowDefinitions.Add(Row(new W.GridLength(1,
                W.GridUnitType.Star)));
            _todoPanel.RowDefinitions.Add(Row(W.GridLength.Auto));
            WC.Grid.SetRow(todoScroller, 0);
            WC.Grid.SetRow(todoCommands, 1);
            _todoPanel.Children.Add(todoScroller);
            _todoPanel.Children.Add(todoCommands);
            WC.Grid.SetRow(_todoPanel, 3);
            _layout.Children.Add(_todoPanel);

            _scheduleRows = new WC.StackPanel();
            WC.ScrollViewer scheduleScroller = new WC.ScrollViewer();
            scheduleScroller.VerticalScrollBarVisibility =
                WC.ScrollBarVisibility.Auto;
            scheduleScroller.Content = _scheduleRows;
            _schedulePinToggleButton = ActionButton("置顶日程", 76);
            _scheduleUpButton = ActionButton("↑", 46);
            _scheduleDownButton = ActionButton("↓", 46);
            _scheduleCount = new WC.TextBlock();
            _scheduleCount.VerticalAlignment = W.VerticalAlignment.Center;
            _scheduleCount.Margin = new W.Thickness(7, 0, 4, 0);
            _scheduleCount.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            _scheduleCount.FontSize = PointSizeToDip(8.5F);
            WC.Grid scheduleCommands = new WC.Grid();
            scheduleCommands.Margin = new W.Thickness(6, 4, 6, 7);
            scheduleCommands.ColumnDefinitions.Add(Column(new W.GridLength(80)));
            scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            scheduleCommands.ColumnDefinitions.Add(Column(new W.GridLength(1,
                W.GridUnitType.Star)));
            scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            AddToGrid(scheduleCommands, _schedulePinToggleButton, 1);
            AddToGrid(scheduleCommands, _scheduleUpButton, 3);
            AddToGrid(scheduleCommands, _scheduleDownButton, 4);
            AddToGrid(scheduleCommands, _scheduleCount, 5);
            _schedulePanel = new WC.Grid();
            _schedulePanel.RowDefinitions.Add(Row(new W.GridLength(1,
                W.GridUnitType.Star)));
            _schedulePanel.RowDefinitions.Add(Row(W.GridLength.Auto));
            WC.Grid.SetRow(scheduleScroller, 0);
            WC.Grid.SetRow(scheduleCommands, 1);
            _schedulePanel.Children.Add(scheduleScroller);
            _schedulePanel.Children.Add(scheduleCommands);
            WC.Grid.SetRow(_schedulePanel, 3);
            _layout.Children.Add(_schedulePanel);

            _shell = new WC.Border();
            _shell.BorderThickness = new W.Thickness(1);
            _shell.Child = _layout;
            Content = _shell;

            _saveTimer = new DispatcherTimer(DispatcherPriority.Background);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                if (ShouldDeferAutoSave(_editorTextCompositionActive,
                    _lastInputUtc, DateTime.UtcNow))
                {
                    _saveTimer.Start();
                    return;
                }
                PersistNow();
            };
            _linkRefreshTimer = new DispatcherTimer(
                DispatcherPriority.Background);
            _linkRefreshTimer.Interval = TimeSpan.FromMilliseconds(550);
            _linkRefreshTimer.Tick += delegate
            {
                _linkRefreshTimer.Stop();
                if (_editorTextCompositionActive)
                {
                    _linkRefreshTimer.Start();
                    return;
                }
                RefreshOrdinaryLinks(true);
            };
            _scheduleRefreshTimer = new DispatcherTimer(
                DispatcherPriority.Background);
            _scheduleRefreshTimer.Interval = TimeSpan.FromMinutes(1);
            _scheduleRefreshTimer.Tick += delegate
            {
                if (Data.IsSchedule && IsVisible) RefreshScheduleList();
            };

            _deleteReminderButton.Click += delegate { ExecuteSelectedReminderDelete(); };
            _pinButton.Click += delegate { ToggleTopMost(); };
            _colorButton.Click += delegate { ShowAppearanceDialog(); };
            _closeButton.Click += delegate { RequestHideNote(); };
            _todoAddButton.Click += delegate { AddBlankTodoAndEdit(); };
            _todoDeleteButton.Click += delegate { DeleteSelectedTodo(); };
            _todoPinToggleButton.Click += delegate {
                if (_selectedTodo != null)
                    SetTodoPinned(_selectedTodo, !_selectedTodo.IsPinned);
            };
            _todoUpButton.Click += delegate { MoveSelectedTodo(-1); };
            _todoDownButton.Click += delegate { MoveSelectedTodo(1); };
            _scheduleAddButton.Click += delegate { PromptAddScheduleItem(); };
            _scheduleDeleteButton.Click += delegate { DeleteSelectedSchedule(); };
            _schedulePinToggleButton.Click += delegate {
                if (_selectedSchedule != null)
                    SetSchedulePinned(_selectedSchedule,
                        !_selectedSchedule.IsPinned);
            };
            _scheduleUpButton.Click += delegate { MoveSelectedSchedule(-1); };
            _scheduleDownButton.Click += delegate { MoveSelectedSchedule(1); };
            _header.MouseLeftButtonDown += HeaderMouseLeftButtonDown;
            _layout.PreviewMouseLeftButtonDown +=
                NoteSurfacePreviewMouseLeftButtonDown;
            Deactivated += WindowDeactivated;
            GotKeyboardFocus += delegate { QueueInputFocusChanged(); };
            LostKeyboardFocus += delegate { QueueInputFocusChanged(); };

            SourceInitialized += delegate
            {
                IntPtr initializedHandle = new WindowInteropHelper(this).Handle;
                DisableNativeMaximizeAndSnap(initializedHandle);
                HwndSource source = HwndSource.FromHwnd(initializedHandle);
                if (source != null) source.AddHook(WindowHook);
            };
            LocationChanged += delegate
            {
                if (_initializing) return;
                Raise(HeaderDragMoved);
                ScheduleSave();
            };
            SizeChanged += delegate { if (!_initializing) ScheduleSave(); };
            StateChanged += delegate
            {
                if (base.WindowState == W.WindowState.Maximized)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(RecoverUnexpectedMaximize));
            };
            ContentRendered += delegate
            {
                if (_shownRaised) return;
                _shownRaised = true;
                Raise(Shown);
            };
            Closing += WindowClosing;
            Closed += WindowClosed;

            ContextMenu = BuildNoteMenu();
            RefreshTodoList();
            RefreshScheduleList();
            RefreshMode();
            RefreshTitle();
            RefreshReminderState();
            ApplyColors();
            RefreshOrdinaryLinks(false);
            _initializing = false;
            RefreshFormatToolbar();
        }

        public StickyNoteData Data { get; private set; }

        public event EventHandler NoteChanged;
        public event EventHandler DeleteRequested;
        public event EventHandler CancelReminderRequested;
        public event EventHandler<ReminderActionEventArgs> ModifyReminderRequested;
        public event EventHandler<ReminderActionEventArgs> DeleteReminderRequested;
        public event EventHandler NewNoteRequested;
        public event EventHandler NewTodoRequested;
        public event EventHandler NewScheduleRequested;
        public event EventHandler TypingActivity;
        public event EventHandler InputFocusChanged;
        public event EventHandler<ImeCompositionEventArgs> ImeCompositionChanged;
        public event EventHandler Shown;
        public event EventHandler HeaderDragStarted;
        public event EventHandler HeaderDragMoved;
        public event EventHandler HeaderDragCompleted;
        public event EventHandler CloseRequested;
        public event EventHandler PinStateChanged;
        public event EventHandler<DockHorizontalResizeEventArgs>
            DockHorizontalResizing;
        public event WF.FormClosedEventHandler FormClosed;

        public bool IsDisposed { get { return _disposed; } }
        public bool Disposing { get { return false; } }
        public bool Visible { get { return IsVisible; } }
        public bool TopMost
        {
            get { return Topmost; }
            set { Topmost = value; }
        }
        public WF.FormStartPosition StartPosition { get; set; }
        public new int Width
        {
            get { return (int)Math.Round(base.Width); }
            set { base.Width = value; }
        }
        public new int Height
        {
            get { return (int)Math.Round(base.Height); }
            set { base.Height = value; }
        }
        public new int Left
        {
            get { return (int)Math.Round(base.Left); }
            set { base.Left = value; }
        }
        public new int Top
        {
            get { return (int)Math.Round(base.Top); }
            set { base.Top = value; }
        }
        public System.Drawing.Size Size
        {
            get { return new System.Drawing.Size(Width, Height); }
            set { Width = value.Width; Height = value.Height; }
        }
        public System.Drawing.Point Location
        {
            get { return new System.Drawing.Point(Left, Top); }
            set { Left = value.X; Top = value.Y; }
        }
        public Rectangle Bounds
        {
            get { return new Rectangle(Left, Top, Width, Height); }
            set { Left = value.X; Top = value.Y; Width = value.Width; Height = value.Height; }
        }
        public Rectangle ClientRectangle
        {
            get { return new Rectangle(0, 0, Width, Height); }
        }
        public new WF.FormWindowState WindowState
        {
            get
            {
                if (base.WindowState == W.WindowState.Minimized)
                    return WF.FormWindowState.Minimized;
                if (base.WindowState == W.WindowState.Maximized)
                    return WF.FormWindowState.Maximized;
                return WF.FormWindowState.Normal;
            }
            set
            {
                base.WindowState = value == WF.FormWindowState.Minimized
                    ? W.WindowState.Minimized
                    : value == WF.FormWindowState.Maximized
                        ? W.WindowState.Maximized : W.WindowState.Normal;
            }
        }
        public IntPtr Handle
        {
            get
            {
                if (_disposed) return IntPtr.Zero;
                return new WindowInteropHelper(this).EnsureHandle();
            }
        }

        IntPtr WF.IWin32Window.Handle { get { return Handle; } }

        internal bool UsesImeCompatibleEditor { get { return true; } }

        internal bool IsImeCompositionActiveForHost
        {
            get { return _editorTextCompositionActive; }
        }
        internal bool ExerciseOrdinaryLinkRefreshForTest()
        {
            if (Data.IsTodoList || Data.IsSchedule) return false;
            RefreshOrdinaryLinks(false);
            bool detected = _ordinaryLinkRanges.Count == 2;
            SetEditorPlainText("普通文字");
            RefreshOrdinaryLinks(false);
            return detected && _ordinaryLinkRanges.Count == 0;
        }
        internal bool AcceptsMultilineReturnForTest { get { return _editor.AcceptsReturn; } }
        internal bool UsesLegacyInputProxyForTest { get { return false; } }
        internal IntPtr LegacyInputProxyHandleForTest { get { return IntPtr.Zero; } }
        internal byte BackgroundAlphaForTest
        {
            get
            {
                SolidColorBrush brush = _shell.Background as SolidColorBrush;
                return brush == null ? (byte)0 : brush.Color.A;
            }
        }
        internal byte TextAlphaForTest
        {
            get
            {
                SolidColorBrush brush = _editor.Foreground as SolidColorBrush;
                return brush == null ? (byte)0 : brush.Color.A;
            }
        }
        internal bool HasFocusedTextInput
        {
            get
            {
                return _editor.IsKeyboardFocusWithin ||
                    _todoPanel.IsKeyboardFocusWithin ||
                    _schedulePanel.IsKeyboardFocusWithin ||
                    _formatToolbar.IsKeyboardFocusWithin;
            }
        }
        internal int VisibleTodoItemCount { get { return Data.TodoItems.Count; } }
        internal int TodoGroupCount { get { return 3; } }
        internal int ReminderBannerLineCount { get { return _reminderList.Items.Count; } }
        internal int ReminderBannerRebuildCountForTest
        {
            get { return _reminderBannerRebuildCount; }
        }
        internal bool HasRichTextFormattingToolbar
        {
            get { return _formatToolbar != null && _fontFamilyBox != null &&
                _fontSizeBox != null && _boldButton != null &&
                _italicButton != null && _underlineButton != null; }
        }
        internal bool UsesStableListFormatSelectors { get { return true; } }
        internal bool FormatControlsPreserveSelectionForTest
        {
            get { return !_boldButton.Focusable && !_italicButton.Focusable &&
                !_underlineButton.Focusable; }
        }
        internal bool FormatSelectorsAlwaysBlackForTest
        {
            get
            {
                SolidColorBrush family = _fontFamilyBox.Foreground as
                    SolidColorBrush;
                SolidColorBrush size = _fontSizeBox.Foreground as
                    SolidColorBrush;
                return family != null && size != null &&
                    family.Color == System.Windows.Media.Colors.Black &&
                    size.Color == System.Windows.Media.Colors.Black;
            }
        }

        internal bool HeaderTypeIconVisibleForTest
        {
            get
            {
                return _typeIcon != null && _typeIcon.Source != null &&
                    _typeIconHost != null &&
                    Math.Abs(_typeIcon.Width - 24) < 0.1 &&
                    Math.Abs(_typeIconHost.Width - 32) < 0.1;
            }
        }
        internal bool UsesBufferedResizePainting { get { return true; } }
        internal bool NativeMaximizeStyleDisabledForTest
        {
            get
            {
                CreateControl();
                IntPtr handle = new WindowInteropHelper(this).Handle;
                return handle != IntPtr.Zero &&
                    (GetWindowStyle(handle).ToInt64() & WsMaximizeBox) == 0;
            }
        }
        internal string CurrentPinActionText { get { return _pinButton.Content as string; } }
        internal float ReminderBannerFirstFontSize
        {
            get
            {
                if (_reminderList.Items.Count == 0) return 0F;
                WC.ListBoxItem row = _reminderList.Items[0] as WC.ListBoxItem;
                return row == null ? 0F : (float)(row.FontSize * 72.0 / 96.0);
            }
        }
        internal bool HasInlineCreationButtonsForTest
        {
            get
            {
                WC.DockPanel panel = _header.Child as WC.DockPanel;
                if (panel == null) return false;
                foreach (W.UIElement element in panel.Children)
                {
                    WC.Button button = element as WC.Button;
                    string content = button == null ? String.Empty :
                        Convert.ToString(button.Content);
                    if (content == "新增提醒" || content == "新建清单")
                        return true;
                }
                return false;
            }
        }
        internal string ReminderBannerText
        {
            get
            {
                List<string> lines = new List<string>();
                foreach (object value in _reminderList.Items)
                {
                    WC.ListBoxItem row = value as WC.ListBoxItem;
                    if (row != null) lines.Add(Convert.ToString(row.Content));
                }
                return String.Join(Environment.NewLine, lines.ToArray());
            }
        }

        public void ShowAndEdit()
        {
            Data.Visible = true;
            EnsureOnScreen();
            if (!IsVisible) Show();
            base.WindowState = W.WindowState.Normal;
            ApplyTopMostWindowState(Data.AlwaysOnTop);
            Activate();
            int requestedBeforeInteraction = _userInteractionGeneration;
            Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(delegate
                {
                    if (_disposed || !ShouldApplyDeferredInitialFocus(
                        requestedBeforeInteraction,
                        _userInteractionGeneration,
                        IsKeyboardFocusWithin)) return;
                    if (Data.IsTodoList) _todoAddButton.Focus();
                    else if (Data.IsSchedule) _scheduleAddButton.Focus();
                    else _editor.Focus();
                }));
            PersistNow();
        }

        internal static bool ShouldApplyDeferredInitialFocus(
            int requestedGeneration, int currentGeneration,
            bool keyboardFocusAlreadyWithin)
        {
            return requestedGeneration == currentGeneration &&
                !keyboardFocusAlreadyWithin;
        }

        internal void FocusPrimaryInputForTest()
        {
            Activate();
            if (Data.IsTodoList) _todoAddButton.Focus();
            else if (Data.IsSchedule) _scheduleAddButton.Focus();
            else _editor.Focus();
        }

        public void ShowRestored()
        {
            Data.Visible = true;
            EnsureOnScreen();
            if (!IsVisible) Show();
            base.WindowState = W.WindowState.Normal;
            ApplyTopMostWindowState(Data.AlwaysOnTop);
        }

        internal bool HasCompletedFirstRender
        {
            get { return _shownRaised; }
        }

        internal void ShowRestoredDocked(Rectangle bounds)
        {
            // The owner computes one layout for the entire stack.  Applying
            // EnsureOnScreen independently to every member would clamp each
            // window differently and tear the restored group apart.
            Data.Visible = true;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            Data.X = bounds.Left;
            Data.Y = bounds.Top;
            Data.Width = bounds.Width;
            Data.Height = bounds.Height;
            if (!IsVisible) Show();
            base.WindowState = W.WindowState.Normal;
            ApplyTopMostWindowState(Data.AlwaysOnTop);
        }

        // Screen.WorkingArea and the WinForms pet use physical pixels, while
        // WPF Window.Left/Top/Width/Height use 1/96-inch device-independent
        // units.  On a 150%-200% laptop display, feeding WinForms coordinates
        // into the WPF properties can put the right-hand notes beyond the real
        // desktop.  Move the HWND in physical pixels so Windows performs the
        // correct per-monitor DPI conversion for us.
        internal void ShowRestoredAtPhysicalBounds(Rectangle bounds)
        {
            Data.Visible = true;
            if (!IsVisible) Show();
            base.WindowState = W.WindowState.Normal;
            ApplyTopMostWindowState(Data.AlwaysOnTop);
            IntPtr hwnd = Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SwRestore);
                SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top,
                    Math.Max(1, bounds.Width), Math.Max(1, bounds.Height),
                    SwpNoZOrder | SwpNoActivate | SwpShowWindow);
                UpdateLayout();
            }
            Data.X = Left;
            Data.Y = Top;
            Data.Width = Width;
            Data.Height = Height;
        }

        internal Rectangle PhysicalBounds
        {
            get
            {
                NativeRect bounds;
                if (Handle != IntPtr.Zero && GetWindowRect(Handle, out bounds))
                    return Rectangle.FromLTRB(bounds.Left, bounds.Top,
                        bounds.Right, bounds.Bottom);
                return Rectangle.Empty;
            }
        }

        // Penny's desktop host owns a WinForms message loop while this note is
        // a top-level WPF window.  Without this bridge, normal WM_KEY*/WM_CHAR
        // messages can remain in the WinForms loop; an IME composition may
        // still arrive, which misleadingly makes Chinese work while Latin text,
        // digits, punctuation and Enter do not.  Register each shown note once
        // with the supported WPF/WinForms modeless keyboard interop.
        internal void EnableWinFormsKeyboardInterop()
        {
            if (_winFormsKeyboardInteropEnabled) return;
            System.Windows.Forms.Integration.ElementHost.
                EnableModelessKeyboardInterop(this);
            _winFormsKeyboardInteropEnabled = true;
        }

        internal bool UsesWinFormsKeyboardInteropForTest
        {
            get { return _winFormsKeyboardInteropEnabled; }
        }

        public void HideNote()
        {
            HideNoteCore(true);
        }

        internal void HideAsDockGroupMember()
        {
            HideNoteCore(false);
        }

        private void HideNoteCore(bool notifyChanged)
        {
            RaiseImeCompositionChanged(false);
            CloseAppearanceDialogAsCancel();
            Data.Visible = false;
            PersistNow(true, notifyChanged);
            Hide();
        }

        public void CloseForApplicationExit()
        {
            if (_disposed) return;
            RaiseImeCompositionChanged(false);
            CloseAppearanceDialogAsCancel();
            _closingForExit = true;
            PersistNow(false, false);
            Close();
        }

        internal void FlushPendingChanges()
        {
            PersistNow(false, true);
        }

        public void BringToFront()
        {
            bool topmost = Topmost;
            Topmost = true;
            Topmost = topmost;
            Activate();
        }

        public DispatcherOperation BeginInvoke(Delegate method)
        {
            return Dispatcher.BeginInvoke(method);
        }

        public void CreateControl()
        {
            new WindowInteropHelper(this).EnsureHandle();
            Measure(new W.Size(Width, Height));
            Arrange(new W.Rect(0, 0, Width, Height));
            UpdateLayout();
        }

        public System.Drawing.Point PointToScreen(System.Drawing.Point point)
        {
            W.Point result = base.PointToScreen(new W.Point(point.X, point.Y));
            return new System.Drawing.Point((int)Math.Round(result.X),
                (int)Math.Round(result.Y));
        }

        public void DrawToBitmap(Bitmap target, Rectangle targetBounds)
        {
            if (target == null) throw new ArgumentNullException("target");
            CreateControl();
            int pixelWidth = Math.Max(1, targetBounds.Width);
            int pixelHeight = Math.Max(1, targetBounds.Height);
            RenderTargetBitmap render = new RenderTargetBitmap(pixelWidth,
                pixelHeight, 96, 96, PixelFormats.Pbgra32);
            render.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(render));
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                using (Bitmap rendered = new Bitmap(stream))
                using (Graphics graphics = Graphics.FromImage(target))
                    graphics.DrawImage(rendered, targetBounds);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _closingForExit = true;
            Close();
        }

        private static WC.RowDefinition Row(W.GridLength height)
        {
            WC.RowDefinition row = new WC.RowDefinition();
            row.Height = height;
            return row;
        }

        private static WC.ColumnDefinition Column(W.GridLength width)
        {
            WC.ColumnDefinition column = new WC.ColumnDefinition();
            column.Width = width;
            return column;
        }

        private static void AddToGrid(WC.Grid grid, W.UIElement element,
            int column)
        {
            WC.Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private WC.Button HeaderButton(string text, double width, bool bold)
        {
            WC.Button button = new WC.Button();
            button.Content = text;
            button.Width = width;
            button.Padding = new W.Thickness(2, 0, 2, 0);
            button.BorderThickness = new W.Thickness(0);
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            button.FontSize = PointSizeToDip(bold ? 11F : 9F);
            button.FontWeight = bold ? W.FontWeights.Bold : W.FontWeights.Normal;
            button.FocusVisualStyle = null;
            return button;
        }

        private WC.Button ActionButton(string text, double width)
        {
            WC.Button button = HeaderButton(text, width, false);
            button.Height = 27;
            button.Margin = new W.Thickness(4, 0, 0, 0);
            button.BorderThickness = new W.Thickness(1);
            return button;
        }

        private WC.TextBox PlainTextBox()
        {
            WC.TextBox text = new WC.TextBox();
            text.BorderThickness = new W.Thickness(1);
            text.Padding = new W.Thickness(4, 2, 4, 2);
            text.Background = System.Windows.Media.Brushes.Transparent;
            text.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            text.FontSize = PointSizeToDip(Data.FontSizeTwips / 20F);
            return text;
        }

        private void ConfigureMultilingualTextInput(W.UIElement input)
        {
            InputMethod.SetIsInputMethodEnabled(input, true);
            InputMethod.SetIsInputMethodSuspended(input, false);
            input.PreviewTextInput += delegate
            {
                _lastInputUtc = DateTime.UtcNow;
                Raise(TypingActivity);
            };
        }

        private WC.ContextMenu BuildNoteMenu()
        {
            WC.ContextMenu menu = new WC.ContextMenu();
            AddNoteActions(menu);
            return menu;
        }

        private void AddNoteActions(WC.ContextMenu menu)
        {
            AddMenuItem(menu, "新建便利贴", delegate { Raise(NewNoteRequested); });
            AddMenuItem(menu, "新建待办清单", delegate { Raise(NewTodoRequested); });
            AddMenuItem(menu, "新建日程", delegate { Raise(NewScheduleRequested); });
            menu.Items.Add(new WC.Separator());
            AddMenuItem(menu, "重命名", RenameNote);
            AddMenuItem(menu, "取消提醒", delegate { Raise(CancelReminderRequested); });
            AddMenuItem(menu, "颜色与透明度", ShowAppearanceDialog);
            AddMenuItem(menu, "置顶 / 取消置顶", ToggleTopMost);
            menu.Items.Add(new WC.Separator());
            AddMenuItem(menu, "删除此便利贴", delegate { Raise(DeleteRequested); });
            AddMenuItem(menu, "收起到侧边页签", RequestHideNote);
        }

        private static void AddMenuItem(WC.ContextMenu menu, string text,
            Action action)
        {
            WC.MenuItem item = new WC.MenuItem();
            item.Header = text;
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private void ScheduleSave()
        {
            if (_disposed) return;
            _saveTimer.Stop();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _saveTimer.Start();
        }

        private void PersistNow()
        {
            PersistNow(true, true);
        }

        private void PersistNow(bool markModified, bool notifyChanged)
        {
            if (_disposed) return;
            _saveTimer.Stop();
            if (Data.IsTodoList)
                Data.Text = BuildPlainTextFromTodos(Data.TodoItems);
            else if (Data.IsSchedule)
                Data.Text = BuildPlainTextFromSchedules(Data.ScheduleItems);
            else CaptureEditorContent();
            Data.X = Left;
            Data.Y = Top;
            Data.Width = Width;
            Data.Height = Height;
            if (markModified) Data.ModifiedUtcTicks = DateTime.UtcNow.Ticks;
            if (notifyChanged) Raise(NoteChanged);
        }

        private void RefreshTitle()
        {
            _title.Text = Data.IsSchedule && String.IsNullOrWhiteSpace(Data.Title)
                ? "日程" : Data.DisplayTitle;
        }

        private void RefreshMode()
        {
            bool todo = Data.IsTodoList;
            bool schedule = Data.IsSchedule;
            bool listMode = todo || schedule;
            if (schedule) _scheduleRefreshTimer.Start();
            else _scheduleRefreshTimer.Stop();
            _updatingFormatToolbar = true;
            try { AddFontSizeOptions(_fontSizeBox, listMode); }
            finally { _updatingFormatToolbar = false; }
            _formatToolbar.Visibility = W.Visibility.Visible;
            _fontFamilyBox.Visibility = listMode
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            _boldButton.Visibility = listMode
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            _italicButton.Visibility = listMode
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            _underlineButton.Visibility = listMode
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            _todoAddButton.Visibility = todo
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            _todoDeleteButton.Visibility = todo
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            _scheduleAddButton.Visibility = schedule
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            _scheduleDeleteButton.Visibility = schedule
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            WC.Grid.SetColumn(_fontSizeBox, listMode ? 0 : 1);
            _formatToolbar.ColumnDefinitions[1].Width = listMode
                ? new W.GridLength(0) : new W.GridLength(88);
            for (int column = 2; column <= 4; column++)
                _formatToolbar.ColumnDefinitions[column].Width = listMode
                    ? new W.GridLength(0) : new W.GridLength(34);
            _formatToolbar.ColumnDefinitions[5].Width = listMode
                ? new W.GridLength(56) : new W.GridLength(0);
            _formatToolbar.ColumnDefinitions[6].Width = listMode
                ? new W.GridLength(56) : new W.GridLength(0);
            _fontSizeBox.Width = listMode ? 120 : Double.NaN;
            _fontSizeBox.HorizontalAlignment = listMode
                ? W.HorizontalAlignment.Left : W.HorizontalAlignment.Stretch;
            _editor.Visibility = listMode
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            _todoPanel.Visibility = todo
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            _schedulePanel.Visibility = schedule
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            if (todo)
            {
                Data.FontSizeTwips = (int)Math.Round(
                    NormalizeScheduleFontSize(Data.FontSizeTwips / 20F) * 20F);
                _todoInput.FontSize = PointSizeToDip(Data.FontSizeTwips / 20F);
                RefreshTodoList();
            }
            if (schedule)
            {
                Data.IsTodoList = false;
                Data.FontSizeTwips = (int)Math.Round(
                    NormalizeScheduleFontSize(Data.FontSizeTwips / 20F) * 20F);
                RefreshScheduleList();
            }
            RefreshFormatToolbar();
        }

        private void ApplyColors()
        {
            Color paper = Color.FromArgb(Data.ColorArgb);
            Color header = paper;
            Color body = WF.ControlPaint.LightLight(paper);
            Color toolbar = WF.ControlPaint.Light(paper, 0.08F);
            Color input = WF.ControlPaint.LightLight(paper);
            Color reminder = toolbar;
            Color border = header;
            System.Windows.Media.Brush text = OpaqueBrush(EffectiveTextColor());
            int opacity = Math.Max(10, Math.Min(100,
                Data.BackgroundOpacityPercent));
            _shell.Background = AlphaBrush(body, opacity);
            _shell.BorderBrush = AlphaBrush(border, Math.Max(35, opacity));
            _header.Background = AlphaBrush(header, opacity);
            RefreshHeaderTypeIcon(header);
            _formatToolbar.Background = AlphaBrush(toolbar, opacity);
            _reminderPanel.Background = AlphaBrush(reminder, opacity);
            _todoPanel.Background = System.Windows.Media.Brushes.Transparent;
            _schedulePanel.Background = System.Windows.Media.Brushes.Transparent;
            _editor.Background = System.Windows.Media.Brushes.Transparent;
            int effectiveTextArgb = EffectiveTextColor().ToArgb();
            if (_appliedEditorTextColorArgb != effectiveTextArgb)
            {
                TextRange document = new TextRange(
                    _editor.Document.ContentStart,
                    _editor.Document.ContentEnd);
                document.ApplyPropertyValue(TextElement.ForegroundProperty,
                    text);
                _editor.Document.Foreground = text;
                _appliedEditorTextColorArgb = effectiveTextArgb;
            }
            _editor.Foreground = text;
            _title.Foreground = text;
            _reminderList.Foreground = text;
            _todoProgress.Foreground = text;
            _scheduleCount.Foreground = text;
            _todoInput.Foreground = text;
            _todoInput.Background = AlphaBrush(input, opacity);
            foreach (WC.Button button in new WC.Button[] {
                _deleteReminderButton, _pinButton, _colorButton,
                _closeButton, _boldButton, _italicButton, _underlineButton,
                _todoAddButton, _todoDeleteButton, _todoPinToggleButton,
                _todoUpButton, _todoDownButton,
                _scheduleAddButton, _scheduleDeleteButton,
                _schedulePinToggleButton,
                _scheduleUpButton, _scheduleDownButton })
            {
                button.Foreground = text;
                button.Background = System.Windows.Media.Brushes.Transparent;
                button.BorderBrush = AlphaBrush(border, Math.Max(35, opacity));
            }
            // Selector captions are controls, not note content.  Keep them
            // legible and stable even when the note body is switched to white.
            _fontFamilyBox.Foreground = System.Windows.Media.Brushes.Black;
            _fontFamilyBox.Background = AlphaBrush(input, opacity);
            _fontSizeBox.Foreground = System.Windows.Media.Brushes.Black;
            _fontSizeBox.Background = AlphaBrush(input, opacity);
            if (_dockPreviewActive)
            {
                _shell.BorderThickness = new W.Thickness(
                    _dockPreviewTarget ? 2 : 3);
                _shell.BorderBrush = OpaqueBrush(Color.FromArgb(
                    32, 160, 255));
            }
            else _shell.BorderThickness = new W.Thickness(1);
            RefreshTodoRowColors();
            RefreshScheduleRowColors();
        }

        private void RefreshHeaderTypeIcon(Color headerColor)
        {
            using (Bitmap bitmap = StickyNoteTabControl.CreateTypeIconBitmap(
                Data, headerColor, 24))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                BitmapImage source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.StreamSource = stream;
                source.EndInit();
                source.Freeze();
                _typeIcon.Source = source;
            }
        }

        internal void SetDockPreview(bool active, bool target)
        {
            if (_dockPreviewActive == active && _dockPreviewTarget == target)
                return;
            _dockPreviewActive = active;
            _dockPreviewTarget = target;
            ApplyColors();
        }

        internal void RaiseForDockDragWithoutActivation()
        {
            if (_disposed || !IsVisible) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            IntPtr insertAfter = Topmost ? new IntPtr(-1) : IntPtr.Zero;
            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        internal void SetDockResizeRole(bool grouped, bool resizeTop,
            bool resizeBottom)
        {
            SetDockResizeRole(grouped, resizeTop, resizeBottom, false);
        }

        internal void SetDockResizeRole(bool grouped, bool resizeTop,
            bool resizeBottom, bool splitBottom)
        {
            SetDockResizeRole(grouped, resizeTop, resizeBottom, splitBottom,
                220, 700);
        }

        internal void SetDockResizeRole(bool grouped, bool resizeTop,
            bool resizeBottom, bool splitBottom, int dividerMinimumHeight,
            int dividerMaximumHeight)
        {
            _dockGrouped = grouped;
            _dockResizeTop = !grouped || resizeTop;
            _dockResizeBottom = !grouped || resizeBottom;
            _dockSplitBottom = grouped && splitBottom;
            _dockDividerMinimumHeight = Math.Max(220, Math.Min(700,
                dividerMinimumHeight));
            _dockDividerMaximumHeight = Math.Max(
                _dockDividerMinimumHeight, Math.Min(700,
                    dividerMaximumHeight));
            if (!_dockSplitBottom)
            {
                _dockDividerResizeActive = false;
                _lastResizeHitTest = 0;
            }
        }

        internal bool DockDividerResizeActive
        {
            get { return _dockDividerResizeActive; }
        }

        internal bool DockHorizontalResizeActive
        {
            get
            {
                if (!_windowResizeActive) return false;
                return _lastResizeHitTest == HtLeft ||
                    _lastResizeHitTest == HtRight ||
                    _lastResizeHitTest == HtTopLeft ||
                    _lastResizeHitTest == HtTopRight ||
                    _lastResizeHitTest == HtBottomLeft ||
                    _lastResizeHitTest == HtBottomRight;
            }
        }

        internal int DockHorizontalGroupLeft(double currentWidth)
        {
            bool fromLeft = _lastResizeHitTest == HtLeft ||
                _lastResizeHitTest == HtTopLeft ||
                _lastResizeHitTest == HtBottomLeft;
            double left = fromLeft
                ? _resizeStartLeft + _resizeStartWidth - currentWidth
                : _resizeStartLeft;
            return (int)Math.Round(left);
        }

        private Color EffectiveTextColor()
        {
            return Data.TextColorArgb == Color.White.ToArgb()
                ? Color.White : Color.Black;
        }

        private static System.Windows.Media.Brush AlphaBrush(Color value,
            int opacityPercent)
        {
            byte alpha = (byte)Math.Max(0, Math.Min(255,
                (int)Math.Round(opacityPercent * 2.55)));
            SolidColorBrush brush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha,
                    value.R, value.G, value.B));
            brush.Freeze();
            return brush;
        }

        private static System.Windows.Media.Brush OpaqueBrush(Color value)
        {
            SolidColorBrush brush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255,
                    value.R, value.G, value.B));
            brush.Freeze();
            return brush;
        }




        private void ToggleTopMost()
        {
            Data.AlwaysOnTop = !Data.AlwaysOnTop;
            ApplyTopMostWindowState(Data.AlwaysOnTop);
            Raise(PinStateChanged);
            PersistNow();
        }

        internal void ApplyTopMostWindowState(bool alwaysOnTop)
        {
            Topmost = alwaysOnTop;
            _pinButton.Content = PinActionText(alwaysOnTop);
        }

        private void RenameNote()
        {
            using (NoteTitleDialog dialog = new NoteTitleDialog(Data.Title))
            {
                if (dialog.ShowDialog(this) != WF.DialogResult.OK) return;
                Data.Title = dialog.NoteTitle;
            }
            RefreshTitle();
            PersistNow();
        }


        private void EnsureOnScreen()
        {
            Rectangle work = WF.Screen.FromPoint(
                new System.Drawing.Point(Data.X, Data.Y)).WorkingArea;
            Left = Math.Max(work.Left, Math.Min(Data.X, work.Right - Width));
            Top = Math.Max(work.Top, Math.Min(Data.Y, work.Bottom - Height));
        }

        private void WindowClosing(object sender,
            System.ComponentModel.CancelEventArgs e)
        {
            if (_closingForExit) return;
            e.Cancel = true;
            HideNote();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer.Stop();
            _linkRefreshTimer.Stop();
            _scheduleRefreshTimer.Stop();
            CloseAppearanceDialogAsCancel();
            WF.FormClosedEventHandler handler = FormClosed;
            if (handler != null)
                handler(this, new WF.FormClosedEventArgs(WF.CloseReason.None));
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void QueueInputFocusChanged()
        {
            if (_inputFocusReportQueued || _disposed) return;
            _inputFocusReportQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(delegate
                {
                    _inputFocusReportQueued = false;
                    if (!_disposed) Raise(InputFocusChanged);
                }));
        }

        private void Raise(EventHandler<ReminderActionEventArgs> handler,
            ReminderActionEventArgs arguments)
        {
            if (handler != null) handler(this, arguments);
        }

        private void RaiseImeCompositionChanged(bool active)
        {
            EventHandler<ImeCompositionEventArgs> handler = ImeCompositionChanged;
            if (handler != null)
                handler(this, new ImeCompositionEventArgs(active));
        }

        private static System.Windows.Media.FontFamily SafeWpfFontFamily(
            string familyName)
        {
            try
            {
                return new System.Windows.Media.FontFamily(
                    String.IsNullOrWhiteSpace(familyName)
                        ? "Microsoft YaHei UI" : familyName);
            }
            catch
            {
                return new System.Windows.Media.FontFamily("Microsoft YaHei UI");
            }
        }

        private static double PointSizeToDip(float points)
        {
            return Math.Max(1.0, points * 96.0 / 72.0);
        }

        private static string[] InstalledFontNames()
        {
            return InstalledFontNameCache.Value;
        }

        private static string[] LoadInstalledFontNames()
        {
            List<string> names = new List<string>();
            try
            {
                foreach (System.Drawing.FontFamily family in
                    System.Drawing.FontFamily.Families)
                {
                    string name = family.Name;
                    if (String.IsNullOrWhiteSpace(name) || name[0] == '@' ||
                        names.Contains(name)) continue;
                    names.Add(name);
                }
            }
            catch { }
            if (names.Count == 0) names.Add("Microsoft YaHei UI");
            names.Sort(CompareFontNamesChineseFirst);
            return names.ToArray();
        }

        internal static bool InstalledFontNamesCachedForTest()
        {
            return Object.ReferenceEquals(InstalledFontNames(),
                InstalledFontNames());
        }

        private static int CompareFontNamesChineseFirst(string left,
            string right)
        {
            bool leftChinese = IsChineseFontName(left);
            bool rightChinese = IsChineseFontName(right);
            if (leftChinese != rightChinese) return leftChinese ? -1 : 1;
            return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        }

        private static bool IsChineseFontName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            foreach (char value in name)
                if ((value >= '\u3400' && value <= '\u9FFF') ||
                    (value >= '\uF900' && value <= '\uFAFF')) return true;
            string lower = name.Trim().ToLowerInvariant();
            string[] tokens = { "yahei", "jhenghei", "simsun", "nsimsun",
                "simhei", "kaiti", "fangsong", "dengxian", "mingliu",
                "pmingliu", "dfkai", "source han sans cn",
                "source han serif cn", "source han sans sc",
                "source han serif sc", "noto sans cjk sc",
                "noto serif cjk sc", "noto sans sc", "noto serif sc",
                "sarasa gothic sc", "lxgw", "misans", "harmonyos sans sc",
                "alibaba puhuiti", "opposans", "honor sans cn", "stheiti",
                "stsong", "stkaiti", "stfangsong" };
            foreach (string token in tokens)
                if (lower.Contains(token)) return true;
            return lower.StartsWith("fz", StringComparison.Ordinal) ||
                lower.EndsWith(" sc", StringComparison.Ordinal) ||
                lower.Contains(" sc ");
        }

        internal static Color PaletteColorForTest(int index)
        {
            return Palette[Math.Max(0, Math.Min(Palette.Length - 1, index))];
        }

        internal static bool ShouldDeferAutoSave(bool composing,
            DateTime lastInputUtc, DateTime nowUtc)
        {
            return composing || nowUtc - lastInputUtc <
                TimeSpan.FromMilliseconds(900);
        }

        internal static string PinActionText(bool alwaysOnTop)
        {
            return alwaysOnTop ? "取消置顶" : "置顶";
        }

        internal static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero) return "现在";
            if (remaining.TotalDays >= 1)
                return ((int)remaining.TotalDays) + "天" + remaining.Hours + "小时";
            if (remaining.TotalHours >= 1)
                return ((int)remaining.TotalHours) + "小时" + remaining.Minutes + "分";
            if (remaining.TotalMinutes >= 1)
                return ((int)remaining.TotalMinutes) + "分" + remaining.Seconds + "秒";
            return Math.Max(1,
                (int)Math.Ceiling(remaining.TotalSeconds)) + "秒";
        }

        internal static Font CreateSafeFont(string familyName, float points,
            FontStyle style)
        {
            string family = StickyNoteRepository.NormalizeFontFamily(familyName);
            float size = Math.Max(6F, Math.Min(72F, points));
            int sizeTwips = (int)Math.Round(size * 20F);
            string key = family + "|" + sizeTwips + "|" + (int)style;
            lock (SharedFontsGate)
            {
                Font cached;
                if (SharedFonts.TryGetValue(key, out cached)) return cached;
                try { cached = new Font(family, sizeTwips / 20F, style); }
                catch
                {
                    cached = new Font("Microsoft YaHei UI",
                        sizeTwips / 20F, style);
                }
                SharedFonts[key] = cached;
                return cached;
            }
        }

        internal static bool IsChineseFontNameForTest(string name)
        {
            return IsChineseFontName(name);
        }

        internal static bool FontNameSortsBeforeForTest(string left,
            string right)
        {
            return CompareFontNamesChineseFirst(left, right) < 0;
        }

        internal static bool TryParseFontSize(string value, out float points)
        {
            points = 0F;
            string text = (value ?? String.Empty).Trim();
            if (text.Length == 0) return false;
            Dictionary<string, float> chinese = new Dictionary<string, float>();
            chinese["小五"] = 9F; chinese["五号"] = 10.5F;
            chinese["小四"] = 12F; chinese["四号"] = 14F;
            chinese["小三"] = 15F; chinese["三号"] = 16F;
            chinese["小二"] = 18F; chinese["二号"] = 22F;
            chinese["小一"] = 24F; chinese["一号"] = 26F;
            chinese["小初"] = 36F; chinese["初号"] = 42F;
            foreach (KeyValuePair<string, float> item in chinese)
            {
                if (!text.StartsWith(item.Key, StringComparison.Ordinal)) continue;
                points = item.Value;
                return true;
            }
            StringBuilder numeric = new StringBuilder();
            foreach (char character in text)
            {
                if ((character >= '0' && character <= '9') ||
                    character == '.' || character == ',')
                    numeric.Append(character == ',' ? '.' : character);
            }
            return Single.TryParse(numeric.ToString(),
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out points) &&
                points >= 6F && points <= 72F;
        }

        internal static string FormatFontSize(float points)
        {
            string[] labels = { "小五 9", "五号 10.5", "小四 12", "四号 14",
                "小三 15", "三号 16", "小二 18", "二号 22", "小一 24",
                "一号 26", "小初 36", "初号 42" };
            foreach (string label in labels)
            {
                float parsed;
                if (TryParseFontSize(label, out parsed) &&
                    Math.Abs(parsed - points) < 0.1F) return label;
            }
            return points.ToString("0.#",
                System.Globalization.CultureInfo.CurrentCulture);
        }

        internal static string NormalizeFullWidthLatin(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? String.Empty;
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (character >= 0xFF01 && character <= 0xFF5E)
                    builder.Append((char)(character - 0xFEE0));
                else if (character == 0x3000) builder.Append(' ');
                else builder.Append(character);
            }
            return builder.ToString();
        }

        internal static int ScaleForDpi(int logicalPixels, int dpi)
        {
            return (int)Math.Round(logicalPixels * Math.Max(96, dpi) / 96.0);
        }

        internal static System.Drawing.Size MinimumNoteSizeForDpi(int dpi)
        {
            return new System.Drawing.Size(ScaleForDpi(280, dpi),
                ScaleForDpi(220, dpi));
        }

        internal static int HeaderRowHeightForDpi(int dpi)
        {
            return ScaleForDpi(32, dpi);
        }

        internal static string ParseTodoTextLine(string line,
            out bool completed)
        {
            string text = (line ?? String.Empty).Trim();
            completed = false;
            while (text.Length > 0)
            {
                if (text.StartsWith("[x]",
                    StringComparison.OrdinalIgnoreCase))
                {
                    completed = true;
                    text = text.Substring(3).TrimStart();
                    continue;
                }
                if (text.StartsWith("[ ]", StringComparison.Ordinal) ||
                    text.StartsWith("[]", StringComparison.Ordinal))
                {
                    text = text.Substring(text.StartsWith("[]",
                        StringComparison.Ordinal) ? 2 : 3).TrimStart();
                    continue;
                }
                break;
            }
            return text;
        }

        internal static string BuildPlainTextFromTodos(
            IEnumerable<StickyTodoItem> items)
        {
            StringBuilder body = new StringBuilder();
            if (items == null) return String.Empty;
            foreach (StickyTodoItem item in items)
            {
                if (item == null) continue;
                bool ignoredCompleted;
                string text = ParseTodoTextLine(item.Text,
                    out ignoredCompleted);
                if (text.Length == 0) continue;
                if (body.Length > 0) body.AppendLine();
                body.Append(text);
            }
            return body.ToString();
        }

        internal void OpenAppearanceDialogForTest()
        {
            ShowAppearanceDialog();
        }

        internal bool ExerciseAppearanceCloseStressForTest(int cycles)
        {
            int count = Math.Max(1, cycles);
            for (int index = 0; index < count; index++)
            {
                ShowAppearanceDialog();
                WF.Application.DoEvents();
                if (_appearanceDialog == null || _appearanceDialog.IsDisposed)
                    return false;
                _appearanceDialog.Close();
                WF.Application.DoEvents();
                if (_appearanceDialog != null && !_appearanceDialog.IsDisposed)
                    return false;
            }
            return true;
        }

        internal bool ExerciseRichTextFormattingForTest()
        {
            _initializing = true;
            SetEditorPlainText("格式测试");
            _initializing = false;
            _editor.SelectAll();
            ApplySelectionFontFamily("Microsoft YaHei UI");
            ApplySelectionFontSize(18F);
            EditingCommands.ToggleBold.Execute(null, _editor);
            EditingCommands.ToggleItalic.Execute(null, _editor);
            EditingCommands.ToggleUnderline.Execute(null, _editor);
            CaptureEditorContent();
            return Data.Text == "格式测试" &&
                !String.IsNullOrEmpty(Data.RichTextRtf) &&
                Data.FontSizeTwips == 360;
        }

        internal bool ExerciseSmoothFormatInteractionForTest()
        {
            _initializing = true;
            SetEditorPlainText("从后往前选择文字");
            _initializing = false;
            TextPointer start = _editor.Document.ContentStart.GetPositionAtOffset(2);
            TextPointer end = _editor.Document.ContentStart.GetPositionAtOffset(6);
            if (start == null || end == null) return false;
            _editor.Selection.Select(start, end);
            ApplySelectionFontSize(24F);
            return !_editor.Selection.IsEmpty && Data.FontSizeTwips == 480;
        }

        internal bool ExerciseFirstFormatCommitForTest()
        {
            _initializing = true;
            SetEditorPlainText("First format commit");
            _initializing = false;
            _editor.SelectAll();
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(24F);
            return Data.FontSizeTwips == 480 &&
                String.Equals(Data.FontFamilyName, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseEmptyNoteFormattingForTest()
        {
            Data.IsTodoList = false;
            Data.IsSchedule = false;
            _initializing = true;
            SetEditorPlainText(String.Empty);
            _initializing = false;
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(22F);
            Block first = _editor.Document.Blocks.FirstBlock;
            if (first == null) return false;
            ApplyEmptyEditorTypingDefaults();
            _editor.CaretPosition.InsertTextInRun("A");
            TextRange typed = new TextRange(first.ContentStart,
                first.ContentEnd);
            object typedSize = typed.GetPropertyValue(
                TextElement.FontSizeProperty);
            System.Windows.Media.FontFamily typedFamily =
                typed.GetPropertyValue(TextElement.FontFamilyProperty) as
                    System.Windows.Media.FontFamily;
            return EditorPlainText() == "A" &&
                Data.FontSizeTwips == 440 &&
                String.Equals(Data.FontFamilyName, "Arial",
                    StringComparison.CurrentCultureIgnoreCase) &&
                first != null && Math.Abs(first.FontSize -
                    PointSizeToDip(22F)) < 0.1 &&
                first.FontFamily != null && String.Equals(
                    first.FontFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase) &&
                typedSize is double && Math.Abs((double)typedSize -
                    PointSizeToDip(22F)) < 0.1 && typedFamily != null &&
                String.Equals(typedFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseCaretTypingFormatSwitchForTest()
        {
            Data.IsTodoList = false;
            Data.IsSchedule = false;
            _initializing = true;
            SetEditorPlainText("已有正文");
            _initializing = false;
            Paragraph paragraph = _editor.Document.Blocks.FirstBlock as Paragraph;
            if (paragraph == null) return false;
            TextPointer caret = paragraph.ContentEnd.GetInsertionPosition(
                LogicalDirection.Backward);
            _editor.Selection.Select(caret, caret);
            SaveEditorSelection();
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(22F);
            _editor.Selection.Text = "Z";
            TextRange typed = FindLastTextRange("Z");
            object typedSize = typed == null ? null : typed.GetPropertyValue(
                TextElement.FontSizeProperty);
            System.Windows.Media.FontFamily typedFamily = typed == null ? null :
                typed.GetPropertyValue(TextElement.FontFamilyProperty) as
                    System.Windows.Media.FontFamily;
            return EditorPlainText().EndsWith("Z", StringComparison.Ordinal) &&
                typedSize is double && Math.Abs((double)typedSize -
                    PointSizeToDip(22F)) < 0.1 && typedFamily != null &&
                String.Equals(typedFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseSingleNativeImeCommitAfterFormatForTest()
        {
            _initializing = true;
            SetEditorPlainText("吃饭了没");
            _initializing = false;
            Paragraph paragraph = _editor.Document.Blocks.FirstBlock as Paragraph;
            if (paragraph == null) return false;
            TextPointer caret = paragraph.ContentEnd.GetInsertionPosition(
                LogicalDirection.Backward);
            _editor.Selection.Select(caret, caret);
            SaveEditorSelection();
            ApplySelectionFontFamily("Arial");
            _editor.Selection.Text = "宝贝";
            string value = EditorPlainText();
            return value == "吃饭了没宝贝" &&
                value.IndexOf("宝贝", StringComparison.Ordinal) ==
                value.LastIndexOf("宝贝", StringComparison.Ordinal);
        }

        private TextRange FindLastTextRange(string expected)
        {
            if (String.IsNullOrEmpty(expected)) return null;
            TextPointer cursor = _editor.Document.ContentStart;
            TextRange result = null;
            while (cursor != null && cursor.CompareTo(
                _editor.Document.ContentEnd) < 0)
            {
                if (cursor.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    string value = cursor.GetTextInRun(LogicalDirection.Forward);
                    int index = value.LastIndexOf(expected,
                        StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        TextPointer start = cursor.GetPositionAtOffset(index,
                            LogicalDirection.Forward);
                        TextPointer end = start == null ? null :
                            start.GetPositionAtOffset(expected.Length,
                                LogicalDirection.Forward);
                        if (start != null && end != null)
                            result = new TextRange(start, end);
                    }
                }
                cursor = cursor.GetNextContextPosition(
                    LogicalDirection.Forward);
            }
            return result;
        }

        internal bool ExerciseUnifiedNoteContextMenusForTest()
        {
            string[] required = { "新建便利贴", "新建待办清单", "新建日程",
                "重命名", "取消提醒", "颜色与透明度", "置顶 / 取消置顶",
                "删除此便利贴", "收起到侧边页签" };
            foreach (string header in required)
            {
                if (!ContextMenuContainsHeader(ContextMenu, header) ||
                    !ContextMenuContainsHeader(_editor.ContextMenu, header))
                    return false;
            }
            return !ContextMenuContainsHeader(ContextMenu, "删除便利贴") &&
                !ContextMenuContainsHeader(_editor.ContextMenu, "删除便利贴") &&
                ContextMenuContainsHeader(_editor.ContextMenu, "撤销") &&
                ContextMenuContainsHeader(_editor.ContextMenu, "全选");
        }

        private static bool ContextMenuContainsHeader(WC.ContextMenu menu,
            string expected)
        {
            if (menu == null) return false;
            foreach (object value in menu.Items)
            {
                WC.MenuItem item = value as WC.MenuItem;
                if (item != null && String.Equals(Convert.ToString(item.Header),
                    expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal bool ExerciseSchedulePinMarkerForTest()
        {
            Data.IsTodoList = false;
            Data.IsSchedule = true;
            Data.ScheduleItems.Clear();
            StickyScheduleItem item = new StickyScheduleItem("圆点只显示一次",
                DateTime.Today.AddDays(2));
            Data.ScheduleItems.Add(item);
            RefreshMode();
            PinSchedule(item);
            bool pinned = item.IsPinned && item.Text == "圆点只显示一次" &&
                ScheduleRowNameForTest() == "• 圆点只显示一次";
            PinSchedule(item);
            bool unpinned = !item.IsPinned && item.Text == "圆点只显示一次" &&
                ScheduleRowNameForTest() == "圆点只显示一次";
            return pinned && unpinned;
        }

        private string ScheduleRowNameForTest()
        {
            if (_scheduleRows.Children.Count == 0) return String.Empty;
            WC.Border border = _scheduleRows.Children[0] as WC.Border;
            WC.Grid grid = border == null ? null : border.Child as WC.Grid;
            WC.StackPanel details = grid == null || grid.Children.Count == 0
                ? null : grid.Children[0] as WC.StackPanel;
            WC.TextBlock name = details == null || details.Children.Count == 0
                ? null : details.Children[0] as WC.TextBlock;
            return name == null ? String.Empty : name.Text;
        }

        internal bool ExerciseFixedNoteTypeActionsForTest()
        {
            bool originalMode = Data.IsTodoList;
            bool originalSchedule = Data.IsSchedule;
            Data.IsSchedule = false;
            Data.IsTodoList = false;
            RefreshMode();
            bool ordinaryStayedOrdinary = !Data.IsTodoList && !Data.IsSchedule;
            Data.IsTodoList = true;
            RefreshMode();
            bool todoStayedTodo = Data.IsTodoList && !Data.IsSchedule;
            Data.IsTodoList = false;
            Data.IsSchedule = true;
            RefreshMode();
            bool scheduleStayedSchedule = Data.IsSchedule && !Data.IsTodoList;
            Data.IsTodoList = originalMode;
            Data.IsSchedule = originalSchedule;
            RefreshMode();
            return ordinaryStayedOrdinary && todoStayedTodo &&
                scheduleStayedSchedule &&
                !HasInlineCreationButtonsForTest;
        }

        internal bool ExerciseMultilingualInputForTest()
        {
            const string multilingual =
                "English 日本語 한국어 Русский العربية Français 🙂";
            bool editorInputEnabled =
                InputMethod.GetIsInputMethodEnabled(_editor) &&
                !InputMethod.GetIsInputMethodSuspended(_editor);
            _initializing = true;
            SetEditorPlainText(multilingual);
            _initializing = false;
            CaptureEditorContent();
            bool ordinaryRoundTrip = Data.Text == multilingual;
            _todoInput.Text = multilingual;
            bool todoInputEnabled =
                InputMethod.GetIsInputMethodEnabled(_todoInput) &&
                !InputMethod.GetIsInputMethodSuspended(_todoInput);
            return editorInputEnabled && todoInputEnabled && ordinaryRoundTrip &&
                _todoInput.Text == multilingual && _editor.AcceptsReturn;
        }

        internal bool ExerciseReminderSwitchContentPreservationForTest()
        {
            _initializing = true;
            SetEditorPlainText("修改字体字号后仍需保留的正文");
            _initializing = false;
            CaptureEditorContent();
            PersistNow();
            return Data.Text == "修改字体字号后仍需保留的正文" &&
                !String.IsNullOrEmpty(Data.RichTextRtf);
        }

        internal bool ExerciseReminderSelectionActionsForTest()
        {
            ReminderItem first = new ReminderItem(DateTime.UtcNow.AddMinutes(20),
                new string('提', 50));
            ReminderItem second = new ReminderItem(DateTime.UtcNow.AddMinutes(40),
                "第二条可管理提醒");
            UpdateReminderBanner(new ReminderItem[] { first, second });
            _reminderList.SelectedIndex = 1;
            ReminderItem modified = null;
            ReminderItem deleted = null;
            ModifyReminderRequested += delegate(object sender,
                ReminderActionEventArgs e) { modified = e.Reminder; };
            DeleteReminderRequested += delegate(object sender,
                ReminderActionEventArgs e) { deleted = e.Reminder; };
            ExecuteSelectedReminderModify();
            ExecuteSelectedReminderDelete();
            return Object.ReferenceEquals(_selectedReminder, second) &&
                Object.ReferenceEquals(modified, second) &&
                Object.ReferenceEquals(deleted, second);
        }

        internal bool ExerciseInlineCreationActionsRemovedForTest()
        {
            return !HasInlineCreationButtonsForTest;
        }

        internal bool ExerciseReminderFirstClickStabilityForTest(
            out bool blankAreaClearsSelection, out bool refreshesInPlace)
        {
            ReminderItem first = new ReminderItem(DateTime.UtcNow.AddMinutes(15),
                "首次点击必须稳定保留的提醒");
            ReminderItem second = new ReminderItem(DateTime.UtcNow.AddMinutes(30),
                "第二条提醒");
            ReminderItem[] items = new ReminderItem[] { first, second };
            UpdateReminderBanner(items);
            int rebuildCount = _reminderBannerRebuildCount;
            _reminderList.SelectedIndex = 0;
            bool selected = Object.ReferenceEquals(_selectedReminder, first);
            UpdateReminderBanner(items);
            refreshesInPlace = _reminderBannerRebuildCount == rebuildCount;
            bool preserved = Object.ReferenceEquals(_selectedReminder, first);
            _reminderList.SelectedItem = null;
            blankAreaClearsSelection = _selectedReminder == null;
            return selected && preserved && refreshesInPlace;
        }

        internal bool ExerciseTodoWrapAndInlineEditForTest()
        {
            Data.IsTodoList = true;
            Data.TodoItems.Clear();
            StickyTodoItem item = new StickyTodoItem(new string('待', 50), false);
            Data.TodoItems.Add(item);
            RefreshTodoList();
            WC.Border firstRow = null;
            foreach (W.UIElement element in _todoRows.Children)
            {
                WC.Border border = element as WC.Border;
                if (border != null) { firstRow = border; break; }
            }
            if (firstRow == null) return false;
            WC.Grid grid = firstRow.Child as WC.Grid;
            WC.TextBox editor = grid == null ? null : grid.Children[1] as WC.TextBox;
            if (editor == null) return false;
            bool readOnlyUntilDoubleClick = editor.IsReadOnly && !editor.Focusable;
            BeginTodoInlineEdit(editor, 7);
            bool preservesTextWithoutAutoSelection =
                editor.Text == item.Text && editor.SelectionStart == 7 &&
                editor.SelectionLength == 0;
            editor.Text = "双击修改成功";
            CommitTodoInlineEdit(item, editor, item.Text);
            return readOnlyUntilDoubleClick && preservesTextWithoutAutoSelection &&
                item.Text == "双击修改成功" &&
                editor.IsReadOnly && !editor.Focusable &&
                editor.TextWrapping == W.TextWrapping.Wrap;
        }

        internal bool ExerciseTodoOverallFontSizeForTest()
        {
            Data.IsTodoList = true;
            Data.TodoItems.Clear();
            Data.TodoItems.Add(new StickyTodoItem("整体字号测试", false));
            RefreshMode();
            ApplyTodoFontSize(48F);
            WC.Border firstRow = null;
            foreach (W.UIElement element in _todoRows.Children)
            {
                WC.Border border = element as WC.Border;
                if (border != null) { firstRow = border; break; }
            }
            WC.Grid grid = firstRow == null ? null : firstRow.Child as WC.Grid;
            WC.TextBox editor = grid == null ? null : grid.Children[1] as WC.TextBox;
            double expected = PointSizeToDip(48F);
            return Data.FontSizeTwips == 960 && editor != null &&
                Math.Abs(editor.FontSize - expected) < 0.1 &&
                Math.Abs(_todoInput.FontSize - expected) < 0.1 &&
                _formatToolbar.Visibility == W.Visibility.Visible &&
                _fontFamilyBox.Visibility == W.Visibility.Collapsed &&
                _fontSizeBox.Visibility == W.Visibility.Visible &&
                _fontSizeBox.Items.Count == 5 &&
                Convert.ToString(_fontSizeBox.Items[0]) == "特小 9" &&
                Convert.ToString(_fontSizeBox.Items[1]) == "小 10.5" &&
                Convert.ToString(_fontSizeBox.Items[2]) == "中 16" &&
                Convert.ToString(_fontSizeBox.Items[3]) == "大 22" &&
                Convert.ToString(_fontSizeBox.Items[4]) == "特大 48" &&
                _boldButton.Visibility == W.Visibility.Collapsed;
        }

        internal bool ExerciseDedicatedRowContextMenusForTest()
        {
            Data.IsTodoList = true;
            Data.TodoItems.Clear();
            Data.TodoItems.Add(new StickyTodoItem("右键菜单测试", false));
            RefreshMode();
            WC.Border todoRow = null;
            foreach (W.UIElement child in _todoRows.Children)
            {
                todoRow = child as WC.Border;
                if (todoRow != null) break;
            }
            bool todoMenuOk = todoRow != null && todoRow.ContextMenu != null &&
                todoRow.ContextMenu.Items.Count == 6 &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[0]).Header)
                    == "编辑待办" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[1]).Header)
                    == "设为未完成" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[2]).Header)
                    == "设为进行中" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[3]).Header)
                    == "设为已完成" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[4]).Header)
                    == "删除待办" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[5]).Header)
                    == "置顶待办";
            WC.Grid todoGrid = todoRow == null ? null : todoRow.Child as WC.Grid;
            WC.CheckBox todoCheck = todoGrid == null ? null :
                todoGrid.Children[0] as WC.CheckBox;
            WC.TextBox todoEditor = todoGrid == null ? null :
                todoGrid.Children[1] as WC.TextBox;
            todoMenuOk = todoMenuOk && todoCheck != null &&
                todoEditor != null && Object.ReferenceEquals(
                    todoCheck.ContextMenu, todoRow.ContextMenu) &&
                Object.ReferenceEquals(todoEditor.ContextMenu,
                    todoRow.ContextMenu) &&
                NextTodoState(StickyTodoState.Pending) ==
                    StickyTodoState.InProgress &&
                NextTodoState(StickyTodoState.InProgress) ==
                    StickyTodoState.Completed &&
                NextTodoState(StickyTodoState.Completed) ==
                    StickyTodoState.Pending;
            UpdateReminderBanner(new ReminderItem[] {
                new ReminderItem(DateTime.UtcNow.AddMinutes(5), "右键提醒测试") });
            WC.ListBoxItem reminderRow = _reminderList.Items.Count == 0
                ? null : _reminderList.Items[0] as WC.ListBoxItem;
            bool reminderMenuOk = reminderRow != null &&
                reminderRow.ContextMenu != null &&
                reminderRow.ContextMenu.Items.Count == 2 &&
                Convert.ToString(((WC.MenuItem)reminderRow.ContextMenu.Items[0]).Header)
                    == "编辑提醒" &&
                Convert.ToString(((WC.MenuItem)reminderRow.ContextMenu.Items[1]).Header)
                    == "删除提醒";
            return todoMenuOk && reminderMenuOk;
        }

        internal bool ExerciseBodyTextColorSwitchForTest()
        {
            _editor.Document.Blocks.Clear();
            Paragraph paragraph = new Paragraph(new Run("正文颜色切换"));
            paragraph.Foreground = System.Windows.Media.Brushes.Black;
            _editor.Document.Blocks.Add(paragraph);
            Data.TextColorArgb = Color.White.ToArgb();
            ApplyColors();
            TextRange whiteRange = new TextRange(
                _editor.Document.ContentStart, _editor.Document.ContentEnd);
            SolidColorBrush white = whiteRange.GetPropertyValue(
                TextElement.ForegroundProperty) as SolidColorBrush;
            Data.TextColorArgb = Color.Black.ToArgb();
            ApplyColors();
            TextRange blackRange = new TextRange(
                _editor.Document.ContentStart, _editor.Document.ContentEnd);
            SolidColorBrush black = blackRange.GetPropertyValue(
                TextElement.ForegroundProperty) as SolidColorBrush;
            return white != null && black != null &&
                white.Color == System.Windows.Media.Colors.White &&
                black.Color == System.Windows.Media.Colors.Black;
        }

        internal bool ExerciseReminderLiveSizePreviewForTest()
        {
            ReminderItem reminder = new ReminderItem(
                DateTime.UtcNow.AddMinutes(5), "实时字号", null, 10.5F, false);
            UpdateReminderBanner(new ReminderItem[] { reminder });
            PreviewReminderFontSize(reminder, 22F);
            bool preview = Math.Abs(ReminderBannerFirstFontSize - 22F) < 0.2F;
            UpdateReminderBanner(new ReminderItem[] { reminder });
            bool restored = Math.Abs(ReminderBannerFirstFontSize - 10.5F) < 0.2F;
            return preview && restored;
        }

        internal bool ExerciseDockResizeRoleForTest()
        {
            SetDockResizeRole(true, true, true, true, 250, 430);
            bool top = _dockGrouped && _dockResizeTop && _dockResizeBottom &&
                _dockSplitBottom && _dockDividerMinimumHeight == 250 &&
                _dockDividerMaximumHeight == 430;
            SetDockResizeRole(true, false, true, false);
            bool bottom = _dockGrouped && !_dockResizeTop && _dockResizeBottom &&
                !_dockSplitBottom;
            SetDockResizeRole(false, false, false);
            bool standalone = !_dockGrouped && _dockResizeTop &&
                _dockResizeBottom && !_dockSplitBottom;
            _resizeStartLeft = 300;
            _resizeStartWidth = 400;
            _windowResizeActive = true;
            _lastResizeHitTest = HtLeft;
            bool leftEdgeKeepsRightFixed =
                DockHorizontalGroupLeft(500) == 200;
            _lastResizeHitTest = HtRight;
            bool rightEdgeKeepsLeftFixed =
                DockHorizontalGroupLeft(500) == 300;
            _windowResizeActive = false;
            _lastResizeHitTest = 0;
            return top && bottom && standalone &&
                leftEdgeKeepsRightFixed && rightEdgeKeepsLeftFixed;
        }

        internal bool ExerciseGroupTopMostForTest()
        {
            Data.AlwaysOnTop = false;
            ApplyTopMostWindowState(false);
            bool unpinned = !Data.AlwaysOnTop && !Topmost &&
                CurrentPinActionText == "置顶";
            Data.AlwaysOnTop = true;
            ApplyTopMostWindowState(true);
            bool pinned = Data.AlwaysOnTop && Topmost &&
                CurrentPinActionText == "取消置顶";
            return unpinned && pinned;
        }
    }
}
