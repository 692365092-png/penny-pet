using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Platform-neutral settings state. Storage paths, backups and retry
    // notifications belong to the platform adapter, not this model.
    internal class PetSettingsData
    {
        public bool HasLocation;
        public int X;
        public int Y;
        public bool StartupPreferenceInitialized;
        public bool StartAtLogin;
        public int ScalePercent = 100;
        public bool ShowKeyOverlay;
        public bool KeyboardPrivacyNoticeAccepted;
        public int KeyOverlayScalePercent = 100;
        public bool SilentMode;
        public readonly List<ReminderItem> Reminders =
            new List<ReminderItem>();

        public void SetReminders(IEnumerable<ReminderItem> items)
        {
            if (items == null)
            {
                Reminders.Clear();
                return;
            }
            List<ReminderItem> source = new List<ReminderItem>(items);
            Reminders.Clear();
            foreach (ReminderItem item in source)
            {
                if (item == null ||
                    Reminders.Count >= ReminderSchedule.MaximumItems) continue;
                Reminders.Add(new ReminderItem(item.DeadlineUtc, item.Text,
                    item.SourceNoteId, item.FontSizeTwips / 20F,
                    item.PreAlertEnabled));
            }
        }

        internal void CopyFrom(PetSettingsData source)
        {
            if (source == null || Object.ReferenceEquals(source, this)) return;
            HasLocation = source.HasLocation;
            X = source.X;
            Y = source.Y;
            StartupPreferenceInitialized = source.StartupPreferenceInitialized;
            StartAtLogin = source.StartAtLogin;
            ScalePercent = source.ScalePercent;
            ShowKeyOverlay = source.ShowKeyOverlay;
            KeyboardPrivacyNoticeAccepted =
                source.KeyboardPrivacyNoticeAccepted;
            KeyOverlayScalePercent = source.KeyOverlayScalePercent;
            SilentMode = source.SilentMode;
            SetReminders(source.Reminders);
        }
    }
}
