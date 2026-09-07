using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // Static wiring evidence for the hosted Dock group Z-order band. These
    // source contracts cannot replace the manual interleave regression.
    [TestCategory("ArchitectureSourceBoundary")]
    [TestClass]
    public sealed class DockZOrderContractTests
    {
        [TestMethod]
        public void BeginDockDrag_QueuesZOrderBeforeEnteringDragging()
        {
            string source = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string method = SliceMethod(source,
                "private void BeginStickyDockDrag(");
            int raise = method.IndexOf("RaiseDockGroupForDrag(",
                StringComparison.Ordinal);
            int enter = method.IndexOf("TryEnterDragging(",
                StringComparison.Ordinal);

            Assert.IsTrue(raise >= 0,
                "A hosted Dock drag must restore the group Z-order band.");
            Assert.IsTrue(enter > raise,
                "The Z-order request must be queued before the live drag is armed.");
            Assert.IsTrue(method.Contains(
                "BuildActiveDockZOrderIds(seed)"),
                "The Z-order ids must come from the semantic Dock chain order.");
        }

        [TestMethod]
        public void StickySession_ZOrderBridgeDoesNotCaptureGeometry()
        {
            string method = SliceMethod(
                ReadSource("StickyWindowSession.cs"),
                "internal bool RaiseForDockDragWithoutActivation(");

            StringAssert.Contains(method,
                "_window.RaiseForDockDragWithoutActivation()");
            Assert.IsFalse(method.Contains("_sequence++"));
            Assert.IsFalse(method.Contains("CaptureWindowFacts"));
            Assert.IsFalse(method.Contains("CaptureSnapshot"));
            Assert.IsFalse(method.Contains("CurrentResult"));
            Assert.IsFalse(method.Contains("Save"));
        }

        [TestMethod]
        public void WpfZOrderPrimitive_DoesNotMoveResizeOrActivate()
        {
            string method = SliceMethod(
                ReadSource("Features/StickyNotes/StickyNoteWpf.cs"),
                "internal void RaiseForDockDragWithoutActivation(");

            StringAssert.Contains(method, "SwpNoMove");
            StringAssert.Contains(method, "SwpNoSize");
            StringAssert.Contains(method, "SwpNoActivate");
            Assert.IsFalse(method.Contains("Activate()"));
            Assert.IsFalse(method.Contains("BringToFront"));
        }

        [TestMethod]
        public void LiveDockBatch_RemainsGeometryOnly()
        {
            string source = ReadSource(
                "Infrastructure/Display/WindowsBatchWindowPlacementExecutor.cs");

            StringAssert.Contains(source, "SWP_NOZORDER");
            StringAssert.Contains(source, "SWP_NOACTIVATE");
        }

        [TestMethod]
        public void HostZOrder_ValidatesGenerationAndEpochBeforeEffect()
        {
            string method = SliceMethod(ReadSource("StickyUiHost.cs"),
                "private StickyUiCommandResult RaiseDockGroupForDrag(");

            StringAssert.Contains(method,
                "command.Topology.Generation != currentTopology.Generation");
            StringAssert.Contains(method,
                "command.InteractionEpoch != currentEpoch");
            StringAssert.Contains(method, "TryBuildDockDragRaiseOrder(");
            StringAssert.Contains(method, "reason=missing-session");
            StringAssert.Contains(method, "reason=stale-before-effect");
        }
    }
}
