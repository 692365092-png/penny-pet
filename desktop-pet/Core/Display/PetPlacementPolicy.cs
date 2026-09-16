using System;

namespace PennyPet
{
    internal static class PetPlacementPolicy
    {
        internal const int DefaultMarginLogical = 24;

        // Active Pet drag rebase across a DPI handoff: convert the old
        // cursor-relative grab offset to the same logical grab point and
        // project it to the new DPI, so the next MouseMove delta restarts
        // from zero instead of reapplying the pre-transition displacement.
        internal static PhysicalPoint RebaseActiveDragTopLeft(
            PhysicalPoint oldTopLeft,
            PhysicalPoint oldCursor,
            PhysicalPoint newCursor,
            int oldDpi,
            int newDpi)
        {
            int safeOldDpi = Math.Max(1, oldDpi);
            int safeNewDpi = Math.Max(1, newDpi);

            int oldOffsetX = oldCursor.X - oldTopLeft.X;
            int oldOffsetY = oldCursor.Y - oldTopLeft.Y;

            int newOffsetX = (int)Math.Round(
                oldOffsetX * safeNewDpi / (double)safeOldDpi,
                MidpointRounding.AwayFromZero);
            int newOffsetY = (int)Math.Round(
                oldOffsetY * safeNewDpi / (double)safeOldDpi,
                MidpointRounding.AwayFromZero);

            return new PhysicalPoint
            {
                X = newCursor.X - newOffsetX,
                Y = newCursor.Y - newOffsetY
            };
        }

        internal static bool TryBuildPreferredPoint(
            WindowFacts facts,
            DisplayTopologySnapshot topology,
            string existingPreferredKey,
            out string preferredTargetKey,
            out LogicalPoint localPoint)
        {
            preferredTargetKey = String.Empty;
            localPoint = new LogicalPoint();

            if (facts == null || topology == null ||
                facts.TopologyGeneration != topology.Generation ||
                facts.Dpi <= 0)
                return false;

            DisplaySurfaceSnapshot surface =
                topology.FindByRuntimeGdiName(facts.RuntimeGdiName);

            if (surface == null) return false;

            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, existingPreferredKey);

            if (String.IsNullOrWhiteSpace(key))
                return false;

            localPoint = DisplayGeometry.PhysicalToLocal(
                facts.PhysicalBounds.Left,
                facts.PhysicalBounds.Top,
                surface.Bounds.Left,
                surface.Bounds.Top,
                facts.Dpi / 96.0);

            preferredTargetKey = key;
            return true;
        }

        internal static PhysicalPoint ProjectLocalPoint(
            LogicalPoint local,
            DisplaySurfaceSnapshot surface,
            int actualDpi)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));
            if (actualDpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(actualDpi));

            return DisplayGeometry.LocalToPhysical(
                local.X, local.Y,
                surface.Bounds.Left,
                surface.Bounds.Top,
                actualDpi / 96.0);
        }

        internal static PhysicalPoint ClampTopLeft(
            PhysicalPoint requested,
            PhysicalRect workArea,
            int physicalWidth,
            int physicalHeight)
        {
            int width = Math.Max(1, physicalWidth);
            int height = Math.Max(1, physicalHeight);
            int maxX = Math.Max(workArea.Left, workArea.Right - width);
            int maxY = Math.Max(workArea.Top, workArea.Bottom - height);

            return new PhysicalPoint
            {
                X = Math.Max(workArea.Left,
                    Math.Min(requested.X, maxX)),
                Y = Math.Max(workArea.Top,
                    Math.Min(requested.Y, maxY))
            };
        }

        internal static PhysicalPoint DefaultBottomRight(
            PhysicalRect workArea,
            int physicalWidth,
            int physicalHeight,
            int actualDpi)
        {
            int margin = ScaleLogicalLength(
                DefaultMarginLogical, actualDpi);

            return ClampTopLeft(
                new PhysicalPoint
                {
                    X = workArea.Right - Math.Max(1, physicalWidth) - margin,
                    Y = workArea.Bottom - Math.Max(1, physicalHeight) - margin
                },
                workArea, physicalWidth, physicalHeight);
        }

        internal static bool ContainsLegacyPoint(
            DisplaySurfaceSnapshot surface, int x, int y)
        {
            if (surface == null) return false;
            PhysicalRect work = surface.WorkArea;
            return x >= work.Left && x < work.Right &&
                y >= work.Top && y < work.Bottom;
        }

        internal static int ScaleLogicalLength(int logical, int actualDpi)
        {
            int safeDpi = Math.Max(96, actualDpi);
            return Math.Max(1, (int)Math.Round(
                logical * safeDpi / 96.0,
                MidpointRounding.AwayFromZero));
        }

        internal static int PredictSurfaceDpi(DisplaySurfaceSnapshot surface)
        {
            if (surface == null) return 96;
            int dpi = (int)Math.Round(surface.Scale * 96.0,
                MidpointRounding.AwayFromZero);
            return Math.Max(96, dpi);
        }

        internal static PhysicalRect PredictPetPhysicalSize(
            int logicalWidth, int logicalHeight, int targetDpi)
        {
            int safeDpi = Math.Max(96, targetDpi);
            // Same integer projection the formal Pet uses after
            // ApplyCurrentDisplayScale, so the loading canvas lands on the
            // identical physical footprint within native rounding.
            return new PhysicalRect(0, 0,
                Math.Max(1, Math.Max(1, logicalWidth) * safeDpi / 96),
                Math.Max(1, Math.Max(1, logicalHeight) * safeDpi / 96));
        }

        // Bootstrap prediction of the formal Pet's first placement. It
        // mirrors the real startup order exactly - preferred target, then the
        // legacy compatibility X/Y, then the primary bottom-right default -
        // using only immutable settings facts plus the captured topology.
        // It is not a second placement authority.
        internal static StartupPetPlacementSnapshot ResolveStartupPetPlacement(
            string preferredTargetKey,
            LogicalPoint preferredLocal,
            bool hasLegacyLocation,
            int legacyX,
            int legacyY,
            int petLogicalWidth,
            int petLogicalHeight,
            DisplayTopologySnapshot topology)
        {
            if (topology == null || topology.Surfaces.Count == 0)
                return null;

            DisplaySurfaceSnapshot preferred =
                topology.FindByTargetKey(preferredTargetKey);
            if (preferred != null)
            {
                int dpi = PredictSurfaceDpi(preferred);
                PhysicalRect size = PredictPetPhysicalSize(
                    petLogicalWidth, petLogicalHeight, dpi);
                PhysicalPoint requested = ProjectLocalPoint(
                    preferredLocal, preferred, dpi);
                PhysicalPoint clamped = ClampTopLeft(requested,
                    preferred.WorkArea, size.Width, size.Height);
                return new StartupPetPlacementSnapshot(
                    new PhysicalRect(clamped.X, clamped.Y,
                        size.Width, size.Height), dpi);
            }

            if (String.IsNullOrWhiteSpace(preferredTargetKey) &&
                hasLegacyLocation)
            {
                foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
                {
                    if (!ContainsLegacyPoint(surface, legacyX, legacyY))
                        continue;
                    int dpi = PredictSurfaceDpi(surface);
                    PhysicalRect size = PredictPetPhysicalSize(
                        petLogicalWidth, petLogicalHeight, dpi);
                    PhysicalPoint clamped = ClampTopLeft(
                        new PhysicalPoint { X = legacyX, Y = legacyY },
                        surface.WorkArea, size.Width, size.Height);
                    return new StartupPetPlacementSnapshot(
                        new PhysicalRect(clamped.X, clamped.Y,
                            size.Width, size.Height), dpi);
                }
            }

            DisplaySurfaceSnapshot fallback = topology.PrimaryOrFirst();
            if (fallback == null) return null;
            int fallbackDpi = PredictSurfaceDpi(fallback);
            PhysicalRect fallbackSize = PredictPetPhysicalSize(
                petLogicalWidth, petLogicalHeight, fallbackDpi);
            PhysicalPoint fallbackTopLeft = DefaultBottomRight(
                fallback.WorkArea, fallbackSize.Width,
                fallbackSize.Height, fallbackDpi);
            return new StartupPetPlacementSnapshot(
                new PhysicalRect(fallbackTopLeft.X, fallbackTopLeft.Y,
                    fallbackSize.Width, fallbackSize.Height), fallbackDpi);
        }
    }
}
