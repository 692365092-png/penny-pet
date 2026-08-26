using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    // HIGH RISK: RichTextBox, focus and IME compatibility code.
    // Keep event ordering and composition behavior unchanged unless the full
    // Windows IME regression suite and real Chinese IME checks are repeated.
    internal sealed partial class StickyNoteForm
    {
        private WC.Grid BuildFormatToolbar(out WC.ComboBox familyBox,
            out WC.ComboBox sizeBox, out WC.Button boldButton,
            out WC.Button italicButton, out WC.Button underlineButton)
        {
            WC.Grid toolbar = new WC.Grid();
            toolbar.Height = 32;
            toolbar.Margin = new W.Thickness(4, 0, 4, 0);
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(1,
                W.GridUnitType.Star)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(88)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(34)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(34)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(34)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(0)));
            toolbar.ColumnDefinitions.Add(Column(new W.GridLength(0)));

            familyBox = new WC.ComboBox();
            familyBox.Margin = new W.Thickness(0, 3, 4, 3);
            familyBox.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            familyBox.FontSize = PointSizeToDip(9F);
            familyBox.Foreground = System.Windows.Media.Brushes.Black;
            familyBox.IsTextSearchEnabled = true;
            familyBox.IsSynchronizedWithCurrentItem = false;
            WC.VirtualizingStackPanel.SetIsVirtualizing(familyBox, true);
            WC.VirtualizingStackPanel.SetVirtualizationMode(familyBox,
                WC.VirtualizationMode.Recycling);
            foreach (string family in InstalledFontNames())
                familyBox.Items.Add(family);
            WC.ComboBox familySelector = familyBox;
            familyBox.PreviewMouseLeftButtonDown += delegate
            {
                // Preserve a RichTextBox selection before the selector takes
                // keyboard focus.  The following SelectionChanged can then
                // format exactly the highlighted text.
                SaveEditorSelection();
            };
            familyBox.PreviewMouseWheel += delegate(object sender,
                MouseWheelEventArgs e)
            {
                if (familySelector.IsDropDownOpen ||
                    familySelector.Items.Count == 0) return;
                SaveEditorSelection();
                int current = familySelector.SelectedIndex;
                if (current < 0) current = 0;
                int next = Math.Max(0, Math.Min(
                    familySelector.Items.Count - 1,
                    current + (e.Delta > 0 ? -1 : 1)));
                if (next != familySelector.SelectedIndex)
                    familySelector.SelectedIndex = next;
                e.Handled = true;
            };
            familyBox.DropDownOpened += delegate
            {
                SaveEditorSelection();
            };
            familyBox.DropDownClosed += delegate
            {
                RestoreEditorFocusAfterToolbarChoice();
            };
            familyBox.SelectionChanged += delegate
            {
                if (_updatingFormatToolbar || familySelector.SelectedItem == null) return;
                ApplySelectionFontFamily(Convert.ToString(familySelector.SelectedItem));
                if (!familySelector.IsDropDownOpen)
                    RestoreEditorFocusAfterToolbarChoice();
            };

            sizeBox = new WC.ComboBox();
            sizeBox.Margin = new W.Thickness(0, 3, 4, 3);
            sizeBox.FontFamily = new System.Windows.Media.FontFamily(
                "Microsoft YaHei UI");
            sizeBox.FontSize = PointSizeToDip(9F);
            sizeBox.Foreground = System.Windows.Media.Brushes.Black;
            sizeBox.IsTextSearchEnabled = true;
            sizeBox.IsSynchronizedWithCurrentItem = false;
            WC.VirtualizingStackPanel.SetIsVirtualizing(sizeBox, true);
            WC.VirtualizingStackPanel.SetVirtualizationMode(sizeBox,
                WC.VirtualizationMode.Recycling);
            AddFontSizeOptions(sizeBox, false);
            WC.ComboBox sizeSelector = sizeBox;
            sizeBox.PreviewMouseLeftButtonDown += delegate
            {
                SaveEditorSelection();
            };
            sizeBox.PreviewMouseWheel += delegate(object sender,
                MouseWheelEventArgs e)
            {
                if (sizeSelector.IsDropDownOpen ||
                    sizeSelector.Items.Count == 0) return;
                SaveEditorSelection();
                int current = sizeSelector.SelectedIndex;
                if (current < 0) current = 0;
                int next = Math.Max(0, Math.Min(
                    sizeSelector.Items.Count - 1,
                    current + (e.Delta > 0 ? -1 : 1)));
                if (next != sizeSelector.SelectedIndex)
                    sizeSelector.SelectedIndex = next;
                e.Handled = true;
            };
            sizeBox.DropDownOpened += delegate
            {
                SaveEditorSelection();
            };
            sizeBox.DropDownClosed += delegate
            {
                RestoreEditorFocusAfterToolbarChoice();
            };
            sizeBox.SelectionChanged += delegate
            {
                if (_updatingFormatToolbar || sizeSelector.SelectedItem == null) return;
                float points;
                if (TryParseFontSize(Convert.ToString(sizeSelector.SelectedItem),
                    out points))
                {
                    if (Data.IsTodoList) ApplyTodoFontSize(points);
                    else if (Data.IsSchedule) ApplyScheduleFontSize(points);
                    else ApplySelectionFontSize(points);
                    if (!sizeSelector.IsDropDownOpen && !Data.IsTodoList &&
                        !Data.IsSchedule)
                        RestoreEditorFocusAfterToolbarChoice();
                }
            };

            boldButton = HeaderButton("B", 34, true);
            italicButton = HeaderButton("I", 34, false);
            italicButton.FontStyle = W.FontStyles.Italic;
            underlineButton = HeaderButton("U", 34, false);
            WC.TextBlock underlineText = new WC.TextBlock();
            underlineText.Text = "U";
            underlineText.TextDecorations = W.TextDecorations.Underline;
            underlineButton.Content = underlineText;
            // Formatting controls must not take keyboard focus away from the
            // RichTextBox.  Otherwise the first click can collapse/reroute the
            // selection before ToggleItalic/Bold/Underline runs.
            foreach (WC.Button formatButton in new WC.Button[] {
                boldButton, italicButton, underlineButton })
            {
                formatButton.Focusable = false;
                formatButton.IsTabStop = false;
            }
            boldButton.Click += delegate
            {
                EditingCommands.ToggleBold.Execute(null, _editor);
                ScheduleSave();
            };
            italicButton.Click += delegate
            {
                EditingCommands.ToggleItalic.Execute(null, _editor);
                ScheduleSave();
            };
            underlineButton.Click += delegate
            {
                EditingCommands.ToggleUnderline.Execute(null, _editor);
                ScheduleSave();
            };

            AddToGrid(toolbar, familyBox, 0);
            AddToGrid(toolbar, sizeBox, 1);
            AddToGrid(toolbar, boldButton, 2);
            AddToGrid(toolbar, italicButton, 3);
            AddToGrid(toolbar, underlineButton, 4);
            return toolbar;
        }

        private static void AddFontSizeOptions(WC.ComboBox sizeBox,
            bool compactListOnly)
        {
            sizeBox.Items.Clear();
            string[] values = compactListOnly
                ? new string[] { "特小 9", "小 10.5", "中 16", "大 22",
                    "特大 48" }
                : new string[] { "小五 9", "五号 10.5", "小四 12", "四号 14",
                    "小三 15", "三号 16", "小二 18", "二号 22", "小一 24",
                    "一号 26", "小初 36", "初号 42", "48", "56", "72" };
            foreach (string value in values) sizeBox.Items.Add(value);
        }

        private WC.ContextMenu BuildEditorMenu()
        {
            WC.ContextMenu menu = new WC.ContextMenu();
            AddCommandMenuItem(menu, "撤销", ApplicationCommands.Undo);
            AddCommandMenuItem(menu, "重做", ApplicationCommands.Redo);
            menu.Items.Add(new WC.Separator());
            AddCommandMenuItem(menu, "剪切", ApplicationCommands.Cut);
            AddCommandMenuItem(menu, "复制", ApplicationCommands.Copy);
            AddCommandMenuItem(menu, "粘贴", ApplicationCommands.Paste);
            AddCommandMenuItem(menu, "全选", ApplicationCommands.SelectAll);
            menu.Items.Add(new WC.Separator());
            AddNoteActions(menu);
            return menu;
        }

        private void AddCommandMenuItem(WC.ContextMenu menu, string text,
            ICommand command)
        {
            WC.MenuItem item = new WC.MenuItem();
            item.Header = text;
            item.Command = command;
            item.CommandTarget = _editor;
            menu.Items.Add(item);
        }

        private void LoadEditorContent()
        {
            _editor.Document = new FlowDocument();
            _editor.Document.PagePadding = new W.Thickness(0);
            _editor.Document.FontFamily = SafeWpfFontFamily(Data.FontFamilyName);
            _editor.Document.FontSize = PointSizeToDip(Data.FontSizeTwips / 20F);
            string rtf = StickyNoteRepository.NormalizeRtf(Data.RichTextRtf);
            if (!String.IsNullOrEmpty(rtf))
            {
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(rtf);
                    using (MemoryStream stream = new MemoryStream(bytes))
                    {
                        TextRange range = new TextRange(
                            _editor.Document.ContentStart,
                            _editor.Document.ContentEnd);
                        range.Load(stream, W.DataFormats.Rtf);
                    }
                    Data.Text = EditorPlainText();
                    return;
                }
                catch
                {
                    // A malformed/temporarily unreadable RTF payload should not
                    // be destroyed merely because this window failed to render
                    // it. Fall back to the plain-text projection in memory.
                }
            }
            SetEditorPlainText(Data.Text ?? String.Empty);
        }

        private void SetEditorPlainText(string text)
        {
            _editor.Document.Blocks.Clear();
            Paragraph paragraph = new Paragraph(new Run(text ?? String.Empty));
            paragraph.Margin = new W.Thickness(0);
            paragraph.FontFamily = SafeWpfFontFamily(Data.FontFamilyName);
            paragraph.FontSize = PointSizeToDip(Data.FontSizeTwips / 20F);
            _editor.Document.Blocks.Add(paragraph);
        }

        private string EditorPlainText()
        {
            string value = new TextRange(_editor.Document.ContentStart,
                _editor.Document.ContentEnd).Text ?? String.Empty;
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 2);
            return value;
        }

        private void CaptureEditorContent()
        {
            if (Data.IsTodoList || Data.IsSchedule) return;
            Data.Text = EditorPlainText();
            try
            {
                TextRange range = new TextRange(_editor.Document.ContentStart,
                    _editor.Document.ContentEnd);
                using (MemoryStream stream = new MemoryStream())
                {
                    range.Save(stream, W.DataFormats.Rtf);
                    Data.RichTextRtf = StickyNoteRepository.NormalizeRtf(
                        Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            catch (Exception error)
            {
                // Keep the last successfully captured RTF. Data.Text already
                // contains the current full plain-text projection, so a
                // transient serialization failure cannot erase note content.
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-rich-text-capture", error);
            }
        }

        private void EditorTextChanged(object sender,
            WC.TextChangedEventArgs e)
        {
            if (_initializing || _applyingAutoLinkFormat) return;
            _lastInputUtc = DateTime.UtcNow;
            Data.Text = EditorPlainText();
            RefreshTitle();
            QueueOrdinaryLinkRefresh();
            ScheduleSave();
        }

        private void EditorTextCompositionStarted(object sender,
            TextCompositionEventArgs e)
        {
            _editorTextCompositionActive = true;
            RaiseImeCompositionChanged(true);
        }

        private void EditorTextCompositionUpdated(object sender,
            TextCompositionEventArgs e)
        {
            _editorTextCompositionActive = true;
            RaiseImeCompositionChanged(true);
        }

        private void EditorTextCompositionCompleted(object sender,
            TextCompositionEventArgs e)
        {
            _editorTextCompositionActive = false;
            RaiseImeCompositionChanged(false);
            QueueOrdinaryLinkRefresh();
        }

        private void ApplySelectionFontFamily(string familyName)
        {
            if (String.IsNullOrWhiteSpace(familyName)) return;
            System.Windows.Media.FontFamily family = SafeWpfFontFamily(familyName);
            Data.FontFamilyName = family.Source;
            _typingFontFamilyName = family.Source;
            RestoreEditorSelection();
            _applyingTypingFormat = true;
            try
            {
                // This is the same native WPF formatting path used by mature
                // RichTextBox editors.  Never insert replacement text and
                // never touch the IME composition range.
                _editor.Selection.ApplyPropertyValue(
                    TextElement.FontFamilyProperty, family);
                if (String.IsNullOrEmpty(EditorPlainText()))
                    ApplyEmptyEditorTypingDefaultsCore();
            }
            finally { _applyingTypingFormat = false; }
            SaveEditorSelection();
            ScheduleSave();
        }

        private void ApplySelectionFontSize(float points)
        {
            points = Math.Max(6F, Math.Min(72F, points));
            double dip = PointSizeToDip(points);
            Data.FontSizeTwips = (int)Math.Round(points * 20F);
            _typingFontSizePoints = points;
            RestoreEditorSelection();
            _applyingTypingFormat = true;
            try
            {
                _editor.Selection.ApplyPropertyValue(
                    TextElement.FontSizeProperty, dip);
                if (String.IsNullOrEmpty(EditorPlainText()))
                    ApplyEmptyEditorTypingDefaultsCore();
            }
            finally { _applyingTypingFormat = false; }
            SaveEditorSelection();
            ScheduleSave();
        }

        private void ApplyEmptyEditorTypingDefaults()
        {
            if (_editor == null || _editor.Document == null ||
                !String.IsNullOrEmpty(EditorPlainText())) return;
            System.Windows.Media.FontFamily family = SafeWpfFontFamily(
                _typingFontFamilyName);
            double size = PointSizeToDip(_typingFontSizePoints);
            _applyingTypingFormat = true;
            try
            {
                ApplyEmptyEditorTypingDefaultsCore();
            }
            finally { _applyingTypingFormat = false; }
            SaveEditorSelection();
        }

        private void ApplyEmptyEditorTypingDefaultsCore()
        {
            System.Windows.Media.FontFamily family = SafeWpfFontFamily(
                _typingFontFamilyName);
            double size = PointSizeToDip(_typingFontSizePoints);
            _editor.FontFamily = family;
            _editor.FontSize = size;
            _editor.Document.FontFamily = family;
            _editor.Document.FontSize = size;
            Paragraph paragraph = _editor.Document.Blocks.FirstBlock as Paragraph;
            if (paragraph == null)
            {
                paragraph = new Paragraph();
                paragraph.Margin = new W.Thickness(0);
                _editor.Document.Blocks.Clear();
                _editor.Document.Blocks.Add(paragraph);
            }
            paragraph.FontFamily = family;
            paragraph.FontSize = size;
            TextPointer caret = paragraph.ContentStart;
            _editor.CaretPosition = caret;
            _editor.Selection.Select(caret, caret);
            _editor.Selection.ApplyPropertyValue(
                TextElement.FontFamilyProperty, family);
            _editor.Selection.ApplyPropertyValue(
                TextElement.FontSizeProperty, size);
        }

        private void EditorSelectionChanged(object sender, EventArgs e)
        {
            if (_applyingTypingFormat) return;
            SaveEditorSelection();
            // IME composition moves the caret several times for every
            // syllable.  Rebuilding the font ComboBox selection during those
            // moves is expensive and can disturb third-party candidate UI.
            if (_editorTextCompositionActive) return;
            if (_editor.IsKeyboardFocusWithin)
                CaptureTypingFormatFromSelection();
            RefreshFormatToolbar();
        }

        private void SaveEditorSelection()
        {
            if (_editor == null || _editor.Document == null) return;
            _savedSelectionStart = _editor.Selection.Start;
            _savedSelectionEnd = _editor.Selection.End;
        }

        private void RestoreEditorSelection()
        {
            if (_editor == null || _editor.Document == null ||
                _savedSelectionStart == null || _savedSelectionEnd == null)
                return;
            try
            {
                _applyingTypingFormat = true;
                _editor.Selection.Select(_savedSelectionStart,
                    _savedSelectionEnd);
            }
            catch (InvalidOperationException)
            {
                TextPointer caret = _editor.Document.ContentEnd;
                _editor.Selection.Select(caret, caret);
            }
            finally { _applyingTypingFormat = false; }
        }

        private void CaptureTypingFormatFromSelection()
        {
            object familyValue = _editor.Selection.GetPropertyValue(
                TextElement.FontFamilyProperty);
            System.Windows.Media.FontFamily family = familyValue as
                System.Windows.Media.FontFamily;
            if (family != null) _typingFontFamilyName = family.Source;
            object sizeValue = _editor.Selection.GetPropertyValue(
                TextElement.FontSizeProperty);
            if (sizeValue is double)
                _typingFontSizePoints = (float)((double)sizeValue * 72.0 / 96.0);
        }

        private void RestoreEditorFocusAfterToolbarChoice()
        {
            if (_restoreEditorFocusQueued) return;
            _restoreEditorFocusQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(delegate
            {
                _restoreEditorFocusQueued = false;
                if (_disposed || Data.IsTodoList || Data.IsSchedule) return;
                if (_editor.IsKeyboardFocusWithin)
                {
                    SaveEditorSelection();
                    return;
                }
                RestoreEditorSelection();
                _editor.Focus();
                Keyboard.Focus(_editor);
                SaveEditorSelection();
            }));
        }

        private void ApplyTodoFontSize(float points)
        {
            points = NormalizeScheduleFontSize(points);
            Data.FontSizeTwips = (int)Math.Round(points * 20F);
            _todoInput.FontSize = PointSizeToDip(points);
            RefreshTodoList();
            ScheduleSave();
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(RefreshFormatToolbar));
        }

        private void ApplyScheduleFontSize(float points)
        {
            points = NormalizeScheduleFontSize(points);
            Data.FontSizeTwips = (int)Math.Round(points * 20F);
            RefreshScheduleList();
            ScheduleSave();
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(RefreshFormatToolbar));
        }

        private void RefreshFormatToolbar()
        {
            if (_initializing || _updatingFormatToolbar) return;
            _updatingFormatToolbar = true;
            try
            {
                if (Data.IsTodoList)
                {
                    SelectComboText(_fontSizeBox,
                        ScheduleFontSizeLabel(Data.FontSizeTwips / 20F));
                    return;
                }
                if (Data.IsSchedule)
                {
                    SelectComboText(_fontSizeBox,
                        ScheduleFontSizeLabel(Data.FontSizeTwips / 20F));
                    return;
                }
                object familyValue = _editor.Selection.GetPropertyValue(
                    TextElement.FontFamilyProperty);
                System.Windows.Media.FontFamily family =
                    familyValue as System.Windows.Media.FontFamily;
                string familyName = family == null
                    ? Data.FontFamilyName : family.Source;
                SelectComboText(_fontFamilyBox, familyName);

                object sizeValue = _editor.Selection.GetPropertyValue(
                    TextElement.FontSizeProperty);
                double dip = sizeValue is double ? (double)sizeValue :
                    PointSizeToDip(Data.FontSizeTwips / 20F);
                SelectComboText(_fontSizeBox,
                    FormatFontSize((float)(dip * 72.0 / 96.0)));

                SetToggleButtonState(_boldButton,
                    _editor.Selection.GetPropertyValue(
                        TextElement.FontWeightProperty), W.FontWeights.Bold);
                SetToggleButtonState(_italicButton,
                    _editor.Selection.GetPropertyValue(
                        TextElement.FontStyleProperty), W.FontStyles.Italic);
                object decorations = _editor.Selection.GetPropertyValue(
                    Inline.TextDecorationsProperty);
                _underlineButton.Opacity = decorations != null &&
                    decorations != W.DependencyProperty.UnsetValue &&
                    !Object.Equals(decorations, null) ? 1.0 : 0.72;
            }
            finally { _updatingFormatToolbar = false; }
        }

        private static void SetToggleButtonState(WC.Button button,
            object current, object enabledValue)
        {
            button.Opacity = current != W.DependencyProperty.UnsetValue &&
                Object.Equals(current, enabledValue) ? 1.0 : 0.72;
        }

        private static void SelectComboText(WC.ComboBox combo, string value)
        {
            int match = -1;
            for (int index = 0; index < combo.Items.Count; index++)
            {
                string item = Convert.ToString(combo.Items[index]);
                if (String.Equals(item, value,
                    StringComparison.CurrentCultureIgnoreCase) ||
                    item.EndsWith(" " + value, StringComparison.Ordinal))
                {
                    match = index;
                    break;
                }
            }
            combo.SelectedIndex = match;
        }

    }
}
