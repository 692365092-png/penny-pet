using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm : Form, IReminderRuntimeHost, IPetDisplayWindow
    {
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private const int IdleRow = 0;
        private const int HoverRow = 2;
        private const int DragRow = 1;
        private const int GoodbyeRow = 3;
        private const int NotificationRow = 9;
        private const int InputTypingRow = 4;
        private const int ManualTypingRow = 5;
        private const int WaitingRow = 6;
        private const int ThinkingRow = 7;
        private const int ReviewRow = 8;

        private readonly PetSettings _settings;
        private readonly PetArtPackage _art;
        private readonly InteractionRuntime _interaction;
        private readonly PetDisplayRuntime _petDisplay;
        private readonly PetContextMenu _petContextMenu;
        private readonly ContextMenuStrip _menu;
        private readonly ReminderSchedule _reminders;
        private readonly GlobalKeyboardActivity _keyboard;
        private readonly KeyboardPrivacyRuntime _keyboardPrivacy;
        private readonly KeyboardOverlayCoordinator _keyOverlay;
        private readonly ConversationRuntime _conversation;
        private readonly PetWeatherSource _weatherSource;
        private readonly PersistenceRetryRuntime _persistence;
        private readonly PetPersistenceCoordinator _persistenceCoordinator;
        private readonly PetStartupCoordinator _startup;
        private readonly PetWindowLayers _windowLayers;
        private readonly PetBubbleCoordinator _bubbleCoordinator;
        private readonly PetDailyBriefingCoordinator _dailyBriefing;
        private readonly PetSmallTalkCoordinator _smallTalk;
        private readonly DisplayTopologyRuntime _displayTopologyRuntime;

        private StickyFeature _notes;
        private StickyWorkspace _stickyWorkspace;
        private ReminderRuntime _reminderRuntime;
        private int _scalePercent;
        private bool _exiting;
        private bool _disposed;

        internal StickyWorkspace Workspace { get { return _stickyWorkspace; } }

        internal PetForm(PetSettings preloadedSettings = null,
            PetArtPackage preloadedArt = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            _windowLayers = new PetWindowLayers(this);
            _settings = preloadedSettings ?? PetSettings.Load();
            _art = preloadedArt ?? PetArtPackage.Load(CellWidth, CellHeight);
            _interaction = new InteractionRuntime(_art, DateTime.UtcNow);
            _interaction.FrameChanged += RenderCurrentFrame;
            _interaction.HoverChanged += InteractionHoverChanged;
            _displayTopologyRuntime = new DisplayTopologyRuntime(
                new WindowsDisplayTopologyProvider().Capture);
            _displayTopologyRuntime.TopologyChanged += DisplayTopologyChanged;
            _displayTopologyRuntime.CaptureInitial();
            _petDisplay = new PetDisplayRuntime(this, _settings,
                CurrentTopologySnapshot, DisplayDiagnostics.Trace);
            _weatherSource = new PetWeatherSource();
            _conversation = new ConversationRuntime(_settings,
                _weatherSource.GetForecastAsync, ShowConversationMessage);
            _reminders = new ReminderSchedule();
            _keyboard = new GlobalKeyboardActivity();
            _keyboardPrivacy = new KeyboardPrivacyRuntime(_keyboard,
                KeyboardFocusSnapshot.Capture, DateTime.UtcNow);
            _keyOverlay = new KeyboardOverlayCoordinator(this, _settings,
                _keyboardPrivacy, _windowLayers);
            _persistenceCoordinator = new PetPersistenceCoordinator(this);
            _startup = new PetStartupCoordinator(this, DateTime.UtcNow);
            _bubbleCoordinator = new PetBubbleCoordinator(this,
                delegate { return _interaction != null && _interaction.PointerDown; },
                delegate { return _exiting; }, BubbleMessageClosed,
                RestoreAmbientBubble, null, _windowLayers);

            bool persistKeyboardPrivacyReset =
                PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                    _settings.ShowKeyOverlay,
                    _settings.KeyboardPrivacyNoticeAccepted);
            if (persistKeyboardPrivacyReset)
            {
                // Older versions could enable the hook without the explicit
                // first-use notice. Require a fresh opt-in after this upgrade.
                // The dirty-aware startup save persists this together with any
                // other startup normalization; no constructor write is needed.
                _settings.ShowKeyOverlay = false;
            }
            Text = _art.DisplayName;
            _scalePercent = NormalizeScalePercent(_settings.ScalePercent);
            ClientSize = ScaledPetSize(_scalePercent);
            // Always show the compact ordinary idle clip first. The less common
            // long animations are decoded only when they are actually selected.
            // Resolve only the mandatory startup clip before any preload worker.
            _art.PreloadRow(IdleRow);
            BuildRenderedFrameCache();

            // Sticky disk parsing is prepared by the lifecycle owner only
            // after this shell has rendered and become interactive.
            if (!_settings.StartupPreferenceInitialized)
            {
                // Startup is an explicit opt-in. First launch records the safe
                // default; the context-menu action remains the single place
                // that enables the registry entry. Persistence is coalesced by
                // the startup dirty-save phase below.
                _settings.StartupPreferenceInitialized = true;
                _settings.StartAtLogin = false;
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
                    workspace.QueueStickyWindowAction(
                        workspace.ShowStickyManager,
                        "sticky-manager-menu-open");
                });
            };
            menuCommands.ExportNotes = delegate
            {
                if (_notes != null) _ = _persistenceCoordinator.ExportStickyNotesBackup();
            };
            menuCommands.ToggleKeyboard = ToggleKeyOverlay;
            menuCommands.ToggleSilentMode = ToggleSilentMode;
            menuCommands.ToggleStartup = ToggleStartAtLogin;
            menuCommands.ContactAuthor = ShowContactAuthor;
            menuCommands.Exit = BeginExitSequence;
            _petContextMenu = new PetContextMenu(_art.DisplayName,
                _settings.ShowKeyOverlay, _settings.SilentMode,
                _settings.StartAtLogin, menuCommands);
            _menu = _petContextMenu.Menu;
            _interaction.IdleRowProvider = NextIdleRow;
            _interaction.HoverRowProvider = delegate { return HoverRow; };
            _interaction.DragRowProvider = delegate { return DragRow; };
            _interaction.ManualAnimationRowProvider = NextManualAnimationRow;
            _interaction.TransitionRequested += InteractionTransitionRequested;
            _interaction.PointerCaptureRequested += delegate(object sender,
                PointerCaptureRequestedEventArgs e)
            {
                Capture = e.Capture;
            };
            MouseEnter += delegate
            {
                _interaction.MouseEntered(DateTime.UtcNow);
            };
            MouseLeave += delegate
            {
                _interaction.MouseLeft(DateTime.UtcNow);
            };
            MouseDown += PetMouseDown;
            MouseMove += PetMouseMove;
            MouseUp += PetMouseUp;
            FormClosing += PetFormClosing;
            FormClosed += PetFormClosed;
            Shown += PetFormShown;
            _dailyBriefing = new PetDailyBriefingCoordinator(_settings,
                _weatherSource.GetForecastAsync, ShowDailyBriefingMessage,
                SaveDailyBriefingSettings);
            _smallTalk = new PetSmallTalkCoordinator(ShowSmallTalkMessage);
            _persistence = new PersistenceRetryRuntime(
                new IPersistenceRetryTarget[] { _settings });
            _persistence.Notice += _persistenceCoordinator.PersistenceNoticeReceived;
            _startup.ShellReady += PetShellReady;
            _startup.StartupBackgroundReady += PetStartupBackgroundReady;
            _keyboardPrivacy.ActivityChanged += KeyboardPrivacyActivityChanged;
            _keyboardPrivacy.AvailabilityChanged += KeyboardPrivacyAvailabilityChanged;
            _interaction.SetPointerBounds(ClientRectangle);
            RenderCurrentFrame(this, EventArgs.Empty);
        }

        private void PetFormShown(object sender, EventArgs e)
        {
            _startup.ArtReady(_art != null);
        }

        private void PetShellReady(object sender, EventArgs e)
        {
            _startup.EnableRuntimeComposition();
            _startup.BeginDeferredStartupWork();
        }

        private void PetStartupBackgroundReady(object sender, EventArgs e)
        {
            _dailyBriefing.TryShow(DateTime.Now);
        }

        private void PetFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;
            e.Cancel = true;
            BeginExitSequence();
        }

        private void PetFormClosed(object sender, FormClosedEventArgs e)
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _startup.Dispose();
                _persistence.Dispose();
                _conversation.Dispose();
                _dailyBriefing.Dispose();
                _smallTalk.Dispose();
                _keyOverlay.Dispose();
                _keyboardPrivacy.Dispose();
                _keyboard.Dispose();
                _weatherSource.Dispose();
                _displayTopologyRuntime.Dispose();
                _bubbleCoordinator.Dispose();
                _petContextMenu.Dispose();
                _interaction.Dispose();
                _art.Dispose();
                _windowLayers.Dispose();
            }
            base.Dispose(disposing);
        }

        private int NextIdleRow()
        {
            return _art.NextIdleRow();
        }

        private int NextManualAnimationRow()
        {
            return _art.NextManualAnimationRow();
        }

        private void InteractionTransitionRequested(object sender,
            InteractionTransitionRequestedEventArgs e)
        {
            if (e == null) return;
            if (e.Kind == InteractionTransitionKind.BeginGoodbye)
                _interaction.BeginGoodbye(DateTime.UtcNow);
        }

        private void PetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _interaction.PointerDownAt(e.Location, DateTime.UtcNow);
        }

        private void PetMouseMove(object sender, MouseEventArgs e)
        {
            _interaction.PointerMoved(e.Location, DateTime.UtcNow);
        }

        private void PetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _interaction.PointerUpAt(e.Location, DateTime.UtcNow);
        }

        private Size ScaledPetSize(int scalePercent)
        {
            return new Size(
                Math.Max(1, CellWidth * scalePercent / 100),
                Math.Max(1, CellHeight * scalePercent / 100));
        }

        internal static int NormalizeScalePercent(int percent)
        {
            return PetSettingRules.NormalizePetScalePercent(percent);
        }
    }
}
