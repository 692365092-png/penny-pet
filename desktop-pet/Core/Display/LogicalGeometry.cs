using System;

namespace PennyPet
{
    // Platform-neutral logical geometry. These types must never reference
    // desktop drawing libraries, per-monitor scale APIs, or windowing APIs;
    // platform adapters project logical geometry to native coordinates.
    internal readonly struct LogicalPoint
    {
        internal LogicalPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal double X { get; }
        internal double Y { get; }
    }

    internal readonly struct LogicalSize
    {
        internal LogicalSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        internal double Width { get; }
        internal double Height { get; }
    }

    internal readonly struct LogicalRect
    {
        internal LogicalRect(double left, double top,
            double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        internal double Left { get; }
        internal double Top { get; }
        internal double Width { get; }
        internal double Height { get; }
        internal double Right { get { return Left + Width; } }
        internal double Bottom { get { return Top + Height; } }
    }

    internal readonly struct DisplayScale
    {
        internal DisplayScale(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal double X { get; }
        internal double Y { get; }
    }
}
