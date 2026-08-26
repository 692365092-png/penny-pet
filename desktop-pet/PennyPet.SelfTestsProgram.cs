using System;
using System.Text;
using System.IO;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class PennySelfTestsProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            string path;
            if (TryPath(args, "--self-test=", out path)) SelfTest.Run(path);
            else if (TryPath(args, "--sticky-input-probe=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RunStickyInputProbe(path);
            }
            else if (TryPath(args, "--sticky-pump-probe=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RunStickyWinFormsPumpProbe(path);
            }
            else if (TryPath(args, "--sticky-transparency-probe=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RunStickyTransparencyOverlapProbe(path);
            }
            else if (TryPath(args, "--startup-probe=", out path))
                SelfTest.RunStartupProbe(path);
            else if (TryPath(args, "--render-sticky-preview=", out path))
            {
                PrepareWindowsUi();
                RunWithErrorFile(path, delegate { SelfTest.RenderStickyPreview(path); });
            }
            else if (TryPath(args, "--render-schedule-preview=", out path))
            {
                PrepareWindowsUi();
                RunWithErrorFile(path, delegate { SelfTest.RenderSchedulePreview(path); });
            }
            else if (TryPath(args, "--render-sticky-appearance-preview=", out path))
            {
                PrepareWindowsUi();
                RunWithErrorFile(path, delegate
                {
                    SelfTest.RenderStickyAppearancePreview(path);
                });
            }
            else if (TryPath(args, "--render-hover-preview=", out path))
                SelfTest.RenderHoverBubblePreview(path);
            else if (TryPath(args, "--render-preview=", out path))
                SelfTest.RenderPreview(path);
            else if (TryPath(args, "--render-feature-preview=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RenderFeaturePreview(path);
            }
            else if (TryPath(args, "--render-reminder-preview=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RenderReminderPreview(path);
            }
            else if (TryPath(args, "--render-contact-preview=", out path))
            {
                PrepareWindowsUi();
                SelfTest.RenderContactAuthorPreview(path);
            }
            else
            {
                Console.Error.WriteLine(
                    "PennyPet.SelfTests expects a self-test, probe or preview command.");
                return 2;
            }
            return Environment.ExitCode;
        }

        private static void PrepareWindowsUi()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        private static bool TryPath(string[] args, string prefix,
            out string path)
        {
            path = null;
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (arg == null || !arg.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase)) continue;
                path = arg.Substring(prefix.Length).Trim('"');
                return !String.IsNullOrEmpty(path);
            }
            return false;
        }

        private static void RunWithErrorFile(string path, Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                try { File.WriteAllText(path + ".error.txt", error.ToString(),
                    Encoding.UTF8); }
                catch { }
                throw;
            }
        }
    }
}

