using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class KeyboardOverlayForm : Form
    {
        internal const string TextFontFamilyName = "Microsoft YaHei UI";
        internal const float BaseTextFontSizePoints = 15F;
        internal const FontStyle TextFontStyle = FontStyle.Bold;
        private readonly KeyDisplayAccumulator _accumulator = new KeyDisplayAccumulator();
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private Font _font;
        private Bitmap _surface;
        private DateTime _lastInputUtc;
        private byte _opacity;
        private string _displayText = String.Empty;
        private int _textScalePercent = 100;
        private int _heldVirtualKeyCode;
        private bool _ownedResourcesDisposed;

        public KeyboardOverlayForm() : this(100)
        {
        }

        public KeyboardOverlayForm(int textScalePercent)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(220, 46);
            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = 50;
            _fadeTimer.Tick += FadeTick;
            SetTextScale(textScalePercent);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000;
                value.ExStyle |= 0x08000000;
                value.ExStyle |= 0x00000080;
                return value;
            }
        }

        public void ShowKey(Form pet, string keyText)
        {
            ShowKey(pet, keyText, 1);
        }

        public void ShowKey(Form pet, string keyText, int occurrences)
        {
            ShowKey(pet, keyText, occurrences, 0);
        }

        public void ShowKey(Form pet, string keyText, int occurrences,
            int virtualKeyCode)
        {
            ShowKey(pet, keyText, occurrences, virtualKeyCode, null);
        }

        internal void ShowKey(Form pet, string keyText, int occurrences,
            int virtualKeyCode, Rectangle? avoidBounds)
        {
            if (pet == null || pet.IsDisposed || String.IsNullOrEmpty(keyText)) return;
            DateTime now = DateTime.UtcNow;
            _displayText = _accumulator.Register(keyText, now, occurrences);
            _heldVirtualKeyCode = virtualKeyCode;
            _lastInputUtc = now;
            _opacity = 255;
            RenderSurface(ChooseTextColor(CalculateLocation(pet,
                avoidBounds)));
            UpdatePosition(pet, avoidBounds);
            if (!Visible) Show(pet);
            LayeredSpriteRenderer.Show(this, _surface, _opacity);
            _fadeTimer.Start();
        }

        public void ShowKeyRepeatCount(Form pet, string keyText, int repeatCount,
            int virtualKeyCode)
        {
            ShowKeyRepeatCount(pet, keyText, repeatCount, virtualKeyCode,
                null);
        }

        internal void ShowKeyRepeatCount(Form pet, string keyText,
            int repeatCount, int virtualKeyCode, Rectangle? avoidBounds)
        {
            if (pet == null || pet.IsDisposed || String.IsNullOrEmpty(keyText)) return;
            DateTime now = DateTime.UtcNow;
            _displayText = _accumulator.RegisterAbsolute(keyText, now, repeatCount);
            _heldVirtualKeyCode = virtualKeyCode;
            _lastInputUtc = now;
            _opacity = 255;
            RenderSurface(ChooseTextColor(CalculateLocation(pet,
                avoidBounds)));
            UpdatePosition(pet, avoidBounds);
            if (!Visible) Show(pet);
            LayeredSpriteRenderer.Show(this, _surface, _opacity);
            _fadeTimer.Start();
        }

        public void UpdatePosition(Form pet)
        {
            UpdatePosition(pet, null);
        }

        internal void UpdatePosition(Form pet, Rectangle? avoidBounds)
        {
            if (pet == null || pet.IsDisposed) return;
            Location = CalculateLocation(pet, avoidBounds);
            if (Visible && _surface != null)
                LayeredSpriteRenderer.Show(this, _surface, _opacity);
        }

        public void HideImmediately()
        {
            _fadeTimer.Stop();
            _accumulator.Reset();
            _displayText = String.Empty;
            _heldVirtualKeyCode = 0;
            if (Visible) Hide();
        }

        public void SetTextScale(int percent)
        {
            int normalized = NormalizeTextScalePercent(percent);
            if (_font != null && normalized == _textScalePercent) return;
            _textScalePercent = normalized;
            if (_font != null) _font.Dispose();
            _font = new Font(TextFontFamilyName,
                TextFontSizePoints(normalized), TextFontStyle,
                GraphicsUnit.Point);
            HideImmediately();
        }

        internal static int NormalizeTextScalePercent(int value)
        {
            return PetSettingRules.NormalizeKeyboardTextScalePercent(value);
        }

        internal static float TextFontSizePoints(int textScalePercent)
        {
            return BaseTextFontSizePoints *
                NormalizeTextScalePercent(textScalePercent) / 100F;
        }

        internal static Color ChooseTextColorFromLuminance(double luminance)
        {
            return luminance >= 0.56 ? Color.Black : Color.White;
        }

        internal static Bitmap RenderTextPreview(string text, Color color,
            byte opacity)
        {
            return RenderTextPreview(text, color, opacity, 100);
        }

        internal static Bitmap RenderTextPreview(string text, Color color,
            byte opacity, int textScalePercent)
        {
            using (Font font = new Font(TextFontFamilyName,
                TextFontSizePoints(textScalePercent), TextFontStyle,
                GraphicsUnit.Point))
                return RenderTextBitmap(text, color, opacity, font);
        }

        private Point CalculateLocation(Form pet, Rectangle? avoidBounds)
        {
            Rectangle work = Screen.FromRectangle(pet.Bounds).WorkingArea;
            return CalculateLocationForTest(pet.Bounds, work, ClientSize,
                avoidBounds);
        }

        internal static Point CalculateLocationForTest(Rectangle petBounds,
            Rectangle workingArea, Size overlaySize, Rectangle? avoidBounds)
        {
            int x = Clamp(petBounds.Left + petBounds.Width / 2 -
                overlaySize.Width / 2, workingArea.Left + 4,
                workingArea.Right - overlaySize.Width - 4);
            int y = petBounds.Bottom + 4;
            if (y + overlaySize.Height > workingArea.Bottom - 2)
                y = Math.Max(workingArea.Top + 2,
                    petBounds.Top - overlaySize.Height - 4);
            Point normal = new Point(x, y);
            if (!avoidBounds.HasValue || !avoidBounds.Value.IntersectsWith(
                new Rectangle(normal, overlaySize))) return normal;

            Rectangle avoid = avoidBounds.Value;
            Point[] candidates =
            {
                new Point(avoid.Right + 8,
                    petBounds.Top + petBounds.Height / 2 -
                        overlaySize.Height / 2),
                new Point(avoid.Left - overlaySize.Width - 8,
                    petBounds.Top + petBounds.Height / 2 -
                        overlaySize.Height / 2),
                new Point(petBounds.Left + petBounds.Width / 2 -
                    overlaySize.Width / 2, avoid.Bottom + 8),
                new Point(petBounds.Left + petBounds.Width / 2 -
                    overlaySize.Width / 2,
                    avoid.Top - overlaySize.Height - 8)
            };
            Point best = normal;
            long bestDistance = Int64.MaxValue;
            foreach (Point candidate in candidates)
            {
                Point fitted = new Point(
                    Clamp(candidate.X, workingArea.Left + 4,
                        workingArea.Right - overlaySize.Width - 4),
                    Clamp(candidate.Y, workingArea.Top + 2,
                        workingArea.Bottom - overlaySize.Height - 2));
                Rectangle bounds = new Rectangle(fitted, overlaySize);
                if (avoid.IntersectsWith(bounds)) continue;
                long dx = bounds.Left + bounds.Width / 2 -
                    (petBounds.Left + petBounds.Width / 2);
                long dy = bounds.Top + bounds.Height / 2 -
                    (petBounds.Top + petBounds.Height / 2);
                long distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;
                best = fitted;
                bestDistance = distance;
            }
            return best;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (maximum < minimum) return minimum;
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        private Color ChooseTextColor(Point location)
        {
            try
            {
                int centerX = location.X + Width / 2;
                int centerY = location.Y + Height / 2;
                using (Bitmap sample = new Bitmap(12, 12, PixelFormat.Format24bppRgb))
                using (Graphics graphics = Graphics.FromImage(sample))
                {
                    graphics.CopyFromScreen(centerX - 6, centerY - 6, 0, 0,
                        sample.Size, CopyPixelOperation.SourceCopy);
                    double total = 0;
                    int count = 0;
                    for (int y = 0; y < sample.Height; y += 2)
                    {
                        for (int x = 0; x < sample.Width; x += 2)
                        {
                            Color pixel = sample.GetPixel(x, y);
                            total += (0.2126 * pixel.R + 0.7152 * pixel.G +
                                0.0722 * pixel.B) / 255.0;
                            count++;
                        }
                    }
                    return ChooseTextColorFromLuminance(total / Math.Max(1, count));
                }
            }
            catch
            {
                return ChooseTextColorFromLuminance(
                    SystemColors.Desktop.GetBrightness());
            }
        }

        private void RenderSurface(Color textColor)
        {
            Bitmap next = RenderTextBitmap(_displayText, textColor, 255, _font);
            ClientSize = next.Size;
            if (_surface != null) _surface.Dispose();
            _surface = next;
        }

        private static Bitmap RenderTextBitmap(string text, Color textColor,
            byte opacity, Font font)
        {
            string value = text ?? String.Empty;
            SizeF measured;
            using (Bitmap measurementBitmap = new Bitmap(1, 1))
            using (Graphics measurement = Graphics.FromImage(measurementBitmap))
            using (StringFormat measurementFormat =
                (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                measurementFormat.FormatFlags |= StringFormatFlags.NoWrap |
                    StringFormatFlags.MeasureTrailingSpaces;
                measured = measurement.MeasureString(value, font, 520,
                    measurementFormat);
            }
            int width = Math.Max(150, Math.Min(430,
                (int)Math.Ceiling(measured.Width) + 30));
            int height = Math.Max(40, Math.Min(72,
                (int)Math.Ceiling(measured.Height) + 14));
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (StringFormat format =
                (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap |
                    StringFormatFlags.MeasureTrailingSpaces;
                RectangleF area = new RectangleF(4, 1, width - 8, height - 3);
                Color outlineColor = textColor.R < 128
                    ? Color.FromArgb(110, 255, 255, 255)
                    : Color.FromArgb(125, 0, 0, 0);
                using (SolidBrush outline = new SolidBrush(outlineColor))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(opacity,
                    textColor.R, textColor.G, textColor.B)))
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0) continue;
                            RectangleF shifted = new RectangleF(area.X + x,
                                area.Y + y, area.Width, area.Height);
                            graphics.DrawString(value, font, outline, shifted, format);
                        }
                    }
                    graphics.DrawString(value, font, fill, area, format);
                }
            }
            return bitmap;
        }

        private void FadeTick(object sender, EventArgs e)
        {
            if (_heldVirtualKeyCode > 0 &&
                KeyboardInputFormatter.IsVirtualKeyDown(_heldVirtualKeyCode))
            {
                _lastInputUtc = DateTime.UtcNow;
                if (_opacity != 255)
                {
                    _opacity = 255;
                    if (_surface != null && Visible)
                        LayeredSpriteRenderer.Show(this, _surface, _opacity);
                }
                return;
            }
            TimeSpan silence = DateTime.UtcNow - _lastInputUtc;
            if (silence < TimeSpan.FromSeconds(1)) return;
            if (_opacity <= 30)
            {
                HideImmediately();
                return;
            }
            _opacity = (byte)(_opacity - 30);
            if (_surface != null && Visible)
                LayeredSpriteRenderer.Show(this, _surface, _opacity);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_ownedResourcesDisposed)
            {
                _ownedResourcesDisposed = true;
                _fadeTimer.Dispose();
                if (_font != null) _font.Dispose();
                if (_surface != null) _surface.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
