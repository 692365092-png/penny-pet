using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
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
