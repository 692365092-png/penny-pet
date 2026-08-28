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
            if (_frame == null) return;
            bool first = LayeredSpriteRenderer.Show(this, _frame);
            ApplicationDiagnostics.WriteWindowLayerEvent(
                "StartupLoadingFrame",
                _frame.Width + "x" + _frame.Height + " first=" + first);
            System.Windows.Forms.Timer retry =
                new System.Windows.Forms.Timer();
            retry.Interval = 80;
            retry.Tick += delegate
            {
                retry.Stop();
                if (IsDisposed || _frame == null) return;
                bool second = LayeredSpriteRenderer.Show(this, _frame);
                ApplicationDiagnostics.WriteWindowLayerEvent(
                    "StartupLoadingFrame",
                    _frame.Width + "x" + _frame.Height + " second=" + second);
                if (!second)
                    ApplicationDiagnostics.ReportNonFatal(
                        "startup-loading-show",
                        new InvalidOperationException(
                            "Startup loading frame could not be shown."));
                retry.Dispose();
            };
            retry.Start();
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

        internal bool UsesNormalizedIdleFrameForTest()
        {
            if (_frame == null) return false;
            Size canvas = PetForm.ScaledPetSize(100);
            if (_frame.Size != canvas) return false;
            using (PetArtPackage art = PetArtPackage.Load(canvas.Width,
                canvas.Height))
            using (Bitmap expected = art.GetFrame(0, 0))
            {
                if (expected == null || expected.Size != _frame.Size)
                    return false;
                for (int y = 0; y < _frame.Height; y++)
                    for (int x = 0; x < _frame.Width; x++)
                        if (_frame.GetPixel(x, y).ToArgb() !=
                            expected.GetPixel(x, y).ToArgb())
                            return false;
            }
            return true;
        }

        private static Bitmap LoadScaledFrame(Size size)
        {
            // The loading window must show the exact same normalized idle frame
            // as PetForm.  Using the generated startup cache instead of the
            // legacy loading.png avoids the old stretch/aspect mismatch.
            Size canvas = PetForm.ScaledPetSize(100);
            using (PetArtPackage art = PetArtPackage.Load(canvas.Width,
                canvas.Height))
            {
                Bitmap source = art.GetFrame(0, 0);
                return ResizeStartupFrame(source, size);
            }
        }

        private static Bitmap ResizeStartupFrame(Bitmap source, Size size)
        {
            if (source.Width == size.Width && source.Height == size.Height)
                return new Bitmap(source);

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
                    new Rectangle(Point.Empty, size),
                    new Rectangle(Point.Empty, source.Size),
                    GraphicsUnit.Pixel);
            }
            return output;
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
            if (disposing && _frame != null)
            {
                _frame.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
