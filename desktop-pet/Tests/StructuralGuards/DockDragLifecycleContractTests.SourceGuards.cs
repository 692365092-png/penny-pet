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
            int begin = method.IndexOf("BeginGesture(", StringComparison.Ordinal);
            Assert.IsTrue(begin >= 0 && method.IndexOf("TryEnterDragging(", StringComparison.Ordinal) > begin);
            Assert.IsTrue(method.Contains("CaptureDockInteractionBaseline("));
            Assert.IsFalse(method.Contains("StickyUiCommand.CaptureDockFacts("));
            Assert.IsFalse(method.Contains("TryApplyDockFactsBarrier("));
            // The async facts barrier is gone; the only post allowed at drag
            // start is the one-time Z-order band request, which is a visual
            // effect and never delays the first move.
            Assert.IsTrue(method.Contains(
                "StickyUiCommand.RaiseDockGroupForDrag("));
        }

    }
}
