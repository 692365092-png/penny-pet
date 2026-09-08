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
        private readonly PhysicalRect? _placementRect;

        internal StartupLoadingForm(
            StartupPetPlacementSnapshot placement)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            _placementRect = placement == null
                ? (PhysicalRect?)null
                : placement.PhysicalBounds;
            ClientSize = placement == null
                ? new Size(192, 208)
                : new Size(placement.PhysicalBounds.Width,
                    placement.PhysicalBounds.Height);
            Location = placement == null
                ? Point.Empty
                : new Point(placement.PhysicalBounds.Left,
                    placement.PhysicalBounds.Top);
            _frame = LoadScaledFrame(ClientSize);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Same native physical placement the formal Pet uses: never let
            // WinForms per-monitor DPI handling re-scale the bootstrap rect.
            if (_placementRect.HasValue && Handle != IntPtr.Zero)
                NativeDisplayConfig.SetWindowPos(Handle, IntPtr.Zero,
                    _placementRect.Value.Left,
                    _placementRect.Value.Top,
                    0, 0,
                    NativeDisplayConfig.SWP_NOSIZE |
                    NativeDisplayConfig.SWP_NOZORDER |
                    NativeDisplayConfig.SWP_NOACTIVATE);
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
                    double sourceAspect = (double)source.Width / source.Height;
                    double renderedAspect = (double)bounds.Width /
                        bounds.Height;
                    bool transparentPadding = bounds.Left > 0
                        ? _frame.GetPixel(bounds.Left - 1,
                            ClientSize.Height / 2).A == 0
                        : bounds.Top > 0 && _frame.GetPixel(
                            ClientSize.Width / 2, bounds.Top - 1).A == 0;
                    return transparentPadding &&
                        Math.Abs(sourceAspect - renderedAspect) < 0.01D &&
                        bounds.Left == (ClientSize.Width - bounds.Width) / 2 &&
                        bounds.Bottom == ClientSize.Height &&
                        bounds.Width > 0 && bounds.Height > 0 &&
                        bounds.Left >= 0 && bounds.Top >= 0 &&
                        bounds.Right <= ClientSize.Width &&
                        bounds.Bottom <= ClientSize.Height;
                }
            }
        }

        // The loading canvas is a pure projection of the bootstrap snapshot:
        // same physical size and same physical top-left, nothing more.
        internal bool UsesPlacementForTest(
            StartupPetPlacementSnapshot placement)
        {
            if (placement == null) return false;
            Size expected = new Size(placement.PhysicalBounds.Width,
                placement.PhysicalBounds.Height);
            return ClientSize == expected && _frame != null &&
                _frame.Size == expected &&
                Location == new Point(placement.PhysicalBounds.Left,
                    placement.PhysicalBounds.Top);
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
                canvas.Height - height, width, height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _frame != null) _frame.Dispose();
            base.Dispose(disposing);
        }
    }
}
