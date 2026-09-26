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
            IEnumerable<StickyNoteData> notes, StickyNoteData seed, bool visibleOnly)
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
            var notes = new Dictionary<string, StickyNoteData>(StringComparer.OrdinalIgnoreCase);
            var ids = new List<string>();
            StickyNoteData matched = null;
            if (ordered != null)
                foreach (StickyNoteData note in ordered)
                    if (note != null) { notes[note.Id] = note; ids.Add(note.Id); }
            if (extracted != null) notes.TryGetValue(extracted.Id, out matched);
            var remaining = new List<StickyNoteData>();
            foreach (string id in DockOrderRules.Remove(ids, extracted == null ? null : extracted.Id))
                remaining.Add(notes[id]);
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
            var notes = new Dictionary<string, StickyNoteData>(StringComparer.OrdinalIgnoreCase);
            var targetIds = new List<string>();
            var insertedIds = new List<string>();
            var insertedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (targetSnapshot != null)
                foreach (StickyNoteData note in targetSnapshot)
                    if (note != null) { notes[note.Id] = note; targetIds.Add(note.Id); }
            if (insertedSnapshot != null)
                foreach (StickyNoteData note in insertedSnapshot)
                    if (note != null && insertedSet.Add(note.Id))
                    { notes[note.Id] = note; insertedIds.Add(note.Id); }
            var result = new List<StickyNoteData>();
            foreach (string id in DockOrderRules.MergeAfter(targetIds, parent == null ? null : parent.Id, insertedIds))
                result.Add(notes[id]);
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
            IEnumerable<StickyNoteData> notes, IList<StickyNoteData> activeGroup,
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

        internal static string FindSnapTarget(DockWindowTarget source,
            IEnumerable<DockWindowTarget> candidates, int threshold)
        {
            string best = null;
            long bestScore = Int64.MaxValue;
            PhysicalRect moving = source.PhysicalBounds;
            foreach (DockWindowTarget candidate in candidates)
            {
                PhysicalRect target = candidate.PhysicalBounds;
                if (String.Equals(source.NoteId, candidate.NoteId, StringComparison.OrdinalIgnoreCase) ||
                    !CanDockBelow(moving.Left, moving.Top, moving.Width, moving.Height,
                        target.Left, target.Top, target.Width, target.Height, threshold)) continue;
                long score = Math.Abs((long)moving.Top - ((long)target.Top + target.Height)) * 10 +
                    Math.Min(Math.Abs((long)moving.Left - target.Left),
                        Math.Abs(((long)moving.Left + moving.Width) - ((long)target.Left + target.Width)));
                if (score >= bestScore) continue;
                best = candidate.NoteId;
                bestScore = score;
            }
            return best;
        }

        internal static bool CanDockBelow(int movingLeft, int movingTop,
            int movingWidth, int movingHeight, int targetLeft, int targetTop,
            int targetWidth, int targetHeight, int threshold)
        {
            int limit = Math.Max(4, threshold);
            long targetBottom = (long)targetTop + targetHeight;
            if (Math.Abs(movingTop - targetBottom) > limit) return false;

            long movingRight = (long)movingLeft + movingWidth;
            long targetRight = (long)targetLeft + targetWidth;
            long overlap = Math.Min(movingRight, targetRight) -
                Math.Max(movingLeft, targetLeft);
            int narrowerWidth = Math.Min(movingWidth, targetWidth);
            int widerWidth = Math.Max(movingWidth, targetWidth);
            bool aligned = Math.Abs((long)movingLeft - targetLeft) <= limit ||
                Math.Abs(movingRight - targetRight) <= limit ||
                Math.Abs((movingLeft + movingRight) -
                    (targetLeft + targetRight)) <= (long)limit * 2;
            bool differentWidths = widerWidth >= (long)narrowerWidth * 3 / 2;
            return overlap >= Math.Max(48, narrowerWidth / 2) &&
                (aligned || differentWidths);
        }
    }
}
