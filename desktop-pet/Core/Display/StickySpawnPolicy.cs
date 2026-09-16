using System;

namespace PennyPet
{
    // Single owner of the product spawn-position rule: a new Sticky appears
    // centered in the target surface WorkArea (never Bounds, never beside the
    // pet, never a cascade). Pure physical-pixel math using the project-wide
    // AwayFromZero rounding; negative origins and negative Y are legal.
    internal static class StickySpawnPolicy
    {
        internal static PhysicalRect CenterInWorkArea(
            PhysicalRect workArea, int width, int height)
        {
            if (!workArea.IsValid) return new PhysicalRect();
            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);
            // A note larger than the work area is fitted first, and the final
            // preferred rect must derive from the fitted result.
            int fittedWidth = Math.Min(safeWidth, workArea.Width);
            int fittedHeight = Math.Min(safeHeight, workArea.Height);
            int left = workArea.Left + (int)Math.Round(
                (workArea.Width - fittedWidth) / 2.0,
                MidpointRounding.AwayFromZero);
            int top = workArea.Top + (int)Math.Round(
                (workArea.Height - fittedHeight) / 2.0,
                MidpointRounding.AwayFromZero);
            return new PhysicalRect(left, top, fittedWidth, fittedHeight);
        }

        // A spawn request produces pixels, not another persisted geometry model.
        internal static PhysicalRect PlanCenteredSpawn(PhysicalRect workArea,
            double scale, int logicalWidth, int logicalHeight)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            int physicalWidth = Math.Max(1, (int)Math.Round(
                Math.Max(1, logicalWidth) * safeScale, MidpointRounding.AwayFromZero));
            int physicalHeight = Math.Max(1, (int)Math.Round(
                Math.Max(1, logicalHeight) * safeScale, MidpointRounding.AwayFromZero));
            return CenterInWorkArea(workArea, physicalWidth, physicalHeight);
        }
    }
}
