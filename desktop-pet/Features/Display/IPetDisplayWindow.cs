namespace PennyPet
{
    // All operations run on the Pet STA. No repository, host, menu or other
    // window is exposed through this port; it owns no placement preference.
    internal interface IPetDisplayWindow
    {
        bool IsAvailable { get; }
        bool IsUserDragging { get; }
        PhysicalRect Bounds { get; }
        int ScalePercent { get; }
        int GetDpi(int fallbackDpi);
        bool MoveTopLeft(int x, int y);
        void ApplyScale(int dpi);
        WindowFacts CaptureFacts(DisplayTopologySnapshot topology, long sequence);
        void PlacementChanged();
    }
}
