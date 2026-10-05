using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Execute domain probes and hand the result objects directly to the generic
    // report serializer. There is intentionally no JSON-field-to-bool mapping
    // layer here: a CheckResult field is both the test result and report source.
    internal static partial class SelfTest
    {
        private sealed class DockCheckResult
        {
            internal DockPersistenceCheckResult Persistence;
            internal DockLifecycleCheckResult Lifecycle;
            internal DockGeometryCheckResult Geometry;
            internal bool PersistenceAndGeometryOk;
        }

        private sealed class SelfTestRootCheckResult
        {
            internal bool Pc2CharacterizationOk;
            internal bool StickyFeatureBoundaryOk;
            internal bool DisplayResolverConsistencyOk;
            internal bool AutomaticNoteBackupOk;
            internal bool SilentModeReminderPolicyOk;
        }

        private static DockCheckResult RunDockChecks(string outputPath)
        {
            DockCheckResult result = new DockCheckResult();
            result.Persistence = RunDockPersistenceChecks(outputPath);
            result.Lifecycle = RunDockLifecycleChecks();
            result.Geometry = RunDockGeometryChecks();
            result.PersistenceAndGeometryOk =
                result.Persistence.DockRoundTripOk &&
                result.Geometry.BottomDockingOk;
            return result;
        }

        public static void Run(string outputPath)
        {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(fullOutputPath);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            try
            {
                SelfTestRootCheckResult rootChecks =
                    new SelfTestRootCheckResult();

                // PC-2A executes production rejection paths. Any failed
                // characterization throws; reaching the next line is the result.
                RunPc2CharacterizationChecks(fullOutputPath);
                rootChecks.Pc2CharacterizationOk = true;

                rootChecks.StickyFeatureBoundaryOk =
                    RunStickyFeatureBoundaryChecks();
                RunDisplayResolverConsistencyCheck();
                rootChecks.DisplayResolverConsistencyOk = true;

                ArtResourceCheckResult artChecks = RunArtResourceChecks();
                SettingsPersistenceCheckResult settingsChecks =
                    RunSettingsPersistenceChecks(fullOutputPath);
                ReminderCoordinatorCheckResult reminderCoordinatorChecks =
                    RunReminderCoordinatorChecks(settingsChecks.ReminderBaseUtc);
                KeyboardOverlayCheckResult keyboardOverlayChecks =
                    RunKeyboardOverlayChecks();
                AnimationCheckResult animationChecks =
                    RunAnimationChecks(artChecks.AnimationCycleDurations);
                BubbleCheckResult bubbleChecks = RunBubbleChecks();
                WeatherCheckResult weatherChecks = RunWeatherChecks();
                StickyEditorCheckResult editorChecks = RunStickyEditorChecks();
                StickyPersistenceCheckResult stickyChecks =
                    RunStickyPersistenceChecks(fullOutputPath);
                WindowShellCheckResult shellChecks =
                    RunWindowShellChecks(stickyChecks.RestoredNote);
                StickyScheduleWindowCheckResult scheduleWindowChecks =
                    RunStickyScheduleWindowChecks();
                StickyFontCheckResult fontChecks = RunStickyFontChecks();
                StickyDialogCheckResult dialogChecks = RunStickyDialogChecks();
                StickyWindowPolicyCheckResult windowPolicyChecks =
                    RunStickyWindowPolicyChecks(stickyChecks.Repository);
                StickySideTabCheckResult sideTabChecks =
                    RunStickySideTabChecks(stickyChecks.RestoredNote);
                rootChecks.AutomaticNoteBackupOk =
                    RunStickyBackupCleanupCheck(stickyChecks);
                StickyCompatibilityCheckResult compatibilityChecks =
                    RunStickyCompatibilityChecks(fullOutputPath);
                DockCheckResult dockChecks = RunDockChecks(fullOutputPath);
                rootChecks.SilentModeReminderPolicyOk =
                    !PetMessagePolicy.ShouldSuppress(
                        PetMessageKind.ReminderDue, true);

                SelfTestReport report = new SelfTestReport();
                report.Add(rootChecks);
                report.Add(artChecks);
                report.Add(settingsChecks);
                report.Add(reminderCoordinatorChecks);
                report.Add(keyboardOverlayChecks);
                report.Add(animationChecks);
                report.Add(bubbleChecks);
                report.Add(weatherChecks);
                report.Add(editorChecks);
                report.Add(stickyChecks);
                report.Add(shellChecks);
                report.Add(scheduleWindowChecks);
                report.Add(fontChecks);
                report.Add(dialogChecks);
                report.Add(windowPolicyChecks);
                report.Add(sideTabChecks);
                report.Add(compatibilityChecks);
                report.Add(dockChecks);

                string json = report.ToJson();
                File.WriteAllText(fullOutputPath, json,
                    new UTF8Encoding(false));
                if (!report.Ok)
                {
                    Console.Error.WriteLine(
                        "MODULAR FALSE FIELDS: " +
                        String.Join(", ", report.FalsePaths));
                }
            }
            catch (Exception ex)
            {
                string message = (ex.Message ?? String.Empty)
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
                File.WriteAllText(fullOutputPath,
                    "{\"ok\":false,\"error\":\"" + message + "\"}" +
                    Environment.NewLine,
                    new UTF8Encoding(false));
                Console.Error.WriteLine("MODULAR ERROR: " + ex);
            }
        }
    }
}
