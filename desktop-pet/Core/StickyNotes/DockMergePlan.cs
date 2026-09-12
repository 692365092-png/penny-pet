using System;
using System.Collections.Generic;

namespace PennyPet
{
    // A detached membership edit, retained by the gesture until its final
    // window batch succeeds. It never holds mutable repository note objects.
    internal sealed class DockMergePlan
    {
        private readonly Member[] _members;

        internal DockMergePlan(IList<StickyNoteData> ordered)
        {
            _members = new Member[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) _members[i] = new Member(ordered[i]);
        }

        internal bool TryResolve(IList<StickyNoteData> current,
            out List<StickyNoteData> ordered)
        {
            ordered = new List<StickyNoteData>();
            Dictionary<string, StickyNoteData> byId = new Dictionary<string, StickyNoteData>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> expectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> expectedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Member member in _members)
            {
                expectedIds.Add(member.Id);
                if (!String.IsNullOrEmpty(member.GroupId)) expectedGroups.Add(member.GroupId);
            }
            foreach (StickyNoteData note in current)
            {
                if (expectedGroups.Contains(note.DockGroupId) && !expectedIds.Contains(note.Id)) return false;
                byId.Add(note.Id, note);
            }
            foreach (Member expected in _members)
            {
                StickyNoteData note;
                if (!byId.TryGetValue(expected.Id, out note) ||
                    !String.Equals(note.DockGroupId, expected.GroupId, StringComparison.OrdinalIgnoreCase) ||
                    note.DockGroupOrder != expected.Order || note.Visible != expected.Visible)
                {
                    ordered.Clear();
                    return false;
                }
                ordered.Add(note);
            }
            return true;
        }

        internal bool TryCommit(IList<StickyNoteData> current)
        {
            List<StickyNoteData> ordered;
            if (!TryResolve(current, out ordered)) return false;
            StickyDockGroups.ApplyOrderedGroup(ordered);
            return true;
        }

        private sealed class Member
        {
            internal readonly string Id;
            internal readonly string GroupId;
            internal readonly int Order;
            internal readonly bool Visible;
            internal Member(StickyNoteData note)
            {
                Id = note.Id; GroupId = note.DockGroupId;
                Order = note.DockGroupOrder; Visible = note.Visible;
            }
        }
    }
}
