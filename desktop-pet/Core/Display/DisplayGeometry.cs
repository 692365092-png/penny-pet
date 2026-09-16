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
                    safeScale, MidpointRounding.AwayFromZero),
                Y = (int)Math.Round((physicalY - physicalOriginY) /
                    safeScale, MidpointRounding.AwayFromZero)
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
                    (int)Math.Round(logicalX * safeScale,
                        MidpointRounding.AwayFromZero),
                Y = physicalOriginY +
                    (int)Math.Round(logicalY * safeScale,
                        MidpointRounding.AwayFromZero)
            };
        }

        internal static int PhysicalLengthToLogical(int physicalLength,
            double scale)
        {
            if (physicalLength <= 0) return 0;
            double safeScale = scale > 0.0 ? scale : 1.0;
            double logical = Math.Round(physicalLength / safeScale,
                MidpointRounding.AwayFromZero);
            return logical >= Int32.MaxValue ? Int32.MaxValue :
                Math.Max(1, (int)logical);
        }

        // Single rounding policy for preferred-placement projection: long
        // arithmetic before multiplication and AwayFromZero rounding keep
        // extreme coordinates from overflowing and make roundtrips stable.
        internal static PhysicalRect ProjectLocalRect(
            LogicalRect local, int physicalOriginX, int physicalOriginY,
            double scale)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            long x = physicalOriginX + (long)Math.Round(
                local.X * safeScale, MidpointRounding.AwayFromZero);
            long y = physicalOriginY + (long)Math.Round(
                local.Y * safeScale, MidpointRounding.AwayFromZero);
            long width = (long)Math.Round(
                local.Width * safeScale, MidpointRounding.AwayFromZero);
            long height = (long)Math.Round(
                local.Height * safeScale, MidpointRounding.AwayFromZero);
            return new PhysicalRect(ClampToInt32(x), ClampToInt32(y),
                ClampToInt32(Math.Max(1, width)),
                ClampToInt32(Math.Max(1, height)));
        }

        private static int ClampToInt32(long value)
        {
            if (value < Int32.MinValue) return Int32.MinValue;
            if (value > Int32.MaxValue) return Int32.MaxValue;
            return (int)value;
        }

        // True when an actual window rect sits inside the accepted tolerance
        // of the rect that was requested by an exact native placement. The
        // caller may issue at most one corrective placement when this fails,
        // then must accept the actual Windows facts instead of fighting them.
        internal static bool IsWithinPlacementTolerance(
            PhysicalRect expected, PhysicalRect actual, int tolerancePixels)
        {
            int tolerance = Math.Max(0, tolerancePixels);
            return Math.Abs(actual.Left - expected.Left) <= tolerance &&
                Math.Abs(actual.Top - expected.Top) <= tolerance &&
                Math.Abs(actual.Width - expected.Width) <= tolerance &&
                Math.Abs(actual.Height - expected.Height) <= tolerance;
        }
    }
}
