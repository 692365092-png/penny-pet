using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Domain operations for changing a persisted dock stack. The Windows
    // layer decides when a drag or window event invokes these operations; it
    // does not own the ordering and membership rules themselves.
    internal static class StickyDockOperations
    {
        internal const int SplitHoldMilliseconds = 520;
        internal const int SplitPreHoldMovement = 7;
        internal const int DockCoordinateSafetyLimit = 30000;

        internal static List<StickyNoteData> SelectMoreCompleteDockOrder(
            IList<StickyNoteData> live, IList<StickyNoteData> stored)
        {
            List<StickyNoteData> liveCopy = live == null
                ? new List<StickyNoteData>() :
                new List<StickyNoteData>(live);
            List<StickyNoteData> storedCopy = stored == null
                ? new List<StickyNoteData>() :
                new List<StickyNoteData>(stored);
            // A newly inserted stack makes the live parent chain larger; a
            // temporarily broken parent link makes the saved group larger.
            return liveCopy.Count >= storedCopy.Count ? liveCopy : storedCopy;
        }

        internal static List<StickyNoteData> BuildDockChainOrderFromNotes(
            IList<StickyNoteData> notes, StickyNoteData seed,
            bool visibleOnly)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (seed == null) return result;
            if (!visibleOnly)
                return StickyDockGroups.GetOrderedGroup(notes, seed);

            Dictionary<string, StickyNoteData> visible =
                new Dictionary<string, StickyNoteData>(
                    StringComparer.OrdinalIgnoreCase);
            if (notes == null) return result;
            foreach (StickyNoteData note in notes)
                if (note != null && note.Visible &&
                    !String.IsNullOrEmpty(note.Id))
                    visible[note.Id] = note;

            StickyNoteData root = seed;
            HashSet<string> guard = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            while (root != null && guard.Add(root.Id) &&
                !String.IsNullOrEmpty(root.DockParentId))
            {
                StickyNoteData parent;
                if (!visible.TryGetValue(root.DockParentId, out parent)) break;
                root = parent;
            }

            guard.Clear();
            StickyNoteData current = root;
            while (current != null && guard.Add(current.Id))
            {
                result.Add(current);
                StickyNoteData child = null;
                foreach (StickyNoteData candidate in visible.Values)
                {
                    if (String.Equals(candidate.DockParentId, current.Id,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        child = candidate;
                        break;
                    }
                }
                current = child;
            }
            return result;
        }

        internal static bool ShouldRestoreWholeDockComponent(
            int storedComponentCount, bool anyMemberHidden)
        {
            // A request for a persisted group is a group-level operation even
            // during startup, when every member may already be marked visible.
            return storedComponentCount > 1;
        }

        internal static bool ShouldCollapseWholeDockGroup(int sourceIndex,
            int visibleComponentCount)
        {
            return visibleComponentCount > 1 && sourceIndex == 0;
        }

        internal static void RewireDockChainAfterMemberClose(
            StickyNoteData closing, StickyNoteData child)
        {
            if (closing == null) return;
            if (child != null) child.DockParentId = closing.DockParentId;
            closing.DockParentId = String.Empty;
        }

        internal static List<StickyNoteData> ExtractSingleDockMember(
            IList<StickyNoteData> ordered, StickyNoteData extracted)
        {
            List<StickyNoteData> remaining = new List<StickyNoteData>();
            if (ordered != null)
            {
                foreach (StickyNoteData note in ordered)
                    if (note != null && !Object.ReferenceEquals(note,
                        extracted)) remaining.Add(note);
            }
            StickyDockGroups.ApplyOrderedGroup(remaining);
            StickyDockGroups.ClearMembership(extracted);
            return remaining;
        }

        internal static void PreserveDockSlotForHiddenMember(
            IList<StickyNoteData> snapshot, StickyNoteData hidden)
        {
            if (hidden != null) hidden.Visible = false;
            StickyDockGroups.ApplyGroupSnapshot(snapshot);
            StickyDockGroups.RebuildVisibleParentChain(snapshot);
        }

        internal static void RewireDockChainForInsertion(
            StickyNoteData parent, StickyNoteData insertedHead,
            StickyNoteData insertedTail, StickyNoteData previousChild)
        {
            if (parent == null || insertedHead == null) return;
            StickyNoteData tail = insertedTail ?? insertedHead;
            insertedHead.DockParentId = parent.Id;
            if (previousChild != null)
                previousChild.DockParentId = tail.Id;
        }

        internal static List<StickyNoteData> MergeDockSnapshotsAfterParent(
            IList<StickyNoteData> targetSnapshot, StickyNoteData parent,
            IList<StickyNoteData> insertedSnapshot)
        {
            List<StickyNoteData> inserted = new List<StickyNoteData>();
            HashSet<string> insertedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (insertedSnapshot != null)
            {
                foreach (StickyNoteData note in insertedSnapshot)
                    if (note != null && insertedIds.Add(note.Id))
                        inserted.Add(note);
            }
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (targetSnapshot != null)
            {
                foreach (StickyNoteData note in targetSnapshot)
                    if (note != null && !insertedIds.Contains(note.Id))
                        result.Add(note);
            }
            int insertion = result.Count;
            if (parent != null)
            {
                int parentIndex = result.FindIndex(
                    delegate(StickyNoteData note)
                    {
                        return String.Equals(note.Id, parent.Id,
                            StringComparison.OrdinalIgnoreCase);
                    });
                if (parentIndex >= 0) insertion = parentIndex + 1;
            }
            result.InsertRange(insertion, inserted);
            StickyDockGroups.ApplyOrderedGroup(result);
            return result;
        }

        internal static bool CancelsDockSplitHold(double heldMilliseconds,
            int totalDx, int totalDy)
        {
            long dx = totalDx;
            long dy = totalDy;
            long threshold = SplitPreHoldMovement;
            return heldMilliseconds < SplitHoldMilliseconds &&
                dx * dx + dy * dy > threshold * threshold;
        }

        internal static bool IsDockSplitEligible(string parentId,
            int componentCount)
        {
            return componentCount > 1 && !String.IsNullOrEmpty(parentId);
        }

        internal static bool IsDockCoordinateRangeSafe(int top,
            IList<int> heights)
        {
            long y = top;
            if (y < -DockCoordinateSafetyLimit ||
                y > DockCoordinateSafetyLimit) return false;
            if (heights == null) return true;
            foreach (int value in heights)
            {
                int height = Math.Max(220, Math.Min(700, value));
                y += height;
                if (y < -DockCoordinateSafetyLimit ||
                    y > DockCoordinateSafetyLimit) return false;
            }
            return true;
        }

        internal static StickyNoteData FindActiveDockTail(
            IList<StickyNoteData> notes, IList<StickyNoteData> activeGroup,
            StickyNoteData seed)
        {
            if (seed == null) return null;
            HashSet<string> activeIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (activeGroup != null)
            {
                foreach (StickyNoteData note in activeGroup)
                    if (note != null) activeIds.Add(note.Id);
            }

            StickyNoteData tail = seed;
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            while (tail != null && visited.Add(tail.Id))
            {
                StickyNoteData child = null;
                if (notes != null)
                {
                    foreach (StickyNoteData note in notes)
                    {
                        if (note != null && activeIds.Contains(note.Id) &&
                            String.Equals(note.DockParentId, tail.Id,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            child = note;
                            break;
                        }
                    }
                }
                if (child == null) break;
                tail = child;
            }
            return tail ?? seed;
        }
    }
}
