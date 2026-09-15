using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Compatibility belongs to the file boundary. Legacy edges live only for
    // one load/import; runtime notes carry only the ordered group relation.
    internal static class StickyDockFileRelations
    {
        // File output only: derive v7 compatibility links without mutating
        // either the live model or its detached persistence snapshot.
        internal static Dictionary<string, string> BuildLegacyParents(
            IEnumerable<StickyNoteData> notes)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (List<StickyNoteData> group in IndexGroups(notes).Values)
            {
                group.Sort(StickyDockGroups.CompareOrder);
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
        internal static void Normalize(IList<StickyNoteData> notes,
            IReadOnlyDictionary<string, string> legacyParents = null)
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
                group.Sort(StickyDockGroups.CompareOrder);
                StickyDockGroups.ApplyOrderedGroup(group);
            }
            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string id in legacy.Keys) edges[id] = new List<string>();
            foreach (StickyNoteData note in legacy.Values)
            {
                string parent = ParentOf(legacyParents, note.Id);
                if (!String.IsNullOrEmpty(parent) && legacy.ContainsKey(parent))
                {
                    edges[note.Id].Add(parent);
                    edges[parent].Add(note.Id);
                }
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
                StickyDockGroups.ApplyOrderedGroup(OrderLegacyComponent(component, legacyParents));
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

        private static List<StickyNoteData> OrderLegacyComponent(List<StickyNoteData> component,
            IReadOnlyDictionary<string, string> legacyParents)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in component) ids.Add(note.Id);
            Dictionary<string, StickyNoteData> children = new Dictionary<string, StickyNoteData>(
                StringComparer.OrdinalIgnoreCase);
            StickyNoteData root = null;
            bool chain = true;
            foreach (StickyNoteData note in component)
            {
                string parent = ParentOf(legacyParents, note.Id);
                if (String.IsNullOrEmpty(parent) || !ids.Contains(parent))
                {
                    if (root != null) chain = false;
                    root = note;
                }
                else if (children.ContainsKey(parent)) chain = false;
                else children.Add(parent, note);
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

        private static string ParentOf(IReadOnlyDictionary<string, string> parents, string noteId)
        {
            string parent;
            return parents != null && parents.TryGetValue(noteId, out parent) ? parent : String.Empty;
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
