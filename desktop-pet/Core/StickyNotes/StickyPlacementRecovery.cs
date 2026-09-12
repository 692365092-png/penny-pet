using System;

namespace PennyPet
{
    // Legacy fields are read when entering the window lifecycle, never as
    // live layout input. Selecting recovery does not commit user preference.
    internal static class StickyPlacementRecovery
    {
        internal static WindowPlacementPlan SelectForShow(StickyNoteData note,
            DisplayTopologySnapshot topology)
        {
            if (note == null || topology == null ||
                !String.IsNullOrEmpty(note.DockGroupId)) return null;
            bool preferredValid = !String.IsNullOrWhiteSpace(
                    note.PreferredDisplayTargetKey) &&
                note.PreferredLocalLogicalWidth > 0 &&
                note.PreferredLocalLogicalHeight > 0;
            bool legacyValid = !String.IsNullOrWhiteSpace(note.DisplayId) &&
                note.LocalLogicalWidth > 0 && note.LocalLogicalHeight > 0;
            // Preserve the existing uninitialized-note visibility fallback.
            if (!preferredValid && !legacyValid) return null;
            DisplaySurfaceSnapshot surface = preferredValid
                ? topology.FindByTargetKey(note.PreferredDisplayTargetKey) : null;
            if (surface != null)
                return WindowPlacementPlan.OnSurface(topology, surface,
                    new LogicalRect { X = note.PreferredLocalLogicalX,
                        Y = note.PreferredLocalLogicalY,
                        Width = note.PreferredLocalLogicalWidth,
                        Height = note.PreferredLocalLogicalHeight });
            surface = legacyValid ? topology.FindByRuntimeGdiName(note.DisplayId) : null;
            if (surface != null)
                return WindowPlacementPlan.OnSurface(topology, surface,
                    new LogicalRect { X = note.LocalLogicalX, Y = note.LocalLogicalY,
                        Width = note.LocalLogicalWidth, Height = note.LocalLogicalHeight });
            if (note.Width <= 0 || note.Height <= 0) return null;
            PhysicalRect physical = new PhysicalRect(note.X, note.Y,
                note.Width, note.Height);
            DisplaySurfaceSnapshot nearest = LargestIntersection(topology,
                physical) ?? topology.PrimaryOrFirst();
            if (nearest == null) return null;
            int left = Math.Max(nearest.WorkArea.Left,
                Math.Min(physical.Left, nearest.WorkArea.Right - physical.Width));
            int top = Math.Max(nearest.WorkArea.Top,
                Math.Min(physical.Top, nearest.WorkArea.Bottom - physical.Height));
            return WindowPlacementPlan.RecoverPhysical(topology, nearest.WorkArea,
                new PhysicalRect(left, top, physical.Width, physical.Height));
        }

        private static DisplaySurfaceSnapshot LargestIntersection(
            DisplayTopologySnapshot topology, PhysicalRect rect)
        {
            DisplaySurfaceSnapshot best = null;
            long bestArea = 0;
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                long width = Math.Max(0L,
                    (long)Math.Min(surface.Bounds.Right, rect.Right) -
                    Math.Max(surface.Bounds.Left, rect.Left));
                long height = Math.Max(0L,
                    (long)Math.Min(surface.Bounds.Bottom, rect.Bottom) -
                    Math.Max(surface.Bounds.Top, rect.Top));
                long area = width * height;
                if (area > bestArea) { bestArea = area; best = surface; }
            }
            return best;
        }
    }
}
