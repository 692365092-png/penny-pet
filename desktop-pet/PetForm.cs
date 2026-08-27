using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm : Form
    {
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private const int IdleRow = PetAnimationController.IdleRow;
        private const int RightRow = PetAnimationController.RightRow;
        private const int LeftRow = PetAnimationController.LeftRow;
        private const int WavingRow = PetAnimationController.WavingRow;
        private const int HoverRow = PetAnimationController.HoverRow;
        private const int FailedRow = PetAnimationController.FailedRow;
        private const int WaitingRow = PetAnimationController.WaitingRow;
        private const int ThinkingRow = PetAnimationController.ThinkingRow;
        private const int ReviewRow = PetAnimationController.ReviewRow;
        private const int NotificationRow = PetAnimationController.NotificationRow;
        // Zero means an at-time reminder stays until the bubble itself is
        // clicked or another application message replaces it.
        private sealed class BubbleMessage
        {
            public BubbleMessage(string text, string fontFamilyName,
                float fontSizePoints)
            {
                Text = text ?? String.Empty;
                FontFamilyName = fontFamilyName ?? "Microsoft YaHei UI";
                FontSizePoints = fontSizePoints;
            }

            public readonly string Text;
            public readonly string FontFamilyName;
            public readonly float FontSizePoints;
        }

        private sealed class DockTarget
        {
            public StickyNoteWindow Parent;
            public StickyNoteWindow ExistingChild;
        }

        private readonly System.Windows.Forms.Timer _animationTimer;
        private readonly System.Windows.Forms.Timer _reminderTimer;
        private readonly System.Windows.Forms.Timer _persistenceRetryTimer;
        private readonly PetReminderCoordinator _reminderCoordinator =
            new PetReminderCoordinator();
        private long _lastReminderBannerSecond
            { get { return _reminderCoordinator.LastBannerSecond; }
                set { _reminderCoordinator.LastBannerSecond = value; } }
        private readonly PetContextMenu _petContextMenu;
        private ContextMenuStrip _menu { get { return _petContextMenu.Menu; } }
        private ToolStripMenuItem _statusItem
            { get { return _petContextMenu.StatusItem; } }
        private ToolStripMenuItem _setReminderItem
            { get { return _petContextMenu.SetReminderItem; } }
        private ToolStripMenuItem _cancelItem
            { get { return _petContextMenu.CancelItem; } }
        private ToolStripMenuItem _manageNotesItem
            { get { return _petContextMenu.ManageNotesItem; } }
        private ToolStripMenuItem _collapseNotesItem
            { get { return _petContextMenu.CollapseNotesItem; } }
        private ToolStripMenuItem _expandTabsItem
            { get { return _petContextMenu.ExpandTabsItem; } }
        private ToolStripMenuItem _scaleItem
            { get { return _petContextMenu.ScaleItem; } }
        private ToolStripMenuItem _startupItem
            { get { return _petContextMenu.StartupItem; } }
        private ToolStripMenuItem _keyboardItem
            { get { return _petContextMenu.KeyboardItem; } }
        private ToolStripMenuItem _silentItem
            { get { return _petContextMenu.SilentItem; } }
        private readonly NotifyIcon _trayIcon;
        private readonly Icon _appIcon;
        private readonly ReminderSchedule _reminders;
        private readonly PetSettings _settings;
        private readonly GlobalKeyboardActivity _keyboard;
        private readonly KeyboardOverlayForm _keyOverlay;
        private readonly StickyNoteRepository _notes;
        private readonly StickyNoteTabsForm _leftNoteTabs;
        private readonly StickyNoteTabsForm _rightNoteTabs;
        private readonly Dictionary<string, StickyNoteWindow> _noteWindows =
            new Dictionary<string, StickyNoteWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<BubbleMessage> _pendingBubbleTexts =
            new Queue<BubbleMessage>();
        private readonly Random _random = new Random();
        private readonly object _keyboardQueueGate = new object();
        private readonly ArtPreloadReservations _artPreloads =
            new ArtPreloadReservations();
        private readonly PetAnimationController _animation =
            new PetAnimationController();

        private PetArtPackage _art;
        private Bitmap[][] _renderedFrames;
        private bool _renderedFramesOwnBitmaps;
        private SpeechBubbleForm _bubble;
        private ContactAuthorForm _contactAuthorForm;
        private bool _bubbleIsHover;
        private bool _bubbleIsPreAlert;
        private bool _bubbleIsDueReminder;
        private ReminderItem _preAlertItem
            { get { return _reminderCoordinator.PreAlertItem; }
                set { _reminderCoordinator.PreAlertItem = value; } }
        private bool _suppressHoverRestore;
        private int _row
            { get { return _animation.Row; } set { _animation.Row = value; } }
        private int _frame
            { get { return _animation.Frame; } set { _animation.Frame = value; } }
        private bool _dragging;
        private bool _dragMoved;
        private bool _mouseInside;
        private Point _dragMouseOrigin;
        private Point _dragWindowOrigin;
        private bool _typingSession { get { return _animation.TypingSession; }
            set { _animation.TypingSession = value; } }
        private int _typingRow { get { return _animation.TypingRow; }
            set { _animation.TypingRow = value; } }
        private int _idleRow { get { return _animation.IdleRowState; }
            set { _animation.IdleRowState = value; } }
        private DateTime _typingUntilUtc { get { return _animation.TypingUntilUtc; }
            set { _animation.TypingUntilUtc = value; } }
        private bool _reminderAttentionActive
            { get { return _animation.ReminderAttentionActive; }
                set { _animation.ReminderAttentionActive = value; } }
        private DateTime _nextFrameUtc { get { return _animation.NextFrameUtc; }
            set { _animation.NextFrameUtc = value; } }
        private DateTime _manualAnimationCooldownUntilUtc
            { get { return _animation.ManualAnimationCooldownUntilUtc; }
                set { _animation.ManualAnimationCooldownUntilUtc = value; } }
        private bool _manualAnimationActive
            { get { return _animation.ManualAnimationActive; }
                set { _animation.ManualAnimationActive = value; } }
        private int _manualAnimationRow
            { get { return _animation.ManualAnimationRow; }
                set { _animation.ManualAnimationRow = value; } }
        private bool _exiting;
        private int _scalePercent = 100;
        private KeyboardInputEventArgs _latestKeyboardEvent;
        private int _pendingKeyboardOccurrences;
        private bool _keyboardUiDispatchQueued;
        private bool _privacyScanRunning;
        private string _pendingOverlayText = String.Empty;
        private int _pendingOverlayOccurrences;
        private int _pendingOverlayVirtualKeyCode;
        private KeyboardFocusSnapshot _pendingOverlayFocusSnapshot;
        private long _pendingOverlayGeneration;
        private bool _positioningNoteTabs;
        private string _noteTabsSignature = String.Empty;
        private bool _ownNoteImeComposing;
        private DateTime _ownNoteInputQuietUntilUtc;
        private StickyNoteWindow _activeNoteDrag;
        private readonly List<StickyNoteData> _activeDockGroup =
            new List<StickyNoteData>();
        private readonly Dictionary<string, Point> _activeDockOriginalLocations =
            new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        private Point _activeNoteDragStartLocation;
        private Point _activeNoteDragLastLocation;
        private DateTime _activeNoteDragStartedUtc;
        private StickyNoteWindow _dockPreviewParent;
        private StickyNoteWindow _dockPreviewChild;
        private DockPulseIndicatorForm _dockPreviewIndicator;
        private DockPulseIndicatorForm _splitGuideIndicator;
        private StickyNoteData _splitRemainderSeed;
        private bool _movingDockGroup;
        private bool _activeNoteDetached;
        private bool _activeNoteSplitEligible;
        private bool _synchronizingDockLayout;
        private System.Windows.Forms.Timer _startupWorkTimer;
        private StartupWorkPhase _startupWorkPhase;
        private Queue<StickyNoteData> _startupVisibleNotes;
        private bool _startupUiReady;
        private bool _startupArtReady;
        private bool _startupReadyRaised;
        // The loading window is the only startup visual.  Keep the layered pet
        // window alive for initialization, but do not publish one of its frames
        // until both the restored notes and animation rows are ready.  Showing
        // both layered bitmaps at the saved pet location caused the startup
        // artwork and the normal pet to overlap.
        private bool _startupDisplaySuppressed = true;

        internal event EventHandler StartupReady;

        public PetForm() : this(null)
        {
        }

        internal PetForm(PetSettings preloadedSettings)
        {
            Text = "Penny pet";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(CellWidth, CellHeight);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            _settings = preloadedSettings ?? PetSettings.Load();
            _settings.SaveFailed += PersistenceSaveFailed;
            if (PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                _settings.ShowKeyOverlay,
                _settings.KeyboardPrivacyNoticeAccepted))
            {
                // Older versions could enable the hook without the explicit
                // first-use notice. Require a fresh opt-in after this upgrade.
                _settings.ShowKeyOverlay = false;
                _settings.Save();
            }
            _art = PetArtPackage.Load(CellWidth, CellHeight);
            Text = _art.DisplayName;
            _scalePercent = NormalizeScalePercent(_settings.ScalePercent);
            ClientSize = ScaledPetSize(_scalePercent);
            // Always show the compact ordinary idle clip first. The less common
            // long animations are decoded only when they are actually selected.
            _idleRow = IdleRow;
            _row = _idleRow;
            BuildRenderedFrameCache();

            _reminders = new ReminderSchedule();
            RestoreReminders();
            _notes = StickyNoteRepository.Load();
            _notes.SaveFailed += PersistenceSaveFailed;
            ReconcileNoteReminders();
            _leftNoteTabs = new StickyNoteTabsForm(StickyTabSide.Left,
                delegate(StickyNoteData note) { ShowStickyNote(note, true); },
                delegate(StickyNoteData note) { ConfirmDeleteStickyNote(note); },
                delegate(StickyNoteData note, int index)
                {
                    ReorderStickyNoteTab(note, index);
                });
            _rightNoteTabs = new StickyNoteTabsForm(StickyTabSide.Right,
                delegate(StickyNoteData note) { ShowStickyNote(note, true); },
                delegate(StickyNoteData note) { ConfirmDeleteStickyNote(note); },
                delegate(StickyNoteData note, int index)
                {
                    ReorderStickyNoteTab(note, index);
                });
            if (!_settings.StartupPreferenceInitialized)
            {
                // Startup is an explicit opt-in. First launch records the safe
                // default; the context-menu action remains the single place
                // that enables the registry entry.
                _settings.StartupPreferenceInitialized = true;
                _settings.StartWithWindows = false;
                _settings.Save();
            }
            _settings.ScalePercent = _scalePercent;
            _settings.KeyOverlayScalePercent =
                KeyboardOverlayForm.NormalizeTextScalePercent(
                    _settings.KeyOverlayScalePercent);
            Location = RestoreLocation();

            PetContextMenuCommands menuCommands = new PetContextMenuCommands();
            menuCommands.Opening = delegate
            {
                HideHoverBubble();
                RefreshMenuText();
            };
            menuCommands.Closed = delegate
            {
                if (_mouseInside && !_exiting) ShowOrUpdateHoverBubble();
            };
            menuCommands.ShowReminder = ShowReminderDialog;
            menuCommands.CreateNote = delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-note-menu-create");
            };
            menuCommands.CreateTodo = delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateTodoStickyNote();
                }, "sticky-todo-menu-create");
            };
            menuCommands.CreateSchedule = delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateScheduleStickyNote();
                }, "sticky-schedule-menu-create");
            };
            menuCommands.ManageNotes = ShowStickyNotesManager;
            menuCommands.CollapseNotes = CollapseAllStickyNotes;
            menuCommands.ExpandTabs = ExpandAllStickyNoteTabs;
            menuCommands.RecoverWindows = delegate
            {
                QueueStickyWindowAction(MoveVisibleStickyNotesToPetScreen,
                    "sticky-window-screen-recovery");
            };
            menuCommands.ShowScale = ShowScaleDialog;
            menuCommands.StartupClick = StartupItemClick;
            menuCommands.KeyboardClick = KeyboardItemClick;
            menuCommands.SilentClick = SilentItemClick;
            menuCommands.ContactAuthor = ShowContactAuthor;
            menuCommands.Exit = BeginExitSequence;
            _petContextMenu = new PetContextMenu(_art.DisplayName,
                _settings.StartWithWindows, _settings.ShowKeyOverlay,
                _settings.SilentMode, menuCommands);
            ContextMenuStrip = _petContextMenu.Menu;

            _trayIcon = new NotifyIcon();
            _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _trayIcon.Icon = _appIcon ?? SystemIcons.Application;
            _trayIcon.Text = _art.DisplayName.Length > 63
                ? _art.DisplayName.Substring(0, 63) : _art.DisplayName;
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _petContextMenu.Menu;
            _trayIcon.DoubleClick += delegate
            {
                EnsureVisible();
                BringToFront();
            };

            _animationTimer = new System.Windows.Forms.Timer();
            _animationTimer.Interval = 15;
            _animationTimer.Tick += AnimationTick;
            _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                RuntimeFrameDuration(_row, 0));
            _animationTimer.Start();
            _reminderTimer = new System.Windows.Forms.Timer();
            _reminderTimer.Interval = 500;
            _reminderTimer.Tick += ReminderTick;
            _reminderTimer.Start();
            _persistenceRetryTimer = new System.Windows.Forms.Timer();
            _persistenceRetryTimer.Interval = 5000;
            _persistenceRetryTimer.Tick += RetryUnsavedPersistence;
            if (_notes.HasUnsavedChanges || _settings.HasUnsavedChanges)
                _persistenceRetryTimer.Start();

            MouseDown += PetMouseDown;
            MouseMove += PetMouseMove;
            MouseUp += PetMouseUp;
            MouseEnter += delegate
            {
                _mouseInside = true;
                QueueArtPreload(HoverRow);
                ShowOrUpdateHoverBubble();
            };
            MouseLeave += delegate
            {
                _mouseInside = false;
                HideHoverBubble();
            };
            LocationChanged += delegate
            {
                PositionNoteTabs();
                RepositionCurrentBubble();
            };
            SizeChanged += delegate { PositionNoteTabs(); };

            _keyOverlay = new KeyboardOverlayForm(_settings.KeyOverlayScalePercent);
            _keyboard = new GlobalKeyboardActivity();
            _keyboard.Activity += KeyboardActivity;
            RefreshKeyboardMenuText();

            Shown += delegate
            {
                RenderCurrentFrame();
                QueueStartupInteractionPreload();
                // Notification remains lazy: only users who actually have a
                // reminder pay the decode cost before the reminder becomes due.
                if (_reminders.Count > 0) QueueArtPreload(NotificationRow);
                BeginDeferredStartupWork();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000;
                return value;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveLocation();
            foreach (StickyNoteWindow noteWindow in
                new List<StickyNoteWindow>(_noteWindows.Values))
            {
                if (!noteWindow.IsDisposed) noteWindow.CloseForApplicationExit();
            }
            _noteWindows.Clear();
            _notes.Save();
            _notes.SaveFailed -= PersistenceSaveFailed;
            _settings.SaveFailed -= PersistenceSaveFailed;
            _leftNoteTabs.Close();
            _rightNoteTabs.Close();
            _keyboard.Dispose();
            _keyOverlay.Dispose();
            _mouseInside = false;
            _suppressHoverRestore = true;
            if (_bubble != null && !_bubble.IsDisposed) _bubble.Close();
            if (_contactAuthorForm != null && !_contactAuthorForm.IsDisposed)
                _contactAuthorForm.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            if (_appIcon != null) _appIcon.Dispose();
            _menu.Dispose();
            _animationTimer.Dispose();
            _reminderTimer.Dispose();
            _persistenceRetryTimer.Dispose();
            StopDeferredStartupWork();
            ClearDockPreview();
            ClearSplitGuide();
            DisposeRenderedFrameCache();
            _art.Dispose();
            base.OnFormClosed(e);
        }

        private void ShowContactAuthor()
        {
            if (_contactAuthorForm != null && !_contactAuthorForm.IsDisposed)
            {
                if (!_contactAuthorForm.Visible) _contactAuthorForm.Show(this);
                PositionOwnedWindowLikeReminder(_contactAuthorForm);
                _contactAuthorForm.BringToFront();
                _contactAuthorForm.Activate();
                return;
            }
            _contactAuthorForm = new ContactAuthorForm();
            _contactAuthorForm.FormClosed += delegate
            {
                _contactAuthorForm = null;
            };
            _contactAuthorForm.StartPosition = FormStartPosition.Manual;
            PositionOwnedWindowLikeReminder(_contactAuthorForm);
            _contactAuthorForm.Show(this);
            PositionOwnedWindowLikeReminder(_contactAuthorForm);
            _contactAuthorForm.Activate();
        }

        private void PositionOwnedWindowLikeReminder(Form child)
        {
            if (child == null || child.IsDisposed) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Left + Width / 2 - child.Width / 2;
            int y = Top + Height / 2 - child.Height / 2;
            child.Location = new Point(
                Math.Max(work.Left, Math.Min(x, work.Right - child.Width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - child.Height)));
        }

        private Point RestoreLocation()
        {
            if (_settings.HasLocation)
            {
                Point saved = new Point(_settings.X, _settings.Y);
                if (IsVisible(saved)) return saved;
            }
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            return new Point(work.Right - Width - 24, work.Bottom - Height - 24);
        }

        private bool IsVisible(Point location)
        {
            Rectangle candidate = new Rectangle(location, ClientSize);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(screen.WorkingArea, candidate);
                if (visible.Width >= 48 && visible.Height >= 48) return true;
            }
            return false;
        }

        private void EnsureVisible()
        {
            if (IsVisible(Location)) return;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
            SaveLocation();
        }

        private void KeepFullyVisible()
        {
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
            int y = Math.Max(work.Top, Math.Min(Top, work.Bottom - Height));
            Location = new Point(x, y);
        }

        private void SaveLocation()
        {
            _settings.HasLocation = true;
            _settings.X = Left;
            _settings.Y = Top;
            _settings.ScalePercent = _scalePercent;
            _settings.Save();
        }

    }
}
