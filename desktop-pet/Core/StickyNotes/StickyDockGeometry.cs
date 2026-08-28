using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal struct DockPoint
    {
        internal int X;
        internal int Y;
    }

    internal struct DockSize
    {
        internal int Width;
        internal int Height;
    }

    internal struct DockRect
    {
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;

        internal int Right { get { return Left + Width; } }
        internal int Bottom { get { return Top + Height; } }
    }

    internal static class StickyDockGeometry
    {
        internal static List<DockRect> CalculateUnifiedDockLayout(
            IList<DockSize> sizes, int left, int top, int width, float scale)
        {
            List<DockRect> result = new List<DockRect>();
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int maximumHeight = Math.Max(minimumHeight,
                (int)Math.Round(700 * scale));
            int normalizedWidth = Math.Max(minimumWidth,
                Math.Min(maximumWidth, width));
            int y = top;
            if (sizes == null) return result;
            foreach (DockSize size in sizes)
            {
                int height = Math.Max(minimumHeight,
                    Math.Min(maximumHeight, size.Height));
                result.Add(new DockRect
                {
                    Left = left,
                    Top = y,
                    Width = normalizedWidth,
                    Height = height
                });
                y += height;
            }
            return result;
        }

        internal static DockSize CalculateDockDividerHeights(
            int previousUpperHeight, int requestedUpperHeight,
            int currentLowerHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            int oldUpper = Math.Max(minimum, Math.Min(maximum,
                previousUpperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum,
                currentLowerHeight));
            int total = oldUpper + lower;
            int minimumUpper = Math.Max(minimum, total - maximum);
            int maximumUpper = Math.Min(maximum, total - minimum);
            int upper = Math.Max(minimumUpper, Math.Min(maximumUpper,
                requestedUpperHeight));
            return new DockSize { Width = upper, Height = total - upper };
        }

        internal static DockSize CalculateDockDividerRange(int upperHeight,
            int lowerHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            int upper = Math.Max(minimum, Math.Min(maximum, upperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum, lowerHeight));
            int total = upper + lower;
            return new DockSize
            {
                Width = Math.Max(minimum, total - maximum),
                Height = Math.Min(maximum, total - minimum)
            };
        }

        internal static DockPoint CalculateHeaderReachableTranslation(
            DockRect header, DockRect work)
        {
            int dx = 0;
            int dy = 0;
            const int minimumVisibleWidth = 64;
            if (header.Right < work.Left + minimumVisibleWidth)
                dx = work.Left + minimumVisibleWidth - header.Right;
            else if (header.Left > work.Right - minimumVisibleWidth)
                dx = work.Right - minimumVisibleWidth - header.Left;
            if (header.Top < work.Top) dy = work.Top - header.Top;
            else if (header.Bottom > work.Bottom)
                dy = work.Bottom - header.Bottom;
            return new DockPoint { X = dx, Y = dy };
        }

        internal static DockPoint CalculateStickyRecoveryAnchor(
            DockRect work, DockRect pet, DockSize window, int componentIndex)
        {
            int preferredLeft = pet.Left - window.Width - 12;
            if (preferredLeft < work.Left) preferredLeft = pet.Right + 12;
            int targetLeft = Math.Max(work.Left,
                Math.Min(preferredLeft, work.Right - window.Width));
            int availableTop = Math.Max(1, work.Height - 36);
            int relativeTop = pet.Top - work.Top +
                Math.Max(0, componentIndex) * 34;
            relativeTop %= availableTop;
            if (relativeTop < 0) relativeTop += availableTop;
            int targetTop = Math.Max(work.Top,
                Math.Min(work.Top + relativeTop, work.Bottom - 32));
            return new DockPoint { X = targetLeft, Y = targetTop };
        }
    }
}
