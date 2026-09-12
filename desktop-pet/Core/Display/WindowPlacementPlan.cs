namespace PennyPet
{
    // Selected intent, not actual geometry. Only the HWND executor supplies
    // the DPI used to project a logical target after reaching its surface.
    internal sealed class WindowPlacementPlan
    {
        private WindowPlacementPlan(DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface, LogicalRect logical,
            PhysicalRect workArea, PhysicalRect physical)
        {
            Topology = topology;
            Surface = surface;
            Logical = logical;
            WorkArea = workArea;
            Physical = physical;
        }

        internal DisplayTopologySnapshot Topology { get; private set; }
        internal DisplaySurfaceSnapshot Surface { get; private set; }
        internal LogicalRect Logical { get; private set; }
        internal PhysicalRect WorkArea { get; private set; }
        internal PhysicalRect Physical { get; private set; }

        internal static WindowPlacementPlan OnSurface(
            DisplayTopologySnapshot topology, DisplaySurfaceSnapshot surface,
            LogicalRect logical)
        {
            return new WindowPlacementPlan(topology, surface, logical,
                surface.WorkArea, new PhysicalRect());
        }

        internal static WindowPlacementPlan RecoverPhysical(
            DisplayTopologySnapshot topology, PhysicalRect workArea,
            PhysicalRect physical)
        {
            return new WindowPlacementPlan(topology, null, new LogicalRect(),
                workArea, physical);
        }

        internal PhysicalRect Resolve(int dpi)
        {
            if (Surface == null) return Physical;
            return DisplayGeometry.ProjectLocalRect(Logical,
                Surface.Bounds.Left, Surface.Bounds.Top, dpi / 96.0);
        }
    }
}
