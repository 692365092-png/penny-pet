using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

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
            retrySettings.WaitForPendingSaves();
            System.Windows.Forms.Application.DoEvents();
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
    }
}
