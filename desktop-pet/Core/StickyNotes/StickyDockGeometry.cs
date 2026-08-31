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

        internal static int CalculateDockDividerHeight(
            int requestedUpperHeight)
        {
            const int minimum = 220;
            const int maximum = 700;
            return Math.Max(minimum, Math.Min(maximum,
                requestedUpperHeight));
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

        internal static DockRect CalculateRecoveredHeaderDragBounds(
            DockRect start, DockRect current, DockPoint cursor,
            DockPoint pointerOffset, bool systemChangedGeometry)
        {
            if (!systemChangedGeometry) return current;
            return new DockRect
            {
                Left = cursor.X - pointerOffset.X,
                Top = cursor.Y - pointerOffset.Y,
                Width = start.Width,
                Height = start.Height
            };
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

        internal static List<DockRect> CalculateStickyRecoveryLayout(
            DockRect work, IList<DockSize> componentSizes, float scale)
        {
            List<DockRect> result = new List<DockRect>();
            int count = componentSizes == null ? 0 : componentSizes.Count;
            for (int index = 0; index < count; index++)
                result.Add(new DockRect());
            if (count == 0) return result;

            const int margin = 24;
            const int gap = 18;
            int scaledMargin = Math.Max(1, (int)Math.Round(margin * scale));
            int scaledGap = Math.Max(1, (int)Math.Round(gap * scale));
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int rowWidthLimit = Math.Max(minimumWidth,
                work.Width - scaledMargin * 2);
            List<List<int>> rows = new List<List<int>>();
            List<int> normal = new List<int>();
            List<int> oversized = new List<int>();
            for (int index = 0; index < count; index++)
            {
                DockSize size = componentSizes[index];
                bool isOversized = size.Width >= Math.Max(
                    (int)Math.Round(520 * scale),
                    work.Width * 45 / 100) || size.Height >= Math.Max(
                    (int)Math.Round(520 * scale),
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
                int nextWidth = row.Count == 0 ? width :
                    rowWidth + scaledGap + width;
                if (row.Count > 0 && nextWidth > rowWidthLimit)
                {
                    rows.Add(row);
                    row = new List<int>();
                    rowWidth = 0;
                }
                row.Add(index);
                rowWidth = rowWidth == 0 ? width :
                    rowWidth + scaledGap + width;
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
                    height = Math.Max(height, Math.Min(
                        componentSizes[index].Height,
                        Math.Max(minimumHeight, work.Height * 58 / 100)));
                rowHeights.Add(height);
                totalHeight += height;
            }
            totalHeight += Math.Max(0, rows.Count - 1) * scaledGap;
            int y = work.Top + Math.Max(scaledMargin,
                (work.Height - totalHeight) / 2);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<int> recoveryRow = rows[rowIndex];
                int width = 0;
                foreach (int index in recoveryRow)
                {
                    if (width > 0) width += scaledGap;
                    width += Math.Max(minimumWidth, Math.Min(maximumWidth,
                        componentSizes[index].Width));
                }
                int x = work.Left + (work.Width - width) / 2;
                foreach (int index in recoveryRow)
                {
                    int itemWidth = Math.Max(minimumWidth,
                        Math.Min(maximumWidth,
                        componentSizes[index].Width));
                    result[index] = new DockRect
                    {
                        Left = x,
                        Top = y,
                        Width = itemWidth,
                        Height = componentSizes[index].Height
                    };
                    x += itemWidth + scaledGap;
                }
                y += rowHeights[rowIndex] + scaledGap;
            }
            return result;
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
            return new DockPoint { X = x, Y = y };
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
            else y = work.Bottom - owner.Bottom >= owner.Top - work.Top
                ? work.Bottom - popup.Height : work.Top;
            return new DockPoint
            {
                X = x,
                Y = Math.Max(work.Top,
                    Math.Min(y, work.Bottom - popup.Height))
            };
        }
    }
}
