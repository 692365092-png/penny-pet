using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Pure compatibility codec for settings.ini. It deliberately knows
    // nothing about disk paths, backups, diagnostics or retry UI.
    internal static class PetSettingsCodec
    {
        internal static PetSettingsData Parse(IEnumerable<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            PetSettingsData settings = new PetSettingsData();
            int recognizedLines = 0;
            long[] ticks = new long[ReminderSchedule.MaximumItems];
            string[] texts = new string[ReminderSchedule.MaximumItems];
            string[] sourceNoteIds =
                new string[ReminderSchedule.MaximumItems];
            int[] fontSizeTwips = new int[ReminderSchedule.MaximumItems];
            bool[] preAlerts = new bool[ReminderSchedule.MaximumItems];
            long legacyTicks = 0;
            string legacyText = String.Empty;

            foreach (string line in lines)
            {
                if (line == null) continue;
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
                else if (key == "ScalePercent" &&
                    Int32.TryParse(value, out intValue))
                {
                    settings.ScalePercent =
                        PetSettingRules.NormalizePetScalePercent(intValue);
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
                    settings.KeyOverlayScalePercent = PetSettingRules
                        .NormalizeKeyboardTextScalePercent(intValue);
                    recognized = true;
                }
                else if (key == "ReminderUtcTicks" &&
                    Int64.TryParse(value, out longValue))
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
                        else if (key == "Reminder" + i +
                            "SourceNoteIdBase64")
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
                    "Settings data contains no recognized values.");
            for (int i = 0; i < ReminderSchedule.MaximumItems; i++)
                AddLoadedReminder(settings, ticks[i], texts[i],
                    sourceNoteIds[i], fontSizeTwips[i], preAlerts[i]);
            if (settings.Reminders.Count == 0)
                AddLoadedReminder(settings, legacyTicks, legacyText, null,
                    0, false);
            return settings;
        }

        internal static List<string> Serialize(PetSettingsData settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            List<string> lines = new List<string>();
            lines.Add("HasLocation=" + (settings.HasLocation ? "1" : "0"));
            lines.Add("X=" + settings.X);
            lines.Add("Y=" + settings.Y);
            lines.Add("StartupPreferenceInitialized=" +
                (settings.StartupPreferenceInitialized ? "1" : "0"));
            lines.Add("StartWithWindows=" +
                (settings.StartWithWindows ? "1" : "0"));
            lines.Add("ScalePercent=" + PetSettingRules
                .NormalizePetScalePercent(settings.ScalePercent));
            lines.Add("ShowKeyOverlay=" +
                (settings.ShowKeyOverlay ? "1" : "0"));
            lines.Add("KeyboardPrivacyNoticeAccepted=" +
                (settings.KeyboardPrivacyNoticeAccepted ? "1" : "0"));
            lines.Add("SilentMode=" + (settings.SilentMode ? "1" : "0"));
            lines.Add("KeyOverlayScalePercent=" + PetSettingRules
                .NormalizeKeyboardTextScalePercent(
                    settings.KeyOverlayScalePercent));

            List<ReminderItem> items =
                new List<ReminderItem>(settings.Reminders);
            items.Sort(delegate(ReminderItem left, ReminderItem right)
            {
                return left.DeadlineUtc.CompareTo(right.DeadlineUtc);
            });
            for (int i = 0; i < items.Count &&
                i < ReminderSchedule.MaximumItems; i++)
            {
                lines.Add("Reminder" + i + "UtcTicks=" +
                    items[i].DeadlineUtc.Ticks);
                lines.Add("Reminder" + i + "TextBase64=" +
                    EncodeText(items[i].Text));
                if (!String.IsNullOrEmpty(items[i].SourceNoteId))
                    lines.Add("Reminder" + i + "SourceNoteIdBase64=" +
                        EncodeText(items[i].SourceNoteId));
                lines.Add("Reminder" + i + "FontSizeTwips=" +
                    items[i].FontSizeTwips);
                lines.Add("Reminder" + i + "PreAlert=" +
                    (items[i].PreAlertEnabled ? "1" : "0"));
            }
            return lines;
        }

        private static void AddLoadedReminder(PetSettingsData settings,
            long ticks, string text, string sourceNoteId, int fontSizeTwips,
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
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? String.Empty));
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
