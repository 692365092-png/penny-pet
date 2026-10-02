using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockPureRulesTests
    {
        [TestMethod]
        public void CoreAssemblyDoesNotOwnDockGestureOrTransportLifetimes()
        {
            var core = typeof(DockLayout).Assembly;
            foreach (string name in new[] { "DockInput", "DockInteractionSession", "DockMutationQueue",
                "DockPlacementPlan", "DockGroupReprojectPlan", "DockPlacementPlanner" })
                Assert.IsNull(core.GetType("PennyPet." + name), name);
            foreach (var property in typeof(DockWindowTarget).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.IsFalse(property.Name.Contains("Epoch") || property.Name.Contains("Sequence") ||
                    property.Name.Contains("Input"), "A geometry result cannot contain transport state.");
        }

        [TestMethod]
        [DataRow(96)]
        [DataRow(120)]
        [DataRow(144)]
        [DataRow(192)]
        public void LogicalProjectionPreservesRoundedSeamsAtTargetDpi(int dpi)
        {
            var surface = StickyGeometryAuthorityTests.Topology().PrimaryOrFirst();
            var group = new DockGroupLogicalState(new LogicalPoint { X = -33, Y = -21 }, new[] {
                new DockLogicalMember("a", 281, 221), new DockLogicalMember("b", 281, 333) });
            var result = DockLayout.ProjectGroup(group, group.RootAnchor, surface, dpi);
            Assert.AreEqual(result[0].PhysicalBounds.Bottom, result[1].PhysicalBounds.Top);
            Assert.AreEqual(result[0].PhysicalBounds.Left, result[1].PhysicalBounds.Left);
            Assert.AreEqual(result[0].PhysicalBounds.Width, result[1].PhysicalBounds.Width);
            Assert.AreEqual(surface.Bounds.Top + (int)Math.Round(533 * dpi / 96.0,
                MidpointRounding.AwayFromZero), result[1].PhysicalBounds.Bottom);
            Assert.AreEqual(221, group.Members[0].Height);
            Assert.AreEqual(-21, group.RootAnchor.Y);
        }

        [TestMethod]
        public void HorizontalFollowersKeepTheirOwnPhysicalHeights()
        {
            var members = new[] { new DockRect(-500, 10, 315, 330),
                new DockRect(-500, 340, 420, 440), new DockRect(-500, 780, 560, 600) };
            var result = StickyDockGeometry.CalculateHorizontalResizeTargets(members, 1, -700, 900);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(-700, result[0].Left);
            Assert.AreEqual(900, result[1].Width);
            Assert.AreEqual(330, result[0].Height);
            Assert.AreEqual(600, result[1].Height);
            Assert.AreEqual(780, result[1].Top);
            Assert.AreEqual(420, members[1].Width);
        }

        [TestMethod]
        public void DividerCorrectionUsesActualHeightsAndFinalSourceTop()
        {
            var source = new PhysicalRect(100, 93, 420, 450);
            var actual = new[] { new PhysicalRect(100, 999, 560, 601),
                new PhysicalRect(100, 9999, 315, 333) };
            var result = StickyDockGeometry.CorrectResizeFollowers(DockResizeKind.Divider, source, actual);
            Assert.AreEqual(543, result[0].Top);
            Assert.AreEqual(1144, result[1].Top);
            Assert.AreEqual(560, result[0].Width);
            Assert.AreEqual(333, result[1].Height);
            Assert.AreEqual(999, actual[0].Top);
        }

        [TestMethod]
        public void SnapSelectionKeepsInputOrderForTiesAndPrefersCloserSeam()
        {
            var source = new DockWindowTarget("moving", new PhysicalRect(-900, 410, 320, 300));
            var first = new DockWindowTarget("first", new PhysicalRect(-900, 100, 320, 300));
            var equal = new DockWindowTarget("equal", first.PhysicalBounds);
            var closer = new DockWindowTarget("closer", new PhysicalRect(-900, 108, 320, 300));
            Assert.AreEqual("first", StickyDockOperations.FindSnapTarget(source, new[] { first, equal }, 20));
            Assert.AreEqual("closer", StickyDockOperations.FindSnapTarget(source, new[] { first, closer }, 20));
            Assert.IsNull(StickyDockOperations.FindSnapTarget(source, new[] { source }, 20));
        }

        [TestMethod]
        public void SnapDistanceDoesNotOverflowAtOppositeScreenCoordinates()
        {
            Assert.IsFalse(StickyDockOperations.CanDockBelow(100, Int32.MinValue, 320, 300,
                100, Int32.MaxValue - 300, 320, 300, 20));
        }

        [TestMethod]
        public void DetachedOrderAndCommitAgreeAndRetainHiddenSlots()
        {
            var a = new StickyNoteData { Id = "a" };
            var hidden = new StickyNoteData { Id = "hidden", Visible = false };
            var c = new StickyNoteData { Id = "c" };
            var d = new StickyNoteData { Id = "d", Visible = false };
            var target = new[] { a, hidden };
            var inserted = new[] { c, d };
            StickyDockGroups.ApplyOrderedGroup(target);
            StickyDockGroups.ApplyOrderedGroup(inserted);
            var expected = DockOrderRules.MergeAfter(new[] { "a", "hidden" }, "A", new[] { "c", "d" });
            var plan = StickyDockOperations.PrepareMergeAfterParent(target, a, inserted);
            Assert.IsTrue(plan.TryResolve(target.Concat(inserted), out var preview));
            CollectionAssert.AreEqual(expected, preview.Select(n => n.Id).ToList());
            Assert.IsTrue(plan.TryCommit(target.Concat(inserted)));
            CollectionAssert.AreEqual(expected, StickyDockGroups.GetOrderedGroup(preview, a).Select(n => n.Id).ToList());
            var remaining = StickyDockOperations.ExtractSingleDockMember(preview, c);
            CollectionAssert.AreEqual(DockOrderRules.Remove(expected, "C"), remaining.Select(n => n.Id).ToList());
            Assert.IsFalse(hidden.Visible);
            Assert.IsFalse(d.Visible);
            Assert.AreEqual(String.Empty, c.DockGroupId);
        }

        [TestMethod]
        public void OverlappingMergeMovesIdsOnceWithoutEditingInputOrder()
        {
            var target = new[] { "a", "b", "hidden" };
            var inserted = new[] { "B", "c", "C" };
            CollectionAssert.AreEqual(new[] { "a", "B", "c", "hidden" },
                DockOrderRules.MergeAfter(target, "a", inserted));
            CollectionAssert.AreEqual(new[] { "a", "b", "hidden" }, target);
            CollectionAssert.AreEqual(new[] { "B", "c", "C" }, inserted);
        }
    }
}
