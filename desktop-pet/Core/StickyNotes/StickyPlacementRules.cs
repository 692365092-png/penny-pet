using System;

namespace PennyPet
{
    // Why a window is being placed. Only user-gesture / user-initiated
    // reasons may update the durable preferred placement; programmatic
    // restore, temporary rehome, dock follower moves and recovery must never
    // overwrite what the user chose.
    internal enum PlacementReason
    {
        UserMoveCommit,
        UserResizeCommit,
        Spawn,
        Restore,
        TemporaryRehome,
        PreferredDisplayReturned,
        DockLiveFollower,
        DockCommit,
        ExpandAndTile,
        Recovery
    }

    internal static class StickyPlacementRules
    {
        internal static bool CanCommitPreferred(PlacementReason reason)
        {
            switch (reason)
            {
                case PlacementReason.UserMoveCommit:
                case PlacementReason.UserResizeCommit:
                case PlacementReason.Spawn:
                case PlacementReason.DockCommit:
                case PlacementReason.ExpandAndTile:
                    return true;
                default:
                    return false;
            }
        }

        // v10 -> v11 runtime migration (case A): the persisted DisplayId is a
        // runtime GDI name, so resolving it against the live topology upgrades
        // the v10 display-local rect into a durable preferred identity. Never
        // overwrite an existing preference and never guess a durable identity
        // when the saved display is not resolvable; those notes fall back to
        // their physical rect and adopt the actually-shown position later.
        internal static bool MigrateV10Preferred(StickyNoteData note,
            DisplayTopologySnapshot topology)
        {
            if (note == null || topology == null) return false;
            if (!String.IsNullOrWhiteSpace(note.PreferredDisplayTargetKey))
                return false;
            if (String.IsNullOrWhiteSpace(note.DisplayId) ||
                note.LocalLogicalWidth <= 0 ||
                note.LocalLogicalHeight <= 0) return false;
            DisplaySurfaceSnapshot surface =
                topology.FindByRuntimeGdiName(note.DisplayId);
            if (surface == null) return false;
            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, null);
            if (String.IsNullOrEmpty(key)) return false;
            note.PreferredDisplayTargetKey = key;
            note.PreferredLocalLogicalX = note.LocalLogicalX;
            note.PreferredLocalLogicalY = note.LocalLogicalY;
            note.PreferredLocalLogicalWidth = note.LocalLogicalWidth;
            note.PreferredLocalLogicalHeight = note.LocalLogicalHeight;
            return true;
        }
    }
}
