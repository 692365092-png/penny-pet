using System;

namespace PennyPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            EmbeddedAssemblyResolver.Register();
            // Compatibility-test build: keep WPF sticky-note rendering away
            // from GPU/driver-specific layered-window paths.  The animated pet
            // itself remains on the existing WinForms renderer.
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            PennyApplicationHost.Run();
        }
    }
}
