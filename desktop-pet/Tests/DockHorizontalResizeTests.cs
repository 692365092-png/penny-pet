using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockHorizontalResizeTests
    {
        private static WindowFacts Facts(string id, int top, int height, long sequence = 1,
            int left = -1800, int width = 640, int dpi = 192, long generation = 7)
        {
            return new WindowFacts(id, "mdp:one", "DISPLAY1",
                new PhysicalRect(left, top, width, height), dpi, generation, sequence);
        }

        private static List<WindowFacts> Baseline()
        {
            return new List<WindowFacts> { Facts("a", -900, 450, dpi: 144),
                Facts("b", -450, 480), Facts("c", 30, 275, dpi: 96) };
        }

        private static DockResizeSession Start(string source = "b")
        { return DockResizeSession.TryStart(DockResizeKind.Horizontal, source, Baseline()); }

        private static StickyUiEvent Event(string id = "b", long sequence = 2,
            int left = -1600, int width = 1200, bool final = false, long generation = 7)
        {
            var snapshot = StickyNoteUiSnapshot.FromContentData(new StickyNoteData { Id = id, Visible = true });
            // During WM_SIZING the requested rect can differ from actual HWND facts.
            WindowFacts facts = Facts(id, -480, 510, sequence, final ? left : -1800,
                final ? width : 640, generation: generation);
            return final ? StickyUiEvent.FromSnapshot(StickyUiEventKind.DockHorizontalResizeCompleted,
                snapshot, sequence, facts, StickyGeometryAuthorityTests.Topology(generation)) :
                StickyUiEvent.HorizontalResize(snapshot, sequence, left, width,
                    facts, StickyGeometryAuthorityTests.Topology(generation));
        }

        private static DockBatchResult Result(params WindowFacts[] facts)
        {
            var members = new List<DockBatchMemberResult>();
            foreach (WindowFacts fact in facts)
                members.Add(new DockBatchMemberResult(fact.WindowId, fact.WindowSequence, fact, null));
            return new DockBatchResult(0, 7, members);
        }

        [TestMethod]
        [DataRow("a", 1200)]
        [DataRow("b", 1800)]
        [DataRow("c", 1350)]
        public void AnyMemberResizesAllOtherMembersInPhysicalPixels(string source, int width)
        {
            DockResizeSession session = Start(source);
            bool post;
            Assert.IsTrue(session.QueueLive(Event(source, width: width), out post));
            Assert.IsTrue(post);
            DockResizeBatch batch = session.Mailbox.TakeLatest();
            Assert.AreEqual(2, batch.Targets.Count);
            int index = 0;
            foreach (WindowFacts original in Baseline())
            {
                if (original.WindowId == source) continue;
                DockWindowTarget target = batch.Targets[index++];
                Assert.AreEqual(original.WindowId, target.NoteId);
                Assert.AreEqual(-1600, target.PhysicalBounds.Left);
                Assert.AreEqual(width, target.PhysicalBounds.Width, "There is no second logical width clamp.");
                Assert.AreEqual(original.PhysicalBounds.Top, target.PhysicalBounds.Top);
                Assert.AreEqual(original.PhysicalBounds.Height, target.PhysicalBounds.Height);
            }
        }

        [TestMethod]
        public void ManyLiveEventsPostOneLatestFrameAndDoNotReplaceActualSourceFacts()
        {
            DockResizeSession session = Start();
            var runtime = new StickyPlacementRuntime();
            foreach (WindowFacts facts in Baseline())
                Assert.IsTrue(runtime.TryUpdateEffective(facts.WindowId, facts, StickyGeometryAuthorityTests.Topology()));
            for (int sequence = 2; sequence <= 50; sequence++)
            {
                StickyUiEvent value = Event(sequence: sequence, width: 1000 + sequence);
                bool post;
                Assert.IsTrue(session.QueueLive(value, out post));
                Assert.AreEqual(sequence == 2, post);
                Assert.IsTrue(runtime.TryUpdateEffective(value.NoteId, value.Facts, value.Topology));
            }
            Assert.AreEqual(1050, session.Mailbox.TakeLatest().Targets[0].PhysicalBounds.Width);
            Assert.AreEqual(640, runtime.GetEffective("b").PhysicalBounds.Width);
            Assert.AreEqual(50L, runtime.GetEffective("b").WindowSequence);
            Assert.IsNull(session.Mailbox.TakeLatest());
        }

        [TestMethod]
        [DataRow("a", 2L, 7L, 1200)]
        [DataRow("b", 1L, 7L, 1200)]
        [DataRow("b", 2L, 8L, 1200)]
        [DataRow("b", 2L, 7L, 0)]
        public void RejectedProgressDoesNotInvalidateCurrentGesture(string id, long sequence, long generation, int width)
        {
            DockResizeSession session = Start();
            bool post;
            Assert.IsFalse(session.QueueLive(Event(id, sequence, width: width, generation: generation), out post));
            Assert.IsFalse(post);
            Assert.IsTrue(session.QueueLive(Event(sequence: 3), out post));
            Assert.IsTrue(post);
        }

        [TestMethod]
        public void AxesCannotConsumeEachOthersLifecycle()
        {
            DockResizeSession horizontal = Start();
            DockResizeSession divider = DockResizeSession.TryStart(DockResizeKind.Divider, "b", Baseline());
            StickyUiEvent value = Event();
            StickyUiEvent wrong = StickyUiEvent.DividerResize(StickyUiEventKind.DockDividerResizing,
                value.Snapshot, value.Sequence, 900, value.Facts, value.Topology);
            bool post;
            Assert.IsFalse(horizontal.QueueLive(wrong, out post));
            Assert.IsFalse(divider.QueueLive(value, out post));
            Assert.IsNull(divider.BeginFinal(Event(final: true)));
            Assert.IsTrue(horizontal.IsResizing);
            Assert.IsTrue(divider.IsResizing);
        }

        [TestMethod]
        public void FinalUsesSettledSourceRectAndHostAcknowledgmentCannotReopenLivePhase()
        {
            DockResizeSession session = Start();
            bool post;
            session.QueueLive(Event(), out post);
            DockResizeBatch final = session.BeginFinal(Event(sequence: 3, left: -1770, width: 900, final: true));
            Assert.AreEqual(-1770, final.Targets[0].PhysicalBounds.Left);
            Assert.AreEqual(900, final.Targets[0].PhysicalBounds.Width);
            Assert.AreEqual(-900, final.Targets[0].PhysicalBounds.Top);
            Assert.IsNull(session.Mailbox.TakeLatest());
            session.Mailbox.CompleteFinal(final);
            Assert.IsTrue(session.IsCurrentFinal(final));
            Assert.IsTrue(session.IsFinalizing);
            Assert.IsFalse(session.QueueLive(Event(sequence: 4), out post));
            Assert.IsNull(session.BeginFinal(Event(sequence: 5, final: true)));
        }

        [TestMethod]
        public void CorrectionKeepsEachSettledHeightAndRevokesTheFirstFinalToken()
        {
            DockResizeSession session = Start();
            DockResizeBatch first = session.BeginFinal(Event(final: true, width: 900));
            DockBatchResult drifted = Result(Facts("a", -920, 465, 3, width: 880),
                Facts("c", 60, 285, 3, width: 880));
            DockResizeBatch correction = session.TryCorrect(first, drifted);
            Assert.IsNotNull(correction);
            Assert.AreEqual(-1600, correction.Targets[0].PhysicalBounds.Left);
            Assert.AreEqual(900, correction.Targets[0].PhysicalBounds.Width);
            Assert.AreEqual(-920, correction.Targets[0].PhysicalBounds.Top);
            Assert.AreEqual(465, correction.Targets[0].PhysicalBounds.Height);
            Assert.AreEqual(60, correction.Targets[1].PhysicalBounds.Top);
            Assert.AreEqual(285, correction.Targets[1].PhysicalBounds.Height);
            Assert.IsFalse(session.IsCurrentFinal(first));
            session.Mailbox.CompleteFinal(first);
            Assert.AreSame(correction, session.Mailbox.TakeFinal(correction));
            Assert.IsNull(session.TryCorrect(correction, drifted), "Correction is bounded even if Windows still disagrees.");
        }

        [TestMethod]
        [DataRow(0, true)]
        [DataRow(2, true)]
        [DataRow(3, false)]
        public void FinalChecksHorizontalAlignmentWithTwoPixelTolerance(int drift, bool exact)
        {
            DockResizeSession session = Start();
            session.BeginFinal(Event(final: true, left: -1600, width: 900));
            Assert.AreEqual(exact, session.LayoutIsExact(Result(
                Facts("a", -900, 450, 3, left: -1600 + drift, width: 900),
                Facts("c", 30, 275, 3, left: -1600, width: 900 + drift))));
        }

        [TestMethod]
        public void PartialOrReorderedFollowersCannotSupplyFinalPreferencesOrCorrection()
        {
            DockResizeSession session = Start();
            DockResizeBatch final = session.BeginFinal(Event(final: true));
            foreach (DockBatchResult invalid in new[] {
                Result(Facts("a", -900, 450, 3)),
                Result(Facts("c", 30, 275, 3), Facts("a", -900, 450, 3)),
                Result(Facts("a", -900, 450, 3), Facts("a", 30, 275, 3)),
                Result(Facts("a", -900, 450, 3, generation: 8), Facts("c", 30, 275, 3)) })
            {
                Assert.IsFalse(session.HasExpectedFollowers(invalid));
                Assert.IsNull(session.TryCorrect(final, invalid));
            }
            Assert.IsTrue(session.IsCurrentFinal(final));
        }

        [TestMethod]
        public void DependentActionsStayPendingAcrossCorrectionAndAreReleasedOnceInOrder()
        {
            DockResizeSession session = Start();
            var observed = new List<string>();
            Assert.IsNull(session.Mutations);
            DockResizeBatch first = session.BeginFinal(Event(final: true));
            Assert.IsTrue(session.Mutations.Defer(null, null, () => observed.Add("hide")));
            Assert.IsTrue(session.Mutations.Defer(null, null, () => observed.Add("reopen")));
            session.Mailbox.CompleteFinal(first);
            Assert.AreEqual(0, observed.Count);
            DockResizeBatch correction = session.TryCorrect(first,
                Result(Facts("a", -900, 450, 3), Facts("c", 30, 275, 3)));
            Assert.IsNotNull(correction);
            Assert.AreEqual(0, observed.Count);
            foreach (Action action in session.Finish()) action();
            CollectionAssert.AreEqual(new[] { "hide", "reopen" }, observed);
            Assert.AreEqual(0, session.Finish().Length);
            Assert.IsFalse(session.IsCurrentFinal(correction));
            Assert.IsNull(session.Mutations);
        }

        [TestMethod]
        public void CanceledOrFailedFinalReleasesUserActionsAndCannotDrainNextGesture()
        {
            DockResizeSession old = Start();
            DockResizeBatch final = old.BeginFinal(Event(final: true));
            int hidden = 0;
            old.Mutations.Defer(null, null, () => hidden++);
            foreach (Action action in old.Finish()) action();
            DockResizeSession current = Start();
            current.BeginFinal(Event(final: true));
            current.Mutations.Defer(null, null, () => hidden += 10);
            old.Mailbox.CompleteFinal(final);
            foreach (Action action in old.Finish()) action();
            Assert.AreEqual(1, hidden);
            Assert.IsTrue(current.IsFinalizing);
            foreach (Action action in current.Finish()) action();
            Assert.AreEqual(11, hidden);
        }

        [TestMethod]
        public void DividerWaitsForFollowerSettlementBeforeReleasingDependentActions()
        {
            DockResizeSession divider = DockResizeSession.TryStart(DockResizeKind.Divider, "b", Baseline());
            StickyUiEvent value = Event();
            divider.BeginFinal(StickyUiEvent.DividerResize(StickyUiEventKind.DockDividerResizeCompleted,
                value.Snapshot, value.Sequence, 510, value.Facts, value.Topology));
            int hidden = 0;
            Assert.IsTrue(divider.Mutations.Defer(null, null, () => hidden++));
            Assert.AreEqual(0, hidden);
            foreach (Action action in divider.Finish()) action();
            Assert.AreEqual(1, hidden);
        }

        [TestMethod]
        public void ImmediateCollapseRoundTripKeepsEachWindowsOwnDpiWidth()
        {
            string directory = Path.Combine(Path.GetTempPath(), "penny-horizontal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var repository = new StickyNoteRepository(Path.Combine(directory, "notes.dat"));
            try
            {
                StickyNoteData root = repository.CreateDraft("root", Point.Empty);
                StickyNoteData source = repository.CreateDraft("source", Point.Empty);
                root.Visible = source.Visible = true;
                root.DockGroupId = source.DockGroupId = "resize-group";
                root.DockGroupOrder = 0;
                source.DockGroupOrder = 1;
                foreach (StickyNoteData note in new[] { root, source })
                {
                    note.PreferredDisplayTargetKey = "mdp:one";
                    note.PreferredLocalLogicalWidth = 320;
                    note.PreferredLocalLogicalHeight = 240;
                }
                DockResizeSession session = DockResizeSession.TryStart(DockResizeKind.Horizontal, source.Id,
                    new[] { Facts(root.Id, -900, 360, dpi: 144), Facts(source.Id, -540, 480) });
                StickyUiEvent completed = Event(source.Id, final: true, left: -1800, width: 900);
                session.BeginFinal(completed);
                CommitPreference(source, completed.Facts, true);
                Assert.IsTrue(session.Mutations.Defer(null, null, () => {
                    Assert.AreEqual(600, root.PreferredLocalLogicalWidth, "Hide must see the settled root width.");
                    root.Visible = source.Visible = false;
                    repository.SaveAsync();
                }));
                Assert.IsTrue(root.Visible);
                Assert.AreEqual(320, root.PreferredLocalLogicalWidth);
                DockBatchResult actual = Result(Facts(root.Id, -900, 360, 3, width: 900, dpi: 144));
                Assert.IsTrue(session.LayoutIsExact(actual));
                CommitPreference(root, actual.Members[0].Facts);
                foreach (Action action in session.Finish()) action();
                Assert.IsTrue(repository.WaitForPendingSaves(TimeSpan.FromSeconds(5)).Succeeded);
                StickyNoteRepository restored = StickyNoteRepository.LoadFromFile(Path.Combine(directory, "notes.dat"));
                StickyNoteData savedRoot = restored.Find(root.Id), savedSource = restored.Find(source.Id);
                Assert.IsFalse(savedRoot.Visible);
                Assert.AreEqual(600, savedRoot.PreferredLocalLogicalWidth);
                Assert.AreEqual(450, savedSource.PreferredLocalLogicalWidth);
                Assert.AreEqual(savedRoot.DockGroupId, savedSource.DockGroupId);
                Assert.AreEqual(0, savedRoot.DockGroupOrder);
                Assert.AreEqual(1, savedSource.DockGroupOrder);
            }
            finally
            {
                repository.WaitForPendingSaves(TimeSpan.FromSeconds(5));
                Directory.Delete(directory, true);
            }
        }

        private static void CommitPreference(StickyNoteData note, WindowFacts facts, bool source = false)
        {
            WindowPlacementPreference preference;
            Assert.IsTrue(StickyResizePreferences.TryBuild(note, facts, StickyGeometryAuthorityTests.Topology(),
                DockResizeKind.Horizontal, source, out preference));
            note.PreferredDisplayTargetKey = preference.PreferredTargetKey;
            note.PreferredLocalLogicalX = preference.LocalLogicalRect.X;
            note.PreferredLocalLogicalY = preference.LocalLogicalRect.Y;
            note.PreferredLocalLogicalWidth = preference.LocalLogicalRect.Width;
            note.PreferredLocalLogicalHeight = preference.LocalLogicalRect.Height;
        }
    }
}
