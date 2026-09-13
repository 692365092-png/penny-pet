using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Legacy fields are read when entering the window lifecycle, never as
    // live layout input. Selecting recovery does not commit user preference.
    internal static class StickyPlacementRecovery
    {
        // Compatibility conversion only: v7 physical sizes keep their units
        // and the existing root-width / independent-height clamps. Both old
        // and current formats use the same host transaction after selection.
        internal static DockGroupReprojectPlan SelectDockPhysical(
            IList<StickyNoteData> ordered, DisplayTopologySnapshot topology, long planSequence)
        {
            StickyNoteData root = ordered[0];
            int width = Math.Max(280, Math.Min(900, root.Width));
            var header = new PhysicalRect(root.X, root.Y, width, 32);
            DisplaySurfaceSnapshot target = FindNearestSurface(topology, header);
            PhysicalRect work = target.WorkArea;
            DockPoint shift = StickyDockGeometry.CalculateHeaderReachableTranslation(
                new DockRect(header.Left, header.Top, header.Width, header.Height),
                new DockRect(work.Left, work.Top, work.Width, work.Height));
            var sizes = new List<DockSize>(ordered.Count);
            foreach (StickyNoteData member in ordered) sizes.Add(new DockSize(member.Width, member.Height));
            List<DockRect> layout = StickyDockGeometry.CalculateUnifiedDockLayout(
                sizes, root.X + shift.X, root.Y + shift.Y, width, 1F);
            var targets = new List<DockWindowTarget>(ordered.Count);
            for (int index = 0; index < ordered.Count; index++)
            {
                DockRect bounds = layout[index];
                targets.Add(new DockWindowTarget(ordered[index].Id,
                    new PhysicalRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height)));
            }
            return DockGroupReprojectPlan.RecoverPhysical(topology.Generation, planSequence,
                target.RuntimeSurfaceId, targets);
        }

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

        // Equivalent selection intent to Screen.FromRectangle, evaluated
        // against the captured topology: largest overlap, otherwise nearest.
        private static DisplaySurfaceSnapshot FindNearestSurface(
            DisplayTopologySnapshot topology, PhysicalRect rect)
        {
            DisplaySurfaceSnapshot overlap = LargestIntersection(topology, rect);
            if (overlap != null) return overlap;
            DisplaySurfaceSnapshot nearest = null;
            double bestDistance = Double.PositiveInfinity;
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                PhysicalRect bounds = surface.Bounds;
                double dx = Math.Max(0L, Math.Max((long)bounds.Left - rect.Right, (long)rect.Left - bounds.Right));
                double dy = Math.Max(0L, Math.Max((long)bounds.Top - rect.Bottom, (long)rect.Top - bounds.Bottom));
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance || (distance == bestDistance && surface.IsPrimary))
                {
                    nearest = surface;
                    bestDistance = distance;
                }
            }
            return nearest;
        }
    }
}
