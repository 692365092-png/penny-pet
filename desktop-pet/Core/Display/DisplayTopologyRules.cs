using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal static class DisplayTopologyRules
    {
        internal static void Validate(long generation,
            IReadOnlyList<DisplaySurfaceSnapshot> surfaces)
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException("generation");
            if (surfaces == null || surfaces.Count == 0)
                throw new ArgumentException(
                    "A topology snapshot needs at least one surface.",
                    "surfaces");

            HashSet<string> surfaceIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> gdiNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> targetKeys = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            int primaryCount = 0;
            foreach (DisplaySurfaceSnapshot surface in surfaces)
            {
                if (surface == null ||
                    String.IsNullOrWhiteSpace(surface.RuntimeSurfaceId) ||
                    String.IsNullOrWhiteSpace(surface.RuntimeGdiName) ||
                    !surface.Bounds.IsValid || !surface.WorkArea.IsValid ||
                    surface.Targets.Count == 0 ||
                    !IsRotationValid(surface.RotationDegrees))
                    throw new ArgumentException(
                        "Topology contains an invalid surface.", "surfaces");
                if (!surfaceIds.Add(surface.RuntimeSurfaceId) ||
                    !gdiNames.Add(surface.RuntimeGdiName))
                    throw new ArgumentException(
                        "Runtime surface identities must be unique.",
                        "surfaces");
                if (surface.IsPrimary) primaryCount++;
                foreach (DisplayTargetIdentity target in surface.Targets)
                {
                    if (target == null ||
                        String.IsNullOrWhiteSpace(target.StableKey) ||
                        !targetKeys.Add(target.StableKey))
                        throw new ArgumentException(
                            "Target identities must be non-empty and unique.",
                            "surfaces");
                }
            }
            if (primaryCount > 1)
                throw new ArgumentException(
                    "Topology cannot contain multiple primary surfaces.",
                    "surfaces");
        }

        internal static DisplaySurfaceSnapshot FindByTargetKey(
            IReadOnlyList<DisplaySurfaceSnapshot> surfaces, string key)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            foreach (DisplaySurfaceSnapshot surface in surfaces)
                foreach (DisplayTargetIdentity target in surface.Targets)
                    if (String.Equals(target.StableKey, key.Trim(),
                        StringComparison.OrdinalIgnoreCase)) return surface;
            return null;
        }

        internal static DisplaySurfaceSnapshot FindByRuntimeGdiName(
            IReadOnlyList<DisplaySurfaceSnapshot> surfaces, string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            foreach (DisplaySurfaceSnapshot surface in surfaces)
                if (String.Equals(surface.RuntimeGdiName, name.Trim(),
                    StringComparison.OrdinalIgnoreCase)) return surface;
            return null;
        }

        internal static DisplaySurfaceSnapshot PrimaryOrFirst(
            IReadOnlyList<DisplaySurfaceSnapshot> surfaces)
        {
            foreach (DisplaySurfaceSnapshot surface in surfaces)
                if (surface.IsPrimary) return surface;
            return surfaces.Count == 0 ? null : surfaces[0];
        }

        private static bool IsRotationValid(int rotationDegrees)
        {
            return rotationDegrees == 0 || rotationDegrees == 90 ||
                rotationDegrees == 180 || rotationDegrees == 270;
        }
    }
}
