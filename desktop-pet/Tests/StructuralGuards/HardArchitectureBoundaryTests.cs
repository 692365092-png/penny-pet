using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    // Dependency and safety boundaries only. Runtime behavior belongs in
    // behavior tests or native SelfTests, never in method-body string checks.
    [TestClass]
    public sealed class HardArchitectureBoundaryTests
    {
        [TestMethod]
        public void CoreAssembly_HasNoDesktopOrHarnessReferences()
        {
            foreach (var reference in typeof(StickyNoteData).Assembly.GetReferencedAssemblies())
                Assert.IsFalse(new[] { "System.Drawing", "System.Windows.Forms",
                    "PresentationCore", "PresentationFramework", "WindowsBase",
                    "UIAutomationClient", "UIAutomationTypes", "PennyPet.SelfTests",
                    "PennyPet.Tools", "PennyPet.Windows.Core" }.Contains(reference.Name),
                    reference.Name);
        }

        [TestMethod]
        public void CoreProject_IsPortableAndCompilesOnlyCoreSources()
        {
            XElement project = Project("PennyPet.Core.csproj");
            Assert.AreEqual("netstandard2.0", project.Element("PropertyGroup")
                .Element("TargetFramework").Value);
            Assert.IsFalse(project.Descendants("UseWindowsForms").Any() ||
                project.Descendants("UseWPF").Any() ||
                project.Descendants("ProjectReference").Any());
            CollectionAssert.AreEqual(new[] { "Core/**/*.cs" },
                project.Descendants("Compile").Select(item =>
                    Normalize((string)item.Attribute("Include"))).ToArray());
        }

        [TestMethod]
        [DataRow("PennyPet.Windows.csproj")]
        [DataRow("PennyPet.Windows.Core.csproj")]
        [DataRow("PennyPet.App.csproj")]
        public void ProductCompileGraph_ExcludesTestAndToolSources(string projectName)
        {
            XElement project = Project(projectName);
            string root = FindDesktopPetDirectory();
            string[] harnessFiles = Directory.EnumerateFiles(root, "*.cs",
                SearchOption.AllDirectories).Select(path =>
                    Normalize(Path.GetRelativePath(root, path))).Where(path =>
                    path.StartsWith("Tests/") || path.StartsWith("SelfTests/") ||
                    path.StartsWith("Tools/") ||
                    new[] { "SelfTestRunner.cs", "PennySelfTests.cs",
                        "PennyPet.SelfTestsProgram.cs", "PennyPet.ToolsProgram.cs",
                        "Infrastructure/SelfTestCommandRouter.cs",
                        "Infrastructure/ArtCommandRouter.cs" }.Contains(path)).ToArray();
            foreach (XElement compile in project.Descendants("Compile"))
            {
                string include = Normalize((string)compile.Attribute("Include"));
                string[] exclusions = Normalize((string)compile.Attribute("Exclude"))
                    .Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (string path in harnessFiles)
                    Assert.IsFalse(MatchesGlob(path, include) &&
                        !exclusions.Any(pattern => MatchesGlob(path, pattern)),
                        projectName + " compiles harness source: " + path);
            }
        }

        [TestMethod]
        [DataRow("PennyPet.Windows.csproj")]
        [DataRow("PennyPet.Windows.Core.csproj")]
        [DataRow("PennyPet.App.csproj")]
        public void ProductReferences_ExcludeHarnessAssemblies(string projectName)
        {
            foreach (XElement reference in Project(projectName).Descendants()
                .Where(item => item.Name == "ProjectReference" || item.Name == "Reference"))
            {
                string name = (string)reference.Attribute("Include") ?? "";
                Assert.IsFalse(name.Contains("SelfTests") || name.Contains("PennyPet.Tests"),
                    projectName + ": " + name);
                if (!name.Contains("PennyPet.Tools")) continue;
                Assert.AreEqual("false", (string)reference.Attribute("ReferenceOutputAssembly"),
                    "Tools may generate resources but cannot be a product runtime dependency.");
            }
        }

        [TestMethod]
        public void WindowsLibrary_DoesNotCompileASecondCoreCopy()
        {
            XElement compile = Project("PennyPet.Windows.Core.csproj")
                .Descendants("Compile").Single();
            Assert.IsTrue(Normalize((string)compile.Attribute("Exclude"))
                .Split(';').Contains("Core/**"));
        }

        [TestMethod]
        public void UiOwners_DoNotSynchronouslyWaitForOtherUiThreads()
        {
            foreach (string file in new[] { "StickyUiHost.cs", "StickyWindowSession.cs",
                "Features/StickyNotes/StickyDockController.cs",
                "Features/StickyNotes/StickyWorkspace.cs",
                "Features/Display/PetDisplayRuntime.cs" })
                Forbid(file, ".Wait(", ".Join(", "GetAwaiter().GetResult(",
                    "Dispatcher.Invoke(", "dispatcher.Invoke(");
        }

        [TestMethod]
        public void KeyboardHookAndUiDelivery_DoNotRunAutomationInspection()
        {
            foreach (string file in new[] { "GlobalKeyboardActivity.cs",
                "KeyboardFocusSnapshot.cs", "PetForm.KeyboardOverlay.cs" })
                Forbid("Features/KeyboardOverlay/" + file, "AutomationElement.",
                    "SensitiveInputDetector.", ".Wait(", ".Join(");
        }

        [TestMethod]
        public void KeyboardInspectionWorker_DoesNotOwnDesktopUi()
        {
            Forbid("Features/KeyboardOverlay/KeyboardPrivacyWorker.cs",
                "System.Windows", "System.Drawing", "DllImport(");
        }

        [TestMethod]
        public void Startup_DoesNotRequestWeatherOrLocation()
        {
            foreach (string file in new[] { "PennyApplicationHost.cs",
                "PetStartupCoordinator.cs" })
                Forbid(file, "GetForecastAsync(", "SearchLocationsAsync(", "HttpClient");
        }

        [TestMethod]
        [DataRow("Features/StickyNotes/StickyUiCommand.cs")]
        [DataRow("Features/StickyNotes/StickyHostedRuntime.cs")]
        [DataRow("Features/StickyNotes/StickyDockCommitProtocol.cs")]
        public void DetachedStickyContracts_DoNotOwnNativeWindows(string file)
        {
            Forbid(file, "System.Windows", "System.Drawing", "DllImport(",
                "StickyNoteWindow ", "PetForm ");
        }

        [TestMethod]
        public void PetOwners_DoNotConstructStickyWindows()
        {
            string root = FindDesktopPetDirectory();
            foreach (string path in Directory.EnumerateFiles(root, "Pet*.cs"))
                Assert.IsFalse(File.ReadAllText(path).Contains("new StickyNoteWindow("),
                    Path.GetFileName(path));
        }

        private static XElement Project(string name)
        {
            return XElement.Parse(ReadSource(name));
        }

        private static void Forbid(string file, params string[] patterns)
        {
            string source = ReadSource(file);
            foreach (string pattern in patterns)
                Assert.IsFalse(source.Contains(pattern), file + ": " + pattern);
        }

        private static string Normalize(string path)
        {
            return (path ?? "").Replace('\\', '/');
        }

        // MSBuild uses ** for recursive paths; retain support for the existing
        // directory-only exclusions (e.g. SelfTests/**) as well as explicit files.
        private static bool MatchesGlob(string path, string glob)
        {
            string pattern = Regex.Escape(glob).Replace(@"\*\*/", "(?:.*/)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
            return Regex.IsMatch(path, "^" + pattern + "$");
        }
    }
}
