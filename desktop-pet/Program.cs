using System;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Compatibility-test build: keep WPF sticky-note rendering away
            // from GPU/driver-specific layered-window paths.  The animated pet
            // itself remains on the existing WinForms renderer.
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            bool stickyKeyboardDemo = CommandLineArguments.HasFlag(args,
                "--sticky-keyboard-demo");
            bool stickyKeyboardHostDemo = CommandLineArguments.HasFlag(args,
                "--sticky-keyboard-host-demo");
            bool stickyTodoDemo = CommandLineArguments.HasFlag(args,
                "--sticky-todo-demo");
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
                using (StickyNoteWindow note = new StickyNoteWindow(demo, false, true))
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
                        WpfApplicationHost.Ensure();
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
            if (CommandLineArguments.HasFlag(args, "--sticky-appearance-demo"))
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
                using (StickyNoteWindow note = new StickyNoteWindow(demo, true))
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
            int commandExitCode;
            if (SelfTestCommandRouter.TryRun(args, out commandExitCode) ||
                ArtCommandRouter.TryRun(args, out commandExitCode))
            {
                Environment.ExitCode = commandExitCode;
                return;
            }

            PennyApplicationHost.Run();
        }
    }
}
