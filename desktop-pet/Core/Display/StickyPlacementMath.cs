namespace PennyPet
{
    internal static class StickyPlacementMath
    {
        internal static LogicalRect ToLocalRect(int physicalOriginX,
            int physicalOriginY, double scale, PhysicalRect physical)
        {
            LogicalPoint point = DisplayGeometry.PhysicalToLocal(physical.Left,
                physical.Top, physicalOriginX, physicalOriginY, scale);
            return new LogicalRect { X = point.X, Y = point.Y,
                Width = DisplayGeometry.PhysicalLengthToLogical(physical.Width, scale),
                Height = DisplayGeometry.PhysicalLengthToLogical(physical.Height, scale) };
        }

        internal static WindowPlacementPreference PreferenceFromPhysicalRect(
            string targetKey, int physicalOriginX, int physicalOriginY,
            double scale, PhysicalRect physicalRect)
        {
            return new WindowPlacementPreference(targetKey,
                ToLocalRect(physicalOriginX, physicalOriginY, scale, physicalRect));
        }
    }
}
