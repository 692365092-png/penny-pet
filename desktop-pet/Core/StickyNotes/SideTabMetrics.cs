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
