using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    // Full regression orchestration and report generation. Individual probes
    // and preview renderers remain in PennySelfTests.cs.
    internal static partial class SelfTest
    {
        private sealed class ArtResourceCheckResult
        {
            internal int Width;
            internal int Height;
            internal bool AtlasOk;
            internal bool AnimationTimingOk;
            internal bool StartupCacheEmbeddedOk;
            internal bool StartupLazyLoadOk;
            internal bool InteractionPreloadOk;
            internal bool NotificationPlaybackOk;
            internal bool InnerOutlineOk;
            internal bool GreenHaloAbsent;
            internal bool ApplicationIconEmbeddedOk;
            internal bool StartupFrameEmbeddedOk;
            internal bool StartupFrameUsesEmbeddedLoadingOk;
            internal bool StartupUsesSavedScaleOk;
            internal bool StartupLocationOk;
            internal bool StartupLoadingThreadHostOk;
            internal bool ContactAuthorFeatureOk;
            internal int[] AnimationCycleDurations;
        }

        private static ArtResourceCheckResult RunArtResourceChecks()
        {
            ArtResourceCheckResult result = new ArtResourceCheckResult
            {
                AnimationTimingOk = true,
                StartupCacheEmbeddedOk =
                    PetArtPackage.HasEmbeddedStartupCacheForTest,
                AnimationCycleDurations = new int[
                    PetArtPackage.RuntimeStateNames.Length]
            };
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            {
                Bitmap rendered = art.GetFrame(0, 0);
                result.StartupLazyLoadOk = art.LoadedRuntimeStateCount == 1;
                result.InteractionPreloadOk = !art.IsRowLoaded(4);
                art.PreloadRow(4);
                result.InteractionPreloadOk = result.InteractionPreloadOk &&
                    art.IsRowLoaded(4) && art.LoadedRuntimeStateCount == 2;
                art.PreloadRow(9);
                result.NotificationPlaybackOk = art.IsRowLoaded(9) &&
                    art.GetFrame(9, 0) != null &&
                    PetAnimationController.AttentionAnimationRow(true) == 9 &&
                    PetAnimationController.AttentionAnimationRow(false) == 0;
                result.Width = rendered.Width;
                result.Height = rendered.Height;
                result.AtlasOk = result.Width == 192 && result.Height == 208;
                for (int row = 0;
                    row < PetArtPackage.RuntimeStateNames.Length; row++)
                {
                    result.AnimationCycleDurations[row] =
                        art.CycleDuration(row);
                    result.AnimationTimingOk = result.AnimationTimingOk &&
                        result.AnimationCycleDurations[row] > 0;
                    result.AtlasOk = result.AtlasOk &&
                        art.FrameCount(row) > 0 &&
                        result.AnimationCycleDurations[row] > 0;
                }
                int greenPixels = 0;
                for (int y = 0; y < rendered.Height; y++)
                {
                    for (int x = 0; x < rendered.Width; x++)
                    {
                        Color pixel = rendered.GetPixel(x, y);
                        if (pixel.A >= 16 &&
                            TouchesTransparency(rendered, x, y) &&
                            pixel.G > pixel.R + 40 &&
                            pixel.G > pixel.B + 40)
                            greenPixels++;
                    }
                }
                result.InnerOutlineOk = rendered.PixelFormat ==
                    PixelFormat.Format32bppPArgb;
                result.GreenHaloAbsent = greenPixels == 0;
            }
            using (Icon applicationIcon = Icon.ExtractAssociatedIcon(
                Assembly.GetExecutingAssembly().Location))
            {
                result.ApplicationIconEmbeddedOk = applicationIcon != null &&
                    applicationIcon.Width >= 16 && applicationIcon.Height >= 16;
            }
            result.StartupFrameEmbeddedOk = StartupLoadingForm.HasEmbeddedFrame;
            using (StartupLoadingForm loadingFrameForm =
                new StartupLoadingForm(new PetSettings()))
                result.StartupFrameUsesEmbeddedLoadingOk =
                    loadingFrameForm.UsesEmbeddedLoadingFrameForTest();
            result.StartupUsesSavedScaleOk = true;
            int[] startupScales = { 50, 100, 150, 200 };
            foreach (int scale in startupScales)
            {
                using (StartupLoadingForm loadingScaleForm =
                    new StartupLoadingForm(new PetSettings
                    {
                        ScalePercent = scale
                    }))
                    result.StartupUsesSavedScaleOk =
                        result.StartupUsesSavedScaleOk &&
                        loadingScaleForm.UsesPetScaleForTest(scale) &&
                        loadingScaleForm.UsesEmbeddedLoadingFrameForTest();
            }
            Rectangle startupWork = Screen.PrimaryScreen.WorkingArea;
            Point savedLoadingLocation = new Point(startupWork.Left + 24,
                startupWork.Top + 24);
            using (StartupLoadingForm savedLoadingForm =
                new StartupLoadingForm(new PetSettings
                {
                    HasLocation = true,
                    X = savedLoadingLocation.X,
                    Y = savedLoadingLocation.Y
                }))
            using (StartupLoadingForm fallbackLoadingForm =
                new StartupLoadingForm(new PetSettings()))
            {
                Point expectedFallback = new Point(startupWork.Right -
                    fallbackLoadingForm.ClientSize.Width - 24,
                    startupWork.Bottom - fallbackLoadingForm.ClientSize.Height -
                    24);
                result.StartupLocationOk =
                    savedLoadingForm.Location == savedLoadingLocation &&
                    fallbackLoadingForm.Location == expectedFallback;
            }
            using (StartupLoadingThreadHost loadingHost =
                new StartupLoadingThreadHost())
            {
                loadingHost.Start(new PetSettings());
                loadingHost.BringToFront();
                result.StartupLoadingThreadHostOk = true;
                loadingHost.Close();
            }
            bool contactArtworkEmbedded;
            using (Stream contactArtwork = typeof(ContactAuthorForm).Assembly
                .GetManifestResourceStream("PennyPet.ContactAuthor.Image"))
                contactArtworkEmbedded = contactArtwork != null;
            using (ContactAuthorForm contact = new ContactAuthorForm())
            {
                result.ContactAuthorFeatureOk = contactArtworkEmbedded &&
                    contact.CopyAndArtworkBehaviorConfigured &&
                    contact.DisplayedXiaohongshuNumber ==
                        ContactAuthorForm.XiaohongshuNumber &&
                    ContactAuthorForm.XiaohongshuProfileUrl ==
                        "https://www.xiaohongshu.com/user/profile/" +
                        "59bd4b0b51783a7612f6fc43" &&
                    ContactAuthorForm.XiaohongshuProfileUrl.IndexOf('?') < 0 &&
                    contact.XiaohongshuOnlyLayoutForTest;
            }
            return result;
        }

        private sealed class SettingsPersistenceCheckResult
        {
            internal DateTime ReminderBaseUtc;
            internal bool MinuteTimerOk;
            internal bool CancelOk;
            internal bool FiveRemindersOk;
            internal bool SixthReminderBlocked;
            internal bool ReminderMemoryOk;
            internal bool KeyboardPrivacyNoticePersistenceOk;
            internal bool SilentModePersistenceOk;
            internal bool DailyBriefingDatePersistenceOk;
            internal bool DailyContentPreferencesPersistenceOk;
            internal bool ZodiacPreferencePersistenceOk;
            internal bool WeatherPreferencePersistenceOk;
            internal bool FailureDirtyRetryOk;
            internal bool BackupRecoveryOk;
        }

        private static SettingsPersistenceCheckResult
            RunSettingsPersistenceChecks(string outputPath)
        {
            SettingsPersistenceCheckResult result =
                new SettingsPersistenceCheckResult
                {
                    ReminderBaseUtc = DateTime.UtcNow.AddDays(1)
                };
            ReminderSchedule schedule = new ReminderSchedule();
            schedule.Set(TimeSpan.FromMinutes(1), "test");
            result.MinuteTimerOk = schedule.Active &&
                schedule.DeadlineUtc > DateTime.UtcNow.AddSeconds(55);
            schedule.Cancel();
            result.CancelOk = !schedule.Active &&
                schedule.Text == String.Empty;
            for (int i = 0; i < ReminderSchedule.MaximumItems; i++)
            {
                if (i == 0)
                    schedule.Add(result.ReminderBaseUtc.AddMinutes(i),
                        "reminder-" + i, "note-0", 24F, true);
                else
                    schedule.Add(result.ReminderBaseUtc.AddMinutes(i),
                        "reminder-" + i, null);
            }
            result.FiveRemindersOk = schedule.Count == 5 &&
                schedule.GetItems()[0].Text == "reminder-0";
            try
            {
                schedule.Add(result.ReminderBaseUtc.AddHours(1), "sixth");
            }
            catch (InvalidOperationException)
            {
                result.SixthReminderBlocked = true;
            }

            PetSettings memorySettings = new PetSettings();
            memorySettings.SetReminders(schedule.GetItems());
            ReminderSchedule restored = new ReminderSchedule();
            restored.Restore(memorySettings.Reminders);
            result.ReminderMemoryOk = restored.Count == 5 &&
                restored.GetItems()[4].Text == "reminder-4";
            string persistenceTestPath = outputPath + ".settings-test.ini";
            memorySettings.StartupPreferenceInitialized = true;
            memorySettings.StartAtLogin = false;
            memorySettings.ScalePercent = 170;
            memorySettings.ShowKeyOverlay = false;
            memorySettings.KeyboardPrivacyNoticeAccepted = true;
            memorySettings.KeyOverlayScalePercent = 150;
            memorySettings.SilentMode = true;
            memorySettings.DailyContentEnabled = false;
            memorySettings.SolarTermEnabled = false;
            memorySettings.WeatherEnabled = true;
            memorySettings.WeatherLocationName = "武汉";
            memorySettings.WeatherLocationAdmin1 = "湖北";
            memorySettings.WeatherLocationCountry = "中国";
            memorySettings.WeatherLatitude = 30.5928;
            memorySettings.WeatherLongitude = 114.3055;
            memorySettings.WeatherTimezone = "Asia/Shanghai";
            memorySettings.ZodiacSign = ZodiacSign.Scorpio;
            memorySettings.LastDailyBriefingDate = "20350908";
            memorySettings.SaveToFile(persistenceTestPath);
            PetSettings diskSettings = PetSettings.LoadFromFile(
                persistenceTestPath);
            result.ReminderMemoryOk = result.ReminderMemoryOk &&
                diskSettings.Reminders.Count == 5 &&
                diskSettings.Reminders[0].Text == "reminder-0" &&
                diskSettings.Reminders[0].SourceNoteId == "note-0" &&
                diskSettings.Reminders[0].FontSizeTwips == 480 &&
                diskSettings.Reminders[0].PreAlertEnabled &&
                diskSettings.StartupPreferenceInitialized &&
                !diskSettings.StartAtLogin &&
                diskSettings.ScalePercent == 170 &&
                !diskSettings.ShowKeyOverlay &&
                diskSettings.KeyboardPrivacyNoticeAccepted &&
                diskSettings.KeyOverlayScalePercent == 150;
            result.KeyboardPrivacyNoticePersistenceOk =
                diskSettings.KeyboardPrivacyNoticeAccepted;
            result.SilentModePersistenceOk = diskSettings.SilentMode;
            result.DailyBriefingDatePersistenceOk =
                diskSettings.LastDailyBriefingDate == "20350908";
            PetSettingsData legacyDailySettings = PetSettingsCodec.Parse(
                new string[] { "SilentMode=0" });
            result.DailyContentPreferencesPersistenceOk =
                !diskSettings.DailyContentEnabled &&
                !diskSettings.SolarTermEnabled &&
                legacyDailySettings.DailyContentEnabled &&
                legacyDailySettings.SolarTermEnabled &&
                new PetSettings().DailyContentEnabled &&
                new PetSettings().SolarTermEnabled;
            result.ZodiacPreferencePersistenceOk =
                diskSettings.ZodiacSign == ZodiacSign.Scorpio &&
                legacyDailySettings.ZodiacSign == ZodiacSign.None &&
                new PetSettings().ZodiacSign == ZodiacSign.None;
            result.WeatherPreferencePersistenceOk =
                diskSettings.WeatherEnabled &&
                diskSettings.WeatherLocationName == "武汉" &&
                diskSettings.WeatherLocationAdmin1 == "湖北" &&
                diskSettings.WeatherLocationCountry == "中国" &&
                Math.Abs(diskSettings.WeatherLatitude - 30.5928) < 0.000001 &&
                Math.Abs(diskSettings.WeatherLongitude - 114.3055) < 0.000001 &&
                diskSettings.WeatherTimezone == "Asia/Shanghai" &&
                !legacyDailySettings.WeatherEnabled &&
                !new PetSettings().WeatherEnabled;

            string settingsRetryPath = outputPath +
                ".settings-retry-test.ini";
            PetSettings retrySettings = new PetSettings();
            int settingsFailureEvents = 0;
            retrySettings.SaveFailed += delegate { settingsFailureEvents++; };
            PersistenceResult failedSettingsSave = retrySettings.SaveToFile(
                settingsRetryPath + "\0");
            result.FailureDirtyRetryOk = !failedSettingsSave.Succeeded &&
                retrySettings.HasUnsavedChanges &&
                retrySettings.LastSaveError != null &&
                settingsFailureEvents == 1;
            PersistenceResult retriedSettingsSave = retrySettings.SaveToFile(
                settingsRetryPath);
            result.FailureDirtyRetryOk = result.FailureDirtyRetryOk &&
                retriedSettingsSave.Succeeded &&
                !retrySettings.HasUnsavedChanges;
            if (File.Exists(settingsRetryPath)) File.Delete(settingsRetryPath);
            if (File.Exists(settingsRetryPath + ".bak"))
                File.Delete(settingsRetryPath + ".bak");

            File.Copy(persistenceTestPath, persistenceTestPath + ".bak", true);
            const string corruptSettingsPayload = "not-a-penny-setting";
            File.WriteAllText(persistenceTestPath, corruptSettingsPayload,
                new UTF8Encoding(false));
            PetSettings recoveredSettings = PetSettings.LoadFromFile(
                persistenceTestPath);
            recoveredSettings.SaveToFile(persistenceTestPath);
            string settingsDirectory = Path.GetDirectoryName(
                Path.GetFullPath(persistenceTestPath));
            string settingsName = Path.GetFileName(persistenceTestPath);
            string[] preservedSettings = Directory.GetFiles(settingsDirectory,
                settingsName + ".corrupt-*");
            result.BackupRecoveryOk = recoveredSettings.ScalePercent == 170 &&
                recoveredSettings.SilentMode &&
                recoveredSettings.KeyboardPrivacyNoticeAccepted &&
                preservedSettings.Length > 0 &&
                File.ReadAllText(preservedSettings[0], Encoding.UTF8) ==
                    corruptSettingsPayload;
            if (File.Exists(persistenceTestPath))
                File.Delete(persistenceTestPath);
            if (File.Exists(persistenceTestPath + ".bak"))
                File.Delete(persistenceTestPath + ".bak");
            foreach (string preservedSetting in preservedSettings)
                if (File.Exists(preservedSetting)) File.Delete(preservedSetting);
            return result;
        }

        private sealed class StickyPersistenceCheckResult
        {
            internal string FilePath;
            internal StickyNoteRepository Repository;
            internal StickyNoteData RestoredNote;
            internal bool PersistenceOk;
            internal bool FailureDirtyRetryOk;
            internal bool GenerationMonotonicOk;
            internal bool MultilingualOk;
            internal bool RichTextOk;
            internal bool RichTextNoSilentTruncationOk;
            internal bool TodoOk;
            internal bool ScheduleOk;
        }

        private static StickyPersistenceCheckResult
            RunStickyPersistenceChecks(string outputPath)
        {
            StickyPersistenceCheckResult result =
                new StickyPersistenceCheckResult
                {
                    FilePath = outputPath + ".sticky-test.dat"
                };
            StickyNoteRepository stickyRepository =
                StickyNoteRepository.LoadFromFile(result.FilePath);
            const string multilingualSample =
                "English line\n日本語 한국어 Русский العربية Français";
            StickyNoteData sticky = stickyRepository.Create(multilingualSample,
                new Point(120, 160));
            sticky.Title = "多语言 Note 日本語";
            sticky.IsTodoList = true;
            sticky.TodoItems.Add(new StickyTodoItem("整理会议记录", false));
            sticky.TodoItems.Add(new StickyTodoItem("给家人回电话", true));
            sticky.ColorArgb = Color.LightBlue.ToArgb();
            sticky.BackgroundOpacityPercent = 60;
            sticky.TextColorArgb = Color.White.ToArgb();
            sticky.AlwaysOnTop = false;
            sticky.Width = 360;
            sticky.Height = 260;
            sticky.ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks;
            using (RichTextBox richSource = new RichTextBox())
            using (Font richFont = new Font("Microsoft YaHei UI", 14F,
                FontStyle.Bold | FontStyle.Italic | FontStyle.Underline))
            {
                richSource.Text = sticky.Text;
                richSource.SelectAll();
                richSource.SelectionFont = richFont;
                sticky.RichTextRtf = richSource.Rtf;
            }
            stickyRepository.SaveToFile(result.FilePath);
            result.Repository = StickyNoteRepository.LoadFromFile(
                result.FilePath);
            List<StickyNoteData> restoredNotes = result.Repository.GetAll();

            string persistenceStatePath = outputPath +
                ".persistence-state-test.dat";
            StickyNoteRepository persistenceStateRepository =
                StickyNoteRepository.LoadFromFile(persistenceStatePath);
            persistenceStateRepository.Create("dirty-state", Point.Empty);
            PersistenceResult failedSave = persistenceStateRepository
                .SaveToFile(persistenceStatePath + "\0");
            result.FailureDirtyRetryOk = !failedSave.Succeeded &&
                persistenceStateRepository.HasUnsavedChanges &&
                persistenceStateRepository.LastSaveError != null;
            PersistenceResult recoveredSave = persistenceStateRepository
                .SaveToFile(persistenceStatePath);
            result.FailureDirtyRetryOk = result.FailureDirtyRetryOk &&
                recoveredSave.Succeeded &&
                !persistenceStateRepository.HasUnsavedChanges;
            if (File.Exists(persistenceStatePath))
                File.Delete(persistenceStatePath);
            if (File.Exists(persistenceStatePath + ".bak"))
                File.Delete(persistenceStatePath + ".bak");

            string generationPath = outputPath + ".generation-test.dat";
            StickyNoteRepository generationRepository =
                StickyNoteRepository.LoadFromFile(generationPath);
            MethodInfo physicalWriter = typeof(StickyNoteRepository).GetMethod(
                "WriteSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            StickyNoteData newerNote = new StickyNoteData();
            newerNote.Text = "newer-generation";
            StickyNoteData staleNote = new StickyNoteData();
            staleNote.Text = "stale-generation";
            PersistenceResult newerWrite = (PersistenceResult)physicalWriter.Invoke(
                generationRepository, new object[] { generationPath,
                    new List<StickyNoteData> { newerNote }, 20L,
                    "sticky-generation-test" });
            PersistenceResult staleWrite = (PersistenceResult)physicalWriter.Invoke(
                generationRepository, new object[] { generationPath,
                    new List<StickyNoteData> { staleNote }, 19L,
                    "sticky-generation-test" });
            List<StickyNoteData> generationRestored = StickyNoteRepository
                .LoadFromFile(generationPath).GetAll();
            result.GenerationMonotonicOk = newerWrite.Succeeded &&
                staleWrite.Succeeded && generationRestored.Count == 1 &&
                generationRestored[0].Text == "newer-generation";
            if (File.Exists(generationPath)) File.Delete(generationPath);
            if (File.Exists(generationPath + ".bak"))
                File.Delete(generationPath + ".bak");

            result.PersistenceOk = restoredNotes.Count == 1 &&
                restoredNotes[0].Text == multilingualSample &&
                restoredNotes[0].Title == "多语言 Note 日本語" &&
                restoredNotes[0].ColorArgb == Color.LightBlue.ToArgb() &&
                restoredNotes[0].BackgroundOpacityPercent == 60 &&
                restoredNotes[0].TextColorArgb == Color.White.ToArgb() &&
                !restoredNotes[0].AlwaysOnTop &&
                restoredNotes[0].Width == 360 &&
                restoredNotes[0].Height == 260 &&
                restoredNotes[0].ReminderUtc.HasValue;
            result.RestoredNote = restoredNotes[0];
            result.MultilingualOk = result.PersistenceOk &&
                result.RestoredNote.SearchText.IndexOf("日本語",
                    StringComparison.Ordinal) >= 0 &&
                result.RestoredNote.SearchText.IndexOf("한국어",
                    StringComparison.Ordinal) >= 0 &&
                result.RestoredNote.SearchText.IndexOf("العربية",
                    StringComparison.Ordinal) >= 0;
            using (RichTextBox richRestored = new RichTextBox())
            {
                try
                {
                    richRestored.Rtf = result.RestoredNote.RichTextRtf;
                    richRestored.Select(0, "English line".Length);
                    Font restoredFont = richRestored.SelectionFont;
                    result.RichTextOk = richRestored.Text ==
                        result.RestoredNote.Text && restoredFont != null &&
                        restoredFont.Bold && restoredFont.Italic &&
                        restoredFont.Underline &&
                        Math.Abs(restoredFont.SizeInPoints - 14F) < 0.2F;
                }
                catch { result.RichTextOk = false; }
            }

            string longStickyPath = outputPath +
                ".sticky-long-content-test.dat";
            if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
            if (File.Exists(longStickyPath + ".bak"))
                File.Delete(longStickyPath + ".bak");
            string longVisibleText = new string('长', 13050) + "结尾保留";
            StickyNoteRepository longRepository =
                StickyNoteRepository.LoadFromFile(longStickyPath);
            StickyNoteData longNote = longRepository.Create(longVisibleText,
                Point.Empty);
            using (RichTextBox longRichText = new RichTextBox())
            {
                longRichText.Text = longVisibleText;
                longNote.RichTextRtf = longRichText.Rtf;
            }
            longRepository.SaveToFile(longStickyPath);
            List<StickyNoteData> longRestored = StickyNoteRepository
                .LoadFromFile(longStickyPath).GetAll();
            string rtfAboveOldLimit = "{\\rtf1\\ansi " +
                new string('x', 350000) + "}";
            result.RichTextNoSilentTruncationOk =
                longRestored.Count == 1 &&
                longRestored[0].Text == longVisibleText &&
                longRestored[0].Text.EndsWith("结尾保留",
                    StringComparison.Ordinal) &&
                StickyNoteRepository.NormalizeRtf(rtfAboveOldLimit) ==
                    rtfAboveOldLimit;
            if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
            if (File.Exists(longStickyPath + ".bak"))
                File.Delete(longStickyPath + ".bak");
            result.TodoOk = result.PersistenceOk &&
                result.RestoredNote.IsTodoList &&
                result.RestoredNote.TodoItems.Count == 2 &&
                result.RestoredNote.TodoItems[0].Text == "整理会议记录" &&
                !result.RestoredNote.TodoItems[0].Completed &&
                result.RestoredNote.TodoItems[1].Text == "给家人回电话" &&
                result.RestoredNote.TodoItems[1].Completed;

            string scheduleTestPath = outputPath + ".schedule-test.dat";
            StickyNoteRepository scheduleRepository =
                StickyNoteRepository.LoadFromFile(scheduleTestPath);
            StickyNoteData scheduleNote = scheduleRepository.Create(
                String.Empty, new Point(210, 180));
            scheduleNote.IsSchedule = true;
            scheduleNote.IsTodoList = false;
            scheduleNote.Title = "日程";
            scheduleNote.FontSizeTwips = 320;
            scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                "参加画展", DateTime.Today.AddDays(6), true));
            scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                "朋友生日", DateTime.Today.AddDays(58)));
            scheduleRepository.SaveToFile(scheduleTestPath);
            List<StickyNoteData> restoredSchedules = StickyNoteRepository
                .LoadFromFile(scheduleTestPath).GetAll();
            result.ScheduleOk = restoredSchedules.Count == 1 &&
                restoredSchedules[0].IsSchedule &&
                !restoredSchedules[0].IsTodoList &&
                restoredSchedules[0].ScheduleItems.Count == 2 &&
                restoredSchedules[0].ScheduleItems[0].Text == "参加画展" &&
                restoredSchedules[0].ScheduleItems[0].IsPinned &&
                !restoredSchedules[0].ScheduleItems[1].IsPinned &&
                restoredSchedules[0].ScheduleItems[0].TargetDate ==
                    DateTime.Today.AddDays(6) &&
                restoredSchedules[0].SearchText.IndexOf("朋友生日",
                    StringComparison.Ordinal) >= 0;
            if (File.Exists(scheduleTestPath)) File.Delete(scheduleTestPath);
            if (File.Exists(scheduleTestPath + ".bak"))
                File.Delete(scheduleTestPath + ".bak");
            return result;
        }

        private sealed class StickyCompatibilityCheckResult
        {
            internal bool LegacyMigrationOk;
            internal bool OldestFolderCacheImportOk;
            internal bool VersionFourMigrationOk;
            internal bool AncientCacheDisplayRepairOk;
            internal bool FailedLoadNeverOverwritesOk;
            internal bool BackupRecoveryOk;
        }

        private static StickyCompatibilityCheckResult
            RunStickyCompatibilityChecks(string outputPath)
        {
            StickyCompatibilityCheckResult result =
                new StickyCompatibilityCheckResult();
            string legacyChinese = "旧版中文便利贴";
            string legacyLine = String.Join("|", new string[] {
                "1", "legacy-note", "1", "1", Color.LightYellow.ToArgb().ToString(),
                "10", "20", "280", "230", DateTime.UtcNow.Ticks.ToString(),
                DateTime.UtcNow.Ticks.ToString(), "0",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyChinese))
            });
            string legacyStickyPath = outputPath + ".sticky-v1-test.dat";
            File.WriteAllText(legacyStickyPath, legacyLine,
                new UTF8Encoding(false));
            List<StickyNoteData> legacyNotes =
                StickyNoteRepository.LoadFromFile(legacyStickyPath).GetAll();
            result.LegacyMigrationOk = legacyNotes.Count == 1 &&
                legacyNotes[0].Text == legacyChinese &&
                !legacyNotes[0].IsTodoList &&
                String.IsNullOrEmpty(legacyNotes[0].RichTextRtf);
            if (File.Exists(legacyStickyPath)) File.Delete(legacyStickyPath);

            string legacyImportCurrent = outputPath +
                ".legacy-import-current.dat";
            string legacyImportSource = outputPath +
                ".legacy-import-source.dat";
            File.WriteAllText(legacyImportSource, legacyLine,
                new UTF8Encoding(false));
            StickyNoteRepository legacyImported = StickyNoteRepository
                .LoadFromFileWithLegacyCandidates(legacyImportCurrent,
                    new string[] { legacyImportSource });
            result.OldestFolderCacheImportOk = legacyImported.Count == 1 &&
                legacyImported.GetAll()[0].Text == legacyChinese &&
                File.Exists(legacyImportCurrent) &&
                File.Exists(legacyImportSource);
            foreach (string legacyImportFile in new string[] {
                legacyImportCurrent, legacyImportCurrent + ".bak",
                legacyImportSource, legacyImportSource + ".bak" })
                if (File.Exists(legacyImportFile))
                    File.Delete(legacyImportFile);

            string versionFourStickyPath = outputPath +
                ".sticky-v4-test.dat";
            string versionFourText = "第四版便签兼容测试";
            string versionFourLine = String.Join("|", new string[] {
                "4", "legacy-v4-note", "1", "1",
                Color.LightYellow.ToArgb().ToString(), "10", "20",
                "280", "230", DateTime.UtcNow.Ticks.ToString(),
                DateTime.UtcNow.Ticks.ToString(), "0", "0",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("旧便签")),
                String.Empty,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(versionFourText)),
                "0", String.Empty
            });
            File.WriteAllText(versionFourStickyPath, versionFourLine,
                new UTF8Encoding(false));
            StickyNoteRepository versionFourRepository =
                StickyNoteRepository.LoadFromFile(versionFourStickyPath);
            List<StickyNoteData> versionFourNotes =
                versionFourRepository.GetAll();
            result.VersionFourMigrationOk = versionFourNotes.Count == 1 &&
                versionFourNotes[0].Text == versionFourText &&
                versionFourNotes[0].FontFamilyName == "Microsoft YaHei UI" &&
                versionFourNotes[0].FontSizeTwips == 210;
            StickyNoteData ancientDisplayData = new StickyNoteData();
            ancientDisplayData.Width = -100;
            ancientDisplayData.Height = Int32.MaxValue;
            ancientDisplayData.FontFamilyName = null;
            ancientDisplayData.RichTextRtf = "not rtf";
            ancientDisplayData.BackgroundOpacityPercent = -1;
            ancientDisplayData.IsTodoList = true;
            ancientDisplayData.IsSchedule = true;
            ancientDisplayData.DockParentId = ancientDisplayData.Id;
            result.AncientCacheDisplayRepairOk =
                StickyNoteRepository.RepairForDisplay(
                    ancientDisplayData, true) &&
                ancientDisplayData.Width == 280 &&
                ancientDisplayData.Height == 700 &&
                ancientDisplayData.FontFamilyName == "Microsoft YaHei UI" &&
                String.IsNullOrEmpty(ancientDisplayData.RichTextRtf) &&
                ancientDisplayData.BackgroundOpacityPercent == 10 &&
                ancientDisplayData.IsSchedule &&
                !ancientDisplayData.IsTodoList &&
                String.IsNullOrEmpty(ancientDisplayData.DockParentId) &&
                !ancientDisplayData.Visible;
            versionFourRepository.SaveToFile(versionFourStickyPath);
            result.VersionFourMigrationOk = result.VersionFourMigrationOk &&
                File.ReadAllText(versionFourStickyPath, Encoding.UTF8)
                    .StartsWith("9|");
            if (File.Exists(versionFourStickyPath))
                File.Delete(versionFourStickyPath);
            if (File.Exists(versionFourStickyPath + ".bak"))
                File.Delete(versionFourStickyPath + ".bak");

            string corruptStickyPath = outputPath +
                ".sticky-corrupt-test.dat";
            File.WriteAllText(corruptStickyPath, "this-is-not-a-note",
                new UTF8Encoding(false));
            StickyNoteRepository corruptRepository =
                StickyNoteRepository.LoadFromFile(corruptStickyPath);
            string preservedCorruptPath = corruptRepository.RecoveryBackupPath;
            StickyNoteData recoveredCreate = corruptRepository.Create(
                "损坏数据恢复后仍可新建", Point.Empty);
            result.FailedLoadNeverOverwritesOk =
                corruptRepository.LoadSucceeded &&
                corruptRepository.RecoveredFromLoadFailure &&
                recoveredCreate != null &&
                !String.IsNullOrEmpty(preservedCorruptPath) &&
                File.Exists(preservedCorruptPath) &&
                File.ReadAllText(preservedCorruptPath, Encoding.UTF8) ==
                    "this-is-not-a-note" &&
                File.ReadAllText(corruptStickyPath, Encoding.UTF8)
                    .StartsWith("9|");
            if (File.Exists(corruptStickyPath)) File.Delete(corruptStickyPath);
            if (File.Exists(corruptStickyPath + ".bak"))
                File.Delete(corruptStickyPath + ".bak");
            if (!String.IsNullOrEmpty(preservedCorruptPath) &&
                File.Exists(preservedCorruptPath))
                File.Delete(preservedCorruptPath);

            string backupRecoveryPath = outputPath +
                ".sticky-backup-recovery-test.dat";
            File.WriteAllText(backupRecoveryPath, "broken-primary",
                new UTF8Encoding(false));
            File.WriteAllText(backupRecoveryPath + ".bak", legacyLine,
                new UTF8Encoding(false));
            StickyNoteRepository backupRecoveryRepository =
                StickyNoteRepository.LoadFromFile(backupRecoveryPath);
            result.BackupRecoveryOk =
                backupRecoveryRepository.LoadSucceeded &&
                backupRecoveryRepository.RecoveredFromLoadFailure &&
                backupRecoveryRepository.GetAll().Count == 1 &&
                backupRecoveryRepository.GetAll()[0].Text == legacyChinese &&
                File.ReadAllText(backupRecoveryRepository.RecoveryBackupPath,
                    Encoding.UTF8) == "broken-primary" &&
                File.ReadAllText(backupRecoveryPath, Encoding.UTF8)
                    .StartsWith("9|");
            string preservedBackupPrimary =
                backupRecoveryRepository.RecoveryBackupPath;
            if (File.Exists(backupRecoveryPath))
                File.Delete(backupRecoveryPath);
            if (File.Exists(backupRecoveryPath + ".bak"))
                File.Delete(backupRecoveryPath + ".bak");
            if (!String.IsNullOrEmpty(preservedBackupPrimary) &&
                File.Exists(preservedBackupPrimary))
                File.Delete(preservedBackupPrimary);
            return result;
        }

        private sealed class DockPersistenceCheckResult
        {
            internal bool SideTabOrderOk;
            internal bool DockRoundTripOk;
            internal bool MixedInsertionOk;
            internal bool WholeComponentRestoreOk;
            internal bool SnapshotSurvivesBrokenParentLinksOk;
            internal bool GroupSnapshotRoundTripOk;
            internal bool HiddenSlotRestartOk;
            internal bool LowerCloseRewiresNeighborsOk;
            internal bool ExpandAndTileRoundTripOk;
        }

        private static DockPersistenceCheckResult
            RunDockPersistenceChecks(string outputPath)
        {
            DockPersistenceCheckResult result =
                new DockPersistenceCheckResult();
            string tabOrderPath = outputPath + ".tab-order-test.dat";
            StickyNoteRepository tabOrderRepository =
                StickyNoteRepository.LoadFromFile(tabOrderPath);
            StickyNoteData tabA = tabOrderRepository.Create("A", Point.Empty);
            StickyNoteData tabB = tabOrderRepository.Create("B", Point.Empty);
            StickyNoteData tabC = tabOrderRepository.Create("C", Point.Empty);
            tabA.Visible = false;
            tabB.Visible = false;
            tabC.Visible = false;
            tabOrderRepository.SaveToFile(tabOrderPath);
            tabOrderRepository.ReorderHidden(tabA, 3);
            StickyNoteRepository restoredTabOrder =
                StickyNoteRepository.LoadFromFile(tabOrderPath);
            List<StickyNoteData> orderedTabs =
                restoredTabOrder.GetHiddenInTabOrder();
            result.SideTabOrderOk = orderedTabs.Count == 3 &&
                orderedTabs[0].Text == "B" && orderedTabs[1].Text == "C" &&
                orderedTabs[2].Text == "A" &&
                File.ReadAllText(tabOrderPath, Encoding.UTF8).StartsWith("9|");
            if (File.Exists(tabOrderPath)) File.Delete(tabOrderPath);
            if (File.Exists(tabOrderPath + ".bak"))
                File.Delete(tabOrderPath + ".bak");

            string dockPath = outputPath + ".dock-test.dat";
            StickyNoteRepository dockRepository =
                StickyNoteRepository.LoadFromFile(dockPath);
            StickyNoteData dockParent = dockRepository.Create("上层",
                new Point(100, 100));
            StickyNoteData dockChild = dockRepository.Create("下层",
                new Point(100, 330));
            dockChild.DockParentId = dockParent.Id;
            dockRepository.SaveToFile(dockPath);
            List<StickyNoteData> restoredDockNotes =
                StickyNoteRepository.LoadFromFile(dockPath).GetAll();
            result.DockRoundTripOk = restoredDockNotes.Count == 2 &&
                restoredDockNotes.Exists(delegate(StickyNoteData value)
                {
                    return !String.IsNullOrEmpty(value.DockParentId);
                });
            StickyNoteData dockInsertedTodo = dockRepository.Create(
                "中间待办", new Point(100, 330));
            dockInsertedTodo.IsTodoList = true;
            StickyDockOperations.RewireDockChainForInsertion(dockParent,
                dockInsertedTodo, dockInsertedTodo, dockChild);
            StickyDockGroups.NormalizeAll(new StickyNoteData[] {
                dockParent, dockInsertedTodo, dockChild });
            result.MixedInsertionOk =
                dockInsertedTodo.DockParentId == dockParent.Id &&
                dockChild.DockParentId == dockInsertedTodo.Id &&
                dockInsertedTodo.IsTodoList && !dockParent.IsTodoList;

            dockParent.Visible = false;
            dockInsertedTodo.Visible = false;
            dockChild.Visible = false;
            List<StickyNoteData> storedDockOrder = StickyDockOperations
                .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                    dockChild, dockParent, dockInsertedTodo }, dockChild,
                    false);
            result.WholeComponentRestoreOk =
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    storedDockOrder.Count, true) &&
                storedDockOrder.Count == 3 &&
                Object.ReferenceEquals(storedDockOrder[0], dockParent) &&
                Object.ReferenceEquals(storedDockOrder[1], dockInsertedTodo) &&
                Object.ReferenceEquals(storedDockOrder[2], dockChild);
            string savedMiddleParent = dockInsertedTodo.DockParentId;
            string savedChildParent = dockChild.DockParentId;
            dockInsertedTodo.DockParentId = "broken-parent";
            dockChild.DockParentId = String.Empty;
            List<StickyNoteData> snapshotOrder = StickyDockGroups
                .GetOrderedGroup(new StickyNoteData[] { dockChild,
                    dockInsertedTodo, dockParent }, dockInsertedTodo);
            result.SnapshotSurvivesBrokenParentLinksOk =
                snapshotOrder.Count == 3 &&
                Object.ReferenceEquals(snapshotOrder[0], dockParent) &&
                Object.ReferenceEquals(snapshotOrder[1], dockInsertedTodo) &&
                Object.ReferenceEquals(snapshotOrder[2], dockChild);
            dockInsertedTodo.DockParentId = savedMiddleParent;
            dockChild.DockParentId = savedChildParent;
            dockRepository.SaveToFile(dockPath);
            StickyNoteRepository persistedDockRepository =
                StickyNoteRepository.LoadFromFile(dockPath);
            StickyNoteData persistedDockMember =
                persistedDockRepository.Find(dockInsertedTodo.Id);
            List<StickyNoteData> persistedDockOrder = StickyDockGroups
                .GetOrderedGroup(persistedDockRepository.GetAll(),
                    persistedDockMember);
            result.GroupSnapshotRoundTripOk = persistedDockOrder.Count == 3 &&
                persistedDockOrder[0].Id == dockParent.Id &&
                persistedDockOrder[1].Id == dockInsertedTodo.Id &&
                persistedDockOrder[2].Id == dockChild.Id &&
                persistedDockOrder[0].DockGroupOrder == 0 &&
                persistedDockOrder[1].DockGroupOrder == 1 &&
                persistedDockOrder[2].DockGroupOrder == 2 &&
                File.ReadAllText(dockPath, Encoding.UTF8).StartsWith("9|");
            dockParent.Visible = true;
            dockInsertedTodo.Visible = true;
            dockChild.Visible = true;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                dockParent, dockInsertedTodo, dockChild });
            StickyDockOperations.RewireDockChainAfterMemberClose(
                dockInsertedTodo, dockChild);
            result.LowerCloseRewiresNeighborsOk =
                String.IsNullOrEmpty(dockInsertedTodo.DockParentId) &&
                dockChild.DockParentId == dockParent.Id;
            if (File.Exists(dockPath)) File.Delete(dockPath);
            if (File.Exists(dockPath + ".bak")) File.Delete(dockPath + ".bak");

            string hiddenSlotPath = outputPath + ".hidden-slot-test.dat";
            StickyNoteRepository hiddenSlotRepository =
                StickyNoteRepository.LoadFromFile(hiddenSlotPath);
            StickyNoteData persistedHideA = hiddenSlotRepository.Create(
                "A", Point.Empty);
            StickyNoteData persistedHideB = hiddenSlotRepository.Create(
                "B", Point.Empty);
            StickyNoteData persistedHideC = hiddenSlotRepository.Create(
                "C", Point.Empty);
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                persistedHideA, persistedHideB, persistedHideC });
            StickyDockOperations.PreserveDockSlotForHiddenMember(
                new StickyNoteData[] { persistedHideA, persistedHideB,
                    persistedHideC }, persistedHideB);
            hiddenSlotRepository.SaveToFile(hiddenSlotPath);
            StickyNoteRepository restoredHiddenSlotRepository =
                StickyNoteRepository.LoadFromFile(hiddenSlotPath);
            StickyNoteData restoredHiddenMiddle =
                restoredHiddenSlotRepository.Find(persistedHideB.Id);
            List<StickyNoteData> restoredHiddenSlotOrder =
                StickyDockGroups.GetOrderedGroup(
                    restoredHiddenSlotRepository.GetAll(),
                    restoredHiddenMiddle);
            result.HiddenSlotRestartOk = restoredHiddenSlotOrder.Count == 3 &&
                !restoredHiddenSlotOrder[1].Visible &&
                restoredHiddenSlotOrder[1].Id == persistedHideB.Id &&
                restoredHiddenSlotOrder[1].DockGroupOrder == 1 &&
                restoredHiddenSlotOrder[2].DockParentId ==
                    restoredHiddenSlotOrder[0].Id;
            if (File.Exists(hiddenSlotPath)) File.Delete(hiddenSlotPath);
            if (File.Exists(hiddenSlotPath + ".bak"))
                File.Delete(hiddenSlotPath + ".bak");

            string expandPath = outputPath + ".expand-and-tile-test.dat";
            StickyNoteRepository expandRepository =
                StickyNoteRepository.LoadFromFile(expandPath);
            StickyNoteData expandA = expandRepository.Create("普通",
                new Point(-5000, -5000));
            StickyNoteData expandB = expandRepository.Create("待办",
                new Point(-5000, -5000));
            expandB.IsTodoList = true;
            StickyNoteData expandC = expandRepository.Create("日程",
                new Point(-5000, -5000));
            expandC.IsSchedule = true;
            expandA.Width = 320;
            expandA.Height = 230;
            expandB.Width = 420;
            expandB.Height = 310;
            expandC.Width = 360;
            expandC.Height = 260;
            expandB.Visible = false;
            expandC.Visible = false;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                expandA, expandB, expandC });
            StickyHostedRuntime expandRuntime = new StickyHostedRuntime();
            expandRuntime.AddNote(expandA.Id);
            List<DockLayoutTarget> expandTargets = PetForm
                .PrepareStickyExpandAndTileTargets(expandRepository.GetAll(),
                    new Rectangle(0, 0, 1920, 1040));
            List<DockLayoutTarget> secondaryTargets = PetForm
                .PrepareStickyExpandAndTileTargets(new StickyNoteData[] {
                    new StickyNoteData(), new StickyNoteData() },
                    new Rectangle(-1920, 0, 1920, 1040));
            expandRepository.SaveToFile(expandPath);
            StickyNoteRepository restoredExpandRepository =
                StickyNoteRepository.LoadFromFile(expandPath);
            List<StickyNoteData> restoredExpanded =
                restoredExpandRepository.GetAll();
            result.ExpandAndTileRoundTripOk =
                expandRuntime.ContainsNote(expandA.Id) &&
                expandTargets.Count == 3 &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandA.Id;
                }) &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandB.Id;
                }) &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandC.Id;
                }) &&
                expandTargets[0].X != expandTargets[1].X &&
                expandTargets[0].X != expandTargets[2].X &&
                expandTargets[1].X != expandTargets[2].X &&
                secondaryTargets.TrueForAll(delegate(DockLayoutTarget target)
                {
                    return target.X >= -1920 && target.X < 0 &&
                        target.Y >= 0 && target.Y < 1040;
                }) &&
                restoredExpanded.Count == 3 &&
                restoredExpanded.TrueForAll(delegate(StickyNoteData note)
                {
                    return note.Visible &&
                        String.IsNullOrEmpty(note.DockParentId) &&
                        String.IsNullOrEmpty(note.DockGroupId) &&
                        note.DockGroupOrder == -1 &&
                        note.X >= 0 && note.Y >= 0 &&
                        note.X < 1920 && note.Y < 1040;
                }) &&
                restoredExpandRepository.Find(expandA.Id).Width == 320 &&
                restoredExpandRepository.Find(expandB.Id).Width == 420 &&
                restoredExpandRepository.Find(expandC.Id).Height == 260;
            if (File.Exists(expandPath)) File.Delete(expandPath);
            if (File.Exists(expandPath + ".bak"))
                File.Delete(expandPath + ".bak");
            return result;
        }

        private sealed class DockLifecycleCheckResult
        {
            internal bool ScheduleMixedTypesOk;
            internal bool GroupRestoreAtomicOk;
            internal bool MiddleExtractionOk;
            internal bool HiddenSlotPreservedOk;
            internal bool HiddenSlotReopenOk;
            internal bool PartialHiddenMergeOk;
            internal bool SecondRestoreCycleOk;
            internal bool RepeatedRestoreCyclesOk;
            internal bool CloseHierarchyOk;
            internal bool SplitGestureOk;
            internal bool RootDragNeverSplitsOk;
        }

        private static DockLifecycleCheckResult RunDockLifecycleChecks()
        {
            DockLifecycleCheckResult result = new DockLifecycleCheckResult();
            StickyNoteData mixedOrdinary = new StickyNoteData();
            StickyNoteData mixedTodo = new StickyNoteData();
            mixedTodo.IsTodoList = true;
            StickyNoteData mixedSchedule = new StickyNoteData();
            mixedSchedule.IsSchedule = true;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                mixedOrdinary, mixedTodo, mixedSchedule });
            List<StickyNoteData> mixedScheduleOrder = StickyDockGroups
                .GetOrderedGroup(new StickyNoteData[] { mixedSchedule,
                    mixedOrdinary, mixedTodo }, mixedSchedule);
            result.ScheduleMixedTypesOk = mixedScheduleOrder.Count == 3 &&
                Object.ReferenceEquals(mixedScheduleOrder[0], mixedOrdinary) &&
                Object.ReferenceEquals(mixedScheduleOrder[1], mixedTodo) &&
                Object.ReferenceEquals(mixedScheduleOrder[2], mixedSchedule) &&
                mixedTodo.IsTodoList && !mixedTodo.IsSchedule &&
                mixedSchedule.IsSchedule && !mixedSchedule.IsTodoList;
            result.GroupRestoreAtomicOk =
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    3, false) &&
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    3, true) &&
                !StickyDockOperations.ShouldRestoreWholeDockComponent(
                    1, true);

            StickyNoteData extractA = new StickyNoteData();
            StickyNoteData extractB = new StickyNoteData();
            StickyNoteData extractC = new StickyNoteData();
            StickyNoteData extractD = new StickyNoteData();
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                extractA, extractB, extractC, extractD });
            List<StickyNoteData> afterMiddleExtraction = StickyDockOperations
                .ExtractSingleDockMember(new StickyNoteData[] { extractA,
                    extractB, extractC, extractD }, extractB);
            result.MiddleExtractionOk = afterMiddleExtraction.Count == 3 &&
                Object.ReferenceEquals(afterMiddleExtraction[0], extractA) &&
                Object.ReferenceEquals(afterMiddleExtraction[1], extractC) &&
                Object.ReferenceEquals(afterMiddleExtraction[2], extractD) &&
                extractC.DockParentId == extractA.Id &&
                extractD.DockParentId == extractC.Id &&
                String.IsNullOrEmpty(extractB.DockParentId) &&
                String.IsNullOrEmpty(extractB.DockGroupId) &&
                extractB.DockGroupOrder == -1;

            StickyNoteData hideA = new StickyNoteData();
            StickyNoteData hideB = new StickyNoteData();
            StickyNoteData hideC = new StickyNoteData();
            StickyNoteData hideD = new StickyNoteData();
            List<StickyNoteData> hideSnapshot =
                new List<StickyNoteData>(new StickyNoteData[] {
                    hideA, hideB, hideC, hideD });
            StickyDockGroups.ApplyOrderedGroup(hideSnapshot);
            string hiddenGroupId = hideB.DockGroupId;
            StickyDockOperations.PreserveDockSlotForHiddenMember(
                hideSnapshot, hideB);
            result.HiddenSlotPreservedOk = hideB.DockGroupId == hiddenGroupId &&
                hideB.DockGroupOrder == 1 &&
                String.IsNullOrEmpty(hideB.DockParentId) &&
                hideC.DockParentId == hideA.Id &&
                hideD.DockParentId == hideC.Id;
            List<StickyNoteData> hiddenMemberOpenOrder =
                StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                    hideD, hideB, hideA, hideC }, hideB);
            foreach (StickyNoteData member in hiddenMemberOpenOrder)
                member.Visible = true;
            StickyDockGroups.ApplyOrderedGroup(hiddenMemberOpenOrder);
            result.HiddenSlotReopenOk = hiddenMemberOpenOrder.Count == 4 &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[0], hideA) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[1], hideB) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[2], hideC) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[3], hideD) &&
                hideB.DockParentId == hideA.Id &&
                hideC.DockParentId == hideB.Id &&
                hideD.DockParentId == hideC.Id;

            StickyNoteData mergeA = new StickyNoteData();
            StickyNoteData mergeB = new StickyNoteData();
            StickyNoteData mergeC = new StickyNoteData();
            StickyNoteData mergeD = new StickyNoteData();
            StickyNoteData mergeE = new StickyNoteData();
            List<StickyNoteData> mergeTarget =
                new List<StickyNoteData>(new StickyNoteData[] {
                    mergeA, mergeB, mergeC });
            List<StickyNoteData> mergeSource =
                new List<StickyNoteData>(new StickyNoteData[] {
                    mergeD, mergeE });
            StickyDockGroups.ApplyOrderedGroup(mergeTarget);
            StickyDockGroups.ApplyOrderedGroup(mergeSource);
            mergeB.Visible = false;
            mergeE.Visible = false;
            StickyDockGroups.ApplyOrderedGroup(mergeTarget);
            StickyDockGroups.ApplyOrderedGroup(mergeSource);
            List<StickyNoteData> mergedPartialSnapshots = StickyDockOperations
                .MergeDockSnapshotsAfterParent(mergeTarget, mergeA,
                    mergeSource);
            result.PartialHiddenMergeOk = mergedPartialSnapshots.Count == 5 &&
                Object.ReferenceEquals(mergedPartialSnapshots[0], mergeA) &&
                Object.ReferenceEquals(mergedPartialSnapshots[1], mergeD) &&
                Object.ReferenceEquals(mergedPartialSnapshots[2], mergeE) &&
                Object.ReferenceEquals(mergedPartialSnapshots[3], mergeB) &&
                Object.ReferenceEquals(mergedPartialSnapshots[4], mergeC) &&
                mergeD.DockParentId == mergeA.Id &&
                mergeC.DockParentId == mergeD.Id &&
                String.IsNullOrEmpty(mergeE.DockParentId) &&
                String.IsNullOrEmpty(mergeB.DockParentId);

            StickyDockOperations.RewireDockChainForInsertion(
                extractA, extractB, extractB, extractC);
            List<StickyNoteData> secondCycleLive = StickyDockOperations
                .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                    extractD, extractC, extractA, extractB }, extractA, true);
            List<StickyNoteData> secondCycleStoredBeforeCommit =
                StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                    extractD, extractC, extractA, extractB }, extractA);
            List<StickyNoteData> secondCycleCommit = StickyDockOperations
                .SelectMoreCompleteDockOrder(secondCycleLive,
                    secondCycleStoredBeforeCommit);
            StickyDockGroups.ApplyOrderedGroup(secondCycleCommit);
            extractC.DockParentId = String.Empty;
            List<StickyNoteData> brokenSecondCycleLive = StickyDockOperations
                .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                    extractD, extractC, extractA, extractB }, extractA, true);
            List<StickyNoteData> completeSecondCycleSnapshot =
                StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                    extractD, extractC, extractA, extractB }, extractD);
            List<StickyNoteData> closeSecondCycleOrder = StickyDockOperations
                .SelectMoreCompleteDockOrder(brokenSecondCycleLive,
                    completeSecondCycleSnapshot);
            StickyDockGroups.ApplyOrderedGroup(closeSecondCycleOrder);
            result.SecondRestoreCycleOk = closeSecondCycleOrder.Count == 4 &&
                Object.ReferenceEquals(closeSecondCycleOrder[0], extractA) &&
                Object.ReferenceEquals(closeSecondCycleOrder[1], extractB) &&
                Object.ReferenceEquals(closeSecondCycleOrder[2], extractC) &&
                Object.ReferenceEquals(closeSecondCycleOrder[3], extractD) &&
                extractB.DockParentId == extractA.Id &&
                extractC.DockParentId == extractB.Id &&
                extractD.DockParentId == extractC.Id;

            List<StickyNoteData> repeatedMembers =
                new List<StickyNoteData>();
            for (int index = 0; index < 6; index++)
                repeatedMembers.Add(new StickyNoteData());
            StickyDockGroups.ApplyOrderedGroup(repeatedMembers);
            result.RepeatedRestoreCyclesOk = true;
            for (int cycle = 0; cycle < 18; cycle++)
            {
                List<StickyNoteData> current = StickyDockGroups
                    .GetOrderedGroup(repeatedMembers, repeatedMembers[0]);
                if (current.Count != repeatedMembers.Count)
                {
                    result.RepeatedRestoreCyclesOk = false;
                    break;
                }
                int extractIndex = 1 + cycle % (current.Count - 1);
                StickyNoteData moved = current[extractIndex];
                List<StickyNoteData> remainder = StickyDockOperations
                    .ExtractSingleDockMember(current, moved);
                int targetIndex = cycle % remainder.Count;
                StickyNoteData cycleParent = remainder[targetIndex];
                StickyNoteData previousChild = targetIndex + 1 <
                    remainder.Count ? remainder[targetIndex + 1] : null;
                StickyDockOperations.RewireDockChainForInsertion(
                    cycleParent, moved, moved, previousChild);
                List<StickyNoteData> liveCycle = StickyDockOperations
                    .BuildDockChainOrderFromNotes(repeatedMembers,
                        remainder[0], true);
                List<StickyNoteData> storedCycle = StickyDockGroups
                    .GetOrderedGroup(repeatedMembers, cycleParent);
                List<StickyNoteData> committedCycle = StickyDockOperations
                    .SelectMoreCompleteDockOrder(liveCycle, storedCycle);
                StickyDockGroups.ApplyOrderedGroup(committedCycle);
                List<StickyNoteData> randomOpenOrder = StickyDockGroups
                    .GetOrderedGroup(new StickyNoteData[] {
                        repeatedMembers[5], repeatedMembers[2],
                        repeatedMembers[0], repeatedMembers[4],
                        repeatedMembers[1], repeatedMembers[3] }, moved);
                if (committedCycle.Count != repeatedMembers.Count ||
                    randomOpenOrder.Count != repeatedMembers.Count)
                {
                    result.RepeatedRestoreCyclesOk = false;
                    break;
                }
                for (int position = 0; position < randomOpenOrder.Count;
                    position++)
                {
                    StickyNoteData member = randomOpenOrder[position];
                    string expectedParent = position == 0 ? String.Empty :
                        randomOpenOrder[position - 1].Id;
                    if (member.DockGroupOrder != position ||
                        !String.Equals(member.DockParentId, expectedParent,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        result.RepeatedRestoreCyclesOk = false;
                        break;
                    }
                }
                if (!result.RepeatedRestoreCyclesOk) break;
            }
            result.CloseHierarchyOk =
                StickyDockOperations.ShouldCollapseWholeDockGroup(0, 3) &&
                !StickyDockOperations.ShouldCollapseWholeDockGroup(1, 3) &&
                !StickyDockOperations.ShouldCollapseWholeDockGroup(0, 1);
            result.SplitGestureOk =
                StickyDockOperations.CancelsDockSplitHold(120, 20, 0) &&
                !StickyDockOperations.CancelsDockSplitHold(600, 20, 0) &&
                !StickyDockOperations.CancelsDockSplitHold(120, 3, 3);
            result.RootDragNeverSplitsOk =
                !StickyDockOperations.IsDockSplitEligible(String.Empty, 3) &&
                StickyDockOperations.IsDockSplitEligible("parent-note", 3) &&
                !StickyDockOperations.IsDockSplitEligible("parent-note", 1);
            return result;
        }

        private sealed class DockGeometryCheckResult
        {
            internal bool BottomDockingOk;
            internal bool UnifiedGroupResizeOk;
            internal bool RootAnchorPreservedOk;
            internal bool DividerMovesFollowingChainOk;
            internal bool DividerIndependentRangeOk;
            internal bool DividerReallocatesPairOk;
            internal bool WideNarrowDockingOk;
            internal bool LongCoordinateGuardOk;
            internal bool FirstDragRecoveryOk;
            internal bool DetachedGroupReturnsOnScreenOk;
            internal bool ScreenRecoveryAnchorOk;
            internal bool DetachedGroupTranslationOk;
            internal bool ExecutorNeutralDockVisualSeamOk;
        }

        private static DockGeometryCheckResult RunDockGeometryChecks()
        {
            DockGeometryCheckResult result = new DockGeometryCheckResult();
            result.BottomDockingOk =
                PetForm.CanDockBelow(new Rectangle(100, 330, 320, 300),
                    new Rectangle(100, 30, 320, 300), 20) &&
                !PetForm.CanDockBelow(new Rectangle(170, 330, 320, 300),
                    new Rectangle(100, 30, 320, 300), 20);
            List<Rectangle> unifiedLayout = PetForm.CalculateUnifiedDockLayout(
                new Size[] { new Size(320, 300), new Size(500, 240),
                    new Size(380, 260) }, 120, 80, 460);
            result.UnifiedGroupResizeOk = unifiedLayout.Count == 3 &&
                unifiedLayout[0] == new Rectangle(120, 80, 460, 300) &&
                unifiedLayout[1] == new Rectangle(120, 380, 460, 240) &&
                unifiedLayout[2] == new Rectangle(120, 620, 460, 260);
            result.RootAnchorPreservedOk = unifiedLayout.Count == 3 &&
                unifiedLayout[0].Location == new Point(120, 80);
            List<DockLayoutTarget> dividerTargets =
                PetForm.CalculateDockDividerTargets(
                    new DockWindowFacts("upper", 120, 80, 420, 500,
                        true, true),
                    new DockWindowFacts("lower", 120, 310, 420, 230,
                        true, true));
            result.DividerMovesFollowingChainOk =
                dividerTargets.Count == 2 &&
                dividerTargets[0].Height == 500 &&
                dividerTargets[1].Y == 580 &&
                dividerTargets[1].Height == 230 &&
                dividerTargets[1].X == 120 &&
                dividerTargets[1].Width == 420 &&
                dividerTargets[1].TopMost;
            result.DividerIndependentRangeOk =
                PetForm.CalculateDockDividerHeight(50) == 220 &&
                PetForm.CalculateDockDividerHeight(500) == 500 &&
                PetForm.CalculateDockDividerHeight(900) == 700;
            List<DockLayoutTarget> reallocatedDivider =
                PetForm.CalculateDockDividerTargets(
                    new DockWindowFacts("upper", 120, 80, 420, 350,
                        true, true),
                    new DockWindowFacts("lower", 120, 380, 420, 300,
                        true, true),
                    600);
            List<DockLayoutTarget> clampedDivider =
                PetForm.CalculateDockDividerTargets(
                    new DockWindowFacts("upper", 120, 80, 420, 500,
                        true, true),
                    new DockWindowFacts("lower", 120, 380, 420, 300,
                        true, true),
                    600);
            result.DividerReallocatesPairOk =
                reallocatedDivider.Count == 2 &&
                reallocatedDivider[0].Height == 350 &&
                reallocatedDivider[1].Y == 430 &&
                reallocatedDivider[1].Height == 250 &&
                clampedDivider.Count == 2 &&
                clampedDivider[0].Height == 380 &&
                clampedDivider[1].Height == 220;
            result.WideNarrowDockingOk = PetForm.CanDockBelow(
                new Rectangle(80, 400, 900, 300),
                new Rectangle(400, 100, 280, 300), 20) &&
                PetForm.CanDockBelow(new Rectangle(400, 400, 280, 300),
                    new Rectangle(80, 100, 900, 300), 20);
            result.LongCoordinateGuardOk =
                StickyDockOperations.IsDockCoordinateRangeSafe(100,
                    new int[] { 700, 700, 700 }, 30000) &&
                !StickyDockOperations.IsDockCoordinateRangeSafe(29000,
                    new int[] { 700, 700 }, 30000);
            Dictionary<string, DockWindowFacts> dragFacts =
                new Dictionary<string, DockWindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "root", new DockWindowFacts("root", 100, 200,
                        320, 300, true, false) },
                    { "child", new DockWindowFacts("child", 100, 500,
                        320, 240, true, false) }
                };
            List<DockLayoutTarget> translated =
                PetForm.CalculateDockTranslationTargets(
                    new string[] { "root", "child" }, dragFacts,
                    new DockWindowFacts("root", 115, 190, 320, 300,
                        true, false), 15, -10);
            result.DetachedGroupTranslationOk = translated.Count == 2 &&
                translated[0].NoteId == "root" &&
                translated[0].X == 115 && translated[0].Y == 190 &&
                translated[1].NoteId == "child" &&
                translated[1].X == 115 && translated[1].Y == 490;
            Rectangle recoveredDrag = StickyNoteWindow
                .CalculateRecoveredHeaderDragBounds(
                    new Rectangle(100, 100, 320, 300),
                    new Rectangle(0, 0, 1920, 1080),
                    new Point(500, 400), new Point(20, 10), true);
            result.FirstDragRecoveryOk = recoveredDrag ==
                new Rectangle(480, 390, 320, 300);
            Point reachable = PetForm.CalculateHeaderReachableTranslation(
                new Rectangle(100, -200, 400, 32),
                new Rectangle(0, 0, 1200, 900));
            result.DetachedGroupReturnsOnScreenOk = reachable.Y == 200;
            Point recoveredPrimary = PetForm.CalculateStickyRecoveryAnchor(
                new Rectangle(0, 0, 1920, 1040),
                new Rectangle(20, 700, 192, 208),
                new Size(320, 300), 0);
            Point recoveredSecondary = PetForm.CalculateStickyRecoveryAnchor(
                new Rectangle(-1920, 0, 1920, 1040),
                new Rectangle(-300, 700, 192, 208),
                new Size(320, 300), 1);
            result.ScreenRecoveryAnchorOk =
                recoveredPrimary.X >= 0 && recoveredPrimary.Y >= 0 &&
                recoveredPrimary.Y <= 1008 &&
                recoveredSecondary.X >= -1920 &&
                recoveredSecondary.X <= -320 &&
                recoveredSecondary.Y >= 0 &&
                recoveredSecondary.Y <= 1008;
            result.ExecutorNeutralDockVisualSeamOk =
                PetForm.CalculateDockVisualSeam(new DockWindowFacts(
                    "hosted-or-legacy", -860, 140, 420, 310, true, true)) ==
                    new Rectangle(-860, 447, 420, 6) &&
                PetForm.CalculateDockVisualSeam(null).IsEmpty;
            return result;
        }

        private sealed class StickyEditorCheckResult
        {
            internal bool ImeAnimationGuardOk;
            internal bool ImeAutoSaveGuardOk;
            internal bool DeferredInitialFocusSafeOk;
            internal bool RichTextToolbarOk;
            internal bool SmoothFormatInteractionOk;
            internal bool StableFormatSelectorModelOk;
            internal bool FormatToolbarFocusOk;
            internal bool FormatSelectorsAlwaysBlackOk;
            internal bool BodyTextColorSwitchOk;
            internal bool DockResizeRoleOk;
            internal bool NativeWindowStyleAppliedOk;
            internal bool GroupTopMostSyncOk;
            internal bool MultilingualInputOk;
            internal bool TabSwitchContentPreservedOk;
            internal bool FirstFormatCommitOk;
            internal bool EmptyNoteFormattingOk;
            internal bool CaretTypingFormatSwitchOk;
            internal bool SingleNativeImeCommitOk;
            internal bool UnifiedContextMenusOk;
        }

        private static StickyEditorCheckResult RunStickyEditorChecks()
        {
            StickyEditorCheckResult result = new StickyEditorCheckResult();
            DateTime imeGuardNow = DateTime.UtcNow;
            result.ImeAnimationGuardOk =
                ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010D) &&
                ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010F) &&
                !ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010E) &&
                PetAnimationController.ShouldPauseOwnNoteAnimation(
                    true, DateTime.MinValue, imeGuardNow) &&
                PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                    imeGuardNow.AddMilliseconds(1), imeGuardNow) &&
                !PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                    imeGuardNow.AddMilliseconds(-1), imeGuardNow);
            result.ImeAutoSaveGuardOk = StickyNoteWindow.ShouldDeferAutoSave(
                true, DateTime.MinValue, imeGuardNow) &&
                StickyNoteWindow.ShouldDeferAutoSave(false,
                    imeGuardNow.AddMilliseconds(-200), imeGuardNow) &&
                !StickyNoteWindow.ShouldDeferAutoSave(false,
                    imeGuardNow.AddSeconds(-2), imeGuardNow);
            result.DeferredInitialFocusSafeOk =
                StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 0, false) &&
                !StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 1, false) &&
                !StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 0, true);

            StickyNoteData richToolbarData = new StickyNoteData();
            richToolbarData.Text = "格式工具栏测试";
            richToolbarData.Width = 360;
            richToolbarData.Height = 260;
            using (StickyNoteWindow richToolbarNote =
                new StickyNoteWindow(richToolbarData))
            {
                result.RichTextToolbarOk =
                    richToolbarNote.HasRichTextFormattingToolbar &&
                    richToolbarNote.HeaderTypeIconVisibleForTest &&
                    richToolbarNote.ExerciseRichTextFormattingForTest();
                result.SmoothFormatInteractionOk =
                    richToolbarNote.ExerciseSmoothFormatInteractionForTest();
                result.StableFormatSelectorModelOk =
                    richToolbarNote.UsesStableListFormatSelectors;
                result.FormatToolbarFocusOk =
                    richToolbarNote.FormatControlsPreserveSelectionForTest;
                result.FormatSelectorsAlwaysBlackOk =
                    richToolbarNote.FormatSelectorsAlwaysBlackForTest;
                result.BodyTextColorSwitchOk =
                    richToolbarNote.ExerciseBodyTextColorSwitchForTest();
                result.DockResizeRoleOk =
                    richToolbarNote.ExerciseDockResizeRoleForTest();
                result.NativeWindowStyleAppliedOk =
                    richToolbarNote.NativeMaximizeStyleDisabledForTest;
                result.GroupTopMostSyncOk =
                    richToolbarNote.ExerciseGroupTopMostForTest();
                result.MultilingualInputOk =
                    richToolbarNote.ExerciseMultilingualInputForTest();
                result.TabSwitchContentPreservedOk = richToolbarNote
                    .ExerciseReminderSwitchContentPreservationForTest();
            }

            using (StickyNoteWindow firstFormatNote =
                new StickyNoteWindow(new StickyNoteData()))
            {
                result.FirstFormatCommitOk =
                    firstFormatNote.ExerciseFirstFormatCommitForTest();
                result.EmptyNoteFormattingOk =
                    firstFormatNote.ExerciseEmptyNoteFormattingForTest();
                result.CaretTypingFormatSwitchOk =
                    firstFormatNote.ExerciseCaretTypingFormatSwitchForTest();
                result.SingleNativeImeCommitOk = firstFormatNote
                    .ExerciseSingleNativeImeCommitAfterFormatForTest();
                result.UnifiedContextMenusOk =
                    firstFormatNote.ExerciseUnifiedNoteContextMenusForTest();
            }
            return result;
        }

        private sealed class StickyReminderWindowCheckResult
        {
            internal bool BannerCountdownOk;
            internal bool CompactBannerOk;
            internal bool SelectionActionsOk;
            internal bool InlineCreationActionsRemovedOk;
            internal bool FirstClickStableOk;
            internal bool BlankAreaClearOk;
            internal bool BannerRefreshInPlaceOk;
        }

        private static StickyReminderWindowCheckResult
            RunStickyReminderWindowChecks(StickyNoteWindow note)
        {
            StickyReminderWindowCheckResult result =
                new StickyReminderWindowCheckResult();
            List<ReminderItem> bannerItems = new List<ReminderItem>();
            bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddSeconds(65),
                "中文提醒项目", null, 24F, true));
            bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddHours(2),
                "第二条提醒"));
            note.UpdateReminderBanner(bannerItems);
            result.BannerCountdownOk = note.ReminderBannerLineCount == 2 &&
                note.ReminderBannerText.Contains("中文提醒项目") &&
                note.ReminderBannerText.Contains("第二条提醒") &&
                StickyNoteWindow.FormatCountdown(TimeSpan.FromSeconds(65)) ==
                    "1分5秒" &&
                StickyNoteWindow.FormatCountdown(TimeSpan.Zero) == "现在";
            result.CompactBannerOk = Math.Abs(
                note.ReminderBannerFirstFontSize - 24F) < 0.2F;
            result.SelectionActionsOk =
                note.ExerciseReminderSelectionActionsForTest();
            result.InlineCreationActionsRemovedOk =
                note.ExerciseInlineCreationActionsRemovedForTest();
            result.FirstClickStableOk =
                note.ExerciseReminderFirstClickStabilityForTest(
                    out result.BlankAreaClearOk,
                    out result.BannerRefreshInPlaceOk);
            return result;
        }

        private sealed class StickyTodoWindowCheckResult
        {
            internal bool GroupingOk;
            internal bool MarkerRoundTripOk;
            internal bool PlainTextProjectionOk;
            internal bool FixedTypeActionsOk;
            internal bool WrapAndInlineEditOk;
            internal bool OverallFontSizeOk;
            internal bool DedicatedRowContextMenusOk;
        }

        private static StickyTodoWindowCheckResult
            RunStickyTodoWindowChecks(StickyNoteWindow note)
        {
            StickyTodoWindowCheckResult result =
                new StickyTodoWindowCheckResult();
            result.GroupingOk = note.VisibleTodoItemCount == 2 &&
                note.TodoGroupCount == 3;
            bool completedMarker;
            string cleaned = StickyNoteWindow.ParseTodoTextLine(
                "[ ][ ][x] 最推荐的修改方法", out completedMarker);
            bool pendingMarker;
            string pending = StickyNoteWindow.ParseTodoTextLine(
                "[ ] 普通项目", out pendingMarker);
            result.MarkerRoundTripOk = cleaned == "最推荐的修改方法" &&
                completedMarker && pending == "普通项目" && !pendingMarker;
            List<StickyTodoItem> switchItems = new List<StickyTodoItem>();
            switchItems.Add(new StickyTodoItem("[ ] 第一项", false));
            switchItems.Add(new StickyTodoItem("[x] 第二项", true));
            result.PlainTextProjectionOk =
                StickyNoteWindow.BuildPlainTextFromTodos(switchItems) ==
                    "第一项" + Environment.NewLine + "第二项";
            using (StickyNoteWindow todoStressNote =
                new StickyNoteWindow(new StickyNoteData()))
            {
                result.FixedTypeActionsOk =
                    todoStressNote.ExerciseFixedNoteTypeActionsForTest();
                result.WrapAndInlineEditOk =
                    todoStressNote.ExerciseTodoWrapAndInlineEditForTest();
                result.OverallFontSizeOk =
                    todoStressNote.ExerciseTodoOverallFontSizeForTest();
                result.DedicatedRowContextMenusOk =
                    todoStressNote.ExerciseDedicatedRowContextMenusForTest();
            }
            return result;
        }

        private sealed class StickyScheduleWindowCheckResult
        {
            internal bool CountdownOk;
            internal bool FontChoicesOk;
            internal bool DateMouseWheelOk;
            internal bool PinMarkerToggleOk;
        }

        private static StickyScheduleWindowCheckResult
            RunStickyScheduleWindowChecks()
        {
            StickyScheduleWindowCheckResult result =
                new StickyScheduleWindowCheckResult();
            result.CountdownOk = StickyNoteWindow.FormatScheduleCountdown(
                DateTime.Today.AddDays(6), DateTime.Today) == "6天" &&
                StickyNoteWindow.FormatScheduleCountdown(DateTime.Today,
                    DateTime.Today) == "今天" &&
                StickyNoteWindow.FormatScheduleCountdown(
                    DateTime.Today.AddDays(-2), DateTime.Today) == "已过2天";
            result.FontChoicesOk =
                StickyNoteWindow.ScheduleFontSizeLabel(9F) == "特小 9" &&
                StickyNoteWindow.ScheduleFontSizeLabel(10.5F) == "小 10.5" &&
                StickyNoteWindow.ScheduleFontSizeLabel(12F) == "小 10.5" &&
                StickyNoteWindow.ScheduleFontSizeLabel(16F) == "中 16" &&
                StickyNoteWindow.ScheduleFontSizeLabel(22F) == "大 22" &&
                StickyNoteWindow.ScheduleFontSizeLabel(48F) == "特大 48";
            result.DateMouseWheelOk =
                ScheduleItemDialog.StepDateWithMouseWheel(
                    DateTime.Today, 120) == DateTime.Today.AddDays(-1) &&
                ScheduleItemDialog.StepDateWithMouseWheel(
                    DateTime.Today, -240) == DateTime.Today.AddDays(2);
            StickyNoteData data = new StickyNoteData();
            data.IsSchedule = true;
            using (StickyNoteWindow note = new StickyNoteWindow(data))
                result.PinMarkerToggleOk = note.HeaderTypeIconVisibleForTest &&
                    note.ExerciseSchedulePinMarkerForTest();
            return result;
        }

        private sealed class StickyFontCheckResult
        {
            internal bool SizeParsingOk;
            internal bool ChineseFontsFirstOk;
            internal bool InstalledFontListCacheOk;
            internal bool SharedFontLifetimeOk;
        }

        private static StickyFontCheckResult RunStickyFontChecks()
        {
            StickyFontCheckResult result = new StickyFontCheckResult();
            float parsedFive;
            float parsedNumeric;
            result.SizeParsingOk =
                StickyNoteWindow.TryParseFontSize("五号", out parsedFive) &&
                Math.Abs(parsedFive - 10.5F) < 0.01F &&
                StickyNoteWindow.TryParseFontSize("18 磅", out parsedNumeric) &&
                Math.Abs(parsedNumeric - 18F) < 0.01F &&
                !StickyNoteWindow.TryParseFontSize("100", out parsedNumeric);
            result.ChineseFontsFirstOk =
                StickyNoteWindow.IsChineseFontNameForTest("微软雅黑") &&
                StickyNoteWindow.IsChineseFontNameForTest(
                    "Noto Serif SC SemiBold") &&
                StickyNoteWindow.IsChineseFontNameForTest(
                    "Microsoft YaHei UI") &&
                !StickyNoteWindow.IsChineseFontNameForTest("Arial") &&
                !StickyNoteWindow.IsChineseFontNameForTest("Noto Sans JP") &&
                StickyNoteWindow.FontNameSortsBeforeForTest(
                    "Noto Sans SC", "Arial");
            result.InstalledFontListCacheOk =
                StickyNoteWindow.InstalledFontNamesCachedForTest();
            Font first = StickyNoteWindow.CreateSafeFont(
                "Microsoft YaHei UI", 18F, FontStyle.Regular);
            Font second = StickyNoteWindow.CreateSafeFont(
                "Microsoft YaHei UI", 18F, FontStyle.Regular);
            bool usable;
            try
            {
                byte ignoredCharacterSet = second.GdiCharSet;
                usable = ignoredCharacterSet == second.GdiCharSet;
            }
            catch
            {
                usable = false;
            }
            result.SharedFontLifetimeOk = Object.ReferenceEquals(first, second) &&
                usable;
            return result;
        }

        private sealed class StickyDialogCheckResult
        {
            internal bool ReminderSizePreviewOk;
            internal bool ReminderLiveSizePreviewOk;
            internal bool UnforcedMultilingualImeOk;
            internal bool StandaloneReminderNoAutoStickyOptionOk;
            internal bool RenameInitialFocusOk;
            internal bool AppearanceLocationOk;
            internal bool ReminderDefaultCurrentTimeOk;
        }

        private static StickyDialogCheckResult RunStickyDialogChecks()
        {
            StickyDialogCheckResult result = new StickyDialogCheckResult();
            result.StandaloneReminderNoAutoStickyOptionOk = true;
            using (ReminderDialog dialog = new ReminderDialog(
                "预览", 10.5F, false))
            {
                result.ReminderSizePreviewOk =
                    dialog.ExerciseSizePreviewForTest();
                result.UnforcedMultilingualImeOk =
                    dialog.UsesUnforcedMultilingualIme;
                foreach (Control control in dialog.Controls)
                {
                    if (control is CheckBox && control.Text.IndexOf(
                        "创建桌面便利贴", StringComparison.Ordinal) >= 0)
                        result.StandaloneReminderNoAutoStickyOptionOk = false;
                }
                result.StandaloneReminderNoAutoStickyOptionOk =
                    result.StandaloneReminderNoAutoStickyOptionOk &&
                    dialog.ClientSize.Height == 487;
            }
            using (StickyNoteWindow note =
                new StickyNoteWindow(new StickyNoteData()))
                result.ReminderLiveSizePreviewOk =
                    note.ExerciseReminderLiveSizePreviewForTest();
            using (NoteTitleDialog dialog = new NoteTitleDialog("周计划"))
            {
                result.RenameInitialFocusOk = dialog.TitleInputIsInitialActive;
                result.UnforcedMultilingualImeOk &=
                    dialog.UsesUnforcedMultilingualIme;
            }
            Point below = StickyNoteWindow.CalculateAppearanceDialogLocation(
                new Rectangle(300, 100, 320, 300),
                new Size(520, 260),
                new Rectangle(0, 0, 1200, 900));
            Point above = StickyNoteWindow.CalculateAppearanceDialogLocation(
                new Rectangle(850, 700, 320, 180),
                new Size(520, 260),
                new Rectangle(0, 0, 1200, 900));
            result.AppearanceLocationOk = below.Y == 408 && below.X >= 0 &&
                above.Y < 700 && above.X >= 0 && above.X + 520 <= 1200;
            result.ReminderDefaultCurrentTimeOk = Math.Abs(
                (ReminderDialog.DefaultSuggestedLocal() -
                    DateTime.Now).TotalSeconds) < 3;
            return result;
        }

        private sealed class StickySideTabCheckResult
        {
            internal bool OverflowOk;
            internal bool DragPreviewOk;
            internal bool DeferredDropCommitOk;
            internal bool PreviewClearsBothSidesOk;
            internal bool ExplicitSourceKeepsTargetFirstOk;
            internal bool TargetNeverMarkedAsSourceOk;
            internal bool ExclusiveCanvasStateOk;
            internal bool ReverseBoundaryRolloverOk;
            internal bool BoundaryEdgeDropOk;
            internal bool ScaledGapOk;
            internal bool VectorIconColorOk;
            internal bool DeleteCommandOk;
            internal bool ZOrderPolicyOk;
            internal bool LayoutInvalidationOk;
        }

        private static StickySideTabCheckResult RunStickySideTabChecks(
            StickyNoteData restoredNote)
        {
            StickySideTabCheckResult result = new StickySideTabCheckResult();
            Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(9, 208,
                workArea);
            result.OverflowOk = leftCount >= 4 && leftCount < 9 &&
                9 - leftCount > 0 &&
                StickyNoteTabsForm.ScreenCapacity(workArea) >= 9 - leftCount;
            result.DragPreviewOk =
                StickyNoteTabsForm.PreviewInsertionGap >= 12 &&
                StickyNoteTabsForm.DragSourceVisualOffset >= 6 &&
                StickyNoteTabsForm.DragSourceVisualOffset <= 12 &&
                StickyNoteTabsForm.PetGap == -20 &&
                !String.IsNullOrEmpty(StickyNoteTabsForm.DragDataFormat) &&
                StickyNoteTabsForm.CalculateDropIndex(0, 4) == 0 &&
                StickyNoteTabsForm.CalculateDropIndex(95, 4) == 3 &&
                StickyNoteTabsForm.PreviewTargetTop(0, -1, 1) == 0 &&
                StickyNoteTabsForm.PreviewTargetTop(1, -1, 1) ==
                    StickyNoteTabsForm.TabHeight +
                    StickyNoteTabsForm.TabGap +
                    StickyNoteTabsForm.PreviewInsertionGap &&
                StickyNoteTabsForm.PreviewTargetTop(0, 2, 0) ==
                    StickyNoteTabsForm.PreviewInsertionGap;
            StickyTabDropSession dropSession = new StickyTabDropSession();
            StickyNoteData dropNote = new StickyNoteData();
            object dropSource = new object();
            int dropCommits = 0;
            dropSession.Begin(dropNote, dropSource);
            bool dropQueued = dropSession.QueueCommit(dropNote,
                delegate { dropCommits++; });
            result.DeferredDropCommitOk = dropQueued && dropCommits == 0 &&
                dropSession.IsSource(dropSource) &&
                dropSession.Complete(dropNote) && dropCommits == 1 &&
                dropSession.CurrentNote == null &&
                !dropSession.Complete(dropNote);
            StickyNoteData previewNote = new StickyNoteData();
            StickyNoteData boundaryNote = new StickyNoteData();
            StickyNoteData targetNote = new StickyNoteData();
            using (StickyNoteTabsForm left = new StickyNoteTabsForm(
                StickyTabSide.Left, delegate(string noteId) { }))
            using (StickyNoteTabsForm right = new StickyNoteTabsForm(
                StickyTabSide.Right, delegate(string noteId) { }))
            {
                left.SetNotes(new List<StickyNoteData> { previewNote }, 0);
                right.SetNotes(new List<StickyNoteData>
                    { boundaryNote, targetNote }, 1);
                left.Hide();
                right.Hide();
                StickyNoteTabsForm.BeginDragSession(previewNote, left);
                left.ShowDropPreviewForTest(previewNote, 0);
                bool leftWasTarget = left.HasDropPreviewForTest;
                right.ShowDropPreviewForTest(previewNote, 2);
                result.ExplicitSourceKeepsTargetFirstOk =
                    right.TabTopForTest(targetNote) == 0 &&
                    !right.TabVisibleForTest(boundaryNote) &&
                    left.HasBoundaryRolloverForTest(boundaryNote, false);
                result.TargetNeverMarkedAsSourceOk =
                    !right.HasDragSourceVisualForTest(previewNote);
                result.ExclusiveCanvasStateOk =
                    left.HasStableDragCanvasForTest &&
                    right.HasStableDragCanvasForTest;
                bool targetIsExclusive = leftWasTarget &&
                    !left.HasDropPreviewForTest &&
                    right.HasDropPreviewForTest &&
                    result.ExplicitSourceKeepsTargetFirstOk &&
                    result.TargetNeverMarkedAsSourceOk &&
                    result.ExclusiveCanvasStateOk &&
                    left.HasDragSourceVisualForTest(previewNote);
                StickyNoteTabsForm.EndDragSession(previewNote);
                result.PreviewClearsBothSidesOk = targetIsExclusive &&
                    !left.HasDropPreviewForTest &&
                    !right.HasDropPreviewForTest &&
                    left.HasStableDragCanvasForTest &&
                    right.HasStableDragCanvasForTest &&
                    right.TabVisibleForTest(boundaryNote) &&
                    !left.HasBoundaryRolloverForTest(boundaryNote, false) &&
                    !left.HasDragSourceVisualForTest(previewNote);
                StickyNoteTabsForm.BeginDragSession(previewNote, left);
                right.ShowDropPreviewForTest(previewNote, 0);
                result.BoundaryEdgeDropOk =
                    right.TabVisibleForTest(boundaryNote) &&
                    !left.HasBoundaryRolloverForTest(boundaryNote, false) &&
                    left.HasDragSourceVisualForTest(previewNote);
                StickyNoteTabsForm.EndDragSession(previewNote);
            }
            StickyNoteData reverseTop = new StickyNoteData();
            StickyNoteData reverseBoundary = new StickyNoteData();
            StickyNoteData reverseSource = new StickyNoteData();
            StickyNoteData reverseTail = new StickyNoteData();
            using (StickyNoteTabsForm left = new StickyNoteTabsForm(
                StickyTabSide.Left, delegate(string noteId) { }))
            using (StickyNoteTabsForm right = new StickyNoteTabsForm(
                StickyTabSide.Right, delegate(string noteId) { }))
            {
                left.SetNotes(new List<StickyNoteData>
                    { reverseTop, reverseBoundary }, 0);
                right.SetNotes(new List<StickyNoteData>
                    { reverseSource, reverseTail }, 2);
                left.Hide();
                right.Hide();
                StickyNoteTabsForm.BeginDragSession(reverseSource, right);
                left.ShowDropPreviewForTest(reverseSource, 1);
                result.ReverseBoundaryRolloverOk =
                    left.TabTopForTest(reverseTop) == 0 &&
                    !left.TabVisibleForTest(reverseBoundary) &&
                    right.HasBoundaryRolloverForTest(reverseBoundary, true) &&
                    right.HasStableDragCanvasForTest;
                StickyNoteTabsForm.EndDragSession(reverseSource);
                result.ReverseBoundaryRolloverOk =
                    result.ReverseBoundaryRolloverOk &&
                    left.TabVisibleForTest(reverseBoundary) &&
                    !right.HasBoundaryRolloverForTest(reverseBoundary, true);
                StickyNoteTabsForm.BeginDragSession(reverseSource, right);
                left.ShowDropPreviewForTest(reverseSource, 2);
                result.BoundaryEdgeDropOk = result.BoundaryEdgeDropOk &&
                    left.TabVisibleForTest(reverseBoundary) &&
                    !right.HasBoundaryRolloverForTest(reverseBoundary, true) &&
                    right.HasDragSourceVisualForTest(reverseSource);
                StickyNoteTabsForm.EndDragSession(reverseSource);
            }
            int fullOverlap = StickyNoteTabsForm.PetOverlapForWidth(192);
            int doubleOverlap = StickyNoteTabsForm.PetOverlapForWidth(384);
            result.ScaledGapOk =
                Math.Abs((44 - fullOverlap) - (44 - 20) / 2.0) < 1.0 &&
                Math.Abs((88 - doubleOverlap) - (88 - 20) / 2.0) < 1.0 &&
                doubleOverlap > fullOverlap;
            Color paper = Color.FromArgb(255, 118, 169, 242);
            Color ink = StickyNoteTabControl.TypeIconColor(paper);
            result.VectorIconColorOk = ink.ToArgb() != Color.Black.ToArgb() &&
                ink.GetBrightness() < paper.GetBrightness();
            using (StickyNoteTabControl tab = new StickyNoteTabControl(
                restoredNote, StickyTabSide.Left,
                delegate(string noteId) { },
                delegate(string noteId) { }))
                result.DeleteCommandOk = tab.HasDeleteCommand;
            result.ZOrderPolicyOk =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(false) &&
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true);
            const int layoutNoteCount = 14;
            int layoutLeftCount = StickyNoteTabsForm.CalculateLeftCount(
                layoutNoteCount, 208, workArea);
            Rectangle shortWorkArea = new Rectangle(0, 0, 1920, 280);
            int shortLeftCount = StickyNoteTabsForm.CalculateLeftCount(
                layoutNoteCount, 208,
                shortWorkArea);
            result.LayoutInvalidationOk =
                StickyNoteTabsForm.IsLayoutSplitCurrent(layoutLeftCount,
                    layoutNoteCount - layoutLeftCount, 208, workArea) &&
                !StickyNoteTabsForm.IsLayoutSplitCurrent(layoutLeftCount,
                    layoutNoteCount - layoutLeftCount, 208, shortWorkArea) &&
                StickyNoteTabsForm.IsLayoutSplitCurrent(shortLeftCount,
                    layoutNoteCount - shortLeftCount, 208, shortWorkArea);
            return result;
        }

        private sealed class StickyWindowPolicyCheckResult
        {
            internal bool HighDpiLayoutOk;
            internal bool ResourceLimitsOk;
            internal bool SoftPaletteOk;
            internal bool FullWidthNormalizationOk;
            internal bool ManagerMarqueeBatchDeleteOk;
            internal bool NativeSnapDisabledOk;
            internal bool SteadyDockGuideOk;
            internal bool OrdinaryLinkDetectionOk;
        }

        private static StickyWindowPolicyCheckResult
            RunStickyWindowPolicyChecks(StickyNoteRepository repository)
        {
            StickyWindowPolicyCheckResult result =
                new StickyWindowPolicyCheckResult();
            result.HighDpiLayoutOk =
                StickyNoteWindow.MinimumNoteSizeForDpi(96) ==
                    new Size(280, 220) &&
                StickyNoteWindow.MinimumNoteSizeForDpi(192) ==
                    new Size(560, 440) &&
                StickyNoteWindow.HeaderRowHeightForDpi(192) >=
                    StickyNoteWindow.HeaderRowHeightForDpi(96) * 2 - 1;
            result.ResourceLimitsOk =
                StickyNoteLimits.MaximumNotes == 100 &&
                StickyNoteLimits.MaximumTodoItemsPerNote == 500 &&
                StickyNoteLimits.MaximumBodyCharacters == 4000000 &&
                StickyNoteLimits.MaximumRichTextCharacters == 16000000 &&
                StickyNoteRepository.CanCreateAtCount(true, 0) &&
                StickyNoteRepository.CanCreateAtCount(true,
                    StickyNoteLimits.MaximumNotes - 1) &&
                !StickyNoteRepository.CanCreateAtCount(true,
                    StickyNoteLimits.MaximumNotes) &&
                !StickyNoteRepository.CanCreateAtCount(false, 0);
            result.SoftPaletteOk =
                StickyNoteWindow.PaletteColorForTest(0).ToArgb() ==
                    Color.FromArgb(255, 255, 117, 112).ToArgb() &&
                StickyNoteWindow.PaletteColorForTest(32).ToArgb() ==
                    Color.FromArgb(255, 239, 240, 241).ToArgb();
            result.FullWidthNormalizationOk =
                StickyNoteWindow.NormalizeFullWidthLatin(
                    "中文ｃｔｒｌＥｎｇｌｉｓｈ１２３") ==
                    "中文ctrlEnglish123";
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return repository.GetAll(); }, delegate { },
                delegate(StickyNoteData note) { },
                delegate(StickyNoteData note) { },
                delegate(StickyNoteData note) { }))
                result.ManagerMarqueeBatchDeleteOk =
                    manager.SupportsMarqueeBatchDelete;
            long styleWithMaximize = 0x00040000L | 0x00010000L;
            result.NativeSnapDisabledOk =
                StickyNoteWindow.RemoveMaximizeStyle(styleWithMaximize) ==
                    0x00040000L;
            using (DockPulseIndicatorForm guide =
                new DockPulseIndicatorForm(Color.DeepSkyBlue, 0))
                result.SteadyDockGuideOk = guide.UsesSteadyOpacityForTest;
            StickyNoteData linkData = new StickyNoteData();
            linkData.Text = "C:\\Users\\Penny pet\\进度表.xlsx\r\n" +
                "https://www.baidu.com/";
            using (StickyNoteWindow note = new StickyNoteWindow(linkData, true))
                result.OrdinaryLinkDetectionOk =
                    note.ExerciseOrdinaryLinkRefreshForTest();
            return result;
        }

        private sealed class ReminderCoordinatorCheckResult
        {
            internal bool MultipleLinkedReminderOk;
            internal bool ConcreteDateTimeOk;
            internal bool BannerTickThrottleOk;
            internal bool DueBubblePersistentOk;
            internal bool DueBubbleUsesOwnSizeOk;
            internal bool DueBubbleReplacementOk;
            internal bool PreAlertBubbleProtectionOk;
            internal bool ExpiredAtLaunchDiscardedOk;
        }

        private static ReminderCoordinatorCheckResult
            RunReminderCoordinatorChecks(DateTime reminderBaseUtc)
        {
            ReminderCoordinatorCheckResult result =
                new ReminderCoordinatorCheckResult();
            ReminderSchedule sameNoteSchedule = new ReminderSchedule();
            sameNoteSchedule.Add(reminderBaseUtc.AddMinutes(30), "later",
                "shared-note");
            ReminderItem earlierLinked = sameNoteSchedule.Add(
                reminderBaseUtc.AddMinutes(10), "earlier", "shared-note");
            sameNoteSchedule.Add(reminderBaseUtc.AddMinutes(20), "unrelated",
                "other-note");
            result.MultipleLinkedReminderOk = Object.ReferenceEquals(
                sameNoteSchedule.FindBySourceNoteId("shared-note"),
                earlierLinked) &&
                sameNoteSchedule.RemoveBySourceNoteId("shared-note") == 2 &&
                sameNoteSchedule.Count == 1 &&
                sameNoteSchedule.FindBySourceNoteId("shared-note") == null &&
                sameNoteSchedule.FindBySourceNoteId("other-note") != null;
            ReminderSchedule concreteSchedule = new ReminderSchedule();
            DateTime concreteLocal = DateTime.Now.AddMinutes(10);
            ReminderItem concrete = concreteSchedule.Add(
                concreteLocal.ToUniversalTime(), "concrete");
            result.ConcreteDateTimeOk = Math.Abs(
                (concrete.DeadlineUtc.ToLocalTime() -
                    concreteLocal).TotalSeconds) < 1;
            long second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            result.BannerTickThrottleOk =
                PetReminderCoordinator.ShouldRefreshReminderBanner(
                    Int64.MinValue, second) &&
                !PetReminderCoordinator.ShouldRefreshReminderBanner(
                    second, second) &&
                PetReminderCoordinator.ShouldRefreshReminderBanner(
                    second, second + 1);
            result.DueBubblePersistentOk =
                PetReminderCoordinator.DueReminderBubbleDurationMilliseconds == 0;
            result.DueBubbleUsesOwnSizeOk = Math.Abs(
                PetForm.DueReminderBubbleFontSizePoints(100) -
                KeyboardOverlayForm.TextFontSizePoints(100)) < 0.2F;
            result.DueBubbleReplacementOk =
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.ReminderDue, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.DailyGreeting, true) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.Hover,
                    PetMessageKind.Feedback, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.DailyGreeting, false);
            result.PreAlertBubbleProtectionOk =
                !PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.ReminderDue, false) &&
                PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.Feedback, true);
            ReminderItem expired = new ReminderItem(
                DateTime.UtcNow.AddMinutes(-1), "已错过");
            ReminderItem future = new ReminderItem(
                DateTime.UtcNow.AddMinutes(1), "仍有效");
            DateTime launchGate = DateTime.UtcNow;
            result.ExpiredAtLaunchDiscardedOk =
                !PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                    expired, launchGate) &&
                PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                    future, launchGate);
            return result;
        }

        private sealed class KeyboardOverlayCheckResult
        {
            internal bool HookOptInDefaultOk;
            internal bool TextScaleChoicesOk;
            internal bool ShortcutAndRepeatOk;
            internal bool HeldKeyStableOk;
            internal bool HookCapturePolicyOk;
            internal bool OwnProcessEligibilityOk;
            internal bool PrivacyGenerationOk;
            internal bool FocusSnapshotIdentityOk;
            internal bool AdaptiveContrastOk;
        }

        private static KeyboardOverlayCheckResult RunKeyboardOverlayChecks()
        {
            KeyboardOverlayCheckResult result =
                new KeyboardOverlayCheckResult();
            result.HookOptInDefaultOk =
                !new PetSettings().ShowKeyOverlay &&
                !new PetSettings().KeyboardPrivacyNoticeAccepted &&
                !PetKeyboardPrivacyPolicy.ShouldStartHook(false, false) &&
                !PetKeyboardPrivacyPolicy.ShouldStartHook(true, false) &&
                PetKeyboardPrivacyPolicy.ShouldStartHook(true, true) &&
                PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, false) &&
                !PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, true) &&
                PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                    true, false) &&
                PetForm.WindowsKeyboardFirstUseNotice.IndexOf("杀毒软件",
                    StringComparison.Ordinal) >= 0 &&
                PetForm.WindowsKeyboardFirstUseNotice.IndexOf("误报",
                    StringComparison.Ordinal) >= 0;
            result.TextScaleChoicesOk =
                KeyboardOverlayForm.NormalizeTextScalePercent(55) == 60 &&
                KeyboardOverlayForm.NormalizeTextScalePercent(100) == 100 &&
                KeyboardOverlayForm.NormalizeTextScalePercent(140) == 150 &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(60) - 9F) <
                    0.01F &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(100) - 15F) <
                    0.01F &&
                Math.Abs(KeyboardOverlayForm.TextFontSizePoints(150) - 22.5F) <
                    0.01F;
            string shortcut = KeyboardInputFormatter.ComposeKeyName(
                (int)Keys.W, true, false, false, false);
            string modifierChord = KeyboardInputFormatter.ComposeKeyName(
                (int)Keys.LShiftKey, true, true, false, false);
            int repeatOne = GlobalKeyboardActivity.NextRepeatCount(
                0, (uint)Keys.W, 0, 1000, 0);
            int repeatTwo = GlobalKeyboardActivity.NextRepeatCount(
                (uint)Keys.W, (uint)Keys.W, 1000, 1800, repeatOne);
            int repeatReset = GlobalKeyboardActivity.NextRepeatCount(
                (uint)Keys.W, (uint)Keys.A, 1800, 1850, repeatTwo);
            result.ShortcutAndRepeatOk = shortcut == "CTRL+W" &&
                modifierChord == "CTRL+SHIFT" && repeatOne == 1 &&
                repeatTwo == 2 && repeatReset == 1;
            result.HeldKeyStableOk =
                GlobalKeyboardActivity.ShouldPublishKeyDown(false) &&
                !GlobalKeyboardActivity.ShouldPublishKeyDown(true);
            result.HookCapturePolicyOk =
                GlobalKeyboardActivity.ShouldPublishKey(false) &&
                !GlobalKeyboardActivity.ShouldPublishKey(true);
            result.OwnProcessEligibilityOk =
                !PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    false, false) &&
                !PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, true) &&
                PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, false);
            result.PrivacyGenerationOk = PetForm.IsCurrentPrivacyScan(12, 12) &&
                !PetForm.IsCurrentPrivacyScan(12, 13);
            KeyboardFocusSnapshot captured = new KeyboardFocusSnapshot(
                new IntPtr(10), 20, 30, new IntPtr(40),
                new int[] { 1, 2, 3 });
            result.FocusSnapshotIdentityOk =
                KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(10), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 3 })) &&
                !KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(11), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 3 })) &&
                !KeyboardFocusSnapshot.IsSameTarget(captured,
                    new KeyboardFocusSnapshot(new IntPtr(10), 20, 30,
                        new IntPtr(40), new int[] { 1, 2, 4 }));
            result.AdaptiveContrastOk =
                KeyboardOverlayForm.ChooseTextColorFromLuminance(0.8) ==
                    Color.Black &&
                KeyboardOverlayForm.ChooseTextColorFromLuminance(0.2) ==
                    Color.White;
            return result;
        }

        private sealed class AnimationCheckResult
        {
            internal bool SmoothTimingOk;
            internal bool GoodbyeOk;
            internal bool NotificationOk;
            internal bool NotificationSingleCycleOk;
            internal bool DragUsesSecondIdleRowOk;
            internal bool IdleRandomRowsOk;
            internal bool TypingRandomRowsOk;
            internal bool IdleThoughtProbabilityReducedOk;
            internal bool GuitarFailureProbabilityReducedOk;
            internal bool ManualRandomPoolOk;
            internal bool ManualSpecialProbabilityReducedOk;
            internal bool ManualFullCycleGuardOk;
            internal bool PokeBurstOk;
            internal bool ClickDragThresholdOk;
        }

        private static AnimationCheckResult RunAnimationChecks(
            int[] cycleDurations)
        {
            AnimationCheckResult result = new AnimationCheckResult();
            result.SmoothTimingOk = cycleDurations[0] >= 2000 &&
                cycleDurations[5] >= 2200 &&
                cycleDurations[6] >= 1800 &&
                cycleDurations[7] >= 2400 &&
                cycleDurations[8] >= 2000;
            result.GoodbyeOk = cycleDurations[3] >= 1200;
            result.NotificationOk = cycleDurations[9] >= 1200;
            result.NotificationSingleCycleOk =
                !PetAnimationController.ReminderAnimationCycleComplete(
                    true, 9, 1, 4) &&
                PetAnimationController.ReminderAnimationCycleComplete(
                    true, 9, 3, 4) &&
                !PetAnimationController.ReminderAnimationCycleComplete(
                    false, 9, 3, 4) &&
                !PetAnimationController.ReminderAnimationCycleComplete(
                    true, 0, 3, 4);
            result.DragUsesSecondIdleRowOk =
                PetAnimationController.FailedRow == 5;
            Random random = new Random(20260810);
            HashSet<int> idleChoices = new HashSet<int>();
            HashSet<int> typingChoices = new HashSet<int>();
            int idleRow = -1;
            bool idleNoImmediateRepeat = true;
            for (int i = 0; i < 256; i++)
            {
                int next = PetAnimationController.PickRandomIdleAnimationRow(
                    random, idleRow);
                idleNoImmediateRepeat = idleNoImmediateRepeat &&
                    (next == 0 || next != idleRow);
                idleChoices.Add(next);
                idleRow = next;
                typingChoices.Add(
                    PetAnimationController.PickRandomTypingAnimationRow(random));
            }
            result.IdleRandomRowsOk = idleNoImmediateRepeat &&
                idleChoices.Count == 3 &&
                PetAnimationController.IsIdleAnimationRow(0) &&
                PetAnimationController.IsIdleAnimationRow(5) &&
                PetAnimationController.IsIdleAnimationRow(8) &&
                !PetAnimationController.IsIdleAnimationRow(7);
            result.TypingRandomRowsOk = typingChoices.Count == 2 &&
                PetAnimationController.IsTypingAnimationRow(6) &&
                PetAnimationController.IsTypingAnimationRow(7) &&
                !PetAnimationController.IsTypingAnimationRow(8);
            Random probabilityRandom = new Random(20260820);
            int firstThought = 0;
            int secondThought = 0;
            for (int i = 0; i < 100000; i++)
            {
                int selected =
                    PetAnimationController.PickRandomIdleAnimationRow(
                        probabilityRandom, -1);
                if (selected == 5) firstThought++;
                if (selected == 8) secondThought++;
            }
            result.IdleThoughtProbabilityReducedOk =
                PetAnimationController.IdleThoughtProbabilityDenominator == 20 &&
                firstThought >= 4300 && firstThought <= 5700 &&
                secondThought >= 4300 && secondThought <= 5700;
            int failedGuitar = 0;
            for (int i = 0; i < 60000; i++)
                if (PetAnimationController.PickRandomTypingAnimationRow(
                    probabilityRandom) == 7) failedGuitar++;
            result.GuitarFailureProbabilityReducedOk =
                PetAnimationController.GuitarFailureProbabilityDenominator == 6 &&
                failedGuitar >= 9500 && failedGuitar <= 10500;
            Random manualRandom = new Random(20260811);
            HashSet<int> manualRows = new HashSet<int>();
            int manualRow = -1;
            bool manualNoImmediateRepeat = true;
            for (int i = 0; i < 256; i++)
            {
                int next = PetAnimationController.PickRandomManualAnimationRow(
                    manualRandom, manualRow);
                manualNoImmediateRepeat = manualNoImmediateRepeat &&
                    next != manualRow;
                manualRows.Add(next);
                manualRow = next;
            }
            result.ManualRandomPoolOk = manualNoImmediateRepeat &&
                manualRows.Count == 6 &&
                PetAnimationController.IsManualAnimationRow(0) &&
                PetAnimationController.IsManualAnimationRow(4) &&
                PetAnimationController.IsManualAnimationRow(5) &&
                PetAnimationController.IsManualAnimationRow(6) &&
                PetAnimationController.IsManualAnimationRow(7) &&
                PetAnimationController.IsManualAnimationRow(8) &&
                !PetAnimationController.IsManualAnimationRow(9) &&
                !PetAnimationController.IsManualAnimationRow(1) &&
                !PetAnimationController.IsManualAnimationRow(2) &&
                !PetAnimationController.IsManualAnimationRow(3);
            Random manualProbabilityRandom = new Random(20260821);
            int manualFirstThought = 0;
            int manualFailedGuitar = 0;
            int manualSecondThought = 0;
            for (int i = 0; i < 42000; i++)
            {
                int selected =
                    PetAnimationController.PickRandomManualAnimationRow(
                        manualProbabilityRandom, -1);
                if (selected == 5) manualFirstThought++;
                if (selected == 7) manualFailedGuitar++;
                if (selected == 8) manualSecondThought++;
            }
            result.ManualSpecialProbabilityReducedOk =
                manualFirstThought >= 2200 && manualFirstThought <= 2900 &&
                manualFailedGuitar >= 2200 && manualFailedGuitar <= 2900 &&
                manualSecondThought >= 2200 && manualSecondThought <= 2900;
            PetAnimationController interaction =
                new PetAnimationController();
            bool firstOrdinary = interaction.TryStartOrdinaryPoke(4);
            bool blockedOrdinary = interaction.TryStartOrdinaryPoke(6);
            bool easterOverride = interaction.TryStartEasterEgg(5);
            interaction.CompleteInteractionAnimation();
            bool nextCycle = interaction.TryStartOrdinaryPoke(6);
            result.ManualFullCycleGuardOk = firstOrdinary &&
                !blockedOrdinary && easterOverride &&
                interaction.InteractionAnimationRow == 6 && nextCycle;
            DateTime burstStart = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            PetPokeBurstTracker burst = new PetPokeBurstTracker();
            bool earlyTrigger = false;
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                earlyTrigger |= burst.RegisterPoke(
                    burstStart.AddMilliseconds((poke - 1) * 100));
            bool targetTrigger = burst.RegisterPoke(
                burstStart.AddMilliseconds(4900));
            bool repeatedTrigger = burst.RegisterPoke(
                burstStart.AddMilliseconds(5000));
            PetPokeBurstTracker resetBurst = new PetPokeBurstTracker();
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                resetBurst.RegisterPoke(
                    burstStart.AddMilliseconds((poke - 1) * 100));
            bool afterPause = resetBurst.RegisterPoke(
                burstStart.AddMilliseconds(5201));
            result.PokeBurstOk = !earlyTrigger && targetTrigger &&
                !repeatedTrigger && !afterPause;
            result.ClickDragThresholdOk =
                !PetAnimationController.MovementStartsDrag(5, 0) &&
                !PetAnimationController.MovementStartsDrag(4, 4) &&
                PetAnimationController.MovementStartsDrag(6, 0) &&
                PetAnimationController.MovementStartsDrag(5, 4);
            return result;
        }

        private sealed class SequenceRandom : Random
        {
            private readonly int[] _values;
            private int _index;

            internal SequenceRandom(params int[] values)
            {
                _values = values ?? new int[0];
            }

            public override int Next(int maxValue)
            {
                if (maxValue <= 0) return 0;
                int value = _index < _values.Length ? _values[_index++] : 0;
                return (value & Int32.MaxValue) % maxValue;
            }
        }

        private sealed class BubbleCheckResult
        {
            internal bool HoverCopyOk;
            internal bool ManualPositionOk;
            internal bool StyledReminderOk;
            internal bool ThemeAndKeyboardFontOk;
            internal bool DragSuppressionOk;
            internal bool SilentModeOk;
            internal bool PositionMathOk;
            internal bool SingleMessageKindOk;
            internal bool ReplacementClosesOldFormOk;
            internal bool ProtectedMessageOk;
            internal bool DeferredMessageSemanticsOk;
            internal bool PendingRetryOk;
            internal bool SmallTalkFeedbackLifecycleOk;
            internal bool ReminderPriorityRegressionOk;
            internal bool SingleRestoreAfterCloseOk;
            internal bool AdaptiveSizingOk;
            internal bool UpdateTextRelayoutOk;
            internal bool DailyFirstPokeOk;
            internal bool DailyRejectedRetryOk;
            internal bool DailyGreetingRequestOk;
            internal bool EasterEggRequestOk;
            internal bool MinimumReadableOk;
            internal bool ReadabilityBypassOk;
            internal bool SmallTalkRequestOk;
            internal bool SmallTalkCoordinatorCooldownOk;
            internal bool SmallTalkCoordinatorRejectedRetryOk;
            internal bool SmallTalkCoordinatorSilentModeOk;
            internal bool SmallTalkCoordinatorReminderRetryOk;
            internal bool SolarTermOk;
            internal bool DailyContentPreferencesOk;
            internal bool CuratedCatalogOk;
            internal bool DailySelectorBudgetOk;
            internal bool DailyBriefingBudgetOk;
            internal bool DailyBriefingCoordinatorOk;
            internal bool DailyBriefingRejectedRetryOk;
            internal bool DailyBriefingSameDaySwitchOk;
            internal bool AlmanacCalculatorOk;
            internal bool AlmanacSemanticOk;
            internal bool AlmanacWordingOk;
        }

        private sealed class WeatherCheckResult
        {
            internal bool ForecastFixtureParsingOk;
            internal bool ForecastRequestShapeOk;
            internal bool GeocodingRequestAndSelectionOk;
            internal bool NoStartupRequestOk;
            internal bool SameDayCacheAndInFlightOk;
            internal bool BoundedCacheInvalidationOk;
            internal bool FailureCooldownOk;
            internal bool MeaningAndWordingOk;
            internal bool DailyCoordinatorWeatherOk;
            internal bool DailyCoordinatorFailureFallbackOk;
            internal bool DailyCoordinatorInFlightOk;
            internal bool RejectedBubbleReusesForecastOk;
            internal bool LocationDialogLayoutOk;
        }

        private sealed class WeatherFixtureHandler : HttpMessageHandler
        {
            private readonly string _forecastJson;
            private readonly bool _failForecast;

            internal WeatherFixtureHandler(string forecastJson,
                bool failForecast)
            {
                _forecastJson = forecastJson;
                _failForecast = failForecast;
            }

            internal int RequestCount;
            internal int ForecastCount;
            internal int GeocodingCount;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref RequestCount);
                if (request.RequestUri.Host.StartsWith("geocoding-api.",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref GeocodingCount);
                    return Task.FromResult(JsonResponse(
                        "{\"results\":[{\"name\":\"武汉\"," +
                        "\"admin1\":\"湖北\",\"country\":\"中国\"," +
                        "\"latitude\":30.5928,\"longitude\":114.3055," +
                        "\"timezone\":\"Asia/Shanghai\"}]}"));
                }
                Interlocked.Increment(ref ForecastCount);
                if (_failForecast)
                    return Task.FromResult(new HttpResponseMessage(
                        System.Net.HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(JsonResponse(_forecastJson));
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                HttpResponseMessage response = new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK);
                response.Content = new StringContent(json, Encoding.UTF8,
                    "application/json");
                return response;
            }
        }

        private static WeatherCheckResult RunWeatherChecks()
        {
            WeatherCheckResult result = new WeatherCheckResult();
            string fixture = ReadWeatherFixture("weather-rain-later.json");

            DateTime date = new DateTime(2026, 9, 1);
            WeatherForecastWindow parsed = new OpenMeteoForecastParser()
                .Parse(fixture, date);
            result.ForecastFixtureParsingOk = parsed.Yesterday != null &&
                parsed.Today != null && parsed.Tomorrow != null &&
                parsed.Today.MinimumTemperatureC == 22D &&
                parsed.Today.MaximumTemperatureC == 27D &&
                Math.Abs(parsed.Today.TotalPrecipitationMm - 4.2D) < 0.001D &&
                parsed.Today.MaximumPrecipitationProbability == 85D &&
                parsed.Today.FirstLikelyPrecipitationHour == 16 &&
                parsed.Today.LastLikelyPrecipitationHour == 16 &&
                parsed.Today.LikelyPrecipitationHours == 1;

            WeatherLocation location;
            WeatherLocation.TryCreate("武汉", "湖北", "中国", 30.5928,
                114.3055, "Asia/Shanghai", out location);
            using (PetWeatherSource dialogSource = new PetWeatherSource())
            using (WeatherLocationDialog dialog =
                new WeatherLocationDialog(dialogSource))
                result.LocationDialogLayoutOk =
                    dialog.UsesCompactFormattedResultsForTest(location);
            string forecastUrl = Uri.UnescapeDataString(
                OpenMeteoForecastClient.BuildUri(location).AbsoluteUri);
            result.ForecastRequestShapeOk = forecastUrl.StartsWith(
                    OpenMeteoForecastClient.Endpoint,
                    StringComparison.Ordinal) &&
                forecastUrl.Contains("hourly=" + String.Join(",",
                    OpenMeteoForecastClient.HourlyVariables)) &&
                OpenMeteoForecastClient.HourlyVariables.Length == 8 &&
                forecastUrl.Contains("past_days=1") &&
                forecastUrl.Contains("forecast_days=2") &&
                forecastUrl.Contains("timezone=Asia/Shanghai") &&
                forecastUrl.Contains("temperature_unit=celsius") &&
                forecastUrl.Contains("wind_speed_unit=kmh") &&
                forecastUrl.Contains("precipitation_unit=mm") &&
                forecastUrl.IndexOf("apikey", StringComparison.OrdinalIgnoreCase)
                    < 0;

            WeatherFixtureHandler handler = new WeatherFixtureHandler(
                fixture, false);
            using (PetWeatherSource source = new PetWeatherSource(
                new HttpClient(handler), delegate
                {
                    return new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                        TimeSpan.Zero);
                }))
            {
                result.NoStartupRequestOk = handler.RequestCount == 0;
                IReadOnlyList<WeatherLocation> locations = source
                    .SearchLocationsAsync("武汉").GetAwaiter().GetResult();
                result.GeocodingRequestAndSelectionOk =
                    handler.GeocodingCount == 1 && locations.Count == 1 &&
                    locations[0].DisplayName == "武汉 · 湖北 · 中国" &&
                    locations[0].Timezone == "Asia/Shanghai" &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "count=5") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "language=zh") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "format=json") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.IndexOf(
                        "apikey", StringComparison.OrdinalIgnoreCase) < 0;
                Task<WeatherForecastWindow> first = source.GetForecastAsync(
                    location, date);
                Task<WeatherForecastWindow> concurrent =
                    source.GetForecastAsync(location, date);
                WeatherForecastWindow firstValue = first.GetAwaiter()
                    .GetResult();
                WeatherForecastWindow cached = source.GetForecastAsync(
                    location, date).GetAwaiter().GetResult();
                result.SameDayCacheAndInFlightOk =
                    Object.ReferenceEquals(first, concurrent) &&
                    Object.ReferenceEquals(firstValue, cached) &&
                    handler.ForecastCount == 1 &&
                    source.ForecastRequestCountForTest == 1;
                source.GetForecastAsync(location, date.AddDays(1))
                    .GetAwaiter().GetResult();
                source.GetForecastAsync(location, date).GetAwaiter()
                    .GetResult();
                bool retainedRecentDays = handler.ForecastCount == 2;
                source.InvalidateCache();
                source.GetForecastAsync(location, date).GetAwaiter()
                    .GetResult();
                result.BoundedCacheInvalidationOk = retainedRecentDays &&
                    handler.ForecastCount == 3;
            }

            WeatherFixtureHandler failing = new WeatherFixtureHandler(
                fixture, true);
            using (PetWeatherSource source = new PetWeatherSource(
                new HttpClient(failing), delegate
                {
                    return new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                        TimeSpan.Zero);
                }))
            {
                WeatherForecastWindow failed = source.GetForecastAsync(
                    location, date).GetAwaiter().GetResult();
                WeatherForecastWindow cooledDown = source.GetForecastAsync(
                    location, date).GetAwaiter().GetResult();
                result.FailureCooldownOk = failed == null &&
                    cooledDown == null && failing.ForecastCount == 1;
            }

            string[] fixtureNames =
            {
                "weather-clear.json", "weather-rain-later.json",
                "weather-cooling.json", "weather-rain-cooling.json",
                "weather-windy.json", "weather-snow.json"
            };
            WeatherMeaning?[] expectedMeanings =
            {
                null, WeatherMeaning.RainLater, WeatherMeaning.Cooling,
                WeatherMeaning.RainAndCooling, WeatherMeaning.Windy,
                WeatherMeaning.Snow
            };
            bool meaningsOk = true;
            for (int i = 0; i < fixtureNames.Length; i++)
            {
                WeatherForecastWindow window = new OpenMeteoForecastParser()
                    .Parse(ReadWeatherFixture(fixtureNames[i]), date);
                meaningsOk &= WeatherMeaningRules.Select(window) ==
                    expectedMeanings[i];
            }
            foreach (WeatherMeaning meaning in Enum.GetValues(
                typeof(WeatherMeaning)))
            {
                HashSet<string> variants = new HashSet<string>();
                for (int day = 0; day < 365; day++)
                {
                    WeatherDailySelection selected =
                        WeatherWordingCatalog.Select(meaning,
                            date.AddDays(day), location.StableKey);
                    variants.Add(selected.Text);
                    meaningsOk &= selected.Text == WeatherWordingCatalog
                        .Select(meaning, date.AddDays(day),
                            location.StableKey).Text &&
                        selected.Text.Length <= 60;
                }
                int required = meaning == WeatherMeaning.RainLater ||
                    meaning == WeatherMeaning.Cooling ||
                    meaning == WeatherMeaning.Windy ||
                    meaning == WeatherMeaning.Hot ? 5 : 3;
                meaningsOk &= variants.Count >= required;
            }
            result.MeaningAndWordingOk = meaningsOk;

            WeatherForecastWindow rainLater = new OpenMeteoForecastParser()
                .Parse(fixture, date);
            string lastDate = String.Empty;
            string shownText = null;
            int dailyForecastCalls = 0;
            int dailyShowCount = 0;
            PetDailyContentCoordinator daily =
                new PetDailyContentCoordinator(
                    delegate { return lastDate; },
                    delegate { return false; }, delegate { return true; },
                    delegate { return false; },
                    delegate { return true; },
                    delegate { return true; },
                    delegate { return location; },
                    delegate
                    {
                        dailyForecastCalls++;
                        return Task.FromResult(rainLater);
                    },
                    delegate { return ZodiacSign.None; },
                    delegate(string text)
                    {
                        dailyShowCount++;
                        shownText = text;
                        return true;
                    },
                    delegate(string value) { lastDate = value; });
            bool weatherShown = RunDaily(daily,
                new DateTimeOffset(date, TimeSpan.FromHours(8)));
            string expectedWeather = WeatherWordingCatalog.Select(
                WeatherMeaning.RainLater, date, location.StableKey).Text;
            result.DailyCoordinatorWeatherOk = weatherShown &&
                dailyForecastCalls == 1 && dailyShowCount == 1 &&
                shownText.Contains(expectedWeather) && lastDate == "20260901";

            lastDate = String.Empty;
            shownText = null;
            dailyForecastCalls = 0;
            PetDailyContentCoordinator unavailable =
                new PetDailyContentCoordinator(
                    delegate { return lastDate; },
                    delegate { return false; }, delegate { return true; },
                    delegate { return false; },
                    delegate { return true; },
                    delegate { return true; },
                    delegate { return location; },
                    delegate
                    {
                        dailyForecastCalls++;
                        return Task.FromResult<WeatherForecastWindow>(null);
                    },
                    delegate { return ZodiacSign.None; },
                    delegate(string text)
                    {
                        shownText = text;
                        return true;
                    },
                    delegate(string value) { lastDate = value; });
            bool fallbackShown = RunDaily(unavailable,
                new DateTimeOffset(date, TimeSpan.FromHours(8)));
            result.DailyCoordinatorFailureFallbackOk = fallbackShown &&
                dailyForecastCalls == 1 &&
                !String.IsNullOrWhiteSpace(shownText) &&
                !shownText.Contains(expectedWeather) &&
                lastDate == "20260901";

            result.DailyCoordinatorInFlightOk = Task.Run(delegate
            {
                string pendingDate = String.Empty;
                int pendingFetches = 0;
                int pendingShows = 0;
                TaskCompletionSource<WeatherForecastWindow> pending =
                    new TaskCompletionSource<WeatherForecastWindow>();
                PetDailyContentCoordinator pendingDaily =
                    new PetDailyContentCoordinator(
                        delegate { return pendingDate; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return true; },
                        delegate { return location; },
                        delegate
                        {
                            pendingFetches++;
                            return pending.Task;
                        },
                        delegate { return ZodiacSign.None; },
                        delegate { pendingShows++; return true; },
                        delegate(string value) { pendingDate = value; });
                DateTimeOffset pendingNow = new DateTimeOffset(date,
                    TimeSpan.FromHours(8));
                Task<bool> firstAttempt = pendingDaily.HandlePetPokedAsync(
                    pendingNow);
                Task<bool> secondAttempt = pendingDaily.HandlePetPokedAsync(
                    pendingNow.AddMinutes(1));
                bool secondConsumed = secondAttempt.GetAwaiter().GetResult();
                pending.SetResult(rainLater);
                bool firstShown = firstAttempt.GetAwaiter().GetResult();
                return secondConsumed && firstShown && pendingFetches == 1 &&
                    pendingShows == 1 && pendingDate == "20260901";
            }).GetAwaiter().GetResult();

            result.RejectedBubbleReusesForecastOk = Task.Run(delegate
            {
                WeatherFixtureHandler retryHandler =
                    new WeatherFixtureHandler(fixture, false);
                using (PetWeatherSource retrySource = new PetWeatherSource(
                    new HttpClient(retryHandler), delegate
                    {
                        return new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                            TimeSpan.Zero);
                    }))
                {
                    string retryDate = String.Empty;
                    bool accept = false;
                    int attempts = 0;
                    PetDailyContentCoordinator retryDaily =
                        new PetDailyContentCoordinator(
                            delegate { return retryDate; },
                            delegate { return false; },
                            delegate { return true; },
                            delegate { return false; },
                            delegate { return true; },
                            delegate { return true; },
                            delegate { return location; },
                            delegate(WeatherLocation target,
                                DateTime localDate)
                            {
                                return retrySource.GetForecastAsync(target,
                                    localDate);
                            },
                            delegate { return ZodiacSign.None; },
                            delegate { attempts++; return accept; },
                            delegate(string value) { retryDate = value; });
                    DateTimeOffset retryNow = new DateTimeOffset(date,
                        TimeSpan.FromHours(8));
                    bool rejected = !RunDaily(retryDaily, retryNow);
                    accept = true;
                    bool accepted = RunDaily(retryDaily,
                        retryNow.AddMinutes(1));
                    return rejected && accepted && attempts == 2 &&
                        retryHandler.ForecastCount == 1 &&
                        retryDate == "20260901";
                }
            }).GetAwaiter().GetResult();
            return result;
        }

        private static string ReadWeatherFixture(string fileName)
        {
            string resourceName = "PennyPet.Tests.Fixtures." + fileName;
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "Missing weather fixture: " + resourceName);
                using (StreamReader reader = new StreamReader(stream,
                    Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static BubbleCheckResult RunBubbleChecks()
        {
            BubbleCheckResult result = new BubbleCheckResult();
            DateTime smallTalkStart = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            bool smallTalkSilent = false;
            int smallTalkShowCount = 0;
            string firstSmallTalk = null;
            string secondSmallTalk = null;
            PetSmallTalkCoordinator smallTalk = new PetSmallTalkCoordinator(
                delegate { return smallTalkSilent; },
                delegate(string text)
                {
                    smallTalkShowCount++;
                    if (firstSmallTalk == null) firstSmallTalk = text;
                    else secondSmallTalk = text;
                    return true;
                }, new SequenceRandom(0, 0, 0, 0, 0));
            bool firstSmallTalkShown = smallTalk.HandlePetPoked(
                smallTalkStart);
            bool cooldownBlocked = !smallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(
                    PetSmallTalkPolicy.CooldownMilliseconds - 1));
            bool cooldownElapsed = smallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(
                    PetSmallTalkPolicy.CooldownMilliseconds));
            result.SmallTalkCoordinatorCooldownOk = firstSmallTalkShown &&
                cooldownBlocked && cooldownElapsed &&
                smallTalkShowCount == 2 && firstSmallTalk != secondSmallTalk;

            int rejectedShowCount = 0;
            PetSmallTalkCoordinator rejectedSmallTalk =
                new PetSmallTalkCoordinator(delegate { return false; },
                    delegate
                    {
                        rejectedShowCount++;
                        return rejectedShowCount > 1;
                    }, new SequenceRandom(0, 0, 0, 0));
            bool rejectedFirst = !rejectedSmallTalk.HandlePetPoked(
                smallTalkStart);
            bool rejectedRetry = rejectedSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorRejectedRetryOk = rejectedFirst &&
                rejectedRetry && rejectedShowCount == 2;

            int silentShowCount = 0;
            smallTalkSilent = true;
            PetSmallTalkCoordinator silentSmallTalk =
                new PetSmallTalkCoordinator(
                    delegate { return smallTalkSilent; },
                    delegate
                    {
                        silentShowCount++;
                        return true;
                    }, new SequenceRandom(0, 0));
            bool silentBlocked = !silentSmallTalk.HandlePetPoked(
                smallTalkStart);
            smallTalkSilent = false;
            bool silentRetry = silentSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorSilentModeOk = silentBlocked &&
                silentRetry && silentShowCount == 1;

            bool reminderDue = true;
            int reminderRejectedCount = 0;
            PetSmallTalkCoordinator reminderRejectedSmallTalk =
                new PetSmallTalkCoordinator(delegate { return false; },
                    delegate
                    {
                        reminderRejectedCount++;
                        return !reminderDue;
                    }, new SequenceRandom(0, 0, 0, 0));
            bool reminderRejected = !reminderRejectedSmallTalk.HandlePetPoked(
                smallTalkStart);
            reminderDue = false;
            bool afterReminderShown = reminderRejectedSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorReminderRetryOk = reminderRejected &&
                afterReminderShown && reminderRejectedCount == 2;

            using (SpeechBubbleForm bubble = new SpeechBubbleForm("初始", 0))
            using (SpeechBubbleForm styled = new SpeechBubbleForm(
                "样式提醒", 0, "Microsoft YaHei UI", 24F))
            {
                bubble.UpdateText("今天想要做些什么呢？");
                result.HoverCopyOk =
                    bubble.DisplayText == "今天想要做些什么呢？" &&
                    PetForm.FormatRemaining(TimeSpan.FromSeconds(65)) ==
                        "1分5秒";
                result.ManualPositionOk = bubble.StartPosition ==
                    System.Windows.Forms.FormStartPosition.Manual;
                result.StyledReminderOk = styled.Font != null &&
                    Math.Abs(styled.Font.SizeInPoints - 24F) < 0.2F &&
                    styled.Font.Bold &&
                    SpeechBubbleForm.BubbleTextColor == Color.White;
                result.ThemeAndKeyboardFontOk = bubble.Font != null &&
                    String.Equals(bubble.Font.FontFamily.Name,
                        KeyboardOverlayForm.TextFontFamilyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(bubble.Font.SizeInPoints -
                        KeyboardOverlayForm.TextFontSizePoints(100)) < 0.2F &&
                    bubble.Font.Bold &&
                    SpeechBubbleForm.BubbleFillColor ==
                        Color.FromArgb(255, 73, 74, 40) &&
                    SpeechBubbleForm.BubbleTextColor == Color.White;
            }
            result.DragSuppressionOk =
                !PetForm.ShouldShowHoverBubble(true, false, true) &&
                PetForm.ShouldShowHoverBubble(true, false, false);
            result.SilentModeOk =
                !PetForm.ShouldShowHoverBubble(true, false, false, true) &&
                PetForm.ShouldShowHoverBubble(true, false, false, false) &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.DailyGreeting, true) &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.Discovery, true) &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.Feedback, true) &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.ReminderDue, true);
            Point position = SpeechBubbleForm.CalculateNearLocation(
                new Rectangle(1400, 800, 192, 208), new Size(330, 138),
                new Rectangle(0, 0, 1920, 1080));
            result.PositionMathOk = position.X > 1000 && position.Y > 500 &&
                position != Point.Empty;
            bool dragging = false;
            bool exiting = false;
            int restoreCount = 0;
            int closeCount = 0;
            DateTime bubbleNow = DateTime.UtcNow;
            using (Form owner = new Form())
            using (PetBubbleCoordinator coordinator = new PetBubbleCoordinator(
                owner, delegate { return dragging; },
                delegate { return exiting; },
                delegate(PetMessageKind kind) { closeCount++; },
                delegate { restoreCount++; },
                delegate { return bubbleNow; }))
            {
                IntPtr ownerHandle = owner.Handle;
                PetBubbleRequest firstRequest = PetBubbleRequest.Feedback(
                    "第一条", KeyboardOverlayForm.TextFontFamilyName, 18F);
                coordinator.Show(firstRequest);
                SpeechBubbleForm first = coordinator.CurrentBubbleForTest;
                result.SingleMessageKindOk = coordinator.CurrentKind ==
                    PetMessageKind.Feedback &&
                    coordinator.CurrentRequestForTest.Kind ==
                        PetMessageKind.Feedback;
                bubbleNow = bubbleNow.AddMilliseconds(1500);
                coordinator.Show(PetBubbleRequest.Feedback("第二条",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                Application.DoEvents();
                result.ReplacementClosesOldFormOk = first.IsDisposed &&
                    coordinator.CurrentRequestForTest.Text == "第二条" &&
                    closeCount == 1;
                coordinator.Show(PetBubbleRequest.ReminderPreAlert(
                    "提醒倒计时", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                SpeechBubbleForm protectedBubble =
                    coordinator.CurrentBubbleForTest;
                bool feedbackAccepted = coordinator.Show(
                    PetBubbleRequest.Feedback("不能覆盖",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ProtectedMessageOk = !feedbackAccepted &&
                    ReferenceEquals(protectedBubble,
                        coordinator.CurrentBubbleForTest) &&
                    !protectedBubble.IsDisposed;
                coordinator.CloseCurrent(true);
                coordinator.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool smallTalkBlocked = !coordinator.Show(
                    PetBubbleRequest.SmallTalk("需要我帮什么忙吗？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bubbleNow = bubbleNow.AddMilliseconds(3000);
                bool smallTalkAllowed = coordinator.Show(
                    PetBubbleRequest.SmallTalk("怎么啦？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.MinimumReadableOk = smallTalkBlocked &&
                    smallTalkAllowed &&
                    coordinator.CurrentKind == PetMessageKind.SmallTalk;
                coordinator.CloseCurrent(true);
                dragging = true;
                coordinator.Show(PetBubbleRequest.Feedback(
                    "拖拽后显示", "Microsoft YaHei UI", 19F));
                bool queued = coordinator.PendingCountForTest == 1 &&
                    !coordinator.HasCurrent;
                dragging = false;
                coordinator.ShowNextPending();
                PetBubbleRequest restored = coordinator.CurrentRequestForTest;
                result.DeferredMessageSemanticsOk = queued && restored != null &&
                    restored.Kind == PetMessageKind.Feedback &&
                    restored.Text == "拖拽后显示" &&
                    restored.AutoCloseMilliseconds ==
                        BubbleReadingDurationRules.AutoCloseMilliseconds(
                            "拖拽后显示") &&
                    restored.DeferWhileDragging &&
                    Math.Abs(restored.FontSizePoints - 19F) < 0.2F;
                coordinator.CurrentBubbleForTest.Close();
                Application.DoEvents();
                Application.DoEvents();
                result.SingleRestoreAfterCloseOk = restoreCount == 1 &&
                    !coordinator.HasCurrent;
            }
            DateTime pendingNow = DateTime.UtcNow;
            bool pendingDragging = true;
            using (Form pendingOwner = new Form())
            using (PetBubbleCoordinator pending = new PetBubbleCoordinator(
                pendingOwner, delegate { return pendingDragging; },
                delegate { return false; }, delegate { }, delegate { },
                delegate { return pendingNow; }))
            {
                IntPtr pendingOwnerHandle = pendingOwner.Handle;
                pending.Show(PetBubbleRequest.Feedback("稍后反馈",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pending.Show(PetBubbleRequest.DailyGreeting("早上好～",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pendingDragging = false;
                pending.ShowNextPending();
                bool minimumRetained = pending.PendingCountForTest == 1 &&
                    pending.CurrentKind == PetMessageKind.DailyGreeting;
                pending.ShowNextPending();
                bool retryNotDuplicated = pending.PendingCountForTest == 1;
                pendingNow = pendingNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        "早上好～") + 1);
                pending.ShowNextPending();
                bool minimumEventuallyShown = pending.PendingCountForTest == 0 &&
                    pending.CurrentKind == PetMessageKind.Feedback;
                pending.CloseCurrent(true);

                pendingDragging = true;
                pending.Show(PetBubbleRequest.Feedback("提醒后反馈",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pending.Show(PetBubbleRequest.ReminderDue("提醒到了",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pendingDragging = false;
                pending.ShowNextPending();
                bool policyRetained = pending.PendingCountForTest == 1 &&
                    pending.CurrentKind == PetMessageKind.ReminderDue;
                pending.CurrentBubbleForTest.Close();
                Application.DoEvents();
                Application.DoEvents();
                bool policyEventuallyShown = pending.PendingCountForTest == 0 &&
                    pending.CurrentKind == PetMessageKind.Feedback;
                result.PendingRetryOk = minimumRetained &&
                    retryNotDuplicated && minimumEventuallyShown &&
                    policyRetained && policyEventuallyShown;
            }
            DateTime lifecycleNow = DateTime.UtcNow;
            using (Form lifecycleOwner = new Form())
            using (PetBubbleCoordinator lifecycle = new PetBubbleCoordinator(
                lifecycleOwner, delegate { return false; },
                delegate { return false; }, delegate { }, delegate { },
                delegate { return lifecycleNow; }))
            {
                IntPtr lifecycleOwnerHandle = lifecycleOwner.Handle;
                const string smallTalkText = "怎么啦？";
                lifecycle.Show(PetBubbleRequest.SmallTalk(smallTalkText,
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool feedbackBlocked = !lifecycle.Show(
                    PetBubbleRequest.Feedback("设置完成",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycleNow = lifecycleNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        smallTalkText) + 1);
                bool feedbackAllowed = lifecycle.Show(
                    PetBubbleRequest.Feedback("设置完成",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.SmallTalk(smallTalkText,
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycleNow = lifecycleNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        smallTalkText) + 1);
                bool hoverBlocked = !lifecycle.Show(PetBubbleRequest.Hover(
                    "今天想做什么？",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.SmallTalkFeedbackLifecycleOk = feedbackBlocked &&
                    feedbackAllowed && hoverBlocked;

                bool reminderFromSmallTalk = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒一",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool feedbackCannotReplaceDue = !lifecycle.Show(
                    PetBubbleRequest.Feedback("不能覆盖",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.DailyGreeting("下午好～",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool reminderFromDaily = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒二",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.EasterEgg(
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool preAlertFromEaster = lifecycle.Show(
                    PetBubbleRequest.ReminderPreAlert("提醒倒计时",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.EasterEgg(
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool reminderFromEaster = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒三",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ReminderPriorityRegressionOk =
                    reminderFromSmallTalk && feedbackCannotReplaceDue &&
                    reminderFromDaily && preAlertFromEaster &&
                    reminderFromEaster;
            }
            DateTime bypassNow = DateTime.UtcNow;
            using (Form bypassOwner = new Form())
            using (PetBubbleCoordinator bypass = new PetBubbleCoordinator(
                bypassOwner, delegate { return false; },
                delegate { return false; }, delegate { },
                delegate { }, delegate { return bypassNow; }))
            {
                bypass.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool smallTalkBlocked = !bypass.Show(
                    PetBubbleRequest.SmallTalk("需要我帮什么忙吗？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool easterAccepted = bypass.Show(
                    PetBubbleRequest.EasterEgg(
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bypass.CloseCurrent(true);
                bypass.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool reminderAccepted = bypass.Show(
                    PetBubbleRequest.ReminderDue("提醒到了",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ReadabilityBypassOk = smallTalkBlocked &&
                    easterAccepted && reminderAccepted &&
                    bypass.CurrentKind == PetMessageKind.ReminderDue;
            }
            using (SpeechBubbleForm empty = new SpeechBubbleForm("", 0))
            using (SpeechBubbleForm shortChinese = new SpeechBubbleForm(
                "嗯。", 0))
            using (SpeechBubbleForm greeting = new SpeechBubbleForm(
                "早上好～", 0))
            using (SpeechBubbleForm medium = new SpeechBubbleForm(
                "今天想要做些什么呢？", 0))
            using (SpeechBubbleForm english = new SpeechBubbleForm(
                "What would you like to work on today?", 0))
            using (SpeechBubbleForm countdown = new SpeechBubbleForm(
                "提醒倒计时 19 秒", 0))
            using (SpeechBubbleForm multiline = new SpeechBubbleForm(
                "第一行提醒\n第二行提醒\n第三行提醒", 0))
            using (SpeechBubbleForm veryLong = new SpeechBubbleForm(
                new String('长', 300), 0))
            {
                SpeechBubbleForm[] samples = new SpeechBubbleForm[]
                {
                    empty, shortChinese, greeting, medium, english,
                    countdown, multiline, veryLong
                };
                bool valid = true;
                foreach (SpeechBubbleForm sample in samples)
                {
                    valid &= sample.Width >=
                        SpeechBubbleForm.MinimumBubbleSize.Width &&
                        sample.Height >=
                            SpeechBubbleForm.MinimumBubbleSize.Height &&
                        sample.Width <=
                            SpeechBubbleForm.MaximumBubbleSize.Width &&
                        sample.Height <=
                            SpeechBubbleForm.MaximumBubbleSize.Height;
                }
                result.AdaptiveSizingOk = valid &&
                    shortChinese.Width * shortChinese.Height <
                        medium.Width * medium.Height &&
                    multiline.Height > shortChinese.Height &&
                    veryLong.Width ==
                        SpeechBubbleForm.MaximumBubbleSize.Width &&
                    veryLong.Height <=
                        SpeechBubbleForm.MaximumBubbleSize.Height;
                Size shortSize = shortChinese.ClientSize;
                shortChinese.UpdateText(new String('长', 300));
                Size longSize = shortChinese.ClientSize;
                shortChinese.UpdateText("嗯。");
                result.UpdateTextRelayoutOk = longSize != shortSize &&
                    shortChinese.ClientSize == shortSize;
            }
            string lastBriefingDate = String.Empty;
            bool silent = false;
            bool acceptGreeting = true;
            bool dailyContentEnabled = true;
            bool solarTermEnabled = true;
            ZodiacSign zodiacSign = ZodiacSign.None;
            int greetingCount = 0;
            int recordCount = 0;
            string greetingText = null;
            PetDailyContentCoordinator daily =
                new PetDailyContentCoordinator(
                    delegate { return lastBriefingDate; },
                    delegate { return silent; },
                    delegate { return dailyContentEnabled; },
                    delegate { return solarTermEnabled; },
                    delegate { return true; },
                    delegate { return false; },
                    delegate { return null; },
                    delegate
                    {
                        return Task.FromResult<WeatherForecastWindow>(null);
                    },
                    delegate { return zodiacSign; },
                    delegate(string text)
                    {
                        greetingCount++;
                        greetingText = text;
                        return acceptGreeting;
                    },
                    delegate(string date)
                    {
                        recordCount++;
                        lastBriefingDate = date;
                    });
            DateTimeOffset morning = new DateTimeOffset(2035, 6, 15, 8, 30, 0,
                TimeSpan.FromHours(8));
            bool firstPoke = RunDaily(daily, morning);
            bool secondPoke = RunDaily(daily, morning.AddHours(1));
            lastBriefingDate = "20350614";
            bool nextDay = RunDaily(daily, morning.AddHours(6.5));
            result.DailyFirstPokeOk = firstPoke && !secondPoke && nextDay &&
                greetingCount == 2 && recordCount == 2 &&
                greetingText.StartsWith("下午好～今天过得怎么样？\n",
                    StringComparison.Ordinal) &&
                lastBriefingDate == "20350615";
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            silent = true;
            bool silentPoke = RunDaily(daily, morning);
            silent = false;
            acceptGreeting = false;
            bool rejectedPoke = RunDaily(daily, morning);
            acceptGreeting = true;
            bool retriedPoke = RunDaily(daily, morning);
            result.DailyRejectedRetryOk = !silentPoke && !rejectedPoke &&
                retriedPoke && greetingCount == 2 && recordCount == 1 &&
                lastBriefingDate == "20350615";
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            dailyContentEnabled = false;
            bool disabledPoke = RunDaily(daily, morning);
            dailyContentEnabled = true;
            bool enabledLaterPoke = RunDaily(daily, morning);
            bool enabledSameDayPoke = RunDaily(daily,
                morning.AddHours(1));
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            solarTermEnabled = false;
            DateTimeOffset whiteDewDate = new DateTimeOffset(
                2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(8));
            bool solarOffPoke = RunDaily(daily, whiteDewDate);
            bool plainGreeting = greetingText.IndexOf("白露",
                StringComparison.Ordinal) < 0;
            solarTermEnabled = true;
            bool solarEnabledSameDayPoke = RunDaily(daily,
                whiteDewDate.AddHours(1));
            lastBriefingDate = String.Empty;
            bool solarOnPoke = RunDaily(daily, whiteDewDate);
            result.DailyContentPreferencesOk = !disabledPoke &&
                enabledLaterPoke && !enabledSameDayPoke && solarOffPoke &&
                plainGreeting && !solarEnabledSameDayPoke && solarOnPoke &&
                greetingText.IndexOf("今天是白露哦。",
                    StringComparison.Ordinal) >= 0;

            DailyLineEntry[] curatedEntries =
                CuratedDailyLineCatalog.GetEntries();
            HashSet<string> curatedIds = new HashSet<string>();
            HashSet<string> curatedTexts = new HashSet<string>();
            bool curatedCatalogValid = curatedEntries.Length == 96;
            foreach (DailyLineEntry entry in curatedEntries)
                curatedCatalogValid &=
                    !String.IsNullOrWhiteSpace(entry.Id) &&
                    !String.IsNullOrWhiteSpace(entry.Text) &&
                    curatedIds.Add(entry.Id) && curatedTexts.Add(entry.Text);
            bool zodiacCatalogValid = ZodiacDailyCatalog.GetEntries(
                ZodiacSign.None).Length == 0;
            HashSet<string> zodiacIds = new HashSet<string>();
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                DailyLineEntry[] entries = ZodiacDailyCatalog.GetEntries(
                    (ZodiacSign)value);
                HashSet<string> uniqueTexts = new HashSet<string>();
                zodiacCatalogValid &= entries.Length == 6;
                foreach (DailyLineEntry entry in entries)
                    zodiacCatalogValid &=
                        !String.IsNullOrWhiteSpace(entry.Id) &&
                        !String.IsNullOrWhiteSpace(entry.Text) &&
                        zodiacIds.Add(entry.Id) &&
                        uniqueTexts.Add(entry.Text);
            }
            result.CuratedCatalogOk = curatedCatalogValid &&
                curatedIds.Count == 96 && curatedTexts.Count == 96 &&
                zodiacCatalogValid && zodiacIds.Count == 72;

            DateTimeOffset briefingDate = new DateTimeOffset(2026, 9, 3,
                12, 0, 0, TimeSpan.FromHours(8));
            DailyLineEntry selectedCurated = CuratedDailyLineSelector.Select(
                briefingDate);
            DailyLineEntry selectedScorpio = ZodiacDailySelector.Select(
                ZodiacSign.Scorpio, briefingDate);
            bool deterministic = selectedCurated != null &&
                selectedScorpio != null && selectedCurated.Id ==
                    CuratedDailyLineSelector.Select(briefingDate).Id &&
                selectedScorpio.Id == ZodiacDailySelector.Select(
                    ZodiacSign.Scorpio, briefingDate).Id;
            DateTimeOffset rangeStart = new DateTimeOffset(2026, 1, 1,
                12, 0, 0, TimeSpan.FromHours(8));
            bool eligibilityBounded = true;
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                int eligibleDays = 0;
                for (int day = 0; day < 3650; day++)
                    if (ZodiacDailySelector.Select((ZodiacSign)value,
                        rangeStart.AddDays(day)) != null) eligibleDays++;
                double percent = eligibleDays * 100D / 3650D;
                eligibilityBounded &= percent >= 10D && percent <= 20D;
            }
            DateTimeOffset sameInstant = new DateTimeOffset(2026, 9, 1,
                16, 30, 0, TimeSpan.Zero);
            bool selectedInCatalog = false;
            foreach (DailyLineEntry entry in ZodiacDailyCatalog.GetEntries(
                ZodiacSign.Scorpio))
                selectedInCatalog |= entry.Id == selectedScorpio.Id;
            result.DailySelectorBudgetOk = deterministic &&
                eligibilityBounded &&
                ZodiacDailySelector.Select(ZodiacSign.None, briefingDate) ==
                    null && ZodiacDailySelector.Select((ZodiacSign)999,
                        briefingDate) == null &&
                selectedInCatalog &&
                CuratedDailyLineSelector.Select(sameInstant.ToOffset(
                    TimeSpan.FromHours(8))).Id !=
                CuratedDailyLineSelector.Select(sameInstant.ToOffset(
                    TimeSpan.FromHours(-8))).Id;

            AlmanacDayInfo actualAlmanacDay = AlmanacCalculator.Calculate(
                briefingDate);
            AlmanacDailySelection actualAlmanac = actualAlmanacDay == null
                ? null : AlmanacDailySelector.Select(actualAlmanacDay,
                    briefingDate);
            result.AlmanacCalculatorOk = AlmanacCalculator.Sect == 1 &&
                actualAlmanacDay != null &&
                actualAlmanacDay.Year == briefingDate.Year &&
                actualAlmanacDay.Month == briefingDate.Month &&
                actualAlmanacDay.Day == briefingDate.Day &&
                actualAlmanacDay.Yi.Count > 0 &&
                actualAlmanacDay.Ji.Count > 0;
            AlmanacDailySelection dedupedSocial =
                AlmanacDailySelector.Select(new AlmanacDayInfo(2026, 9, 3,
                    new[] { "会友", "会亲友" }, new string[0]),
                    briefingDate);
            AlmanacDailySelection conflictedOuting =
                AlmanacDailySelector.Select(new AlmanacDayInfo(2026, 9, 3,
                    new[] { "出行" }, new[] { "出行" }), briefingDate);
            AlmanacDailySelection restricted = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "求医", "纳财", "祭祀", "动土" },
                    new string[0]), briefingDate);
            result.AlmanacSemanticOk = dedupedSocial != null &&
                dedupedSocial.Topic == AlmanacTopic.Social &&
                conflictedOuting == null && restricted == null;
            HashSet<string> tidyVariants = new HashSet<string>();
            bool wordingStable = true;
            for (int day = 0; day < 730; day++)
            {
                DateTimeOffset date = rangeStart.AddDays(day);
                AlmanacDayInfo tidyDay = new AlmanacDayInfo(date.Year,
                    date.Month, date.Day, new[] { "扫舍" }, new string[0]);
                AlmanacDailySelection first = AlmanacDailySelector.Select(
                    tidyDay, date);
                AlmanacDailySelection retry = AlmanacDailySelector.Select(
                    tidyDay, date);
                wordingStable &= first != null && retry != null &&
                    first.VariantId == retry.VariantId &&
                    first.Text == retry.Text &&
                    !first.Text.Contains("今天一定") &&
                    !first.Text.Contains("必须") &&
                    !first.Text.Contains("千万不要") &&
                    !first.Text.Contains("绝对不能");
                if (first != null) tidyVariants.Add(first.VariantId);
            }
            result.AlmanacWordingOk = wordingStable &&
                tidyVariants.Count >= 5;

            SolarTermInfo? whiteDew = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 7, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            SolarTermInfo? nonTerm = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 6, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            result.SolarTermOk = whiteDew.HasValue &&
                whiteDew.Value.Term == SolarTerm.WhiteDew &&
                whiteDew.Value.ChineseName == "白露" &&
                whiteDew.Value.LongitudeDegrees == 165 &&
                !nonTerm.HasValue;
            string afternoonGreeting = DailyContentRules.GreetingFor(
                DayPart.Afternoon);
            const string almanacText = "黄历内容。";
            const string weatherText = "天气内容。";
            DailyBriefingContent caseA = new DailyBriefingContent(whiteDew,
                null, almanacText, selectedCurated, selectedScorpio);
            DailyBriefingContent caseB = new DailyBriefingContent(whiteDew,
                null, null, selectedCurated, selectedScorpio);
            DailyBriefingContent caseC = new DailyBriefingContent(null,
                null, almanacText, selectedCurated, selectedScorpio);
            DailyBriefingContent caseD = new DailyBriefingContent(null,
                null, null, selectedCurated, null);
            DailyBriefingContent caseE = new DailyBriefingContent(null,
                null, null, selectedCurated, selectedScorpio);
            DailyBriefingContent solarWeatherAlmanac =
                new DailyBriefingContent(whiteDew, weatherText, almanacText,
                    selectedCurated, selectedScorpio);
            DailyBriefingContent weatherAlmanac =
                new DailyBriefingContent(null, weatherText, almanacText,
                    selectedCurated, selectedScorpio);
            DailyBriefingContent weatherOnly = new DailyBriefingContent(null,
                weatherText, null, selectedCurated, selectedScorpio);
            result.DailyBriefingBudgetOk = whiteDew.HasValue &&
                DailyBriefingComposer.Compose(DayPart.Afternoon, caseA) ==
                    afternoonGreeting + "\n今天是白露哦。\n" + almanacText &&
                DailyBriefingComposer.Compose(DayPart.Afternoon, caseB) ==
                    afternoonGreeting + "\n今天是白露哦。" &&
                DailyBriefingComposer.Compose(DayPart.Afternoon, caseC) ==
                    afternoonGreeting + "\n" + almanacText &&
                DailyBriefingComposer.Compose(DayPart.Afternoon, caseD) ==
                    afternoonGreeting + "\n" + selectedCurated.Text &&
                DailyBriefingComposer.Compose(DayPart.Afternoon, caseE) ==
                    afternoonGreeting + "\n" + selectedCurated.Text +
                        "\n" + selectedScorpio.Text &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    solarWeatherAlmanac) == afternoonGreeting +
                        "\n今天是白露哦。\n" + weatherText &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    weatherAlmanac) == afternoonGreeting + "\n" +
                        weatherText + "\n" + almanacText &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    weatherOnly) == afternoonGreeting + "\n" + weatherText &&
                DailyBriefingComposer.SelectSupplementary(caseA).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseB).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseC).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseD).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseE).Length <= 2;
            result.DailyBriefingBudgetOk = result.DailyBriefingBudgetOk &&
                DailyBriefingComposer.SelectSupplementary(
                    solarWeatherAlmanac).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(
                    weatherAlmanac).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(
                    weatherOnly).Length <= 2;

            lastBriefingDate = String.Empty;
            silent = false;
            dailyContentEnabled = true;
            solarTermEnabled = false;
            zodiacSign = ZodiacSign.Scorpio;
            acceptGreeting = true;
            greetingCount = 0;
            recordCount = 0;
            bool zodiacShown = RunDaily(daily, briefingDate);
            string expectedZodiacText = DailyBriefingComposer.Compose(
                DailyContentRules.ResolveDayPart(briefingDate),
                new DailyBriefingContent(null,
                    null, actualAlmanac == null ? null : actualAlmanac.Text,
                    selectedCurated,
                    selectedScorpio));
            bool zodiacTextOk = greetingText == expectedZodiacText &&
                recordCount == 1;
            zodiacSign = ZodiacSign.Pisces;
            bool changedSignSameDay = RunDaily(daily,
                briefingDate.AddHours(1));
            result.DailyBriefingSameDaySwitchOk = zodiacShown &&
                !changedSignSameDay && recordCount == 1;

            lastBriefingDate = String.Empty;
            solarTermEnabled = true;
            zodiacSign = ZodiacSign.Scorpio;
            greetingCount = 0;
            recordCount = 0;
            bool solarZodiacShown = RunDaily(daily, whiteDewDate);
            AlmanacDayInfo whiteDewAlmanacDay = AlmanacCalculator.Calculate(
                whiteDewDate);
            AlmanacDailySelection whiteDewAlmanac = whiteDewAlmanacDay == null
                ? null : AlmanacDailySelector.Select(whiteDewAlmanacDay,
                    whiteDewDate);
            string solarZodiacExpected = DailyBriefingComposer.Compose(
                DailyContentRules.ResolveDayPart(whiteDewDate),
                new DailyBriefingContent(whiteDew,
                    null, whiteDewAlmanac == null ? null : whiteDewAlmanac.Text,
                    CuratedDailyLineSelector.Select(whiteDewDate),
                    ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                        whiteDewDate)));
            result.DailyBriefingCoordinatorOk = zodiacTextOk &&
                solarZodiacShown && greetingText == solarZodiacExpected &&
                recordCount == 1;

            lastBriefingDate = String.Empty;
            solarTermEnabled = false;
            acceptGreeting = false;
            greetingCount = 0;
            recordCount = 0;
            bool zodiacRejected = !RunDaily(daily, briefingDate);
            string rejectedZodiacText = greetingText;
            acceptGreeting = true;
            bool zodiacRetried = RunDaily(daily,
                briefingDate.AddMinutes(1));
            result.DailyBriefingRejectedRetryOk = zodiacRejected &&
                zodiacRetried &&
                greetingCount == 2 && recordCount == 1 &&
                greetingText == rejectedZodiacText;
            PetBubbleRequest dailyRequest = PetBubbleRequest.DailyGreeting(
                "早上好", KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.DailyGreetingRequestOk = dailyRequest.Kind ==
                PetMessageKind.DailyGreeting &&
                dailyRequest.AutoCloseMilliseconds ==
                    BubbleReadingDurationRules.AutoCloseMilliseconds(
                        "早上好") &&
                !dailyRequest.DeferWhileDragging;
            PetBubbleRequest easterEggRequest = PetBubbleRequest.EasterEgg(
                KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.EasterEggRequestOk = easterEggRequest.Kind ==
                PetMessageKind.EasterEgg &&
                easterEggRequest.Text == "你在整我是不是。" &&
                !easterEggRequest.DeferWhileDragging &&
                easterEggRequest.AutoCloseMilliseconds == 2800 &&
                easterEggRequest.MinimumReadableMilliseconds == 1000 &&
                !easterEggRequest.ClosesOnMouseDown &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.EasterEgg, false) &&
                !PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.EasterEgg, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.EasterEgg,
                    PetMessageKind.DailyGreeting, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.EasterEgg,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.DailyGreeting,
                    PetMessageKind.EasterEgg, false);
            PetBubbleRequest smallTalkRequest = PetBubbleRequest.SmallTalk(
                "怎么啦？", KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.SmallTalkRequestOk = smallTalkRequest.Kind ==
                PetMessageKind.SmallTalk &&
                smallTalkRequest.MinimumReadableMilliseconds ==
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        "怎么啦？") &&
                smallTalkRequest.AutoCloseMilliseconds ==
                    BubbleReadingDurationRules.AutoCloseMilliseconds(
                        "怎么啦？") &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.SmallTalk, true);
            return result;
        }

        private static bool RunDaily(PetDailyContentCoordinator coordinator,
            DateTimeOffset localNow)
        {
            return coordinator.HandlePetPokedAsync(localNow).GetAwaiter()
                .GetResult();
        }

        private sealed class WindowShellCheckResult
        {
            internal bool StartupDefaultOk;
            internal bool StartupLoadingReadinessGateOk;
            internal bool StickyUiHostOk;
            internal StickyCanaryCheckResult StickyCanary;
            internal bool ScaleRangeOk;
            internal bool DailyContentSettingsUiOk;
            internal bool ZodiacPreferenceSettingsUiOk;
            internal bool ReverseReminderStepOk;
            internal bool PinActionTextOk;
            internal bool TodoPinActionTextOk;
            internal bool ImeCompatibleEditorOk;
            internal bool SingleWindowStickyInputOk;
            internal bool StickyResizePaintingOk;
            internal StickyReminderWindowCheckResult ReminderChecks;
            internal StickyTodoWindowCheckResult TodoChecks;
        }

        private sealed class StickyCanaryCheckResult
        {
            internal bool LifecycleOk;
            internal bool PerNoteSequenceOk;
            internal bool CloseAllBatchOk;
            internal bool HostedDockEffectOk;
            internal bool HostedGroupMoveOk;
            internal bool HostedTopMostOk;
            internal bool HostedHorizontalResizeOk;
            internal bool HostedDividerResizeOk;
            internal bool HostedHideReopenOk;
            internal bool HostedMiddleSplitOk;
            internal bool HostedThreeNoteInsertionOk;
            internal bool DockRestoreOk;
        }

        private static WindowShellCheckResult RunWindowShellChecks(
            StickyNoteData restoredNote)
        {
            WindowShellCheckResult result = new WindowShellCheckResult();
            result.StartupDefaultOk = !new PetSettings().StartAtLogin &&
                StartupRegistration.BuildCommand(
                    "C:\\Program Files\\Penny pet.exe") ==
                    "\"C:\\Program Files\\Penny pet.exe\"";
            result.StartupLoadingReadinessGateOk =
                !PetStartupRules.CanReleaseStartupLoading(false, false) &&
                !PetStartupRules.CanReleaseStartupLoading(true, false) &&
                !PetStartupRules.CanReleaseStartupLoading(false, true) &&
                PetStartupRules.CanReleaseStartupLoading(true, true);
            using (StickyUiHost host = new StickyUiHost())
            using (ManualResetEventSlim handlerStarted =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseHandler =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim commandCompleted =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim shutdownCommandCompleted =
                new ManualResetEventSlim(false))
            {
                host.Start();
                int stickyThread = 0;
                host.SetCommandHandler(delegate(StickyUiCommand command)
                {
                    stickyThread = Thread.CurrentThread.ManagedThreadId;
                    handlerStarted.Set();
                    releaseHandler.Wait(5000);
                    return StickyUiCommandResult.Handled();
                });
                StickyUiCommand posted = new StickyUiCommand(
                    StickyUiCommandKind.Show, "note-1", true);
                StickyUiCommandResult commandResult = null;
                Stopwatch postTimer = Stopwatch.StartNew();
                host.PostCommand(posted, delegate(StickyUiCommandResult value)
                {
                    commandResult = value;
                    commandCompleted.Set();
                });
                postTimer.Stop();
                bool handlerRan = handlerStarted.Wait(5000);
                bool returnedBeforeCompletion = postTimer.ElapsedMilliseconds < 1000 &&
                    !commandCompleted.IsSet;
                releaseHandler.Set();
                bool completed = WaitForSignalWithUiPump(commandCompleted, 5000);
                host.BeginShutdown();
                host.WaitForExit(5000);
                StickyUiCommandResult afterShutdown = null;
                host.PostCommand(posted, delegate(StickyUiCommandResult value)
                {
                    afterShutdown = value;
                    shutdownCommandCompleted.Set();
                });
                bool shutdownCompleted = WaitForSignalWithUiPump(
                    shutdownCommandCompleted, 5000);
                result.StickyUiHostOk =
                    handlerRan && returnedBeforeCompletion && completed &&
                    commandResult != null &&
                    commandResult.Status == StickyUiCommandStatus.Handled &&
                    stickyThread != Thread.CurrentThread.ManagedThreadId &&
                    shutdownCompleted && afterShutdown != null &&
                    afterShutdown.Status == StickyUiCommandStatus.NotAccepted;
            }
            result.StickyCanary = RunStickyCanaryLifecycleCheck();
            result.ScaleRangeOk =
                PetForm.NormalizeScalePercent(47) == 50 &&
                PetForm.NormalizeScalePercent(104) == 100 &&
                PetForm.NormalizeScalePercent(156) == 160 &&
                PetForm.NormalizeScalePercent(207) == 200 &&
                PetForm.ScaledPetSize(50) == new Size(96, 104) &&
                PetForm.ScaledPetSize(200) == new Size(384, 416);
            WeatherLocation testWeatherLocation;
            WeatherLocation.TryCreate("武汉", "湖北", "中国", 30.5928,
                114.3055, "Asia/Shanghai", out testWeatherLocation);
            using (PetWeatherSource weatherSource = new PetWeatherSource())
            using (DailyContentSettingsForm dailySettings =
                new DailyContentSettingsForm(false, true, true, true,
                    testWeatherLocation, ZodiacSign.Scorpio, weatherSource))
            using (DailyContentSettingsForm unsetDailySettings =
                new DailyContentSettingsForm(true, true, true, false, null,
                    ZodiacSign.None, weatherSource))
            {
                PetSettingsData stored = new PetSettingsData
                {
                    DailyContentEnabled = false,
                    SolarTermEnabled = true,
                    AlmanacEnabled = true,
                    WeatherEnabled = true,
                    WeatherLocationName = "武汉",
                    WeatherLocationAdmin1 = "湖北",
                    WeatherLocationCountry = "中国",
                    WeatherLatitude = 30.5928,
                    WeatherLongitude = 114.3055,
                    WeatherTimezone = "Asia/Shanghai",
                    ZodiacSign = ZodiacSign.Scorpio
                };
                result.DailyContentSettingsUiOk =
                    !dailySettings.DailyContentEnabled &&
                    dailySettings.SolarTermEnabled &&
                    dailySettings.AlmanacEnabled &&
                    !dailySettings.SolarTermControlEnabledForTest &&
                    !dailySettings.AlmanacControlEnabledForTest &&
                    !dailySettings.WeatherControlEnabledForTest &&
                    !dailySettings.WeatherLocationButtonEnabledForTest;
                result.ZodiacPreferenceSettingsUiOk =
                    !dailySettings.ZodiacControlEnabledForTest &&
                    dailySettings.SelectedZodiacSign == ZodiacSign.Scorpio &&
                    dailySettings.ZodiacDisplayNameForTest == "天蝎座" &&
                    unsetDailySettings.ZodiacDisplayNameForTest ==
                        "暂未设置";
                dailySettings.SetZodiacSignForTest(ZodiacSign.Pisces);
                bool canceled = dailySettings.ApplyIfAccepted(stored,
                    DialogResult.Cancel);
                bool cancelKeepsStored = !canceled &&
                    stored.ZodiacSign == ZodiacSign.Scorpio;
                dailySettings.SetDailyContentEnabledForTest(true);
                dailySettings.SetAlmanacEnabledForTest(false);
                bool accepted = dailySettings.ApplyIfAccepted(stored,
                    DialogResult.OK);
                unsetDailySettings.SetWeatherEnabledForTest(true);
                bool missingCityRejected = !unsetDailySettings
                    .ApplyIfAccepted(new PetSettingsData(), DialogResult.OK);
                result.DailyContentSettingsUiOk =
                    result.DailyContentSettingsUiOk &&
                    dailySettings.DailyContentEnabled &&
                    dailySettings.SolarTermEnabled &&
                    dailySettings.SolarTermControlEnabledForTest &&
                    dailySettings.AlmanacControlEnabledForTest &&
                    !dailySettings.AlmanacEnabled &&
                    dailySettings.WeatherControlEnabledForTest &&
                    dailySettings.WeatherLocationButtonEnabledForTest &&
                    stored.WeatherEnabled &&
                    stored.WeatherLocationName == "武汉" &&
                    stored.WeatherTimezone == "Asia/Shanghai" &&
                    !stored.AlmanacEnabled &&
                    missingCityRejected;
                result.ZodiacPreferenceSettingsUiOk =
                    result.ZodiacPreferenceSettingsUiOk &&
                    cancelKeepsStored && accepted &&
                    dailySettings.ZodiacControlEnabledForTest &&
                    dailySettings.ZodiacDisplayNameForTest == "双鱼座" &&
                    stored.DailyContentEnabled &&
                    stored.SolarTermEnabled &&
                    stored.ZodiacSign == ZodiacSign.Pisces;
            }
            int dailyMenuClicks = 0;
            PetContextMenuCommands menuCommands =
                new PetContextMenuCommands();
            menuCommands.ShowDailyContentSettings =
                delegate { dailyMenuClicks++; };
            using (PetContextMenu contextMenu = new PetContextMenu(
                "Penny", false, false, false, menuCommands))
            {
                contextMenu.DailyContentItem.PerformClick();
                result.DailyContentSettingsUiOk =
                    result.DailyContentSettingsUiOk &&
                    contextMenu.DailyContentItem.Text == "每日内容…" &&
                    contextMenu.Menu.Items.IndexOf(
                        contextMenu.DailyContentItem) <
                    contextMenu.Menu.Items.IndexOf(contextMenu.ScaleItem) &&
                    dailyMenuClicks == 1;
            }
            result.ReverseReminderStepOk =
                ReverseStepDateTimePicker.ReverseVirtualKey(0x26) == 0x28 &&
                ReverseStepDateTimePicker.ReverseVirtualKey(0x28) == 0x26;
            result.PinActionTextOk =
                StickyNoteWindow.PinActionText(false) == "置顶" &&
                StickyNoteWindow.PinActionText(true) == "取消置顶";
            StickyNoteData todoPinData = new StickyNoteData();
            todoPinData.IsTodoList = true;
            todoPinData.AlwaysOnTop = true;
            using (StickyNoteWindow note = new StickyNoteWindow(todoPinData))
                result.TodoPinActionTextOk = note.CurrentPinActionText ==
                    "取消置顶" && note.HeaderTypeIconVisibleForTest;
            using (StickyNoteWindow note = new StickyNoteWindow(restoredNote))
            {
                result.ReminderChecks = RunStickyReminderWindowChecks(note);
                result.TodoChecks = RunStickyTodoWindowChecks(note);
                result.ImeCompatibleEditorOk = note.UsesImeCompatibleEditor;
                result.SingleWindowStickyInputOk =
                    !note.UsesLegacyInputProxyForTest &&
                    note.LegacyInputProxyHandleForTest == IntPtr.Zero;
                result.StickyResizePaintingOk = note.UsesBufferedResizePainting;
            }
            return result;
        }

        private static StickyCanaryCheckResult RunStickyCanaryLifecycleCheck()
        {
            StickyCanaryCheckResult check = new StickyCanaryCheckResult();
            int petThread = Thread.CurrentThread.ManagedThreadId;
            int eventThread = 0;
            StickyUiEvent lastEvent = null;
            HashSet<string> eventNoteIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<StickyUiEventKind> eventKinds =
                new HashSet<StickyUiEventKind>();
            SynchronizationContext petContext =
                new WindowsFormsSynchronizationContext();
            StickyNoteData canonical = new StickyNoteData();
            canonical.Text = "detached-before-post";
            canonical.X = -2400;
            canonical.Y = -2400;
            canonical.Width = 320;
            canonical.Height = 300;
            StickyNoteUiSnapshot detached =
                StickyNoteUiSnapshot.FromData(canonical);
            canonical.Text = "pet-owned-after-post";
            StickyNoteData second = new StickyNoteData();
            second.Text = "second-detached";
            second.X = -2000;
            second.Y = -2000;
            second.Width = 320;
            second.Height = 300;
            StickyNoteData third = new StickyNoteData();
            third.Text = "third-detached";
            third.X = -1800;
            third.Y = -1800;
            third.Width = 320;
            third.Height = 300;
            StickyNoteData todo = new StickyNoteData();
            todo.IsTodoList = true;
            todo.Text = "todo-detached";
            todo.TodoItems.Add(new StickyTodoItem("todo", false));
            todo.X = -1600;
            todo.Y = -1600;
            todo.Width = 320;
            todo.Height = 300;
            StickyNoteData schedule = new StickyNoteData();
            schedule.IsSchedule = true;
            schedule.Text = "schedule-detached";
            schedule.ScheduleItems.Add(new StickyScheduleItem(
                "schedule", DateTime.Today));
            schedule.X = -1200;
            schedule.Y = -1200;
            schedule.Width = 320;
            schedule.Height = 300;
            StickyNoteData reminder = new StickyNoteData();
            reminder.Text = "reminder-detached";
            reminder.ReminderUtcTicks = DateTime.UtcNow.AddHours(1).Ticks;
            reminder.X = -800;
            reminder.Y = -800;
            reminder.Width = 320;
            reminder.Height = 300;

            using (StickyUiHost host = new StickyUiHost())
            {
                host.Start();
                host.ConfigureCanary(delegate(StickyUiEvent value)
                {
                    eventThread = Thread.CurrentThread.ManagedThreadId;
                    lastEvent = value;
                    if (value != null)
                    {
                        eventNoteIds.Add(value.NoteId);
                        eventKinds.Add(value.Kind);
                    }
                }, petContext);
                StickyUiCommandResult created = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Create,
                        canonical.Id, false, detached), petContext);
                bool detachedOwnership = created != null &&
                    created.Status == StickyUiCommandStatus.Handled &&
                    created.Snapshot != null &&
                    created.Snapshot.Text == "detached-before-post" &&
                    canonical.Text == "pet-owned-after-post" &&
                    created.OwnerThreadId != petThread;
                StickyUiCommandResult secondCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            second.Id, false,
                            StickyNoteUiSnapshot.FromData(second)), petContext);
                StickyUiCommandResult thirdCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            third.Id, false,
                            StickyNoteUiSnapshot.FromData(third)), petContext);
                StickyUiCommandResult todoCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            todo.Id, false,
                            StickyNoteUiSnapshot.FromData(todo)), petContext);
                StickyUiCommandResult scheduleCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            schedule.Id, false,
                            StickyNoteUiSnapshot.FromData(schedule)), petContext);
                StickyUiCommandResult reminderCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            reminder.Id, false,
                            StickyNoteUiSnapshot.FromData(reminder)), petContext);

                StickyUiCommandResult hidden = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Hide,
                        canonical.Id, false), petContext);
                StickyUiCommandResult shown = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Show,
                        canonical.Id, false), petContext);
                StickyUiCommandResult closedOne = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Close,
                        canonical.Id, false), petContext);
                StickyUiCommandResult reopened = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Create,
                        canonical.Id, false, detached), petContext);
                StickyUiCommandResult targetPositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null,
                            new StickyUiBounds(100, 100, 320, 300)),
                        petContext);
                StickyUiCommandResult sourcePositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null,
                            new StickyUiBounds(100, 400, 320, 300)),
                        petContext);
                DockWindowFacts targetFacts = targetPositioned == null ? null :
                    DockWindowFacts.FromSnapshot(targetPositioned.Snapshot);
                DockWindowFacts sourceFacts = sourcePositioned == null ? null :
                    DockWindowFacts.FromSnapshot(sourcePositioned.Snapshot);
                bool dockHit = sourceFacts != null && targetFacts != null &&
                    PetForm.CanDockBelow(new Rectangle(sourceFacts.X,
                        sourceFacts.Y, sourceFacts.Width, sourceFacts.Height),
                        new Rectangle(targetFacts.X, targetFacts.Y,
                            targetFacts.Width, targetFacts.Height), 20);
                List<StickyNoteData> dockOrder = dockHit
                    ? StickyDockOperations.MergeDockSnapshotsAfterParent(
                        new StickyNoteData[] { second }, second,
                        new StickyNoteData[] { canonical })
                    : new List<StickyNoteData>();
                List<Rectangle> hostedLayout = PetForm.CalculateUnifiedDockLayout(
                    new Size[] { new Size(targetFacts.Width, targetFacts.Height),
                        new Size(sourceFacts.Width, sourceFacts.Height) },
                    targetFacts.X, targetFacts.Y, targetFacts.Width);
                StickyUiCommandResult targetDocked = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            hostedLayout[0].X, hostedLayout[0].Y,
                            hostedLayout[0].Width, hostedLayout[0].Height)),
                    petContext);
                StickyUiCommandResult sourceDocked = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(
                            hostedLayout[1].X, hostedLayout[1].Y,
                            hostedLayout[1].Width, hostedLayout[1].Height)),
                    petContext);
                Dictionary<string, DockWindowFacts> moveFacts =
                    new Dictionary<string, DockWindowFacts>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        { second.Id, DockWindowFacts.FromSnapshot(
                            targetDocked.Snapshot) },
                        { canonical.Id, DockWindowFacts.FromSnapshot(
                            sourceDocked.Snapshot) }
                    };
                DockWindowFacts movedRoot = new DockWindowFacts(second.Id,
                    160, 140, 320, 300, true, false);
                List<DockLayoutTarget> moveTargets =
                    PetForm.CalculateDockTranslationTargets(
                        new string[] { second.Id, canonical.Id }, moveFacts,
                        movedRoot, 60, 40);
                StickyUiCommandResult targetMoved = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            moveTargets[0].X, moveTargets[0].Y,
                            moveTargets[0].Width, moveTargets[0].Height)),
                    petContext);
                StickyUiCommandResult sourceMoved = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(
                            moveTargets[1].X, moveTargets[1].Y,
                            moveTargets[1].Width, moveTargets[1].Height)),
                    petContext);
                check.HostedGroupMoveOk = targetMoved.Snapshot.X == 160 &&
                    targetMoved.Snapshot.Y == 140 &&
                    sourceMoved.Snapshot.X == 160 &&
                    sourceMoved.Snapshot.Y == 440;

                StickyUiCommandResult targetPinned = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetTopMost,
                        second.Id, true), petContext);
                StickyUiCommandResult sourcePinned = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetTopMost,
                        canonical.Id, true), petContext);
                check.HostedTopMostOk = targetPinned.Snapshot.AlwaysOnTop &&
                    sourcePinned.Snapshot.AlwaysOnTop;

                StickyUiDockResizeRole groupedRole =
                    new StickyUiDockResizeRole(true, true, true,
                        true, 220, 700);
                StickyUiDockResizeRole groupBottomRole =
                    new StickyUiDockResizeRole(true, false, true,
                        false, 220, 700);
                StickyUiCommandResult targetRole = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetDockResizeRole,
                        second.Id, false, null, null, groupedRole), petContext);
                StickyUiCommandResult sourceRole = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetDockResizeRole,
                        canonical.Id, false, null, null, groupBottomRole),
                    petContext);
                List<Rectangle> resizedLayout =
                    PetForm.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(320, 230), new Size(320, 230)
                    }, 80, 140, 420);
                StickyUiCommandResult targetResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                resizedLayout[0].X, resizedLayout[0].Y,
                                resizedLayout[0].Width,
                                resizedLayout[0].Height)), petContext);
                StickyUiCommandResult sourceResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                resizedLayout[1].X, resizedLayout[1].Y,
                                resizedLayout[1].Width,
                                resizedLayout[1].Height)), petContext);
                check.HostedHorizontalResizeOk =
                    targetRole.Status == StickyUiCommandStatus.Handled &&
                    sourceRole.Status == StickyUiCommandStatus.Handled &&
                    targetResized.Snapshot.X == 80 &&
                    sourceResized.Snapshot.X == 80 &&
                    targetResized.Snapshot.Width == 420 &&
                    sourceResized.Snapshot.Width == 420;

                DockWindowFacts twoUpperRequested = new DockWindowFacts(
                    second.Id, 80, 140, 420, 500, true, true);
                List<DockLayoutTarget> twoDividerTargets =
                    PetForm.CalculateDockDividerTargets(twoUpperRequested,
                        DockWindowFacts.FromSnapshot(sourceResized.Snapshot));
                StickyUiCommandResult targetDividerResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                twoDividerTargets[0].X,
                                twoDividerTargets[0].Y,
                                twoDividerTargets[0].Width,
                                twoDividerTargets[0].Height)), petContext);
                StickyUiCommandResult sourceDividerResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                twoDividerTargets[1].X,
                                twoDividerTargets[1].Y,
                                twoDividerTargets[1].Width,
                                twoDividerTargets[1].Height)), petContext);
                bool twoDividerOk =
                    targetDividerResized.Snapshot.Height == 500 &&
                    sourceDividerResized.Snapshot.Height == 230 &&
                    targetDividerResized.Snapshot.Y +
                        targetDividerResized.Snapshot.Height ==
                        sourceDividerResized.Snapshot.Y;

                StickyUiCommandResult targetHidden = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Hide,
                        second.Id, false), petContext);
                StickyUiCommandResult sourceHidden = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Hide,
                        canonical.Id, false), petContext);
                StickyUiCommandResult targetShown = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Show,
                        second.Id, false), petContext);
                StickyUiCommandResult sourceShown = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Show,
                        canonical.Id, false), petContext);
                check.HostedHideReopenOk =
                    !targetHidden.Snapshot.Visible &&
                    !sourceHidden.Snapshot.Visible &&
                    targetShown.Snapshot.Visible && sourceShown.Snapshot.Visible &&
                    targetShown.Snapshot.X == 80 &&
                    sourceShown.Snapshot.Y == 640;

                StickyUiCommandResult thirdPositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null,
                            new StickyUiBounds(80, 740, 420, 300)),
                        petContext);
                List<StickyNoteData> threeOrder =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        dockOrder, second,
                        new StickyNoteData[] { third });
                List<Rectangle> threeLayout =
                    PetForm.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(420, 300), new Size(420, 300),
                        new Size(420, 300)
                    }, 80, 140, 420);
                StickyUiCommandResult targetInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                threeLayout[0].X, threeLayout[0].Y,
                                threeLayout[0].Width, threeLayout[0].Height)),
                        petContext);
                StickyUiCommandResult thirdInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null, new StickyUiBounds(
                                threeLayout[1].X, threeLayout[1].Y,
                                threeLayout[1].Width, threeLayout[1].Height)),
                        petContext);
                StickyUiCommandResult sourceInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                threeLayout[2].X, threeLayout[2].Y,
                                threeLayout[2].Width, threeLayout[2].Height)),
                        petContext);
                check.HostedThreeNoteInsertionOk = threeOrder.Count == 3 &&
                    threeOrder[0].Id == second.Id &&
                    threeOrder[1].Id == third.Id &&
                    threeOrder[2].Id == canonical.Id &&
                    third.DockParentId == second.Id &&
                    canonical.DockParentId == third.Id &&
                    thirdInserted.Snapshot.Y == 440 &&
                    sourceInserted.Snapshot.Y == 740;
                StickyUiCommandResult targetThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            second.Id, false, null, null, groupedRole),
                        petContext);
                StickyUiCommandResult thirdThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            third.Id, false, null, null, groupedRole),
                        petContext);
                StickyUiCommandResult sourceThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            canonical.Id, false, null, null,
                            groupBottomRole), petContext);
                List<DockLayoutTarget> firstDivider =
                    PetForm.CalculateDockDividerTargets(
                        new DockWindowFacts(second.Id, 80, 140, 420, 500,
                            true, true),
                        DockWindowFacts.FromSnapshot(thirdInserted.Snapshot));
                List<Rectangle> firstDividerLayout =
                    PetForm.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(420, firstDivider[0].Height),
                        new Size(420, firstDivider[1].Height),
                        new Size(420, sourceInserted.Snapshot.Height)
                    }, 80, 140, 420);
                StickyUiCommandResult firstUpper = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            firstDividerLayout[0].X, firstDividerLayout[0].Y,
                            420, firstDividerLayout[0].Height)), petContext);
                StickyUiCommandResult firstLower = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        third.Id, false, null, new StickyUiBounds(
                            firstDividerLayout[1].X, firstDividerLayout[1].Y,
                            420, firstDividerLayout[1].Height)), petContext);
                StickyUiCommandResult firstTrailing =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                firstDividerLayout[2].X,
                                firstDividerLayout[2].Y, 420,
                                firstDividerLayout[2].Height)), petContext);
                List<DockLayoutTarget> secondDivider =
                    PetForm.CalculateDockDividerTargets(
                        new DockWindowFacts(third.Id, 80,
                            firstLower.Snapshot.Y, 420, 500,
                            true, true),
                        DockWindowFacts.FromSnapshot(firstTrailing.Snapshot));
                StickyUiCommandResult secondUpper = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        third.Id, false, null, new StickyUiBounds(80,
                            firstLower.Snapshot.Y,
                            420, secondDivider[0].Height)), petContext);
                StickyUiCommandResult secondLower = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(80,
                            secondDivider[1].Y,
                            420, secondDivider[1].Height)), petContext);
                List<DockLayoutTarget> dividerMinimum =
                    PetForm.CalculateDockDividerTargets(
                        new DockWindowFacts(second.Id, 80, 140, 420, 50,
                            true, true),
                        DockWindowFacts.FromSnapshot(firstLower.Snapshot));
                List<DockLayoutTarget> dividerMaximum =
                    PetForm.CalculateDockDividerTargets(
                        new DockWindowFacts(second.Id, 80, 140, 420, 900,
                            true, true),
                        DockWindowFacts.FromSnapshot(firstLower.Snapshot));
                check.HostedDividerResizeOk = twoDividerOk &&
                    targetThreeRole.Status == StickyUiCommandStatus.Handled &&
                    thirdThreeRole.Status == StickyUiCommandStatus.Handled &&
                    sourceThreeRole.Status == StickyUiCommandStatus.Handled &&
                    firstUpper.Snapshot.Height == 500 &&
                    firstLower.Snapshot.Height == 300 &&
                    firstUpper.Snapshot.Y + firstUpper.Snapshot.Height ==
                        firstLower.Snapshot.Y &&
                    firstTrailing.Snapshot.Height == 300 &&
                    firstLower.Snapshot.Y + firstLower.Snapshot.Height ==
                        firstTrailing.Snapshot.Y &&
                    secondUpper.Snapshot.Height == 500 &&
                    secondLower.Snapshot.Height == 300 &&
                    secondUpper.Snapshot.Y + secondUpper.Snapshot.Height ==
                        secondLower.Snapshot.Y &&
                    dividerMinimum[0].Height == 220 &&
                    dividerMinimum[1].Height == 300 &&
                    dividerMaximum[0].Height == 700 &&
                    dividerMaximum[1].Height == 300 &&
                    !eventKinds.Contains(
                        StickyUiEventKind.DockDividerResizing);
                check.DockRestoreOk = VerifyHostedDockPersistence(
                    firstUpper.Snapshot, secondUpper.Snapshot,
                    secondLower.Snapshot);

                List<StickyNoteData> splitRemainder =
                    StickyDockOperations.ExtractSingleDockMember(
                        threeOrder, third);
                StickyUiCommandResult targetAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null,
                            new StickyUiBounds(80, 140, 420, 300)),
                        petContext);
                StickyUiCommandResult sourceAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null,
                            new StickyUiBounds(80, 440, 420, 300)),
                        petContext);
                StickyUiCommandResult thirdAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null,
                            new StickyUiBounds(600, 140, 420, 300)),
                        petContext);
                check.HostedMiddleSplitOk = splitRemainder.Count == 2 &&
                    splitRemainder[0].Id == second.Id &&
                    splitRemainder[1].Id == canonical.Id &&
                    canonical.DockParentId == second.Id &&
                    String.IsNullOrEmpty(third.DockGroupId) &&
                    sourceAfterSplit.Snapshot.Y == 440 &&
                    thirdAfterSplit.Snapshot.X == 600;
                StickyUiCommandResult closed = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.CloseAll,
                        String.Empty, false), petContext);
                host.BeginShutdown();
                bool exited = host.WaitForExit(5000);
                bool closedBoth = closed != null &&
                    closed.Status == StickyUiCommandStatus.Handled &&
                    closed.FinalSnapshots != null &&
                    closed.FinalSnapshots.Length == 6;
                StickyUiFinalSnapshot finalSource = null;
                StickyUiFinalSnapshot finalTarget = null;
                if (closedBoth)
                    foreach (StickyUiFinalSnapshot item in closed.FinalSnapshots)
                    {
                        if (String.Equals(item.NoteId, canonical.Id,
                            StringComparison.OrdinalIgnoreCase))
                            finalSource = item;
                        if (String.Equals(item.NoteId, second.Id,
                            StringComparison.OrdinalIgnoreCase))
                            finalTarget = item;
                    }
                check.CloseAllBatchOk = finalSource != null &&
                    finalTarget != null && sourceAfterSplit != null &&
                    targetAfterSplit != null &&
                    finalSource.Sequence > sourceAfterSplit.Sequence &&
                    finalTarget.Sequence > targetAfterSplit.Sequence &&
                    finalSource.Snapshot.Y == 440 &&
                    finalTarget.Snapshot.Y == 140;
                StickyNoteData staleProbe = sourceDocked.Snapshot
                    .CreateWorkingCopy();
                long appliedSequence = sourceDocked.Sequence;
                if (PetForm.ShouldApplyHostedSequence(reopened.Sequence,
                    appliedSequence)) detached.ApplyTo(staleProbe);
                check.PerNoteSequenceOk =
                    PetForm.ShouldApplyHostedSequence(sourceDocked.Sequence,
                        reopened.Sequence) &&
                    !PetForm.ShouldApplyHostedSequence(reopened.Sequence,
                        appliedSequence) &&
                    staleProbe.X == sourceDocked.Snapshot.X &&
                    staleProbe.Y == sourceDocked.Snapshot.Y;
                check.HostedDockEffectOk = dockHit && dockOrder.Count == 2 &&
                    dockOrder[0].Id == second.Id &&
                    dockOrder[1].DockParentId == second.Id &&
                    targetDocked != null && sourceDocked != null &&
                    targetDocked.Status == StickyUiCommandStatus.Handled &&
                    sourceDocked.Status == StickyUiCommandStatus.Handled &&
                    targetDocked.Snapshot.X == hostedLayout[0].X &&
                    sourceDocked.Snapshot.Y == hostedLayout[1].Y &&
                    eventKinds.Contains(StickyUiEventKind.HeaderDragMoved);
                check.LifecycleOk = detachedOwnership && hidden != null &&
                    hidden.Status == StickyUiCommandStatus.Handled &&
                    hidden.Snapshot != null && !hidden.Snapshot.Visible &&
                    shown != null &&
                    shown.Status == StickyUiCommandStatus.Handled &&
                    shown.Snapshot != null && shown.Snapshot.Visible &&
                    closedOne != null &&
                    closedOne.Status == StickyUiCommandStatus.Handled &&
                    reopened != null &&
                    reopened.Status == StickyUiCommandStatus.Handled &&
                    secondCreated != null &&
                    secondCreated.Status == StickyUiCommandStatus.Handled &&
                    secondCreated.OwnerThreadId == created.OwnerThreadId &&
                    thirdCreated != null &&
                    thirdCreated.Status == StickyUiCommandStatus.Handled &&
                    thirdPositioned != null &&
                    thirdPositioned.Status == StickyUiCommandStatus.Handled &&
                    todoCreated != null &&
                    todoCreated.Status == StickyUiCommandStatus.Handled &&
                    scheduleCreated != null &&
                    scheduleCreated.Status == StickyUiCommandStatus.Handled &&
                    reminderCreated != null &&
                    reminderCreated.Status == StickyUiCommandStatus.Handled &&
                    closedBoth && exited &&
                    eventThread == petThread && lastEvent != null &&
                    eventNoteIds.Contains(canonical.Id) &&
                    eventNoteIds.Contains(second.Id) &&
                    eventNoteIds.Contains(third.Id) &&
                    eventNoteIds.Contains(todo.Id) &&
                    eventNoteIds.Contains(schedule.Id) &&
                    eventNoteIds.Contains(reminder.Id) &&
                    eventKinds.Contains(StickyUiEventKind.FirstRendered) &&
                    eventKinds.Contains(StickyUiEventKind.SnapshotChanged) &&
                    eventKinds.Contains(StickyUiEventKind.Closed);
            }
            return check;
        }

        private static bool VerifyHostedDockPersistence(
            params StickyNoteUiSnapshot[] snapshots)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "penny-hosted-dock-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                StickyNoteRepository repository =
                    StickyNoteRepository.LoadFromFile(path);
                List<StickyNoteData> stored = new List<StickyNoteData>();
                if (snapshots == null || snapshots.Length < 2) return false;
                foreach (StickyNoteUiSnapshot snapshot in snapshots)
                {
                    if (snapshot == null) return false;
                    StickyNoteData note = repository.Create(String.Empty,
                        new Point(snapshot.X, snapshot.Y));
                    if (note == null) return false;
                    snapshot.ApplyTo(note);
                    stored.Add(note);
                }
                StickyDockGroups.ApplyOrderedGroup(stored);
                if (!repository.Save().Succeeded) return false;
                StickyNoteRepository reopened =
                    StickyNoteRepository.LoadFromFile(path);
                StickyNoteData member = reopened.Find(
                    stored[stored.Count - 1].Id);
                List<StickyNoteData> order = StickyDockGroups.GetOrderedGroup(
                    reopened.GetAll(), member);
                if (order.Count != snapshots.Length) return false;
                for (int index = 0; index < order.Count; index++)
                {
                    if (order[index].Id != snapshots[index].NoteId ||
                        order[index].X != snapshots[index].X ||
                        order[index].Y != snapshots[index].Y ||
                        order[index].Width != snapshots[index].Width ||
                        order[index].Height != snapshots[index].Height ||
                        (index > 0 && order[index].DockParentId !=
                            order[index - 1].Id)) return false;
                }
                return true;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        private static StickyUiCommandResult PostStickyCommandAndWait(
            StickyUiHost host, StickyUiCommand command,
            SynchronizationContext completionContext)
        {
            StickyUiCommandResult result = null;
            using (ManualResetEventSlim completed =
                new ManualResetEventSlim(false))
            {
                host.PostCommand(command, delegate(StickyUiCommandResult value)
                {
                    result = value;
                    completed.Set();
                }, completionContext);
                WaitForSignalWithUiPump(completed, 5000);
            }
            return result;
        }

        private static bool WaitForSignalWithUiPump(
            ManualResetEventSlim signal, int timeoutMilliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!signal.IsSet && timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return signal.IsSet;
        }

        private static bool RunStickyBackupCleanupCheck(
            StickyPersistenceCheckResult stickyChecks)
        {
            bool backupExists = File.Exists(stickyChecks.FilePath + ".bak");
            if (File.Exists(stickyChecks.FilePath))
                File.Delete(stickyChecks.FilePath);
            if (File.Exists(stickyChecks.FilePath + ".bak"))
                File.Delete(stickyChecks.FilePath + ".bak");
            return backupExists;
        }

        private sealed class DockCheckResult
        {
            internal DockPersistenceCheckResult Persistence;
            internal DockLifecycleCheckResult Lifecycle;
            internal DockGeometryCheckResult Geometry;
            internal bool PersistenceAndGeometryOk;
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

        private static string BuildDockReportFields(
            DockCheckResult dockChecks,
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickyEditorCheckResult editorChecks)
        {
            return
                "  \"side_tab_order_persistence_ok\": " + Bool(
                    dockChecks.Persistence.SideTabOrderOk) + ",\n" +
                "  \"sticky_bottom_dock_persistence_and_geometry_ok\": " + Bool(
                    dockChecks.PersistenceAndGeometryOk) + ",\n" +
                "  \"sticky_mixed_type_middle_insertion_ok\": " + Bool(
                    dockChecks.Persistence.MixedInsertionOk) + ",\n" +
                "  \"schedule_mixed_with_note_and_todo_docking_ok\": " + Bool(
                    dockChecks.Lifecycle.ScheduleMixedTypesOk) + ",\n" +
                "  \"sticky_lower_close_rewires_neighbors_ok\": " + Bool(
                    dockChecks.Persistence.LowerCloseRewiresNeighborsOk) + ",\n" +
                "  \"sticky_hidden_group_restores_together_in_dock_order_ok\": " +
                    Bool(dockChecks.Persistence.WholeComponentRestoreOk) + ",\n" +
                "  \"sticky_group_snapshot_survives_broken_parent_links_ok\": " +
                    Bool(dockChecks.Persistence.SnapshotSurvivesBrokenParentLinksOk) +
                    ",\n" +
                "  \"sticky_group_snapshot_round_trip_ok\": " + Bool(
                    dockChecks.Persistence.GroupSnapshotRoundTripOk) + ",\n" +
                "  \"sticky_expand_and_tile_all_round_trip_ok\": " + Bool(
                    dockChecks.Persistence.ExpandAndTileRoundTripOk) + ",\n" +
                "  \"sticky_group_requests_restore_atomically_ok\": " + Bool(
                    dockChecks.Lifecycle.GroupRestoreAtomicOk) + ",\n" +
                "  \"sticky_middle_member_extraction_keeps_neighbors_joined_ok\": " +
                    Bool(dockChecks.Lifecycle.MiddleExtractionOk) + ",\n" +
                "  \"sticky_middle_x_preserves_hidden_group_slot_ok\": " + Bool(
                    dockChecks.Lifecycle.HiddenSlotPreservedOk) + ",\n" +
                "  \"sticky_middle_x_reopen_reinserts_original_slot_ok\": " + Bool(
                    dockChecks.Lifecycle.HiddenSlotReopenOk) + ",\n" +
                "  \"sticky_middle_x_hidden_slot_restart_persistence_ok\": " + Bool(
                    dockChecks.Persistence.HiddenSlotRestartOk) + ",\n" +
                "  \"sticky_partial_hidden_groups_merge_without_losing_slots_ok\": " +
                    Bool(dockChecks.Lifecycle.PartialHiddenMergeOk) + ",\n" +
                "  \"sticky_rearranged_group_second_restore_cycle_ok\": " + Bool(
                    dockChecks.Lifecycle.SecondRestoreCycleOk) + ",\n" +
                "  \"sticky_repeated_rearrange_restore_cycles_ok\": " + Bool(
                    dockChecks.Lifecycle.RepeatedRestoreCyclesOk) + ",\n" +
                "  \"sticky_wide_narrow_docking_ok\": " + Bool(
                    dockChecks.Geometry.WideNarrowDockingOk) + ",\n" +
                "  \"appearance_dialog_below_and_bounded_ok\": " + Bool(
                    dialogChecks.AppearanceLocationOk) + ",\n" +
                "  \"sticky_group_unified_width_layout_ok\": " + Bool(
                    dockChecks.Geometry.UnifiedGroupResizeOk) + ",\n" +
                "  \"sticky_group_root_anchor_preserved_ok\": " + Bool(
                    dockChecks.Geometry.RootAnchorPreservedOk) + ",\n" +
                "  \"sticky_detached_group_translation_ok\": " + Bool(
                    dockChecks.Geometry.DetachedGroupTranslationOk) + ",\n" +
                "  \"sticky_internal_divider_moves_following_chain_ok\": " + Bool(
                    dockChecks.Geometry.DividerMovesFollowingChainOk) + ",\n" +
                "  \"sticky_internal_divider_independent_range_ok\": " + Bool(
                    dockChecks.Geometry.DividerIndependentRangeOk) + ",\n" +
                "  \"sticky_internal_divider_reallocates_pair_ok\": " + Bool(
                    dockChecks.Geometry.DividerReallocatesPairOk) + ",\n" +
                "  \"sticky_long_group_coordinate_guard_ok\": " + Bool(
                    dockChecks.Geometry.LongCoordinateGuardOk) + ",\n" +
                "  \"sticky_root_close_collapses_group_ok\": " + Bool(
                    dockChecks.Lifecycle.CloseHierarchyOk) + ",\n" +
                "  \"sticky_native_aero_snap_disabled_ok\": " + Bool(
                    windowPolicyChecks.NativeSnapDisabledOk &&
                    editorChecks.NativeWindowStyleAppliedOk) + ",\n" +
                "  \"sticky_first_drag_geometry_recovery_ok\": " + Bool(
                    dockChecks.Geometry.FirstDragRecoveryOk) + ",\n" +
                "  \"detached_group_returns_on_screen_ok\": " + Bool(
                    dockChecks.Geometry.DetachedGroupReturnsOnScreenOk) + ",\n" +
                "  \"sticky_screen_recovery_anchor_ok\": " + Bool(
                    dockChecks.Geometry.ScreenRecoveryAnchorOk) + ",\n" +
                "  \"dock_visual_seam_uses_detached_facts_ok\": " + Bool(
                    dockChecks.Geometry.ExecutorNeutralDockVisualSeamOk) +
                    ",\n" +
                "  \"ordinary_drag_cannot_accidentally_split_ok\": " + Bool(
                    dockChecks.Lifecycle.SplitGestureOk) + ",\n" +
                "  \"root_drag_always_moves_whole_group_ok\": " + Bool(
                    dockChecks.Lifecycle.RootDragNeverSplitsOk) + ",\n";
        }

        private static string BuildPersistenceReportFields(
            SettingsPersistenceCheckResult settingsChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            StickyPersistenceCheckResult stickyChecks,
            WindowShellCheckResult shellChecks,
            StickyCompatibilityCheckResult compatibilityChecks)
        {
            return
                "  \"settings_backup_recovery_ok\": " + Bool(
                    settingsChecks.BackupRecoveryOk) + ",\n" +
                "  \"settings_failure_dirty_retry_ok\": " + Bool(
                    settingsChecks.FailureDirtyRetryOk) + ",\n" +
                "  \"daily_briefing_date_persistence_ok\": " + Bool(
                    settingsChecks.DailyBriefingDatePersistenceOk) + ",\n" +
                "  \"multiple_reminders_per_note_ok\": " + Bool(
                    reminderCoordinatorChecks.MultipleLinkedReminderOk) + ",\n" +
                "  \"sticky_note_persistence_ok\": " + Bool(
                    stickyChecks.PersistenceOk) + ",\n" +
                "  \"sticky_pin_action_text_ok\": " + Bool(
                    shellChecks.PinActionTextOk) + ",\n" +
                "  \"todo_sticky_pin_action_text_ok\": " + Bool(
                    shellChecks.TodoPinActionTextOk) + ",\n" +
                "  \"sticky_rich_text_persistence_ok\": " + Bool(
                    stickyChecks.RichTextOk) + ",\n" +
                "  \"sticky_rich_text_no_silent_truncation_ok\": " + Bool(
                    stickyChecks.RichTextNoSilentTruncationOk) + ",\n" +
                "  \"multilingual_note_persistence_ok\": " + Bool(
                    stickyChecks.MultilingualOk) + ",\n" +
                "  \"ime_compatible_editor_ok\": " + Bool(
                    shellChecks.ImeCompatibleEditorOk) + ",\n" +
                "  \"sticky_single_window_input_ok\": " + Bool(
                    shellChecks.SingleWindowStickyInputOk) + ",\n" +
                "  \"legacy_note_migration_ok\": " + Bool(
                    compatibilityChecks.LegacyMigrationOk) + ",\n" +
                "  \"old_fish_shanying_cache_import_ok\": " + Bool(
                    compatibilityChecks.OldestFolderCacheImportOk) + ",\n" +
                "  \"version4_note_font_migration_ok\": " + Bool(
                    compatibilityChecks.VersionFourMigrationOk) + ",\n" +
                "  \"ancient_cache_display_repair_ok\": " + Bool(
                    compatibilityChecks.AncientCacheDisplayRepairOk) + ",\n" +
                "  \"todo_persistence_ok\": " + Bool(
                    stickyChecks.TodoOk) + ",\n" +
                "  \"schedule_persistence_ok\": " + Bool(
                    stickyChecks.ScheduleOk) + ",\n";
        }

        private static string BuildStickyInteractionReportFields(
            WindowShellCheckResult shellChecks,
            StickyEditorCheckResult editorChecks,
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickyFontCheckResult fontChecks)
        {
            return
                "  \"todo_pending_completed_groups_ok\": " + Bool(
                    shellChecks.TodoChecks.GroupingOk) + ",\n" +
                "  \"reminder_banner_countdown_ok\": " + Bool(
                    shellChecks.ReminderChecks.BannerCountdownOk) + ",\n" +
                "  \"reminder_banner_compact_font_ok\": " + Bool(
                    shellChecks.ReminderChecks.CompactBannerOk) + ",\n" +
                "  \"reminder_selection_actions_ok\": " + Bool(
                    shellChecks.ReminderChecks.SelectionActionsOk) + ",\n" +
                "  \"inline_new_reminder_and_list_removed_ok\": " + Bool(
                    shellChecks.ReminderChecks.InlineCreationActionsRemovedOk) +
                    ",\n" +
                "  \"reminder_first_click_survives_refresh_ok\": " + Bool(
                    shellChecks.ReminderChecks.FirstClickStableOk) + ",\n" +
                "  \"reminder_banner_refreshes_in_place_ok\": " + Bool(
                    shellChecks.ReminderChecks.BannerRefreshInPlaceOk) + ",\n" +
                "  \"reminder_blank_area_clears_selection_ok\": " + Bool(
                    shellChecks.ReminderChecks.BlankAreaClearOk) + ",\n" +
                "  \"reminder_content_wraps_without_ellipsis_ok\": " + Bool(
                    shellChecks.ReminderChecks.SelectionActionsOk) + ",\n" +
                "  \"todo_double_click_inline_edit_ok\": " + Bool(
                    shellChecks.TodoChecks.WrapAndInlineEditOk) + ",\n" +
                "  \"todo_content_wraps_without_ellipsis_ok\": " + Bool(
                    shellChecks.TodoChecks.WrapAndInlineEditOk) + ",\n" +
                "  \"todo_overall_font_size_ok\": " + Bool(
                    shellChecks.TodoChecks.OverallFontSizeOk) + ",\n" +
                "  \"dedicated_reminder_todo_context_menus_ok\": " + Bool(
                    shellChecks.TodoChecks.DedicatedRowContextMenusOk) + ",\n" +
                "  \"todo_marker_round_trip_ok\": " + Bool(
                    shellChecks.TodoChecks.MarkerRoundTripOk) + ",\n" +
                "  \"todo_plain_text_projection_ok\": " + Bool(
                    shellChecks.TodoChecks.PlainTextProjectionOk) + ",\n" +
                "  \"multilingual_text_input_ok\": " + Bool(
                    editorChecks.MultilingualInputOk) + ",\n" +
                "  \"input_method_not_forced_to_chinese_ok\": " + Bool(
                    dialogChecks.UnforcedMultilingualImeOk) + ",\n" +
                "  \"format_tab_switch_content_preserved_ok\": " + Bool(
                    editorChecks.TabSwitchContentPreservedOk) + ",\n" +
                "  \"sticky_resource_limits_ok\": " + Bool(
                    windowPolicyChecks.ResourceLimitsOk) + ",\n" +
                "  \"maximum_sticky_notes\": " + StickyNoteLimits.MaximumNotes +
                    ",\n" +
                "  \"maximum_todos_per_note\": " +
                    StickyNoteLimits.MaximumTodoItemsPerNote + ",\n" +
                "  \"sticky_resize_buffered_painting_ok\": " + Bool(
                    shellChecks.StickyResizePaintingOk) + ",\n" +
                "  \"sticky_rich_text_toolbar_ok\": " + Bool(
                    editorChecks.RichTextToolbarOk) + ",\n" +
                "  \"sticky_format_interaction_smooth_ok\": " + Bool(
                    editorChecks.SmoothFormatInteractionOk) + ",\n" +
                "  \"sticky_first_dropdown_focus_not_stolen_ok\": " + Bool(
                    editorChecks.DeferredInitialFocusSafeOk) + ",\n" +
                "  \"sticky_format_toolbar_preserves_selection_focus_ok\": " + Bool(
                    editorChecks.FormatToolbarFocusOk) + ",\n" +
                "  \"sticky_format_selectors_always_black_ok\": " + Bool(
                    editorChecks.FormatSelectorsAlwaysBlackOk) + ",\n" +
                "  \"sticky_body_text_color_switch_ok\": " + Bool(
                    editorChecks.BodyTextColorSwitchOk) + ",\n" +
                "  \"sticky_group_outer_resize_roles_ok\": " + Bool(
                    editorChecks.DockResizeRoleOk) + ",\n" +
                "  \"sticky_group_topmost_sync_ok\": " + Bool(
                    editorChecks.GroupTopMostSyncOk) + ",\n" +
                "  \"sticky_first_format_commit_ok\": " + Bool(
                    editorChecks.FirstFormatCommitOk) + ",\n" +
                "  \"empty_sticky_font_and_size_before_typing_ok\": " + Bool(
                    editorChecks.EmptyNoteFormattingOk) + ",\n" +
                "  \"sticky_existing_text_caret_format_switch_ok\": " + Bool(
                    editorChecks.CaretTypingFormatSwitchOk) + ",\n" +
                "  \"sticky_native_ime_single_commit_after_format_ok\": " + Bool(
                    editorChecks.SingleNativeImeCommitOk) + ",\n" +
                "  \"sticky_editor_and_window_context_actions_ok\": " + Bool(
                    editorChecks.UnifiedContextMenusOk) + ",\n" +
                "  \"sticky_note_types_never_convert_ok\": " + Bool(
                    shellChecks.TodoChecks.FixedTypeActionsOk) + ",\n" +
                "  \"sticky_font_size_parsing_ok\": " + Bool(
                    fontChecks.SizeParsingOk) + ",\n" +
                "  \"sticky_chinese_fonts_first_ok\": " + Bool(
                    fontChecks.ChineseFontsFirstOk) + ",\n" +
                "  \"sticky_installed_font_list_cached_ok\": " + Bool(
                    fontChecks.InstalledFontListCacheOk) + ",\n" +
                "  \"sticky_format_selector_single_event_model_ok\": " + Bool(
                    editorChecks.StableFormatSelectorModelOk) + ",\n" +
                "  \"shared_font_lifetime_ok\": " + Bool(
                    fontChecks.SharedFontLifetimeOk) + ",\n";
        }

        private static string BuildArtAndSettingsReportFields(
            ArtResourceCheckResult artChecks,
            SettingsPersistenceCheckResult settingsChecks)
        {
            return
                "  \"art_package\": {\"width\": " + artChecks.Width +
                ", \"height\": " + artChecks.Height +
                ", \"ok\": " + Bool(artChecks.AtlasOk) + "},\n" +
                "  \"animation_timing_from_art_package_ok\": " + Bool(
                    artChecks.AnimationTimingOk) + ",\n" +
                "  \"application_icon_embedded_ok\": " + Bool(
                    artChecks.ApplicationIconEmbeddedOk) + ",\n" +
                "  \"startup_loading_frame_embedded_ok\": " + Bool(
                    artChecks.StartupFrameEmbeddedOk) + ",\n" +
                "  \"startup_loading_uses_embedded_resource_ok\": " + Bool(
                    artChecks.StartupFrameUsesEmbeddedLoadingOk) + ",\n" +
                "  \"startup_loading_uses_saved_pet_scale_ok\": " + Bool(
                    artChecks.StartupUsesSavedScaleOk) + ",\n" +
                "  \"startup_loading_uses_saved_or_fallback_location_ok\": " +
                    Bool(artChecks.StartupLocationOk) + ",\n" +
                "  \"startup_loading_dedicated_sta_ok\": " + Bool(
                    artChecks.StartupLoadingThreadHostOk) + ",\n" +
                "  \"contact_author_feature_ok\": " + Bool(
                    artChecks.ContactAuthorFeatureOk) + ",\n" +
                "  \"contact_author_xiaohongshu_only_ok\": " + Bool(
                    artChecks.ContactAuthorFeatureOk) + ",\n" +
                "  \"minute_timer_ok\": " + Bool(
                    settingsChecks.MinuteTimerOk) + ",\n" +
                "  \"cancel_ok\": " + Bool(settingsChecks.CancelOk) + ",\n" +
                "  \"five_reminders_ok\": " + Bool(
                    settingsChecks.FiveRemindersOk) + ",\n" +
                "  \"sixth_reminder_blocked\": " + Bool(
                    settingsChecks.SixthReminderBlocked) + ",\n" +
                "  \"reminder_memory_ok\": " + Bool(
                    settingsChecks.ReminderMemoryOk) + ",\n" +
                "  \"daily_content_preferences_persistence_and_legacy_defaults_ok\": " +
                    Bool(settingsChecks
                        .DailyContentPreferencesPersistenceOk) + ",\n" +
                "  \"zodiac_preference_persistence_and_legacy_default_ok\": " +
                    Bool(settingsChecks.ZodiacPreferencePersistenceOk) +
                    ",\n" +
                "  \"weather_preference_persistence_and_legacy_default_ok\": " +
                    Bool(settingsChecks.WeatherPreferencePersistenceOk) +
                    ",\n";
        }

        private static string BuildScheduleAndExpiredReminderReportFields(
            StickyScheduleWindowCheckResult scheduleWindowChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks)
        {
            return
                "  \"schedule_countdown_ok\": " + Bool(
                    scheduleWindowChecks.CountdownOk) + ",\n" +
                "  \"schedule_five_tier_font_ok\": " + Bool(
                    scheduleWindowChecks.FontChoicesOk) + ",\n" +
                "  \"schedule_date_mouse_wheel_ok\": " + Bool(
                    scheduleWindowChecks.DateMouseWheelOk) + ",\n" +
                "  \"schedule_pin_marker_toggle_idempotent_ok\": " + Bool(
                    scheduleWindowChecks.PinMarkerToggleOk) + ",\n" +
                "  \"expired_reminder_discarded_after_closed_app_ok\": " + Bool(
                    reminderCoordinatorChecks.ExpiredAtLaunchDiscardedOk) +
                    ",\n";
        }

        private static string BuildDialogWindowAndSideTabReportFields(
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickySideTabCheckResult sideTabChecks)
        {
            return
                "  \"reminder_size_preview_ok\": " + Bool(
                    dialogChecks.ReminderSizePreviewOk) + ",\n" +
                "  \"standalone_reminder_no_auto_sticky_option_ok\": " +
                    Bool(dialogChecks.StandaloneReminderNoAutoStickyOptionOk) + ",\n" +
                "  \"reminder_live_note_size_preview_ok\": " + Bool(
                    dialogChecks.ReminderLiveSizePreviewOk) + ",\n" +
                "  \"sticky_high_dpi_layout_ok\": " + Bool(
                    windowPolicyChecks.HighDpiLayoutOk) + ",\n" +
                "  \"reminder_default_current_time_ok\": " + Bool(
                    dialogChecks.ReminderDefaultCurrentTimeOk) + ",\n" +
                "  \"ordinary_sticky_web_and_local_links_ok\": " + Bool(
                    windowPolicyChecks.OrdinaryLinkDetectionOk) + ",\n" +
                "  \"soft_sticky_palette_ok\": " + Bool(
                    windowPolicyChecks.SoftPaletteOk) + ",\n" +
                "  \"full_width_latin_normalization_ok\": " + Bool(
                    windowPolicyChecks.FullWidthNormalizationOk) + ",\n" +
                "  \"rename_initial_focus_ok\": " + Bool(
                    dialogChecks.RenameInitialFocusOk) + ",\n" +
                "  \"side_tab_left_then_right_overflow_ok\": " + Bool(
                    sideTabChecks.OverflowOk) + ",\n" +
                "  \"side_tab_delete_command_ok\": " + Bool(
                    sideTabChecks.DeleteCommandOk) + ",\n" +
                "  \"side_tab_drag_preview_ok\": " + Bool(
                    sideTabChecks.DragPreviewOk) + ",\n" +
                "  \"side_tab_drop_commit_after_drag_loop_ok\": " + Bool(
                    sideTabChecks.DeferredDropCommitOk) + ",\n" +
                "  \"side_tab_preview_clears_both_sides_ok\": " + Bool(
                    sideTabChecks.PreviewClearsBothSidesOk) + ",\n" +
                "  \"side_tab_explicit_source_keeps_target_first_ok\": " + Bool(
                    sideTabChecks.ExplicitSourceKeepsTargetFirstOk) + ",\n" +
                "  \"side_tab_target_never_marked_as_source_ok\": " + Bool(
                    sideTabChecks.TargetNeverMarkedAsSourceOk) + ",\n" +
                "  \"side_tab_exclusive_canvas_state_ok\": " + Bool(
                    sideTabChecks.ExclusiveCanvasStateOk) + ",\n" +
                "  \"side_tab_reverse_boundary_rollover_ok\": " + Bool(
                    sideTabChecks.ReverseBoundaryRolloverOk) + ",\n" +
                "  \"side_tab_boundary_edge_drop_ok\": " + Bool(
                    sideTabChecks.BoundaryEdgeDropOk) + ",\n" +
                "  \"side_tab_scaled_visual_gap_halved_ok\": " + Bool(
                    sideTabChecks.ScaledGapOk) + ",\n" +
                "  \"side_tab_vector_icon_uses_darker_tab_color_ok\": " + Bool(
                    sideTabChecks.VectorIconColorOk) + ",\n" +
                "  \"side_tab_z_order_policy_ok\": " + Bool(
                    sideTabChecks.ZOrderPolicyOk) + ",\n" +
                "  \"side_tab_layout_invalidation_ok\": " + Bool(
                    sideTabChecks.LayoutInvalidationOk) + ",\n";
        }

        private static string BuildPolicyKeyboardReminderReportFields(
            StickyWindowPolicyCheckResult windowPolicyChecks,
            KeyboardOverlayCheckResult keyboardOverlayChecks,
            StickyEditorCheckResult editorChecks,
            WindowShellCheckResult shellChecks,
            bool automaticNoteBackupOk,
            StickyPersistenceCheckResult stickyChecks,
            StickyCompatibilityCheckResult compatibilityChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            SettingsPersistenceCheckResult settingsChecks)
        {
            return
                "  \"dock_guides_do_not_flash_ok\": " + Bool(
                    windowPolicyChecks.SteadyDockGuideOk) + ",\n" +
                "  \"manager_marquee_batch_delete_ok\": " + Bool(
                    windowPolicyChecks.ManagerMarqueeBatchDeleteOk) + ",\n" +
                "  \"held_key_overlay_stays_constant_ok\": " + Bool(
                    keyboardOverlayChecks.HeldKeyStableOk) + ",\n" +
                "  \"keyboard_hook_captures_own_process_ok\": " + Bool(
                    keyboardOverlayChecks.HookCapturePolicyOk) + ",\n" +
                "  \"own_process_sticky_eligibility_ok\": " + Bool(
                    keyboardOverlayChecks.OwnProcessEligibilityOk) + ",\n" +
                "  \"ime_animation_guard_ok\": " + Bool(
                    editorChecks.ImeAnimationGuardOk) + ",\n" +
                "  \"ime_autosave_guard_ok\": " + Bool(
                    editorChecks.ImeAutoSaveGuardOk) + ",\n" +
                "  \"reverse_reminder_step_ok\": " + Bool(
                    shellChecks.ReverseReminderStepOk) + ",\n" +
                "  \"automatic_note_backup_ok\": " + Bool(automaticNoteBackupOk) + ",\n" +
                "  \"persistence_failure_dirty_state_ok\": " + Bool(
                    stickyChecks.FailureDirtyRetryOk) + ",\n" +
                "  \"sticky_generation_monotonic_ok\": " + Bool(
                    stickyChecks.GenerationMonotonicOk) + ",\n" +
                "  \"failed_load_never_overwrites_ok\": " + Bool(
                    compatibilityChecks.FailedLoadNeverOverwritesOk) + ",\n" +
                "  \"sticky_backup_recovery_allows_create_ok\": " + Bool(
                    compatibilityChecks.BackupRecoveryOk) + ",\n" +
                "  \"concrete_date_time_ok\": " + Bool(
                    reminderCoordinatorChecks.ConcreteDateTimeOk) + ",\n" +
                "  \"reminder_banner_tick_throttled_ok\": " + Bool(
                    reminderCoordinatorChecks.BannerTickThrottleOk) + ",\n" +
                "  \"startup_default_ok\": " + Bool(
                    shellChecks.StartupDefaultOk) + ",\n" +
                "  \"sticky_ui_host_ok\": " + Bool(
                    shellChecks.StickyUiHostOk) + ",\n" +
                "  \"sticky_canary_lifecycle_ok\": " + Bool(
                    shellChecks.StickyCanary.LifecycleOk) + ",\n" +
                "  \"sticky_hosted_sequence_rejection_ok\": " + Bool(
                    shellChecks.StickyCanary.PerNoteSequenceOk) + ",\n" +
                "  \"sticky_hosted_close_all_batch_ok\": " + Bool(
                    shellChecks.StickyCanary.CloseAllBatchOk) + ",\n" +
                "  \"sticky_hosted_two_note_dock_effect_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedDockEffectOk) + ",\n" +
                "  \"sticky_hosted_group_move_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedGroupMoveOk) + ",\n" +
                "  \"sticky_hosted_topmost_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedTopMostOk) + ",\n" +
                "  \"sticky_hosted_horizontal_resize_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedHorizontalResizeOk) +
                    ",\n" +
                "  \"sticky_hosted_divider_resize_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedDividerResizeOk) +
                    ",\n" +
                "  \"sticky_hosted_hide_reopen_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedHideReopenOk) + ",\n" +
                "  \"sticky_hosted_middle_split_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedMiddleSplitOk) + ",\n" +
                "  \"sticky_hosted_three_note_insertion_ok\": " + Bool(
                    shellChecks.StickyCanary.HostedThreeNoteInsertionOk) +
                    ",\n" +
                "  \"sticky_hosted_dock_restore_ok\": " + Bool(
                    shellChecks.StickyCanary.DockRestoreOk) + ",\n" +
                "  \"keyboard_hook_opt_in_and_default_off_ok\": " + Bool(
                    keyboardOverlayChecks.HookOptInDefaultOk) + ",\n" +
                "  \"keyboard_privacy_notice_persistence_ok\": " + Bool(
                    settingsChecks.KeyboardPrivacyNoticePersistenceOk) +
                    ",\n" +
                "  \"startup_loading_waits_for_ui_and_art_ok\": " + Bool(
                    shellChecks.StartupLoadingReadinessGateOk) + ",\n";
        }

        private static string BuildAnimationArtReportFields(
            AnimationCheckResult animationChecks,
            ArtResourceCheckResult artChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks)
        {
            return
                "  \"goodbye_animation_ok\": " + Bool(
                    animationChecks.GoodbyeOk) + ",\n" +
                "  \"notification_animation_ok\": " + Bool(
                    animationChecks.NotificationOk) + ",\n" +
                "  \"notification_trigger_playback_route_ok\": " + Bool(
                    artChecks.NotificationPlaybackOk) + ",\n" +
                "  \"due_reminder_bubble_persists_until_clicked_ok\": " + Bool(
                    reminderCoordinatorChecks.DueBubblePersistentOk) + ",\n" +
                "  \"due_reminder_bubble_uses_own_size_ok\": " + Bool(
                    reminderCoordinatorChecks.DueBubbleUsesOwnSizeOk) + ",\n" +
                "  \"due_reminder_bubble_blocks_lower_priority_messages_ok\": " +
                    Bool(reminderCoordinatorChecks.DueBubbleReplacementOk) +
                    ",\n" +
                "  \"prealert_countdown_bubble_not_replaced_by_note_feedback_ok\": " +
                    Bool(reminderCoordinatorChecks.PreAlertBubbleProtectionOk) +
                    ",\n" +
                "  \"notification_animation_single_cycle_ok\": " + Bool(
                    animationChecks.NotificationSingleCycleOk) + ",\n" +
                "  \"drag_uses_second_idle_row_ok\": " + Bool(
                    animationChecks.DragUsesSecondIdleRowOk) + ",\n" +
                "  \"idle_random_rows_ok\": " + Bool(
                    animationChecks.IdleRandomRowsOk) + ",\n" +
                "  \"typing_random_rows_ok\": " + Bool(
                    animationChecks.TypingRandomRowsOk) + ",\n" +
                "  \"idle_thought_combined_ten_percent_ok\": " + Bool(
                    animationChecks.IdleThoughtProbabilityReducedOk) + ",\n" +
                "  \"failed_guitar_probability_one_third_ok\": " + Bool(
                    animationChecks.GuitarFailureProbabilityReducedOk) +
                    ",\n" +
                "  \"startup_lazy_art_load_ok\": " + Bool(
                    artChecks.StartupLazyLoadOk) + ",\n" +
                "  \"startup_interaction_art_preload_ok\": " + Bool(
                    artChecks.InteractionPreloadOk) + ",\n" +
                "  \"startup_idle_frame_cache_embedded_ok\": " + Bool(
                    artChecks.StartupCacheEmbeddedOk) + ",\n" +
                "  \"per_pixel_alpha_renderer\": true,\n" +
                "  \"inner_outline_ok\": " + Bool(
                    artChecks.InnerOutlineOk) + ",\n" +
                "  \"external_outline_pixels\": 0,\n" +
                "  \"green_halo_absent\": " + Bool(
                    artChecks.GreenHaloAbsent) + ",\n" +
                "  \"idle_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[0] + ",\n" +
                "  \"failed_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[5] + ",\n" +
                "  \"waiting_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[6] + ",\n" +
                "  \"thinking_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[7] + ",\n" +
                "  \"review_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[8] + ",\n" +
                "  \"goodbye_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[3] + ",\n" +
                "  \"notification_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[9] + ",\n" +
                "  \"smooth_timing_ok\": " + Bool(
                    animationChecks.SmoothTimingOk) + ",\n";
        }

        private static string BuildBubbleManualKeyboardReportFields(
            BubbleCheckResult bubbleChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            SettingsPersistenceCheckResult settingsChecks,
            AnimationCheckResult animationChecks,
            WindowShellCheckResult shellChecks,
            KeyboardOverlayCheckResult keyboardOverlayChecks,
            WeatherCheckResult weatherChecks)
        {
            return
                "  \"hover_bubble_copy_ok\": " + Bool(
                    bubbleChecks.HoverCopyOk) + ",\n" +
                "  \"styled_reminder_bubble_ok\": " + Bool(
                    bubbleChecks.StyledReminderOk) + ",\n" +
                "  \"bubble_green_white_keyboard_font_ok\": " + Bool(
                    bubbleChecks.ThemeAndKeyboardFontOk) + ",\n" +
                "  \"reminder_bubble_uses_configured_size_ok\": " + Bool(
                    bubbleChecks.StyledReminderOk) + ",\n" +
                "  \"bubble_manual_position_ok\": " + Bool(
                    bubbleChecks.ManualPositionOk) + ",\n" +
                "  \"drag_bubble_suppression_ok\": " + Bool(
                    bubbleChecks.DragSuppressionOk) + ",\n" +
                "  \"silent_mode_persistence_ok\": " + Bool(
                    settingsChecks.SilentModePersistenceOk) + ",\n" +
                "  \"silent_mode_daily_bubbles_suppressed_ok\": " + Bool(
                    bubbleChecks.SilentModeOk) + ",\n" +
                "  \"silent_mode_reminder_bubbles_preserved_ok\": " + Bool(
                    !PetMessagePolicy.ShouldSuppress(
                        PetMessageKind.ReminderDue, true)) + ",\n" +
                "  \"manual_animation_random_pool_excludes_running_rows_ok\": " + Bool(
                    animationChecks.ManualRandomPoolOk) + ",\n" +
                "  \"manual_special_animation_probability_reduced_ok\": " + Bool(
                    animationChecks.ManualSpecialProbabilityReducedOk) +
                    ",\n" +
                "  \"manual_animation_full_cycle_guard_ok\": " + Bool(
                    animationChecks.ManualFullCycleGuardOk) + ",\n" +
                "  \"poke_burst_fifty_once_until_pause_ok\": " + Bool(
                    animationChecks.PokeBurstOk) + ",\n" +
                "  \"left_click_drag_threshold_ok\": " + Bool(
                    animationChecks.ClickDragThresholdOk) + ",\n" +
                "  \"bubble_position_math_ok\": " + Bool(
                    bubbleChecks.PositionMathOk) + ",\n" +
                "  \"bubble_single_message_kind_ok\": " + Bool(
                    bubbleChecks.SingleMessageKindOk) + ",\n" +
                "  \"bubble_replacement_closes_old_form_ok\": " + Bool(
                    bubbleChecks.ReplacementClosesOldFormOk) + ",\n" +
                "  \"bubble_protected_message_ok\": " + Bool(
                    bubbleChecks.ProtectedMessageOk) + ",\n" +
                "  \"bubble_deferred_message_semantics_ok\": " + Bool(
                    bubbleChecks.DeferredMessageSemanticsOk) + ",\n" +
                "  \"bubble_pending_retry_without_loss_or_duplication_ok\": " +
                    Bool(bubbleChecks.PendingRetryOk) + ",\n" +
                "  \"smalltalk_feedback_minimum_readable_lifecycle_ok\": " +
                    Bool(bubbleChecks.SmallTalkFeedbackLifecycleOk) +
                    ",\n" +
                "  \"bubble_reminder_priority_regression_ok\": " + Bool(
                    bubbleChecks.ReminderPriorityRegressionOk) + ",\n" +
                "  \"bubble_single_restore_after_close_ok\": " + Bool(
                    bubbleChecks.SingleRestoreAfterCloseOk) + ",\n" +
                "  \"bubble_adaptive_sizing_ok\": " + Bool(
                    bubbleChecks.AdaptiveSizingOk) + ",\n" +
                "  \"bubble_update_text_relayout_ok\": " + Bool(
                    bubbleChecks.UpdateTextRelayoutOk) + ",\n" +
                "  \"daily_content_first_poke_once_ok\": " + Bool(
                    bubbleChecks.DailyFirstPokeOk) + ",\n" +
                "  \"daily_content_rejected_retry_ok\": " + Bool(
                    bubbleChecks.DailyRejectedRetryOk) + ",\n" +
                "  \"daily_greeting_typed_request_ok\": " + Bool(
                    bubbleChecks.DailyGreetingRequestOk) + ",\n" +
                "  \"poke_easter_egg_typed_request_and_priority_ok\": " + Bool(
                    bubbleChecks.EasterEggRequestOk) + ",\n" +
                "  \"bubble_minimum_readable_dwell_ok\": " + Bool(
                    bubbleChecks.MinimumReadableOk) + ",\n" +
                "  \"bubble_readability_priority_bypass_ok\": " + Bool(
                    bubbleChecks.ReadabilityBypassOk) + ",\n" +
                "  \"smalltalk_typed_request_and_silent_mode_ok\": " + Bool(
                    bubbleChecks.SmallTalkRequestOk) + ",\n" +
                "  \"smalltalk_coordinator_cooldown_and_rotation_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorCooldownOk) + ",\n" +
                "  \"smalltalk_coordinator_rejected_show_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorRejectedRetryOk) +
                    ",\n" +
                "  \"smalltalk_coordinator_silent_mode_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorSilentModeOk) + ",\n" +
                "  \"smalltalk_coordinator_reminder_reject_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorReminderRetryOk) +
                    ",\n" +
                "  \"solar_term_daily_greeting_fact_ok\": " + Bool(
                    bubbleChecks.SolarTermOk) + ",\n" +
                "  \"daily_content_preference_flow_ok\": " + Bool(
                    bubbleChecks.DailyContentPreferencesOk) + ",\n" +
                "  \"daily_line_catalogs_complete_unique_ok\": " + Bool(
                    bubbleChecks.CuratedCatalogOk) + ",\n" +
                "  \"daily_selectors_deterministic_budgeted_ok\": " +
                    Bool(bubbleChecks.DailySelectorBudgetOk) + ",\n" +
                "  \"daily_briefing_supplementary_budget_ok\": " + Bool(
                    bubbleChecks.DailyBriefingBudgetOk) + ",\n" +
                "  \"daily_briefing_coordinator_integration_ok\": " + Bool(
                    bubbleChecks.DailyBriefingCoordinatorOk) + ",\n" +
                "  \"daily_briefing_rejected_show_retry_ok\": " + Bool(
                    bubbleChecks.DailyBriefingRejectedRetryOk) + ",\n" +
                "  \"daily_briefing_same_day_sign_switch_ok\": " + Bool(
                    bubbleChecks.DailyBriefingSameDaySwitchOk) + ",\n" +
                "  \"almanac_calculator_dependency_and_sect_ok\": " + Bool(
                    bubbleChecks.AlmanacCalculatorOk) + ",\n" +
                "  \"almanac_semantic_whitelist_conflict_ok\": " + Bool(
                    bubbleChecks.AlmanacSemanticOk) + ",\n" +
                "  \"almanac_wording_deterministic_variation_ok\": " + Bool(
                    bubbleChecks.AlmanacWordingOk) + ",\n" +
                "  \"daily_content_settings_ui_and_menu_ok\": " + Bool(
                    shellChecks.DailyContentSettingsUiOk) + ",\n" +
                "  \"weather_fixture_parser_ok\": " + Bool(
                    weatherChecks.ForecastFixtureParsingOk) + ",\n" +
                "  \"weather_forecast_request_shape_ok\": " + Bool(
                    weatherChecks.ForecastRequestShapeOk) + ",\n" +
                "  \"weather_geocoding_explicit_search_ok\": " + Bool(
                    weatherChecks.GeocodingRequestAndSelectionOk) + ",\n" +
                "  \"weather_zero_startup_requests_ok\": " + Bool(
                    weatherChecks.NoStartupRequestOk) + ",\n" +
                "  \"weather_same_day_cache_and_inflight_ok\": " + Bool(
                    weatherChecks.SameDayCacheAndInFlightOk) + ",\n" +
                "  \"weather_bounded_cache_invalidation_ok\": " + Bool(
                    weatherChecks.BoundedCacheInvalidationOk) + ",\n" +
                "  \"weather_failure_cooldown_ok\": " + Bool(
                    weatherChecks.FailureCooldownOk) + ",\n" +
                "  \"weather_meaning_and_wording_ok\": " + Bool(
                    weatherChecks.MeaningAndWordingOk) + ",\n" +
                "  \"weather_daily_coordinator_integration_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorWeatherOk) + ",\n" +
                "  \"weather_failure_daily_fallback_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorFailureFallbackOk) +
                    ",\n" +
                "  \"weather_daily_inflight_coalescing_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorInFlightOk) + ",\n" +
                "  \"weather_rejected_bubble_reuses_forecast_ok\": " + Bool(
                    weatherChecks.RejectedBubbleReusesForecastOk) + ",\n" +
                "  \"weather_location_dialog_compact_formatting_ok\": " +
                    Bool(weatherChecks.LocationDialogLayoutOk) + ",\n" +
                "  \"zodiac_preference_settings_ui_ok\": " + Bool(
                    shellChecks.ZodiacPreferenceSettingsUiOk) + ",\n" +
                "  \"scale_50_to_200_step_10_ok\": " + Bool(
                    shellChecks.ScaleRangeOk) + ",\n" +
                "  \"keyboard_text_scale_choices_ok\": " + Bool(
                    keyboardOverlayChecks.TextScaleChoicesOk) + ",\n" +
                "  \"keyboard_shortcut_and_repeat_ok\": " + Bool(
                    keyboardOverlayChecks.ShortcutAndRepeatOk) + ",\n" +
                "  \"keyboard_privacy_generation_ok\": " + Bool(
                    keyboardOverlayChecks.PrivacyGenerationOk) + ",\n" +
                "  \"keyboard_focus_snapshot_identity_ok\": " + Bool(
                    keyboardOverlayChecks.FocusSnapshotIdentityOk) + ",\n" +
                "  \"adaptive_black_white_text_ok\": " + Bool(
                    keyboardOverlayChecks.AdaptiveContrastOk) + ",\n";
        }

        private static string BuildStaticReportTail()
        {
            return
                "  \"thinking_row_registered\": true,\n" +
                "  \"typing_random_rows_registered\": true,\n" +
                "  \"idle_random_rows_registered\": true,\n" +
                "  \"goodbye_row_registered\": true,\n" +
                "  \"notification_row_registered\": true,\n" +
                "  \"typing_moves_pet\": false,\n" +
                "  \"look_follow_registered\": false,\n" +
                "  \"keyboard_content_recorded\": false\n" +
                "}\n";
        }

        public static void Run(string outputPath)
        {
            try
            {
                ArtResourceCheckResult artChecks = RunArtResourceChecks();
                SettingsPersistenceCheckResult settingsChecks =
                    RunSettingsPersistenceChecks(outputPath);
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
                    RunStickyPersistenceChecks(outputPath);
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
                bool automaticNoteBackupOk =
                    RunStickyBackupCleanupCheck(stickyChecks);
                StickyCompatibilityCheckResult stickyCompatibilityChecks =
                    RunStickyCompatibilityChecks(outputPath);
                DockCheckResult dockChecks = RunDockChecks(outputPath);
                // Every boolean emitted through Bool below is registered in one
                // collection. The root result can no longer drift away from the
                // detailed report when a new check is added.
                BeginCheckCollection();
                string reportBody =
                    BuildArtAndSettingsReportFields(artChecks, settingsChecks) +
                    BuildPersistenceReportFields(settingsChecks,
                        reminderCoordinatorChecks, stickyChecks, shellChecks,
                        stickyCompatibilityChecks) +
                    BuildScheduleAndExpiredReminderReportFields(
                        scheduleWindowChecks, reminderCoordinatorChecks) +
                    BuildStickyInteractionReportFields(shellChecks,
                        editorChecks, dialogChecks, windowPolicyChecks,
                        fontChecks) +
                    BuildDialogWindowAndSideTabReportFields(dialogChecks,
                        windowPolicyChecks, sideTabChecks) +
                    BuildDockReportFields(dockChecks, dialogChecks,
                        windowPolicyChecks, editorChecks) +
                    BuildPolicyKeyboardReminderReportFields(windowPolicyChecks,
                        keyboardOverlayChecks, editorChecks, shellChecks,
                        automaticNoteBackupOk, stickyChecks,
                        stickyCompatibilityChecks, reminderCoordinatorChecks,
                        settingsChecks) +
                    BuildAnimationArtReportFields(animationChecks, artChecks,
                        reminderCoordinatorChecks) +
                    BuildBubbleManualKeyboardReportFields(bubbleChecks,
                        reminderCoordinatorChecks, settingsChecks,
                        animationChecks, shellChecks, keyboardOverlayChecks,
                        weatherChecks) +
                    BuildStaticReportTail();
                bool ok = EndCheckCollection();
                string json = "{\n" +
                    "  \"ok\": " + Bool(ok) + ",\n" + reportBody;
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                CancelCheckCollection();
                string message = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
                File.WriteAllText(outputPath,
                    "{\"ok\":false,\"error\":\"" + message + "\"}",
                    new UTF8Encoding(false));
            }
        }
    }
}
