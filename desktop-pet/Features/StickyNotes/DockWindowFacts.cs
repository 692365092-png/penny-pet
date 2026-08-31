using System;

namespace PennyPet
{
    internal sealed class DockWindowFacts
    {
        internal DockWindowFacts(string noteId, int x, int y,
            int width, int height, bool visible, bool topMost)
        {
            NoteId = noteId ?? String.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Visible = visible;
            TopMost = topMost;
        }

        internal string NoteId { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal bool Visible { get; private set; }
        internal bool TopMost { get; private set; }

        internal static DockWindowFacts FromData(StickyNoteData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new DockWindowFacts(source.Id, source.X, source.Y,
                source.Width, source.Height, source.Visible,
                source.AlwaysOnTop);
        }

        internal DockLayoutTarget ToTarget(int x, int y)
        {
            return new DockLayoutTarget(NoteId, x, y, Width, Height,
                Visible, TopMost);
        }

        internal static DockWindowFacts FromTarget(DockLayoutTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new DockWindowFacts(target.NoteId, target.X, target.Y,
                target.Width, target.Height, target.Visible, target.TopMost);
        }
    }

    internal sealed class DockLayoutTarget
    {
        internal DockLayoutTarget(string noteId, int x, int y,
            int width, int height, bool visible, bool topMost)
        {
            NoteId = noteId ?? String.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Visible = visible;
            TopMost = topMost;
        }

        internal string NoteId { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal bool Visible { get; private set; }
        internal bool TopMost { get; private set; }
    }
}
