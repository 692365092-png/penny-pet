using System;

namespace PennyPet
{
    internal static class PennyToolsProgram
    {
        private static int Main(string[] args)
        {
            string startupCache = ArgumentValue(args, "--write-startup-cache=");
            if (!String.IsNullOrEmpty(startupCache))
            {
                PetArtPackage.WriteStartupCache(192, 208, startupCache);
                return 0;
            }
            string releasePack = ArgumentValue(args, "--write-release-pack=");
            if (!String.IsNullOrEmpty(releasePack))
            {
                PetArtPackage.WriteReleasePack(192, 208, releasePack);
                return 0;
            }
            string validation = ArgumentValue(args, "--validate-art=");
            if (!String.IsNullOrEmpty(validation))
            {
                PetArtPackage.WriteValidationReport(192, 208, validation);
                return 0;
            }
            Console.Error.WriteLine("PennyPet.Tools expects one art/cache command.");
            return 2;
        }

        private static string ArgumentValue(string[] args, string prefix)
        {
            if (args == null) return null;
            foreach (string arg in args)
                if (arg != null && arg.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length).Trim('"');
            return null;
        }
    }
}

