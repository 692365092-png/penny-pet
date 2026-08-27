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
            Rectangle work = WF.Screen.FromRectangle(new Rectangle(
                (int)Left, (int)Top, (int)Width, (int)Height)).WorkingArea;
            _appearanceDialog.Location = CalculateAppearanceDialogLocation(
                new Rectangle((int)Left, (int)Top, (int)Width, (int)Height),
                _appearanceDialog.Size, work);
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
            const int gap = 8;
            int x = note.Left + (note.Width - dialog.Width) / 2;
            x = Math.Max(work.Left, Math.Min(x, work.Right - dialog.Width));
            int below = note.Bottom + gap;
            int above = note.Top - dialog.Height - gap;
            int y;
            if (below + dialog.Height <= work.Bottom) y = below;
            else if (above >= work.Top) y = above;
            else
            {
                int belowSpace = work.Bottom - note.Bottom;
                int aboveSpace = note.Top - work.Top;
                y = belowSpace >= aboveSpace
                    ? work.Bottom - dialog.Height : work.Top;
            }
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - dialog.Height));
            return new System.Drawing.Point(x, y);
        }

        private void CloseAppearanceDialogAsCancel()
        {
            if (_appearanceDialog != null && !_appearanceDialog.IsDisposed)
                _appearanceDialog.Close();
        }
    }
}
