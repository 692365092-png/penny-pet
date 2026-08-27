using System;
using System.Collections.Generic;
using System.Windows.Input;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    // Sticky-note reminder banner UI only. Reminder scheduling and pet-side
    // coordination remain in their existing reminder modules.
    internal sealed partial class StickyNoteWindow
    {
        private void ReminderSelectionChanged(object sender,
            WC.SelectionChangedEventArgs e)
        {
            WC.ListBoxItem row = _reminderList.SelectedItem as WC.ListBoxItem;
            _selectedReminder = row == null ? null : row.Tag as ReminderItem;
            RefreshReminderState();
        }

        private void ReminderListPreviewMouseRightButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            WC.ListBoxItem row = FindVisualParent<WC.ListBoxItem>(
                e.OriginalSource as W.DependencyObject);
            if (row != null) _reminderList.SelectedItem = row;
        }

        private WC.ContextMenu BuildReminderItemMenu(ReminderItem reminder)
        {
            WC.ContextMenu menu = new WC.ContextMenu();
            AddMenuItem(menu, "编辑提醒", delegate
            {
                _selectedReminder = reminder;
                ExecuteSelectedReminderModify();
            });
            AddMenuItem(menu, "删除提醒", delegate
            {
                _selectedReminder = reminder;
                ExecuteSelectedReminderDelete();
            });
            return menu;
        }

        public void UpdateReminderBanner(IEnumerable<ReminderItem> reminders)
        {
            ReminderItem previous = _selectedReminder;
            List<ReminderItem> items = new List<ReminderItem>();
            if (reminders != null)
            {
                foreach (ReminderItem reminder in reminders)
                {
                    if (reminder == null || items.Count >= 5) continue;
                    items.Add(reminder);
                }
            }
            bool rebuild = _reminderList.Items.Count != items.Count;
            if (!rebuild)
            {
                for (int index = 0; index < items.Count; index++)
                {
                    WC.ListBoxItem existing = _reminderList.Items[index] as
                        WC.ListBoxItem;
                    if (existing == null || !Object.ReferenceEquals(
                        existing.Tag, items[index]))
                    {
                        rebuild = true;
                        break;
                    }
                }
            }
            if (rebuild)
            {
                _reminderBannerRebuildCount++;
                _reminderList.Items.Clear();
                WC.ListBoxItem selectedRow = null;
                foreach (ReminderItem reminder in items)
                {
                    WC.ListBoxItem row = new WC.ListBoxItem();
                    row.Tag = reminder;
                    row.Padding = new W.Thickness(4, 3, 4, 3);
                    row.Background = System.Windows.Media.Brushes.Transparent;
                    row.HorizontalContentAlignment =
                        W.HorizontalAlignment.Stretch;
                    row.ContextMenu = BuildReminderItemMenu(reminder);
                    UpdateReminderRow(row, reminder);
                    _reminderList.Items.Add(row);
                    if (Object.ReferenceEquals(reminder, previous))
                        selectedRow = row;
                }
                _reminderList.SelectedItem = selectedRow;
                _selectedReminder = selectedRow == null ? null : previous;
            }
            else
            {
                for (int index = 0; index < items.Count; index++)
                    UpdateReminderRow((WC.ListBoxItem)
                        _reminderList.Items[index], items[index]);
            }
            _reminderPanel.Visibility = items.Count == 0
                ? W.Visibility.Collapsed : W.Visibility.Visible;
            RefreshReminderState();
        }

        private void UpdateReminderRow(WC.ListBoxItem row,
            ReminderItem reminder)
        {
            row.Content = ReminderDisplayText(reminder);
            row.FontSize = PointSizeToDip(Math.Max(6F, Math.Min(72F,
                reminder.FontSizeTwips / 20F)));
        }

        internal void PreviewReminderFontSize(ReminderItem reminder,
            float points)
        {
            if (reminder == null) return;
            foreach (object value in _reminderList.Items)
            {
                WC.ListBoxItem row = value as WC.ListBoxItem;
                if (row != null && Object.ReferenceEquals(row.Tag, reminder))
                {
                    row.FontSize = PointSizeToDip(Math.Max(8F,
                        Math.Min(36F, points)));
                    break;
                }
            }
        }

        public void RefreshReminderState()
        {
            _deleteReminderButton.Visibility = _selectedReminder == null
                ? W.Visibility.Collapsed : W.Visibility.Visible;
        }

        private string ReminderDisplayText(ReminderItem reminder)
        {
            if (reminder == null) return String.Empty;
            return "• " + ShortItemText.Normalize(reminder.Text) + "  ·  " +
                FormatCountdown(reminder.DeadlineUtc - DateTime.UtcNow);
        }

        private void ExecuteSelectedReminderDelete()
        {
            if (_selectedReminder == null) return;
            Raise(DeleteReminderRequested,
                new ReminderActionEventArgs(_selectedReminder));
        }

        private void ExecuteSelectedReminderModify()
        {
            if (_selectedReminder == null) return;
            PersistNow();
            Raise(ModifyReminderRequested,
                new ReminderActionEventArgs(_selectedReminder));
        }
    }
}
