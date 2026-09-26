using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    internal sealed partial class StickyNoteWindow
    {
        internal sealed class StickyScheduleContentView : StickyListContentView
        {
            internal readonly WC.Grid _schedulePanel;
            internal readonly WC.StackPanel _scheduleRows;
            internal readonly WC.Button _scheduleAddButton;
            internal readonly WC.Button _scheduleDeleteButton;
            internal readonly WC.Button _schedulePinToggleButton;
            internal readonly WC.Button _scheduleUpButton;
            internal readonly WC.Button _scheduleDownButton;
            internal readonly WC.TextBlock _scheduleCount;
            internal StickyScheduleItem _selectedSchedule;
            internal bool _rebuildingSchedules;
            internal readonly DispatcherTimer _scheduleRefreshTimer;

            internal StickyScheduleContentView(StickyNoteWindow owner) : base(owner)
            {
                _scheduleAddButton = _owner.ActionButton("新增", 52);
                _scheduleDeleteButton = _owner.ActionButton("删除", 52);
                BuildListToolbar(_scheduleAddButton, _scheduleDeleteButton);
                _scheduleRows = new WC.StackPanel();
                WC.ScrollViewer scheduleScroller = new WC.ScrollViewer();
                scheduleScroller.VerticalScrollBarVisibility =
                    WC.ScrollBarVisibility.Auto;
                scheduleScroller.Content = _scheduleRows;
                _schedulePinToggleButton = _owner.ActionButton("置顶日程", 76);
                _scheduleUpButton = _owner.ActionButton("↑", 46);
                _scheduleDownButton = _owner.ActionButton("↓", 46);
                _scheduleCount = new WC.TextBlock();
                _scheduleCount.VerticalAlignment = W.VerticalAlignment.Center;
                _scheduleCount.Margin = new W.Thickness(7, 0, 4, 0);
                _scheduleCount.FontFamily = new System.Windows.Media.FontFamily(
                    "Microsoft YaHei UI");
                _scheduleCount.FontSize = PointSizeToDip(8.5F);
                WC.Grid scheduleCommands = new WC.Grid();
                scheduleCommands.Margin = new W.Thickness(6, 4, 6, 7);
                scheduleCommands.ColumnDefinitions.Add(Column(new W.GridLength(80)));
                scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                scheduleCommands.ColumnDefinitions.Add(Column(new W.GridLength(1,
                    W.GridUnitType.Star)));
                scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                scheduleCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                AddToGrid(scheduleCommands, _schedulePinToggleButton, 1);
                AddToGrid(scheduleCommands, _scheduleUpButton, 3);
                AddToGrid(scheduleCommands, _scheduleDownButton, 4);
                AddToGrid(scheduleCommands, _scheduleCount, 5);
                _schedulePanel = new WC.Grid();
                _schedulePanel.RowDefinitions.Add(Row(new W.GridLength(1,
                    W.GridUnitType.Star)));
                _schedulePanel.RowDefinitions.Add(Row(W.GridLength.Auto));
                WC.Grid.SetRow(scheduleScroller, 0);
                WC.Grid.SetRow(scheduleCommands, 1);
                _schedulePanel.Children.Add(scheduleScroller);
                _schedulePanel.Children.Add(scheduleCommands);

                _scheduleRefreshTimer = new DispatcherTimer(
                    DispatcherPriority.Background);
                _scheduleRefreshTimer.Interval = TimeSpan.FromMinutes(1);
                _scheduleRefreshTimer.Tick += delegate
                {
                    if (_owner.Data.IsSchedule && _owner.IsVisible) RefreshScheduleList();
                };

                _scheduleAddButton.Click += delegate { PromptAddScheduleItem(); };
                _scheduleDeleteButton.Click += delegate { DeleteSelectedSchedule(); };
                _schedulePinToggleButton.Click += delegate {
                    if (_selectedSchedule != null)
                        SetSchedulePinned(_selectedSchedule,
                            !_selectedSchedule.IsPinned);
                };
                _scheduleUpButton.Click += delegate { MoveSelectedSchedule(-1); };
                _scheduleDownButton.Click += delegate { MoveSelectedSchedule(1); };
                RefreshScheduleList();
                _scheduleRefreshTimer.Start();
            }

            internal void PromptAddScheduleItem()
            {
                if (!_owner.Data.IsSchedule || _owner.Data.ScheduleItems.Count >=
                    StickyNoteLimits.MaximumScheduleItemsPerNote) return;
                ScheduleItemDialog dialog = new ScheduleItemDialog(String.Empty,
                    DateTime.Today.AddDays(1));
                dialog.Owner = _owner;
                if (dialog.ShowDialog() != true) return;
                StickyScheduleItem item = new StickyScheduleItem(
                    dialog.ScheduleText, dialog.ScheduleDate);
                int destination = ScheduleInsertionIndex(_owner.Data.ScheduleItems, item);
                _owner.Data.ScheduleItems.Insert(destination, item);
                _selectedSchedule = item;
                RefreshScheduleList();
                _owner.RefreshTitle();
                _owner.PersistNow();
            }

            internal void EditSchedule(StickyScheduleItem item)
            {
                if (item == null) return;
                ScheduleItemDialog dialog = new ScheduleItemDialog(item.Text,
                    item.TargetDate);
                dialog.Owner = _owner;
                if (dialog.ShowDialog() != true) return;
                item.Text = ShortItemText.NormalizeAndTruncate(dialog.ScheduleText);
                item.TargetDateTicks = dialog.ScheduleDate.Date.Ticks;
                _selectedSchedule = item;
                RefreshScheduleList();
                _owner.RefreshTitle();
                _owner.PersistNow();
            }

            internal void DeleteSelectedSchedule()
            {
                DeleteSchedule(_selectedSchedule);
            }

            internal void DeleteSchedule(StickyScheduleItem item)
            {
                if (item == null || !_owner.Data.ScheduleItems.Remove(item)) return;
                if (Object.ReferenceEquals(_selectedSchedule, item))
                    _selectedSchedule = null;
                RefreshScheduleList();
                _owner.RefreshTitle();
                _owner.PersistNow();
            }

            internal void MoveSelectedSchedule(int delta)
            {
                if (_selectedSchedule == null || delta == 0) return;
                List<StickyScheduleItem> peers = new List<StickyScheduleItem>();
                foreach (StickyScheduleItem item in _owner.Data.ScheduleItems)
                {
                    if (item.IsPinned == _selectedSchedule.IsPinned)
                        peers.Add(item);
                }
                int peerIndex = peers.IndexOf(_selectedSchedule);
                int destination = peerIndex + Math.Sign(delta);
                if (peerIndex < 0 || destination < 0 ||
                    destination >= peers.Count) return;
                StickyScheduleItem other = peers[destination];
                int index = _owner.Data.ScheduleItems.IndexOf(_selectedSchedule);
                int otherIndex = _owner.Data.ScheduleItems.IndexOf(other);
                _owner.Data.ScheduleItems[index] = other;
                _owner.Data.ScheduleItems[otherIndex] = _selectedSchedule;
                RefreshScheduleList();
                _owner.PersistNow();
            }

            internal void PinSchedule(StickyScheduleItem item)
            {
                if (item == null) return;
                SetSchedulePinned(item, !item.IsPinned);
            }

            internal void SetSchedulePinned(StickyScheduleItem item, bool pinned)
            {
                if (item == null) return;
                int index = _owner.Data.ScheduleItems.IndexOf(item);
                if (index < 0) return;
                if (item.IsPinned == pinned)
                {
                    _selectedSchedule = item;
                    RefreshScheduleRowColors();
                    return;
                }
                _owner.Data.ScheduleItems.RemoveAt(index);
                item.IsPinned = pinned;
                _owner.Data.ScheduleItems.Insert(ScheduleInsertionIndex(
                    _owner.Data.ScheduleItems, item), item);
                _selectedSchedule = item;
                RefreshScheduleList();
                _owner.PersistNow();
            }

            internal void RefreshScheduleList()
            {
                if (_scheduleRows == null) return;
                _rebuildingSchedules = true;
                try
                {
                    if (_selectedSchedule != null &&
                        !_owner.Data.ScheduleItems.Contains(_selectedSchedule))
                        _selectedSchedule = null;
                    _scheduleRows.Children.Clear();
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool pinned = pass == 0;
                        foreach (StickyScheduleItem item in _owner.Data.ScheduleItems)
                        {
                            if (item.IsPinned != pinned) continue;
                            _scheduleRows.Children.Add(CreateScheduleRow(item));
                        }
                    }
                    _scheduleCount.Text = _owner.Data.ScheduleItems.Count + "项";
                }
                finally { _rebuildingSchedules = false; }
                RefreshScheduleRowColors();
            }

            internal W.UIElement CreateScheduleRow(StickyScheduleItem item)
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
                    NormalizeScheduleFontSize(_owner.Data.FontSizeTwips / 20F));
                WC.TextBlock date = new WC.TextBlock();
                date.Text = item.TargetDate.ToString("yyyy-MM-dd");
                date.Margin = new W.Thickness(0, 3, 0, 0);
                date.FontFamily = new System.Windows.Media.FontFamily(
                    "Microsoft YaHei UI");
                date.FontSize = PointSizeToDip(Math.Max(8.5F,
                    NormalizeScheduleFontSize(_owner.Data.FontSizeTwips / 20F) * 0.66F));
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
                    NormalizeScheduleFontSize(_owner.Data.FontSizeTwips / 20F) + 5F);
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

            internal void RefreshScheduleRowColors()
            {
                if (_scheduleRows == null) return;
                Color paper = Color.FromArgb(_owner.Data.ColorArgb);
                Color selected = WF.ControlPaint.Dark(paper, 0.14F);
                Color pinned = WF.ControlPaint.Dark(paper, 0.065F);
                Color borderColor = WF.ControlPaint.Dark(paper, 0.12F);
                int opacity = Math.Max(10, _owner.Data.BackgroundOpacityPercent);
                System.Windows.Media.Brush text = OpaqueBrush(_owner.EffectiveTextColor());
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

            internal override void ClearSelection()
            { if (_selectedSchedule == null) return; _selectedSchedule = null; RefreshScheduleRowColors(); }
            internal override W.FrameworkElement Body { get { return _schedulePanel; } }
            internal override void FocusPrimaryInput() { _scheduleAddButton.Focus(); }
            internal override void Capture() { _owner.Data.Text = BuildPlainTextFromSchedules(_owner.Data.ScheduleItems); }
            internal override void RefreshList() { RefreshScheduleList(); }
            internal override void ApplyContentColors()
            {
                StyleSelector(_fontSizeBox);
                StyleButtons(_scheduleAddButton, _scheduleDeleteButton, _schedulePinToggleButton, _scheduleUpButton, _scheduleDownButton);
                _scheduleCount.Foreground = OpaqueBrush(_owner.EffectiveTextColor());
                RefreshScheduleRowColors();
            }
            public override void Dispose() { _scheduleRefreshTimer.Stop(); }

        }
    }
}
