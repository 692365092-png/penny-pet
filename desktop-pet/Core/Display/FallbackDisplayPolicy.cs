using System;

namespace PennyPet
{
    // Pure Core fallback-surface policy. When a durable preferred target is
    // not active, the next surface is chosen in this order:
    //   1. preferred target still active
    //   2. last physical rect clearly inside an active work area
    //   3. Pet's current surface
    //   4. primary surface
    //   5. any active surface (primary-or-first guarantees one)
    // It never inspects enumeration order as an identity and never calls
    // Windows.
    internal static class FallbackDisplayPolicy
    {
        internal static DisplaySurfaceSnapshot ResolveFallbackSurface(
            DisplayTopologySnapshot topology, string preferredTargetKey,
            PhysicalRect lastPhysicalRect, string petRuntimeGdiName)
        {
            if (topology == null || topology.Surfaces.Count == 0) return null;

            if (!String.IsNullOrWhiteSpace(preferredTargetKey))
            {
                DisplaySurfaceSnapshot preferred =
                    topology.FindByTargetKey(preferredTargetKey);
                if (preferred != null) return preferred;
            }

            if (lastPhysicalRect.IsValid)
            {
                DisplaySurfaceSnapshot containing =
                    FindSurfaceContainingRect(topology, lastPhysicalRect);
                if (containing != null) return containing;
            }

            if (!String.IsNullOrWhiteSpace(petRuntimeGdiName))
            {
                DisplaySurfaceSnapshot petSurface =
                    topology.FindByRuntimeGdiName(petRuntimeGdiName);
                if (petSurface != null) return petSurface;
            }

            return topology.PrimaryOrFirst();
        }

        private static DisplaySurfaceSnapshot FindSurfaceContainingRect(
            DisplayTopologySnapshot topology, PhysicalRect rect)
        {
            int centerX = rect.Left + rect.Width / 2;
            int centerY = rect.Top + rect.Height / 2;
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                PhysicalRect workArea = surface.WorkArea;
                if (centerX >= workArea.Left && centerX < workArea.Right &&
                    centerY >= workArea.Top && centerY < workArea.Bottom)
                    return surface;
            }
            return null;
        }
    }
}
