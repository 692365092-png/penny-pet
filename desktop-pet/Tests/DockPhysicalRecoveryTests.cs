using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockPhysicalRecoveryTests
    {
        private static List<StickyNoteData> Group()
        {
            return new List<StickyNoteData> {
                new StickyNoteData { Id = "root", DockGroupId = "group", DockGroupOrder = 0,
                    X = -1819, Y = -999, Width = 421, Height = 333, Visible = false, AlwaysOnTop = true },
                new StickyNoteData { Id = "tail", DockGroupId = "group", DockGroupOrder = 1,
                    X = 8000, Y = 9000, Width = 900, Height = 617, Visible = true, AlwaysOnTop = false } };
        }

        private static DockRestoreOperation Create(List<StickyNoteData> group,
            DisplayTopologySnapshot topology = null, long sequence = 17)
        {
            return DockRestoreOperation.TryCreate(group, "tail", true, true,
                topology ?? StickyGeometryAuthorityTests.Topology(), null, sequence);
        }

        [TestMethod]
        [DataRow(96)]
        [DataRow(120)]
        [DataRow(144)]
        [DataRow(192)]
        public void PhysicalRecoveryKeepsExactPixelsAtActualTargetDpi(int dpi)
        {
            List<StickyNoteData> group = Group();
            DockRestoreOperation operation = Create(group);
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(operation.Plan, operation.Target, dpi);
            PhysicalRect root = plan.WindowTargets[0].PhysicalBounds;
            PhysicalRect tail = plan.WindowTargets[1].PhysicalBounds;
            Assert.AreEqual(DockTopologyReprojectReason.LegacyRecovery, operation.Reason);
            Assert.IsNull(operation.Plan.Group);
            Assert.AreEqual(421, root.Width);
            Assert.AreEqual(421, tail.Width);
            Assert.AreEqual(333, root.Height);
            Assert.AreEqual(617, tail.Height);
            Assert.AreEqual(-1819, root.Left);
            Assert.AreEqual(-999, root.Top);
            Assert.AreEqual(root.Left, tail.Left);
            Assert.AreEqual(root.Bottom, tail.Top);
            Assert.AreEqual(dpi, plan.TargetDpi);
            Assert.AreEqual(7L, plan.TopologyGeneration);
            Assert.AreEqual(17L, plan.PlanSequence);
            Assert.AreEqual(String.Empty, plan.SourceNoteId);
            Assert.IsFalse(group[0].Visible);
            Assert.AreEqual(8000, group[1].X);
            Assert.IsNull(group[0].PreferredPlacement);
        }

        [TestMethod]
        [DataRow(-1, 1100, 1, 280, 700, 220)]
        [DataRow(1500, 1, 1100, 900, 220, 700)]
        [DataRow(421, 333, 617, 421, 333, 617)]
        public void OldSizeClampsUnifyRootWidthAndPreserveIndependentHeights(
            int width, int rootHeight, int tailHeight, int expectedWidth, int expectedRootHeight, int expectedTailHeight)
        {
            List<StickyNoteData> group = Group();
            group[0].Width = width;
            group[0].Height = rootHeight;
            group[1].Height = tailHeight;
            DockRestoreOperation operation = Create(group);
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(operation.Plan, operation.Target, 144);
            Assert.AreEqual(expectedWidth, plan.WindowTargets[0].PhysicalBounds.Width);
            Assert.AreEqual(expectedWidth, plan.WindowTargets[1].PhysicalBounds.Width);
            Assert.AreEqual(expectedRootHeight, plan.WindowTargets[0].PhysicalBounds.Height);
            Assert.AreEqual(expectedTailHeight, plan.WindowTargets[1].PhysicalBounds.Height);
            Assert.AreEqual(plan.WindowTargets[0].PhysicalBounds.Bottom, plan.WindowTargets[1].PhysicalBounds.Top);
            Assert.AreEqual(width, group[0].Width, "Planning cannot normalize the saved note.");
        }

        [TestMethod]
        [DataRow(-10000, -5000, -2277, -1040)]
        [DataRow(10000, 5000, -64, -32)]
        [DataRow(-2000, -1030, -2000, -1030)]
        [DataRow(-1819, -10, -1819, -32)]
        [DataRow(-1819, -1100, -1819, -1040)]
        public void RecoveryKeepsHeaderReachableWithoutClampingTheWholeStack(
            int x, int y, int expectedX, int expectedY)
        {
            List<StickyNoteData> group = Group();
            group[0].X = x;
            group[0].Y = y;
            DockRestoreOperation operation = Create(group);
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(operation.Plan, operation.Target, 192);
            Assert.AreEqual(expectedX, plan.WindowTargets[0].PhysicalBounds.Left);
            Assert.AreEqual(expectedY, plan.WindowTargets[0].PhysicalBounds.Top);
            Assert.AreEqual(expectedY + 333, plan.WindowTargets[1].PhysicalBounds.Top);
            Assert.IsFalse(operation.Plan.CenterInWorkArea);
        }

        [TestMethod]
        [DataRow(-150, "right")]
        [DataRow(-3000, "left")]
        [DataRow(4000, "right")]
        public void RecoveryChoosesLargestHeaderOverlapOrNearestCapturedSurface(int x, string expected)
        {
            var topology = new DisplayTopologySnapshot(11, new[] {
                Surface("right", 0, true), Surface("left", -1920, false) });
            List<StickyNoteData> group = Group();
            group[0].X = x;
            group[0].Y = 100;
            DockRestoreOperation operation = Create(group, topology);
            Assert.AreEqual(expected, operation.Target.RuntimeSurfaceId);
            Assert.AreSame(topology, operation.Topology);
            Assert.AreEqual(11L, operation.Plan.TopologyGeneration);
        }

        [TestMethod]
        public void MissingTopologyCannotBeginRestore()
        {
            Assert.IsNull(DockRestoreOperation.TryCreate(Group(), "tail", true, true, null, null, 17));
        }

        [TestMethod]
        [DataRow(0, false)]
        [DataRow(144, true)]
        public void PhysicalRecoveryStillRequiresActualDpiAndTheCapturedTarget(int dpi, bool wrongTarget)
        {
            DockRestoreOperation operation = Create(Group());
            try
            {
                DockPlacementPlanner.PlanReproject(operation.Plan,
                    wrongTarget ? Surface("different", 0, true) : operation.Target, dpi);
                Assert.Fail("A physical recovery cannot bypass target/DPI validation.");
            }
            catch (ArgumentException) { }
        }

        [TestMethod]
        public void RecoveryCaptureDoesNotReadLaterChangesOrCommitAPartialPreference()
        {
            List<StickyNoteData> group = Group();
            group[0].PreferredPlacement = new WindowPlacementPreference("mdp:unplugged",
                new LogicalRect { X = 0, Y = 0, Width = 700, Height = 500 });
            group[1].DisplayId = "DISPLAY-old";
            group[1].LocalLogicalWidth = 800;
            group[1].LocalLogicalHeight = 600;
            DockRestoreOperation operation = Create(group);
            group[0].X = 999;
            group[0].Width = 555;
            group[1].Height = 700;
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(operation.Plan, operation.Target, 144);
            Assert.AreEqual(-1819, plan.WindowTargets[0].PhysicalBounds.Left);
            Assert.AreEqual(421, plan.WindowTargets[0].PhysicalBounds.Width);
            Assert.AreEqual(617, plan.WindowTargets[1].PhysicalBounds.Height);
            Assert.AreEqual("mdp:unplugged", group[0].PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(700, group[0].PreferredPlacement.LocalLogicalRect.Width);
            Assert.IsNull(group[1].PreferredPlacement);
            Assert.IsTrue(operation.Snapshots[1].AlwaysOnTop, "New sessions inherit the root pin state.");
            Assert.IsFalse(group[1].AlwaysOnTop, "Canonical pin state waits for acceptance.");
        }

        [TestMethod]
        public void LegacyAndPreferredRequestsShareOneGroupOwnerAndCancellation()
        {
            List<StickyNoteData> group = Group();
            DockRestoreOperation old = Create(group);
            foreach (StickyNoteData member in group)
            {
                member.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                    new LogicalRect { X = 0, Y = 0, Width = 320, Height = 300 });
            }
            DockRestoreOperation next = Create(group, sequence: 18);
            var operations = new DockRestoreOperations();
            Assert.IsTrue(operations.TryBegin(old));
            Assert.IsFalse(operations.TryBegin(next));
            Assert.IsTrue(operations.Finish(old));
            Assert.IsTrue(old.Cancellation.IsCancellationRequested);
            Assert.IsTrue(operations.TryBegin(next));
            Assert.IsFalse(operations.Finish(old));
            Assert.IsTrue(operations.IsCurrent(next));
            Assert.IsNull(next.Plan.RecoveryTargets);
            Assert.AreEqual(2, next.Plan.MemberIds.Count);
            StickyUiCommand command = StickyUiCommand.RestoreDockGroup(old, null);
            Assert.AreSame(old.Plan, command.DockGroupReprojectPlan);
            Assert.IsTrue(command.DockRestore.Cancellation.IsCancellationRequested);
        }

        private static DisplaySurfaceSnapshot Surface(string id, int x, bool primary)
        {
            return new DisplaySurfaceSnapshot(id, id, new PhysicalRect(x, 0, 1920, 1080),
                new PhysicalRect(x, 40, 1920, 1040), primary, 0,
                new[] { new DisplayTargetIdentity("mdp:" + id, true, id, id, 0, 0, 0) }, 2.0);
        }
    }
}
