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
            List<ImportedTabOrderEntry> appendedNotes =
                new List<ImportedTabOrderEntry>();

            foreach (StickyNoteData incoming in imported)
            {
                StickyNoteData effectiveIncoming = incoming.CloneForPersistence();
                // Import visibility is a runtime placement policy, not an
                // identity difference. New and conflict copies always start
                // hidden, while an existing note keeps its current visibility.
                effectiveIncoming.Visible = false;
                // Reminder records live in settings, not in the portable
                // sticky-note contract.  Never create a note-side phantom
                // reminder for an imported add or conflict copy.
                effectiveIncoming.ReminderUtcTicks = 0;
                if (partialImportedIds.Contains(incoming.Id))
                    StickyDockGroups.ClearMembership(effectiveIncoming);
                StickyNoteData existing;
                if (!currentById.TryGetValue(incoming.Id, out existing))
                {
                    StickyNoteData added = effectiveIncoming;
                    merged.Add(added);
                    currentById.Add(added.Id, added);
                    appendedNotes.Add(new ImportedTabOrderEntry(added, incoming));
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
                    appendedNotes.Add(new ImportedTabOrderEntry(conflict, incoming));
                }
                actions.Add(new StickyImportAction(
                    StickyImportActionKind.PreserveConflictCopy, incoming.Id,
                    conflictId, addedConflict));
            }

            AppendImportedTabOrders(merged, appendedNotes);

            return new StickyImportMergeResult(merged, actions);
        }

        internal static bool PersistedContentEquals(StickyNoteData left,
            StickyNoteData right)
        {
            if (left == null || right == null) return left == right;
            if (!String.Equals(left.Id, right.Id,
                StringComparison.OrdinalIgnoreCase)) return false;
            return String.Equals(BuildLogicalContentFingerprint(left),
                BuildLogicalContentFingerprint(right),
                StringComparison.Ordinal);
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
                "|" + BuildLogicalContentFingerprint(imported);
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
            return String.Equals(BuildLogicalContentFingerprint(left),
                BuildLogicalContentFingerprint(right), StringComparison.Ordinal);
        }

        private sealed class ImportedTabOrderEntry
        {
            internal ImportedTabOrderEntry(StickyNoteData note,
                StickyNoteData source)
            {
                Note = note;
                Source = source;
            }

            internal StickyNoteData Note { get; private set; }
            internal StickyNoteData Source { get; private set; }
        }

        private static void AppendImportedTabOrders(
            IList<StickyNoteData> merged,
            List<ImportedTabOrderEntry> appendedNotes)
        {
            if (appendedNotes == null || appendedNotes.Count == 0) return;
            int maximum = -1;
            if (merged != null)
                foreach (StickyNoteData note in merged)
                {
                    bool imported = false;
                    foreach (ImportedTabOrderEntry entry in appendedNotes)
                        if (Object.ReferenceEquals(entry.Note, note))
                        {
                            imported = true;
                            break;
                        }
                    if (!imported && note != null)
                        maximum = Math.Max(maximum, note.TabOrder);
                }
            appendedNotes.Sort(delegate(ImportedTabOrderEntry left,
                ImportedTabOrderEntry right)
            {
                int result = left.Source.TabOrder.CompareTo(right.Source.TabOrder);
                if (result != 0) return result;
                result = left.Source.CreatedUtcTicks.CompareTo(
                    right.Source.CreatedUtcTicks);
                if (result != 0) return result;
                return StringComparer.OrdinalIgnoreCase.Compare(
                    left.Source.Id, right.Source.Id);
            });
            foreach (ImportedTabOrderEntry entry in appendedNotes)
                entry.Note.TabOrder = ++maximum;
        }

        private static string BuildLogicalContentFingerprint(StickyNoteData note)
        {
            if (note == null) return String.Empty;
            StringBuilder builder = new StringBuilder();
            AppendFingerprint(builder, note.Title);
            AppendFingerprint(builder, note.Text);
            AppendFingerprint(builder, StickyNoteCodec.NormalizeRtf(
                note.RichTextRtf));
            AppendFingerprint(builder, note.IsTodoList ? "1" : "0");
            AppendFingerprint(builder, note.IsSchedule ? "1" : "0");
            AppendFingerprint(builder, note.FontFamilyName == null
                ? String.Empty : note.FontFamilyName.Trim());
            AppendFingerprint(builder, note.FontSizeTwips.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendFingerprint(builder, note.ColorArgb.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendFingerprint(builder, note.BackgroundOpacityPercent.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AppendFingerprint(builder, note.TextColorArgb.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            builder.Append("todo[");
            foreach (StickyTodoItem item in note.TodoItems)
            {
                if (item == null) continue;
                AppendFingerprint(builder, ((int)item.State).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                AppendFingerprint(builder, item.IsPinned ? "1" : "0");
                AppendFingerprint(builder, item.Text);
            }
            builder.Append("]schedule[");
            foreach (StickyScheduleItem item in note.ScheduleItems)
            {
                if (item == null) continue;
                AppendFingerprint(builder, item.TargetDate.Ticks.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                AppendFingerprint(builder, item.IsPinned ? "1" : "0");
                AppendFingerprint(builder, item.Text);
            }
            builder.Append(']');
            return builder.ToString();
        }

        private static void AppendFingerprint(StringBuilder builder,
            string value)
        {
            string text = value ?? String.Empty;
            builder.Append(text.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':').Append(text).Append(';');
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
