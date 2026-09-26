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
        internal sealed class StickyTextContentView : StickyContentView
        {
            internal readonly WC.Grid _formatToolbar;
            internal readonly WC.ComboBox _fontFamilyBox;
            internal readonly WC.ComboBox _fontSizeBox;
            internal readonly WC.Button _boldButton;
            internal readonly WC.Button _italicButton;
            internal readonly WC.Button _underlineButton;
            internal readonly WC.RichTextBox _editor;
            internal bool _updatingFormatToolbar;
            internal int _appliedEditorTextColorArgb = Int32.MinValue;
            internal TextPointer _savedSelectionStart;
            internal TextPointer _savedSelectionEnd;
            internal string _typingFontFamilyName;
            internal float _typingFontSizePoints;
            internal bool _restoreEditorFocusQueued;
            internal bool _applyingTypingFormat;
            internal bool _editorTextCompositionActive;

            internal StickyTextContentView(StickyNoteWindow owner) : base(owner)
            {
                _typingFontFamilyName = StickyNoteCodec.NormalizeFontFamily(_owner.Data.FontFamilyName);
                _typingFontSizePoints = Math.Max(6F, Math.Min(72F, _owner.Data.FontSizeTwips / 20F));
                _formatToolbar = BuildFormatToolbar(out _fontFamilyBox,
                    out _fontSizeBox, out _boldButton, out _italicButton, out _underlineButton);
                _editor = new WC.RichTextBox();
                _editor.BorderThickness = new W.Thickness(0);
                _editor.Padding = new W.Thickness(8, 5, 8, 8);
                _editor.Background = System.Windows.Media.Brushes.Transparent;
                _editor.AcceptsTab = true;
                _editor.AcceptsReturn = true;
                _editor.VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto;
                _editor.HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled;
                _editor.FontFamily = SafeWpfFontFamily(_owner.Data.FontFamilyName);
                _editor.FontSize = PointSizeToDip(_owner.Data.FontSizeTwips / 20F);
                _editor.SpellCheck.IsEnabled = false;
                _owner.ConfigureMultilingualTextInput(_editor);
                LoadEditorContent();
                SaveEditorSelection();
                _editor.TextChanged += EditorTextChanged;
                _editor.SelectionChanged += EditorSelectionChanged;
                _editor.PreviewMouseLeftButtonUp += EditorPreviewMouseLeftButtonUp;
                _editor.PreviewMouseMove += EditorPreviewMouseMove;
                TextCompositionManager.AddPreviewTextInputStartHandler(_editor,
                    EditorTextCompositionStarted);
                TextCompositionManager.AddPreviewTextInputUpdateHandler(_editor,
                    EditorTextCompositionUpdated);
                _editor.PreviewTextInput += EditorTextCompositionCompleted;
                _editor.ContextMenu = BuildEditorMenu();

                _linkRefreshTimer = new DispatcherTimer(
                    DispatcherPriority.Background);
                _linkRefreshTimer.Interval = TimeSpan.FromMilliseconds(550);
                _linkRefreshTimer.Tick += delegate
                {
                    _linkRefreshTimer.Stop();
                    if (_editorTextCompositionActive)
                    {
                        _linkRefreshTimer.Start();
                        return;
                    }
                    RefreshOrdinaryLinks(true);
                };
            }

            internal WC.Grid BuildFormatToolbar(out WC.ComboBox familyBox,
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
                        ApplySelectionFontSize(points);
                        if (!sizeSelector.IsDropDownOpen)
                            RestoreEditorFocusAfterToolbarChoice();
                    }
                };

                boldButton = _owner.HeaderButton("B", 34, true);
                italicButton = _owner.HeaderButton("I", 34, false);
                italicButton.FontStyle = W.FontStyles.Italic;
                underlineButton = _owner.HeaderButton("U", 34, false);
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
                    _owner.ScheduleSave();
                };
                italicButton.Click += delegate
                {
                    EditingCommands.ToggleItalic.Execute(null, _editor);
                    _owner.ScheduleSave();
                };
                underlineButton.Click += delegate
                {
                    EditingCommands.ToggleUnderline.Execute(null, _editor);
                    _owner.ScheduleSave();
                };

                AddToGrid(toolbar, familyBox, 0);
                AddToGrid(toolbar, sizeBox, 1);
                AddToGrid(toolbar, boldButton, 2);
                AddToGrid(toolbar, italicButton, 3);
                AddToGrid(toolbar, underlineButton, 4);
                return toolbar;
            }

            internal WC.ContextMenu BuildEditorMenu()
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
                _owner.AddNoteActions(menu);
                return menu;
            }

            internal void AddCommandMenuItem(WC.ContextMenu menu, string text,
                ICommand command)
            {
                WC.MenuItem item = new WC.MenuItem();
                item.Header = text;
                item.Command = command;
                item.CommandTarget = _editor;
                menu.Items.Add(item);
            }

            internal void LoadEditorContent()
            {
                _editor.Document = new FlowDocument();
                _editor.Document.PagePadding = new W.Thickness(0);
                _editor.Document.FontFamily = SafeWpfFontFamily(_owner.Data.FontFamilyName);
                _editor.Document.FontSize = PointSizeToDip(_owner.Data.FontSizeTwips / 20F);
                string rtf = StickyNoteCodec.NormalizeRtf(_owner.Data.RichTextRtf);
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
                        _owner.Data.Text = EditorPlainText();
                        return;
                    }
                    catch
                    {
                        // A malformed/temporarily unreadable RTF payload should not
                        // be destroyed merely because this window failed to render
                        // it. Fall back to the plain-text projection in memory.
                    }
                }
                SetEditorPlainText(_owner.Data.Text ?? String.Empty);
            }

            internal void SetEditorPlainText(string text)
            {
                _editor.Document.Blocks.Clear();
                Paragraph paragraph = new Paragraph(new Run(text ?? String.Empty));
                paragraph.Margin = new W.Thickness(0);
                paragraph.FontFamily = SafeWpfFontFamily(_owner.Data.FontFamilyName);
                paragraph.FontSize = PointSizeToDip(_owner.Data.FontSizeTwips / 20F);
                _editor.Document.Blocks.Add(paragraph);
            }

            internal string EditorPlainText()
            {
                string value = new TextRange(_editor.Document.ContentStart,
                    _editor.Document.ContentEnd).Text ?? String.Empty;
                if (value.EndsWith("\r\n", StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - 2);
                return value;
            }

            internal void CaptureEditorContent()
            {
                if (_owner.Data.IsTodoList || _owner.Data.IsSchedule) return;
                _owner.Data.Text = EditorPlainText();
                try
                {
                    TextRange range = new TextRange(_editor.Document.ContentStart,
                        _editor.Document.ContentEnd);
                    using (MemoryStream stream = new MemoryStream())
                    {
                        range.Save(stream, W.DataFormats.Rtf);
                        _owner.Data.RichTextRtf = StickyNoteCodec.NormalizeRtf(
                            Encoding.UTF8.GetString(stream.ToArray()));
                    }
                }
                catch (Exception error)
                {
                    // Keep the last successfully captured RTF. _owner.Data.Text already
                    // contains the current full plain-text projection, so a
                    // transient serialization failure cannot erase note content.
                    ApplicationDiagnostics.ReportNonFatal(
                        "sticky-rich-text-capture", error);
                }
            }

            internal void EditorTextChanged(object sender,
                WC.TextChangedEventArgs e)
            {
                if (_owner._initializing || _applyingAutoLinkFormat) return;
                _owner._lastInputUtc = DateTime.UtcNow;
                _owner.Data.Text = EditorPlainText();
                _owner.RefreshTitle();
                QueueOrdinaryLinkRefresh();
                _owner.ScheduleSave();
            }

            internal void EditorTextCompositionStarted(object sender,
                TextCompositionEventArgs e)
            {
                _editorTextCompositionActive = true;
                _owner.RaiseImeCompositionChanged(true);
            }

            internal void EditorTextCompositionUpdated(object sender,
                TextCompositionEventArgs e)
            {
                _editorTextCompositionActive = true;
                _owner.RaiseImeCompositionChanged(true);
            }

            internal void EditorTextCompositionCompleted(object sender,
                TextCompositionEventArgs e)
            {
                _editorTextCompositionActive = false;
                _owner.RaiseImeCompositionChanged(false);
                QueueOrdinaryLinkRefresh();
            }

            internal void ApplySelectionFontFamily(string familyName)
            {
                if (String.IsNullOrWhiteSpace(familyName)) return;
                System.Windows.Media.FontFamily family = SafeWpfFontFamily(familyName);
                _owner.Data.FontFamilyName = family.Source;
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
                _owner.ScheduleSave();
            }

            internal void ApplySelectionFontSize(float points)
            {
                points = Math.Max(6F, Math.Min(72F, points));
                double dip = PointSizeToDip(points);
                _owner.Data.FontSizeTwips = (int)Math.Round(points * 20F);
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
                _owner.ScheduleSave();
            }

            internal void ApplyEmptyEditorTypingDefaults()
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

            internal void ApplyEmptyEditorTypingDefaultsCore()
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

            internal void EditorSelectionChanged(object sender, EventArgs e)
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

            internal void SaveEditorSelection()
            {
                if (_editor == null || _editor.Document == null) return;
                _savedSelectionStart = _editor.Selection.Start;
                _savedSelectionEnd = _editor.Selection.End;
            }

            internal void RestoreEditorSelection()
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

            internal void CaptureTypingFormatFromSelection()
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

            internal void RestoreEditorFocusAfterToolbarChoice()
            {
                if (_restoreEditorFocusQueued) return;
                _restoreEditorFocusQueued = true;
                _owner.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    new Action(delegate
                {
                    _restoreEditorFocusQueued = false;
                    if (_owner._disposed || _owner.Data.IsTodoList || _owner.Data.IsSchedule) return;
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

            internal void RefreshFormatToolbar()
            {
                if (_owner._initializing || _updatingFormatToolbar) return;
                _updatingFormatToolbar = true;
                try
                {
                    object familyValue = _editor.Selection.GetPropertyValue(
                        TextElement.FontFamilyProperty);
                    System.Windows.Media.FontFamily family =
                        familyValue as System.Windows.Media.FontFamily;
                    string familyName = family == null
                        ? _owner.Data.FontFamilyName : family.Source;
                    SelectComboText(_fontFamilyBox, familyName);

                    object sizeValue = _editor.Selection.GetPropertyValue(
                        TextElement.FontSizeProperty);
                    double dip = sizeValue is double ? (double)sizeValue :
                        PointSizeToDip(_owner.Data.FontSizeTwips / 20F);
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

            internal static void SetToggleButtonState(WC.Button button,
                object current, object enabledValue)
            {
                button.Opacity = current != W.DependencyProperty.UnsetValue &&
                    Object.Equals(current, enabledValue) ? 1.0 : 0.72;
            }

            internal readonly DispatcherTimer _linkRefreshTimer;
            internal bool _applyingAutoLinkFormat;
            internal readonly List<OrdinaryLinkRange> _ordinaryLinkRanges =
                new List<OrdinaryLinkRange>();

            internal void QueueOrdinaryLinkRefresh()
            {
                if (_owner._disposed || _owner.Data.IsTodoList || _owner.Data.IsSchedule) return;
                _linkRefreshTimer.Stop();
                _linkRefreshTimer.Start();
            }

            internal void RefreshOrdinaryLinks(bool saveAfterFormatting)
            {
                if (_owner._disposed || _owner.Data.IsTodoList || _owner.Data.IsSchedule ||
                    _editorTextCompositionActive) return;
                _applyingAutoLinkFormat = true;
                try
                {
                    ClearOrdinaryLinkFormatting();
                    foreach (Paragraph paragraph in CollectParagraphs(
                        _editor.Document.Blocks))
                    {
                        string text = new TextRange(paragraph.ContentStart,
                            paragraph.ContentEnd).Text ?? String.Empty;
                        foreach (StickyLinkMatch match in
                            WindowsStickyNoteLinkDetector.Find(text))
                        {
                            TextPointer start = PointerAtCharacterOffset(
                                paragraph.ContentStart, paragraph.ContentEnd,
                                match.Start);
                            TextPointer end = PointerAtCharacterOffset(
                                paragraph.ContentStart, paragraph.ContentEnd,
                                match.Start + match.Length);
                            if (start == null || end == null ||
                                start.CompareTo(end) >= 0) continue;
                            TextRange range = new TextRange(start, end);
                            range.ApplyPropertyValue(
                                TextElement.ForegroundProperty,
                                System.Windows.Media.Brushes.DodgerBlue);
                            range.ApplyPropertyValue(
                                Inline.TextDecorationsProperty,
                                W.TextDecorations.Underline);
                            _ordinaryLinkRanges.Add(new OrdinaryLinkRange(start,
                                end, match.Target, match.IsFileTarget));
                        }
                    }
                    _editor.ToolTip = _ordinaryLinkRanges.Count == 0 ? null :
                        "单击蓝色链接即可打开";
                }
                finally { _applyingAutoLinkFormat = false; }
                if (saveAfterFormatting) _owner.ScheduleSave();
            }

            internal void ClearOrdinaryLinkFormatting()
            {
                System.Windows.Media.Brush text = OpaqueBrush(_owner.EffectiveTextColor());
                foreach (OrdinaryLinkRange link in _ordinaryLinkRanges)
                {
                    try
                    {
                        if (link.Start.CompareTo(link.End) >= 0) continue;
                        TextRange range = new TextRange(link.Start, link.End);
                        range.ApplyPropertyValue(TextElement.ForegroundProperty,
                            text);
                        range.ApplyPropertyValue(Inline.TextDecorationsProperty,
                            null);
                    }
                    catch (InvalidOperationException) { }
                }
                _ordinaryLinkRanges.Clear();
            }

            internal static IList<Paragraph> CollectParagraphs(
                BlockCollection blocks)
            {
                List<Paragraph> result = new List<Paragraph>();
                CollectParagraphs(blocks, result);
                return result;
            }

            internal static void CollectParagraphs(BlockCollection blocks,
                List<Paragraph> result)
            {
                foreach (Block block in blocks)
                {
                    Paragraph paragraph = block as Paragraph;
                    if (paragraph != null) result.Add(paragraph);
                    Section section = block as Section;
                    if (section == null) continue;
                    CollectParagraphs(section.Blocks, result);
                }
            }

            internal static TextPointer PointerAtCharacterOffset(TextPointer start,
                TextPointer end, int characterOffset)
            {
                if (characterOffset < 0) return null;
                TextPointer position = start;
                int remaining = characterOffset;
                while (position != null && position.CompareTo(end) <= 0)
                {
                    if (position.GetPointerContext(LogicalDirection.Forward) ==
                        TextPointerContext.Text)
                    {
                        int runLength = position.GetTextRunLength(
                            LogicalDirection.Forward);
                        if (remaining <= runLength)
                            return position.GetPositionAtOffset(remaining,
                                LogicalDirection.Forward);
                        remaining -= runLength;
                    }
                    if (position.CompareTo(end) == 0) break;
                    position = position.GetNextContextPosition(
                        LogicalDirection.Forward);
                }
                return remaining == 0 ? end : null;
            }

            internal OrdinaryLinkRange OrdinaryLinkAt(TextPointer position)
            {
                if (position == null) return null;
                foreach (OrdinaryLinkRange link in _ordinaryLinkRanges)
                {
                    try
                    {
                        if (link.Start.CompareTo(position) <= 0 &&
                            link.End.CompareTo(position) > 0) return link;
                    }
                    catch (InvalidOperationException) { }
                }
                return null;
            }

            internal void EditorPreviewMouseMove(object sender, MouseEventArgs e)
            {
                if (_owner.Data.IsTodoList || _owner.Data.IsSchedule)
                {
                    _editor.Cursor = Cursors.IBeam;
                    return;
                }
                TextPointer position = _editor.GetPositionFromPoint(
                    e.GetPosition(_editor), true);
                _editor.Cursor = OrdinaryLinkAt(position) == null
                    ? Cursors.IBeam : Cursors.Hand;
            }

            internal void EditorPreviewMouseLeftButtonUp(object sender,
                MouseButtonEventArgs e)
            {
                if (_owner.Data.IsTodoList || _owner.Data.IsSchedule ||
                    !_editor.Selection.IsEmpty) return;
                TextPointer position = _editor.GetPositionFromPoint(
                    e.GetPosition(_editor), true);
                OrdinaryLinkRange link = OrdinaryLinkAt(position);
                if (link == null) return;
                e.Handled = true;
                OpenOrdinaryLink(link);
            }

            internal void OpenOrdinaryLink(OrdinaryLinkRange link)
            {
                if (link == null || String.IsNullOrWhiteSpace(link.Target)) return;
                Exception error;
                StickyLinkOpenResult result = StickyLinkService.Open(link.Target,
                    link.IsFileTarget,
                    delegate(StickyLinkOpenRisk risk, string target)
                    {
                        return W.MessageBox.Show(_owner,
                            StickyLinkPolicy.ConfirmationMessage(risk, target),
                            "确认打开可能有风险的路径",
                            W.MessageBoxButton.YesNo,
                            W.MessageBoxImage.Warning,
                            W.MessageBoxResult.No) == W.MessageBoxResult.Yes;
                    }, out error);
                if (result == StickyLinkOpenResult.Missing)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                if (result == StickyLinkOpenResult.Failed)
                    ApplicationDiagnostics.ReportNonFatal(
                        "sticky-link-open", error);
            }

            internal sealed class OrdinaryLinkRange
            {
                internal OrdinaryLinkRange(TextPointer start, TextPointer end,
                    string target, bool fileTarget)
                {
                    Start = start;
                    End = end;
                    Target = target;
                    IsFileTarget = fileTarget;
                }

                internal TextPointer Start { get; private set; }
                internal TextPointer End { get; private set; }
                internal string Target { get; private set; }
                internal bool IsFileTarget { get; private set; }
            }

            internal override WC.ComboBox FontSizeSelector { get { return _fontSizeBox; } }
            internal override WC.Grid Toolbar { get { return _formatToolbar; } }
            internal override W.FrameworkElement Body { get { return _editor; } }
            internal override bool IsComposing { get { return _editorTextCompositionActive; } }
            internal override void FocusPrimaryInput() { _editor.Focus(); }
            internal override void Capture() { CaptureEditorContent(); }
            internal override void Ready() { RefreshFormatToolbar(); RefreshOrdinaryLinks(false); }
            internal override void ApplyContentColors()
            {
                var text = OpaqueBrush(_owner.EffectiveTextColor());
                int argb = _owner.EffectiveTextColor().ToArgb();
                if (_appliedEditorTextColorArgb != argb)
                {
                    var document = new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd);
                    document.ApplyPropertyValue(TextElement.ForegroundProperty, text);
                    _editor.Document.Foreground = text;
                    _appliedEditorTextColorArgb = argb;
                }
                _editor.Foreground = text;
                StyleButtons(_boldButton, _italicButton, _underlineButton);
                StyleSelector(_fontFamilyBox);
                StyleSelector(_fontSizeBox);
            }
            public override void Dispose()
            {
                _linkRefreshTimer.Stop();
                _editor.TextChanged -= EditorTextChanged;
                _editor.SelectionChanged -= EditorSelectionChanged;
                _editor.PreviewMouseLeftButtonUp -= EditorPreviewMouseLeftButtonUp;
                _editor.PreviewMouseMove -= EditorPreviewMouseMove;
                TextCompositionManager.RemovePreviewTextInputStartHandler(_editor, EditorTextCompositionStarted);
                TextCompositionManager.RemovePreviewTextInputUpdateHandler(_editor, EditorTextCompositionUpdated);
                _editor.PreviewTextInput -= EditorTextCompositionCompleted;
                _ordinaryLinkRanges.Clear();
                _savedSelectionStart = _savedSelectionEnd = null;
            }

        }
    }
}
