using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    public sealed partial class DockDragLifecycleContractTests
    {
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
    }
}
