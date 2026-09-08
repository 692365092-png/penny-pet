using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    internal static class SourceGuardText
    {
        internal static string DockCommitValidationSource()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            return Between(coordinator,
                "private bool TryPrepareDockCommit",
                "private static void TraceDockCommitRejected");
        }

        internal static string ReadSource(string relativePath)
        {
            string root = FindDesktopPetDirectory();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
        }

        internal static string Between(string text, string startMarker, string endMarker)
        {
            int start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing marker: " + startMarker);
            int end = text.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start);
        }

        private static string FindDesktopPetDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Windows.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet Windows source directory.");
        }
        // Preserve the topology guards' original raw-text reader and errors.
        internal static class RawSource
        {
            internal static string SliceMethod(string source, string signature)
            {
                int start = source.IndexOf(signature, StringComparison.Ordinal);
                Assert.IsTrue(start >= 0, "Method not found: " + signature);
                int open = source.IndexOf('{', start);
                Assert.IsTrue(open >= 0);
                int depth = 0;
                for (int index = open; index < source.Length; index++)
                {
                    if (source[index] == '{') depth++;
                    else if (source[index] == '}' && --depth == 0)
                        return source.Substring(start, index - start + 1);
                }
                Assert.Fail("Unclosed method: " + signature);
                return String.Empty;
            }

            internal static string ReadSource(string relativePath)
            {
                DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
                while (current != null)
                {
                    string project = Path.Combine(current.FullName, "desktop-pet",
                        relativePath);
                    if (File.Exists(project)) return File.ReadAllText(project);
                    current = current.Parent;
                }
                throw new FileNotFoundException("Could not locate " + relativePath);
            }
        }

    }
}
