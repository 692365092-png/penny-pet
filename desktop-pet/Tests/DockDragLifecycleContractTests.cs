using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockDragLifecycleContractTests
    {
        private const string Dock = "Features/StickyNotes/PetStickyDockCoordinator.cs";
        private const string Windows = "Features/StickyNotes/PetStickyWindowCoordinator.cs";

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void NormalDrag_EntersDraggingWithoutAsyncFactsBarrier()
        {
            string method = SliceMethod(ReadSource(Dock), "private void BeginStickyDockDrag(");
            Assert.IsTrue(method.IndexOf("TryEnterDragging(", StringComparison.Ordinal) >
                method.IndexOf("BeginPreparing(", StringComparison.Ordinal));
            Assert.IsTrue(method.Contains("CaptureDockInteractionBaseline("));
            Assert.IsFalse(method.Contains("StickyUiCommand.CaptureDockFacts("));
            Assert.IsFalse(method.Contains("TryApplyDockFactsBarrier("));
            // The async facts barrier is gone; the only post allowed at drag
            // start is the one-time Z-order band request, which is a visual
            // effect and never delays the first move.
            Assert.IsTrue(method.Contains(
                "StickyUiCommand.RaiseDockGroupForDrag("));
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void GestureStartClock_IsWrittenOnceAtHeaderStart()
        {
            string dock = ReadSource(Dock);
            string start = SliceMethod(dock, "private void BeginStickyDockDrag(");
            string reset = SliceMethod(dock, "private void ResetDockDragState(");
            foreach (string field in new[] { "_activeNoteDragStartedUtc", "_activeNoteDragStartFacts" })
            {
                string pattern = Regex.Escape(field) + @"\s*=(?!=)";
                Assert.AreEqual(1, Regex.Matches(start, pattern).Count);
                Assert.AreEqual(0, Regex.Matches(dock.Replace(start, "").Replace(reset, "") +
                    ReadSource(Windows), pattern).Count,
                    "No callback may rewrite original gesture provenance: " + field);
            }
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void Rebase_DoesNotRestartSplitGestureClock()
        {
            string method = SliceMethod(ReadSource(Windows),
                "private void ResumeDockDragAfterTopologyChange(");
            Assert.IsTrue(method.Contains("TryApplyDockFactsBarrier("));
            Assert.IsTrue(method.Contains("_activeNoteSplitEligible = false"));
            Assert.IsTrue(method.Contains("_activeNoteDragLastFacts = sourceRuntime"));
            Assert.IsFalse(method.Contains("_activeNoteDragStartedUtc ="));
            Assert.IsFalse(method.Contains("_activeNoteDragStartFacts ="));
        }

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
