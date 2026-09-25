using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DisplayRuntimeArchitectureGuardTests
    {
        [TestMethod]
        public void PetDisplayRuntime_DoesNotUseLegacyScreenAuthority()
        {
            string source = ReadSource("PetDisplayRuntime.cs") +
                ReadSource("Features/Display/PetDisplayRuntime.cs");
            AssertNotContains(source, "Screen.PrimaryScreen");
            AssertNotContains(source, "Screen.AllScreens");
            AssertNotContains(source, "GetDpiForMonitor(");
            AssertNotContains(source, "WindowsDisplayResolver");
        }

        [TestMethod]
        public void ExistingHwndFacts_UsesGetDpiForWindow()
        {
            string source = ReadSource(
                "Infrastructure/Display/WindowsWindowFactsReader.cs");
            StringAssert.Contains(source, "GetDpiForWindow");
        }

        [TestMethod]
        public void SideTabsPosition_IsDerivedAndNeverPushesPet()
        {
            string method = SliceMethod(
                SourceGuardText.ReadStickyWorkflowSource(),
                "internal void PositionNoteTabs(");

            StringAssert.Contains(method,
                "TryGetPetDerivedDisplayContext");
            AssertNotContains(method, "Screen.");
            AssertNotContains(method, "Location =");
            AssertNotContains(method, "Left =");
            AssertNotContains(method, "Top =");
        }

        [TestMethod]
        public void LiveDockNativeBatch_RemainsGeometryOnly()
        {
            string source = ReadSource(
                "Infrastructure/Display/WindowsBatchWindowPlacementExecutor.cs");
            StringAssert.Contains(source, "SWP_NOACTIVATE");
            StringAssert.Contains(source, "SWP_NOZORDER");
        }

        [TestMethod]
        public void DockExecutionRules_KeepGenerationAndEpochGate()
        {
            string method = SliceMethod(
                ReadSource("Features/StickyNotes/DockInteractionState.cs"),
                "internal static bool CanExecute(");

            StringAssert.Contains(method,
                "plan.TopologyGeneration == currentGeneration");
            StringAssert.Contains(method,
                "plan.InteractionEpoch == currentEpoch");
        }

        [TestMethod]
        public void DisplayRuntimeOwners_DoNotSynchronouslyBlockUiThread()
        {
            string[] files =
            {
                "PetDisplayRuntime.cs",
                "Features/Display/PetDisplayRuntime.cs",
                "Features/StickyNotes/StickyDockController.cs",
                "Features/StickyNotes/StickyWorkspace.cs",
                "StickyUiHost.cs",
                "StickyWindowSession.cs"
            };

            foreach (string file in files)
            {
                string source = ReadSource(file);
                AssertNotContains(source, ".Wait(");
                AssertNotContains(source, "GetAwaiter().GetResult(");
            }
        }

        [TestMethod]
        public void PetDisplayState_IsIndependentOfNativeWindowAndTopologyProvider()
        {
            string source = ReadSource("Features/Display/PetDisplayRuntime.cs");
            AssertNotContains(source, "partial class PetForm");
            AssertNotContains(source, "System.Windows");
            AssertNotContains(source, "NativeDisplayConfig");
            AssertNotContains(source, "WindowsDisplayTopologyProvider");
            AssertNotContains(ReadSource("PetForm.cs"), "_petTemporaryRehome");
        }

        private static void AssertNotContains(string source, string value)
        {
            Assert.IsTrue(source.IndexOf(value,
                StringComparison.Ordinal) < 0,
                "Forbidden runtime pattern: " + value);
        }
    }
}
