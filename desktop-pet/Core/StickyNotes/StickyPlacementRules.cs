using System;
using System.Collections.Generic;

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
        internal static bool TryGetLogicalFacts(WindowFacts facts,
            DisplayTopologySnapshot capturedTopology, out LogicalRect logical)
        {
            logical = new LogicalRect();
            if (facts == null || capturedTopology == null ||
                facts.TopologyGeneration != capturedTopology.Generation) return false;
            DisplaySurfaceSnapshot surface = capturedTopology.FindByTargetKey(
                facts.ActiveTargetKey) ?? capturedTopology.FindByRuntimeGdiName(facts.RuntimeGdiName);
            if (surface == null) return false;
            LogicalPoint point = DisplayGeometry.PhysicalToLocal(
                facts.PhysicalBounds.Left, facts.PhysicalBounds.Top,
                surface.Bounds.Left, surface.Bounds.Top, facts.Scale);
            logical = new LogicalRect { X = point.X, Y = point.Y,
                Width = DisplayGeometry.PhysicalLengthToLogical(facts.PhysicalBounds.Width, facts.Scale),
                Height = DisplayGeometry.PhysicalLengthToLogical(facts.PhysicalBounds.Height, facts.Scale) };
            return logical.Width > 0 && logical.Height > 0;
        }

        // No persisted note enters the live planner. Every member contributes
        // actual pixels interpreted with that HWND's own DPI.
        internal static bool TryBuildLiveDockState(IList<WindowFacts> orderedFacts,
            WindowFacts source, DisplayTopologySnapshot topology, out DockGroupLogicalState state)
        {
            state = null;
            LogicalRect sourceLocal;
            if (orderedFacts == null || orderedFacts.Count == 0 ||
                !TryGetLogicalFacts(source, topology, out sourceLocal)) return false;
            List<DockLogicalMember> members = new List<DockLogicalMember>(orderedFacts.Count);
            int sourceIndex = -1;
            int rootY = sourceLocal.Y;
            foreach (WindowFacts facts in orderedFacts)
            {
                if (facts == null || facts.TopologyGeneration != topology.Generation) return false;
                int height = DisplayGeometry.PhysicalLengthToLogical(facts.PhysicalBounds.Height, facts.Scale);
                if (height <= 0 || String.IsNullOrWhiteSpace(facts.WindowId)) return false;
                if (String.Equals(facts.WindowId, source.WindowId, StringComparison.OrdinalIgnoreCase))
                    sourceIndex = members.Count;
                else if (sourceIndex < 0) rootY -= height;
                members.Add(new DockLogicalMember(facts.WindowId, sourceLocal.Width, height));
            }
            if (sourceIndex < 0) return false;
            try {
                state = new DockGroupLogicalState(new LogicalPoint { X = sourceLocal.X, Y = rootY }, members);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

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

        // Derives a durable placement preference strictly from capture-time
        // facts plus the capture-time topology snapshot. No other topology
        // generation may participate: geometry captured at generation G is
        // always interpreted against G.
        internal static bool TryBuildPreferredPlacement(WindowFacts facts,
            DisplayTopologySnapshot topology, string existingPreferredKey,
            out WindowPlacementPreference preference)
        {
            preference = null;
            if (facts == null || topology == null ||
                facts.TopologyGeneration != topology.Generation) return false;
            DisplaySurfaceSnapshot surface =
                topology.FindByTargetKey(facts.ActiveTargetKey);
            if (surface == null)
                surface = topology.FindByRuntimeGdiName(
                    facts.RuntimeGdiName);
            if (surface == null) return false;
            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, existingPreferredKey);
            if (String.IsNullOrEmpty(key)) return false;
            preference = StickyPlacementMath.PreferenceFromPhysicalRect(
                key, surface.Bounds.Left, surface.Bounds.Top, facts.Scale,
                facts.PhysicalBounds);
            return true;
        }
    }
}
