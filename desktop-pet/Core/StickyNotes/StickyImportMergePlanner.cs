using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PennyPet
{
    // B1 merge planning is deliberately pure: it works on detached copies and
    // never mutates the repository or any live StickyNoteData instance.
    internal enum StickyImportActionKind
    {
        Add,
        SkipIdentical,
        PreserveConflictCopy
    }

    internal sealed class StickyImportAction
    {
        internal StickyImportAction(StickyImportActionKind kind,
            string importedNoteId, string resultNoteId, bool added)
        {
            Kind = kind;
            ImportedNoteId = importedNoteId ?? String.Empty;
            ResultNoteId = resultNoteId ?? String.Empty;
            Added = added;
        }

        internal StickyImportActionKind Kind { get; private set; }
        internal string ImportedNoteId { get; private set; }
        internal string ResultNoteId { get; private set; }
        internal bool Added { get; private set; }
    }

    internal sealed class StickyImportMergeResult
    {
        internal StickyImportMergeResult(List<StickyNoteData> mergedSnapshot,
            List<StickyImportAction> actions)
        {
            MergedSnapshot = mergedSnapshot;
            Actions = actions;
        }

        internal List<StickyNoteData> MergedSnapshot { get; private set; }
        internal List<StickyImportAction> Actions { get; private set; }

        internal int AddedCount
        {
            get
            {
                int count = 0;
                foreach (StickyImportAction action in Actions)
                    if (action.Added) count++;
                return count;
            }
        }

        internal int SkippedIdenticalCount
        {
            get
            {
                int count = 0;
                foreach (StickyImportAction action in Actions)
                    if (action.Kind == StickyImportActionKind.SkipIdentical)
                        count++;
                return count;
            }
        }

        internal int ConflictCount
        {
            get
            {
                int count = 0;
                foreach (StickyImportAction action in Actions)
                    if (action.Kind == StickyImportActionKind.PreserveConflictCopy)
                        count++;
                return count;
            }
        }
    }

    internal static class StickyImportMergePlanner
    {
        internal static StickyImportMergeResult Calculate(
            IList<StickyNoteData> currentSnapshot,
            IList<StickyNoteData> importedSnapshot)
        {
            List<StickyNoteData> merged = CloneNotes(currentSnapshot);
            Dictionary<string, StickyNoteData> currentById = BuildIndex(merged,
                "current snapshot");
            HashSet<string> existingCurrentIds = new HashSet<string>(
                currentById.Keys, StringComparer.OrdinalIgnoreCase);
            List<StickyNoteData> imported = CloneNotes(importedSnapshot);
            BuildIndex(imported, "backup");
            HashSet<string> partialImportedIds = FindPartialImportedIds(
                imported, existingCurrentIds);
            List<StickyImportAction> actions = new List<StickyImportAction>();

            foreach (StickyNoteData incoming in imported)
            {
                StickyNoteData effectiveIncoming = incoming.CloneForPersistence();
                if (partialImportedIds.Contains(incoming.Id))
                    StickyDockGroups.ClearMembership(effectiveIncoming);
                StickyNoteData existing;
                if (!currentById.TryGetValue(incoming.Id, out existing))
                {
                    StickyNoteData added = effectiveIncoming;
                    merged.Add(added);
                    currentById.Add(added.Id, added);
                    actions.Add(new StickyImportAction(
                        StickyImportActionKind.Add, incoming.Id, added.Id, true));
                    continue;
                }

                if (PersistedContentEquals(existing, effectiveIncoming))
                {
                    actions.Add(new StickyImportAction(
                        StickyImportActionKind.SkipIdentical, incoming.Id,
                        existing.Id, false));
                    continue;
                }

                string conflictId = FindConflictCopyId(incoming,
                    effectiveIncoming, currentById);
                StickyNoteData conflict;
                bool addedConflict = !currentById.TryGetValue(conflictId,
                    out conflict);
                if (addedConflict)
                {
                    conflict = effectiveIncoming;
                    conflict.Id = conflictId;
                    merged.Add(conflict);
                    currentById.Add(conflict.Id, conflict);
                }
                actions.Add(new StickyImportAction(
                    StickyImportActionKind.PreserveConflictCopy, incoming.Id,
                    conflictId, addedConflict));
            }

            return new StickyImportMergeResult(merged, actions);
        }

        internal static bool PersistedContentEquals(StickyNoteData left,
            StickyNoteData right)
        {
            if (left == null || right == null) return left == right;
            if (!String.Equals(left.Id, right.Id,
                StringComparison.OrdinalIgnoreCase)) return false;
            return String.Equals(CanonicalLine(left, true),
                CanonicalLine(right, true), StringComparison.Ordinal);
        }

        private static HashSet<string> FindPartialImportedIds(
            IList<StickyNoteData> imported,
            ISet<string> existingCurrentIds)
        {
            HashSet<string> partialIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> processed = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData seed in imported)
            {
                if (seed == null || String.IsNullOrEmpty(seed.Id) ||
                    !processed.Add(seed.Id)) continue;
                List<StickyNoteData> group = StickyDockGroups.GetOrderedGroup(
                    imported, seed);
                if (group.Count <= 1) continue;
                foreach (StickyNoteData member in group) processed.Add(member.Id);

                bool intersectsCurrent = false;
                foreach (StickyNoteData member in group)
                    if (existingCurrentIds.Contains(member.Id))
                    {
                        intersectsCurrent = true;
                        break;
                    }
                if (!intersectsCurrent) continue;
                foreach (StickyNoteData member in group)
                    partialIds.Add(member.Id);
            }
            return partialIds;
        }

        private static string FindConflictCopyId(StickyNoteData imported,
            StickyNoteData expectedCopy,
            IDictionary<string, StickyNoteData> occupied)
        {
            string fingerprint = (imported.Id ?? String.Empty).ToLowerInvariant() +
                "|" + CanonicalLine(imported, false);
            for (int suffix = 0; ; suffix++)
            {
                string candidate = DeterministicGuid(fingerprint + "|" +
                    suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
                StickyNoteData existing;
                if (!occupied.TryGetValue(candidate, out existing) ||
                    CanonicalPayloadEquals(existing, expectedCopy)) return candidate;
            }
        }

        private static bool CanonicalPayloadEquals(StickyNoteData left,
            StickyNoteData right)
        {
            return String.Equals(CanonicalLine(left, false),
                CanonicalLine(right, false), StringComparison.Ordinal);
        }

        private static string CanonicalLine(StickyNoteData note, bool includeId)
        {
            StickyNoteData copy = note.CloneForPersistence();
            if (!includeId) copy.Id = String.Empty;
            return StickyNoteCodec.SerializeLine(copy);
        }

        private static string DeterministicGuid(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                byte[] guidBytes = new byte[16];
                Buffer.BlockCopy(digest, 0, guidBytes, 0, guidBytes.Length);
                return new Guid(guidBytes).ToString("N");
            }
        }

        private static List<StickyNoteData> CloneNotes(
            IEnumerable<StickyNoteData> notes)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes == null) return result;
            foreach (StickyNoteData note in notes)
                if (note != null) result.Add(note.CloneForPersistence());
            return result;
        }

        private static Dictionary<string, StickyNoteData> BuildIndex(
            IEnumerable<StickyNoteData> notes, string sourceName)
        {
            Dictionary<string, StickyNoteData> result =
                new Dictionary<string, StickyNoteData>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in notes)
            {
                if (note == null || String.IsNullOrEmpty(note.Id))
                    throw new ArgumentException(sourceName +
                        " contains a note without an id.");
                if (result.ContainsKey(note.Id))
                    throw new ArgumentException(sourceName +
                        " contains duplicate note ids.");
                result.Add(note.Id, note);
            }
            return result;
        }
    }
}
