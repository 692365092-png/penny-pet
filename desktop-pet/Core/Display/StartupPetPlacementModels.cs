using System;

namespace PennyPet
{
    // One immutable bootstrap prediction of the formal Pet's first-settled
    // placement. The startup loading canvas only renders into this physical
    // rect. It owns no runtime authority, writes no settings and never
    // mutates the durable preferred placement.
    internal sealed class StartupPetPlacementSnapshot
    {
        internal StartupPetPlacementSnapshot(
            PhysicalRect physicalBounds, int targetDpi)
        {
            PhysicalBounds = physicalBounds;
            TargetDpi = Math.Max(96, targetDpi);
        }

        internal PhysicalRect PhysicalBounds { get; private set; }
        internal int TargetDpi { get; private set; }
    }
}
