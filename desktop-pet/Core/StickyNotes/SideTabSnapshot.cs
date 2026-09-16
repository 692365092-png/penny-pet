using System;

namespace PennyPet
{
    // Detached lightweight data for platform SideTab UI. It is not canonical
    // StickyNoteData and is not a persistence owner.
    internal sealed class SideTabSnapshot
    {
        private SideTabSnapshot(StickyNoteData source)
        {
            NoteId = source.Id ?? String.Empty;
            DisplayTitle = source.DisplayTitle;
            ColorArgb = source.ColorArgb;
            IsTodoList = source.IsTodoList;
            IsSchedule = source.IsSchedule;
            Visible = source.Visible;
        }

        internal string NoteId { get; private set; }
        internal string DisplayTitle { get; private set; }
        internal int ColorArgb { get; private set; }
        internal bool IsTodoList { get; private set; }
        internal bool IsSchedule { get; private set; }
        internal bool Visible { get; private set; }

        internal static SideTabSnapshot FromData(StickyNoteData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new SideTabSnapshot(source);
        }
    }
}
