using System;
using System.Drawing;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;

namespace PennyPet
{
    // Appearance dialog lifecycle and preview only. Editor formatting and
    // WPF focus/IME behavior remain in the main sticky window implementation.
    internal sealed partial class StickyNoteWindow
    {
        private void ShowAppearanceDialog()
        {
            if (_appearanceDialog != null && !_appearanceDialog.IsDisposed)
            {
                _appearanceDialog.Show();
                _appearanceDialog.BringToFront();
                _appearanceDialog.Activate();
                return;
            }
            _saveTimer.Stop();
            int originalColor = Data.ColorArgb;
            int originalOpacity = Data.BackgroundOpacityPercent;
            int originalTextColor = Data.TextColorArgb;
            _appearanceDialog = new StickyAppearanceDialog(Palette,
                Color.FromArgb(Data.ColorArgb), Data.BackgroundOpacityPercent,
                EffectiveTextColor(), ApplyAppearancePreview,
                delegate(bool accepted)
                {
                    if (!accepted)
                    {
                        Data.ColorArgb = originalColor;
                        Data.BackgroundOpacityPercent = originalOpacity;
                        Data.TextColorArgb = originalTextColor;
                        ApplyColors();
                    }
                    if (!_disposed) ScheduleSave();
                });
            DisplayScale scale = WindowsDisplayMetrics.ScaleForWindow(Handle);
            System.Drawing.Point physicalLocation =
                WindowsDisplayMetrics.LogicalToPhysicalPoint(
                    new LogicalPoint(Left, Top), scale);
            System.Drawing.Point physicalSize =
                WindowsDisplayMetrics.LogicalToPhysicalPoint(
                    new LogicalPoint(Width, Height), scale);
            Rectangle physicalNote = new Rectangle(physicalLocation.X,
                physicalLocation.Y, physicalSize.X, physicalSize.Y);
            Rectangle work = WF.Screen.FromRectangle(physicalNote)
                .WorkingArea;
            _appearanceDialog.Location = CalculateAppearanceDialogLocation(
                physicalNote, _appearanceDialog.Size, work);
            _appearanceDialog.FormClosed += delegate { _appearanceDialog = null; };
            if (_opaqueQaHost)
            {
                _appearanceDialog.EnableQaTargeting();
                _appearanceDialog.Show();
            }
            else _appearanceDialog.Show(this);
        }

        private void ApplyAppearancePreview(Color color, int opacityPercent,
            Color textColor)
        {
            Data.ColorArgb = Color.FromArgb(255, color.R, color.G,
                color.B).ToArgb();
            Data.BackgroundOpacityPercent = Math.Max(10, Math.Min(100,
                ((opacityPercent + 5) / 10) * 10));
            Data.TextColorArgb = textColor.ToArgb() == Color.White.ToArgb()
                ? Color.White.ToArgb() : Color.Black.ToArgb();
            ApplyColors();
        }

        internal static System.Drawing.Point CalculateAppearanceDialogLocation(
            Rectangle note, Size dialog, Rectangle work)
        {
            DockPoint point = StickyDockGeometry.CalculatePopupLocation(
                new DockRect(note.Left, note.Top, note.Width, note.Height),
                new DockSize(dialog.Width, dialog.Height),
                new DockRect(work.Left, work.Top, work.Width, work.Height), 8);
            return new System.Drawing.Point(point.X, point.Y);
        }

        private void CloseAppearanceDialogAsCancel()
        {
            if (_appearanceDialog != null && !_appearanceDialog.IsDisposed)
                _appearanceDialog.Close();
        }
    }
}
