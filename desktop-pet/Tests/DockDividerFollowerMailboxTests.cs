using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockDividerFollowerMailboxTests
    {
        private static DockDividerFollowerBatch Batch(
            long generation, int top)
        {
            return new DockDividerFollowerBatch(generation,
                new List<DockWindowTarget>
                {
                    new DockWindowTarget("n-1",
                        new PhysicalRect(0, top, 400, 300)),
                });
        }

        [TestMethod]
        public void QueueLive_CoalescesWhileApplyInFlight()
        {
            DockDividerFollowerMailbox mailbox =
                new DockDividerFollowerMailbox();
            Assert.IsTrue(mailbox.QueueLive(Batch(1, 100)));
            Assert.IsFalse(mailbox.QueueLive(Batch(1, 120)));
            DockDividerFollowerBatch taken = mailbox.TakeLatest();
            Assert.IsNotNull(taken);
            Assert.AreEqual(120, taken.Targets[0].PhysicalBounds.Top);
            Assert.IsTrue(mailbox.QueueLive(Batch(1, 140)));
        }

        [TestMethod]
        public void QueueFinal_SupersedesPendingLiveFrames()
        {
            DockDividerFollowerMailbox mailbox =
                new DockDividerFollowerMailbox();
            Assert.IsTrue(mailbox.QueueLive(Batch(1, 100)));
            DockDividerFollowerBatch expected = Batch(1, 200);
            Assert.IsTrue(mailbox.QueueFinal(expected));
            Assert.IsNull(mailbox.TakeLatest());
            Assert.IsFalse(mailbox.QueueLive(Batch(1, 300)));
            DockDividerFollowerBatch final = mailbox.TakeFinal(expected);
            Assert.IsNotNull(final);
            Assert.AreEqual(200, final.Targets[0].PhysicalBounds.Top);
            mailbox.CompleteFinal(expected);
            Assert.IsNull(mailbox.TakeFinal(expected));
            Assert.IsFalse(mailbox.QueueLive(Batch(1, 400)),
                "A host acknowledgment cannot reopen a finalizing gesture to live frames.");
        }

        [TestMethod]
        public void OldFinalCannotTakeOrAcknowledgeCorrection()
        {
            DockDividerFollowerMailbox mailbox = new DockDividerFollowerMailbox();
            DockDividerFollowerBatch first = Batch(1, 200), corrected = Batch(1, 220);
            mailbox.QueueFinal(first);
            mailbox.QueueFinal(corrected);
            Assert.IsNull(mailbox.TakeFinal(first));
            mailbox.CompleteFinal(first);
            Assert.AreSame(corrected, mailbox.TakeFinal(corrected));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void CancelRevokesPendingWorkAndPermanentlyClosesMailbox(bool final)
        {
            DockDividerFollowerMailbox mailbox = new DockDividerFollowerMailbox();
            DockDividerFollowerBatch batch = Batch(1, 200);
            if (final) mailbox.QueueFinal(batch);
            else mailbox.QueueLive(batch);
            mailbox.Cancel();
            Assert.IsNull(mailbox.TakeLatest());
            Assert.IsNull(mailbox.TakeFinal(batch));
            mailbox.CompleteFinal(batch);
            Assert.IsFalse(mailbox.QueueLive(batch));
            Assert.IsFalse(mailbox.QueueFinal(batch));
        }

        [TestMethod]
        public void TargetsCannotChangeAfterQueueing()
        {
            List<DockWindowTarget> input = new List<DockWindowTarget> {
                new DockWindowTarget("a", new PhysicalRect(0, 100, 400, 300)) };
            DockDividerFollowerBatch batch = new DockDividerFollowerBatch(1, input);
            input.Clear();
            Assert.AreEqual(1, batch.Targets.Count);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<DockWindowTarget>)batch.Targets).Clear());
        }
    }
}
