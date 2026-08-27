using System;

namespace PennyPet
{
    internal static class ArtCommandRouter
    {
        internal static bool TryRun(string[] args, out int exitCode)
        {
            string path;
            if (CommandLineArguments.TryGetPath(args,
                "--write-startup-cache=", out path))
            {
                exitCode = CommandLineArguments.RunOutputCommand(path,
                    delegate { PetArtPackage.WriteStartupCache(192, 208, path); });
                return true;
            }
            if (CommandLineArguments.TryGetPath(args,
                "--write-release-pack=", out path))
            {
                exitCode = CommandLineArguments.RunOutputCommand(path,
                    delegate { PetArtPackage.WriteReleasePack(192, 208, path); });
                return true;
            }
            if (CommandLineArguments.TryGetPath(args, "--validate-art=", out path))
            {
                exitCode = CommandLineArguments.RunOutputCommand(path,
                    delegate
                    {
                        PetArtPackage.WriteValidationReport(192, 208, path);
                    });
                return true;
            }
            exitCode = 0;
            return false;
        }
    }
}
