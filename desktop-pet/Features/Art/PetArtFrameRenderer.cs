using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PennyPet
{
    internal static class PetArtFrameRenderer
    {
        internal static Bitmap Render(Image source,
            PetArtStateDefinition definition, PetArtRenderSettings render,
            int canvasWidth, int canvasHeight)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (render == null) throw new ArgumentNullException(nameof(render));

            Bitmap output = new Bitmap(canvasWidth, canvasHeight,
                PixelFormat.Format32bppPArgb);
            double stateScale = definition != null &&
                definition.renderScale > 0.0
                ? definition.renderScale : 1.0;
            double stateScaleY = definition != null &&
                definition.renderScaleY > 0.0
                ? definition.renderScaleY : 1.0;
            double sx = (double)canvasWidth / Math.Max(1, source.Width);
            double sy = (double)canvasHeight / Math.Max(1, source.Height);
            if (String.Equals(render.fit, "stretch",
                StringComparison.OrdinalIgnoreCase))
            {
                sx *= render.scale * stateScale;
                sy *= render.scale * stateScale * stateScaleY;
            }
            else
            {
                double fitScale = String.Equals(render.fit, "cover",
                    StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(sx, sy) : Math.Min(sx, sy);
                sx = sy = fitScale * render.scale * stateScale;
                sy *= stateScaleY;
            }
            int width = Math.Max(1, (int)Math.Round(source.Width * sx));
            int height = Math.Max(1, (int)Math.Round(source.Height * sy));
            int x = (int)Math.Round((canvasWidth - width) * render.anchorX +
                render.offsetX + (definition == null
                    ? 0.0 : definition.renderOffsetX));
            int y = (int)Math.Round((canvasHeight - height) * render.anchorY +
                render.offsetY + (definition == null
                    ? 0.0 : definition.renderOffsetY));

            using (Graphics graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(x, y, width, height),
                    new Rectangle(0, 0, source.Width, source.Height),
                    GraphicsUnit.Pixel);
            }
            if (render.innerOutline)
                LayeredSpriteRenderer.ApplyInnerOutline(output);
            return output;
        }
    }
}
