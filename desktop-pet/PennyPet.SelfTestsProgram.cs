using System;

namespace PennyPet
{
    internal static class PennySelfTestsProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            int exitCode;
            if (!SelfTestCommandRouter.TryRun(args, out exitCode))
            {
                Console.Error.WriteLine(
                    "PennyPet.SelfTests expects a self-test, probe or preview command.");
                return 2;
            }
            return exitCode;
        }
    }
}
