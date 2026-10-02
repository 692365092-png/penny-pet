using System;
using System.IO;

namespace PennyPet.Tests
{
    internal static class SourceGuardText
    {
        internal static string ReadSource(string relativePath)
        {
            return File.ReadAllText(Path.Combine(FindDesktopPetDirectory(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string FindDesktopPetDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Windows.csproj"))) return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet Windows source directory.");
        }
    }
}
