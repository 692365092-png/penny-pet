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
            Assert.IsTrue(mailbox.QueueFinal(Batch(1, 200)));
            Assert.IsNull(mailbox.TakeLatest());
            Assert.IsFalse(mailbox.QueueLive(Batch(1, 300)));
            DockDividerFollowerBatch final = mailbox.TakeFinal();
            Assert.IsNotNull(final);
            Assert.AreEqual(200, final.Targets[0].PhysicalBounds.Top);
            Assert.IsTrue(mailbox.FinalPending);
            mailbox.CompleteFinal();
            Assert.IsFalse(mailbox.FinalPending);
            Assert.IsTrue(mailbox.QueueLive(Batch(1, 400)));
        }
    }
}
