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

[assembly: AssemblyTitle("Penny pet")]
[assembly: AssemblyDescription("支持动画、便利贴、待办、日程、提醒与按键显示的桌面宠物")]
[assembly: AssemblyProduct("Penny pet")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.CompilerServices.SuppressIldasm]

namespace PennyPet
{
    internal static class Program
    {
        private static Mutex _singleInstance;
        private static System.Windows.Application _wpfApplication;

        [STAThread]
        private static void Main(string[] args)
        {
            // Compatibility-test build: keep WPF sticky-note rendering away
            // from GPU/driver-specific layered-window paths.  The animated pet
            // itself remains on the existing WinForms renderer.
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            bool stickyKeyboardDemo = HasArgument(args, "--sticky-keyboard-demo");
            bool stickyKeyboardHostDemo = HasArgument(args,
                "--sticky-keyboard-host-demo");
            bool stickyTodoDemo = HasArgument(args, "--sticky-todo-demo");
            if (stickyKeyboardDemo || stickyKeyboardHostDemo || stickyTodoDemo)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StickyNoteData demo = new StickyNoteData();
                demo.Title = stickyTodoDemo ? "待办字号实测" : "多语言与回车实测";
                demo.Text = String.Empty;
                demo.IsTodoList = stickyTodoDemo;
                if (stickyTodoDemo)
                {
                    demo.TodoItems.Add(new StickyTodoItem("双击我可以编辑", false));
                    demo.TodoItems.Add(new StickyTodoItem("整体字号由上方选择", true));
                }
                demo.X = 420;
                demo.Y = 210;
                demo.Width = 520;
                demo.Height = 360;
                demo.BackgroundOpacityPercent = 90;
                using (StickyNoteForm note = new StickyNoteForm(demo, false, true))
                {
                    note.Title = stickyTodoDemo
                        ? "Penny 待办字号实测" : "Penny 多语言键盘实测";
                    note.Shown += delegate
                    {
                        note.BeginInvoke((MethodInvoker)delegate
                        {
                            note.FocusPrimaryInputForTest();
                        });
                    };
                    if (stickyKeyboardHostDemo)
                    {
                        // Exercise the same WinForms-owned message pump and
                        // modeless WPF keyboard bridge used by the real pet.
                        EnsureWpfApplicationForStickyNotes();
                        note.EnableWinFormsKeyboardInterop();
                        note.Closed += delegate { Application.ExitThread(); };
                        note.Show();
                        Application.Run();
                    }
                    else
                    {
                        System.Windows.Application wpfApplication =
                            new System.Windows.Application();
                        wpfApplication.ShutdownMode =
                            System.Windows.ShutdownMode.OnMainWindowClose;
                        wpfApplication.MainWindow = note;
                        note.Show();
                        wpfApplication.Run();
                    }
                }
                return;
            }
            if (HasArgument(args, "--sticky-appearance-demo"))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StickyNoteData demo = new StickyNoteData();
                demo.Title = "颜色与透明度预览";
                demo.Text = "这段文字始终保持完全不透明。\r\n可以点击正文继续输入。";
                demo.X = 360;
                demo.Y = 210;
                demo.Width = 720;
                demo.Height = 400;
                demo.BackgroundOpacityPercent = 60;
                using (StickyNoteForm note = new StickyNoteForm(demo, true))
                {
                    note.Shown += delegate
                    {
                        note.BeginInvoke((MethodInvoker)delegate
                        {
                            note.OpenAppearanceDialogForTest();
                        });
                    };
                    System.Windows.Application wpfApplication =
                        new System.Windows.Application();
                    wpfApplication.ShutdownMode =
                        System.Windows.ShutdownMode.OnMainWindowClose;
                    wpfApplication.MainWindow = note;
                    note.Show();
                    wpfApplication.Run();
                }
                return;
            }
            string renderStickyPreviewPath = ArgumentValue(args,
                "--render-sticky-preview=");
            if (!String.IsNullOrEmpty(renderStickyPreviewPath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { SelfTest.RenderStickyPreview(renderStickyPreviewPath); }
                catch (Exception error)
                {
                    try
                    {
                        File.WriteAllText(renderStickyPreviewPath + ".error.txt",
                            error.GetType().FullName + Environment.NewLine +
                            (error.Message ?? String.Empty) + Environment.NewLine +
                            (error.StackTrace ?? String.Empty), Encoding.UTF8);
                    }
                    catch { }
                    throw;
                }
                return;
            }
            string renderSchedulePreviewPath = ArgumentValue(args,
                "--render-schedule-preview=");
            if (!String.IsNullOrEmpty(renderSchedulePreviewPath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { SelfTest.RenderSchedulePreview(renderSchedulePreviewPath); }
                catch (Exception error)
                {
                    try
                    {
                        File.WriteAllText(renderSchedulePreviewPath + ".error.txt",
                            error.GetType().FullName + Environment.NewLine +
                            error.Message, Encoding.UTF8);
                    }
                    catch { }
                }
                return;
            }
            string renderStickyAppearancePath = ArgumentValue(args,
                "--render-sticky-appearance-preview=");
            if (!String.IsNullOrEmpty(renderStickyAppearancePath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    SelfTest.RenderStickyAppearancePreview(
                        renderStickyAppearancePath);
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "sticky-appearance-preview", error);
                    try
                    {
                        File.WriteAllText(renderStickyAppearancePath +
                            ".error.txt", error.ToString(), Encoding.UTF8);
                    }
                    catch { }
                    Environment.ExitCode = 1;
                }
                return;
            }
            string renderHoverPreviewPath = ArgumentValue(args, "--render-hover-preview=");
            if (!String.IsNullOrEmpty(renderHoverPreviewPath))
            {
                SelfTest.RenderHoverBubblePreview(renderHoverPreviewPath);
                return;
            }
            string renderPreviewPath = ArgumentValue(args, "--render-preview=");
            if (!String.IsNullOrEmpty(renderPreviewPath))
            {
                SelfTest.RenderPreview(renderPreviewPath);
                return;
            }
            string renderFeaturePreviewPath = ArgumentValue(args,
                "--render-feature-preview=");
            if (!String.IsNullOrEmpty(renderFeaturePreviewPath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RenderFeaturePreview(renderFeaturePreviewPath);
                return;
            }
            string renderReminderPreviewPath = ArgumentValue(args,
                "--render-reminder-preview=");
            if (!String.IsNullOrEmpty(renderReminderPreviewPath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RenderReminderPreview(renderReminderPreviewPath);
                return;
            }
            string renderContactPreviewPath = ArgumentValue(args,
                "--render-contact-preview=");
            if (!String.IsNullOrEmpty(renderContactPreviewPath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RenderContactAuthorPreview(renderContactPreviewPath);
                return;
            }
            string selfTestPath = ArgumentValue(args, "--self-test=");
            if (!String.IsNullOrEmpty(selfTestPath))
            {
                SelfTest.Run(selfTestPath);
                return;
            }
            string stickyInputProbePath = ArgumentValue(args,
                "--sticky-input-probe=");
            if (!String.IsNullOrEmpty(stickyInputProbePath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RunStickyInputProbe(stickyInputProbePath);
                return;
            }
            string stickyPumpProbePath = ArgumentValue(args,
                "--sticky-pump-probe=");
            if (!String.IsNullOrEmpty(stickyPumpProbePath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RunStickyWinFormsPumpProbe(stickyPumpProbePath);
                return;
            }
            string stickyTransparencyProbePath = ArgumentValue(args,
                "--sticky-transparency-probe=");
            if (!String.IsNullOrEmpty(stickyTransparencyProbePath))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SelfTest.RunStickyTransparencyOverlapProbe(
                    stickyTransparencyProbePath);
                return;
            }
            string startupProbePath = ArgumentValue(args, "--startup-probe=");
            if (!String.IsNullOrEmpty(startupProbePath))
            {
                SelfTest.RunStartupProbe(startupProbePath);
                return;
            }
            string startupCachePath = ArgumentValue(args,
                "--write-startup-cache=");
            if (!String.IsNullOrEmpty(startupCachePath))
            {
                PetArtPackage.WriteStartupCache(192, 208, startupCachePath);
                return;
            }
            string releasePackPath = ArgumentValue(args,
                "--write-release-pack=");
            if (!String.IsNullOrEmpty(releasePackPath))
            {
                PetArtPackage.WriteReleasePack(192, 208, releasePackPath);
                return;
            }
            string validateArtPath = ArgumentValue(args, "--validate-art=");
            if (!String.IsNullOrEmpty(validateArtPath))
            {
                PetArtPackage.WriteValidationReport(192, 208, validateArtPath);
                return;
            }

            bool createdNew;
            _singleInstance = new Mutex(true, "Local\\PennyPet.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Penny pet 已经在桌面上啦。", "Penny pet");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationDiagnostics.Initialize();
            try
            {
                PetSettings preloadedSettings = PetSettings.Load();
                using (StartupLoadingForm loading = new StartupLoadingForm(
                    preloadedSettings))
                {
                    loading.Show();
                    Application.DoEvents();
                    PetForm pet = new PetForm(preloadedSettings);
                    pet.StartupReady += delegate
                    {
                        if (!loading.IsDisposed) loading.Close();
                    };
                    pet.FormClosed += delegate
                    {
                        if (!loading.IsDisposed) loading.Close();
                    };
                    pet.Show();
                    Application.DoEvents();
                    loading.BringToFront();
                    Application.Run(pet);
                }
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportFatal("application-run", error);
                MessageBox.Show(
                    "Penny pet 启动失败。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            try
            {
                if (_wpfApplication != null) _wpfApplication.Shutdown();
            }
            catch { }
            GC.KeepAlive(_singleInstance);
        }

        internal static void EnsureWpfApplicationForStickyNotes()
        {
            if (System.Windows.Application.Current != null) return;
            _wpfApplication = new System.Windows.Application();
            _wpfApplication.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;
            _wpfApplication.DispatcherUnhandledException += delegate(object sender,
                System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                ApplicationDiagnostics.ReportFatal("wpf-dispatcher", e.Exception);
            };
        }

        private static bool HasArgument(string[] args, string expected)
        {
            if (args == null) return false;
            foreach (string argument in args)
            {
                if (String.Equals(argument, expected,
                    StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string ArgumentValue(string[] args, string prefix)
        {
            if (args == null) return null;
            foreach (string arg in args)
            {
                if (arg != null && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length).Trim('"');
            }
            return null;
        }

    }

    internal sealed class ArtPreloadReservations
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private readonly HashSet<int> _active = new HashSet<int>();
        private readonly Dictionary<int, DateTime> _retryAfterUtc =
            new Dictionary<int, DateTime>();

        internal bool TryReserve(int row, bool alreadyLoaded, DateTime nowUtc)
        {
            lock (_active)
            {
                if (alreadyLoaded)
                {
                    _active.Remove(row);
                    _retryAfterUtc.Remove(row);
                    return false;
                }
                DateTime retryAfter;
                if (_active.Contains(row) ||
                    (_retryAfterUtc.TryGetValue(row, out retryAfter) &&
                    nowUtc < retryAfter)) return false;
                _active.Add(row);
                return true;
            }
        }

        internal void Complete(int row, bool loaded, DateTime nowUtc)
        {
            lock (_active)
            {
                _active.Remove(row);
                if (loaded) _retryAfterUtc.Remove(row);
                else _retryAfterUtc[row] = nowUtc.Add(RetryDelay);
            }
        }
    }

    internal sealed class PetForm : Form
    {
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private const int IdleRow = 0;
        private const int RightRow = 1;
        private const int LeftRow = 2;
        private const int WavingRow = 3;
        private const int HoverRow = 4;
        private const int FailedRow = 5;
        private const int WaitingRow = 6;
        private const int ThinkingRow = 7;
        private const int ReviewRow = 8;
        private const int NotificationRow = 9;
        // Two long thought clips share a combined 10% idle probability.
        private const int IdleThoughtProbabilityDenominator = 20;
        private const int GuitarFailureProbabilityDenominator = 6;
        private const int ManualAnimationCooldownMilliseconds = 600;
        private const int DragClickThresholdPixels = 6;
        // Zero means an at-time reminder stays until the bubble itself is
        // clicked or another application message replaces it.
        private const int ReminderBubbleDurationMilliseconds = 0;
        private static readonly int[] ManualAnimationRows =
            { IdleRow, HoverRow, FailedRow, WaitingRow, ThinkingRow, ReviewRow,
                NotificationRow };

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
        private long _lastReminderBannerSecond = Int64.MinValue;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _setReminderItem;
        private readonly ToolStripMenuItem _cancelItem;
        private readonly ToolStripMenuItem _newNoteItem;
        private readonly ToolStripMenuItem _newTodoItem;
        private readonly ToolStripMenuItem _newScheduleItem;
        private readonly ToolStripMenuItem _manageNotesItem;
        private readonly ToolStripMenuItem _collapseNotesItem;
        private readonly ToolStripMenuItem _expandTabsItem;
        private readonly ToolStripMenuItem _recoverWindowsItem;
        private readonly ToolStripMenuItem _scaleItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _keyboardItem;
        private readonly ToolStripMenuItem _silentItem;
        private readonly ToolStripMenuItem _contactAuthorItem;
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

        private PetArtPackage _art;
        private Bitmap[][] _renderedFrames;
        private bool _renderedFramesOwnBitmaps;
        private SpeechBubbleForm _bubble;
        private ContactAuthorForm _contactAuthorForm;
        private bool _bubbleIsHover;
        private bool _bubbleIsPreAlert;
        private bool _bubbleIsDueReminder;
        private ReminderItem _preAlertItem;
        private bool _suppressHoverRestore;
        private int _row;
        private int _frame;
        private bool _dragging;
        private bool _dragMoved;
        private bool _mouseInside;
        private Point _dragMouseOrigin;
        private Point _dragWindowOrigin;
        private bool _typingSession;
        private int _typingRow = ThinkingRow;
        private int _idleRow = IdleRow;
        private DateTime _typingUntilUtc;
        private bool _reminderAttentionActive;
        private int _reminderAnimationGeneration;
        private DateTime _nextFrameUtc;
        private DateTime _manualAnimationCooldownUntilUtc;
        private bool _manualAnimationActive;
        private int _manualAnimationRow = -1;
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

            _statusItem = new ToolStripMenuItem("当前没有提醒");
            _statusItem.Enabled = false;
            _setReminderItem = new ToolStripMenuItem("添加提醒…");
            _setReminderItem.Click += delegate { ShowReminderDialog(); };
            _cancelItem = new ToolStripMenuItem("取消提醒");
            _newNoteItem = new ToolStripMenuItem("新建便利贴");
            _newNoteItem.Click += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-note-menu-create");
            };
            _newTodoItem = new ToolStripMenuItem("新建待办清单");
            _newTodoItem.Click += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateTodoStickyNote();
                }, "sticky-todo-menu-create");
            };
            _newScheduleItem = new ToolStripMenuItem("新建日程");
            // The collapsed tab carries the type icon. Keep the pet menu
            // command itself text-only.
            _newScheduleItem.Image = null;
            _newScheduleItem.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _newScheduleItem.Click += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateScheduleStickyNote();
                }, "sticky-schedule-menu-create");
            };
            _manageNotesItem = new ToolStripMenuItem("便利贴管理…");
            _manageNotesItem.Click += delegate { ShowStickyNotesManager(); };
            _collapseNotesItem = new ToolStripMenuItem("收起全部便利贴到页签");
            _collapseNotesItem.Click += delegate { CollapseAllStickyNotes(); };
            _expandTabsItem = new ToolStripMenuItem("展开全部侧边页签");
            _expandTabsItem.Click += delegate { ExpandAllStickyNoteTabs(); };
            _recoverWindowsItem = new ToolStripMenuItem(
                "将已展开的便利贴集中到此屏幕");
            _recoverWindowsItem.Click += delegate
            {
                QueueStickyWindowAction(MoveVisibleStickyNotesToPetScreen,
                    "sticky-window-screen-recovery");
            };
            _scaleItem = new ToolStripMenuItem("调整桌宠大小…");
            _scaleItem.Click += delegate { ShowScaleDialog(); };
            _startupItem = new ToolStripMenuItem("开机自动启动");
            _startupItem.CheckOnClick = true;
            _startupItem.Checked = _settings.StartWithWindows;
            _startupItem.Click += StartupItemClick;
            _keyboardItem = new ToolStripMenuItem("按键显示：正在检查");
            _keyboardItem.CheckOnClick = true;
            _keyboardItem.Checked = _settings.ShowKeyOverlay;
            _keyboardItem.Click += KeyboardItemClick;
            _silentItem = new ToolStripMenuItem("静默模式（隐藏日常气泡）");
            _silentItem.CheckOnClick = true;
            _silentItem.Checked = _settings.SilentMode;
            _silentItem.Click += SilentItemClick;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出" + _art.DisplayName);
            exitItem.Click += delegate { BeginExitSequence(); };
            _contactAuthorItem = new ToolStripMenuItem("联系作者");
            _contactAuthorItem.Click += delegate { ShowContactAuthor(); };

            _menu = new ContextMenuStrip();
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_newNoteItem);
            _menu.Items.Add(_newTodoItem);
            _menu.Items.Add(_newScheduleItem);
            _menu.Items.Add(_manageNotesItem);
            _menu.Items.Add(_collapseNotesItem);
            _menu.Items.Add(_expandTabsItem);
            _menu.Items.Add(_recoverWindowsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_setReminderItem);
            _menu.Items.Add(_cancelItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_scaleItem);
            _menu.Items.Add(_keyboardItem);
            _menu.Items.Add(_silentItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_contactAuthorItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _menu.Opening += delegate
            {
                HideHoverBubble();
                RefreshMenuText();
            };
            _menu.Closed += delegate
            {
                if (_mouseInside && !_exiting) ShowOrUpdateHoverBubble();
            };
            ContextMenuStrip = _menu;

            _trayIcon = new NotifyIcon();
            _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _trayIcon.Icon = _appIcon ?? SystemIcons.Application;
            _trayIcon.Text = _art.DisplayName.Length > 63
                ? _art.DisplayName.Substring(0, 63) : _art.DisplayName;
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _menu;
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

        private void RestoreReminders()
        {
            try
            {
                DateTime launchedUtc = DateTime.UtcNow;
                List<ReminderItem> future = new List<ReminderItem>();
                foreach (ReminderItem item in _settings.Reminders)
                    if (ShouldRestoreReminderAfterLaunch(item, launchedUtc))
                        future.Add(item);
                _reminders.Restore(future);
                if (future.Count != _settings.Reminders.Count)
                {
                    _settings.SetReminders(_reminders.GetItems());
                    _settings.Save();
                }
            }
            catch
            {
                _reminders.Cancel();
                _settings.SetReminders(_reminders.GetItems());
                _settings.Save();
            }
        }

        private void SaveReminders()
        {
            _settings.SetReminders(_reminders.GetItems());
            _settings.Save();
            UpdateAllStickyNoteReminderBanners();
        }

        private void UpdateAllStickyNoteReminderBanners()
        {
            List<ReminderItem> reminders = _reminders.GetItems();
            foreach (StickyNoteForm form in _noteWindows.Values)
            {
                if (!form.IsDisposed) form.UpdateReminderBanner(reminders);
            }
        }

        private void ReconcileNoteReminders()
        {
            bool changed = false;
            foreach (StickyNoteData note in _notes.GetAll())
            {
                ReminderItem linked = _reminders.FindBySourceNoteId(note.Id);
                long nextTicks = linked == null ? 0 : linked.DeadlineUtc.Ticks;
                if (note.ReminderUtcTicks == nextTicks) continue;
                note.ReminderUtcTicks = nextTicks;
                changed = true;
            }
            if (changed) _notes.Save();
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
                    StickyNoteForm form = GetOrCreateStickyNoteWindow(member);
                    form.ShowRestoredDocked(layout[index]);
                    form.EnableWinFormsKeyboardInterop();
                }
            }
            finally { _synchronizingDockLayout = false; }
            StickyNoteForm focusForm;
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
            StickyNoteForm source = sender as StickyNoteForm;
            if (source == null || source.IsDisposed) return;
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(source.Data);
            if (component.Count == 0)
                component = BuildDockComponent(source.Data);
            bool alwaysOnTop = source.Data.AlwaysOnTop;
            foreach (StickyNoteData note in component)
            {
                note.AlwaysOnTop = alwaysOnTop;
                StickyNoteForm member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed)
                    member.ApplyGroupTopMost(alwaysOnTop);
            }
            _notes.Save();
        }

        private void StickyNoteCloseRequested(object sender, EventArgs e)
        {
            StickyNoteForm source = sender as StickyNoteForm;
            if (source == null || source.IsDisposed) return;
            List<StickyNoteData> ordered =
                BuildAuthoritativeVisibleDockOrder(source.Data);
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(source.Data);
            snapshot = SelectMoreCompleteDockOrder(ordered, snapshot);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, source.Data.Id,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (ShouldCollapseWholeDockGroup(sourceIndex, ordered.Count))
            {
                // The top header is the group-level close handle. Preserve the
                // links so expanding all side tabs restores the same stack.
                StickyDockGroups.ApplyGroupSnapshot(snapshot);
                foreach (StickyNoteData note in snapshot)
                {
                    StickyNoteForm member;
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
                StickyNoteForm root = null;
                if (ordered.Count > 0)
                    _noteWindows.TryGetValue(ordered[0].Id, out root);
                Point rootAnchor = root == null ? source.Location :
                    root.Location;
                int rootWidth = root == null ? source.Width : root.Width;
                source.HideAsDockGroupMember();
                PreserveDockSlotForHiddenMember(snapshot, source.Data);
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
            StickyNoteForm source = sender as StickyNoteForm;
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
                StickyNoteForm member;
                if (_noteWindows.TryGetValue(note.Id, out member) &&
                    member != null && !member.IsDisposed)
                    _activeDockOriginalLocations[note.Id] = member.Location;
            }
            RaiseActiveDockGroupForDrag(source);
            // The root header is the one unambiguous handle for moving the
            // whole stack.  Only a member that has a parent can be pulled out
            // after a deliberate hold.
            _activeNoteSplitEligible = IsDockSplitEligible(
                source.Data.DockParentId, _activeDockGroup.Count);
            if (_activeNoteSplitEligible) ShowSplitGuide(source);
        }

        private void StickyNoteHeaderDragMoved(object sender, EventArgs e)
        {
            StickyNoteForm source = sender as StickyNoteForm;
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
                CancelsDockSplitHold(held.TotalMilliseconds,
                    totalDx, totalDy))
            {
                // A drag that starts moving immediately means "move the
                // group".  Only a deliberate stationary hold may split it.
                _activeNoteSplitEligible = false;
                ClearSplitGuide();
            }

            if (!_activeNoteDetached && _activeNoteSplitEligible &&
                held.TotalMilliseconds >= DockSplitHoldMilliseconds)
            {
                StickyNoteForm connected = null;
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
                        ExtractSingleDockMember(beforeSplit, source.Data);
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
                    StickyNoteForm member;
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
            StickyNoteForm source = sender as StickyNoteForm;
            if (source == null || !Object.ReferenceEquals(source,
                _activeNoteDrag)) return;
            DockTarget target = FindDockTarget(source);
            if (target != null && !CanSafelyCombineDockComponents(
                target, source)) target = null;
            if (target != null && target.Parent != null)
            {
                StickyNoteForm parent = target.Parent;
                List<StickyNoteData> targetOrder = BuildDockChainOrder(
                    parent.Data);
                List<StickyNoteData> targetSnapshot =
                    BuildDockChainOrderIncludingHidden(parent.Data);
                List<StickyNoteData> sourceSnapshot =
                    BuildDockChainOrderIncludingHidden(source.Data);
                StickyNoteForm targetRoot = targetOrder.Count == 0 ? parent :
                    _noteWindows[targetOrder[0].Id];
                Point targetRootAnchor = targetRoot.Location;
                int targetRootWidth = targetRoot.Width;
                StickyNoteData tail = FindActiveDockTail(source.Data);
                StickyNoteForm tailForm;
                if (tail == null || !_noteWindows.TryGetValue(tail.Id,
                    out tailForm) || tailForm.IsDisposed) tailForm = source;
                List<StickyNoteData> mergedSnapshot =
                    MergeDockSnapshotsAfterParent(targetSnapshot,
                        parent.Data, sourceSnapshot);
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
                        StickyNoteForm member;
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
                StickyNoteForm member;
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

        private void RaiseActiveDockGroupForDrag(StickyNoteForm source)
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
                StickyNoteForm member;
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
                StickyNoteForm member;
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
            return BuildDockChainOrderFromNotes(_notes.GetAll(), seed,
                visibleOnly);
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
            return SelectMoreCompleteDockOrder(live, stored);
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

        internal static List<StickyNoteData> SelectMoreCompleteDockOrder(
            IList<StickyNoteData> live, IList<StickyNoteData> stored)
        {
            List<StickyNoteData> liveCopy = live == null
                ? new List<StickyNoteData>() :
                new List<StickyNoteData>(live);
            List<StickyNoteData> storedCopy = stored == null
                ? new List<StickyNoteData>() :
                new List<StickyNoteData>(stored);
            // A newly inserted stack makes the live parent chain larger; a
            // temporarily broken parent link makes the saved group larger.
            // In both cases the more complete set is the only safe basis for
            // a group-level close/commit.
            return liveCopy.Count >= storedCopy.Count ? liveCopy : storedCopy;
        }

        internal static List<StickyNoteData> BuildDockChainOrderFromNotes(
            IList<StickyNoteData> notes, StickyNoteData seed,
            bool visibleOnly)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (seed == null) return result;
            if (!visibleOnly)
                return StickyDockGroups.GetOrderedGroup(notes, seed);
            Dictionary<string, StickyNoteData> visible =
                new Dictionary<string, StickyNoteData>(
                    StringComparer.OrdinalIgnoreCase);
            if (notes == null) return result;
            foreach (StickyNoteData note in notes)
                if (!visibleOnly || note.Visible) visible[note.Id] = note;
            StickyNoteData root = seed;
            HashSet<string> guard = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            while (root != null && guard.Add(root.Id) &&
                !String.IsNullOrEmpty(root.DockParentId))
            {
                StickyNoteData parent;
                if (!visible.TryGetValue(root.DockParentId, out parent)) break;
                root = parent;
            }
            guard.Clear();
            StickyNoteData current = root;
            while (current != null && guard.Add(current.Id))
            {
                result.Add(current);
                StickyNoteData child = null;
                foreach (StickyNoteData candidate in visible.Values)
                {
                    if (String.Equals(candidate.DockParentId, current.Id,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        child = candidate;
                        break;
                    }
                }
                current = child;
            }
            return result;
        }

        internal static bool ShouldRestoreWholeDockComponent(
            int storedComponentCount, bool anyMemberHidden)
        {
            // Any request for a persisted group is a group-level operation.
            // This also covers startup, when Visible may already be true but
            // none of the other native windows has been created yet.
            return storedComponentCount > 1;
        }

        private void LayoutDockChain(List<StickyNoteData> ordered,
            int left, int top, int width)
        {
            List<StickyNoteData> visibleNotes = new List<StickyNoteData>();
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData note in ordered)
            {
                StickyNoteForm form;
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
                StickyNoteForm form = _noteWindows[note.Id];
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

        internal static bool IsDockCoordinateRangeSafe(int top,
            IList<int> heights)
        {
            long y = top;
            if (y < -DockCoordinateSafetyLimit ||
                y > DockCoordinateSafetyLimit) return false;
            if (heights == null) return true;
            foreach (int value in heights)
            {
                int height = Math.Max(220, Math.Min(700, value));
                y += height;
                if (y < -DockCoordinateSafetyLimit ||
                    y > DockCoordinateSafetyLimit) return false;
            }
            return true;
        }

        private bool CanSafelyCombineDockComponents(DockTarget target,
            StickyNoteForm source)
        {
            if (target == null || target.Parent == null || source == null)
                return false;
            List<StickyNoteData> targetOrder = BuildDockChainOrder(
                target.Parent.Data);
            if (targetOrder.Count == 0) return false;
            StickyNoteForm root;
            if (!_noteWindows.TryGetValue(targetOrder[0].Id, out root) ||
                root == null || root.IsDisposed) return false;
            List<int> heights = new List<int>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in targetOrder)
            {
                StickyNoteForm form;
                if (seen.Add(note.Id) && _noteWindows.TryGetValue(note.Id,
                    out form) && form != null && !form.IsDisposed &&
                    form.Visible) heights.Add(form.Height);
            }
            foreach (StickyNoteData note in BuildDockChainOrder(source.Data))
            {
                StickyNoteForm form;
                if (seen.Add(note.Id) && _noteWindows.TryGetValue(note.Id,
                    out form) && form != null && !form.IsDisposed &&
                    form.Visible) heights.Add(form.Height);
            }
            return IsDockCoordinateRangeSafe(root.Top, heights);
        }

        private void NormalizeDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            StickyNoteForm root;
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
                    StickyNoteForm member;
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
            StickyNoteForm source = sender as StickyNoteForm;
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
            StickyNoteForm root;
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
                    StickyNoteForm lower;
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
            StickyNoteForm source = sender as StickyNoteForm;
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
            StickyNoteForm root;
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
            StickyNoteForm source = sender as StickyNoteForm;
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
                    StickyNoteForm member;
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
            foreach (StickyNoteForm form in _noteWindows.Values)
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
                    StickyNoteForm form;
                    if (_noteWindows.TryGetValue(ordered[index].Id, out form) &&
                        form != null && !form.IsDisposed)
                    {
                        bool internalDivider = index < ordered.Count - 1;
                        int dividerMinimum = 220;
                        int dividerMaximum = 700;
                        if (internalDivider)
                        {
                            StickyNoteForm lower;
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

        internal static bool ShouldCollapseWholeDockGroup(int sourceIndex,
            int visibleComponentCount)
        {
            return visibleComponentCount > 1 && sourceIndex == 0;
        }

        internal static void RewireDockChainAfterMemberClose(
            StickyNoteData closing, StickyNoteData child)
        {
            if (closing == null) return;
            if (child != null) child.DockParentId = closing.DockParentId;
            closing.DockParentId = String.Empty;
        }

        internal static List<StickyNoteData> ExtractSingleDockMember(
            IList<StickyNoteData> ordered, StickyNoteData extracted)
        {
            List<StickyNoteData> remaining = new List<StickyNoteData>();
            if (ordered != null)
            {
                foreach (StickyNoteData note in ordered)
                    if (note != null && !Object.ReferenceEquals(note,
                        extracted)) remaining.Add(note);
            }
            StickyDockGroups.ApplyOrderedGroup(remaining);
            StickyDockGroups.ClearMembership(extracted);
            return remaining;
        }

        internal static void PreserveDockSlotForHiddenMember(
            IList<StickyNoteData> snapshot, StickyNoteData hidden)
        {
            if (hidden != null) hidden.Visible = false;
            StickyDockGroups.ApplyGroupSnapshot(snapshot);
            StickyDockGroups.RebuildVisibleParentChain(snapshot);
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
                    StickyNoteForm member;
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
                StickyNoteForm form;
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
            if (seed == null) return null;
            HashSet<string> activeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _activeDockGroup)
                activeIds.Add(note.Id);
            StickyNoteData tail = seed;
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            while (tail != null && visited.Add(tail.Id))
            {
                StickyNoteData child = null;
                foreach (StickyNoteData note in _notes.GetAll())
                {
                    if (activeIds.Contains(note.Id) && String.Equals(
                        note.DockParentId, tail.Id,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        child = note;
                        break;
                    }
                }
                if (child == null) break;
                tail = child;
            }
            return tail ?? seed;
        }

        internal static void RewireDockChainForInsertion(
            StickyNoteData parent, StickyNoteData insertedHead,
            StickyNoteData insertedTail, StickyNoteData previousChild)
        {
            if (parent == null || insertedHead == null) return;
            StickyNoteData tail = insertedTail ?? insertedHead;
            insertedHead.DockParentId = parent.Id;
            if (previousChild != null)
                previousChild.DockParentId = tail.Id;
        }

        internal static List<StickyNoteData> MergeDockSnapshotsAfterParent(
            IList<StickyNoteData> targetSnapshot, StickyNoteData parent,
            IList<StickyNoteData> insertedSnapshot)
        {
            List<StickyNoteData> inserted = new List<StickyNoteData>();
            HashSet<string> insertedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (insertedSnapshot != null)
            {
                foreach (StickyNoteData note in insertedSnapshot)
                    if (note != null && insertedIds.Add(note.Id))
                        inserted.Add(note);
            }
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (targetSnapshot != null)
            {
                foreach (StickyNoteData note in targetSnapshot)
                    if (note != null && !insertedIds.Contains(note.Id))
                        result.Add(note);
            }
            int insertion = result.Count;
            if (parent != null)
            {
                int parentIndex = result.FindIndex(
                    delegate(StickyNoteData note)
                    {
                        return String.Equals(note.Id, parent.Id,
                            StringComparison.OrdinalIgnoreCase);
                    });
                if (parentIndex >= 0) insertion = parentIndex + 1;
            }
            result.InsertRange(insertion, inserted);
            StickyDockGroups.ApplyOrderedGroup(result);
            return result;
        }

        private void ShowSplitGuide(StickyNoteForm source)
        {
            ClearSplitGuide();
            if (source == null || source.IsDisposed) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.Data.DockParentId))
            {
                StickyNoteForm parent;
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

        private void UpdateSplitGuide(StickyNoteForm source)
        {
            if (_splitGuideIndicator == null ||
                _splitGuideIndicator.IsDisposed || source == null) return;
            Rectangle seam = Rectangle.Empty;
            if (!String.IsNullOrEmpty(source.Data.DockParentId))
            {
                StickyNoteForm parent;
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

        private void UpdateDockPreview(StickyNoteForm source)
        {
            DockTarget target = FindDockTarget(source);
            StickyNoteForm parent = target == null ? null : target.Parent;
            StickyNoteForm child = target == null ? null : target.ExistingChild;
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

        private DockTarget FindDockTarget(StickyNoteForm source)
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
            foreach (StickyNoteForm candidate in _noteWindows.Values)
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

        private StickyNoteForm FindDockChild(string parentId,
            HashSet<string> ignoredIds)
        {
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (ignoredIds != null && ignoredIds.Contains(note.Id)) continue;
                if (String.Equals(note.DockParentId, parentId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    StickyNoteForm child;
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
            int limit = Math.Max(4, threshold);
            if (Math.Abs(moving.Top - target.Bottom) > limit) return false;
            int overlap = Math.Min(moving.Right, target.Right) -
                Math.Max(moving.Left, target.Left);
            int narrowerWidth = Math.Min(moving.Width, target.Width);
            int widerWidth = Math.Max(moving.Width, target.Width);
            bool aligned = Math.Abs(moving.Left - target.Left) <= limit ||
                Math.Abs(moving.Right - target.Right) <= limit ||
                Math.Abs((moving.Left + moving.Right) -
                    (target.Left + target.Right)) <= limit * 2;
            // Width is normalized only after docking, so a 900px note must be
            // allowed to meet a 280px note without first matching an edge or
            // center. Half of the narrower window is an unambiguous target.
            bool differentWidths = widerWidth >= narrowerWidth * 3 / 2;
            return overlap >= Math.Max(48, narrowerWidth / 2) &&
                (aligned || differentWidths);
        }

        internal static bool CancelsDockSplitHold(double heldMilliseconds,
            int totalDx, int totalDy)
        {
            return heldMilliseconds < DockSplitHoldMilliseconds &&
                totalDx * totalDx + totalDy * totalDy >
                    DockSplitPreHoldMovement * DockSplitPreHoldMovement;
        }

        internal static bool IsDockSplitEligible(string parentId,
            int componentCount)
        {
            return componentCount > 1 && !String.IsNullOrEmpty(parentId);
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
            StickyNoteForm root = null;
            if (snapshot.Count > 0)
                _noteWindows.TryGetValue(snapshot[0].Id, out root);
            Point rootAnchor = root == null ? new Point(note.X, note.Y) :
                root.Location;
            int rootWidth = root == null ? note.Width : root.Width;
            StickyNoteForm form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.HideNote();
            else
            {
                note.Visible = false;
            }
            PreserveDockSlotForHiddenMember(snapshot, note);
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
            StickyNoteForm form;
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

        private void PreviewReminderDraft(StickyNoteForm form,
            ReminderDialog dialog, string noteId)
        {
            if (form == null || form.IsDisposed || dialog == null) return;
            List<ReminderItem> preview = _reminders.GetItems();
            preview.Add(new ReminderItem(
                dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                noteId, dialog.ReminderFontSizePoints,
                dialog.PreAlertEnabled));
            form.UpdateReminderBanner(preview);
        }

        private void EditReminder(ReminderItem existing)
        {
            if (existing == null || !_reminders.GetItems().Contains(existing)) return;
            using (ReminderDialog dialog = new ReminderDialog(existing.Text, false,
                existing.FontSizeTwips / 20F, existing.PreAlertEnabled,
                existing.DeadlineUtc.ToLocalTime()))
            {
                StickyNoteForm linkedForm = null;
                if (!String.IsNullOrEmpty(existing.SourceNoteId))
                    _noteWindows.TryGetValue(existing.SourceNoteId,
                        out linkedForm);
                if (linkedForm != null && !linkedForm.IsDisposed)
                {
                    dialog.ReminderFontSizePreviewChanged += delegate
                    {
                        if (!linkedForm.IsDisposed)
                            linkedForm.PreviewReminderFontSize(existing,
                                dialog.ReminderFontSizePoints);
                    };
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    UpdateAllStickyNoteReminderBanners();
                    return;
                }
                if (!_reminders.GetItems().Contains(existing))
                {
                    ShowBubble("这条提醒已经到期或被删除，请重新添加。");
                    return;
                }
                ReminderItem replacement = _reminders.Replace(existing,
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                StickyNoteData note = String.IsNullOrEmpty(replacement.SourceNoteId)
                    ? null : _notes.Find(replacement.SourceNoteId);
                if (note != null)
                {
                    RefreshLinkedNoteReminderState(note);
                    _notes.Save();
                    StickyNoteForm noteForm;
                    if (_noteWindows.TryGetValue(note.Id, out noteForm) &&
                        !noteForm.IsDisposed) noteForm.RefreshReminderState();
                }
                SaveReminders();
                RefreshMenuText();
                ShowBriefBubble("提醒已修改：" + replacement.DeadlineUtc.ToLocalTime()
                    .ToString("yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        private void CancelReminderForNote(StickyNoteData note, bool announce)
        {
            if (note == null) return;
            bool closePreAlert = _preAlertItem != null && String.Equals(
                _preAlertItem.SourceNoteId, note.Id,
                StringComparison.OrdinalIgnoreCase);
            int removed = _reminders.RemoveBySourceNoteId(note.Id);
            RefreshLinkedNoteReminderState(note);
            if (closePreAlert) CloseCurrentBubbleWithoutRestoringHover(true);
            _notes.Save();
            SaveReminders();
            StickyNoteForm form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.RefreshReminderState();
            RefreshMenuText();
            if (announce) ShowBubble(removed == 0
                ? "这张便利贴当前没有提醒。" : "这张便利贴的提醒已经全部取消。");
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
            if (_exiting) return _art.IsRowLoaded(WavingRow)
                ? WavingRow : IdleRow;
            if (_dragging && _dragMoved) return _art.IsRowLoaded(FailedRow)
                ? FailedRow : IdleRow;
            if (_manualAnimationActive) return _art.IsRowLoaded(
                _manualAnimationRow) ? _manualAnimationRow : IdleRow;
            if (_typingSession) return _art.IsRowLoaded(_typingRow)
                ? _typingRow : IdleRow;
            if (_reminderAttentionActive)
                return AttentionAnimationRow(_art.IsRowLoaded(NotificationRow));
            if (_mouseInside && !_menu.Visible)
                return _art.IsRowLoaded(HoverRow) ? HoverRow : IdleRow;
            return _art.IsRowLoaded(_idleRow) ? _idleRow : IdleRow;
        }

        internal static bool ReminderAnimationCycleComplete(bool active,
            int row, int frame, int frameCount)
        {
            return active && row == NotificationRow && frameCount > 0 &&
                frame >= frameCount - 1;
        }

        internal static int DueReminderBubbleDurationMilliseconds
        {
            get { return ReminderBubbleDurationMilliseconds; }
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
            // An at-time reminder is persistent against pet clicks, but it is
            // not allowed to block later feedback. Any later application
            // bubble replaces it. Pre-alert countdowns keep their older rule.
            if (currentIsDueReminder) return true;
            return !currentIsPreAlert || incomingIsDueReminder || exiting;
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
                ManualAnimationCooldownMilliseconds);
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
            return notificationLoaded ? NotificationRow : IdleRow;
        }

        internal static int PickRandomTypingAnimationRow(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            return random.Next(GuitarFailureProbabilityDenominator) == 0
                ? ThinkingRow : WaitingRow;
        }

        internal static int PickRandomIdleAnimationRow(Random random,
            int currentRow)
        {
            if (random == null) throw new ArgumentNullException("random");
            int selected = random.Next(IdleThoughtProbabilityDenominator);
            int candidate = selected < IdleThoughtProbabilityDenominator - 2
                ? IdleRow : (selected == IdleThoughtProbabilityDenominator - 2
                    ? FailedRow : ReviewRow);
            // Ordinary idle may repeat; the two thought clips do not repeat
            // immediately. Together they occupy 2/20 = 10% of idle cycles.
            if (candidate == currentRow && candidate != IdleRow) return IdleRow;
            return candidate;
        }

        internal static int IdleThoughtProbabilityBase
        {
            get { return IdleThoughtProbabilityDenominator; }
        }

        internal static int GuitarFailureProbabilityBase
        {
            get { return GuitarFailureProbabilityDenominator; }
        }

        internal static bool IsIdleAnimationRow(int row)
        {
            return row == IdleRow || row == FailedRow || row == ReviewRow;
        }

        internal static bool IsTypingAnimationRow(int row)
        {
            return row == WaitingRow || row == ThinkingRow;
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

        private void ReminderTick(object sender, EventArgs e)
        {
            if (!ShouldRunReminderClock(_exiting)) return;
            DateTime now = DateTime.UtcNow;
            ReminderItem due = _reminders.FirstDue(now);
            if (due != null)
            {
                TriggerReminder(due);
                return;
            }

            // Due checks remain at 500 ms. Countdown labels only display whole
            // seconds, so update their existing rows once per second without
            // rebuilding controls or touching the editor/IME focus.
            long currentSecond = now.Ticks / TimeSpan.TicksPerSecond;
            if (ShouldRefreshReminderBanner(_lastReminderBannerSecond,
                currentSecond))
            {
                _lastReminderBannerSecond = currentSecond;
                UpdateAllStickyNoteReminderBanners();
            }

            ReminderItem next = _reminders.NextPreAlert;
            if (ShouldShowPreAlert(next, next == null
                ? TimeSpan.Zero : next.DeadlineUtc - now))
                ShowOrUpdatePreAlert(next);
            else if (_bubbleIsPreAlert)
                CloseCurrentBubbleWithoutRestoringHover(true);

            if (_bubbleIsHover)
                ShowOrUpdateHoverBubble();
        }

        internal static bool ShouldRefreshReminderBanner(long previousSecond,
            long currentSecond)
        {
            return previousSecond != currentSecond;
        }

        private void ShowReminderDialog()
        {
            if (_reminders.Count >= ReminderSchedule.MaximumItems)
            {
                ShowBubble("最多可以保存五条提醒，请先取消一条。");
                return;
            }
            using (ReminderDialog dialog = new ReminderDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                StickyNoteData note = dialog.CreateStickyNote
                    ? CreateStickyNoteData(dialog.ReminderText) : null;
                if (dialog.CreateStickyNote && note == null) return;
                if (note != null)
                {
                    note.FontSizeTwips = (int)Math.Round(
                        dialog.ReminderFontSizePoints * 20F);
                }
                ReminderItem item = _reminders.Add(
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    note == null ? null : note.Id,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                QueueArtPreload(NotificationRow);
                if (note != null)
                {
                    note.ReminderUtcTicks = item.DeadlineUtc.Ticks;
                    _notes.Save();
                    ShowStickyNote(note, true);
                    PlaceNewStickyWindowOnPetScreen(note);
                }
                SaveReminders();
                RefreshMenuText();
                ShowBriefBubble("提醒已添加：" +
                    item.DeadlineUtc.ToLocalTime().ToString("yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        private void CancelReminder(ReminderItem item, bool announce)
        {
            if (!_reminders.Remove(item)) return;
            ClearLinkedNoteReminder(item, false);
            if (ReferenceEquals(_preAlertItem, item))
                CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            if (announce) ShowBriefBubble("这条提醒已经取消。");
        }

        private void CancelAllReminders()
        {
            _reminders.Cancel();
            ReconcileNoteReminders();
            CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            ShowBubble("全部提醒已经取消。");
        }

        private void TriggerReminder(ReminderItem item)
        {
            string text = item == null ? String.Empty : item.Text;
            if (item != null) _reminders.Remove(item);
            StickyNoteData linkedNote = ClearLinkedNoteReminder(item, true);
            // A due reminder always replaces hover, confirmation and daily
            // speech instead of waiting behind a long-lived bubble.
            if (_bubble != null && !_bubble.IsDisposed)
                CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            RequestReminderAttentionAnimation();
            string reminderText = String.IsNullOrWhiteSpace(text) ? "到时间啦。" : text;
            ShowBubble(reminderText, KeyboardOverlayForm.TextFontFamilyName,
                DueReminderBubbleFontSizePoints(
                    _settings.KeyOverlayScalePercent),
                ReminderBubbleDurationMilliseconds, false, true);
            System.Media.SystemSounds.Asterisk.Play();
            if (linkedNote != null)
                ShowStickyNote(linkedNote, !HasFocusedOwnNoteTextInput());
        }

        private void RequestReminderAttentionAnimation()
        {
            int generation = Interlocked.Increment(
                ref _reminderAnimationGeneration);
            QueueArtPreload(NotificationRow);
            if (_art.IsRowLoaded(NotificationRow))
            {
                BeginReminderAttentionAnimation(generation);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                // Bounded wait: an ordinary lazy decode completes quickly, but
                // damaged art must not create an endless retry loop.
                for (int attempt = 0; attempt < 50 && !_exiting &&
                    !IsDisposed; attempt++)
                {
                    if (_art.IsRowLoaded(NotificationRow)) break;
                    if (attempt == 12) QueueArtPreload(NotificationRow);
                    Thread.Sleep(100);
                }
                if (!_art.IsRowLoaded(NotificationRow) || _exiting ||
                    IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        BeginReminderAttentionAnimation(generation);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void BeginReminderAttentionAnimation(int generation)
        {
            if (_exiting || IsDisposed || generation !=
                _reminderAnimationGeneration ||
                !_art.IsRowLoaded(NotificationRow)) return;
            _reminderAttentionActive = true;
            if (_row == NotificationRow)
            {
                _frame = 0;
                _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
            }
        }

        private StickyNoteData ClearLinkedNoteReminder(ReminderItem item, bool makeVisible)
        {
            if (item == null || String.IsNullOrEmpty(item.SourceNoteId)) return null;
            StickyNoteData note = _notes.Find(item.SourceNoteId);
            if (note == null) return null;
            RefreshLinkedNoteReminderState(note);
            if (makeVisible) note.Visible = true;
            _notes.Save();
            StickyNoteForm form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.RefreshReminderState();
            return note;
        }

        private void RefreshLinkedNoteReminderState(StickyNoteData note)
        {
            if (note == null) return;
            ReminderItem next = _reminders.FindBySourceNoteId(note.Id);
            note.ReminderUtcTicks = next == null ? 0 : next.DeadlineUtc.Ticks;
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
            return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromSeconds(20);
        }

        internal static bool ShouldShowPreAlert(ReminderItem item,
            TimeSpan remaining)
        {
            return item != null && item.PreAlertEnabled &&
                IsPreAlertWindow(remaining);
        }

        internal static bool ShouldPauseOwnNoteAnimation(bool composing,
            DateTime quietUntilUtc, DateTime nowUtc)
        {
            return composing || nowUtc < quietUntilUtc;
        }

        internal static bool ShouldRunReminderClock(bool exiting)
        {
            return !exiting;
        }

        internal static bool ShouldRestoreReminderAfterLaunch(ReminderItem item,
            DateTime launchedUtc)
        {
            return item != null && item.DeadlineUtc > launchedUtc;
        }

        internal static bool IsManualAnimationRow(int row)
        {
            foreach (int candidate in ManualAnimationRows)
                if (candidate == row) return true;
            return false;
        }

        internal static int PickRandomManualAnimationRow(Random random,
            int currentRow)
        {
            if (random == null) throw new ArgumentNullException("random");
            int availableWeight = 0;
            foreach (int candidate in ManualAnimationRows)
            {
                if (candidate == currentRow) continue;
                availableWeight += ManualAnimationWeight(candidate);
            }
            if (availableWeight <= 0) return IdleRow;
            int selected = random.Next(availableWeight);
            foreach (int candidate in ManualAnimationRows)
            {
                if (candidate == currentRow) continue;
                int weight = ManualAnimationWeight(candidate);
                if (selected < weight) return candidate;
                selected -= weight;
            }
            return ManualAnimationRows[0];
        }

        private static int ManualAnimationWeight(int row)
        {
            // Keep both thought clips and the failed-guitar clip rare when the
            // user clicks the pet for a random animation as well.
            return row == FailedRow || row == ReviewRow || row == ThinkingRow
                ? 2 : 9;
        }

        internal static bool ManualAnimationClickReady(DateTime nowUtc,
            DateTime cooldownUntilUtc)
        {
            return nowUtc >= cooldownUntilUtc;
        }

        internal static bool MovementStartsDrag(int dx, int dy)
        {
            return dx * dx + dy * dy >=
                DragClickThresholdPixels * DragClickThresholdPixels;
        }

        internal static int ManualAnimationCooldown
        {
            get { return ManualAnimationCooldownMilliseconds; }
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
