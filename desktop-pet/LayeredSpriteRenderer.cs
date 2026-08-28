using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class LayeredSpriteRenderer
    {
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        public static Bitmap ComposeFrame(Bitmap atlas, int row, int frame,
            int cellWidth, int cellHeight)
        {
            Bitmap output = new Bitmap(cellWidth, cellHeight, PixelFormat.Format32bppPArgb);
            Rectangle source = new Rectangle(frame * cellWidth, row * cellHeight,
                cellWidth, cellHeight);

            using (Graphics graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(atlas, new Rectangle(0, 0, cellWidth, cellHeight),
                    source, GraphicsUnit.Pixel);
            }
            ApplyInnerOutline(output);
            return output;
        }

        public static bool Show(Form form, Bitmap bitmap)
        {
            return Show(form, bitmap, 255);
        }

        public static bool Show(Form form, Bitmap bitmap, byte opacity)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                NativePoint source = new NativePoint(0, 0);
                NativePoint destination = new NativePoint(form.Left, form.Top);
                BlendFunction blend = new BlendFunction();
                blend.BlendOp = AcSrcOver;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = opacity;
                blend.AlphaFormat = AcSrcAlpha;

                bool ok = UpdateLayeredWindow(form.Handle, screenDc, ref destination,
                    ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
                return ok;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        internal static void ApplyInnerOutline(Bitmap bitmap)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite,
                PixelFormat.Format32bppPArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                byte[] original = (byte[])pixels.Clone();

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int index = y * stride + x * 4;
                        if (original[index + 3] == 0) continue;

                        bool touchesTransparency = false;
                        for (int dy = -1; dy <= 1 && !touchesTransparency; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx < 0 || nx >= bitmap.Width ||
                                    ny < 0 || ny >= bitmap.Height ||
                                    original[ny * stride + nx * 4 + 3] == 0)
                                {
                                    touchesTransparency = true;
                                    break;
                                }
                            }
                        }

                        if (!touchesTransparency) continue;
                        pixels[index] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                    }
                }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
            public NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
            public NativeSize(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
            ref NativePoint destination, ref NativeSize size, IntPtr sourceDc,
            ref NativePoint source, int colorKey, ref BlendFunction blend, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);
    }
}
