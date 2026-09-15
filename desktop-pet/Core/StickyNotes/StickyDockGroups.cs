using System;
using System.Collections.Generic;

namespace PennyPet
{
    // One ordered relation, including hidden members. GroupId/Order is its
    // v8-v11 representation; there is no independently maintained live chain.
    internal static class StickyDockGroups
    {
        internal static List<StickyNoteData> GetOrderedGroup(
            IList<StickyNoteData> notes, StickyNoteData seed)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes == null || seed == null) return result;
            foreach (StickyNoteData note in notes)
            {
                if (note == null) continue;
                if (String.IsNullOrEmpty(seed.DockGroupId)
                    ? String.Equals(note.Id, seed.Id, StringComparison.OrdinalIgnoreCase)
                    : String.Equals(note.DockGroupId, seed.DockGroupId,
                        StringComparison.OrdinalIgnoreCase)) result.Add(note);
            }
            result.Sort(CompareOrder);
            return result;
        }

        internal static List<StickyNoteData> GetVisibleGroup(
            IList<StickyNoteData> notes, StickyNoteData seed)
        {
            List<StickyNoteData> result = GetOrderedGroup(notes, seed);
            result.RemoveAll(note => !note.Visible);
            return result;
        }

        internal static StickyNoteData GetVisibleNeighbor(
            IList<StickyNoteData> notes, StickyNoteData seed, int direction)
        {
            if (seed == null) return null;
            List<StickyNoteData> visible = GetVisibleGroup(notes, seed);
            int index = visible.FindIndex(note => String.Equals(note.Id,
                seed.Id, StringComparison.OrdinalIgnoreCase));
            int neighbor = index + direction;
            return index >= 0 && neighbor >= 0 && neighbor < visible.Count
                ? visible[neighbor] : null;
        }

        internal static void ApplyOrderedGroup(IList<StickyNoteData> ordered)
        {
            if (ordered == null || ordered.Count == 0) return;
            // Validate the complete transition before modifying any member.
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in ordered)
                if (note == null || String.IsNullOrEmpty(note.Id) || !ids.Add(note.Id))
                    throw new ArgumentException("A Dock order needs unique note ids.", nameof(ordered));
            if (ordered.Count == 1) { ClearMembership(ordered[0]); return; }
            string groupId = ordered[0].Id;
            for (int index = 0; index < ordered.Count; index++)
            {
                StickyNoteData note = ordered[index];
                note.DockGroupId = groupId;
                note.DockGroupOrder = index;
            }
        }

        internal static void ClearMembership(StickyNoteData note)
        {
            if (note == null) return;
            note.DockGroupId = String.Empty;
            note.DockGroupOrder = -1;
        }

        internal static int CompareOrder(StickyNoteData left, StickyNoteData right)
        {
            int order = left.DockGroupOrder.CompareTo(right.DockGroupOrder);
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        }

    }
}
