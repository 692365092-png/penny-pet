using System;
using Color = System.Drawing.Color;
using System.Windows.Input;
using System.Windows.Media;
using W = System.Windows;
using WC = System.Windows.Controls;
using WF = System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class StickyNoteWindow
    {
        // Window-local view ownership. The shell knows layout, focus, capture,
        // appearance and lifetime; editor-specific commands stay in each view.
        internal abstract class StickyContentView : IDisposable
        {
            protected readonly StickyNoteWindow _owner;
            protected StickyContentView(StickyNoteWindow owner) { _owner = owner; }
            internal abstract WC.Grid Toolbar { get; }
            internal abstract W.FrameworkElement Body { get; }
            internal abstract WC.ComboBox FontSizeSelector { get; }
            internal virtual bool IsComposing { get { return false; } }
            internal abstract void FocusPrimaryInput();
            internal abstract void Capture();
            internal virtual void Ready() { }
            internal abstract void ApplyContentColors();
            internal virtual void ClearSelection() { }
            public virtual void Dispose() { }

            internal void ApplyColors()
            {
                Toolbar.Background = AlphaBrush(WF.ControlPaint.Light(
                    Color.FromArgb(_owner.Data.ColorArgb), 0.08F),
                    _owner.Data.BackgroundOpacityPercent);
                ApplyContentColors();
            }
            protected void StyleSelector(WC.ComboBox selector)
            {
                selector.Foreground = System.Windows.Media.Brushes.Black;
                selector.Background = AlphaBrush(WF.ControlPaint.LightLight(
                    Color.FromArgb(_owner.Data.ColorArgb)), _owner.Data.BackgroundOpacityPercent);
            }
            protected void StyleButtons(params WC.Button[] buttons)
            {
                foreach (WC.Button button in buttons)
                {
                    button.Foreground = OpaqueBrush(_owner.EffectiveTextColor());
                    button.Background = System.Windows.Media.Brushes.Transparent;
                    button.BorderBrush = AlphaBrush(Color.FromArgb(_owner.Data.ColorArgb),
                        Math.Max(35, _owner.Data.BackgroundOpacityPercent));
                }
            }
        }

        internal abstract class StickyListContentView : StickyContentView
        {
            internal WC.Grid _formatToolbar;
            internal WC.ComboBox _fontSizeBox;
            private bool _updatingSize;
            protected StickyListContentView(StickyNoteWindow owner) : base(owner) { }
            internal override WC.Grid Toolbar { get { return _formatToolbar; } }
            internal override WC.ComboBox FontSizeSelector { get { return _fontSizeBox; } }
            internal abstract void RefreshList();
            protected void BuildListToolbar(WC.Button add, WC.Button delete)
            {
                _formatToolbar = new WC.Grid { Height = 32, Margin = new W.Thickness(4, 0, 4, 0) };
                _formatToolbar.ColumnDefinitions.Add(Column(new W.GridLength(1, W.GridUnitType.Star)));
                _formatToolbar.ColumnDefinitions.Add(Column(new W.GridLength(56)));
                _formatToolbar.ColumnDefinitions.Add(Column(new W.GridLength(56)));
                _fontSizeBox = new WC.ComboBox
                {
                    Width = 120, HorizontalAlignment = W.HorizontalAlignment.Left,
                    Margin = new W.Thickness(0, 3, 4, 3),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = PointSizeToDip(9F), Foreground = System.Windows.Media.Brushes.Black,
                    IsTextSearchEnabled = true, IsSynchronizedWithCurrentItem = false
                };
                AddFontSizeOptions(_fontSizeBox, true);
                _owner.Data.FontSizeTwips = (int)Math.Round(
                    NormalizeScheduleFontSize(_owner.Data.FontSizeTwips / 20F) * 20F);
                RefreshSizeSelector();
                _fontSizeBox.SelectionChanged += delegate
                {
                    if (_updatingSize || _fontSizeBox.SelectedItem == null) return;
                    float points;
                    if (TryParseFontSize(Convert.ToString(_fontSizeBox.SelectedItem), out points))
                        ApplyListFontSize(points);
                };
                _fontSizeBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
                {
                    if (_fontSizeBox.IsDropDownOpen) return;
                    _fontSizeBox.SelectedIndex = Math.Max(0, Math.Min(_fontSizeBox.Items.Count - 1,
                        Math.Max(0, _fontSizeBox.SelectedIndex) + (e.Delta > 0 ? -1 : 1)));
                    e.Handled = true;
                };
                AddToGrid(_formatToolbar, _fontSizeBox, 0);
                AddToGrid(_formatToolbar, add, 1);
                AddToGrid(_formatToolbar, delete, 2);
            }
            internal void ApplyListFontSize(float points)
            {
                _owner.Data.FontSizeTwips = (int)Math.Round(NormalizeScheduleFontSize(points) * 20F);
                RefreshList();
                RefreshSizeSelector();
                _owner.ScheduleSave();
            }
            private void RefreshSizeSelector()
            {
                _updatingSize = true;
                try { SelectComboText(_fontSizeBox, ScheduleFontSizeLabel(_owner.Data.FontSizeTwips / 20F)); }
                finally { _updatingSize = false; }
            }
        }
    }
}
