using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockGestureOwnershipTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1);
        private static DockWindowFacts Facts(string id, int x = 100)
        { return new DockWindowFacts(id, x, 100, 300, 230, true, false); }

        private static DockInteractionSession Gesture()
        {
            DockInteractionSession session = new DockInteractionSession();
            long epoch = session.BeginGesture(Facts("b"), new[] { "a", "b", "c" },
                new Dictionary<string, DockWindowFacts> { ["a"] = Facts("a"), ["b"] = Facts("b"), ["c"] = Facts("c") }, 1, Start);
            Assert.IsTrue(session.TryEnterDragging(epoch, 1));
            return session;
        }

        [TestMethod]
        public void RebaseCancelsSplitWithoutChangingOriginalGestureClock()
        {
            DockInteractionSession session = Gesture();
            DockWindowFacts origin = session.StartFacts;
            long epoch = session.BeginRebase(2);
            session.AcceptCapturedFacts(new[] { Facts("a", 900), Facts("b", 900), Facts("c", 900) }, true);
            session.RecordMove(Facts("b", 900));
            Assert.IsTrue(session.TryEnterDragging(epoch, 2));
            Assert.AreSame(origin, session.StartFacts);
            Assert.AreEqual(Start, session.StartedUtc);
            Assert.AreEqual(900, session.LastFacts.X);
            Assert.AreEqual(DockSplitDecision.None, session.EvaluateSplit(Facts("b", 901), Start.AddSeconds(2)));
        }

        [TestMethod]
        [DataRow(519, 107, 0)]
        [DataRow(100, 108, 1)]
        [DataRow(520, 108, 2)]
        public void SplitKeepsProductTimingAndMovementThresholds(int milliseconds, int x, int expected)
        {
            DockInteractionSession session = Gesture();
            Assert.AreEqual((DockSplitDecision)expected, session.EvaluateSplit(Facts("b", x), Start.AddMilliseconds(milliseconds)));
        }

        [TestMethod]
        public void DetachChangesMovingMembersAndRetainsRemainderBaseline()
        {
            DockInteractionSession session = Gesture();
            session.Detach("a");
            CollectionAssert.AreEqual(new[] { "b" }, session.CopyMemberIds());
            Assert.AreEqual("a", session.RemainderNoteId);
            Assert.AreEqual(3, session.BaselineFacts.Count);
            Assert.IsTrue(session.Detached);
            Assert.IsFalse(session.SplitEligible);
        }

        [TestMethod]
        public void StaleFinalCompletionCannotClearRebasedFinalizationOrNewGesture()
        {
            DockInteractionSession session = Gesture();
            long old = session.BeginFinalizing(1, "a");
            long current = session.RestartFinalizing(2);
            long invalidating;
            Assert.IsFalse(session.TryFinish(old, 1, out invalidating, out _));
            Assert.AreEqual(current, session.Epoch);
            Assert.AreEqual(3, session.MemberIds.Count);
            Assert.IsTrue(session.TryFinish(current, 2, out invalidating, out _));
            Assert.AreEqual(0, session.PreviewFacts.Count);
            Assert.IsNull(session.StartFacts);
            session.BeginGesture(Facts("new"), new[] { "new" }, null, 2, Start.AddSeconds(1));
            Assert.IsFalse(session.TryFinish(current, 2, out invalidating, out _));
            Assert.AreEqual("new", session.SourceNoteId);
        }

        [TestMethod]
        public void InvalidBeginLeavesCurrentGestureUntouchedAndViewsCannotBeMutated()
        {
            DockInteractionSession session = Gesture();
            long epoch = session.Epoch;
            Assert.AreEqual(0L, session.BeginGesture(Facts("z"), new[] { "z" }, null, 1, Start));
            Assert.AreEqual(epoch, session.Epoch);
            Assert.AreEqual("b", session.SourceNoteId);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)session.MemberIds).Clear());
            string[] copy = session.CopyMemberIds();
            copy[0] = "external-mutation";
            Assert.AreEqual("a", session.MemberIds[0]);
        }

        [TestMethod]
        public void OldMailboxFinalCompletionDoesNotAcknowledgeNewFinalOrLivePlan()
        {
            var owner = new DockGestureOwner();
            DockFrameMailbox<DockPlacementPlan> mailbox = owner.Plans;
            DockPlacementPlan first = Plan(1), second = Plan(2);
            mailbox.QueueFinal(first);
            mailbox.QueueFinal(second);
            mailbox.CompleteFinal(first);
            Assert.AreSame(second, mailbox.TakeFinal(second));
            Assert.IsTrue(mailbox.HasPending);
            owner.ResetDrag();
            DockPlacementPlan live = Plan(3);
            Assert.IsTrue(owner.Plans.QueueLive(live));
            mailbox.CompleteFinal(second);
            Assert.IsNull(mailbox.TakeFinal(second));
            Assert.AreSame(live, owner.Plans.TakeLatest());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void QueuedCallbackCannotConsumeNextLifetimePlan(bool topologyRebase)
        {
            DockGestureOwner owner = new DockGestureOwner();
            DockFrameMailbox<DockPlacementPlan> retired = owner.Plans;
            Assert.IsTrue(retired.QueueLive(Plan(1)));
            Func<DockPlacementPlan> queuedCallback = retired.TakeLatest;

            if (topologyRebase) owner.RenewPlans();
            else owner.ResetDrag();
            DockPlacementPlan next = Plan(2);
            Assert.IsTrue(owner.Plans.QueueLive(next));

            Assert.IsNull(queuedCallback(), "A callback already queued on Sticky STA must not steal work from the next gesture or topology generation.");
            Assert.AreSame(next, owner.Plans.TakeLatest());
            Assert.IsFalse(retired.QueueLive(next));
            Assert.IsFalse(retired.QueueFinal(next));
        }

        [TestMethod]
        public void PlacementPlanOwnsItsTargetCollection()
        {
            var input = new List<DockWindowTarget> { new DockWindowTarget("a", new PhysicalRect(1, 1, 300, 230)) };
            var plan = new DockPlacementPlan(1, 1, "a", "screen", 96, input);
            input.Clear();
            Assert.AreEqual(1, plan.WindowTargets.Count);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<DockWindowTarget>)plan.WindowTargets).Clear());
        }

        private static DockPlacementPlan Plan(long sequence)
        {
            return new DockPlacementPlan(1, sequence, "a", "screen", 96,
                new[] { new DockWindowTarget("a", new PhysicalRect(1, 1, 300, 230)) }, 1);
        }
    }
}
