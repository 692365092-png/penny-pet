using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void R21_SideTabsAndDockChrome_AreOwnedByStickySta()
        {
            string workspace = ReadSource(
                "Features/StickyNotes/StickyWorkspace.cs");
            string host = ReadSource("StickyUiHost.cs");
            string dock = ReadSource(
                "Features/StickyNotes/StickyDockController.cs");

            Assert.IsFalse(workspace.Contains(
                    "new StickyNoteTabsForm(") ||
                workspace.Contains("private StickyNoteTabsForm"),
                "Pet-side workspace must not own SideTab HWNDs.");
            Assert.IsTrue(workspace.Contains(
                    "Host.UpdateSideTabs(new StickySideTabsProjection(") &&
                workspace.Contains("Host.SetModalZOrderFloor("),
                "Pet may publish only explicit SideTab geometry/layer projections.");
            Assert.IsTrue(host.Contains(
                    "_leftNoteTabs = CreateSideTabs(StickyTabSide.Left)") &&
                host.Contains("ApplySideTabZOrder()") &&
                host.Contains("KeepTransientBelowModal("),
                "Sticky STA must own SideTab creation, live overlap and modal layering.");
            Assert.IsFalse(dock.Contains(
                    "new DockPulseIndicatorForm(") ||
                dock.Contains("DockPulseIndicatorForm _"),
                "Dock controller must not own feedback HWNDs on the Pet STA.");
            Assert.IsTrue(host.Contains(
                    "new DockPulseIndicatorForm(") &&
                dock.Contains("_workspace.Host.UpdateDockPreview(") &&
                dock.Contains("_workspace.Host.ShowSplitGuide("),
                "Dock feedback windows must be created and updated by the Sticky host.");
        }

        [TestMethod]
        public void SideTabs_UseTypedSnapshotAndNoteIdActions()
        {
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string snapshots = ReadSource(
                "Core/StickyNotes/SideTabSnapshot.cs");

            Assert.IsTrue(snapshots.Contains("NoteId") &&
                snapshots.Contains("ColorArgb") &&
                snapshots.Contains("Visible"),
                "Side tabs must consume a pure value snapshot.");
            Assert.IsTrue(tabs.Contains("Action<string> _openNote") &&
                tabs.Contains("Action<string> _deleteNote") &&
                tabs.Contains("Action<string, int> _reorderNote"),
                "Side tab user actions must be typed note-id actions.");
            Assert.IsTrue(tabs.Contains(
                "SetNotes(IList<SideTabSnapshot>"),
                "Side tabs must accept snapshot input.");
            Assert.IsFalse(snapshots.Contains("ToDisplayData"),
                "SideTabSnapshot must remain a detached projection, not a fake note adapter.");
            Assert.IsFalse(tabs.Contains("new StickyNoteData"),
                "Side tab display code must not reconstruct canonical note objects.");
        }
    }
}
