using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockMutationOrderingTests
    {
        private static List<StickyNoteData> Group(string prefix)
        {
            return new List<StickyNoteData> {
                Note(prefix + "1", prefix, 0, true), Note(prefix + "h", prefix, 1, false),
                Note(prefix + "2", prefix, 2, true) };
        }

        private static StickyNoteData Note(string id, string group, int order, bool visible)
        {
            return new StickyNoteData { Id = id, DockGroupId = group, DockGroupOrder = order, Visible = visible,
                PreferredDisplayTargetKey = "mdp:one", PreferredLocalLogicalX = 40,
                PreferredLocalLogicalY = 60, PreferredLocalLogicalWidth = 320, PreferredLocalLogicalHeight = 240 };
        }

        private static DockInteractionSession Header(List<StickyNoteData> group)
        {
            var session = new DockInteractionSession();
            var visible = group.FindAll(note => note.Visible).ConvertAll(note => note.Id);
            var root = new DockWindowFacts(group[0].Id, -1800, -900, 640, 480, true, false);
            long epoch = session.BeginGesture(root, visible, null, 7, DateTime.UtcNow);
            Assert.IsTrue(session.TryEnterDragging(epoch, 7));
            return session;
        }

        private static long Finalize(DockInteractionSession session, List<StickyNoteData> scope, long generation = 7)
        {
            return session.BeginFinalizing(generation, String.Empty,
                scope.FindAll(note => note.Visible).ConvertAll(note => note.Id), scope);
        }

        [TestMethod]
        [DataRow("a1", "a", true)]
        [DataRow("ah", "a", true)]
        [DataRow("bh", "b", true)]
        [DataRow("new-slot", "B", true)]
        [DataRow("other", "outside", false)]
        public void FinalScopeIncludesBothMergeGroupsAndHiddenSlots(string id, string groupId, bool expected)
        {
            List<StickyNoteData> source = Group("a"), target = Group("b");
            DockInteractionSession session = Header(source);
            var scope = new List<StickyNoteData>(source);
            scope.AddRange(target);
            Finalize(session, scope);
            bool ran = false;
            Assert.AreEqual(expected, session.Mutations.Defer(id, groupId, () => ran = true));
            Assert.IsFalse(ran);
            Assert.AreEqual(4, session.MemberIds.Count, "The native plan still includes only visible members.");
        }

        [TestMethod]
        public void ScopeCopiesIdentityBeforeTheModelIsRegroupedOrRemoved()
        {
            List<StickyNoteData> group = Group("a");
            DockInteractionSession session = Header(group);
            Finalize(session, group);
            foreach (StickyNoteData note in group) note.DockGroupId = "changed";
            group.Clear();
            Assert.IsTrue(session.Mutations.Contains("ah", null));
            Assert.IsTrue(session.Mutations.Contains("another-slot", "a"));
            Assert.IsFalse(session.Mutations.Contains("another-slot", "changed"));
        }

        [TestMethod]
        public void TopologyRestartRetainsQueuedActionsAndOldCompletionCannotReleaseThem()
        {
            List<StickyNoteData> group = Group("a");
            DockInteractionSession session = Header(group);
            long old = Finalize(session, group);
            DockMutationQueue queue = session.Mutations;
            var observed = new List<string>();
            queue.Defer("ah", "a", () => observed.Add("hide"));
            session.RestartFinalizing(8);
            long current = Finalize(session, group, 8);
            Assert.AreSame(queue, session.Mutations);
            queue.Defer(null, null, () => observed.Add("exit"));
            Assert.IsFalse(session.TryFinish(old, 7, out _, out Action[] stale));
            Assert.AreEqual(0, stale.Length);
            Assert.AreEqual(0, observed.Count);
            Assert.IsTrue(session.TryFinish(current, 8, out _, out Action[] ready));
            Assert.IsFalse(session.IsActive);
            Assert.IsNull(session.Mutations);
            foreach (Action action in ready) action();
            CollectionAssert.AreEqual(new[] { "hide", "exit" }, observed);
            Assert.AreEqual(0, queue.Release().Length);
            Assert.IsFalse(queue.Defer(null, null, () => observed.Add("late")));
        }

        [TestMethod]
        public void FailedOrCancelledFinalReleasesActionsOnceWithoutCommittingMerge()
        {
            List<StickyNoteData> source = Group("a"), target = Group("b");
            DockInteractionSession session = Header(source);
            session.StageMerge(StickyDockOperations.PrepareMergeAfterParent(target, target[0], source));
            var scope = new List<StickyNoteData>(source);
            scope.AddRange(target);
            long old = Finalize(session, scope);
            int count = 0;
            session.Mutations.Defer("bh", "b", () => count++);
            session.Reset(out Action[] ready);
            Assert.IsNull(session.PendingMerge);
            Assert.AreEqual("a", source[0].DockGroupId);
            Assert.AreEqual("b", target[0].DockGroupId);
            foreach (Action action in ready) action();
            session.Reset(out Action[] duplicate);
            Assert.AreEqual(0, duplicate.Length);
            Assert.IsFalse(session.TryFinish(old, 7, out _, out _));
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void QueuedReopenUsesTheCommittedMergeAndRetainsHiddenMembership()
        {
            List<StickyNoteData> source = Group("a"), target = Group("b");
            var notes = new List<StickyNoteData>(source);
            notes.AddRange(target);
            DockInteractionSession session = Header(source);
            DockMergePlan merge = StickyDockOperations.PrepareMergeAfterParent(target, target[0], source);
            session.StageMerge(merge);
            Assert.IsTrue(merge.TryResolve(notes, out List<StickyNoteData> scope));
            long epoch = Finalize(session, scope);
            DockRestoreOperation restore = null;
            session.Mutations.Defer("bh", "b", () => restore = DockRestoreOperation.TryCreate(
                StickyDockGroups.GetOrderedGroup(notes, target[1]), "bh", true, true,
                StickyGeometryAuthorityTests.Topology(), null, 20));
            Assert.IsNull(restore);
            Assert.IsTrue(merge.TryCommit(notes));
            Assert.IsTrue(session.TryFinish(epoch, 7, out _, out Action[] ready));
            foreach (Action action in ready) action();
            Assert.IsNotNull(restore);
            CollectionAssert.AreEqual(new[] { "b1", "a1", "ah", "a2", "bh", "b2" },
                new List<string>(restore.MemberIds));
            Assert.AreEqual("b1", restore.GroupId);
            Assert.IsFalse(source[1].Visible);
            Assert.IsFalse(target[1].Visible);
            restore.End();
        }

        [TestMethod]
        public void HideThenReopenObservesFinalPreferredWidthInsteadOfThePreDragValue()
        {
            List<StickyNoteData> group = Group("a");
            DockInteractionSession session = Header(group);
            long epoch = Finalize(session, group);
            DockRestoreOperation restore = null;
            session.Mutations.Defer("a1", "a", () => group.ForEach(note => note.Visible = false));
            session.Mutations.Defer("ah", "a", () => {
                Assert.IsFalse(session.IsActive);
                Assert.IsFalse(group[0].Visible);
                restore = DockRestoreOperation.TryCreate(group, "ah", true, true,
                    StickyGeometryAuthorityTests.Topology(), null, 21);
            });
            Assert.IsTrue(group[0].Visible);
            Assert.IsNull(restore);
            // Simulate the accepted Pet-side preferred commit before releasing
            // actions; native HWND capture itself is outside this test.
            group[0].PreferredLocalLogicalWidth = 600;
            Assert.IsTrue(session.TryFinish(epoch, 7, out _, out Action[] ready));
            foreach (Action action in ready) action();
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(restore.Plan, restore.Target, 144);
            Assert.AreEqual(900, plan.WindowTargets[0].PhysicalBounds.Width);
            Assert.AreEqual(900, plan.WindowTargets[2].PhysicalBounds.Width);
            restore.End();
        }

        [TestMethod]
        public void ReleasedActionCanStartNewFinalizationWithoutOldCallbacksClearingIt()
        {
            List<StickyNoteData> group = Group("a");
            DockInteractionSession session = Header(group);
            long old = Finalize(session, group), next = 0;
            session.Mutations.Defer(null, null, () => {
                long begin = session.BeginGesture(new DockWindowFacts("a1", 0, 0, 600, 480, true, false),
                    new[] { "a1", "a2" }, null, 7, DateTime.UtcNow);
                Assert.IsTrue(session.TryEnterDragging(begin, 7));
                next = Finalize(session, group);
            });
            Assert.IsTrue(session.TryFinish(old, 7, out _, out Action[] ready));
            foreach (Action action in ready) action();
            int count = 0;
            session.Mutations.Defer("ah", "a", () => count++);
            Assert.IsFalse(session.TryFinish(old, 7, out _, out Action[] stale));
            Assert.AreEqual(0, stale.Length);
            Assert.IsTrue(session.IsFinalizing);
            Assert.AreEqual(next, session.Epoch);
            Assert.IsTrue(session.TryFinish(next, 7, out _, out ready));
            foreach (Action action in ready) action();
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void BothResizeKindsProtectHiddenSlotsUsingCapturedMembership(int kindValue)
        {
            DockResizeKind kind = kindValue == 0 ? DockResizeKind.Horizontal : DockResizeKind.Divider;
            List<StickyNoteData> group = Group("a");
            var facts = new[] { Facts("a1", 1), Facts("a2", 1) };
            DockResizeSession session = DockResizeSession.TryStart(kind, "a1", facts, group);
            Assert.IsNull(session.Mutations);
            var snapshot = StickyNoteUiSnapshot.Capture(group[0]);
            StickyUiEvent completed = kind == DockResizeKind.Horizontal
                ? StickyUiEvent.FromSnapshot(StickyUiEventKind.DockHorizontalResizeCompleted,
                    snapshot, 2, Facts("a1", 2), StickyGeometryAuthorityTests.Topology())
                : StickyUiEvent.DividerResize(StickyUiEventKind.DockDividerResizeCompleted,
                    snapshot, 2, 480, Facts("a1", 2), StickyGeometryAuthorityTests.Topology());
            Assert.IsNotNull(session.BeginFinal(completed));
            group[0].DockGroupId = "changed-after-capture";
            Assert.IsTrue(session.Mutations.Contains("hidden-in-original-group", "a"));
            Assert.IsTrue(session.Mutations.Contains("ah", null));
            Assert.IsFalse(session.Mutations.Contains("other", "outside"));
            int hidden = 0;
            session.Mutations.Defer("ah", null, () => hidden++);
            Assert.AreEqual(0, hidden);
            foreach (Action action in session.Finish()) action();
            Assert.AreEqual(1, hidden);
            Assert.IsNull(session.Mutations);
        }

        private static WindowFacts Facts(string id, long sequence)
        {
            return new WindowFacts(id, "mdp:one", "DISPLAY1", new PhysicalRect(-1800, -900, 640, 480), 192, 7, sequence);
        }
    }
}
