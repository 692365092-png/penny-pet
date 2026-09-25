using System;
using System.Collections.Generic;
using System.Drawing;
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

        private readonly System.Windows.Forms.Timer _animationTimer;
        private PetPersistenceRuntime _persistence;
        private ReminderRuntime _reminderRuntime;
        private readonly PetBubbleCoordinator _bubbleCoordinator;
        private readonly ConversationRuntime _conversation;
        private readonly PetWeatherSource _weatherSource;
        private readonly PetContextMenu _petContextMenu;
        internal ContextMenuStrip _menu { get { return _petContextMenu.Menu; } }
        private ToolStripMenuItem _statusItem
            { get { return _petContextMenu.StatusItem; } }
        private ToolStripMenuItem _setReminderItem
            { get { return _petContextMenu.SetReminderItem; } }
        private ToolStripMenuItem _cancelItem
            { get { return _petContextMenu.CancelItem; } }
        private ToolStripMenuItem _manageNotesItem
            { get { return _petContextMenu.ManageNotesItem; } }
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
        internal readonly ReminderSchedule _reminders;
        private readonly PetSettings _settings;
        private readonly GlobalKeyboardActivity _keyboard;
        private readonly KeyboardOverlayForm _keyOverlay;
        internal readonly PetWindowLayerCoordinator _windowLayers =
            new PetWindowLayerCoordinator();
        private StickyFeature _notes;
        private StickyWorkspace _stickyWorkspace;
        private readonly object _keyboardQueueGate = new object();
        private readonly InteractionRuntime _interaction;
        internal readonly HashSet<string> _expectedFirstRenderNoteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> _renderedFirstRenderNoteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private PetArtPackage _art;
        private Bitmap[][] _renderedFrames;
        private bool _renderedFramesOwnBitmaps;
        private Size _renderedTargetSize;
        private ContactAuthorForm _contactAuthorForm;
        private DisplayTopologyRuntime _displayTopologyRuntime;
        private PetDisplayRuntime _petDisplay;
        internal bool _exiting;
        private int _scalePercent = 100;
        private KeyboardInputEventArgs _latestKeyboardEvent;
        private bool _keyboardUiDispatchQueued;
        private readonly KeyboardPrivacyWorker _keyboardPrivacy;
        private System.Windows.Forms.Timer _startupWorkTimer;
        private StartupWorkPhase _startupWorkPhase;
        private Queue<StickyNoteData> _startupVisibleNotes;
        private bool _startupBackgroundReady;
        private bool _startupArtReady;
        private bool _shellReadyRaised;
        // The loading window is the only startup visual. Keep the layered pet
        // window alive for initialization, but do not publish one of its
        // frames until the restored notes and the first idle frame are ready.
        // Optional interaction rows continue warming after startup.
        private bool _startupDisplaySuppressed = true;

        internal event EventHandler ShellReady;\n        internal event EventHandler StartupBackgroundReady;

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
            _bubbleCoordinator = new PetBubbleCoordinator(this,
                delegate { return _interaction != null && _interaction.PointerDown; },
                delegate { return _exiting; }, BubbleMessageClosed,
                RestoreAmbientBubble, null, _windowLayers);

            _settings = preloadedSettings ?? PetSettings.Load();
            _petDisplay = new PetDisplayRuntime(this, _settings,
                CurrentTopologySnapshot, DisplayDiagnostics.Trace);
            _weatherSource = new PetWeatherSource();
            _conversation = new ConversationRuntime(_settings,
                _weatherSource.GetForecastAsync, ShowConversationMessage);
            bool persistKeyboardPrivacyReset =
                PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                    _settings.ShowKeyOverlay,
                    _settings.KeyboardPrivacyNoticeAccepted);
            if (persistKeyboardPrivacyReset)
            {
                // Older versions could enable the hook without the explicit
                // first-use notice. Require a fresh opt-in after this upgrade.
                _settings.ShowKeyOverlay = false;
            }
            _art = PetArtPackage.Load(CellWidth, CellHeight);
            Text = _art.DisplayName;
            _scalePercent = NormalizeScalePercent(_settings.ScalePercent);
            ClientSize = ScaledPetSize(_scalePercent);
            // Always show the compact ordinary idle clip first. The less common
            // long animations are decoded only when they are actually selected.
            // Resolve only the mandatory startup clip before any preload worker.
            _art.PreloadRow(IdleRow);
            _interaction = new InteractionRuntime(this, DateTime.UtcNow);
            _interaction.FrameChanged += RenderCurrentFrame;
            _interaction.HoverChanged += InteractionHoverChanged;
            BuildRenderedFrameCache();

            _reminders = new ReminderSchedule();
            // Sticky disk parsing is prepared by PennyApplicationHost only
            // after this shell has rendered and become interactive.
            if (persistKeyboardPrivacyReset) _settings.SaveAsync();
            if (!_settings.StartupPreferenceInitialized)
            {
                // Startup is an explicit opt-in. First launch records the safe
                // default; the context-menu action remains the single place
                // that enables the registry entry.
                _settings.StartupPreferenceInitialized = true;
                _settings.StartAtLogin = false;
                _settings.SaveAsync();
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
                if (!_exiting &&
                    !PetHoverStabilityRules.ShouldSuppressHover(
                        _interaction.StableMouseInside, _menu.Visible, _interaction.PointerDown,
                        _settings.SilentMode,
                        _interaction.HoverSuppressed))
                    ShowOrUpdateHoverBubble();
            };
            menuCommands.ShowReminder = delegate
            {
                RunWhenReminderRuntimeReady(ShowReminderDialog);
            };
            menuCommands.CreateNote = delegate
            {
                RunWhenStickyRuntimeReady(delegate(StickyWorkspace workspace)
                {
                    workspace.QueueStickyWindowAction(delegate
                    {
                        workspace.CreateStickyNote(String.Empty);
                    }, "sticky-note-menu-create");
                });
            };
            menuCommands.CreateTodo = delegate
            {
                RunWhenStickyRuntimeReady(delegate(StickyWorkspace workspace)
                {
                    workspace.QueueStickyWindowAction(
                        workspace.CreateTodoStickyNote,
                        "sticky-todo-menu-create");
                });
            };
            menuCommands.CreateSchedule = delegate
            {
                RunWhenStickyRuntimeReady(delegate(StickyWorkspace workspace)
                {
                    workspace.QueueStickyWindowAction(
                        workspace.CreateScheduleStickyNote,
                        "sticky-schedule-menu-create");
                });
            };
            menuCommands.ManageNotes = delegate
            {
                RunWhenStickyRuntimeReady(delegate(StickyWorkspace workspace)
                {
                    workspace.ShowStickyNotesManager();
                });
            };
            menuCommands.TileAllNotes = delegate
            {
                RunWhenStickyRuntimeReady(delegate(StickyWorkspace workspace)
                {
                    workspace.QueueStickyWindowAction(
                        workspace.Dock.ExpandAndTileAllStickyNotesToPetScreen,
                        "sticky-menu-expand-and-tile");
                });
            };
            menuCommands.ShowDailyContentSettings =
                ShowDailyContentSettingsDialog;
            menuCommands.ShowScale = ShowScaleDialog;
            menuCommands.StartupClick = StartupItemClick;
            menuCommands.KeyboardClick = KeyboardItemClick;
            menuCommands.SilentClick = SilentItemClick;
            menuCommands.ContactAuthor = ShowContactAuthor;
            menuCommands.Exit = BeginExitSequence;
            _petContextMenu = new PetContextMenu(_art.DisplayName,
                _settings.StartAtLogin, _settings.ShowKeyOverlay,
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
            _animationTimer.Start();
            MouseDown += PetMouseDown;
            MouseMove += PetMouseMove;
            MouseUp += PetMouseUp;
            MouseCaptureChanged += PetMouseCaptureChanged;
            MouseEnter += delegate { OnRawMouseEnter(); };
            MouseLeave += delegate { OnRawMouseLeave(); };
            LocationChanged += delegate
            {
                if (_petDpiDragHandoffActive) return;
                if (_stickyWorkspace != null)
                    _stickyWorkspace.PositionNoteTabs();
                RepositionCurrentBubble();
            };
            SizeChanged += delegate
            {
                if (_petDpiDragHandoffActive) return;
                if (_stickyWorkspace != null)
                    _stickyWorkspace.PositionNoteTabs();
            };

            _keyOverlay = new KeyboardOverlayForm(_settings.KeyOverlayScalePercent);
            _windowLayers.LayerChanged += PetWindowLayerChanged;
            _keyboard = new GlobalKeyboardActivity();
            _keyboard.Activity += KeyboardActivity;
            _keyboard.FocusChanged += KeyboardFocusChanged;
            _keyboardPrivacy = new KeyboardPrivacyWorker(
                snapshot => !SensitiveInputDetector.IsSensitiveFocus(snapshot),
                snapshot => snapshot.StillMatchesCurrentTarget(),
                action => BeginInvoke((MethodInvoker)(() => action())),
                PublishCheckedKeyboardInput);
            _keyboardPrivacy.SetEnabled(_settings.ShowKeyOverlay);
            RefreshKeyboardMenuText();
            _displayTopologyRuntime = new DisplayTopologyRuntime(
                delegate { return new WindowsDisplayTopologyProvider().Capture(); });
            _displayTopologyRuntime.TopologyChanged +=
                DisplayTopologyChanged;
            _displayTopologyRuntime.CaptureInitial();

            Shown += delegate
            {
                _petDisplay.Initialize();

                // Shell readiness is intentionally independent of Sticky disk
                // parsing and first-render acknowledgements.
                _startupArtReady = _art.IsRowLoaded(IdleRow);
                TryRaiseShellReady();
                QueueStartupInteractionPreload();
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

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            if (e == null || IsDisposed || Disposing || _petDisplay == null)
            {
                base.OnDpiChanged(e);
                return;
            }

            bool activeDrag = _interaction != null && _interaction.PointerDown && IsHandleCreated &&
                Handle != IntPtr.Zero;

            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            PhysicalPoint oldTopLeft = new PhysicalPoint
            {
                X = Left,
                Y = Top
            };
            PhysicalPoint oldCursor = new PhysicalPoint();
            int oldDpi = Math.Max(1, e.DeviceDpiOld);

            if (activeDrag)
            {
                WindowFacts before = CapturePetWindowFacts(topology);
                if (before != null)
                {
                    oldTopLeft = new PhysicalPoint
                    {
                        X = before.PhysicalBounds.Left,
                        Y = before.PhysicalBounds.Top
                    };
                    if (before.Dpi > 0) oldDpi = before.Dpi;
                }
                Point cursor = Cursor.Position;
                oldCursor = new PhysicalPoint
                {
                    X = cursor.X,
                    Y = cursor.Y
                };
                _petDpiDragHandoffActive = true;
            }

            int actualDpi = 0;
            try
            {
                base.OnDpiChanged(e);

                actualDpi = ActualPetDpi(e.DeviceDpiNew);
                ApplyCurrentDisplayScale(actualDpi);

                if (activeDrag)
                {
                    Point cursor = Cursor.Position;
                    PhysicalPoint newCursor = new PhysicalPoint
                    {
                        X = cursor.X,
                        Y = cursor.Y
                    };
                    PhysicalPoint rebased =
                        PetPlacementPolicy.RebaseActiveDragTopLeft(
                            oldTopLeft, oldCursor, newCursor,
                            oldDpi, actualDpi);

                    _petDisplay.MoveForDpiHandoff(rebased.X, rebased.Y);

                    WindowFacts afterRebase =
                        CapturePetWindowFacts(topology);
                    Point actualTopLeft = afterRebase != null
                        ? new Point(afterRebase.PhysicalBounds.Left,
                            afterRebase.PhysicalBounds.Top)
                        : new Point(rebased.X, rebased.Y);

                    _interaction.RebasePointer(new Point(
                        newCursor.X, newCursor.Y), actualTopLeft);

                    DisplayDiagnostics.Trace("PetDragDpiHandoff",
                        "oldDpi=" + oldDpi +
                        " newDpi=" + actualDpi +
                        " oldTop=(" + oldTopLeft.X + "," +
                        oldTopLeft.Y + ")" +
                        " oldCursor=(" + oldCursor.X + "," +
                        oldCursor.Y + ")" +
                        " newCursor=(" + newCursor.X + "," +
                        newCursor.Y + ")" +
                        " requested=(" + rebased.X + "," +
                        rebased.Y + ")" +
                        " actual=(" + actualTopLeft.X + "," +
                        actualTopLeft.Y + ")");
                }

                WindowFacts facts = CapturePetWindowFacts(topology);
                DisplayDiagnostics.Trace("WindowDpiChanged",
                    "window=pet eventOldDpi=" + e.DeviceDpiOld +
                    " eventDpi=" + e.DeviceDpiNew +
                    " actualDpi=" + actualDpi +
                    " topology=" +
                    (topology == null ? -1 : topology.Generation) +
                    " drag=" + (activeDrag ? "1" : "0"));
            }
            finally
            {
                if (activeDrag)
                    _petDpiDragHandoffActive = false;
            }

            _stickyWorkspace.PositionNoteTabs();
            RepositionCurrentBubble();
        }

        private void ApplyCurrentDisplayScale(int dpi)
        {
            if (_art == null || _renderedFrames == null) return;
            int safeDpi = Math.Max(96, dpi);
            Size logical = ScaledPetSize(_scalePercent);
            Size physical = ScaleForDpi(logical, safeDpi);
            _renderedTargetSize = physical;
            ClientSize = physical;
            DisposeRenderedFrameCache();
            BuildRenderedFrameCache(physical);
            if (!_startupDisplaySuppressed) RenderCurrentFrame();
        }

        private static Size ScaleForDpi(Size size, int dpi)
        {
            int safeDpi = Math.Max(96, dpi);
            return new Size(Math.Max(1, size.Width * safeDpi / 96),
                Math.Max(1, size.Height * safeDpi / 96));
        }

        protected override void WndProc(ref Message message)
        {
            const int WmSettingChange = 0x001A;
            const int WmDisplayChange = 0x007E;
            const int WmDeviceChange = 0x0219;
            base.WndProc(ref message);
            if (IsHandleCreated && !IsDisposed && !Disposing &&
                _displayTopologyRuntime != null)
            {
                if (message.Msg == WmDisplayChange)
                    _displayTopologyRuntime.NotifyPotentialChange(
                        "WM_DISPLAYCHANGE");
                else if (message.Msg == WmSettingChange)
                    _displayTopologyRuntime.NotifyPotentialChange(
                        "WM_SETTINGCHANGE");
                else if (message.Msg == WmDeviceChange)
                    _displayTopologyRuntime.NotifyPotentialChange(
                        "WM_DEVICECHANGE");
            }
        }

        private void DisplayTopologyChanged(string reason,
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || _displayTopologyRuntime == null) return;
            StringBuilder details = new StringBuilder();
            details.Append("generation=")
                .Append(_displayTopologyRuntime.Generation)
                .Append(" reason=").Append(reason ?? String.Empty)
                .Append(" surfaces=").Append(snapshot.Surfaces.Count);
            foreach (DisplaySurfaceSnapshot surface in snapshot.Surfaces)
            {
                details.Append(" | ")
                    .Append(surface.RuntimeGdiName)
                    .Append(" b=(").Append(surface.Bounds.Left).Append(",")
                    .Append(surface.Bounds.Top).Append(",")
                    .Append(surface.Bounds.Width).Append(",")
                    .Append(surface.Bounds.Height).Append(")")
                    .Append(" primary=").Append(surface.IsPrimary ? "1" : "0");
            }
            DisplayDiagnostics.Trace("TopologyCaptured",
                details.ToString());
            if (!String.Equals(reason, "initial",
                StringComparison.Ordinal))
                DisplayDiagnostics.Trace("TopologyChanged",
                    details.ToString());
            if (_stickyWorkspace != null)
                _stickyWorkspace.Host.SetCurrentTopology(snapshot);

            // Pet is upstream of SideTabs and remains independently live while
            // Sticky runtime is still preparing.
            _petDisplay.Reconcile(snapshot, reason);

            if (_stickyWorkspace != null)
            {
                _stickyWorkspace.HandleStickyTopologyChanged(snapshot);
                _stickyWorkspace.PositionNoteTabs();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || _exiting) return;
            e.Cancel = true;
            BeginExitSequence();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_stickyWorkspace != null) _stickyWorkspace.Dispose();
            if (_displayTopologyRuntime != null)
            {
                _displayTopologyRuntime.TopologyChanged -=
                    DisplayTopologyChanged;
                _displayTopologyRuntime.Dispose();
                _displayTopologyRuntime = null;
            }
            if (_persistence != null)
            {
                _persistence.Notice -= PersistenceNoticeReceived;
                _persistence.Dispose();
            }
            _keyboardPrivacy.Dispose();
            _keyboard.FocusChanged -= KeyboardFocusChanged;
            _keyboard.Activity -= KeyboardActivity;
            _keyboard.Dispose();
            _windowLayers.LayerChanged -= PetWindowLayerChanged;
            _keyOverlay.Dispose();
            _interaction.Stop();
            _bubbleCoordinator.Dispose();
            _weatherSource.Dispose();
            if (_contactAuthorForm != null && !_contactAuthorForm.IsDisposed)
                _contactAuthorForm.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            if (_appIcon != null) _appIcon.Dispose();
            _menu.Dispose();
            _animationTimer.Dispose();
            if (_reminderRuntime != null) _reminderRuntime.Dispose();
            _conversation.Stop();
            StopDeferredStartupWork();
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
                return new Point(_settings.X, _settings.Y);

            // Only a pre-HWND bootstrap. Real DRT-12 placement happens after
            // the current topology and live Pet HWND are both available.
            return Point.Empty;
        }

        private bool IsVisible(Point location)
        {
            return _petDisplay.IsVisible(new PhysicalRect(location.X, location.Y,
                ClientSize.Width, ClientSize.Height), CurrentTopologySnapshot());
        }

        private void EnsureVisible()
        {
            _petDisplay.EnsureVisible();
        }

        private void KeepFullyVisible()
        {
            _petDisplay.KeepFullyVisible();
        }

        private void SaveLocation()
        {
            CaptureLocationForSave();
            _settings.SaveAsync();
        }

        private void CaptureLocationForSave()
        {
            _petDisplay.CaptureForSave();
        }

    }
}
