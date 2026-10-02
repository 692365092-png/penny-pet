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
    // Test-side scripts exercise the real current view; no duplicate editor implementation.
    internal sealed class StickyWindowInteractionDriver
    {
        private readonly StickyNoteWindow _window;
        internal StickyWindowInteractionDriver(StickyNoteWindow window) { _window = window; }
        private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        private object View { get { return typeof(StickyNoteWindow).GetField("_contentView", Flags).GetValue(_window); } }
        private static System.Reflection.FieldInfo Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Flags);
                if (field != null) return field;
            }
            return null;
        }
        private object Target(string name)
        { return Field(_window, name) != null ? (object)_window : View; }
        private object FindOptionalField(string name)
        {
            object target = Target(name);
            var field = Field(target, name);
            return field == null ? null : field.GetValue(target);
        }
        private T Read<T>(string name) { return (T)Field(Target(name), name).GetValue(Target(name)); }
        private void Write(string name, object value) { Field(Target(name), name).SetValue(Target(name), value); }
        private object Invoke(string name, params object[] args)
        {
            foreach (object target in new[] { (object)_window, View })
                foreach (var method in target.GetType().GetMethods(Flags))
                    if (method.Name == name && method.GetParameters().Length == args.Length)
                        return method.Invoke(target, args);
            throw new MissingMethodException(name);
        }
        private void RefreshCurrentList() { ((StickyNoteWindow.StickyListContentView)View).RefreshList(); }
        private void ApplyListFontSize(float points) { ((StickyNoteWindow.StickyListContentView)View).ApplyListFontSize(points); }
        private bool HasOnlyCurrentContentTree(int kind)
        {
            var layout = Read<WC.Grid>("_layout");
            int bodies = 0;
            foreach (W.UIElement element in layout.Children)
                if (WC.Grid.GetRow(element) == 3) bodies++;
            if (bodies != 1) return false;
            return kind == 0 ? View is StickyNoteWindow.StickyTextContentView :
                kind == 1 ? View is StickyNoteWindow.StickyTodoContentView :
                View is StickyNoteWindow.StickyScheduleContentView;
        }
        private StickyAppearanceDialog _appearanceDialog
        { get { return Read<StickyAppearanceDialog>("_appearanceDialog"); } set { Write("_appearanceDialog", value); } }
        private WC.Button _boldButton
        { get { return Read<WC.Button>("_boldButton"); } set { Write("_boldButton", value); } }
        private int _dockDividerMaximumHeight
        { get { return Read<int>("_dockDividerMaximumHeight"); } set { Write("_dockDividerMaximumHeight", value); } }
        private int _dockDividerMinimumHeight
        { get { return Read<int>("_dockDividerMinimumHeight"); } set { Write("_dockDividerMinimumHeight", value); } }
        private bool _dockGrouped
        { get { return Read<bool>("_dockGrouped"); } set { Write("_dockGrouped", value); } }
        private bool _dockResizeBottom
        { get { return Read<bool>("_dockResizeBottom"); } set { Write("_dockResizeBottom", value); } }
        private bool _dockResizeTop
        { get { return Read<bool>("_dockResizeTop"); } set { Write("_dockResizeTop", value); } }
        private bool _dockSplitBottom
        { get { return Read<bool>("_dockSplitBottom"); } set { Write("_dockSplitBottom", value); } }
        private WC.RichTextBox _editor
        { get { return Read<WC.RichTextBox>("_editor"); } set { Write("_editor", value); } }
        private WC.ComboBox _fontFamilyBox
        { get { return Read<WC.ComboBox>("_fontFamilyBox"); } set { Write("_fontFamilyBox", value); } }
        private WC.ComboBox _fontSizeBox
        { get { return Read<WC.ComboBox>("_fontSizeBox"); } set { Write("_fontSizeBox", value); } }
        private WC.Grid _formatToolbar
        { get { return Read<WC.Grid>("_formatToolbar"); } set { Write("_formatToolbar", value); } }
        private bool _initializing
        { get { return Read<bool>("_initializing"); } set { Write("_initializing", value); } }
        private System.Collections.ICollection _ordinaryLinkRanges
        { get { return Read<System.Collections.ICollection>("_ordinaryLinkRanges"); } set { Write("_ordinaryLinkRanges", value); } }
        private int _reminderBannerRebuildCount
        { get { return Read<int>("_reminderBannerRebuildCount"); } set { Write("_reminderBannerRebuildCount", value); } }
        private WC.ListBox _reminderList
        { get { return Read<WC.ListBox>("_reminderList"); } set { Write("_reminderList", value); } }
        private WC.StackPanel _scheduleRows
        { get { return Read<WC.StackPanel>("_scheduleRows"); } set { Write("_scheduleRows", value); } }
        private ReminderItem _selectedReminder
        { get { return Read<ReminderItem>("_selectedReminder"); } set { Write("_selectedReminder", value); } }
        private WC.StackPanel _todoRows
        { get { return Read<WC.StackPanel>("_todoRows"); } set { Write("_todoRows", value); } }
        private void RefreshOrdinaryLinks(bool saveAfterFormatting) { Invoke("RefreshOrdinaryLinks" , saveAfterFormatting); }
        private void ShowAppearanceDialog() { Invoke("ShowAppearanceDialog"); }
        private void PinSchedule(StickyScheduleItem item) { Invoke("PinSchedule" , item); }
        private void PersistNow() { Invoke("PersistNow"); }
        private void PersistNow(bool markModified, bool notifyChanged) { Invoke("PersistNow" , markModified, notifyChanged); }
        private void ApplyColors() { Invoke("ApplyColors"); }
        private void SetDockResizeRole(bool grouped, bool resizeTop, bool resizeBottom) { Invoke("SetDockResizeRole" , grouped, resizeTop, resizeBottom); }
        private void SetDockResizeRole(bool grouped, bool resizeTop, bool resizeBottom, bool splitBottom) { Invoke("SetDockResizeRole" , grouped, resizeTop, resizeBottom, splitBottom); }
        private void SetDockResizeRole(bool grouped, bool resizeTop, bool resizeBottom, bool splitBottom, int dividerMinimumHeight, int dividerMaximumHeight) { Invoke("SetDockResizeRole" , grouped, resizeTop, resizeBottom, splitBottom, dividerMinimumHeight, dividerMaximumHeight); }
        private void ApplyTopMostWindowState(bool alwaysOnTop) { Invoke("ApplyTopMostWindowState" , alwaysOnTop); }
        private double PointSizeToDip(float points) { return (double)Invoke("PointSizeToDip" , points); }
        private void SetEditorPlainText(string text) { Invoke("SetEditorPlainText" , text); }
        private string EditorPlainText() { return (string)Invoke("EditorPlainText"); }
        private void CaptureEditorContent() { Invoke("CaptureEditorContent"); }
        private void ApplySelectionFontFamily(string familyName) { Invoke("ApplySelectionFontFamily" , familyName); }
        private void ApplySelectionFontSize(float points) { Invoke("ApplySelectionFontSize" , points); }
        private void ApplyEmptyEditorTypingDefaults() { Invoke("ApplyEmptyEditorTypingDefaults"); }
        private void SaveEditorSelection() { Invoke("SaveEditorSelection"); }
        private void UpdateReminderBanner(IEnumerable<ReminderItem> reminders) { Invoke("UpdateReminderBanner" , reminders); }
        private void PreviewReminderFontSize(ReminderItem reminder, float points) { Invoke("PreviewReminderFontSize" , reminder, points); }
        private void ExecuteSelectedReminderDelete() { Invoke("ExecuteSelectedReminderDelete"); }
        private void ExecuteSelectedReminderModify() { Invoke("ExecuteSelectedReminderModify"); }
        private void AddBlankTodoAndEdit() { Invoke("AddBlankTodoAndEdit"); }
        private WC.TextBox FindTodoEditor(StickyTodoItem item) { return (WC.TextBox)Invoke("FindTodoEditor" , item); }
        private void RefreshTodoList() { Invoke("RefreshTodoList"); }
        private StickyTodoState NextTodoState(StickyTodoState state) { return (StickyTodoState)Invoke("NextTodoState" , state); }
        private void BeginTodoInlineEdit(WC.TextBox editor, int caretIndex) { Invoke("BeginTodoInlineEdit" , editor, caretIndex); }
        private void CommitTodoInlineEdit(StickyTodoItem item, WC.TextBox editor, string originalText) { Invoke("CommitTodoInlineEdit" , item, editor, originalText); }
        internal bool ExerciseOrdinaryLinkRefreshForTest()
        {
            if (_window.Data.IsTodoList || _window.Data.IsSchedule) return false;
            RefreshOrdinaryLinks(false);
            bool detected = _ordinaryLinkRanges.Count == 2;
            SetEditorPlainText("普通文字");
            RefreshOrdinaryLinks(false);
            return detected && _ordinaryLinkRanges.Count == 0;
        }
        internal void OpenAppearanceDialogForTest()
        {
            ShowAppearanceDialog();
        }

        internal bool ExerciseAppearanceCloseStressForTest(int cycles)
        {
            int count = Math.Max(1, cycles);
            for (int index = 0; index < count; index++)
            {
                ShowAppearanceDialog();
                WF.Application.DoEvents();
                if (_appearanceDialog == null || _appearanceDialog.IsDisposed)
                    return false;
                _appearanceDialog.Close();
                WF.Application.DoEvents();
                if (_appearanceDialog != null && !_appearanceDialog.IsDisposed)
                    return false;
            }
            return true;
        }

        internal bool ExerciseRichTextFormattingForTest()
        {
            _initializing = true;
            SetEditorPlainText("格式测试");
            _initializing = false;
            _editor.SelectAll();
            ApplySelectionFontFamily("Microsoft YaHei UI");
            ApplySelectionFontSize(18F);
            EditingCommands.ToggleBold.Execute(null, _editor);
            EditingCommands.ToggleItalic.Execute(null, _editor);
            EditingCommands.ToggleUnderline.Execute(null, _editor);
            CaptureEditorContent();
            return _window.Data.Text == "格式测试" &&
                !String.IsNullOrEmpty(_window.Data.RichTextRtf) &&
                _window.Data.FontSizeTwips == 360;
        }

        internal bool ExerciseSmoothFormatInteractionForTest()
        {
            _initializing = true;
            SetEditorPlainText("从后往前选择文字");
            _initializing = false;
            TextPointer start = _editor.Document.ContentStart.GetPositionAtOffset(2);
            TextPointer end = _editor.Document.ContentStart.GetPositionAtOffset(6);
            if (start == null || end == null) return false;
            _editor.Selection.Select(start, end);
            ApplySelectionFontSize(24F);
            return !_editor.Selection.IsEmpty && _window.Data.FontSizeTwips == 480;
        }

        internal bool ExerciseFirstFormatCommitForTest()
        {
            _initializing = true;
            SetEditorPlainText("First format commit");
            _initializing = false;
            _editor.SelectAll();
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(24F);
            return _window.Data.FontSizeTwips == 480 &&
                String.Equals(_window.Data.FontFamilyName, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseEmptyNoteFormattingForTest()
        {
            _initializing = true;
            SetEditorPlainText(String.Empty);
            _initializing = false;
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(22F);
            Block first = _editor.Document.Blocks.FirstBlock;
            if (first == null) return false;
            ApplyEmptyEditorTypingDefaults();
            _editor.CaretPosition.InsertTextInRun("A");
            TextRange typed = new TextRange(first.ContentStart,
                first.ContentEnd);
            object typedSize = typed.GetPropertyValue(
                TextElement.FontSizeProperty);
            System.Windows.Media.FontFamily typedFamily =
                typed.GetPropertyValue(TextElement.FontFamilyProperty) as
                    System.Windows.Media.FontFamily;
            return EditorPlainText() == "A" &&
                _window.Data.FontSizeTwips == 440 &&
                String.Equals(_window.Data.FontFamilyName, "Arial",
                    StringComparison.CurrentCultureIgnoreCase) &&
                first != null && Math.Abs(first.FontSize -
                    PointSizeToDip(22F)) < 0.1 &&
                first.FontFamily != null && String.Equals(
                    first.FontFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase) &&
                typedSize is double && Math.Abs((double)typedSize -
                    PointSizeToDip(22F)) < 0.1 && typedFamily != null &&
                String.Equals(typedFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseCaretTypingFormatSwitchForTest()
        {
            _initializing = true;
            SetEditorPlainText("已有正文");
            _initializing = false;
            Paragraph paragraph = _editor.Document.Blocks.FirstBlock as Paragraph;
            if (paragraph == null) return false;
            TextPointer caret = paragraph.ContentEnd.GetInsertionPosition(
                LogicalDirection.Backward);
            _editor.Selection.Select(caret, caret);
            SaveEditorSelection();
            ApplySelectionFontFamily("Arial");
            ApplySelectionFontSize(22F);
            _editor.Selection.Text = "Z";
            TextRange typed = FindLastTextRange("Z");
            object typedSize = typed == null ? null : typed.GetPropertyValue(
                TextElement.FontSizeProperty);
            System.Windows.Media.FontFamily typedFamily = typed == null ? null :
                typed.GetPropertyValue(TextElement.FontFamilyProperty) as
                    System.Windows.Media.FontFamily;
            return EditorPlainText().EndsWith("Z", StringComparison.Ordinal) &&
                typedSize is double && Math.Abs((double)typedSize -
                    PointSizeToDip(22F)) < 0.1 && typedFamily != null &&
                String.Equals(typedFamily.Source, "Arial",
                    StringComparison.CurrentCultureIgnoreCase);
        }

        internal bool ExerciseSingleNativeImeCommitAfterFormatForTest()
        {
            _initializing = true;
            SetEditorPlainText("吃饭了没");
            _initializing = false;
            Paragraph paragraph = _editor.Document.Blocks.FirstBlock as Paragraph;
            if (paragraph == null) return false;
            TextPointer caret = paragraph.ContentEnd.GetInsertionPosition(
                LogicalDirection.Backward);
            _editor.Selection.Select(caret, caret);
            SaveEditorSelection();
            ApplySelectionFontFamily("Arial");
            _editor.Selection.Text = "宝贝";
            string value = EditorPlainText();
            return value == "吃饭了没宝贝" &&
                value.IndexOf("宝贝", StringComparison.Ordinal) ==
                value.LastIndexOf("宝贝", StringComparison.Ordinal);
        }

        private TextRange FindLastTextRange(string expected)
        {
            if (String.IsNullOrEmpty(expected)) return null;
            TextPointer cursor = _editor.Document.ContentStart;
            TextRange result = null;
            while (cursor != null && cursor.CompareTo(
                _editor.Document.ContentEnd) < 0)
            {
                if (cursor.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    string value = cursor.GetTextInRun(LogicalDirection.Forward);
                    int index = value.LastIndexOf(expected,
                        StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        TextPointer start = cursor.GetPositionAtOffset(index,
                            LogicalDirection.Forward);
                        TextPointer end = start == null ? null :
                            start.GetPositionAtOffset(expected.Length,
                                LogicalDirection.Forward);
                        if (start != null && end != null)
                            result = new TextRange(start, end);
                    }
                }
                cursor = cursor.GetNextContextPosition(
                    LogicalDirection.Forward);
            }
            return result;
        }

        internal bool ExerciseUnifiedNoteContextMenusForTest()
        {
            string[] required = { "新建便利贴", "新建待办清单", "新建日程",
                "重命名", "取消提醒", "颜色与透明度", "置顶 / 取消置顶",
                "删除此便利贴", "收起到侧边页签" };
            foreach (string header in required)
            {
                if (!ContextMenuContainsHeader(_window.ContextMenu, header) ||
                    !ContextMenuContainsHeader(_editor.ContextMenu, header))
                    return false;
            }
            return !ContextMenuContainsHeader(_window.ContextMenu, "删除便利贴") &&
                !ContextMenuContainsHeader(_editor.ContextMenu, "删除便利贴") &&
                ContextMenuContainsHeader(_editor.ContextMenu, "撤销") &&
                ContextMenuContainsHeader(_editor.ContextMenu, "全选");
        }

        private static bool ContextMenuContainsHeader(WC.ContextMenu menu,
            string expected)
        {
            if (menu == null) return false;
            foreach (object value in menu.Items)
            {
                WC.MenuItem item = value as WC.MenuItem;
                if (item != null && String.Equals(Convert.ToString(item.Header),
                    expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal bool ExerciseSchedulePinMarkerForTest()
        {
            _window.Data.ScheduleItems.Clear();
            StickyScheduleItem item = new StickyScheduleItem("圆点只显示一次",
                DateTime.Today.AddDays(2));
            _window.Data.ScheduleItems.Add(item);
            RefreshCurrentList();
            PinSchedule(item);
            bool pinned = item.IsPinned && item.Text == "圆点只显示一次" &&
                ScheduleRowNameForTest() == "• 圆点只显示一次";
            PinSchedule(item);
            bool unpinned = !item.IsPinned && item.Text == "圆点只显示一次" &&
                ScheduleRowNameForTest() == "圆点只显示一次";
            return pinned && unpinned;
        }

        private string ScheduleRowNameForTest()
        {
            if (_scheduleRows.Children.Count == 0) return String.Empty;
            WC.Border border = _scheduleRows.Children[0] as WC.Border;
            WC.Grid grid = border == null ? null : border.Child as WC.Grid;
            WC.StackPanel details = grid == null || grid.Children.Count == 0
                ? null : grid.Children[0] as WC.StackPanel;
            WC.TextBlock name = details == null || details.Children.Count == 0
                ? null : details.Children[0] as WC.TextBlock;
            return name == null ? String.Empty : name.Text;
        }

        internal bool ExerciseFixedNoteTypeActionsForTest()
        {
            for (int kind = 0; kind < 3; kind++)
            using (var note = new StickyNoteWindow(new StickyNoteData
                { IsTodoList = kind == 1, IsSchedule = kind == 2 }))
            {
                var driver = new StickyWindowInteractionDriver(note);
                if (!driver.HasOnlyCurrentContentTree(kind) || note.HasInlineCreationButtonsForTest)
                    return false;
            }
            return true;
        }

        internal bool ExerciseMultilingualInputForTest()
        {
            const string multilingual = "English 日本語 中文 한국어 Русский العربية Français 🙂";
            bool enabled = InputMethod.GetIsInputMethodEnabled(_editor) &&
                !InputMethod.GetIsInputMethodSuspended(_editor);
            _initializing = true;
            SetEditorPlainText(multilingual);
            _initializing = false;
            CaptureEditorContent();
            using (var todo = new StickyNoteWindow(new StickyNoteData { IsTodoList = true }))
            {
                var driver = new StickyWindowInteractionDriver(todo);
                driver.AddBlankTodoAndEdit();
                WC.TextBox input = driver.FindTodoEditor(todo.Data.TodoItems[0]);
                input.Text = multilingual;
                return enabled && _window.Data.Text == multilingual && _editor.AcceptsReturn &&
                    InputMethod.GetIsInputMethodEnabled(input) &&
                    !InputMethod.GetIsInputMethodSuspended(input) && input.Text == multilingual;
            }
        }

        internal bool ExerciseReminderSwitchContentPreservationForTest()
        {
            _initializing = true;
            SetEditorPlainText("修改字体字号后仍需保留的正文");
            _initializing = false;
            CaptureEditorContent();
            PersistNow();
            return _window.Data.Text == "修改字体字号后仍需保留的正文" &&
                !String.IsNullOrEmpty(_window.Data.RichTextRtf);
        }

        internal bool ExerciseReminderSelectionActionsForTest()
        {
            ReminderItem first = new ReminderItem(DateTime.UtcNow.AddMinutes(20),
                new string('提', 50));
            ReminderItem second = new ReminderItem(DateTime.UtcNow.AddMinutes(40),
                "第二条可管理提醒");
            UpdateReminderBanner(new ReminderItem[] { first, second });
            _reminderList.SelectedIndex = 1;
            ReminderItem modified = null;
            ReminderItem deleted = null;
            _window.ModifyReminderRequested += delegate(object sender,
                ReminderActionEventArgs e) { modified = e.Reminder; };
            _window.DeleteReminderRequested += delegate(object sender,
                ReminderActionEventArgs e) { deleted = e.Reminder; };
            ExecuteSelectedReminderModify();
            ExecuteSelectedReminderDelete();
            return Object.ReferenceEquals(_selectedReminder, second) &&
                Object.ReferenceEquals(modified, second) &&
                Object.ReferenceEquals(deleted, second);
        }

        internal bool ExerciseInlineCreationActionsRemovedForTest()
        {
            return !_window.HasInlineCreationButtonsForTest;
        }

        internal bool ExerciseReminderFirstClickStabilityForTest(
            out bool blankAreaClearsSelection, out bool refreshesInPlace)
        {
            ReminderItem first = new ReminderItem(DateTime.UtcNow.AddMinutes(15),
                "首次点击必须稳定保留的提醒");
            ReminderItem second = new ReminderItem(DateTime.UtcNow.AddMinutes(30),
                "第二条提醒");
            ReminderItem[] items = new ReminderItem[] { first, second };
            UpdateReminderBanner(items);
            int rebuildCount = _reminderBannerRebuildCount;
            _reminderList.SelectedIndex = 0;
            bool selected = Object.ReferenceEquals(_selectedReminder, first);
            UpdateReminderBanner(items);
            refreshesInPlace = _reminderBannerRebuildCount == rebuildCount;
            bool preserved = Object.ReferenceEquals(_selectedReminder, first);
            _reminderList.SelectedItem = null;
            blankAreaClearsSelection = _selectedReminder == null;
            return selected && preserved && refreshesInPlace;
        }

        internal bool ExerciseTodoWrapAndInlineEditForTest()
        {
            if (!_window.Data.IsTodoList)
            {
                using (var note = new StickyNoteWindow(new StickyNoteData { IsTodoList = true }))
                    return new StickyWindowInteractionDriver(note).ExerciseTodoWrapAndInlineEditForTest();
            }

            _window.Data.TodoItems.Clear();
            StickyTodoItem item = new StickyTodoItem(new string('待', 50), false);
            _window.Data.TodoItems.Add(item);
            RefreshTodoList();
            WC.Border firstRow = null;
            foreach (W.UIElement element in _todoRows.Children)
            {
                WC.Border border = element as WC.Border;
                if (border != null) { firstRow = border; break; }
            }
            if (firstRow == null) return false;
            WC.Grid grid = firstRow.Child as WC.Grid;
            WC.TextBox editor = grid == null ? null : grid.Children[1] as WC.TextBox;
            if (editor == null) return false;
            bool readOnlyUntilDoubleClick = editor.IsReadOnly && !editor.Focusable;
            BeginTodoInlineEdit(editor, 7);
            bool preservesTextWithoutAutoSelection =
                editor.Text == item.Text && editor.SelectionStart == 7 &&
                editor.SelectionLength == 0;
            editor.Text = "双击修改成功";
            CommitTodoInlineEdit(item, editor, item.Text);
            return readOnlyUntilDoubleClick && preservesTextWithoutAutoSelection &&
                item.Text == "双击修改成功" &&
                editor.IsReadOnly && !editor.Focusable &&
                editor.TextWrapping == W.TextWrapping.Wrap;
        }

        internal bool ExerciseTodoOverallFontSizeForTest()
        {
            if (!_window.Data.IsTodoList)
            {
                using (var note = new StickyNoteWindow(new StickyNoteData { IsTodoList = true }))
                    return new StickyWindowInteractionDriver(note).ExerciseTodoOverallFontSizeForTest();
            }

            _window.Data.TodoItems.Clear();
            _window.Data.TodoItems.Add(new StickyTodoItem("整体字号测试", false));
            RefreshCurrentList();
            ApplyListFontSize(48F);
            WC.Border firstRow = null;
            foreach (W.UIElement element in _todoRows.Children)
            {
                WC.Border border = element as WC.Border;
                if (border != null) { firstRow = border; break; }
            }
            WC.Grid grid = firstRow == null ? null : firstRow.Child as WC.Grid;
            WC.TextBox editor = grid == null ? null : grid.Children[1] as WC.TextBox;
            double expected = PointSizeToDip(48F);
            return _window.Data.FontSizeTwips == 960 && editor != null &&
                Math.Abs(editor.FontSize - expected) < 0.1 &&
                _formatToolbar.Visibility == W.Visibility.Visible &&
                FindOptionalField("_fontFamilyBox") == null &&
                _fontSizeBox.Visibility == W.Visibility.Visible &&
                _fontSizeBox.Items.Count == 5 &&
                Convert.ToString(_fontSizeBox.Items[0]) == "特小 9" &&
                Convert.ToString(_fontSizeBox.Items[1]) == "小 10.5" &&
                Convert.ToString(_fontSizeBox.Items[2]) == "中 16" &&
                Convert.ToString(_fontSizeBox.Items[3]) == "大 22" &&
                Convert.ToString(_fontSizeBox.Items[4]) == "特大 48" &&
                FindOptionalField("_boldButton") == null;
        }

        internal bool ExerciseDedicatedRowContextMenusForTest()
        {
            if (!_window.Data.IsTodoList)
            {
                using (var note = new StickyNoteWindow(new StickyNoteData { IsTodoList = true }))
                    return new StickyWindowInteractionDriver(note).ExerciseDedicatedRowContextMenusForTest();
            }

            _window.Data.TodoItems.Clear();
            _window.Data.TodoItems.Add(new StickyTodoItem("右键菜单测试", false));
            RefreshCurrentList();
            WC.Border todoRow = null;
            foreach (W.UIElement child in _todoRows.Children)
            {
                todoRow = child as WC.Border;
                if (todoRow != null) break;
            }
            bool todoMenuOk = todoRow != null && todoRow.ContextMenu != null &&
                todoRow.ContextMenu.Items.Count == 6 &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[0]).Header)
                    == "编辑待办" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[1]).Header)
                    == "设为未完成" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[2]).Header)
                    == "设为进行中" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[3]).Header)
                    == "设为已完成" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[4]).Header)
                    == "删除待办" &&
                Convert.ToString(((WC.MenuItem)todoRow.ContextMenu.Items[5]).Header)
                    == "置顶待办";
            WC.Grid todoGrid = todoRow == null ? null : todoRow.Child as WC.Grid;
            WC.CheckBox todoCheck = todoGrid == null ? null :
                todoGrid.Children[0] as WC.CheckBox;
            WC.TextBox todoEditor = todoGrid == null ? null :
                todoGrid.Children[1] as WC.TextBox;
            todoMenuOk = todoMenuOk && todoCheck != null &&
                todoEditor != null && Object.ReferenceEquals(
                    todoCheck.ContextMenu, todoRow.ContextMenu) &&
                Object.ReferenceEquals(todoEditor.ContextMenu,
                    todoRow.ContextMenu) &&
                NextTodoState(StickyTodoState.Pending) ==
                    StickyTodoState.InProgress &&
                NextTodoState(StickyTodoState.InProgress) ==
                    StickyTodoState.Completed &&
                NextTodoState(StickyTodoState.Completed) ==
                    StickyTodoState.Pending;
            UpdateReminderBanner(new ReminderItem[] {
                new ReminderItem(DateTime.UtcNow.AddMinutes(5), "右键提醒测试") });
            WC.ListBoxItem reminderRow = _reminderList.Items.Count == 0
                ? null : _reminderList.Items[0] as WC.ListBoxItem;
            bool reminderMenuOk = reminderRow != null &&
                reminderRow.ContextMenu != null &&
                reminderRow.ContextMenu.Items.Count == 2 &&
                Convert.ToString(((WC.MenuItem)reminderRow.ContextMenu.Items[0]).Header)
                    == "编辑提醒" &&
                Convert.ToString(((WC.MenuItem)reminderRow.ContextMenu.Items[1]).Header)
                    == "删除提醒";
            return todoMenuOk && reminderMenuOk;
        }

        internal bool ExerciseBodyTextColorSwitchForTest()
        {
            _editor.Document.Blocks.Clear();
            Paragraph paragraph = new Paragraph(new Run("正文颜色切换"));
            paragraph.Foreground = System.Windows.Media.Brushes.Black;
            _editor.Document.Blocks.Add(paragraph);
            _window.Data.TextColorArgb = Color.White.ToArgb();
            ApplyColors();
            TextRange whiteRange = new TextRange(
                _editor.Document.ContentStart, _editor.Document.ContentEnd);
            SolidColorBrush white = whiteRange.GetPropertyValue(
                TextElement.ForegroundProperty) as SolidColorBrush;
            _window.Data.TextColorArgb = Color.Black.ToArgb();
            ApplyColors();
            TextRange blackRange = new TextRange(
                _editor.Document.ContentStart, _editor.Document.ContentEnd);
            SolidColorBrush black = blackRange.GetPropertyValue(
                TextElement.ForegroundProperty) as SolidColorBrush;
            return white != null && black != null &&
                white.Color == System.Windows.Media.Colors.White &&
                black.Color == System.Windows.Media.Colors.Black;
        }

        internal bool ExerciseReminderLiveSizePreviewForTest()
        {
            ReminderItem reminder = new ReminderItem(
                DateTime.UtcNow.AddMinutes(5), "实时字号", null, 10.5F, false);
            UpdateReminderBanner(new ReminderItem[] { reminder });
            PreviewReminderFontSize(reminder, 22F);
            bool preview = Math.Abs(_window.ReminderBannerFirstFontSize - 22F) < 0.2F;
            UpdateReminderBanner(new ReminderItem[] { reminder });
            bool restored = Math.Abs(_window.ReminderBannerFirstFontSize - 10.5F) < 0.2F;
            return preview && restored;
        }

        internal bool ExerciseDockResizeRoleForTest()
        {
            SetDockResizeRole(true, true, true, true, 250, 430);
            bool top = _dockGrouped && _dockResizeTop && _dockResizeBottom &&
                _dockSplitBottom && _dockDividerMinimumHeight == 250 &&
                _dockDividerMaximumHeight == 430;
            SetDockResizeRole(true, false, true, false);
            bool bottom = _dockGrouped && !_dockResizeTop && _dockResizeBottom &&
                !_dockSplitBottom;
            SetDockResizeRole(false, false, false);
            bool standalone = !_dockGrouped && _dockResizeTop &&
                _dockResizeBottom && !_dockSplitBottom;
            bool leftEdgeKeepsRightFixed =
                StickyDockGeometry.CalculatePhysicalHorizontalResizeTarget(200, 700, true, 280, 900).Left == 200;
            bool rightEdgeKeepsLeftFixed =
                StickyDockGeometry.CalculatePhysicalHorizontalResizeTarget(300, 800, false, 280, 900).Left == 300;
            return top && bottom && standalone &&
                leftEdgeKeepsRightFixed && rightEdgeKeepsLeftFixed;
        }

        internal bool ExerciseGroupTopMostForTest()
        {
            _window.Data.AlwaysOnTop = false;
            ApplyTopMostWindowState(false);
            bool unpinned = !_window.Data.AlwaysOnTop && !_window.Topmost &&
                _window.CurrentPinActionText == "置顶";
            _window.Data.AlwaysOnTop = true;
            ApplyTopMostWindowState(true);
            bool pinned = _window.Data.AlwaysOnTop && _window.Topmost &&
                _window.CurrentPinActionText == "取消置顶";
            return unpinned && pinned;
        }
    }
}
