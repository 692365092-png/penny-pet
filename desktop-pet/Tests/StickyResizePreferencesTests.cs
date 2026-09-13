using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyResizePreferencesTests
    {
        private static StickyNoteData Note()
        {
            return new StickyNoteData { Id = "note", X = 9000, Y = 8000, Width = 900, Height = 700,
                LocalLogicalX = 999, LocalLogicalY = 888, LocalLogicalWidth = 700, LocalLogicalHeight = 600,
                PreferredDisplayTargetKey = "mdp:one", PreferredLocalLogicalX = 40, PreferredLocalLogicalY = 60,
                PreferredLocalLogicalWidth = 320, PreferredLocalLogicalHeight = 240 };
        }

        private static WindowFacts Facts(int dpi = 192, string id = "note", long generation = 7)
        {
            return new WindowFacts(id, "mdp:one", "DISPLAY1", new PhysicalRect(-1720, -880, 900, 600),
                dpi, generation, 12);
        }

        [TestMethod]
        [DataRow(96, 900, 200)]
        [DataRow(120, 720, 160)]
        [DataRow(144, 600, 133)]
        [DataRow(192, 450, 100)]
        public void HorizontalFollowerUsesOwnHwndDpiAndKeepsUnaffectedDurableDimensions(int dpi, int width, int x)
        {
            StickyNoteData note = Note();
            WindowPlacementPreference result;
            Assert.IsTrue(StickyResizePreferences.TryBuild(note, Facts(dpi), StickyGeometryAuthorityTests.Topology(),
                DockResizeKind.Horizontal, false, out result));
            Assert.AreEqual("mdp:one", result.PreferredTargetKey);
            Assert.AreEqual(x, result.LocalLogicalRect.X);
            Assert.AreEqual(width, result.LocalLogicalRect.Width);
            Assert.AreEqual(60, result.LocalLogicalRect.Y);
            Assert.AreEqual(240, result.LocalLogicalRect.Height);
            Assert.AreEqual(320, note.PreferredLocalLogicalWidth, "Building a preference is pure.");
            Assert.AreEqual(9000, note.X);
            Assert.AreEqual(700, note.LocalLogicalWidth);
        }

        [TestMethod]
        public void CornerResizeCommitsSourcesActualHeightAndTopAsWellAsWidth()
        {
            WindowPlacementPreference result;
            Assert.IsTrue(StickyResizePreferences.TryBuild(Note(), Facts(), StickyGeometryAuthorityTests.Topology(),
                DockResizeKind.Horizontal, true, out result));
            Assert.AreEqual(100, result.LocalLogicalRect.X);
            Assert.AreEqual(100, result.LocalLogicalRect.Y);
            Assert.AreEqual(450, result.LocalLogicalRect.Width);
            Assert.AreEqual(300, result.LocalLogicalRect.Height);
        }

        [TestMethod]
        public void DividerOnlyUpdatesHeightOnSamePreferredSurface()
        {
            WindowPlacementPreference result;
            Assert.IsTrue(StickyResizePreferences.TryBuild(Note(), Facts(), StickyGeometryAuthorityTests.Topology(),
                DockResizeKind.Divider, true, out result));
            Assert.AreEqual(40, result.LocalLogicalRect.X);
            Assert.AreEqual(60, result.LocalLogicalRect.Y);
            Assert.AreEqual(320, result.LocalLogicalRect.Width);
            Assert.AreEqual(300, result.LocalLogicalRect.Height);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void ChangingSurfaceRebuildsLocalCoordinatesFromActualFacts(bool horizontal)
        {
            StickyNoteData note = Note();
            note.PreferredDisplayTargetKey = "mdp:removed";
            WindowPlacementPreference result;
            Assert.IsTrue(StickyResizePreferences.TryBuild(note, Facts(), StickyGeometryAuthorityTests.Topology(),
                horizontal ? DockResizeKind.Horizontal : DockResizeKind.Divider, false, out result));
            Assert.AreEqual("mdp:one", result.PreferredTargetKey);
            Assert.AreEqual(100, result.LocalLogicalRect.X);
            Assert.AreEqual(100, result.LocalLogicalRect.Y);
            Assert.AreEqual(450, result.LocalLogicalRect.Width);
            Assert.AreEqual(300, result.LocalLogicalRect.Height);
            Assert.AreEqual("mdp:removed", note.PreferredDisplayTargetKey);
        }

        [TestMethod]
        public void MissingPreferenceRecoversAllDimensionsFromActualFacts()
        {
            StickyNoteData note = Note();
            note.PreferredLocalLogicalHeight = 0;
            WindowPlacementPreference result;
            Assert.IsTrue(StickyResizePreferences.TryBuild(note, Facts(), StickyGeometryAuthorityTests.Topology(),
                DockResizeKind.Horizontal, false, out result));
            Assert.AreEqual(100, result.LocalLogicalRect.Y);
            Assert.AreEqual(300, result.LocalLogicalRect.Height);
        }

        [TestMethod]
        [DataRow("different", 7L)]
        [DataRow("note", 6L)]
        public void MismatchedIdentityOrTopologyCannotSupplyAPreference(string id, long generation)
        {
            StickyNoteData note = Note();
            WindowPlacementPreference result;
            Assert.IsFalse(StickyResizePreferences.TryBuild(note, Facts(id: id, generation: generation),
                StickyGeometryAuthorityTests.Topology(), DockResizeKind.Horizontal, true, out result));
            Assert.IsNull(result);
            Assert.AreEqual(40, note.PreferredLocalLogicalX);
            Assert.AreEqual(320, note.PreferredLocalLogicalWidth);
        }
    }
}
