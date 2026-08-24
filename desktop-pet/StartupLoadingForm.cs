using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class StartupLoadingForm : Form
    {
        private const string ResourceName = "PennyPet.Startup.Loading";
        private readonly Bitmap _frame;

        internal StartupLoadingForm(PetSettings settings)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            int percent = PetForm.NormalizeScalePercent(settings == null
                ? 100 : settings.ScalePercent);
            ClientSize = PetForm.ScaledPetSize(percent);
            Location = ResolveLocation(settings, ClientSize);
            _frame = LoadScaledFrame(ClientSize);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000;
                return value;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_frame != null) LayeredSpriteRenderer.Show(this, _frame);
        }

        internal static bool HasEmbeddedFrame
        {
            get
            {
                using (Stream stream = typeof(StartupLoadingForm).Assembly
                    .GetManifestResourceStream(ResourceName))
                    return stream != null && stream.Length > 512;
            }
        }

        internal bool UsesPetScaleForTest(int scalePercent)
        {
            Size expected = PetForm.ScaledPetSize(scalePercent);
            return ClientSize == expected && _frame != null &&
                _frame.Size == expected;
        }

        private static Bitmap LoadScaledFrame(Size size)
        {
            using (Stream stream = typeof(StartupLoadingForm).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return null;
                using (Bitmap source = new Bitmap(stream))
                {
                    Bitmap output = new Bitmap(size.Width, size.Height,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(output))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode =
                            InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(source,
                            new Rectangle(Point.Empty, size));
                    }
                    return output;
                }
            }
        }

        private static Point ResolveLocation(PetSettings settings, Size size)
        {
            if (settings != null && settings.HasLocation)
            {
                Point saved = new Point(settings.X, settings.Y);
                Rectangle candidate = new Rectangle(saved, size);
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle visible = Rectangle.Intersect(
                        screen.WorkingArea, candidate);
                    if (visible.Width >= 48 && visible.Height >= 48)
                        return saved;
                }
            }
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            return new Point(work.Right - size.Width - 24,
                work.Bottom - size.Height - 24);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _frame != null) _frame.Dispose();
            base.Dispose(disposing);
        }
    }
}
