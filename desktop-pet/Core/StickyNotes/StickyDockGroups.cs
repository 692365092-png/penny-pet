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
                note.DockParentId = String.Empty;
            }
        }

        internal static void ClearMembership(StickyNoteData note)
        {
            if (note == null) return;
            note.DockGroupId = String.Empty;
            note.DockGroupOrder = -1;
            note.DockParentId = String.Empty;
        }

        // File output only: derive v7 compatibility links without mutating
        // either the live model or its detached persistence snapshot.
        internal static Dictionary<string, string> BuildLegacyParents(
            IEnumerable<StickyNoteData> notes)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (List<StickyNoteData> group in IndexGroups(notes).Values)
            {
                group.Sort(CompareOrder);
                string previous = String.Empty;
                foreach (StickyNoteData note in group)
                {
                    result[note.Id] = note.Visible ? previous : String.Empty;
                    if (note.Visible) previous = note.Id;
                }
            }
            return result;
        }

        // Load/import only. Explicit groups win over stale parent links.
        // Ungrouped v7 data is migrated once, never consulted by live queries.
        internal static void NormalizeAll(IList<StickyNoteData> notes)
        {
            if (notes == null) return;
            Dictionary<string, List<StickyNoteData>> groups = IndexGroups(notes);
            Dictionary<string, StickyNoteData> legacy = new Dictionary<string, StickyNoteData>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in notes)
                if (note != null && !String.IsNullOrEmpty(note.Id) &&
                    String.IsNullOrEmpty(note.DockGroupId)) legacy[note.Id] = note;
            foreach (List<StickyNoteData> group in groups.Values)
            {
                group.Sort(CompareOrder);
                ApplyOrderedGroup(group);
            }
            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string id in legacy.Keys) edges[id] = new List<string>();
            foreach (StickyNoteData note in legacy.Values)
                if (!String.IsNullOrEmpty(note.DockParentId) && legacy.ContainsKey(note.DockParentId))
                {
                    edges[note.Id].Add(note.DockParentId);
                    edges[note.DockParentId].Add(note.Id);
                }
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData seed in legacy.Values)
            {
                if (!visited.Add(seed.Id)) continue;
                List<StickyNoteData> component = new List<StickyNoteData>();
                Queue<string> pending = new Queue<string>();
                pending.Enqueue(seed.Id);
                while (pending.Count > 0)
                {
                    string id = pending.Dequeue();
                    component.Add(legacy[id]);
                    foreach (string neighbor in edges[id])
                        if (visited.Add(neighbor)) pending.Enqueue(neighbor);
                }
                ApplyOrderedGroup(OrderLegacyComponent(component));
            }
        }

        private static Dictionary<string, List<StickyNoteData>> IndexGroups(
            IEnumerable<StickyNoteData> notes)
        {
            Dictionary<string, List<StickyNoteData>> groups =
                new Dictionary<string, List<StickyNoteData>>(StringComparer.OrdinalIgnoreCase);
            if (notes == null) return groups;
            foreach (StickyNoteData note in notes)
            {
                if (note == null || String.IsNullOrEmpty(note.DockGroupId)) continue;
                List<StickyNoteData> group;
                if (!groups.TryGetValue(note.DockGroupId, out group))
                    groups.Add(note.DockGroupId, group = new List<StickyNoteData>());
                group.Add(note);
            }
            return groups;
        }

        private static List<StickyNoteData> OrderLegacyComponent(List<StickyNoteData> component)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in component) ids.Add(note.Id);
            Dictionary<string, StickyNoteData> children = new Dictionary<string, StickyNoteData>(
                StringComparer.OrdinalIgnoreCase);
            StickyNoteData root = null;
            bool chain = true;
            foreach (StickyNoteData note in component)
            {
                if (String.IsNullOrEmpty(note.DockParentId) || !ids.Contains(note.DockParentId))
                {
                    if (root != null) chain = false;
                    root = note;
                }
                else if (children.ContainsKey(note.DockParentId)) chain = false;
                else children.Add(note.DockParentId, note);
            }
            if (chain && root != null)
            {
                List<StickyNoteData> ordered = new List<StickyNoteData>();
                while (root != null && ids.Remove(root.Id))
                {
                    ordered.Add(root);
                    children.TryGetValue(root.Id, out root);
                }
                if (ordered.Count == component.Count) return ordered;
            }
            // Corrupt historical cycles/forks need deterministic recovery.
            // Geometry is only a migration heuristic, never a live tie-breaker.
            component.Sort(CompareLegacyPosition);
            return component;
        }

        private static int CompareOrder(StickyNoteData left, StickyNoteData right)
        {
            int order = left.DockGroupOrder.CompareTo(right.DockGroupOrder);
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        }

        private static int CompareLegacyPosition(StickyNoteData left, StickyNoteData right)
        {
            int value = left.Y.CompareTo(right.Y);
            if (value != 0) return value;
            value = left.X.CompareTo(right.X);
            if (value != 0) return value;
            value = left.TabOrder.CompareTo(right.TabOrder);
            if (value != 0) return value;
            value = left.CreatedUtcTicks.CompareTo(right.CreatedUtcTicks);
            return value != 0 ? value : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        }
    }
}
