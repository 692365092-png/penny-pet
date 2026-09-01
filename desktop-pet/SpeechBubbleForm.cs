using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PennyPet
{
    // Pure Windows presentation: PetBubbleCoordinator decides what to show;
    // this form only measures, draws, times and positions that message.
    internal sealed class SpeechBubbleForm : Form
    {
        internal static readonly Size MinimumBubbleSize = new Size(160, 88);
        internal static readonly Size MaximumBubbleSize = new Size(440, 230);
        internal static readonly Color BubbleFillColor =
            Color.FromArgb(255, 73, 74, 40);
        internal static readonly Color BubbleBorderColor =
            Color.FromArgb(255, 45, 46, 24);
        internal static readonly Color BubbleTextColor = Color.White;
        private const int HorizontalPadding = 44;
        private const int TextTopPadding = 16;
        private const int TextBottomPadding = 16;
        private const int TailHeight = 20;
        private const int CornerRadius = 18;
        private string _text;
        private readonly System.Windows.Forms.Timer _closeTimer;
        private bool _ownedResourcesDisposed;

        public SpeechBubbleForm(string text, int autoCloseMilliseconds)
            : this(text, autoCloseMilliseconds,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(100))
        {
        }

        public SpeechBubbleForm(string text, int autoCloseMilliseconds,
            string fontFamilyName, float fontSizePoints)
        {
            _text = text ?? String.Empty;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            DoubleBuffered = true;
            Font = StickyNoteWindow.CreateSafeFont(fontFamilyName,
                fontSizePoints, KeyboardOverlayForm.TextFontStyle);
            ApplyMeasuredLayout();
            if (autoCloseMilliseconds > 0)
            {
                _closeTimer = new System.Windows.Forms.Timer();
                _closeTimer.Interval = autoCloseMilliseconds;
                _closeTimer.Tick += delegate { Close(); };
                _closeTimer.Start();
            }
            MouseDown += delegate { Close(); };
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
                value.ExStyle |= 0x08000000;
                return value;
            }
        }

        public void ShowNear(Form owner)
        {
            if (!Visible) Show(owner);
            RepositionNear(owner);
            // The first Show can apply monitor DPI after the initial size was
            // measured. Re-center after the native layout pass.
            if (IsHandleCreated)
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && owner != null && !owner.IsDisposed)
                        RepositionNear(owner);
                });
        }

        internal void RepositionNear(Form owner)
        {
            if (owner == null || owner.IsDisposed) return;
            Rectangle work = Screen.FromRectangle(owner.Bounds).WorkingArea;
            Location = CalculateNearLocation(owner.Bounds, Size, work);
        }

        internal static Point CalculateNearLocation(Rectangle ownerBounds,
            Size bubbleSize, Rectangle work)
        {
            int x = ownerBounds.Left + ownerBounds.Width / 2 -
                bubbleSize.Width / 2;
            int y = ownerBounds.Top - bubbleSize.Height - 6;
            if (y < work.Top) y = ownerBounds.Bottom + 6;
            x = Math.Max(work.Left + 6,
                Math.Min(x, work.Right - bubbleSize.Width - 6));
            y = Math.Max(work.Top + 6,
                Math.Min(y, work.Bottom - bubbleSize.Height - 6));
            return new Point(x, y);
        }

        public void UpdateText(string text)
        {
            string next = text ?? String.Empty;
            if (String.Equals(_text, next, StringComparison.Ordinal)) return;
            _text = next;
            ApplyMeasuredLayout();
            Invalidate();
        }

        internal string DisplayText
        {
            get { return _text; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = BubblePath(ClientRectangle))
            using (SolidBrush fill = new SolidBrush(BubbleFillColor))
            using (Pen border = new Pen(BubbleBorderColor, 2F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
            Rectangle textArea = new Rectangle(HorizontalPadding / 2,
                TextTopPadding, Width - HorizontalPadding,
                Height - TextTopPadding - TextBottomPadding - TailHeight);
            TextRenderer.DrawText(e.Graphics, _text, Font, textArea,
                BubbleTextColor,
                TextFormatFlags.WordBreak |
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_ownedResourcesDisposed)
            {
                _ownedResourcesDisposed = true;
                if (_closeTimer != null) _closeTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath BubblePath(Rectangle bounds)
        {
            int radius = CornerRadius;
            int bottom = bounds.Bottom - TailHeight;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left + 2, bounds.Top + 2,
                radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius - 3, bounds.Top + 2,
                radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius - 3, bottom - radius,
                radius, radius, 0, 90);
            path.AddLine(bounds.Left + bounds.Width / 2 + 16, bottom,
                bounds.Left + bounds.Width / 2, bounds.Bottom - 3);
            path.AddLine(bounds.Left + bounds.Width / 2, bounds.Bottom - 3,
                bounds.Left + bounds.Width / 2 - 16, bottom);
            path.AddArc(bounds.Left + 2, bottom - radius,
                radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyMeasuredLayout()
        {
            string value = String.IsNullOrEmpty(_text) ? " " : _text;
            int minimumTextWidth = MinimumBubbleSize.Width - HorizontalPadding;
            int maximumTextWidth = MaximumBubbleSize.Width - HorizontalPadding;
            int naturalWidth = 0;
            string[] lines = value.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n');
            foreach (string line in lines)
            {
                Size lineSize = TextRenderer.MeasureText(
                    line.Length == 0 ? " " : line, Font,
                    new Size(4096, MaximumBubbleSize.Height),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix);
                naturalWidth = Math.Max(naturalWidth, lineSize.Width);
            }
            int textWidth = Math.Max(minimumTextWidth,
                Math.Min(maximumTextWidth, naturalWidth));
            int maximumTextHeight = MaximumBubbleSize.Height -
                TextTopPadding - TextBottomPadding - TailHeight;
            Size wrapped = TextRenderer.MeasureText(value, Font,
                new Size(textWidth, maximumTextHeight),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);
            int width = Math.Max(MinimumBubbleSize.Width,
                Math.Min(MaximumBubbleSize.Width,
                    textWidth + HorizontalPadding));
            int height = Math.Max(MinimumBubbleSize.Height,
                Math.Min(MaximumBubbleSize.Height, wrapped.Height +
                    TextTopPadding + TextBottomPadding + TailHeight));
            Size measured = new Size(width, height);
            if (ClientSize != measured) ClientSize = measured;
        }
    }
}
