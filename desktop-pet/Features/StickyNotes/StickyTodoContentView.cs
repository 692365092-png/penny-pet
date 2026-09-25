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
        internal sealed class StickyTodoContentView : StickyListContentView
        {
            internal readonly WC.Grid _todoPanel;
            internal readonly WC.StackPanel _todoRows;
            internal readonly WC.Button _todoAddButton;
            internal readonly WC.Button _todoDeleteButton;
            internal readonly WC.Button _todoPinToggleButton;
            internal readonly WC.Button _todoUpButton;
            internal readonly WC.Button _todoDownButton;
            internal readonly WC.TextBlock _todoProgress;
            internal StickyTodoItem _selectedTodo;
            internal bool _rebuildingTodos;

            internal StickyTodoContentView(StickyNoteWindow owner) : base(owner)
            {
                _todoAddButton = _owner.ActionButton("新增", 52);
                _todoDeleteButton = _owner.ActionButton("删除", 52);
                BuildListToolbar(_todoAddButton, _todoDeleteButton);
                _todoRows = new WC.StackPanel();
                WC.ScrollViewer todoScroller = new WC.ScrollViewer();
                todoScroller.VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto;
                todoScroller.Content = _todoRows;
                _todoPinToggleButton = _owner.ActionButton("置顶待办", 76);
                _todoUpButton = _owner.ActionButton("↑", 46);
                _todoDownButton = _owner.ActionButton("↓", 46);
                _todoProgress = new WC.TextBlock();
                _todoProgress.VerticalAlignment = W.VerticalAlignment.Center;
                _todoProgress.Margin = new W.Thickness(7, 0, 4, 0);
                _todoProgress.FontFamily = new System.Windows.Media.FontFamily(
                    "Microsoft YaHei UI");
                _todoProgress.FontSize = PointSizeToDip(8.5F);
                WC.Grid todoCommands = new WC.Grid();
                todoCommands.Margin = new W.Thickness(6, 4, 6, 7);
                // Preserve the former cancel-button position while exposing only
                // one pin toggle.  The spacer also keeps the move buttons stable.
                todoCommands.ColumnDefinitions.Add(Column(new W.GridLength(80)));
                todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                todoCommands.ColumnDefinitions.Add(Column(new W.GridLength(1,
                    W.GridUnitType.Star)));
                todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                todoCommands.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                AddToGrid(todoCommands, _todoPinToggleButton, 1);
                AddToGrid(todoCommands, _todoUpButton, 3);
                AddToGrid(todoCommands, _todoDownButton, 4);
                AddToGrid(todoCommands, _todoProgress, 5);
                _todoPanel = new WC.Grid();
                _todoPanel.RowDefinitions.Add(Row(new W.GridLength(1,
                    W.GridUnitType.Star)));
                _todoPanel.RowDefinitions.Add(Row(W.GridLength.Auto));
                WC.Grid.SetRow(todoScroller, 0);
                WC.Grid.SetRow(todoCommands, 1);
                _todoPanel.Children.Add(todoScroller);
                _todoPanel.Children.Add(todoCommands);

                _todoAddButton.Click += delegate { AddBlankTodoAndEdit(); };
                _todoDeleteButton.Click += delegate { DeleteSelectedTodo(); };
                _todoPinToggleButton.Click += delegate {
                    if (_selectedTodo != null)
                        SetTodoPinned(_selectedTodo, !_selectedTodo.IsPinned);
                };
                _todoUpButton.Click += delegate { MoveSelectedTodo(-1); };
                _todoDownButton.Click += delegate { MoveSelectedTodo(1); };
                RefreshTodoList();
            }

            // Collapse is runtime-only UI state. It is intentionally not persisted
            // and never changes Todo data, window size, or Dock geometry.
            internal readonly HashSet<StickyTodoState> _collapsedTodoGroups =
                new HashSet<StickyTodoState>();

            internal void AddBlankTodoAndEdit()
            {
                if (!_owner.Data.IsTodoList || _owner.Data.TodoItems.Count >=
                    StickyNoteLimits.MaximumTodoItemsPerNote) return;
                StickyTodoItem item = new StickyTodoItem(String.Empty, false);
                // The unfinished section preserves collection order, so index 0
                // makes the new editable row immediately visible at its top.
                _owner.Data.TodoItems.Insert(0, item);
                _selectedTodo = item;
                // A brand-new item must be visible immediately, so re-expand the
                // unfinished group even if the user had collapsed it.
                _collapsedTodoGroups.Remove(StickyTodoState.Pending);
                RefreshTodoList();
                _owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(delegate
                    {
                        if (_owner._disposed || !_owner.Data.TodoItems.Contains(item)) return;
                        WC.TextBox editor = FindTodoEditor(item);
                        if (editor != null) BeginTodoInlineEdit(editor, 0);
                    }));
            }

            internal WC.TextBox FindTodoEditor(StickyTodoItem item)
            {
                if (item == null || _todoRows == null) return null;
                foreach (W.UIElement element in _todoRows.Children)
                {
                    WC.Border border = element as WC.Border;
                    if (border == null || !Object.ReferenceEquals(border.Tag, item))
                        continue;
                    WC.Grid grid = border.Child as WC.Grid;
                    if (grid == null || grid.Children.Count < 2) return null;
                    return grid.Children[1] as WC.TextBox;
                }
                return null;
            }

            internal void DeleteSelectedTodo()
            {
                if (_selectedTodo == null) return;
                _owner.Data.TodoItems.Remove(_selectedTodo);
                _selectedTodo = null;
                RefreshTodoList();
                _owner.PersistNow();
            }

            internal void MoveSelectedTodo(int delta)
            {
                if (_selectedTodo == null || delta == 0) return;
                List<StickyTodoItem> peers = new List<StickyTodoItem>();
                foreach (StickyTodoItem item in _owner.Data.TodoItems)
                {
                    if (item.State == _selectedTodo.State &&
                        item.IsPinned == _selectedTodo.IsPinned)
                        peers.Add(item);
                }
                int peerIndex = peers.IndexOf(_selectedTodo);
                int destination = peerIndex + Math.Sign(delta);
                if (peerIndex < 0 || destination < 0 ||
                    destination >= peers.Count) return;
                StickyTodoItem other = peers[destination];
                int index = _owner.Data.TodoItems.IndexOf(_selectedTodo);
                int otherIndex = _owner.Data.TodoItems.IndexOf(other);
                _owner.Data.TodoItems[index] = other;
                _owner.Data.TodoItems[otherIndex] = _selectedTodo;
                RefreshTodoList();
                _owner.PersistNow();
            }

            internal void RefreshTodoList()
            {
                if (_todoRows == null) return;
                _rebuildingTodos = true;
                try
                {
                    _todoRows.Children.Clear();
                    AddTodoSection("未完成", StickyTodoState.Pending);
                    AddTodoSection("进行中", StickyTodoState.InProgress);
                    AddTodoSection("已完成", StickyTodoState.Completed);
                    int completed = 0;
                    foreach (StickyTodoItem item in _owner.Data.TodoItems)
                        if (item.Completed) completed++;
                    _todoProgress.Text = completed + "/" + _owner.Data.TodoItems.Count;
                }
                finally { _rebuildingTodos = false; }
                RefreshTodoRowColors();
            }

            internal void AddTodoSection(string title, StickyTodoState state)
            {
                bool collapsed = _collapsedTodoGroups.Contains(state);
                WC.TextBlock heading = new WC.TextBlock();
                heading.Text = (collapsed ? "▶ " : "▼ ") + title;
                heading.Tag = "todo-heading";
                heading.Margin = new W.Thickness(9, 7, 7, 3);
                heading.FontFamily = new System.Windows.Media.FontFamily(
                    "Microsoft YaHei UI");
                heading.FontSize = PointSizeToDip(8.5F);
                heading.FontWeight = W.FontWeights.Bold;
                heading.Cursor = Cursors.Hand;
                heading.MouseLeftButtonDown += delegate(object sender,
                    MouseButtonEventArgs e)
                {
                    if (_rebuildingTodos) return;
                    ToggleTodoGroup(state);
                    e.Handled = true;
                };
                _todoRows.Children.Add(heading);
                if (collapsed) return;
                // Always render pinned items before ordinary items, while keeping
                // the user's order inside each partition stable.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool pinned = pass == 0;
                    foreach (StickyTodoItem item in _owner.Data.TodoItems)
                    {
                        if (item.State != state ||
                            item.IsPinned != pinned) continue;
                        _todoRows.Children.Add(CreateTodoRow(item));
                    }
                }
            }

            internal void ToggleTodoGroup(StickyTodoState state)
            {
                if (_collapsedTodoGroups.Contains(state))
                    _collapsedTodoGroups.Remove(state);
                else
                    _collapsedTodoGroups.Add(state);
                RefreshTodoList();
            }

            internal W.UIElement CreateTodoRow(StickyTodoItem item)
            {
                WC.Border border = new WC.Border();
                border.Tag = item;
                border.BorderThickness = new W.Thickness(1);
                border.Margin = new W.Thickness(6, 1, 6, 1);
                border.Padding = new W.Thickness(4, 2, 4, 2);
                WC.Grid grid = new WC.Grid();
                grid.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                grid.ColumnDefinitions.Add(Column(W.GridLength.Auto));
                grid.ColumnDefinitions.Add(Column(new W.GridLength(1,
                    W.GridUnitType.Star)));
                WC.CheckBox check = new WC.CheckBox();
                check.IsThreeState = true;
                check.IsChecked = item.State == StickyTodoState.InProgress
                    ? (bool?)null : item.State == StickyTodoState.Completed;
                check.VerticalAlignment = W.VerticalAlignment.Center;
                check.Margin = new W.Thickness(0, 0, 6, 0);
                check.Click += delegate
                {
                    if (_rebuildingTodos) return;
                    SetTodoState(item, NextTodoState(item.State));
                };
                WC.TextBox editor = _owner.PlainTextBox();
                editor.Tag = item;
                editor.Text = item.Text;
                editor.BorderThickness = new W.Thickness(0);
                editor.Background = System.Windows.Media.Brushes.Transparent;
                editor.TextWrapping = W.TextWrapping.Wrap;
                editor.AcceptsReturn = false;
                editor.IsReadOnly = true;
                editor.IsReadOnlyCaretVisible = false;
                editor.Focusable = false;
                editor.Cursor = Cursors.Arrow;
                _owner.ConfigureMultilingualTextInput(editor);
                // Strikeout is render state only. It overlays whatever font
                // weight/style the row already has and never mutates item text.
                editor.TextDecorations = item.State == StickyTodoState.Completed
                    ? System.Windows.TextDecorations.Strikethrough : null;
                WC.TextBlock pinMarker = new WC.TextBlock();
                pinMarker.Text = "•";
                pinMarker.Margin = new W.Thickness(0, 0, 5, 0);
                pinMarker.VerticalAlignment = W.VerticalAlignment.Center;
                pinMarker.FontWeight = W.FontWeights.Bold;
                pinMarker.Visibility = item.IsPinned ? W.Visibility.Visible :
                    W.Visibility.Collapsed;
                string originalText = item.Text;
                border.ContextMenu = BuildTodoItemMenu(item, editor,
                    delegate
                    {
                        originalText = item.Text;
                        BeginTodoInlineEdit(editor, editor.Text.Length);
                    });
                // TextBox and CheckBox otherwise supply/inherit their own menu and
                // can bypass the row-specific commands.
                editor.ContextMenu = border.ContextMenu;
                check.ContextMenu = border.ContextMenu;
                border.PreviewMouseRightButtonDown += delegate
                {
                    _selectedTodo = item;
                    RefreshTodoRowColors();
                };
                border.PreviewMouseLeftButtonDown += delegate(object sender,
                    MouseButtonEventArgs e)
                {
                    _selectedTodo = item;
                    RefreshTodoRowColors();
                    if (e.ClickCount < 2) return;
                    originalText = item.Text;
                    int caretIndex = editor.GetCharacterIndexFromPoint(
                        e.GetPosition(editor), true);
                    if (caretIndex < 0) caretIndex = editor.Text.Length;
                    BeginTodoInlineEdit(editor, caretIndex);
                    e.Handled = true;
                };
                editor.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key == Key.Enter)
                    {
                        CommitTodoInlineEdit(item, editor, originalText);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        if (String.IsNullOrWhiteSpace(originalText))
                            DeleteTodo(item);
                        else CancelTodoInlineEdit(editor, originalText);
                        e.Handled = true;
                    }
                };
                editor.LostKeyboardFocus += delegate
                {
                    if (!editor.IsReadOnly)
                        CommitTodoInlineEdit(item, editor, originalText);
                };
                AddToGrid(grid, check, 0);
                // Keep the editor as child #1 for the existing inline-edit and QA
                // paths; the marker is visual-only and never enters the item text.
                AddToGrid(grid, editor, 2);
                AddToGrid(grid, pinMarker, 1);
                border.Child = grid;
                return border;
            }

            internal WC.ContextMenu BuildTodoItemMenu(StickyTodoItem item,
                WC.TextBox editor, Action beginEdit)
            {
                WC.ContextMenu menu = new WC.ContextMenu();
                AddMenuItem(menu, "编辑待办", delegate
                {
                    _selectedTodo = item;
                    RefreshTodoRowColors();
                    if (beginEdit != null) beginEdit();
                });
                AddMenuItem(menu, "设为未完成", delegate
                    { SetTodoState(item, StickyTodoState.Pending); });
                AddMenuItem(menu, "设为进行中", delegate
                    { SetTodoState(item, StickyTodoState.InProgress); });
                AddMenuItem(menu, "设为已完成", delegate
                    { SetTodoState(item, StickyTodoState.Completed); });
                AddMenuItem(menu, "删除待办", delegate { DeleteTodo(item); });
                AddMenuItem(menu, item.IsPinned ? "取消置顶待办" : "置顶待办",
                    delegate { PinTodo(item); });
                return menu;
            }

            internal void SetTodoState(StickyTodoItem item, StickyTodoState state)
            {
                if (item == null) return;
                item.State = state;
                _selectedTodo = item;
                RefreshTodoList();
                _owner.PersistNow();
            }

            internal static StickyTodoState NextTodoState(StickyTodoState state)
            {
                if (state == StickyTodoState.Pending)
                    return StickyTodoState.InProgress;
                return state == StickyTodoState.InProgress
                    ? StickyTodoState.Completed : StickyTodoState.Pending;
            }

            internal void DeleteTodo(StickyTodoItem item)
            {
                if (item == null || !_owner.Data.TodoItems.Remove(item)) return;
                if (Object.ReferenceEquals(_selectedTodo, item)) _selectedTodo = null;
                RefreshTodoList();
                _owner.PersistNow();
            }

            internal void PinTodo(StickyTodoItem item)
            {
                if (item == null) return;
                SetTodoPinned(item, !item.IsPinned);
            }

            internal void SetTodoPinned(StickyTodoItem item, bool pinned)
            {
                if (item == null) return;
                int index = _owner.Data.TodoItems.IndexOf(item);
                if (index < 0) return;
                if (item.IsPinned == pinned)
                {
                    _selectedTodo = item;
                    RefreshTodoRowColors();
                    return;
                }
                _owner.Data.TodoItems.RemoveAt(index);
                item.IsPinned = pinned;
                _owner.Data.TodoItems.Insert(TodoInsertionIndex(_owner.Data.TodoItems, item),
                    item);
                _selectedTodo = item;
                RefreshTodoList();
                _owner.PersistNow();
            }

            internal static int TodoInsertionIndex(IList<StickyTodoItem> items,
                StickyTodoItem inserted)
            {
                if (items == null || inserted == null) return 0;
                if (inserted.IsPinned)
                {
                    for (int index = 0; index < items.Count; index++)
                        if (items[index] != null &&
                            items[index].State == inserted.State)
                            return index;
                    return items.Count;
                }
                for (int index = 0; index < items.Count; index++)
                    if (items[index] != null &&
                        items[index].State == inserted.State &&
                        !items[index].IsPinned) return index;
                return items.Count;
            }

            internal void BeginTodoInlineEdit(WC.TextBox editor, int caretIndex)
            {
                if (editor == null) return;
                editor.IsReadOnly = false;
                editor.Focusable = true;
                editor.Cursor = Cursors.IBeam;
                editor.Focus();
                int safeIndex = Math.Max(0, Math.Min(editor.Text.Length, caretIndex));
                editor.SelectionStart = safeIndex;
                editor.SelectionLength = 0;
            }

            internal void CommitTodoInlineEdit(StickyTodoItem item,
                WC.TextBox editor, string originalText)
            {
                if (item == null || editor == null || editor.IsReadOnly) return;
                string normalized = ShortItemText.NormalizeAndTruncate(editor.Text);
                if (String.IsNullOrWhiteSpace(normalized))
                {
                    if (String.IsNullOrWhiteSpace(originalText))
                    {
                        editor.IsReadOnly = true;
                        _owner.Data.TodoItems.Remove(item);
                        if (Object.ReferenceEquals(_selectedTodo, item))
                            _selectedTodo = null;
                        RefreshTodoList();
                        _owner.PersistNow();
                        return;
                    }
                    normalized = originalText ?? item.Text ?? String.Empty;
                }
                _rebuildingTodos = true;
                try { editor.Text = normalized; }
                finally { _rebuildingTodos = false; }
                item.Text = normalized;
                editor.IsReadOnly = true;
                editor.IsReadOnlyCaretVisible = false;
                editor.Focusable = false;
                editor.Cursor = Cursors.Arrow;
                _owner.Data.Text = BuildPlainTextFromTodos(_owner.Data.TodoItems);
                _owner.PersistNow();
                _owner.RefreshTitle();
            }

            internal void CancelTodoInlineEdit(WC.TextBox editor,
                string originalText)
            {
                if (editor == null || editor.IsReadOnly) return;
                _rebuildingTodos = true;
                try { editor.Text = originalText ?? String.Empty; }
                finally { _rebuildingTodos = false; }
                editor.IsReadOnly = true;
                editor.IsReadOnlyCaretVisible = false;
                editor.Focusable = false;
                editor.Cursor = Cursors.Arrow;
            }

            internal void RefreshTodoRowColors()
            {
                if (_todoRows == null) return;
                Color paper = Color.FromArgb(_owner.Data.ColorArgb);
                Color selected = WF.ControlPaint.Dark(paper, 0.14F);
                Color body = WF.ControlPaint.LightLight(paper);
                Color pinned = CalculatePinnedItemColor(body, paper);
                Color borderColor = WF.ControlPaint.Dark(paper, 0.12F);
                int opacity = Math.Max(10, _owner.Data.BackgroundOpacityPercent);
                System.Windows.Media.Brush text = OpaqueBrush(_owner.EffectiveTextColor());
                foreach (W.UIElement element in _todoRows.Children)
                {
                    WC.TextBlock heading = element as WC.TextBlock;
                    if (heading != null)
                    {
                        heading.Foreground = text;
                        continue;
                    }
                    WC.Border border = element as WC.Border;
                    if (border == null) continue;
                    StickyTodoItem item = border.Tag as StickyTodoItem;
                    bool active = Object.ReferenceEquals(item, _selectedTodo);
                    bool isPinned = item != null && item.IsPinned;
                    border.BorderBrush = AlphaBrush(borderColor,
                        active ? 80 : (isPinned ? 48 : 25));
                    border.Background = active ? AlphaBrush(selected, opacity) :
                        (isPinned ? AlphaBrush(pinned, opacity) :
                        System.Windows.Media.Brushes.Transparent);
                    WC.Grid grid = border.Child as WC.Grid;
                    if (grid == null || grid.Children.Count < 2) continue;
                    WC.TextBox editor = grid.Children[1] as WC.TextBox;
                    if (editor != null) editor.Foreground = text;
                    if (grid.Children.Count > 2)
                    {
                        WC.TextBlock marker = grid.Children[2] as WC.TextBlock;
                        if (marker != null) marker.Foreground = text;
                    }
                }
                _todoPinToggleButton.IsEnabled = true;
                _todoPinToggleButton.Content = _selectedTodo != null &&
                    _selectedTodo.IsPinned ? "取消置顶" : "置顶待办";
            }

            internal static Color CalculatePinnedItemColor(Color body, Color header)
            {
                // Pinned rows sit between the strong header color and the light
                // body background: 35% toward the header keeps them visible
                // without competing with the title bar.
                return Color.FromArgb(body.A,
                    (int)Math.Round(body.R + (header.R - body.R) * 0.35),
                    (int)Math.Round(body.G + (header.G - body.G) * 0.35),
                    (int)Math.Round(body.B + (header.B - body.B) * 0.35));
            }

            internal override void ClearSelection()
            { if (_selectedTodo == null) return; _selectedTodo = null; RefreshTodoRowColors(); }
            internal override W.FrameworkElement Body { get { return _todoPanel; } }
            internal override void FocusPrimaryInput() { _todoAddButton.Focus(); }
            internal override void Capture() { _owner.Data.Text = BuildPlainTextFromTodos(_owner.Data.TodoItems); }
            internal override void RefreshList() { RefreshTodoList(); }
            internal override void ApplyContentColors()
            {
                StyleSelector(_fontSizeBox);
                StyleButtons(_todoAddButton, _todoDeleteButton, _todoPinToggleButton, _todoUpButton, _todoDownButton);
                _todoProgress.Foreground = OpaqueBrush(_owner.EffectiveTextColor());
                RefreshTodoRowColors();
            }

        }
    }
}
