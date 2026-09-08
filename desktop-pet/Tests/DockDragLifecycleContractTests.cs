using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed partial class DockDragLifecycleContractTests
    {
        private const string Dock = "Features/StickyNotes/PetStickyDockCoordinator.cs";
        private const string Windows = "Features/StickyNotes/PetStickyWindowCoordinator.cs";







        [TestMethod]
        [DataRow(520, 7)]
        public void SplitParameters_RemainProductConstants(int holdMilliseconds,
            int preHoldMovement)
        {
            Assert.IsTrue(StickyDockOperations.SplitHoldMilliseconds == holdMilliseconds);
            Assert.IsTrue(StickyDockOperations.SplitPreHoldMovement == preHoldMovement);
            Assert.IsTrue(StickyDockOperations.CancelsDockSplitHold(100, 8, 0));
            Assert.IsFalse(StickyDockOperations.CancelsDockSplitHold(600, 8, 0));
        }

        [TestMethod]
        public void SynchronousArm_AllowsFirstMoveAndRejectsOldEpochAfterRebase()
        {
            DockInteractionSession session = new DockInteractionSession();
            long epoch = session.BeginPreparing("middle", 4);
            Assert.IsTrue(session.TryEnterDragging(epoch, 4));
            Assert.IsTrue(session.CanPlan("middle", 4));
            long rebased = session.BeginRebase(5);
            Assert.IsFalse(session.TryEnterDragging(epoch, 4));
            Assert.IsTrue(session.TryEnterDragging(rebased, 5));
            Assert.IsTrue(session.CanPlan("middle", 5));
        }
    }
}
