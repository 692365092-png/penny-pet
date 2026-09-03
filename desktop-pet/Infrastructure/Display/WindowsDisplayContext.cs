using System;

namespace PennyPet
{
    internal sealed class WindowsDisplayMetrics
    {
        internal WindowsDisplayMetrics(string displayId,
            int physicalLeft, int physicalTop, int physicalWidth,
            int physicalHeight, int workLeft, int workTop,
            int workWidth, int workHeight, double scale)
        {
            DisplayId = displayId ?? String.Empty;
            PhysicalLeft = physicalLeft;
            PhysicalTop = physicalTop;
            PhysicalWidth = Math.Max(1, physicalWidth);
            PhysicalHeight = Math.Max(1, physicalHeight);
            WorkLeft = workLeft;
            WorkTop = workTop;
            WorkWidth = Math.Max(1, workWidth);
            WorkHeight = Math.Max(1, workHeight);
            Scale = scale > 0.0 && scale < 8.0 ? scale : 1.0;
        }

        internal string DisplayId { get; private set; }
        internal int PhysicalLeft { get; private set; }
        internal int PhysicalTop { get; private set; }
        internal int PhysicalWidth { get; private set; }
        internal int PhysicalHeight { get; private set; }
        internal int WorkLeft { get; private set; }
        internal int WorkTop { get; private set; }
        internal int WorkWidth { get; private set; }
        internal int WorkHeight { get; private set; }
        internal double Scale { get; private set; }
    }

    internal static class WindowsDisplayContext
    {
        internal static LogicalPoint PhysicalToLocal(
            WindowsDisplayMetrics metrics, int physicalX, int physicalY)
        {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));
            return DisplayGeometry.PhysicalToLocal(physicalX, physicalY,
                metrics.PhysicalLeft, metrics.PhysicalTop, metrics.Scale);
        }

        internal static PhysicalPoint LocalToPhysical(
            WindowsDisplayMetrics metrics, int logicalX, int logicalY)
        {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));
            return DisplayGeometry.LocalToPhysical(logicalX, logicalY,
                metrics.PhysicalLeft, metrics.PhysicalTop, metrics.Scale);
        }
    }
}
