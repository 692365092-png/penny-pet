using System;

namespace PennyPet
{
    // Model-only concurrency token for R23. Content, reminder and appearance
    // fields are intentionally absent so unrelated editing cannot invalidate
    // a completed Dock gesture.
    internal static class StickyDockCommitVersion
    {
        internal static long Compute(StickyNoteData note)
        {
            if (note == null) return Int64.MinValue;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Action<long> mix = delegate(long value)
                {
                    hash ^= (ulong)value;
                    hash *= 1099511628211UL;
                };
                string group =
                    note.DockGroupId ?? String.Empty;
                foreach (char value in group) mix(value);
                mix(note.DockGroupOrder);
                mix(note.Visible ? 1 : 0);
                mix(note.X);
                mix(note.Y);
                mix(note.Width);
                mix(note.Height);
                return (long)hash;
            }
        }
    }
}
