using System;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class SelfTestCommandRouter
    {
        internal static bool TryRun(string[] args, out int exitCode)
        {
            if (TryCommand(args, "--self-test=", false,
                delegate(string value) { SelfTest.Run(value); }, out exitCode))
                return true;
            if (TryCommand(args, "--sticky-input-probe=", true,
                delegate(string value) { SelfTest.RunStickyInputProbe(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--sticky-pump-probe=", true,
                delegate(string value)
                {
                    SelfTest.RunStickyWinFormsPumpProbe(value);
                }, out exitCode)) return true;
            if (TryCommand(args, "--sticky-transparency-probe=", true,
                delegate(string value)
                {
                    SelfTest.RunStickyTransparencyOverlapProbe(value);
                }, out exitCode)) return true;
            if (TryCommand(args, "--startup-probe=", false,
                delegate(string value) { SelfTest.RunStartupProbe(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--solar-term-probe=", false,
                delegate(string value) { SelfTest.RunSolarTermProbe(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--zodiac-daily-probe=", false,
                delegate(string value) { SelfTest.RunZodiacDailyProbe(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-sticky-preview=", true,
                delegate(string value) { SelfTest.RenderStickyPreview(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-schedule-preview=", true,
                delegate(string value) { SelfTest.RenderSchedulePreview(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-sticky-appearance-preview=", true,
                delegate(string value)
                {
                    SelfTest.RenderStickyAppearancePreview(value);
                }, out exitCode)) return true;
            if (TryCommand(args, "--render-hover-preview=", false,
                delegate(string value)
                {
                    SelfTest.RenderHoverBubblePreview(value);
                }, out exitCode)) return true;
            if (TryCommand(args, "--render-preview=", false,
                delegate(string value) { SelfTest.RenderPreview(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-feature-preview=", true,
                delegate(string value) { SelfTest.RenderFeaturePreview(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-reminder-preview=", true,
                delegate(string value) { SelfTest.RenderReminderPreview(value); },
                out exitCode)) return true;
            if (TryCommand(args, "--render-contact-preview=", true,
                delegate(string value)
                {
                    SelfTest.RenderContactAuthorPreview(value);
                }, out exitCode)) return true;

            exitCode = 0;
            return false;
        }

        private static bool TryCommand(string[] args, string prefix,
            bool prepareWindowsUi, Action<string> action, out int exitCode)
        {
            string path;
            if (!CommandLineArguments.TryGetPath(args, prefix, out path))
            {
                exitCode = 0;
                return false;
            }
            if (prepareWindowsUi) PrepareWindowsUi();
            exitCode = CommandLineArguments.RunOutputCommand(path,
                delegate { action(path); });
            return true;
        }

        private static void PrepareWindowsUi()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }
}
