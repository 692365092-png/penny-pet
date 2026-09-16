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

        internal static List<StickyNoteData> BuildDockChainOrderFromNotes(
            IList<StickyNoteData> notes, StickyNoteData seed, bool visibleOnly)
        {
            return visibleOnly ? StickyDockGroups.GetVisibleGroup(notes, seed)
                : StickyDockGroups.GetOrderedGroup(notes, seed);
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

        internal static List<StickyNoteData> ExtractSingleDockMember(
            IList<StickyNoteData> ordered, StickyNoteData extracted)
        {
            List<StickyNoteData> remaining = new List<StickyNoteData>();
            StickyNoteData matched = null;
            if (ordered != null)
            {
                foreach (StickyNoteData note in ordered)
                {
                    if (note == null) continue;
                    if (extracted != null && String.Equals(note.Id,
                        extracted.Id, StringComparison.OrdinalIgnoreCase))
                        matched = note;
                    else remaining.Add(note);
                }
            }
            StickyDockGroups.ApplyOrderedGroup(remaining);
            StickyDockGroups.ClearMembership(matched ?? extracted);
            return remaining;
        }

        internal static List<StickyNoteData> MergeDockSnapshotsAfterParent(
            IList<StickyNoteData> targetSnapshot, StickyNoteData parent,
            IList<StickyNoteData> insertedSnapshot)
        {
            List<StickyNoteData> result = BuildMergedOrder(targetSnapshot, parent, insertedSnapshot);
            StickyDockGroups.ApplyOrderedGroup(result);
            return result;
        }

        internal static DockMergePlan PrepareMergeAfterParent(
            IList<StickyNoteData> targetSnapshot, StickyNoteData parent,
            IList<StickyNoteData> insertedSnapshot)
        {
            return new DockMergePlan(BuildMergedOrder(targetSnapshot, parent, insertedSnapshot));
        }

        private static List<StickyNoteData> BuildMergedOrder(
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
            IList<int> heights, int coordinateLimit)
        {
            long y = top;
            if (y < -coordinateLimit || y > coordinateLimit) return false;
            if (heights == null) return true;
            foreach (int value in heights)
            {
                int height = Math.Max(220, Math.Min(700, value));
                y += height;
                if (y < -coordinateLimit || y > coordinateLimit) return false;
            }
            return true;
        }

        internal static StickyNoteData FindActiveDockTail(
            IList<StickyNoteData> notes, IList<StickyNoteData> activeGroup,
            StickyNoteData seed)
        {
            if (seed == null) return null;
            HashSet<string> activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeGroup != null)
                foreach (StickyNoteData note in activeGroup)
                    if (note != null) activeIds.Add(note.Id);
            StickyNoteData tail = seed;
            foreach (StickyNoteData note in StickyDockGroups.GetVisibleGroup(notes, seed))
                if (activeIds.Contains(note.Id)) tail = note;
            return tail;
        }

        internal static bool CanDockBelow(int movingLeft, int movingTop,
            int movingWidth, int movingHeight, int targetLeft, int targetTop,
            int targetWidth, int targetHeight, int threshold)
        {
            int limit = Math.Max(4, threshold);
            int targetBottom = targetTop + targetHeight;
            if (Math.Abs(movingTop - targetBottom) > limit) return false;

            int movingRight = movingLeft + movingWidth;
            int targetRight = targetLeft + targetWidth;
            int overlap = Math.Min(movingRight, targetRight) -
                Math.Max(movingLeft, targetLeft);
            int narrowerWidth = Math.Min(movingWidth, targetWidth);
            int widerWidth = Math.Max(movingWidth, targetWidth);
            bool aligned = Math.Abs(movingLeft - targetLeft) <= limit ||
                Math.Abs(movingRight - targetRight) <= limit ||
                Math.Abs((movingLeft + movingRight) -
                    (targetLeft + targetRight)) <= limit * 2;
            bool differentWidths = widerWidth >= narrowerWidth * 3 / 2;
            return overlap >= Math.Max(48, narrowerWidth / 2) &&
                (aligned || differentWidths);
        }
    }
}
