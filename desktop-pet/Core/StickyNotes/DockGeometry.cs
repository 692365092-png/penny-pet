using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal struct DockPoint
    {
        internal DockPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal int X;
        internal int Y;
    }

    internal struct DockSize
    {
        internal DockSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        internal int Width;
        internal int Height;
    }

    internal struct DockRect
    {
        internal DockRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
        internal int Right { get { return Left + Width; } }
        internal int Bottom { get { return Top + Height; } }
    }

    internal static class DockGeometry
    {
        internal static List<DockRect> CalculateLayout(IList<DockSize> sizes,
            int left, int top, int width, double scale)
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
                result.Add(new DockRect(left, y, normalizedWidth, height));
                y += height;
            }
            return result;
        }

        internal static bool IsCoordinateRangeSafe(int top,
            IList<int> heights, int coordinateLimit)
        {
            long limit = Math.Max(1, coordinateLimit);
            long y = top;
            if (y < -limit || y > limit) return false;
            if (heights == null) return true;
            foreach (int value in heights)
            {
                y += Math.Max(220, Math.Min(700, value));
                if (y < -limit || y > limit) return false;
            }
            return true;
        }

        internal static DockSize CalculateDividerHeights(
            int previousUpperHeight, int requestedUpperHeight,
            int currentLowerHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            int upper = Math.Max(minimum, Math.Min(maximum,
                previousUpperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum,
                currentLowerHeight));
            int total = upper + lower;
            int minimumUpper = Math.Max(minimum, total - maximum);
            int maximumUpper = Math.Min(maximum, total - minimum);
            upper = Math.Max(minimumUpper, Math.Min(maximumUpper,
                requestedUpperHeight));
            return new DockSize(upper, total - upper);
        }

        internal static DockSize CalculateDividerRange(int upperHeight,
            int lowerHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            int upper = Math.Max(minimum, Math.Min(maximum, upperHeight));
            int lower = Math.Max(minimum, Math.Min(maximum, lowerHeight));
            int total = upper + lower;
            return new DockSize(Math.Max(minimum, total - maximum),
                Math.Min(maximum, total - minimum));
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
            else if (header.Bottom > work.Bottom) dy = work.Bottom - header.Bottom;
            return new DockPoint(dx, dy);
        }

        internal static bool CanDockBelow(DockRect moving, DockRect target,
            int threshold)
        {
            int limit = Math.Max(4, threshold);
            if (Math.Abs(moving.Top - target.Bottom) > limit) return false;
            int overlap = Math.Min(moving.Right, target.Right) -
                Math.Max(moving.Left, target.Left);
            int narrowerWidth = Math.Min(moving.Width, target.Width);
            int widerWidth = Math.Max(moving.Width, target.Width);
            bool aligned = Math.Abs(moving.Left - target.Left) <= limit ||
                Math.Abs(moving.Right - target.Right) <= limit ||
                Math.Abs((moving.Left + moving.Right) -
                    (target.Left + target.Right)) <= limit * 2;
            bool differentWidths = widerWidth >= narrowerWidth * 3 / 2;
            return overlap >= Math.Max(48, narrowerWidth / 2) &&
                (aligned || differentWidths);
        }

        internal static List<DockRect> CalculateRecoveryLayout(DockRect work,
            IList<DockSize> componentSizes, double scale)
        {
            List<DockRect> result = new List<DockRect>();
            int count = componentSizes == null ? 0 : componentSizes.Count;
            for (int index = 0; index < count; index++)
                result.Add(new DockRect());
            if (count == 0) return result;

            int margin = Math.Max(1, (int)Math.Round(24 * scale));
            int gap = Math.Max(1, (int)Math.Round(18 * scale));
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int rowWidthLimit = Math.Max(minimumWidth, work.Width - margin * 2);
            List<List<int>> rows = new List<List<int>>();
            List<int> normal = new List<int>();
            List<int> oversized = new List<int>();
            for (int index = 0; index < count; index++)
            {
                DockSize size = componentSizes[index];
                bool isOversized = size.Width >= Math.Max(
                    (int)Math.Round(520 * scale), work.Width * 45 / 100) ||
                    size.Height >= Math.Max((int)Math.Round(520 * scale),
                    work.Height * 50 / 100);
                if (isOversized) oversized.Add(index);
                else normal.Add(index);
            }

            List<int> row = new List<int>();
            int rowWidth = 0;
            foreach (int index in normal)
            {
                int width = Math.Max(minimumWidth, Math.Min(maximumWidth,
                    componentSizes[index].Width));
                int nextWidth = row.Count == 0 ? width : rowWidth + gap + width;
                if (row.Count > 0 && nextWidth > rowWidthLimit)
                {
                    rows.Add(row);
                    row = new List<int>();
                    rowWidth = 0;
                }
                row.Add(index);
                rowWidth = rowWidth == 0 ? width : rowWidth + gap + width;
            }
            if (row.Count > 0) rows.Add(row);
            foreach (int index in oversized)
                rows.Add(new List<int>(new int[] { index }));

            List<int> rowHeights = new List<int>();
            int totalHeight = 0;
            foreach (List<int> recoveryRow in rows)
            {
                int height = minimumHeight;
                foreach (int index in recoveryRow)
                    height = Math.Max(height, Math.Min(componentSizes[index].Height,
                        Math.Max(minimumHeight, work.Height * 58 / 100)));
                rowHeights.Add(height);
                totalHeight += height;
            }
            totalHeight += Math.Max(0, rows.Count - 1) * gap;
            int y = work.Top + Math.Max(margin, (work.Height - totalHeight) / 2);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<int> recoveryRow = rows[rowIndex];
                int width = 0;
                foreach (int index in recoveryRow)
                {
                    if (width > 0) width += gap;
                    width += Math.Max(minimumWidth, Math.Min(maximumWidth,
                        componentSizes[index].Width));
                }
                int x = work.Left + (work.Width - width) / 2;
                foreach (int index in recoveryRow)
                {
                    int itemWidth = Math.Max(minimumWidth, Math.Min(maximumWidth,
                        componentSizes[index].Width));
                    result[index] = new DockRect(x, y, itemWidth,
                        componentSizes[index].Height);
                    x += itemWidth + gap;
                }
                y += rowHeights[rowIndex] + gap;
            }
            return result;
        }

        internal static DockPoint CalculateRecoveryAnchor(DockRect work,
            DockRect pet, DockSize window, int componentIndex)
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
            return new DockPoint(targetLeft, Math.Max(work.Top,
                Math.Min(work.Top + relativeTop, work.Bottom - 32)));
        }

        internal static DockPoint CalculateCascadedWindowLocation(
            DockRect work, DockRect anchor, DockSize window, int itemCount,
            int bottomGap)
        {
            int offset = (Math.Max(0, itemCount) % 7) * 18;
            int x = anchor.Left - window.Width - 12 - offset;
            if (x < work.Left)
                x = Math.Min(work.Right - window.Width,
                    anchor.Right + 12 + offset);
            return new DockPoint(
                Math.Max(work.Left, Math.Min(x, work.Right - window.Width)),
                Math.Max(work.Top, Math.Min(anchor.Top + offset,
                    work.Bottom - window.Height - Math.Max(0, bottomGap))));
        }

        internal static DockPoint CalculateSideTabLocation(DockRect pet,
            DockRect work, DockSize strip, bool onLeft, int overlap,
            int horizontalOffset)
        {
            int x = onLeft ? pet.Left - strip.Width + overlap :
                pet.Right - overlap - Math.Max(0, horizontalOffset);
            x = Math.Max(work.Left + 2,
                Math.Min(x, work.Right - strip.Width - 2));
            int y = pet.Top + (pet.Height - strip.Height) / 2;
            y = Math.Max(work.Top + 4,
                Math.Min(y, work.Bottom - strip.Height - 4));
            return new DockPoint(x, y);
        }

        internal static DockPoint CalculatePetLocationWithSideTabs(
            DockRect pet, DockRect work, int reserveLeft, int reserveRight)
        {
            int minimumLeft = work.Left + Math.Max(0, reserveLeft);
            int maximumLeft = work.Right - Math.Max(0, reserveRight) -
                pet.Width;
            if (maximumLeft < minimumLeft)
                return new DockPoint(pet.Left, pet.Top);
            return new DockPoint(Math.Max(minimumLeft,
                Math.Min(pet.Left, maximumLeft)), Math.Max(work.Top,
                Math.Min(pet.Top, work.Bottom - pet.Height)));
        }

        internal static int CalculateSideTabOverlap(int petWidth)
        {
            int transparentMargin = (int)Math.Round(
                Math.Max(0, petWidth) * 44.0 / 192.0);
            return (20 + transparentMargin) / 2;
        }

        internal static int CalculateSideTabScreenCapacity(int workHeight,
            int tabHeight, int tabGap)
        {
            return Math.Max(1, (workHeight - 16) /
                (Math.Max(1, tabHeight) + Math.Max(0, tabGap)));
        }

        internal static int CalculateLeftSideTabCount(int totalCount,
            int petHeight, int workHeight, int tabHeight, int tabGap)
        {
            if (totalCount <= 0) return 0;
            int screenCapacity = CalculateSideTabScreenCapacity(workHeight,
                tabHeight, tabGap);
            int preferred = CalculatePreferredSideTabCount(petHeight,
                workHeight, tabHeight, tabGap);
            int left = Math.Min(totalCount, preferred);
            if (totalCount - left > screenCapacity)
                left = Math.Min(screenCapacity, totalCount - screenCapacity);
            return Math.Max(0, left);
        }

        internal static int CalculatePreferredSideTabCount(int petHeight,
            int workHeight, int tabHeight, int tabGap)
        {
            int normalizedHeight = Math.Max(1, tabHeight);
            int normalizedGap = Math.Max(0, tabGap);
            return Math.Min(CalculateSideTabScreenCapacity(workHeight,
                normalizedHeight, normalizedGap), Math.Max(4,
                (Math.Max(normalizedHeight, petHeight) + normalizedGap) /
                (normalizedHeight + normalizedGap)));
        }

        internal static DockPoint CalculatePopupLocation(DockRect owner,
            DockSize popup, DockRect work, int gap)
        {
            int x = owner.Left + (owner.Width - popup.Width) / 2;
            x = Math.Max(work.Left, Math.Min(x, work.Right - popup.Width));
            int below = owner.Bottom + gap;
            int above = owner.Top - popup.Height - gap;
            int y;
            if (below + popup.Height <= work.Bottom) y = below;
            else if (above >= work.Top) y = above;
            else
            {
                int belowSpace = work.Bottom - owner.Bottom;
                int aboveSpace = owner.Top - work.Top;
                y = belowSpace >= aboveSpace ?
                    work.Bottom - popup.Height : work.Top;
            }
            return new DockPoint(x, Math.Max(work.Top,
                Math.Min(y, work.Bottom - popup.Height)));
        }

        internal static DockRect CalculateRecoveredDragBounds(DockRect start,
            DockRect current, DockPoint cursor, DockPoint pointerOffset,
            bool systemChangedGeometry)
        {
            return systemChangedGeometry ? new DockRect(
                cursor.X - pointerOffset.X, cursor.Y - pointerOffset.Y,
                start.Width, start.Height) : current;
        }
    }
}
