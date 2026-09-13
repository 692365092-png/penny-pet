using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockDividerResizeSessionTests
    {
        private static WindowFacts Facts(string id, int top, int height, long seq = 1, long gen = 7,
            int width = 480, int dpi = 144)
        {
            return new WindowFacts(id, "screen", "DISPLAY1", new PhysicalRect(2133, top, width, height), dpi, gen, seq);
        }

        private static List<WindowFacts> Baseline()
        {
            return new List<WindowFacts> { Facts("a", -500, 330), Facts("b", -170, 452),
                Facts("c", 282, 600, width: 640, dpi: 192), Facts("d", 882, 275, width: 320, dpi: 96) };
        }

        private static DockResizeSession Start()
        { return DockResizeSession.TryStart(DockResizeKind.Divider, "b", Baseline()); }

        private static StickyUiEvent Event(bool final = false, string id = "b", long seq = 2,
            long gen = 7, int top = -170, int height = 900)
        {
            StickyNoteUiSnapshot snapshot = StickyNoteUiSnapshot.FromData(new StickyNoteData { Id = id, Visible = true });
            return StickyUiEvent.DividerResize(final ? StickyUiEventKind.DockDividerResizeCompleted :
                StickyUiEventKind.DockDividerResizing, snapshot, seq, height,
                Facts(id, top, height, seq, gen), StickyGeometryAuthorityTests.Topology(gen));
        }

        private static DockBatchResult Result(params WindowFacts[] facts)
        {
            List<DockBatchMemberResult> members = new List<DockBatchMemberResult>();
            foreach (WindowFacts fact in facts) members.Add(new DockBatchMemberResult(fact.WindowId, fact.WindowSequence, fact, null));
            return new DockBatchResult(0, 7, members);
        }

        [TestMethod]
        public void LiveUsesPhysicalDeltaAndKeepsEachFollowersOwnDimensions()
        {
            List<WindowFacts> input = Baseline();
            DockResizeSession session = DockResizeSession.TryStart(DockResizeKind.Divider, "b", input);
            input.Clear();
            bool post;
            Assert.IsTrue(session.QueueLive(Event(), out post));
            Assert.IsTrue(post);
            DockResizeBatch batch = session.Mailbox.TakeLatest();
            Assert.AreEqual(2, batch.Targets.Count);
            Assert.AreEqual("c", batch.Targets[0].NoteId);
            Assert.AreEqual(730, batch.Targets[0].PhysicalBounds.Top);
            Assert.AreEqual(600, batch.Targets[0].PhysicalBounds.Height);
            Assert.AreEqual(640, batch.Targets[0].PhysicalBounds.Width);
            Assert.AreEqual(1330, batch.Targets[1].PhysicalBounds.Top);
            Assert.AreEqual(275, batch.Targets[1].PhysicalBounds.Height);
            Assert.AreEqual(320, batch.Targets[1].PhysicalBounds.Width);
        }

        [TestMethod]
        [DataRow("a", 2L, 7L)]
        [DataRow("b", 1L, 7L)]
        [DataRow("b", 2L, 8L)]
        public void ForeignStaleAndWrongTopologyEventsLeaveTheGestureUsable(string id, long seq, long gen)
        {
            DockResizeSession session = Start();
            bool post;
            Assert.IsFalse(session.QueueLive(Event(id: id, seq: seq, gen: gen), out post));
            Assert.IsFalse(post);
            Assert.IsTrue(session.IsResizing);
            Assert.IsTrue(session.QueueLive(Event(seq: 3), out post));
            Assert.IsTrue(post);
        }

        [TestMethod]
        public void FinalReanchorsToActualSourceAndStaysFinalizingAfterHostAcknowledges()
        {
            DockResizeSession session = Start();
            bool post;
            session.QueueLive(Event(), out post);
            DockResizeBatch final = session.BeginFinal(Event(true, seq: 3, top: 0, height: 330));
            Assert.AreEqual(330, final.Targets[0].PhysicalBounds.Top);
            Assert.AreEqual(930, final.Targets[1].PhysicalBounds.Top);
            Assert.IsNull(session.Mailbox.TakeLatest());
            session.Mailbox.CompleteFinal(final);
            Assert.IsFalse(session.IsResizing);
            Assert.IsTrue(session.IsCurrentFinal(final));
            Assert.IsFalse(session.QueueLive(Event(seq: 4), out post));
            Assert.IsNull(session.BeginFinal(Event(true, seq: 5)));
        }

        [TestMethod]
        public void CorrectionUsesVerifiedActualSizesOnceAndInvalidatesFirstFinal()
        {
            DockResizeSession session = Start();
            DockResizeBatch first = session.BeginFinal(Event(true, top: 0, height: 330));
            DockBatchResult actual = Result(Facts("c", 340, 610, 4, width: 650), Facts("d", 952, 280, 4, width: 330));
            DockResizeBatch correction = session.TryCorrect(first, actual);
            Assert.IsNotNull(correction);
            Assert.AreEqual(330, correction.Targets[0].PhysicalBounds.Top);
            Assert.AreEqual(610, correction.Targets[0].PhysicalBounds.Height);
            Assert.AreEqual(650, correction.Targets[0].PhysicalBounds.Width);
            Assert.AreEqual(940, correction.Targets[1].PhysicalBounds.Top);
            Assert.IsFalse(session.IsCurrentFinal(first));
            Assert.IsTrue(session.IsCurrentFinal(correction));
            session.Mailbox.CompleteFinal(first);
            Assert.AreSame(correction, session.Mailbox.TakeFinal(correction));
            Assert.IsNull(session.TryCorrect(correction, actual), "Native seam repair is bounded to one attempt.");
        }

        [TestMethod]
        public void ExactSeamDoesNotScheduleCorrection()
        {
            DockResizeSession session = Start();
            DockResizeBatch final = session.BeginFinal(Event(true, top: 0, height: 330));
            DockBatchResult result = Result(Facts("c", 332, 600, 4), Facts("d", 932, 275, 4));
            Assert.IsTrue(session.LayoutIsExact(result));
            Assert.IsNull(session.TryCorrect(final, result));
        }

        [TestMethod]
        public void IncompleteReorderedDuplicateAndWrongGenerationResultsCannotDriveCorrection()
        {
            DockResizeSession session = Start();
            DockResizeBatch final = session.BeginFinal(Event(true));
            foreach (DockBatchResult invalid in new[] {
                Result(Facts("c", 500, 600, 4)),
                Result(Facts("d", 500, 275, 4), Facts("c", 800, 600, 4)),
                Result(Facts("c", 500, 600, 4), Facts("c", 800, 600, 4)),
                Result(Facts("c", 500, 600, 4, 8), Facts("d", 800, 275, 4)),
                Result(Facts("c", 500, 0, 4), Facts("d", 800, 275, 4)) })
            {
                Assert.IsFalse(session.HasExpectedFollowers(invalid));
                Assert.IsNull(session.TryCorrect(final, invalid));
                Assert.IsTrue(session.IsCurrentFinal(final));
            }
        }

        [TestMethod]
        public void FinishRevokesFinalCallbackAndNativeQueueWithoutTouchingNewGesture()
        {
            DockResizeSession old = Start();
            DockResizeBatch final = old.BeginFinal(Event(true));
            old.Finish();
            DockResizeSession current = Start();
            bool post;
            current.QueueLive(Event(), out post);
            Assert.IsFalse(old.IsCurrentFinal(final));
            Assert.IsNull(old.Mailbox.TakeFinal(final));
            Assert.IsFalse(old.HasExpectedFollowers(Result(Facts("c", 330, 600, 4), Facts("d", 930, 275, 4))));
            old.Mailbox.CompleteFinal(final);
            Assert.IsTrue(current.IsResizing);
            Assert.IsNotNull(current.Mailbox.TakeLatest());
        }

        [TestMethod]
        public void MembershipChangeInvalidatesBaselineButPersistedGeometryDoesNot()
        {
            DockResizeSession session = Start();
            List<StickyNoteData> members = new List<StickyNoteData>();
            foreach (string id in new[] { "a", "b", "c", "d" })
                members.Add(new StickyNoteData { Id = id, Visible = true, Height = 123, DockParentId = "stale" });
            Assert.IsTrue(session.MatchesMembers(members));
            members[2].Visible = false;
            Assert.IsFalse(session.MatchesMembers(members));
            members[2].Visible = true;
            members.Reverse();
            Assert.IsFalse(session.MatchesMembers(members));
        }

        [TestMethod]
        public void StartRejectsMixedTopologyOrMissingSourceWithoutChangingExistingSession()
        {
            DockResizeSession current = Start();
            List<WindowFacts> facts = Baseline();
            facts[2] = Facts("c", 282, 600, gen: 8);
            Assert.IsNull(DockResizeSession.TryStart(DockResizeKind.Divider, "b", facts));
            Assert.IsNull(DockResizeSession.TryStart(DockResizeKind.Divider, "missing", Baseline()));
            Assert.IsNull(DockResizeSession.TryStart(DockResizeKind.Divider, "d", Baseline()));
            Assert.IsTrue(current.IsResizing);
        }

        [TestMethod]
        public void TopologyRecoveryUsesNewFactsForTheWholeStack()
        {
            DockResizeSession old = Start();
            old.Finish();
            List<WindowFacts> fresh = new List<WindowFacts> {
                Facts("b", 20, 450, 10, 8), Facts("c", 500, 620, 9, 8), Facts("d", 1200, 285, 9, 8) };
            DockResizeSession recovered = DockResizeSession.TryStart(DockResizeKind.Divider, "b", fresh);
            DockResizeBatch final = recovered.BeginFinal(Event(true, seq: 10, gen: 8, top: 20, height: 450));
            Assert.AreEqual(8L, final.TopologyGeneration);
            Assert.AreEqual(470, final.Targets[0].PhysicalBounds.Top);
            Assert.AreEqual(620, final.Targets[0].PhysicalBounds.Height);
            Assert.AreEqual(1090, final.Targets[1].PhysicalBounds.Top);
        }
    }
}
