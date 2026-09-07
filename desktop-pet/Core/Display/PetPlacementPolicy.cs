using System;

namespace PennyPet
{
    internal static class PetPlacementPolicy
    {
        internal const int DefaultMarginLogical = 24;

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
    }
}
