using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Plans one restore request per visible persisted Dock component. Platform
    // code decides how and when each request creates native windows.
    internal static class StartupRestorePlanner
    {
        internal static bool CanReleaseLoading(bool uiReady, bool artReady)
        {
            return uiReady && artReady;
        }

        internal static List<StickyNoteData> BuildVisibleRestoreSeeds(
            IList<StickyNoteData> notes)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes == null) return result;
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in notes)
            {
                if (note == null || !note.Visible ||
                    String.IsNullOrEmpty(note.Id) || restored.Contains(note.Id))
                    continue;
                List<StickyNoteData> group = StickyDockGroups.GetOrderedGroup(
                    notes, note);
                foreach (StickyNoteData member in group)
                    if (member != null && !String.IsNullOrEmpty(member.Id))
                        restored.Add(member.Id);
                result.Add(note);
            }
            return result;
        }
    }
}
