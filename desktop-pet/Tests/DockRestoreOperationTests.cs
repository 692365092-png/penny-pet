using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockRestoreOperationTests
    {
        private static List<StickyNoteData> Group()
        {
            return new List<StickyNoteData> {
                new StickyNoteData { Id = "a", DockGroupId = "group", DockGroupOrder = 0, Visible = true,
                    Text = "visible root", X = 8000, Y = 9000, Width = 900, Height = 700,
                    PreferredPlacement = new WindowPlacementPreference("mdp:one",
                        new LogicalRect { X = 40, Y = 60, Width = 320, Height = 300 })},
                new StickyNoteData { Id = "b", DockGroupId = "group", DockGroupOrder = 1, Visible = false,
                    Text = "hidden member", PreferredPlacement = new WindowPlacementPreference("mdp:one",
                        new LogicalRect { X = 0, Y = 0, Width = 900, Height = 450 })} };
        }

        private static DockRestoreOperation Create(IList<StickyNoteData> group, long generation = 7, long sequence = 1)
        {
            return DockRestoreOperation.TryCreate(group, "b", true, true,
                StickyGeometryAuthorityTests.Topology(generation), null, sequence);
        }

        [TestMethod]
        [DataRow(96, 320, 300, 450)]
        [DataRow(144, 480, 450, 675)]
        [DataRow(192, 640, 600, 900)]
        public void RestorePlansFromDurableRootAndIndependentHeightsAtActualTargetDpi(
            int dpi, int width, int rootHeight, int tailHeight)
        {
            List<StickyNoteData> group = Group();
            DockRestoreOperation operation = Create(group);
            Assert.IsNotNull(operation);
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(operation.Plan, operation.Target, dpi);
            Assert.AreEqual(width, plan.WindowTargets[0].PhysicalBounds.Width);
            Assert.AreEqual(width, plan.WindowTargets[1].PhysicalBounds.Width);
            Assert.AreEqual(rootHeight, plan.WindowTargets[0].PhysicalBounds.Height);
            Assert.AreEqual(tailHeight, plan.WindowTargets[1].PhysicalBounds.Height);
            Assert.AreEqual(plan.WindowTargets[0].PhysicalBounds.Bottom, plan.WindowTargets[1].PhysicalBounds.Top);
            Assert.AreEqual(-1920 + 40 * dpi / 96, plan.WindowTargets[0].PhysicalBounds.Left);
            Assert.AreEqual(8000, group[0].X, "Planning must not normalize the canonical model.");
            Assert.IsFalse(group[1].Visible, "Preparing an intent does not commit visibility.");
        }

        [TestMethod]
        public void RequestOwnsDetachedContentAndMemberOrderButAllowsSubsequentContentEdits()
        {
            List<StickyNoteData> group = Group();
            DockRestoreOperation operation = Create(group);
            group[1].Text = "edited later";
            group[0].PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { X = 40, Y = 60, Width = 500, Height = 300 });
            Assert.AreEqual("hidden member", operation.Snapshots[1].Text);
            Assert.AreEqual(320, operation.Plan.Group.Members[0].Width);
            Assert.IsTrue(operation.MatchesMembers(group));
            group.Clear();
            Assert.AreEqual(2, operation.MemberIds.Count);
            Assert.AreEqual("a", operation.MemberIds[0]);
            Assert.AreEqual("b", operation.MemberIds[1]);
        }

        [TestMethod]
        public void MissingPreferredDisplayCentersTemporarilyWithoutOverwritingIntent()
        {
            List<StickyNoteData> group = Group();
            foreach (StickyNoteData note in group) note.PreferredPlacement = new WindowPlacementPreference("mdp:unplugged", note.PreferredPlacement.LocalLogicalRect);
            DockRestoreOperation operation = Create(group);
            Assert.AreEqual(DockTopologyReprojectReason.TemporaryRehome, operation.Reason);
            Assert.IsTrue(operation.Plan.CenterInWorkArea);
            Assert.AreEqual("surface", operation.Target.RuntimeSurfaceId);
            Assert.AreEqual("mdp:unplugged", group[0].PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(40, group[0].PreferredPlacement.LocalLogicalRect.X);
        }

        [TestMethod]
        public void PreferredDisplayReturnCreatesAFreshRequestWithItsOwnCaptureContext()
        {
            List<StickyNoteData> group = Group();
            DisplayTopologySnapshot absent = new DisplayTopologySnapshot(6, new[] {
                new DisplaySurfaceSnapshot("other", "DISPLAY2", new PhysicalRect(0, 0, 1920, 1080),
                    new PhysicalRect(0, 40, 1920, 1040), true, 0,
                    new[] { new DisplayTargetIdentity("mdp:other", true, "other", "display", 0, 0, 0) }, 1.0) });
            DockRestoreOperation old = DockRestoreOperation.TryCreate(group, "b", true, true, absent, null, 10);
            var operations = new DockRestoreOperations();
            Assert.IsTrue(operations.TryBegin(old));
            Assert.IsTrue(operations.Finish(old));
            DockRestoreOperation current = Create(group, generation: 7, sequence: 11);
            Assert.IsTrue(operations.TryBegin(current));
            Assert.IsTrue(old.Cancellation.IsCancellationRequested);
            Assert.AreSame(absent, old.Topology);
            Assert.AreEqual(6L, old.Plan.TopologyGeneration);
            Assert.AreEqual(7L, current.Plan.TopologyGeneration);
            Assert.AreEqual(DockTopologyReprojectReason.RestorePreferred, current.Reason);
            Assert.IsFalse(current.Plan.CenterInWorkArea);
            Assert.IsFalse(operations.Finish(old));
            Assert.IsTrue(operations.IsCurrent(current));
            Assert.IsFalse(current.Cancellation.IsCancellationRequested);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        public void InvalidMemberInputsCannotStartPartialRestore(int invalid)
        {
            List<StickyNoteData> group = Group();
            if (invalid == 0) group[1].Id = group[0].Id;
            if (invalid == 1) group[1].DockGroupId = "another-group";
            if (invalid == 2) group[1].Id = String.Empty;
            if (invalid == 3) group[1] = null;
            if (invalid == 4) group.RemoveAt(1);
            var operations = new DockRestoreOperations();
            Assert.IsNull(Create(group));
            Assert.IsFalse(operations.TryBegin(null));
            Assert.AreEqual(0, operations.Snapshot().Length);
            Assert.IsTrue(group[0].Visible);
        }

        [TestMethod]
        public void FocusMustBelongToTheGroupAndLegacyRecoveryDoesNotInventPreference()
        {
            List<StickyNoteData> group = Group();
            Assert.IsNull(DockRestoreOperation.TryCreate(group, "other", true, true,
                StickyGeometryAuthorityTests.Topology(), null, 1));
            group[1].PreferredPlacement = null;
            group[1].DisplayId = "DISPLAY1";
            group[1].LocalLogicalWidth = 320;
            group[1].LocalLogicalHeight = 300;
            DockRestoreOperation recovery = Create(group);
            Assert.AreEqual(DockTopologyReprojectReason.LegacyRecovery, recovery.Reason);
            Assert.IsNull(recovery.Plan.Group);
            Assert.IsNotNull(recovery.Plan.RecoveryTargets);
            Assert.IsNull(group[1].PreferredPlacement);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        public void HideRemoveReorderRegroupOrModelReplacementRevokesMemberMatch(int change)
        {
            List<StickyNoteData> group = Group();
            DockRestoreOperation operation = Create(group);
            if (change == 0) group[0].Visible = false;
            if (change == 1) group.RemoveAt(1);
            if (change == 2) group.Reverse();
            if (change == 3) group[1].DockGroupId = "different";
            if (change == 4) group[1] = Group()[1];
            Assert.IsFalse(operation.MatchesMembers(group));
        }

        [TestMethod]
        public void DuplicateRequestAndOldCompletionCannotReleaseCurrentOperation()
        {
            var operations = new DockRestoreOperations();
            List<StickyNoteData> group = Group();
            DockRestoreOperation old = Create(group);
            DockRestoreOperation next = Create(group, sequence: 2);
            Assert.IsTrue(operations.TryBegin(old));
            Assert.IsFalse(operations.TryBegin(next));
            Assert.IsTrue(operations.ContainsGroup("GROUP"));
            Assert.IsFalse(operations.Finish(next));
            Assert.IsTrue(operations.IsCurrent(old));
            Assert.IsTrue(operations.Finish(old));
            Assert.IsTrue(operations.TryBegin(next));
            Assert.IsFalse(operations.Finish(old));
            Assert.IsTrue(operations.IsCurrent(next));
            Assert.IsFalse(next.Cancellation.IsCancellationRequested);
            Assert.IsFalse(operations.TryBegin(old));
        }

        [TestMethod]
        public void CancellationRevokesNativeTokenOnceAndLeavesCanonicalVisibilityAlone()
        {
            var operations = new DockRestoreOperations();
            List<StickyNoteData> group = Group();
            DockRestoreOperation operation = Create(group);
            operations.TryBegin(operation);
            int cancelled = 0;
            using (operation.Cancellation.Register(() => cancelled++))
            {
                Assert.IsTrue(operations.Finish(operation));
                Assert.IsFalse(operations.Finish(operation));
                operation.End();
            }
            Assert.AreEqual(1, cancelled);
            Assert.IsTrue(group[0].Visible);
            Assert.IsFalse(group[1].Visible);
            Assert.IsFalse(operation.MatchesMembers(group));
            Assert.IsTrue(operation.Snapshots[0].Visible);
            Assert.IsFalse(operation.Snapshots[1].Visible);
            Assert.IsTrue(operation.ContainsMember("B"));
            Assert.IsFalse(operation.ContainsMember("unrelated"));
        }

        [TestMethod]
        public void RestoreCommandCarriesTheCapturedPlanAndCancellationWithoutShowingIndividualMembers()
        {
            DockRestoreOperation operation = Create(Group());
            StickyUiCommand command = StickyUiCommand.RestoreDockGroup(operation, null);
            Assert.AreEqual(StickyUiCommandKind.RestoreDockGroup, command.Kind);
            Assert.AreSame(operation.Topology, command.Topology);
            Assert.AreSame(operation.Plan, command.DockGroupReprojectPlan);
            Assert.AreSame(operation, command.DockRestore);
            Assert.IsTrue(command.Flag);
            operation.End();
            Assert.IsTrue(command.DockRestore.Cancellation.IsCancellationRequested);
        }

        private static DockBatchMemberResult Member(long sequence, bool created)
        {
            return new DockBatchMemberResult("n", sequence,
                StickyGeometryAuthorityTests.Facts("n", sequence: sequence),
                StickyNoteUiSnapshot.Capture(new StickyNoteData { Id = "n" }), created);
        }

        [TestMethod]
        public void RecreatedWindowMayRestartItsSequenceOnlyAtRestoreAcceptance()
        {
            var runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RecordSequence("n", 100);
            runtime.SetImeComposition("n", true);
            runtime.SetInputFocus("n", true);
            runtime.TryBeginDelete("n");
            DockBatchMemberResult recreated = Member(3, true);
            Assert.IsFalse(runtime.CanApplyBatchSequence(recreated, false));
            Assert.IsTrue(runtime.CanApplyBatchSequence(recreated, true));
            runtime.AcceptBatchSequence(recreated, true);
            Assert.IsTrue(runtime.CanApplySequence("n", 4));
            Assert.IsFalse(runtime.CanApplySequence("n", 3));
            Assert.IsTrue(runtime.HasImeComposition);
            Assert.IsTrue(runtime.HasInputFocus);
            Assert.IsFalse(runtime.TryBeginDelete("n"));
        }

        [TestMethod]
        public void ExistingSessionCannotLowerItsWatermarkEvenInsideARestoreBatch()
        {
            var runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RecordSequence("n", 100);
            Assert.IsFalse(runtime.CanApplyBatchSequence(Member(99, false), true));
            Assert.IsFalse(runtime.CanApplyBatchSequence(Member(100, false), true));
            Assert.IsTrue(runtime.CanApplyBatchSequence(Member(101, false), true));
            runtime.AcceptBatchSequence(Member(101, false), true);
            Assert.IsFalse(runtime.CanApplySequence("n", 101));
            Assert.IsTrue(runtime.CanApplySequence("n", 102));
        }

        [TestMethod]
        public void ExistingHiddenNativeSessionCanBeRegisteredAfterAnEarlierCancelledRestore()
        {
            var runtime = new StickyHostedRuntime();
            DockBatchMemberResult member = Member(10, false);
            Assert.IsFalse(runtime.CanApplyBatchSequence(member, false));
            Assert.IsTrue(runtime.CanApplyBatchSequence(member, true));
            Assert.IsFalse(runtime.ContainsNote("n"), "Preflight cannot register a partial batch.");
            runtime.AcceptBatchSequence(member, true);
            Assert.IsTrue(runtime.ContainsNote("n"));
            Assert.IsFalse(runtime.CanApplySequence("n", 10));
            Assert.IsTrue(runtime.CanApplySequence("n", 11));
            Assert.IsFalse(runtime.ContainsNote("other"));
        }
    }
}
