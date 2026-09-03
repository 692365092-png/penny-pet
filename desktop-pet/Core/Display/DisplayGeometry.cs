using System;

namespace PennyPet
{
    internal struct LogicalPoint
    {
        internal int X;
        internal int Y;
    }

    internal struct LogicalSize
    {
        internal int Width;
        internal int Height;
    }

    internal struct LogicalRect
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;

        internal int Right { get { return X + Width; } }
        internal int Bottom { get { return Y + Height; } }
    }

    internal struct PhysicalPoint
    {
        internal int X;
        internal int Y;
    }

    internal sealed class DisplayScale
    {
        internal DisplayScale(double value)
        {
            Value = value > 0.0 && value < 8.0 ? value : 1.0;
        }

        internal double Value { get; private set; }
    }

    internal sealed class DisplayPlacement
    {
        internal DisplayPlacement(string displayId, int physicalOriginX,
            int physicalOriginY, DisplayScale scale)
        {
            DisplayId = displayId ?? String.Empty;
            PhysicalOriginX = physicalOriginX;
            PhysicalOriginY = physicalOriginY;
            Scale = scale ?? new DisplayScale(1.0);
        }

        internal string DisplayId { get; private set; }
        internal int PhysicalOriginX { get; private set; }
        internal int PhysicalOriginY { get; private set; }
        internal DisplayScale Scale { get; private set; }
    }

    internal static class DisplayGeometry
    {
        internal static LogicalPoint PhysicalToLocal(int physicalX,
            int physicalY, int physicalOriginX, int physicalOriginY,
            double scale)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            return new LogicalPoint
            {
                X = (int)Math.Round((physicalX - physicalOriginX) /
                    safeScale),
                Y = (int)Math.Round((physicalY - physicalOriginY) /
                    safeScale)
            };
        }

        internal static PhysicalPoint LocalToPhysical(int logicalX,
            int logicalY, int physicalOriginX, int physicalOriginY,
            double scale)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            return new PhysicalPoint
            {
                X = physicalOriginX +
                    (int)Math.Round(logicalX * safeScale),
                Y = physicalOriginY +
                    (int)Math.Round(logicalY * safeScale)
            };
        }
    }
}
