using System;

namespace PennyPet
{
    // Pure value snapshot for side tabs. Side tabs may consume this data but
    // must not receive hosted WPF windows or inspect WPF DispatcherObjects.
    internal sealed class SideTabSnapshot
    {
        private SideTabSnapshot(StickyNoteData source)
        {
            NoteId = source.Id ?? String.Empty;
            Title = source.Title ?? String.Empty;
            DisplayTitle = source.DisplayTitle;
            ColorArgb = source.ColorArgb;
            IsTodoList = source.IsTodoList;
            IsSchedule = source.IsSchedule;
            Visible = source.Visible;
        }

        internal string NoteId { get; private set; }
        internal string Title { get; private set; }
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

        internal StickyNoteData ToDisplayData()
        {
            StickyNoteData data = new StickyNoteData();
            data.Id = NoteId;
            data.Title = Title;
            data.ColorArgb = ColorArgb;
            data.IsTodoList = IsTodoList;
            data.IsSchedule = IsSchedule;
            data.Visible = Visible;
            return data;
        }
    }
}
