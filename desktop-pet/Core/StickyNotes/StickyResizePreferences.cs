using System;

namespace PennyPet
{
    internal enum DockResizeKind { Divider, Horizontal }

    internal static class StickyResizePreferences
    {
        // Changed dimensions use this HWND's facts and DPI; dimensions outside
        // the gesture retain the member's existing durable intent.
        internal static bool TryBuild(StickyNoteData note, WindowFacts facts,
            DisplayTopologySnapshot topology, DockResizeKind kind, bool source,
            out WindowPlacementPreference preference)
        {
            preference = null;
            if (note == null || facts == null ||
                !String.Equals(note.Id, facts.WindowId, StringComparison.OrdinalIgnoreCase) ||
                !StickyPlacementRules.TryBuildPreferredPlacement(facts, topology,
                    note.PreferredDisplayTargetKey, out preference)) return false;
            LogicalRect actual = preference.LocalLogicalRect;
            bool samePreferredSurface = !String.IsNullOrWhiteSpace(note.PreferredDisplayTargetKey) &&
                String.Equals(preference.PreferredTargetKey, note.PreferredDisplayTargetKey, StringComparison.OrdinalIgnoreCase) &&
                note.PreferredLocalLogicalWidth > 0 && note.PreferredLocalLogicalHeight > 0;
            if (kind == DockResizeKind.Divider && samePreferredSurface)
                actual = new LogicalRect { X = note.PreferredLocalLogicalX, Y = note.PreferredLocalLogicalY,
                    Width = note.PreferredLocalLogicalWidth, Height = actual.Height };
            else if (kind == DockResizeKind.Horizontal && !source && samePreferredSurface)
                actual = new LogicalRect { X = actual.X, Y = note.PreferredLocalLogicalY,
                    Width = actual.Width, Height = note.PreferredLocalLogicalHeight };
            // On another surface the old display-local Y is not meaningful;
            // a source corner resize may also change Y/height, so keep its full rect.
            preference = new WindowPlacementPreference(preference.PreferredTargetKey, actual);
            return preference.IsValid;
        }
    }
}
