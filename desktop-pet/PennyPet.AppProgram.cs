using System;

namespace PennyPet
{
    internal static class PennyAppProgram
    {
        [STAThread]
        private static void Main()
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            PennyApplicationHost.Run();
        }
    }
}

