using System;

namespace PennyPet
{
    internal static class PennyToolsProgram
    {
        private static int Main(string[] args)
        {
            int exitCode;
            if (ArtCommandRouter.TryRun(args, out exitCode)) return exitCode;
            Console.Error.WriteLine(
                "PennyPet.Tools expects one art/cache command.");
            return 2;
        }
    }
}
