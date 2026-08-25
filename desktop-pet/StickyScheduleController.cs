using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    // Schedule-specific UI and ordering rules stay on the established sticky
    // window path; this file only separates their maintenance boundary.
    internal sealed partial class StickyNoteForm
    {
        internal void PromptAddScheduleItem()
        {
            if (!Data.IsSchedule || Data.ScheduleItems.Count >=
                StickyNoteLimits.MaximumScheduleItemsPerNote) return;
            ScheduleItemDialog dialog = new ScheduleItemDialog(String.Empty,
                DateTime.Today.AddDays(1));
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            StickyScheduleItem item = new StickyScheduleItem(
                dialog.ScheduleText, dialog.ScheduleDate);
            int destination = ScheduleInsertionIndex(Data.ScheduleItems, item);
            Data.ScheduleItems.Insert(destination, item);
            _selectedSchedule = item;
            RefreshScheduleList();
            RefreshTitle();
            PersistNow();
        }

        private void EditSchedule(StickyScheduleItem item)
        {
            if (item == null) return;
            ScheduleItemDialog dialog = new ScheduleItemDialog(item.Text,
                item.TargetDate);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            item.Text = ShortItemText.NormalizeAndTruncate(dialog.ScheduleText);
            item.TargetDateTicks = dialog.ScheduleDate.Date.Ticks;
            _selectedSchedule = item;
            RefreshScheduleList();
            RefreshTitle();
            PersistNow();
        }

        private void DeleteSelectedSchedule()
        {
            DeleteSchedule(_selectedSchedule);
        }

        private void DeleteSchedule(StickyScheduleItem item)
        {
            if (item == null || !Data.ScheduleItems.Remove(item)) return;
            if (Object.ReferenceEquals(_selectedSchedule, item))
                _selectedSchedule = null;
            RefreshScheduleList();
            RefreshTitle();
            PersistNow();
        }

        private void MoveSelectedSchedule(int delta)
        {
            if (_selectedSchedule == null || delta == 0) return;
            List<StickyScheduleItem> peers = new List<StickyScheduleItem>();
            foreach (StickyScheduleItem item in Data.ScheduleItems)
            {
                if (item.IsPinned == _selectedSchedule.IsPinned)
                    peers.Add(item);
            }
            int peerIndex = peers.IndexOf(_selectedSchedule);
            int destination = peerIndex + Math.Sign(delta);
            if (peerIndex < 0 || destination < 0 ||
                destination >= peers.Count) return;
            StickyScheduleItem other = peers[destination];
            int index = Data.ScheduleItems.IndexOf(_selectedSchedule);
            int otherIndex = Data.ScheduleItems.IndexOf(other);
            Data.ScheduleItems[index] = other;
            Data.ScheduleItems[otherIndex] = _selectedSchedule;
            RefreshScheduleList();
            PersistNow();
        }

        private void PinSchedule(StickyScheduleItem item)
        {
            if (item == null) return;
            SetSchedulePinned(item, !item.IsPinned);
        }

        private void SetSchedulePinned(StickyScheduleItem item, bool pinned)
        {
            if (item == null) return;
            int index = Data.ScheduleItems.IndexOf(item);
            if (index < 0) return;
            if (item.IsPinned == pinned)
            {
                _selectedSchedule = item;
                RefreshScheduleRowColors();
                return;
            }
            Data.ScheduleItems.RemoveAt(index);
            item.IsPinned = pinned;
            Data.ScheduleItems.Insert(ScheduleInsertionIndex(
                Data.ScheduleItems, item), item);
            _selectedSchedule = item;
            RefreshScheduleList();
            PersistNow();
        }

        private void RefreshScheduleList()
        {
            if (_scheduleRows == null) return;
            _rebuildingSchedules = true;
            try
            {
                if (_selectedSchedule != null &&
                    !Data.ScheduleItems.Contains(_selectedSchedule))
                    _selectedSchedule = null;
                _scheduleRows.Children.Clear();
                for (int pass = 0; pass < 2; pass++)
                {
                    bool pinned = pass == 0;
                    foreach (StickyScheduleItem item in Data.ScheduleItems)
                    {
                        if (item.IsPinned != pinned) continue;
                        _scheduleRows.Children.Add(CreateScheduleRow(item));
                    }
                }
                _scheduleCount.Text = Data.ScheduleItems.Count + "项";
            }
            finally { _rebuildingSchedules = false; }
            RefreshScheduleRowColors();
        }

        private W.UIElement CreateScheduleRow(StickyScheduleItem item)
        {
            WC.Border border = new WC.Border();
            border.Tag = item;
            border.Margin = new W.Thickness(7, 4, 7, 0);
            border.Padding = new W.Thickness(9, 7, 9, 7);
            border.BorderThickness = new W.Thickness(1);
            border.MinHeight = 58;

            WC.Grid grid = new WC.Grid();
            grid.ColumnDefinitions.Add(Column(new W.GridLength(1,
                W.GridUnitType.Star)));
            grid.ColumnDefinitions.Add(Column(W.GridLength.Auto));
            WC.StackPanel details = new WC.StackPanel();
            WC.TextBlock name = new WC.TextBlock();
            name.Text = (item.IsPinned ? "• " : String.Empty) + item.Text;
            name.TextWrapping = W.TextWrapping.Wrap;
            name.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            name.FontSize = PointSizeToDip(
                NormalizeScheduleFontSize(Data.FontSizeTwips / 20F));
            WC.TextBlock date = new WC.TextBlock();
            date.Text = item.TargetDate.ToString("yyyy-MM-dd");
            date.Margin = new W.Thickness(0, 3, 0, 0);
            date.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            date.FontSize = PointSizeToDip(Math.Max(8.5F,
                NormalizeScheduleFontSize(Data.FontSizeTwips / 20F) * 0.66F));
            details.Children.Add(name);
            details.Children.Add(date);
            WC.TextBlock countdown = new WC.TextBlock();
            countdown.Text = FormatScheduleCountdown(item.TargetDate,
                DateTime.Today);
            countdown.Margin = new W.Thickness(12, 0, 0, 0);
            countdown.VerticalAlignment = W.VerticalAlignment.Center;
            countdown.HorizontalAlignment = W.HorizontalAlignment.Right;
            countdown.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            countdown.FontWeight = W.FontWeights.Bold;
            countdown.FontSize = PointSizeToDip(
                NormalizeScheduleFontSize(Data.FontSizeTwips / 20F) + 5F);
            AddToGrid(grid, details, 0);
            AddToGrid(grid, countdown, 1);
            border.Child = grid;

            WC.ContextMenu menu = new WC.ContextMenu();
            AddMenuItem(menu, "编辑日程", delegate { EditSchedule(item); });
            AddMenuItem(menu, "删除日程", delegate { DeleteSchedule(item); });
            AddMenuItem(menu, item.IsPinned ? "取消置顶日程" : "置顶日程",
                delegate { PinSchedule(item); });
            border.ContextMenu = menu;
            border.PreviewMouseRightButtonDown += delegate
            {
                _selectedSchedule = item;
                RefreshScheduleRowColors();
            };
            border.PreviewMouseLeftButtonDown += delegate(object sender,
                MouseButtonEventArgs e)
            {
                if (_rebuildingSchedules) return;
                _selectedSchedule = item;
                RefreshScheduleRowColors();
                if (e.ClickCount >= 2)
                {
                    EditSchedule(item);
                    e.Handled = true;
                }
            };
            return border;
        }

        private void RefreshScheduleRowColors()
        {
            if (_scheduleRows == null) return;
            Color paper = Color.FromArgb(Data.ColorArgb);
            Color selected = WF.ControlPaint.Dark(paper, 0.14F);
            Color pinned = WF.ControlPaint.Dark(paper, 0.065F);
            Color borderColor = WF.ControlPaint.Dark(paper, 0.12F);
            int opacity = Math.Max(10, Data.BackgroundOpacityPercent);
            System.Windows.Media.Brush text = OpaqueBrush(EffectiveTextColor());
            ApplyTextBrush(_schedulePanel, text);
            foreach (W.UIElement element in _scheduleRows.Children)
            {
                WC.Border border = element as WC.Border;
                if (border == null) continue;
                StickyScheduleItem item = border.Tag as StickyScheduleItem;
                bool active = Object.ReferenceEquals(item, _selectedSchedule);
                bool isPinned = item != null && item.IsPinned;
                border.BorderBrush = AlphaBrush(borderColor, active ? 80 : 35);
                border.Background = active ? AlphaBrush(selected, opacity) :
                    (isPinned ? AlphaBrush(pinned, opacity) :
                    System.Windows.Media.Brushes.Transparent);
            }
            _schedulePinToggleButton.IsEnabled = true;
            _schedulePinToggleButton.Content = _selectedSchedule != null &&
                _selectedSchedule.IsPinned ? "取消置顶" : "置顶日程";
        }

        private static void ApplyTextBrush(W.DependencyObject root,
            System.Windows.Media.Brush brush)
        {
            if (root == null) return;
            WC.TextBlock text = root as WC.TextBlock;
            if (text != null) text.Foreground = brush;
            WC.TextBox input = root as WC.TextBox;
            if (input != null) input.Foreground = brush;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
                ApplyTextBrush(System.Windows.Media.VisualTreeHelper.GetChild(
                    root, index), brush);
        }

        internal static string FormatScheduleCountdown(DateTime targetDate,
            DateTime today)
        {
            int days = (targetDate.Date - today.Date).Days;
            if (days == 0) return "今天";
            if (days > 0) return days + "天";
            return "已过" + Math.Abs(days) + "天";
        }

        internal static int ScheduleInsertionIndex(
            IList<StickyScheduleItem> items, StickyScheduleItem inserted)
        {
            if (items == null || inserted == null) return 0;
            int index = 0;
            if (inserted.IsPinned)
            {
                while (index < items.Count && items[index] != null &&
                    items[index].IsPinned) index++;
                return index;
            }
            while (index < items.Count && items[index] != null &&
                items[index].IsPinned) index++;
            while (index < items.Count && items[index] != null &&
                !items[index].IsPinned &&
                items[index].TargetDate <= inserted.TargetDate) index++;
            return index;
        }

        internal static float NormalizeScheduleFontSize(float points)
        {
            if (points <= 9.75F) return 9F;
            if (points <= 13.25F) return 10.5F;
            if (points <= 19F) return 16F;
            if (points <= 35F) return 22F;
            return 48F;
        }

        internal static string ScheduleFontSizeLabel(float points)
        {
            float normalized = NormalizeScheduleFontSize(points);
            if (normalized <= 9F) return "特小 9";
            if (normalized <= 10.5F) return "小 10.5";
            if (normalized <= 16F) return "中 16";
            if (normalized <= 22F) return "大 22";
            return "特大 48";
        }

        internal static string BuildPlainTextFromSchedules(
            IEnumerable<StickyScheduleItem> items)
        {
            StringBuilder body = new StringBuilder();
            if (items == null) return String.Empty;
            foreach (StickyScheduleItem item in items)
            {
                if (item == null) continue;
                if (body.Length > 0) body.AppendLine();
                body.Append(item.TargetDate.ToString("yyyy-MM-dd"))
                    .Append(' ').Append(item.Text);
            }
            return body.ToString();
        }
    }
}
