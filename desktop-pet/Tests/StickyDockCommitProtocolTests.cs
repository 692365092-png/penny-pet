using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyDockCommitProtocolTests
    {
        [TestMethod]
        public void FastAB_AckAReleasesBWithoutTouchingActiveState()
        {
            StickyDockCommitQueue queue =
                new StickyDockCommitQueue();
            StickyDockGestureCommit a = Commit(1, 0,
                StickyDockCommitIntent.Move);
            StickyDockGestureCommit b = Commit(2, 1,
                StickyDockCommitIntent.Move);

            Assert.IsTrue(queue.TryAdd(a));
            Assert.AreSame(a, queue.PeekReady());
            Assert.IsTrue(queue.TryAdd(b));
            Assert.IsNull(queue.PeekReady(),
                "B depends on A and must not overtake it.");

            StickyDockCommitResolution resolution =
                queue.Acknowledge(
                    new StickyDockCommitAck(1, true));
            Assert.IsTrue(resolution.Matched);
            Assert.IsTrue(resolution.Accepted);
            Assert.AreSame(b, queue.PeekReady());
        }

        [TestMethod]
        public void RejectA_CancelsOnlyDependentResults()
        {
            StickyDockCommitQueue queue =
                new StickyDockCommitQueue();
            StickyDockGestureCommit a = Commit(10, 0,
                StickyDockCommitIntent.MergeAfter);
            StickyDockGestureCommit b = Commit(11, 10,
                StickyDockCommitIntent.Detach);
            Assert.IsTrue(queue.TryAdd(a));
            Assert.AreSame(a, queue.PeekReady());
            Assert.IsTrue(queue.TryAdd(b));

            StickyDockCommitResolution resolution =
                queue.Acknowledge(
                    new StickyDockCommitAck(10, false));
            Assert.AreEqual(1,
                resolution.CancelledDependents.Count);
            Assert.AreEqual(11,
                resolution.CancelledDependents[0]);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [TestMethod]
        public void DuplicateCompletionAndLateAckAreIdempotent()
        {
            StickyDockCommitQueue queue =
                new StickyDockCommitQueue();
            StickyDockGestureCommit a = Commit(3, 0,
                StickyDockCommitIntent.HorizontalResize);
            Assert.IsTrue(queue.TryAdd(a));
            Assert.IsFalse(queue.TryAdd(a));
            Assert.AreSame(a, queue.PeekReady());

            Assert.IsTrue(queue.Acknowledge(
                new StickyDockCommitAck(3, true)).Matched);
            Assert.IsFalse(queue.Acknowledge(
                new StickyDockCommitAck(3, true)).Matched);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [TestMethod]
        public void ActiveB_CanCompleteAfterAckAAndRebasesItsVersions()
        {
            StickyDockCommitQueue queue =
                new StickyDockCommitQueue();
            StickyDockGestureCommit a = Commit(
                20, 0, StickyDockCommitIntent.MergeAfter);
            Assert.IsTrue(queue.TryAdd(a));
            Assert.AreSame(a, queue.PeekReady());

            StickyDockSceneProjection committedScene =
                new StickyDockSceneProjection(
                    new[]
                    {
                        new StickyDockSceneMember(
                            "A", "g", 0, true, 77)
                    }, 3);
            Assert.IsTrue(queue.Acknowledge(
                new StickyDockCommitAck(
                    20, true, committedScene)).Matched);

            StickyDockGestureCommit b = Commit(
                21, 20, StickyDockCommitIntent.Detach);
            Assert.IsTrue(queue.TryAdd(b),
                "B may have started locally while A was awaiting ACK.");
            StickyDockGestureCommit ready =
                queue.PeekReady();
            Assert.IsNotNull(ready);
            Assert.AreEqual(77,
                ready.BaselineVersions["A"]);
        }

        [TestMethod]
        public void PendingResultsAreBoundedInsteadOfSilentlyDropped()
        {
            StickyDockCommitQueue queue =
                new StickyDockCommitQueue();
            long dependency = 0;
            for (int index = 1; index <= 8; index++)
            {
                StickyDockGestureCommit commit =
                    Commit(index, dependency,
                        StickyDockCommitIntent.MergeAfter);
                Assert.IsTrue(queue.TryAdd(commit));
                if (index == 1)
                    Assert.AreSame(commit, queue.PeekReady());
                dependency = index;
            }

            Assert.IsFalse(queue.CanBeginGesture);
            Assert.IsFalse(queue.TryAdd(
                Commit(9, 8, StickyDockCommitIntent.Detach)));
            Assert.AreEqual(8, queue.PendingCount);
        }

        [TestMethod]
        public void UnrelatedContentHasNoPlaceInCommitVersionScope()
        {
            Dictionary<string, long> versions =
                new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = 41,
                    ["B"] = 42
                };
            StickyDockGestureCommit commit =
                new StickyDockGestureCommit(
                    5, 0, StickyDockCommitIntent.Move,
                    "A", String.Empty, 7, versions,
                    new DockBatchMemberResult[0]);

            Assert.AreEqual(2,
                commit.BaselineVersions.Count);
            Assert.IsFalse(commit.BaselineVersions
                .ContainsKey("body-text"));
        }

        private static StickyDockGestureCommit Commit(
            long id, long dependency,
            StickyDockCommitIntent intent)
        {
            return new StickyDockGestureCommit(
                id, dependency, intent, "A", String.Empty,
                1, new Dictionary<string, long>
                {
                    ["A"] = 1
                }, new DockBatchMemberResult[0]);
        }
    }
}
