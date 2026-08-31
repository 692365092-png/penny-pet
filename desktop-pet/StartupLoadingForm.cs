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
        private const int NativePetWidth = 192;
        private const int NativePetHeight = 208;
        private readonly Bitmap _frame;

        internal StartupLoadingForm(PetSettings settings)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = ScaledPetSize(settings == null
                ? 100 : settings.ScalePercent);
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
            Size expected = ScaledPetSize(scalePercent);
            return ClientSize == expected && _frame != null &&
                _frame.Size == expected;
        }

        internal bool UsesEmbeddedLoadingFrameForTest()
        {
            if (_frame == null) return false;
            using (Stream stream = typeof(StartupLoadingForm).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return false;
                using (Bitmap source = new Bitmap(stream))
                {
                    Rectangle bounds = CalculateImageBounds(source.Size,
                        ClientSize);
                    bool transparentPadding = bounds.Left > 0
                        ? _frame.GetPixel(bounds.Left - 1,
                            ClientSize.Height / 2).A == 0
                        : bounds.Top > 0 && _frame.GetPixel(
                            ClientSize.Width / 2, bounds.Top - 1).A == 0;
                    return transparentPadding &&
                        bounds.Width > 0 && bounds.Height > 0 &&
                        bounds.Left >= 0 && bounds.Top >= 0 &&
                        bounds.Right <= ClientSize.Width &&
                        bounds.Bottom <= ClientSize.Height;
                }
            }
        }

        private static Bitmap LoadScaledFrame(Size size)
        {
            using (Stream stream = typeof(StartupLoadingForm).Assembly
                .GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "Embedded startup loading image is missing.");
                using (Bitmap source = new Bitmap(stream))
                    return RenderStartupFrame(source, size);
            }
        }

        private static Bitmap RenderStartupFrame(Bitmap source, Size size)
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
                    CalculateImageBounds(source.Size, size),
                    new Rectangle(Point.Empty, source.Size),
                    GraphicsUnit.Pixel);
            }
            return output;
        }

        private static Rectangle CalculateImageBounds(Size source,
            Size canvas)
        {
            double scale = Math.Min((double)canvas.Width / source.Width,
                (double)canvas.Height / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            return new Rectangle((canvas.Width - width) / 2,
                (canvas.Height - height) / 2, width, height);
        }

        private static Size ScaledPetSize(int scalePercent)
        {
            int normalized = PetSettingRules.NormalizePetScalePercent(
                scalePercent);
            return new Size(NativePetWidth * normalized / 100,
                NativePetHeight * normalized / 100);
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
