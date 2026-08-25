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

}
