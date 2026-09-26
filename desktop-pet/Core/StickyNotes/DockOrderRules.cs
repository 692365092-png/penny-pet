using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Detached ids include hidden slots. The same order is used by preview
    // and commit; applying membership to live notes remains an owner command.
    internal static class DockOrderRules
    {
        internal static List<string> MergeAfter(IEnumerable<string> target,
            string parentId, IEnumerable<string> inserted)
        {
            var added = new List<string>();
            var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in inserted)
                if (addedIds.Add(id)) added.Add(id);
            var result = new List<string>();
            foreach (string id in target)
                if (!addedIds.Contains(id)) result.Add(id);
            int parentIndex = result.FindIndex(id => String.Equals(id, parentId,
                StringComparison.OrdinalIgnoreCase));
            result.InsertRange(parentIndex < 0 ? result.Count : parentIndex + 1, added);
            return result;
        }

        internal static List<string> Remove(IEnumerable<string> members, string removedId)
        {
            var result = new List<string>();
            foreach (string id in members)
                if (!String.Equals(id, removedId, StringComparison.OrdinalIgnoreCase)) result.Add(id);
            return result;
        }
    }
}
