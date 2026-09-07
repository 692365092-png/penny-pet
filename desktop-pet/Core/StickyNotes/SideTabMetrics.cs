using System;

namespace PennyPet
{
    internal sealed class SideTabPhysicalMetrics
    {
        private SideTabPhysicalMetrics(int dpi)
        {
            Dpi = Math.Max(96, dpi);
            Scale = Dpi / 96.0;

            Width = ScaleLength(146);
            Height = ScaleLength(34);
            Gap = ScaleLength(2);
            PreviewInsertionGap = ScaleLength(14);
            DragSourceVisualOffset = ScaleLength(10);
            WindowMarginX = ScaleLength(2);
            WindowMarginY = ScaleLength(4);
            IconSize = ScaleLength(24);
            IconMargin = ScaleLength(10);
        }

        internal int Dpi { get; private set; }
        internal double Scale { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int Gap { get; private set; }
        internal int PreviewInsertionGap { get; private set; }
        internal int DragSourceVisualOffset { get; private set; }
        internal int WindowMarginX { get; private set; }
        internal int WindowMarginY { get; private set; }
        internal int IconSize { get; private set; }
        internal int IconMargin { get; private set; }

        // 8.5pt logical reference projected directly to physical em pixels.
        internal float FontPixels { get { return 8.5F * Dpi / 72F; } }

        internal int ScaleLength(int logical)
        {
            if (logical == 0) return 0;

            int rounded = (int)Math.Round(
                logical * Scale,
                MidpointRounding.AwayFromZero);

            return logical > 0
                ? Math.Max(1, rounded)
                : Math.Min(-1, rounded);
        }

        internal float ScaleStroke(float logical)
        {
            return Math.Max(1F, (float)(logical * Scale));
        }

        internal static SideTabPhysicalMetrics ForDpi(int dpi)
        {
            return new SideTabPhysicalMetrics(dpi);
        }
    }

    internal static class SideTabLayoutPolicy
    {
        internal const int LogicalTabWidth = 146;
        internal const int LogicalTabHeight = 34;
        internal const int LogicalTabGap = 2;
        internal const int LogicalPreviewInsertionGap = 14;
        internal const int LogicalDragSourceVisualOffset = 10;

        internal static int CalculateBalancedLeftCount(int totalCount)
        {
            return (Math.Max(0, totalCount) + 1) / 2;
        }

        internal static int CalculatePhysicalOverlap(
            int petPhysicalWidth, SideTabPhysicalMetrics metrics)
        {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));

            int transparentMargin = (int)Math.Round(
                Math.Max(0, petPhysicalWidth) * 44.0 / 192.0,
                MidpointRounding.AwayFromZero);

            int physicalGap = metrics.ScaleLength(20);

            return Math.Max(0, (physicalGap + transparentMargin) / 2);
        }

        internal static int CalculateEdgeAwareLeftCount(
            int totalCount, DockRect pet, DockRect work,
            int stripPhysicalWidth, int overlap, int marginX)
        {
            int total = Math.Max(0, totalCount);
            if (total == 0) return 0;

            int balanced = CalculateBalancedLeftCount(total);
            int width = Math.Max(1, stripPhysicalWidth);
            int safeOverlap = Math.Max(0, overlap);
            int safeMargin = Math.Max(0, marginX);

            int minX = work.Left + safeMargin;
            int maxRight = work.Right - safeMargin;

            int naturalLeft = pet.Left - width + safeOverlap;
            int naturalRight = pet.Right - safeOverlap;

            long leftRight = (long)naturalLeft + width;
            long rightRight = (long)naturalRight + width;

            bool leftFits = naturalLeft >= minX && leftRight <= maxRight;
            bool rightFits = naturalRight >= minX && rightRight <= maxRight;

            if (leftFits && rightFits) return balanced;
            if (leftFits) return total;
            if (rightFits) return 0;

            long leftOverflow =
                Math.Max(0L, (long)minX - naturalLeft) +
                Math.Max(0L, leftRight - maxRight);
            long rightOverflow =
                Math.Max(0L, (long)minX - naturalRight) +
                Math.Max(0L, rightRight - maxRight);

            return leftOverflow <= rightOverflow ? total : 0;
        }

        internal static int LogicalScreenCapacity(int logicalWorkHeight)
        {
            return Math.Max(1,
                (logicalWorkHeight - 16) /
                (LogicalTabHeight + LogicalTabGap));
        }

        internal static int PhysicalWorkHeightToLogical(
            int physicalWorkHeight, int actualDpi)
        {
            return DisplayGeometry.PhysicalLengthToLogical(
                physicalWorkHeight,
                Math.Max(96, actualDpi) / 96.0);
        }
    }
}
