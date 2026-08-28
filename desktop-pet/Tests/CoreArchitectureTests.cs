using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class CoreArchitectureTests
    {
        private static readonly string[] ForbiddenReferences = new string[]
        {
            "using System.Windows.Forms",
            "using System.Windows;",
            "using System.Windows.",
            "using Microsoft.Win32",
            "using System.Drawing",
            "using System.Windows.Automation",
            "Microsoft.Win32.Registry",
            "System.Drawing.Bitmap",
            "System.Windows.Forms.",
            "System.Windows.Automation."
        };

        [TestMethod]
        public void CoreSource_DoesNotReferenceWindowsDesktopApis()
        {
            string coreDirectory = FindCoreDirectory();
            List<string> violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(coreDirectory,
                "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (string forbidden in ForbiddenReferences)
                {
                    if (source.IndexOf(forbidden,
                        StringComparison.Ordinal) < 0) continue;
                    violations.Add(Path.GetRelativePath(coreDirectory, file) +
                        ": " + forbidden);
                }
            }

            Assert.AreEqual(0, violations.Count,
                "PennyPet.Core must remain platform-neutral:" +
                Environment.NewLine + String.Join(Environment.NewLine,
                    violations));
        }

        private static string FindCoreDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "Core");
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Core.csproj")) && Directory.Exists(candidate))
                    return candidate;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet.Core source directory.");
        }
    }
}
