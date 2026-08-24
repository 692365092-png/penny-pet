using System;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    // A short, non-activating seam indicator makes merge, insertion and split
    // operations visible without stealing focus from either WPF note window.
    internal sealed class DockPulseIndicatorForm : Form
    {
        private readonly Timer _timer;
        private readonly int _autoCloseMilliseconds;
        private int _elapsed;
        private Rectangle _fullBounds;

        internal DockPulseIndicatorForm(Color color, int autoCloseMilliseconds)
        {
            _autoCloseMilliseconds = Math.Max(0, autoCloseMilliseconds);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = color;
            Opacity = 0.86;
            _timer = new Timer();
            _timer.Interval = 90;
            _timer.Tick += Pulse;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        internal bool UsesSteadyOpacityForTest
        {
            get { return Math.Abs(Opacity - 0.86) < 0.01; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                value.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                value.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return value;
            }
        }

        internal void ShowSeam(Rectangle seam)
        {
            _fullBounds = new Rectangle(seam.Left, seam.Top,
                Math.Max(24, seam.Width), Math.Max(4, seam.Height));
            int initialWidth = Math.Max(24, _fullBounds.Width / 4);
            Bounds = new Rectangle(_fullBounds.Left +
                (_fullBounds.Width - initialWidth) / 2, _fullBounds.Top,
                initialWidth, _fullBounds.Height);
            _elapsed = 0;
            if (!Visible) Show();
            _timer.Start();
        }

        internal void UpdateSeam(Rectangle seam)
        {
            _fullBounds = new Rectangle(seam.Left, seam.Top,
                Math.Max(24, seam.Width), Math.Max(4, seam.Height));
            Bounds = _fullBounds;
        }

        private void Pulse(object sender, EventArgs e)
        {
            _elapsed += _timer.Interval;
            double progress = Math.Min(1.0, _elapsed / 270.0);
            int width = Math.Max(24, (int)Math.Round(
                _fullBounds.Width * (0.25 + progress * 0.75)));
            Bounds = new Rectangle(_fullBounds.Left +
                (_fullBounds.Width - width) / 2, _fullBounds.Top,
                width, _fullBounds.Height);
            if (progress >= 1.0 && _autoCloseMilliseconds == 0)
                _timer.Stop();
            if (_autoCloseMilliseconds > 0 &&
                _elapsed >= _autoCloseMilliseconds) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
