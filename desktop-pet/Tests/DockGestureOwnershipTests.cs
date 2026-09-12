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
            Assert.IsFalse(session.TryFinish(old, 1, out invalidating));
            Assert.AreEqual(current, session.Epoch);
            Assert.AreEqual(3, session.MemberIds.Count);
            Assert.IsTrue(session.TryFinish(current, 2, out invalidating));
            Assert.AreEqual(0, session.PreviewFacts.Count);
            Assert.IsNull(session.StartFacts);
            session.BeginGesture(Facts("new"), new[] { "new" }, null, 2, Start.AddSeconds(1));
            Assert.IsFalse(session.TryFinish(current, 2, out invalidating));
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
            DockPlanMailbox mailbox = new DockPlanMailbox();
            DockPlacementPlan first = Plan(1), second = Plan(2);
            mailbox.ReplaceWithFinal(first);
            mailbox.ReplaceWithFinal(second);
            mailbox.CompleteFinal(1);
            Assert.AreSame(second, mailbox.TakeFinal(2));
            Assert.IsTrue(mailbox.ApplyQueued);
            mailbox.Clear();
            mailbox.Current = Plan(3);
            mailbox.ApplyQueued = true;
            mailbox.CompleteFinal(2);
            Assert.IsTrue(mailbox.ApplyQueued);
            Assert.AreEqual(3L, mailbox.TakeLatest().PlanSequence);
        }

        private static DockPlacementPlan Plan(long sequence)
        {
            return new DockPlacementPlan(1, sequence, "a", "screen", 96,
                new[] { new DockWindowTarget("a", new PhysicalRect(1, 1, 300, 230)) }, 1);
        }
    }
}
