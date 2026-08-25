using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    // Todo-specific interaction and rendering remain part of the sticky window
    // so the established WPF focus and input path does not gain a new boundary.
    internal sealed partial class StickyNoteForm
    {
        private void TodoInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddBlankTodoAndEdit();
            e.Handled = true;
        }

        private void AddBlankTodoAndEdit()
        {
            if (!Data.IsTodoList || Data.TodoItems.Count >=
                StickyNoteLimits.MaximumTodoItemsPerNote) return;
            StickyTodoItem item = new StickyTodoItem(String.Empty, false);
            // The unfinished section preserves collection order, so index 0
            // makes the new editable row immediately visible at its top.
            Data.TodoItems.Insert(0, item);
            _selectedTodo = item;
            RefreshTodoList();
            Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(delegate
                {
                    if (_disposed || !Data.TodoItems.Contains(item)) return;
                    WC.TextBox editor = FindTodoEditor(item);
                    if (editor != null) BeginTodoInlineEdit(editor, 0);
                }));
        }

        private WC.TextBox FindTodoEditor(StickyTodoItem item)
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

        private void DeleteSelectedTodo()
        {
            if (_selectedTodo == null) return;
            Data.TodoItems.Remove(_selectedTodo);
            _selectedTodo = null;
            RefreshTodoList();
            PersistNow();
        }

        private void MoveSelectedTodo(int delta)
        {
            if (_selectedTodo == null || delta == 0) return;
            List<StickyTodoItem> peers = new List<StickyTodoItem>();
            foreach (StickyTodoItem item in Data.TodoItems)
            {
                if (item.Completed == _selectedTodo.Completed &&
                    item.IsPinned == _selectedTodo.IsPinned)
                    peers.Add(item);
            }
            int peerIndex = peers.IndexOf(_selectedTodo);
            int destination = peerIndex + Math.Sign(delta);
            if (peerIndex < 0 || destination < 0 ||
                destination >= peers.Count) return;
            StickyTodoItem other = peers[destination];
            int index = Data.TodoItems.IndexOf(_selectedTodo);
            int otherIndex = Data.TodoItems.IndexOf(other);
            Data.TodoItems[index] = other;
            Data.TodoItems[otherIndex] = _selectedTodo;
            RefreshTodoList();
            PersistNow();
        }

        private void RefreshTodoList()
        {
            if (_todoRows == null) return;
            _rebuildingTodos = true;
            try
            {
                _todoRows.Children.Clear();
                AddTodoSection("未完成", false);
                AddTodoSection("已完成", true);
                int completed = 0;
                foreach (StickyTodoItem item in Data.TodoItems)
                    if (item.Completed) completed++;
                _todoProgress.Text = completed + "/" + Data.TodoItems.Count;
            }
            finally { _rebuildingTodos = false; }
            RefreshTodoRowColors();
        }

        private void AddTodoSection(string title, bool completed)
        {
            WC.TextBlock heading = new WC.TextBlock();
            heading.Text = title;
            heading.Tag = "todo-heading";
            heading.Margin = new W.Thickness(9, 7, 7, 3);
            heading.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            heading.FontSize = PointSizeToDip(8.5F);
            heading.FontWeight = W.FontWeights.Bold;
            _todoRows.Children.Add(heading);
            // Always render pinned items before ordinary items, while keeping
            // the user's order inside each partition stable.
            for (int pass = 0; pass < 2; pass++)
            {
                bool pinned = pass == 0;
                foreach (StickyTodoItem item in Data.TodoItems)
                {
                    if (item.Completed != completed ||
                        item.IsPinned != pinned) continue;
                    _todoRows.Children.Add(CreateTodoRow(item));
                }
            }
        }

        private W.UIElement CreateTodoRow(StickyTodoItem item)
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
            check.IsChecked = item.Completed;
            check.VerticalAlignment = W.VerticalAlignment.Center;
            check.Margin = new W.Thickness(0, 0, 6, 0);
            check.Checked += delegate
            {
                if (_rebuildingTodos) return;
                item.Completed = true;
                _selectedTodo = item;
                RefreshTodoList();
                PersistNow();
            };
            check.Unchecked += delegate
            {
                if (_rebuildingTodos) return;
                item.Completed = false;
                _selectedTodo = item;
                RefreshTodoList();
                PersistNow();
            };
            WC.TextBox editor = PlainTextBox();
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
            ConfigureMultilingualTextInput(editor);
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
            // can bypass the row-specific three commands.
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

        private WC.ContextMenu BuildTodoItemMenu(StickyTodoItem item,
            WC.TextBox editor, Action beginEdit)
        {
            WC.ContextMenu menu = new WC.ContextMenu();
            AddMenuItem(menu, "编辑待办", delegate
            {
                _selectedTodo = item;
                RefreshTodoRowColors();
                if (beginEdit != null) beginEdit();
            });
            AddMenuItem(menu, "删除待办", delegate { DeleteTodo(item); });
            AddMenuItem(menu, item.IsPinned ? "取消置顶待办" : "置顶待办",
                delegate { PinTodo(item); });
            return menu;
        }

        private void DeleteTodo(StickyTodoItem item)
        {
            if (item == null || !Data.TodoItems.Remove(item)) return;
            if (Object.ReferenceEquals(_selectedTodo, item)) _selectedTodo = null;
            RefreshTodoList();
            PersistNow();
        }

        private void PinTodo(StickyTodoItem item)
        {
            if (item == null) return;
            SetTodoPinned(item, !item.IsPinned);
        }

        private void SetTodoPinned(StickyTodoItem item, bool pinned)
        {
            if (item == null) return;
            int index = Data.TodoItems.IndexOf(item);
            if (index < 0) return;
            if (item.IsPinned == pinned)
            {
                _selectedTodo = item;
                RefreshTodoRowColors();
                return;
            }
            Data.TodoItems.RemoveAt(index);
            item.IsPinned = pinned;
            Data.TodoItems.Insert(TodoInsertionIndex(Data.TodoItems, item),
                item);
            _selectedTodo = item;
            RefreshTodoList();
            PersistNow();
        }

        private static int TodoInsertionIndex(IList<StickyTodoItem> items,
            StickyTodoItem inserted)
        {
            if (items == null || inserted == null) return 0;
            if (inserted.IsPinned)
            {
                for (int index = 0; index < items.Count; index++)
                    if (items[index] != null &&
                        items[index].Completed == inserted.Completed)
                        return index;
                return items.Count;
            }
            for (int index = 0; index < items.Count; index++)
                if (items[index] != null &&
                    items[index].Completed == inserted.Completed &&
                    !items[index].IsPinned) return index;
            return items.Count;
        }

        private void BeginTodoInlineEdit(WC.TextBox editor, int caretIndex)
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

        private void CommitTodoInlineEdit(StickyTodoItem item,
            WC.TextBox editor, string originalText)
        {
            if (item == null || editor == null || editor.IsReadOnly) return;
            string normalized = ShortItemText.NormalizeAndTruncate(editor.Text);
            if (String.IsNullOrWhiteSpace(normalized))
            {
                if (String.IsNullOrWhiteSpace(originalText))
                {
                    editor.IsReadOnly = true;
                    Data.TodoItems.Remove(item);
                    if (Object.ReferenceEquals(_selectedTodo, item))
                        _selectedTodo = null;
                    RefreshTodoList();
                    PersistNow();
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
            Data.Text = BuildPlainTextFromTodos(Data.TodoItems);
            PersistNow();
            RefreshTitle();
        }

        private void CancelTodoInlineEdit(WC.TextBox editor,
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

        private void RefreshTodoRowColors()
        {
            if (_todoRows == null) return;
            Color paper = Color.FromArgb(Data.ColorArgb);
            Color selected = WF.ControlPaint.Dark(paper, 0.14F);
            Color pinned = WF.ControlPaint.Dark(paper, 0.065F);
            Color borderColor = WF.ControlPaint.Dark(paper, 0.12F);
            int opacity = Math.Max(10, Data.BackgroundOpacityPercent);
            System.Windows.Media.Brush text = OpaqueBrush(EffectiveTextColor());
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
    }
}
