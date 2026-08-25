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
            public StickyNoteForm Parent;
            public StickyNoteForm ExistingChild;
        }

        private readonly System.Windows.Forms.Timer _animationTimer;
        private readonly System.Windows.Forms.Timer _reminderTimer;
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
        private readonly Dictionary<string, StickyNoteForm> _noteWindows =
            new Dictionary<string, StickyNoteForm>(StringComparer.OrdinalIgnoreCase);
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
        private long _pendingOverlayGeneration;
        private bool _positioningNoteTabs;
        private string _noteTabsSignature = String.Empty;
        private bool _ownNoteImeComposing;
        private DateTime _ownNoteInputQuietUntilUtc;
        private StickyNoteForm _activeNoteDrag;
        private readonly List<StickyNoteData> _activeDockGroup =
            new List<StickyNoteData>();
        private readonly Dictionary<string, Point> _activeDockOriginalLocations =
            new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        private const int DockSplitHoldMilliseconds = 520;
        private const int DockSplitPreHoldMovement = 7;
        // Keep every member inside a coordinate range that Win32 mouse
        // messages can address reliably.  A too-long chain is rejected at the
        // seam instead of relocating its root to make the tail fit.
        private const int DockCoordinateSafetyLimit = 30000;
        private Point _activeNoteDragStartLocation;
        private Point _activeNoteDragLastLocation;
        private DateTime _activeNoteDragStartedUtc;
        private StickyNoteForm _dockPreviewParent;
        private StickyNoteForm _dockPreviewChild;
        private DockPulseIndicatorForm _dockPreviewIndicator;
        private DockPulseIndicatorForm _splitGuideIndicator;
        private StickyNoteData _splitRemainderSeed;
        private bool _movingDockGroup;
        private bool _activeNoteDetached;
        private bool _activeNoteSplitEligible;
        private bool _synchronizingDockLayout;
        private System.Windows.Forms.Timer _startupWorkTimer;
        private int _startupWorkPhase;
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
                _settings.StartupPreferenceInitialized = true;
                _settings.StartWithWindows = true;
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

        private void BeginDeferredStartupWork()
        {
            if (_startupWorkTimer != null) return;
            _startupWorkPhase = 0;
            _startupWorkTimer = new System.Windows.Forms.Timer();
            _startupWorkTimer.Interval = 90;
            _startupWorkTimer.Tick += DeferredStartupTick;
            _startupWorkTimer.Start();
        }

        private void DeferredStartupTick(object sender, EventArgs e)
        {
            if (_exiting || IsDisposed)
            {
                StopDeferredStartupWork();
                return;
            }
            if (_startupWorkPhase == 0)
            {
                if (ShouldStartKeyboardHook(_settings.ShowKeyOverlay))
                {
                    try
                    {
                        _keyboard.Start();
                    }
                    catch (Exception error)
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-keyboard-start", error);
                    }
                }
                RefreshKeyboardMenuText();
                _startupWorkPhase++;
                return;
            }
            if (_startupWorkPhase == 1)
            {
                try
                {
                    string startupError;
                    StartupRegistration.Apply(_settings.StartWithWindows,
                        out startupError);
                    _settings.Save();
                    ReminderTick(null, EventArgs.Empty);
                    _startupVisibleNotes = BuildStartupRestoreQueue();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-secondary-startup", error);
                    _startupVisibleNotes = new Queue<StickyNoteData>();
                }
                _startupWorkPhase++;
                return;
            }
            if (_startupVisibleNotes != null &&
                _startupVisibleNotes.Count > 0)
            {
                StickyNoteData note = _startupVisibleNotes.Dequeue();
                try
                {
                    ShowStickyNote(note, false, false);
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-sticky-restore", error);
                    RecoverFailedLegacyStickyWindow(note);
                }
                return;
            }
            foreach (StickyNoteForm startupNote in _noteWindows.Values)
            {
                if (startupNote != null && !startupNote.IsDisposed &&
                    startupNote.IsVisible &&
                    !startupNote.HasCompletedFirstRender) return;
            }
            try
            {
                NormalizeAllDockGroups();
                RefreshMenuText();
                RefreshNoteTabs();
                if (_notes.RecoveredFromLoadFailure)
                    ShowBubble("检测到旧便利贴数据异常，原文件已经保留备份，" +
                        "新建功能已自动恢复。");
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "deferred-startup-finalize", error);
            }
            _startupUiReady = true;
            TryRaiseStartupReady();
            StopDeferredStartupWork();
        }

        private void TryRaiseStartupReady()
        {
            if (_startupReadyRaised || !CanReleaseStartupLoading(
                _startupUiReady, _startupArtReady) ||
                IsDisposed || _exiting) return;
            _startupDisplaySuppressed = false;
            _startupReadyRaised = true;
            RenderCurrentFrame();
            EventHandler ready = StartupReady;
            if (ready != null) ready(this, EventArgs.Empty);
        }

        internal static bool CanReleaseStartupLoading(bool uiReady,
            bool artReady)
        {
            return uiReady && artReady;
        }

        private Queue<StickyNoteData> BuildStartupRestoreQueue()
        {
            Queue<StickyNoteData> result = new Queue<StickyNoteData>();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                    restored.Add(member.Id);
                result.Enqueue(note);
            }
            return result;
        }

        private void StopDeferredStartupWork()
        {
            if (_startupWorkTimer == null) return;
            _startupWorkTimer.Stop();
            _startupWorkTimer.Tick -= DeferredStartupTick;
            _startupWorkTimer.Dispose();
            _startupWorkTimer = null;
            _startupVisibleNotes = null;
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
            foreach (StickyNoteForm noteWindow in
                new List<StickyNoteForm>(_noteWindows.Values))
            {
                if (!noteWindow.IsDisposed) noteWindow.CloseForApplicationExit();
            }
            _noteWindows.Clear();
            _notes.Save();
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

        private void CreateStickyNote(string text)
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(text);
                if (note == null) return;
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("便利贴", error);
            }
        }

        private void CreateTodoStickyNote()
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(String.Empty);
                if (note == null) return;
                note.IsTodoList = true;
                note.Title = "待办清单";
                _notes.Save();
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("待办清单", error);
            }
        }

        private void CreateScheduleStickyNote()
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(String.Empty);
                if (note == null) return;
                note.IsTodoList = false;
                note.IsSchedule = true;
                note.Title = "日程";
                note.FontSizeTwips = 320;
                note.Height = 360;
                _notes.Save();
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("日程", error);
            }
        }

        private void QueueStickyWindowAction(Action action, string context)
        {
            if (action == null || IsDisposed || Disposing) return;
            if (_menu != null && _menu.Visible) _menu.Close();
            BeginInvoke((MethodInvoker)delegate
            {
                try { action(); }
                catch (Exception error) { ShowStickyWindowFailure(context, error); }
            });
        }

        private void EnsureCreatedStickyWindowVisible(StickyNoteData note)
        {
            if (note == null) throw new ArgumentNullException("note");
            StickyNoteForm form;
            if (!_noteWindows.TryGetValue(note.Id, out form) || form == null ||
                form.IsDisposed)
                throw new InvalidOperationException("便利贴窗口没有创建成功。");
            if (!form.Visible)
            {
                form.ShowAndEdit();
                form.EnableWinFormsKeyboardInterop();
            }
            if (!form.Visible)
                throw new InvalidOperationException("便利贴窗口创建后仍不可见。");
        }

        private float PetScreenScale()
        {
            try
            {
                using (Graphics graphics = CreateGraphics())
                {
                    float scale = graphics.DpiX / 96F;
                    if (scale >= 0.75F && scale <= 4F) return scale;
                }
            }
            catch { }
            return 1F;
        }

        private static Size StickyPhysicalSize(StickyNoteForm form,
            float scale)
        {
            return new Size(Math.Max(1, (int)Math.Round(form.Width * scale)),
                Math.Max(1, (int)Math.Round(form.Height * scale)));
        }

        private void PlaceNewStickyWindowOnPetScreen(StickyNoteData note)
        {
            StickyNoteForm form;
            if (note == null || !_noteWindows.TryGetValue(note.Id, out form) ||
                form == null || form.IsDisposed) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            float scale = PetScreenScale();
            Size size = StickyPhysicalSize(form, scale);
            int offset = (_notes.GetAll().Count % 7) * 18;
            int x = Left - size.Width - 12 - offset;
            if (x < work.Left)
                x = Math.Min(work.Right - size.Width, Right + 12 + offset);
            int y = Top + offset;
            x = Math.Max(work.Left, Math.Min(x, work.Right - size.Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height));
            form.ShowRestoredAtPhysicalBounds(new Rectangle(x, y,
                size.Width, size.Height));
            form.EnableWinFormsKeyboardInterop();
            form.BringToFront();
            form.FocusPrimaryInputForTest();
            note.X = form.Left;
            note.Y = form.Top;
            note.Width = form.Width;
            note.Height = form.Height;
            _notes.Save();
        }

        private void MoveVisibleStickyNotesToPetScreen()
        {
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            float targetScale = PetScreenScale();
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<List<StickyNoteData>> components =
                new List<List<StickyNoteData>>();
            List<List<StickyNoteForm>> componentForms =
                new List<List<StickyNoteForm>>();
            List<Size> componentSizes = new List<Size>();
            int attemptedWindows = 0;
            int verifiedWindows = 0;
            _movingDockGroup = true;
            try
            {
                foreach (StickyNoteData seed in _notes.GetAll())
                {
                    if (seed == null || !seed.Visible || visited.Contains(seed.Id))
                        continue;
                    List<StickyNoteData> component = BuildDockChainOrder(seed);
                    if (component.Count == 0) component.Add(seed);
                    foreach (StickyNoteData note in component)
                        if (note != null) visited.Add(note.Id);

                    List<StickyNoteData> activeNotes =
                        new List<StickyNoteData>();
                    List<StickyNoteForm> activeForms =
                        new List<StickyNoteForm>();
                    int componentWidth = 280;
                    int componentHeight = 0;
                    foreach (StickyNoteData note in component)
                    {
                        if (note == null || !note.Visible) continue;
                        StickyNoteForm form;
                        try
                        {
                            form = GetOrCreateStickyNoteWindow(note);
                            if (!form.Visible) form.ShowRestored();
                            form.EnableWinFormsKeyboardInterop();
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "compat-sticky-recover-create", error);
                            continue;
                        }
                        if (form == null || form.IsDisposed) continue;
                        activeNotes.Add(note);
                        activeForms.Add(form);
                        Size physical = StickyPhysicalSize(form, targetScale);
                        componentWidth = Math.Max(componentWidth,
                            physical.Width);
                        componentHeight += Math.Max(
                            (int)Math.Round(220 * targetScale),
                            physical.Height);
                    }
                    if (activeForms.Count == 0) continue;
                    components.Add(activeNotes);
                    componentForms.Add(activeForms);
                    componentSizes.Add(new Size(componentWidth,
                        Math.Max(220, componentHeight)));
                }

                List<Rectangle> roots = CalculateStickyRecoveryLayout(work,
                    componentSizes, targetScale);
                for (int componentIndex = 0;
                    componentIndex < componentForms.Count; componentIndex++)
                {
                    List<StickyNoteForm> forms = componentForms[componentIndex];
                    List<StickyNoteData> notes = components[componentIndex];
                    Rectangle root = roots[componentIndex];
                    List<Size> memberSizes = new List<Size>();
                    foreach (StickyNoteForm form in forms)
                        memberSizes.Add(StickyPhysicalSize(form, targetScale));
                    List<Rectangle> layout = CalculateUnifiedDockLayout(
                        memberSizes, root.Left, root.Top, root.Width,
                        targetScale);
                    for (int memberIndex = 0;
                        memberIndex < forms.Count; memberIndex++)
                    {
                        StickyNoteForm form = forms[memberIndex];
                        StickyNoteData note = notes[memberIndex];
                        attemptedWindows++;
                        try
                        {
                            form.ShowRestoredAtPhysicalBounds(
                                layout[memberIndex]);
                            form.EnableWinFormsKeyboardInterop();
                            form.BringToFront();
                            Rectangle visiblePart = Rectangle.Intersect(
                                form.PhysicalBounds, work);
                            if (form.Visible &&
                                form.WindowState == FormWindowState.Normal &&
                                visiblePart.Width > 0 && visiblePart.Height > 0)
                                verifiedWindows++;
                            note.X = form.Left;
                            note.Y = form.Top;
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "compat-sticky-recover-show", error);
                        }
                    }
                }
            }
            finally { _movingDockGroup = false; }
            if (attemptedWindows > 0)
            {
                _notes.Save();
                ShowBriefBubble("已尝试将 " + attemptedWindows +
                    " 张已展开的便利贴集中到此屏幕；系统确认 " +
                    verifiedWindows + " 张处于可见范围。");
            }
            else ShowBriefBubble("当前没有已展开的便利贴。");
        }

        internal static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes)
        {
            return CalculateStickyRecoveryLayout(work, componentSizes, 1F);
        }

        private static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes, float scale)
        {
            List<Rectangle> result = new List<Rectangle>();
            int count = componentSizes == null ? 0 : componentSizes.Count;
            for (int index = 0; index < count; index++)
                result.Add(Rectangle.Empty);
            if (count == 0) return result;

            const int margin = 24;
            const int gap = 18;
            int scaledMargin = Math.Max(1, (int)Math.Round(margin * scale));
            int scaledGap = Math.Max(1, (int)Math.Round(gap * scale));
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int rowWidthLimit = Math.Max(minimumWidth,
                work.Width - scaledMargin * 2);
            List<List<int>> rows = new List<List<int>>();
            List<int> normal = new List<int>();
            List<int> oversized = new List<int>();
            for (int index = 0; index < count; index++)
            {
                Size size = componentSizes[index];
                // The height is the sum of every member in a docked group, so
                // a four-note stack is treated as one long component.
                bool isOversized = size.Width >= Math.Max(
                    (int)Math.Round(520 * scale),
                    work.Width * 45 / 100) || size.Height >= Math.Max(
                    (int)Math.Round(520 * scale),
                    work.Height * 50 / 100);
                if (isOversized) oversized.Add(index);
                else normal.Add(index);
            }

            List<int> row = new List<int>();
            int rowWidth = 0;
            foreach (int index in normal)
            {
                int width = Math.Max(minimumWidth, Math.Min(maximumWidth,
                    componentSizes[index].Width));
                int nextWidth = row.Count == 0 ? width :
                    rowWidth + scaledGap + width;
                if (row.Count > 0 && nextWidth > rowWidthLimit)
                {
                    rows.Add(row);
                    row = new List<int>();
                    rowWidth = 0;
                }
                row.Add(index);
                rowWidth = rowWidth == 0 ? width :
                    rowWidth + scaledGap + width;
            }
            if (row.Count > 0) rows.Add(row);
            // Wide/long single notes and whole docked stacks get their own
            // lower rows, horizontally centered below the ordinary notes.
            foreach (int index in oversized)
                rows.Add(new List<int>(new int[] { index }));

            List<int> rowHeights = new List<int>();
            int totalHeight = 0;
            foreach (List<int> recoveryRow in rows)
            {
                int height = minimumHeight;
                foreach (int index in recoveryRow)
                    height = Math.Max(height, Math.Min(componentSizes[index].Height,
                        Math.Max(minimumHeight, work.Height * 58 / 100)));
                rowHeights.Add(height);
                totalHeight += height;
            }
            totalHeight += Math.Max(0, rows.Count - 1) * scaledGap;
            int y = work.Top + Math.Max(scaledMargin,
                (work.Height - totalHeight) / 2);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<int> recoveryRow = rows[rowIndex];
                int width = 0;
                foreach (int index in recoveryRow)
                {
                    if (width > 0) width += scaledGap;
                    width += Math.Max(minimumWidth, Math.Min(maximumWidth,
                        componentSizes[index].Width));
                }
                int x = work.Left + (work.Width - width) / 2;
                foreach (int index in recoveryRow)
                {
                    int itemWidth = Math.Max(minimumWidth,
                        Math.Min(maximumWidth,
                        componentSizes[index].Width));
                    result[index] = new Rectangle(x, y, itemWidth,
                        componentSizes[index].Height);
                    x += itemWidth + scaledGap;
                }
                y += rowHeights[rowIndex] + scaledGap;
            }
            return result;
        }

        internal static Point CalculateStickyRecoveryAnchor(Rectangle work,
            Rectangle pet, Size window, int componentIndex)
        {
            int preferredLeft = pet.Left - window.Width - 12;
            if (preferredLeft < work.Left) preferredLeft = pet.Right + 12;
            int targetLeft = Math.Max(work.Left,
                Math.Min(preferredLeft, work.Right - window.Width));
            int availableTop = Math.Max(1, work.Height - 36);
            int relativeTop = pet.Top - work.Top +
                Math.Max(0, componentIndex) * 34;
            relativeTop %= availableTop;
            if (relativeTop < 0) relativeTop += availableTop;
            int targetTop = Math.Max(work.Top,
                Math.Min(work.Top + relativeTop, work.Bottom - 32));
            return new Point(targetLeft, targetTop);
        }

        private void RollBackFailedStickyCreation(StickyNoteData note)
        {
            if (note == null) return;
            StickyNoteForm form;
            if (_noteWindows.TryGetValue(note.Id, out form) && form != null &&
                !form.IsDisposed)
                form.CloseForApplicationExit();
            _noteWindows.Remove(note.Id);
            _notes.Remove(note);
            RefreshMenuText();
            RefreshNoteTabs();
        }

        private void ShowStickyWindowFailure(string kind, Exception error)
        {
            ApplicationDiagnostics.ReportNonFatal(kind ?? "sticky-window", error);
            MessageBox.Show(this,
                "未能显示" + (String.IsNullOrEmpty(kind) ? "便利贴" : kind) +
                "。程序没有保留不可见的空白项目。\n\n" +
                "请把下面的诊断文件发给作者：\n" +
                ApplicationDiagnostics.LogFilePath,
                "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private StickyNoteData CreateStickyNoteData(string text)
        {
            if (!_notes.CanCreate)
            {
                if (!_notes.LoadSucceeded)
                    ShowBubble("旧便利贴数据暂时无法安全恢复，请查看诊断记录。" +
                        "程序没有覆盖原文件。");
                else
                    ShowBubble("便利贴最多可以保存 " +
                        StickyNoteLimits.MaximumNotes +
                        " 张，请先删除不需要的便利贴。");
                return null;
            }
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int offset = (_notes.GetAll().Count % 7) * 18;
            int x = Left - 332 - offset;
            if (x < work.Left) x = Math.Min(work.Right - 332, Right + 12 + offset);
            int y = Math.Max(work.Top, Math.Min(Top + offset, work.Bottom - 312));
            StickyNoteData note = _notes.Create(text, new Point(x, y));
            if (note == null)
            {
                ShowBubble("便利贴创建失败，原有数据没有被修改。请查看诊断记录。");
                return null;
            }
            note.Width = 320;
            note.Height = 300;
            note.Visible = true;
            _notes.Save();
            return note;
        }

        private StickyNoteForm GetOrCreateStickyNoteWindow(StickyNoteData note)
        {
            StickyNoteForm existing;
            if (_noteWindows.TryGetValue(note.Id, out existing) && !existing.IsDisposed)
                return existing;
            Program.EnsureWpfApplicationForStickyNotes();
            StickyNoteRepository.RepairForDisplay(note, false);
            StickyNoteForm form;
            try { form = new StickyNoteForm(note); }
            catch (Exception firstError)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-window-legacy-first-open", firstError);
                // A WPF/native-window failure is not proof that user data is
                // damaged. Retry once without mutating the note; callers can
                // report the second failure while the original data stays safe.
                form = new StickyNoteForm(note);
            }
            form.NoteChanged += delegate { _notes.Save(); RefreshMenuText(); };
            form.NoteChanged += delegate { RefreshNoteTabs(); };
            form.HeaderDragStarted += StickyNoteHeaderDragStarted;
            form.HeaderDragMoved += StickyNoteHeaderDragMoved;
            form.HeaderDragCompleted += StickyNoteHeaderDragCompleted;
            form.CloseRequested += StickyNoteCloseRequested;
            form.PinStateChanged += StickyNotePinStateChanged;
            form.SizeChanged += StickyNoteSizeChanged;
            form.LocationChanged += StickyNoteLocationChanged;
            form.DockHorizontalResizing += StickyNoteDockHorizontalResizing;
            form.NewNoteRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-note-window-create");
            };
            form.NewTodoRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateTodoStickyNote();
                }, "sticky-todo-window-create");
            };
            form.NewScheduleRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateScheduleStickyNote();
                }, "sticky-schedule-window-create");
            };
            form.TypingActivity += delegate
            {
                // The pet and note windows share the WinForms UI thread.  Give
                // committed editor text a short uncontested window before the
                // next layered animation frame is rendered.
                _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(260);
                TriggerTypingAnimation();
            };
            form.ImeCompositionChanged += delegate(object sender,
                ImeCompositionEventArgs e)
            {
                _ownNoteImeComposing = e.Active;
                if (e.Active)
                {
                    _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
                }
                else
                {
                    _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(260);
                    _nextFrameUtc = _ownNoteInputQuietUntilUtc;
                }
            };
            form.CancelReminderRequested += delegate { CancelReminderForNote(note, true); };
            form.ModifyReminderRequested += delegate(object sender,
                ReminderActionEventArgs e)
            {
                EditReminder(e.Reminder);
            };
            form.DeleteReminderRequested += delegate(object sender,
                ReminderActionEventArgs e)
            {
                CancelReminder(e.Reminder, true);
            };
            form.DeleteRequested += delegate
            {
                if (MessageBox.Show(form, "确定删除这张便利贴吗？此操作无法撤销。",
                    "删除便利贴", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
                    DeleteStickyNote(note);
            };
            form.FormClosed += delegate { _noteWindows.Remove(note.Id); };
            _noteWindows[note.Id] = form;
            form.UpdateReminderBanner(_reminders.GetItems());
            return form;
        }

        private void RecoverFailedLegacyStickyWindow(StickyNoteData note)
        {
            if (note == null) return;
            StickyNoteForm failed;
            if (_noteWindows.TryGetValue(note.Id, out failed))
            {
                _noteWindows.Remove(note.Id);
                if (failed != null && !failed.IsDisposed)
                {
                    try { failed.CloseForApplicationExit(); }
                    catch { }
                }
            }
            // Do not apply destructive data repair for a temporary UI failure.
            // The note remains in the repository and can be retried later.
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ShowStickyNote(StickyNoteData note, bool focusEditor)
        {
            ShowStickyNote(note, focusEditor, true);
        }

        private void ShowStickyNote(StickyNoteData note, bool focusEditor,
            bool persistVisibility)
        {
            if (note == null) return;
            List<StickyNoteData> storedDockOrder =
                BuildDockChainOrderIncludingHidden(note);
            bool anyHiddenDockMember = storedDockOrder.Exists(
                delegate(StickyNoteData member)
                {
                    return !member.Visible;
                });
            if (ShouldRestoreWholeDockComponent(storedDockOrder.Count,
                anyHiddenDockMember))
            {
                RestoreStickyDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility);
                return;
            }
            StickyNoteForm form = GetOrCreateStickyNoteWindow(note);
            if (focusEditor) form.ShowAndEdit();
            else form.ShowRestored();
            form.EnableWinFormsKeyboardInterop();
            if (!focusEditor && persistVisibility) _notes.Save();
            RefreshNoteTabs();
        }


        private void ReorderStickyNoteTab(StickyNoteData note,
            int destinationIndex)
        {
            _notes.ReorderHidden(note, destinationIndex);
            _noteTabsSignature = String.Empty;
            RefreshNoteTabs();
        }

        private void CollapseAllStickyNotes()
        {
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
                StickyDockGroups.ApplyOrderedGroup(group);
                foreach (StickyNoteData member in group)
                {
                    handled.Add(member.Id);
                    StickyNoteForm form;
                    if (_noteWindows.TryGetValue(member.Id, out form) &&
                        form != null && !form.IsDisposed)
                        form.HideAsDockGroupMember();
                    else member.Visible = false;
                }
            }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ExpandAllStickyNoteTabs()
        {
            List<StickyNoteData> hidden = _notes.GetHiddenInTabOrder();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in hidden)
            {
                if (restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                    restored.Add(member.Id);
                ShowStickyNote(note, false);
            }
            if (hidden.Count > 0)
            {
                StickyNoteForm first;
                if (_noteWindows.TryGetValue(hidden[0].Id, out first) &&
                    first != null && !first.IsDisposed)
                    first.FocusPrimaryInputForTest();
            }
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void RefreshNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            // Side tabs have their own persistent order.  Sorting them by the
            // note's modified time here used to undo every successful drag.
            List<StickyNoteData> hidden = _notes.GetHiddenInTabOrder();
            StringBuilder signatureBuilder = new StringBuilder();
            foreach (StickyNoteData note in hidden)
            {
                signatureBuilder.Append(note.Id).Append('|')
                    .Append(note.DisplayTitle).Append('|')
                    .Append(note.ColorArgb).Append('\n');
            }
            string signature = signatureBuilder.ToString();
            if (String.Equals(signature, _noteTabsSignature,
                StringComparison.Ordinal))
            {
                PositionNoteTabs();
                return;
            }
            _noteTabsSignature = signature;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(hidden.Count,
                Height, work);
            List<StickyNoteData> left = hidden.GetRange(0, leftCount);
            List<StickyNoteData> right = hidden.GetRange(leftCount,
                hidden.Count - leftCount);
            _leftNoteTabs.SetNotes(left, 0);
            _rightNoteTabs.SetNotes(right, leftCount);
            PositionNoteTabs();
        }

        private void PositionNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null ||
                !IsHandleCreated || IsDisposed || _positioningNoteTabs) return;
            _positioningNoteTabs = true;
            try
            {
                Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
                int reserveLeft = _leftNoteTabs.Controls.Count > 0
                    ? StickyNoteTabsForm.TabWidth -
                        StickyNoteTabsForm.PetOverlapForWidth(Width) + 2 : 0;
                int reserveRight = _rightNoteTabs.Controls.Count > 0
                    ? StickyNoteTabsForm.TabWidth -
                        StickyNoteTabsForm.PetOverlapForWidth(Width) + 2 : 0;
                int minimumLeft = work.Left + reserveLeft;
                int maximumLeft = work.Right - reserveRight - Width;
                if (maximumLeft >= minimumLeft)
                {
                    int adjustedX = Math.Max(minimumLeft,
                        Math.Min(Left, maximumLeft));
                    int adjustedY = Math.Max(work.Top,
                        Math.Min(Top, work.Bottom - Height));
                    if (adjustedX != Left || adjustedY != Top)
                        Location = new Point(adjustedX, adjustedY);
                }
                _leftNoteTabs.ShowNear(Bounds, work);
                _rightNoteTabs.ShowNear(Bounds, work);
            }
            finally
            {
                _positioningNoteTabs = false;
            }
        }

        private void ShowStickyNotesManager()
        {
            bool createRequested = false;
            StickyNoteData showRequested = null;
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return _notes.GetAll(); },
                delegate { CreateStickyNote(String.Empty); },
                delegate(StickyNoteData note) { ShowStickyNote(note, true); },
                delegate(StickyNoteData note) { HideStickyNote(note); },
                delegate(StickyNoteData note) { DeleteStickyNote(note); }))
            {
                manager.ShowDialog(this);
                createRequested = manager.CreateRequested;
                showRequested = manager.ShowRequested;
            }
            if (createRequested)
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-manager-create");
            else if (showRequested != null)
                QueueStickyWindowAction(delegate
                {
                    ShowStickyNote(showRequested, true);
                    EnsureCreatedStickyWindowVisible(showRequested);
                }, "sticky-manager-show");
            RefreshMenuText();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (!_exiting && (HasFocusedOwnNoteTextInput() ||
                ShouldPauseOwnNoteAnimation(_ownNoteImeComposing,
                    _ownNoteInputQuietUntilUtc, now))) return;
            if (_exiting)
            {
                if (_row != WavingRow)
                {
                    _row = WavingRow;
                    _frame = 0;
                    _nextFrameUtc = now.AddMilliseconds(
                        RuntimeFrameDuration(_row, _frame));
                    RenderCurrentFrame();
                    return;
                }
                if (now < _nextFrameUtc) return;
                if (_frame >= RuntimeFrameCount(WavingRow) - 1)
                {
                    Close();
                    return;
                }
                _frame++;
                _nextFrameUtc = now.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
                return;
            }
            if (_typingSession && now > _typingUntilUtc)
                _typingSession = false;
            if (_manualAnimationActive && !(_dragging && _dragMoved))
            {
                if (!_art.IsRowLoaded(_manualAnimationRow))
                {
                    QueueArtPreload(_manualAnimationRow);
                    return;
                }
                if (_row != _manualAnimationRow)
                {
                    _row = _manualAnimationRow;
                    _frame = 0;
                    _nextFrameUtc = now.AddMilliseconds(
                        RuntimeFrameDuration(_row, _frame));
                    RenderCurrentFrame();
                    return;
                }
                if (now < _nextFrameUtc) return;
                if (_frame >= RuntimeFrameCount(_row) - 1)
                {
                    _manualAnimationActive = false;
                    _row = ChooseRow();
                    _frame = 0;
                }
                else
                {
                    _frame++;
                }
                _nextFrameUtc = now.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
                return;
            }
            int wanted = ChooseRow();
            if (_row != wanted)
            {
                _row = wanted;
                _frame = 0;
                _nextFrameUtc = now.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
                return;
            }
            if (now < _nextFrameUtc) return;
            if (ReminderAnimationCycleComplete(_reminderAttentionActive,
                _row, _frame, RuntimeFrameCount(_row)))
            {
                _reminderAttentionActive = false;
                _idleRow = IdleRow;
                _row = IdleRow;
                _frame = 0;
                _nextFrameUtc = now.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
                return;
            }
            if (IsIdleAnimationRow(_row) &&
                _frame >= RuntimeFrameCount(_row) - 1)
            {
                _idleRow = PickRandomIdleAnimationRow(_random, _row);
                QueueArtPreload(_idleRow);
                _row = _art.IsRowLoaded(_idleRow) ? _idleRow : IdleRow;
                _frame = 0;
            }
            else
            {
                _frame = (_frame + 1) % RuntimeFrameCount(_row);
            }
            _nextFrameUtc = now.AddMilliseconds(
                RuntimeFrameDuration(_row, _frame));
            RenderCurrentFrame();
        }

        private int RuntimeFrameCount(int row)
        {
            return _art.FrameCount(row);
        }

        private int RuntimeFrameDuration(int row, int frame)
        {
            return _art.FrameDuration(row, frame);
        }

        private int RuntimeAnimationCycleDuration(int row)
        {
            return _art.CycleDuration(row);
        }

        private int ChooseRow()
        {
            return _animation.ChooseRow(_exiting, _dragging && _dragMoved,
                _mouseInside, _menu.Visible, _art.IsRowLoaded);
        }

        internal static bool ReminderAnimationCycleComplete(bool active,
            int row, int frame, int frameCount)
        {
            return PetAnimationController.ReminderAnimationCycleComplete(
                active, row, frame, frameCount);
        }

        internal static int DueReminderBubbleDurationMilliseconds
        {
            get
            {
                return PetReminderCoordinator.
                    DueReminderBubbleDurationMilliseconds;
            }
        }

        internal static float DueReminderBubbleFontSizePoints(
            int bubbleScalePercent)
        {
            return KeyboardOverlayForm.TextFontSizePoints(
                bubbleScalePercent);
        }

        internal static bool ShouldReplaceBubble(bool currentIsDueReminder,
            bool incomingIsDueReminder, bool exiting)
        {
            return ShouldReplaceBubble(currentIsDueReminder, false,
                incomingIsDueReminder, exiting);
        }

        internal static bool ShouldReplaceBubble(bool currentIsDueReminder,
            bool currentIsPreAlert, bool incomingIsDueReminder, bool exiting)
        {
            return PetReminderCoordinator.ShouldReplaceBubble(
                currentIsDueReminder, currentIsPreAlert,
                incomingIsDueReminder, exiting);
        }

        private void PetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _exiting) return;
            _dragging = true;
            _dragMoved = false;
            QueueArtPreload(FailedRow);
            CloseCurrentBubbleWithoutRestoringHover();
            _keyOverlay.HideImmediately();
            _typingSession = false;
            _dragMouseOrigin = Cursor.Position;
            _dragWindowOrigin = Location;
            Capture = true;
        }

        private void PetMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = Cursor.Position;
            int dx = now.X - _dragMouseOrigin.X;
            int dy = now.Y - _dragMouseOrigin.Y;
            if (!_dragMoved && !MovementStartsDrag(dx, dy)) return;
            if (!_dragMoved)
            {
                _dragMoved = true;
                _manualAnimationActive = false;
            }
            Location = new Point(_dragWindowOrigin.X + dx, _dragWindowOrigin.Y + dy);
            _keyOverlay.UpdatePosition(this);
        }

        private void PetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_dragging) return;
            bool wasDrag = _dragMoved;
            _dragging = false;
            _dragMoved = false;
            Capture = false;
            if (wasDrag)
                SaveLocation();
            else
            {
                Location = _dragWindowOrigin;
                AdvanceManualAnimation();
            }
            ShowNextPendingBubble();
        }

        private void AdvanceManualAnimation()
        {
            DateTime now = DateTime.UtcNow;
            if (!ManualAnimationClickReady(now,
                _manualAnimationCooldownUntilUtc)) return;
            _manualAnimationCooldownUntilUtc = now.AddMilliseconds(
                PetAnimationController.ManualAnimationCooldownMilliseconds);
            int current = _manualAnimationActive ? _manualAnimationRow : _row;
            _manualAnimationRow = PickRandomManualAnimationRow(_random, current);
            _manualAnimationActive = true;
            _typingSession = false;
            QueueArtPreload(_manualAnimationRow);
            if (!_art.IsRowLoaded(_manualAnimationRow)) return;
            _row = _manualAnimationRow;
            _frame = 0;
            _nextFrameUtc = now.AddMilliseconds(
                RuntimeFrameDuration(_row, _frame));
            RenderCurrentFrame();
        }

        private void KeyboardActivity(object sender, KeyboardInputEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            bool shouldQueue;
            lock (_keyboardQueueGate)
            {
                if (_latestKeyboardEvent != null &&
                    String.Equals(_latestKeyboardEvent.DisplayText, e.DisplayText,
                        StringComparison.Ordinal))
                    _pendingKeyboardOccurrences = Math.Max(
                        _pendingKeyboardOccurrences, e.RepeatCount);
                else
                    _pendingKeyboardOccurrences = e.RepeatCount;
                _latestKeyboardEvent = e;
                shouldQueue = !_keyboardUiDispatchQueued;
                if (shouldQueue) _keyboardUiDispatchQueued = true;
            }
            if (!shouldQueue) return;
            try
            {
                BeginInvoke((MethodInvoker)ProcessPendingKeyboardActivity);
            }
            catch
            {
                lock (_keyboardQueueGate) _keyboardUiDispatchQueued = false;
            }
        }

        private void ProcessPendingKeyboardActivity()
        {
            KeyboardInputEventArgs keyboardEvent;
            int occurrences;
            lock (_keyboardQueueGate)
            {
                keyboardEvent = _latestKeyboardEvent;
                occurrences = Math.Max(1, _pendingKeyboardOccurrences);
                _latestKeyboardEvent = null;
                _pendingKeyboardOccurrences = 0;
                _keyboardUiDispatchQueued = false;
            }
            if (keyboardEvent == null || _dragging || _exiting) return;
            TriggerTypingAnimation();
            if (!_settings.ShowKeyOverlay ||
                String.IsNullOrEmpty(keyboardEvent.DisplayText)) return;
            if (IsOwnApplicationInputFocused())
            {
                _keyOverlay.HideImmediately();
                return;
            }
            QueuePrivacyCheckedOverlay(keyboardEvent.DisplayText, occurrences,
                keyboardEvent.VirtualKeyCode);
        }

        private void TriggerTypingAnimation()
        {
            DateTime now = DateTime.UtcNow;
            if (!_typingSession)
            {
                _typingRow = PickRandomTypingAnimationRow(_random);
                _typingSession = true;
                QueueArtPreload(_typingRow);
                int duration = _art.IsRowLoaded(_typingRow)
                    ? RuntimeAnimationCycleDuration(_typingRow) : 2400;
                _typingUntilUtc = now.AddMilliseconds(duration + 80);
            }
            else
            {
                DateTime trailing = now.AddMilliseconds(900);
                if (trailing > _typingUntilUtc) _typingUntilUtc = trailing;
            }
        }

        private void QueueStartupInteractionPreload()
        {
            // Keep loading visible until hover and drag are both decoded and
            // scaled for the user's current pet size. Otherwise the first
            // mouse interaction still performs expensive frame work after the
            // loading image has disappeared.
            Thread preloadThread = new Thread(new ThreadStart(delegate
            {
                int[] warmRows = { HoverRow, FailedRow };
                foreach (int row in warmRows)
                {
                    if (_art.IsRowLoaded(row)) continue;
                    bool ownsPreload = ReserveArtPreload(row);
                    try
                    {
                        if (ownsPreload) _art.PreloadRow(row);
                        else
                        {
                            for (int wait = 0; wait < 200 &&
                                !_art.IsRowLoaded(row); wait++)
                                Thread.Sleep(10);
                            if (!_art.IsRowLoaded(row)) _art.PreloadRow(row);
                        }
                    }
                    catch (Exception error)
                    {
                        if (!_exiting && !IsDisposed)
                            ApplicationDiagnostics.ReportNonFatal(
                                "art-preload-" + row, error);
                    }
                    finally
                    {
                        if (ownsPreload) CompleteArtPreload(row);
                    }
                }
                if (_exiting || IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (_exiting || IsDisposed) return;
                        try
                        {
                            foreach (int row in warmRows)
                                if (_art.IsRowLoaded(row))
                                    EnsureRenderedRow(row);
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "startup-interaction-render", error);
                        }
                        // Idle is the safe fallback if an optional animation
                        // is damaged; never leave the loading window stranded.
                        _startupArtReady = _art.IsRowLoaded(IdleRow);
                        TryRaiseStartupReady();
                    });
                }
                catch (InvalidOperationException) { }
            }));
            preloadThread.IsBackground = true;
            preloadThread.Priority = ThreadPriority.BelowNormal;
            preloadThread.Name = "Penny animation warmup";
            preloadThread.Start();
        }

        private void QueueArtPreload(int row)
        {
            if (!ReserveArtPreload(row)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _art.PreloadRow(row); }
                catch (Exception error)
                {
                    if (!_exiting && !IsDisposed)
                        ApplicationDiagnostics.ReportNonFatal(
                            "art-preload-" + row, error);
                }
                finally { CompleteArtPreload(row); }
            });
        }

        private bool ReserveArtPreload(int row)
        {
            if (_art == null) return false;
            return _artPreloads.TryReserve(row, _art.IsRowLoaded(row),
                DateTime.UtcNow);
        }

        private void CompleteArtPreload(int row)
        {
            bool loaded = _art != null && _art.IsRowLoaded(row);
            _artPreloads.Complete(row, loaded, DateTime.UtcNow);
        }

        private void QueuePrivacyCheckedOverlay(string displayText, int occurrences,
            int virtualKeyCode)
        {
            bool startWorker;
            lock (_keyboardQueueGate)
            {
                string next = displayText ?? String.Empty;
                if (String.Equals(_pendingOverlayText, next,
                    StringComparison.Ordinal))
                    _pendingOverlayOccurrences = Math.Max(
                        _pendingOverlayOccurrences, Math.Max(1, occurrences));
                else
                {
                    _pendingOverlayText = next;
                    _pendingOverlayOccurrences = Math.Max(1, occurrences);
                }
                _pendingOverlayVirtualKeyCode = virtualKeyCode;
                _pendingOverlayGeneration++;
                startWorker = !_privacyScanRunning;
                if (startWorker) _privacyScanRunning = true;
            }
            if (startWorker)
                ThreadPool.QueueUserWorkItem(PrivacyCheckedOverlayWorker);
        }

        private void PrivacyCheckedOverlayWorker(object state)
        {
            string displayText;
            long generation;
            lock (_keyboardQueueGate)
            {
                displayText = _pendingOverlayText;
                generation = _pendingOverlayGeneration;
            }
            bool sensitive = SensitiveInputDetector.IsSensitiveFocus();
            int occurrences;
            int virtualKeyCode;
            bool restart;
            lock (_keyboardQueueGate)
            {
                restart = !IsCurrentPrivacyScan(generation,
                    _pendingOverlayGeneration);
                if (restart)
                {
                    // The focus check belongs to an older key event. Keep the
                    // worker ownership and check the newest event separately.
                    occurrences = 0;
                    virtualKeyCode = 0;
                }
                else
                {
                // The hook provides an absolute repeat count. Keep the latest
                // value that arrived during the privacy scan instead of adding
                // cumulative counts (1+2+3) or losing them to worker timing.
                    displayText = _pendingOverlayText;
                    occurrences = Math.Max(1, _pendingOverlayOccurrences);
                    virtualKeyCode = _pendingOverlayVirtualKeyCode;
                    _pendingOverlayOccurrences = 0;
                    _privacyScanRunning = false;
                }
            }
            if (restart)
            {
                ThreadPool.QueueUserWorkItem(PrivacyCheckedOverlayWorker);
                return;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    lock (_keyboardQueueGate)
                    {
                        if (!IsCurrentPrivacyScan(generation,
                            _pendingOverlayGeneration)) return;
                    }
                    if (_dragging || _exiting || !_settings.ShowKeyOverlay ||
                        IsOwnApplicationInputFocused() || sensitive)
                    {
                        _keyOverlay.HideImmediately();
                        return;
                    }
                    _keyOverlay.ShowKeyRepeatCount(this, displayText, occurrences,
                        virtualKeyCode);
                });
            }
            catch { }
        }

        private static bool IsOwnApplicationInputFocused()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form != null && !form.IsDisposed && form.ContainsFocus &&
                    form.GetType().Assembly == typeof(PetForm).Assembly)
                    return true;
            }
            return false;
        }

        internal static bool IsCurrentPrivacyScan(long capturedGeneration,
            long currentGeneration)
        {
            return capturedGeneration == currentGeneration;
        }

        internal static int AttentionAnimationRow(bool notificationLoaded)
        {
            return PetAnimationController.AttentionAnimationRow(
                notificationLoaded);
        }

        internal static int PickRandomTypingAnimationRow(Random random)
        {
            return PetAnimationController.PickRandomTypingAnimationRow(random);
        }

        internal static int PickRandomIdleAnimationRow(Random random,
            int currentRow)
        {
            return PetAnimationController.PickRandomIdleAnimationRow(random,
                currentRow);
        }

        internal static int IdleThoughtProbabilityBase
        {
            get
            {
                return PetAnimationController.IdleThoughtProbabilityDenominator;
            }
        }

        internal static int GuitarFailureProbabilityBase
        {
            get
            {
                return PetAnimationController.GuitarFailureProbabilityDenominator;
            }
        }

        internal static bool IsIdleAnimationRow(int row)
        {
            return PetAnimationController.IsIdleAnimationRow(row);
        }

        internal static bool IsTypingAnimationRow(int row)
        {
            return PetAnimationController.IsTypingAnimationRow(row);
        }

        internal static int DragAnimationRow
        {
            get { return FailedRow; }
        }

        internal static int NormalizeScalePercent(int value)
        {
            int clamped = Math.Max(50, Math.Min(200, value));
            return ((clamped + 5) / 10) * 10;
        }

        internal static Size ScaledPetSize(int scalePercent)
        {
            int normalized = NormalizeScalePercent(scalePercent);
            return new Size(CellWidth * normalized / 100,
                CellHeight * normalized / 100);
        }

        private void RenderCurrentFrame()
        {
            if (_startupDisplaySuppressed || !IsHandleCreated || IsDisposed)
                return;
            EnsureRenderedRow(_row);
            Bitmap[] rowFrames = _renderedFrames[_row];
            if (rowFrames == null || rowFrames.Length == 0) return;
            if (_frame < 0 || _frame >= rowFrames.Length) _frame = 0;
            Bitmap frame = rowFrames[_frame];
            if (frame != null) LayeredSpriteRenderer.Show(this, frame);
        }

        private void BuildRenderedFrameCache()
        {
            _renderedFrames = new Bitmap[PetArtPackage.RuntimeStateNames.Length][];
            _renderedFramesOwnBitmaps = _scalePercent != 100;
            EnsureRenderedRow(_row);
        }

        private void EnsureRenderedRow(int row)
        {
            if (_renderedFrames == null || row < 0 ||
                row >= _renderedFrames.Length)
                throw new ArgumentOutOfRangeException("row");
            if (_renderedFrames[row] != null) return;
            Dictionary<Bitmap, Bitmap> scaled = new Dictionary<Bitmap, Bitmap>();
            int count = RuntimeFrameCount(row);
            Bitmap[] rendered = new Bitmap[count];
            for (int frame = 0; frame < count; frame++)
            {
                Bitmap source = _art.GetFrame(row, frame);
                if (_scalePercent == 100)
                {
                    rendered[frame] = source;
                }
                else
                {
                    Bitmap resized;
                    if (!scaled.TryGetValue(source, out resized))
                    {
                        resized = ResizeFrame(source,
                            ScaledPetSize(_scalePercent));
                        scaled[source] = resized;
                    }
                    rendered[frame] = resized;
                }
            }
            _renderedFrames[row] = rendered;
        }

        private static Bitmap ResizeFrame(Bitmap original, Size size)
        {
            Bitmap result = new Bitmap(size.Width, size.Height,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(original, new Rectangle(Point.Empty, size),
                    new Rectangle(Point.Empty, original.Size), GraphicsUnit.Pixel);
            }
            return result;
        }

        private void DisposeRenderedFrameCache()
        {
            if (_renderedFrames == null) return;
            if (_renderedFramesOwnBitmaps)
            {
                HashSet<Bitmap> disposed = new HashSet<Bitmap>();
                foreach (Bitmap[] row in _renderedFrames)
                {
                    if (row == null) continue;
                    foreach (Bitmap frame in row)
                        if (frame != null && disposed.Add(frame)) frame.Dispose();
                }
            }
            _renderedFrames = null;
            _renderedFramesOwnBitmaps = false;
        }

        private void ShowBubble(string text)
        {
            ShowBubble(text, KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
        }

        private void ShowBriefBubble(string text)
        {
            ShowBubble(text, KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent), 2000);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints, 20000);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, true);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds,
            bool deferWhileDragging)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, deferWhileDragging, false);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds,
            bool deferWhileDragging, bool isDueReminder)
        {
            if (_bubble != null && !_bubble.IsDisposed &&
                !ShouldReplaceBubble(_bubbleIsDueReminder,
                    _bubbleIsPreAlert, isDueReminder, _exiting))
                return;
            if (_dragging && deferWhileDragging)
            {
                _pendingBubbleTexts.Enqueue(new BubbleMessage(text,
                    fontFamilyName, fontSizePoints));
                return;
            }
            // ShouldReplaceBubble has already protected pre-alerts where
            // appropriate. Force-close here so a later ordinary message can
            // replace a persistent due reminder.
            CloseCurrentBubbleWithoutRestoringHover(true);
            SpeechBubbleForm bubble = new SpeechBubbleForm(text,
                Math.Max(0, autoCloseMilliseconds),
                fontFamilyName, fontSizePoints);
            _bubble = bubble;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = isDueReminder;
            _preAlertItem = null;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        private void ShowNextPendingBubble()
        {
            if (_dragging || _exiting || _pendingBubbleTexts.Count == 0) return;
            BubbleMessage message = _pendingBubbleTexts.Dequeue();
            ShowBubble(message.Text, message.FontFamilyName,
                message.FontSizePoints);
        }

        private void ShowOrUpdatePreAlert(ReminderItem item)
        {
            if (item == null || _dragging || _exiting || _menu.Visible || IsDisposed)
                return;
            int seconds = Math.Max(0, (int)Math.Ceiling(
                (item.DeadlineUtc - DateTime.UtcNow).TotalSeconds));
            string text = "提醒倒计时 " + seconds + " 秒\n" + item.Text;
            if (_bubble != null && !_bubble.IsDisposed)
            {
                if (_bubbleIsPreAlert && ReferenceEquals(_preAlertItem, item))
                {
                    _bubble.UpdateText(text);
                    _bubble.ShowNear(this);
                    return;
                }
                if (!_bubbleIsHover) return;
                CloseCurrentBubbleWithoutRestoringHover();
            }
            // The optional pre-alert is deliberately compact.  The selected
            // reminder size is reserved for the actual due-time bubble.
            SpeechBubbleForm bubble = new SpeechBubbleForm(text, 0,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            _bubble = bubble;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = true;
            _bubbleIsDueReminder = false;
            _preAlertItem = item;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        private void ShowOrUpdateHoverBubble()
        {
            if (!ShouldShowHoverBubble(_mouseInside, _menu.Visible, _dragging,
                _settings.SilentMode) ||
                IsDisposed || _exiting) return;
            ReminderItem next = _reminders.Next;
            string text = next != null
                ? "距离最近提醒还有" + FormatRemaining(next.Remaining) +
                    "。\n当前共有 " + _reminders.Count + " 条提醒。"
                : "今天想要做些什么呢？";

            if (_bubble != null && !_bubble.IsDisposed)
            {
                if (_bubbleIsHover)
                {
                    _bubble.UpdateText(text);
                    _bubble.ShowNear(this);
                }
                return;
            }

            SpeechBubbleForm bubble = new SpeechBubbleForm(text, 0,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            _bubble = bubble;
            _bubbleIsHover = true;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = false;
            _preAlertItem = null;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging)
        {
            return ShouldShowHoverBubble(mouseInside, menuVisible, dragging, false);
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging, bool silentMode)
        {
            return mouseInside && !menuVisible && !dragging && !silentMode;
        }

        internal static bool ShouldSuppressDailyBubble(bool silentMode,
            bool isReminderBubble)
        {
            return silentMode && !isReminderBubble;
        }

        private void HideHoverBubble()
        {
            if (!_bubbleIsHover || _bubble == null || _bubble.IsDisposed) return;
            _bubbleIsHover = false;
            _bubble.Close();
        }

        private void CloseCurrentBubbleWithoutRestoringHover(
            bool forceProtectedReminder = false)
        {
            if (_bubble == null || _bubble.IsDisposed) return;
            if ((_bubbleIsDueReminder || _bubbleIsPreAlert) &&
                !forceProtectedReminder && !_exiting) return;
            _suppressHoverRestore = true;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = false;
            _preAlertItem = null;
            _bubble.Close();
            _suppressHoverRestore = false;
            _bubble = null;
        }

        private void BubbleClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(_bubble, sender))
            {
                _bubble = null;
                _bubbleIsHover = false;
                _bubbleIsPreAlert = false;
                _bubbleIsDueReminder = false;
                _preAlertItem = null;
            }
            if (_suppressHoverRestore || _dragging || _exiting || IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (_pendingBubbleTexts.Count > 0)
                {
                    ShowNextPendingBubble();
                    return;
                }
                ReminderItem next = _reminders.NextPreAlert;
                if (ShouldShowPreAlert(next, next == null
                    ? TimeSpan.Zero : next.Remaining))
                    ShowOrUpdatePreAlert(next);
                else if (_mouseInside && !_menu.Visible)
                    ShowOrUpdateHoverBubble();
            });
        }

        private void RepositionCurrentBubble()
        {
            if (_bubble == null || _bubble.IsDisposed) return;
            _bubble.RepositionNear(this);
        }

        private void RefreshMenuText()
        {
            _cancelItem.DropDownItems.Clear();
            List<ReminderItem> items = _reminders.GetItems();
            if (items.Count > 0)
            {
                _statusItem.Text = "共 " + items.Count + " 条提醒，最近：" +
                    items[0].DeadlineUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                _cancelItem.Enabled = true;
                for (int i = 0; i < items.Count; i++)
                {
                    ReminderItem target = items[i];
                    string shortText = target.Text.Length > 16
                        ? target.Text.Substring(0, 16) + "…" : target.Text;
                    ToolStripMenuItem cancelOne = new ToolStripMenuItem(
                        (i + 1) + "．" + target.DeadlineUtc.ToLocalTime().ToString(
                        "MM-dd HH:mm:ss") + "  " + shortText);
                    cancelOne.Click += delegate { CancelReminder(target, true); };
                    _cancelItem.DropDownItems.Add(cancelOne);
                }
                _cancelItem.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem cancelAll = new ToolStripMenuItem("取消全部提醒");
                cancelAll.Click += delegate { CancelAllReminders(); };
                _cancelItem.DropDownItems.Add(cancelAll);
            }
            else
            {
                _statusItem.Text = "当前没有提醒";
                _cancelItem.Enabled = false;
            }
            _setReminderItem.Enabled = items.Count < ReminderSchedule.MaximumItems;
            _setReminderItem.Text = "添加提醒…（" + items.Count + "/" +
                ReminderSchedule.MaximumItems + "）";
            _manageNotesItem.Text = "便利贴管理…（" + _notes.GetAll().Count + "张）";
            _silentItem.Checked = _settings.SilentMode;
            int visibleNotes = 0;
            int hiddenNotes = 0;
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note.Visible) visibleNotes++;
                else hiddenNotes++;
            }
            _collapseNotesItem.Text = "收起全部便利贴到页签（" + visibleNotes + "张）";
            _collapseNotesItem.Enabled = visibleNotes > 0;
            _expandTabsItem.Text = "展开全部侧边页签（" + hiddenNotes + "张）";
            _expandTabsItem.Enabled = hiddenNotes > 0;
            _scaleItem.Text = "调整大小…（桌宠 " + _scalePercent + "% / 按键" +
                KeyTextSizeName(_settings.KeyOverlayScalePercent) + "）";
            RefreshKeyboardMenuText();
        }

        private void ShowScaleDialog()
        {
            using (ScaleDialog dialog = new ScaleDialog(_scalePercent,
                _settings.KeyOverlayScalePercent))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyScale(dialog.SelectedPercent, dialog.SelectedKeyTextPercent);
            }
        }

        private void ApplyScale(int percent, int keyTextPercent)
        {
            int next = NormalizeScalePercent(percent);
            int nextKeyText = KeyboardOverlayForm.NormalizeTextScalePercent(
                keyTextPercent);
            bool petChanged = next != _scalePercent;
            bool keyTextChanged = nextKeyText != _settings.KeyOverlayScalePercent;
            if (!petChanged && !keyTextChanged) return;
            _keyOverlay.HideImmediately();
            if (petChanged)
            {
                int centerX = Left + Width / 2;
                int bottom = Bottom;
                DisposeRenderedFrameCache();
                _scalePercent = next;
                ClientSize = ScaledPetSize(_scalePercent);
                BuildRenderedFrameCache();
                Location = new Point(centerX - Width / 2, bottom - Height);
                KeepFullyVisible();
                RenderCurrentFrame();
                if (_bubble != null && !_bubble.IsDisposed) _bubble.ShowNear(this);
            }
            _keyOverlay.SetTextScale(nextKeyText);
            _settings.ScalePercent = _scalePercent;
            _settings.KeyOverlayScalePercent = nextKeyText;
            SaveLocation();
            RefreshMenuText();
            ShowBubble("大小已更新：桌宠 " + _scalePercent + "% ，按键文字" +
                KeyTextSizeName(nextKeyText) + "。");
        }

        private static string KeyTextSizeName(int percent)
        {
            int value = KeyboardOverlayForm.NormalizeTextScalePercent(percent);
            if (value == 60) return "小";
            if (value == 150) return "大";
            return "中";
        }

        private void KeyboardItemClick(object sender, EventArgs e)
        {
            bool desired = _keyboardItem.Checked;
            if (desired && !_keyboard.IsRunning)
            {
                try
                {
                    _keyboard.Start();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "keyboard-user-start", error);
                }
                desired = _keyboard.IsRunning;
            }
            else if (!desired && _keyboard.IsRunning)
                _keyboard.Dispose();
            _keyboardItem.Checked = desired;
            _settings.ShowKeyOverlay = desired;
            _settings.Save();
            if (!_settings.ShowKeyOverlay) _keyOverlay.HideImmediately();
            RefreshKeyboardMenuText();
        }

        internal static bool ShouldStartKeyboardHook(bool showKeyOverlay)
        {
            return showKeyOverlay;
        }

        private void SilentItemClick(object sender, EventArgs e)
        {
            _settings.SilentMode = _silentItem.Checked;
            _settings.Save();
            if (_settings.SilentMode) HideHoverBubble();
        }

        private void RefreshKeyboardMenuText()
        {
            if (_keyboard == null)
            {
                _keyboardItem.Text = "按键显示：正在检查";
                _keyboardItem.Enabled = false;
                return;
            }
            _keyboardItem.Enabled = true;
            _keyboardItem.Checked = _settings.ShowKeyOverlay;
            if (!_settings.ShowKeyOverlay)
                _keyboardItem.Text = "按键显示：已关闭";
            else if (_keyboard.IsRunning)
                _keyboardItem.Text = "按键显示：已开启（密码框自动隐藏）";
            else
                _keyboardItem.Text = "按键显示：当前不可用";
        }

        private void StartupItemClick(object sender, EventArgs e)
        {
            bool desired = _startupItem.Checked;
            string error;
            if (!StartupRegistration.Apply(desired, out error))
            {
                _startupItem.Checked = !desired;
                MessageBox.Show(this, "开机自启设置失败：" + error,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settings.StartupPreferenceInitialized = true;
            _settings.StartWithWindows = desired;
            _settings.Save();
            ShowBubble(desired ? "开机自动启动已开启。" : "开机自动启动已关闭。");
        }

        private void BeginExitSequence()
        {
            if (_exiting) return;
            _exiting = true;
            _reminderTimer.Stop();
            _dragging = false;
            Capture = false;
            _typingSession = false;
            _manualAnimationActive = false;
            _keyOverlay.HideImmediately();
            _mouseInside = false;
            if (_menu.Visible) _menu.Close();
            CloseCurrentBubbleWithoutRestoringHover();
            _row = WavingRow;
            _frame = 0;
            _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                RuntimeFrameDuration(_row, _frame));
            RenderCurrentFrame();
            if (!ShouldSuppressDailyBubble(_settings.SilentMode, false))
                ShowBubble("再见啦，照顾好自己！");
        }

        internal static bool IsPreAlertWindow(TimeSpan remaining)
        {
            return PetReminderCoordinator.IsPreAlertWindow(remaining);
        }

        internal static bool ShouldShowPreAlert(ReminderItem item,
            TimeSpan remaining)
        {
            return PetReminderCoordinator.ShouldShowPreAlert(item, remaining);
        }

        internal static bool ShouldPauseOwnNoteAnimation(bool composing,
            DateTime quietUntilUtc, DateTime nowUtc)
        {
            return PetAnimationController.ShouldPauseOwnNoteAnimation(
                composing, quietUntilUtc, nowUtc);
        }

        internal static bool ShouldRunReminderClock(bool exiting)
        {
            return PetReminderCoordinator.ShouldRunReminderClock(exiting);
        }

        internal static bool ShouldRestoreReminderAfterLaunch(ReminderItem item,
            DateTime launchedUtc)
        {
            return PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                item, launchedUtc);
        }

        internal static bool IsManualAnimationRow(int row)
        {
            return PetAnimationController.IsManualAnimationRow(row);
        }

        internal static int PickRandomManualAnimationRow(Random random,
            int currentRow)
        {
            return PetAnimationController.PickRandomManualAnimationRow(random,
                currentRow);
        }

        internal static bool ManualAnimationClickReady(DateTime nowUtc,
            DateTime cooldownUntilUtc)
        {
            return PetAnimationController.ManualAnimationClickReady(nowUtc,
                cooldownUntilUtc);
        }

        internal static bool MovementStartsDrag(int dx, int dy)
        {
            return PetAnimationController.MovementStartsDrag(dx, dy);
        }

        internal static int ManualAnimationCooldown
        {
            get
            {
                return PetAnimationController.ManualAnimationCooldownMilliseconds;
            }
        }

        private bool HasFocusedOwnNoteTextInput()
        {
            foreach (StickyNoteForm form in _noteWindows.Values)
            {
                if (form != null && !form.IsDisposed &&
                    form.HasFocusedTextInput) return true;
            }
            return false;
        }

        internal static string FormatRemaining(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            if (value.TotalDays >= 1)
            {
                int days = (int)value.TotalDays;
                return days + "天" + (value.Hours > 0 ? value.Hours + "小时" : "");
            }
            if (value.TotalHours >= 1)
            {
                int hours = (int)value.TotalHours;
                return hours + "小时" + (value.Minutes > 0 ? value.Minutes + "分钟" : "");
            }
            if (value.TotalMinutes >= 1)
            {
                int minutes = (int)value.TotalMinutes;
                return minutes + "分" + (value.Seconds > 0 ? value.Seconds + "秒" : "");
            }
            return Math.Max(0, (int)Math.Ceiling(value.TotalSeconds)) + "秒";
        }
    }
}
