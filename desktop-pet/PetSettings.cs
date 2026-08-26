using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PennyPet
{
    internal sealed class PetSettings
    {
        private const long MaximumSettingsFileBytes = 1024L * 1024L;
        public bool HasLocation;
        public int X;
        public int Y;
        public bool StartupPreferenceInitialized;
        public bool StartWithWindows = true;
        public int ScalePercent = 100;
        public bool ShowKeyOverlay;
        public bool KeyboardPrivacyNoticeAccepted;
        public int KeyOverlayScalePercent = 100;
        public bool SilentMode;
        public readonly List<ReminderItem> Reminders = new List<ReminderItem>();
        private string _unreadablePrimaryPath;
        private string _unreadableBackupPath;

        private static string DirectoryPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "PennyPet");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.ini"); }
        }

        public void SetReminders(IEnumerable<ReminderItem> items)
        {
            Reminders.Clear();
            if (items == null) return;
            foreach (ReminderItem item in items)
            {
                if (item == null || Reminders.Count >= ReminderSchedule.MaximumItems) continue;
                Reminders.Add(new ReminderItem(item.DeadlineUtc, item.Text,
                    item.SourceNoteId, item.FontSizeTwips / 20F,
                    item.PreAlertEnabled));
            }
        }

        public static PetSettings Load()
        {
            return LoadFromFile(FilePath);
        }

        internal static PetSettings LoadFromFile(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new PetSettings();
            PetSettings settings;
            Exception primaryError;
            if (TryLoadSingleFile(filePath, out settings, out primaryError))
                return settings;
            ApplicationDiagnostics.ReportNonFatal("settings-load-primary",
                primaryError);

            string backupPath = filePath + ".bak";
            Exception backupError = null;
            if (File.Exists(backupPath) && TryLoadSingleFile(backupPath,
                out settings, out backupError))
            {
                // The recovered values are safe to use. Preserve the unreadable
                // primary before the next atomic save replaces it.
                settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
                return settings;
            }
            if (File.Exists(backupPath))
                ApplicationDiagnostics.ReportNonFatal("settings-load-backup",
                    backupError);

            settings = new PetSettings();
            settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
            if (File.Exists(backupPath))
                settings._unreadableBackupPath = Path.GetFullPath(backupPath);
            return settings;
        }

        private static bool TryLoadSingleFile(string filePath,
            out PetSettings settings, out Exception error)
        {
            settings = new PetSettings();
            error = null;
            try
            {
                if (new FileInfo(filePath).Length > MaximumSettingsFileBytes)
                    throw new InvalidDataException("Settings file is too large.");
                int recognizedLines = 0;
                long[] ticks = new long[ReminderSchedule.MaximumItems];
                string[] texts = new string[ReminderSchedule.MaximumItems];
                string[] sourceNoteIds = new string[ReminderSchedule.MaximumItems];
                int[] fontSizeTwips = new int[ReminderSchedule.MaximumItems];
                bool[] preAlerts = new bool[ReminderSchedule.MaximumItems];
                long legacyTicks = 0;
                string legacyText = String.Empty;
                foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    int intValue;
                    long longValue;
                    bool recognized = false;
                    if (key == "HasLocation")
                    {
                        settings.HasLocation = value == "1";
                        recognized = true;
                    }
                    else if (key == "X" && Int32.TryParse(value, out intValue))
                    {
                        settings.X = intValue;
                        recognized = true;
                    }
                    else if (key == "Y" && Int32.TryParse(value, out intValue))
                    {
                        settings.Y = intValue;
                        recognized = true;
                    }
                    else if (key == "StartupPreferenceInitialized")
                    {
                        settings.StartupPreferenceInitialized = value == "1";
                        recognized = true;
                    }
                    else if (key == "StartWithWindows")
                    {
                        settings.StartWithWindows = value != "0";
                        recognized = true;
                    }
                    else if (key == "ScalePercent" && Int32.TryParse(value, out intValue))
                    {
                        settings.ScalePercent = PetForm.NormalizeScalePercent(intValue);
                        recognized = true;
                    }
                    else if (key == "ShowKeyOverlay")
                    {
                        settings.ShowKeyOverlay = value != "0";
                        recognized = true;
                    }
                    else if (key == "KeyboardPrivacyNoticeAccepted")
                    {
                        settings.KeyboardPrivacyNoticeAccepted = value == "1";
                        recognized = true;
                    }
                    else if (key == "SilentMode")
                    {
                        settings.SilentMode = value == "1";
                        recognized = true;
                    }
                    else if (key == "KeyOverlayScalePercent" &&
                        Int32.TryParse(value, out intValue))
                    {
                        settings.KeyOverlayScalePercent =
                            KeyboardOverlayForm.NormalizeTextScalePercent(intValue);
                        recognized = true;
                    }
                    else if (key == "ReminderUtcTicks" && Int64.TryParse(value, out longValue))
                    {
                        legacyTicks = longValue;
                        recognized = true;
                    }
                    else if (key == "ReminderTextBase64")
                    {
                        legacyText = DecodeText(value);
                        recognized = true;
                    }
                    else
                    {
                        for (int i = 0; i < ReminderSchedule.MaximumItems; i++)
                        {
                            if (key == "Reminder" + i + "UtcTicks" &&
                                Int64.TryParse(value, out longValue))
                            {
                                ticks[i] = longValue;
                                recognized = true;
                            }
                            else if (key == "Reminder" + i + "TextBase64")
                            {
                                texts[i] = DecodeText(value);
                                recognized = true;
                            }
                            else if (key == "Reminder" + i + "SourceNoteIdBase64")
                            {
                                sourceNoteIds[i] = DecodeText(value);
                                recognized = true;
                            }
                            else if (key == "Reminder" + i + "FontSizeTwips" &&
                                Int32.TryParse(value, out intValue))
                            {
                                fontSizeTwips[i] = intValue;
                                recognized = true;
                            }
                            else if (key == "Reminder" + i + "PreAlert")
                            {
                                preAlerts[i] = value == "1";
                                recognized = true;
                            }
                        }
                    }
                    if (recognized) recognizedLines++;
                }

                if (recognizedLines == 0)
                    throw new InvalidDataException(
                        "Settings file contains no recognized values.");

                for (int i = 0; i < ReminderSchedule.MaximumItems; i++)
                {
                    AddLoadedReminder(settings, ticks[i], texts[i], sourceNoteIds[i],
                        fontSizeTwips[i], preAlerts[i]);
                }
                if (settings.Reminders.Count == 0)
                    AddLoadedReminder(settings, legacyTicks, legacyText, null,
                        0, false);
            }
            catch (Exception caught)
            {
                settings = null;
                error = caught;
                return false;
            }
            return true;
        }

        public void Save()
        {
            SaveToFile(FilePath);
        }

        internal void SaveToFile(string filePath)
        {
            try
            {
                if (!PreserveUnreadableSources(filePath)) return;
                List<string> lines = new List<string>();
                lines.Add("HasLocation=" + (HasLocation ? "1" : "0"));
                lines.Add("X=" + X);
                lines.Add("Y=" + Y);
                lines.Add("StartupPreferenceInitialized=" +
                    (StartupPreferenceInitialized ? "1" : "0"));
                lines.Add("StartWithWindows=" + (StartWithWindows ? "1" : "0"));
                lines.Add("ScalePercent=" + PetForm.NormalizeScalePercent(ScalePercent));
                lines.Add("ShowKeyOverlay=" + (ShowKeyOverlay ? "1" : "0"));
                lines.Add("KeyboardPrivacyNoticeAccepted=" +
                    (KeyboardPrivacyNoticeAccepted ? "1" : "0"));
                lines.Add("SilentMode=" + (SilentMode ? "1" : "0"));
                lines.Add("KeyOverlayScalePercent=" +
                    KeyboardOverlayForm.NormalizeTextScalePercent(KeyOverlayScalePercent));
                List<ReminderItem> items = new List<ReminderItem>(Reminders);
                items.Sort(delegate(ReminderItem left, ReminderItem right)
                {
                    return left.DeadlineUtc.CompareTo(right.DeadlineUtc);
                });
                for (int i = 0; i < items.Count && i < ReminderSchedule.MaximumItems; i++)
                {
                    lines.Add("Reminder" + i + "UtcTicks=" + items[i].DeadlineUtc.Ticks);
                    lines.Add("Reminder" + i + "TextBase64=" + EncodeText(items[i].Text));
                    if (!String.IsNullOrEmpty(items[i].SourceNoteId))
                        lines.Add("Reminder" + i + "SourceNoteIdBase64=" +
                            EncodeText(items[i].SourceNoteId));
                    lines.Add("Reminder" + i + "FontSizeTwips=" +
                        items[i].FontSizeTwips);
                    lines.Add("Reminder" + i + "PreAlert=" +
                        (items[i].PreAlertEnabled ? "1" : "0"));
                }
                AtomicTextFile.WriteAllLines(filePath, lines, true);
            }
            catch (Exception error)
            {
                // Losing preferences must never make the pet unusable.
                ApplicationDiagnostics.ReportNonFatal("settings-save", error);
            }
        }

        private bool PreserveUnreadableSources(string destinationPath)
        {
            string destination = Path.GetFullPath(destinationPath);
            bool protectsPrimary = !String.IsNullOrEmpty(_unreadablePrimaryPath) &&
                String.Equals(destination, _unreadablePrimaryPath,
                    StringComparison.OrdinalIgnoreCase);
            bool protectsBackup = !String.IsNullOrEmpty(_unreadableBackupPath) &&
                String.Equals(destination + ".bak", _unreadableBackupPath,
                    StringComparison.OrdinalIgnoreCase);
            if (!protectsPrimary && !protectsBackup) return true;
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                if (protectsPrimary && File.Exists(_unreadablePrimaryPath))
                    File.Copy(_unreadablePrimaryPath,
                        _unreadablePrimaryPath + ".corrupt-" + stamp, false);
                if (protectsBackup && File.Exists(_unreadableBackupPath))
                    File.Copy(_unreadableBackupPath,
                        _unreadableBackupPath + ".corrupt-" + stamp, false);
                _unreadablePrimaryPath = null;
                _unreadableBackupPath = null;
                return true;
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "settings-preserve-unreadable", error);
                return false;
            }
        }

        private static void AddLoadedReminder(PetSettings settings, long ticks,
            string text, string sourceNoteId, int fontSizeTwips,
            bool preAlertEnabled)
        {
            if (ticks <= 0 || String.IsNullOrEmpty(text) ||
                settings.Reminders.Count >= ReminderSchedule.MaximumItems) return;
            try
            {
                settings.Reminders.Add(new ReminderItem(
                    new DateTime(ticks, DateTimeKind.Utc), text, sourceNoteId,
                    fontSizeTwips > 0 ? fontSizeTwips / 20F : 10.5F,
                    preAlertEnabled));
            }
            catch
            {
                // Ignore only the malformed reminder entry.
            }
        }

        private static string EncodeText(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string DecodeText(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return String.Empty;
            }
        }
    }
}
