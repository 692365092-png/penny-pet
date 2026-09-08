using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickySessionTopologyContractTests
    {
        [TestMethod]
        public void CaptureDockMember_AdoptsTopologyBeforeFactsCapture()
        {
            string method = SliceMethod(ReadSource("StickyWindowSession.cs"),
                "internal DockBatchMemberResult CaptureDockMember(");
            int adopt = method.IndexOf("AdoptTopology(topology)",
                StringComparison.Ordinal);
            int sequence = method.IndexOf("_sequence++", StringComparison.Ordinal);
            int capture = method.IndexOf("CaptureFactsWith(_topology)",
                StringComparison.Ordinal);
            Assert.IsTrue(adopt >= 0);
            Assert.IsTrue(sequence > adopt);
            Assert.IsTrue(capture > sequence);
        }

        [TestMethod]
        public void DockBatchPaths_AdoptCurrentTopologyBeforeSuppression()
        {
            string source = ReadSource("StickyUiHost.cs");
            AssertAdoptionBeforeSuppression(SliceMethod(source,
                "private StickyUiCommandResult ApplyDockPlan("));
            AssertAdoptionBeforeSuppression(SliceMethod(source,
                "private StickyUiCommandResult ApplyDockGroupReproject("));
        }

        [TestMethod]
        public void CaptureDockFacts_IsAllOrNothing()
        {
            string capture = SliceMethod(ReadSource("StickyUiHost.cs"),
                "private StickyUiCommandResult CaptureDockFactsForCommit(");
            Assert.IsTrue(capture.IndexOf("member == null",
                StringComparison.Ordinal) >= 0);
            Assert.IsTrue(capture.IndexOf("member.Facts == null",
                StringComparison.Ordinal) >= 0);
            Assert.IsTrue(capture.IndexOf("members.Count !=",
                StringComparison.Ordinal) >= 0);
            Assert.IsFalse(capture.IndexOf(
                "if (member != null) members.Add(member)",
                StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void DockFactsBarrier_GuardsRebaseAndFinalizing()
        {
            string dock = ReadSource("Features/StickyNotes/PetStickyDockCoordinator.cs");
            string rebase = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            Assert.IsFalse(SliceMethod(dock, "private void BeginStickyDockDrag(")
                .Contains("TryApplyDockFactsBarrier("));
            Assert.IsTrue(dock.IndexOf("TryApplyDockFactsBarrier(capture, expectedIds",
                StringComparison.Ordinal) >= 0);
            Assert.IsTrue(rebase.IndexOf("TryApplyDockFactsBarrier(result, expectedIds",
                StringComparison.Ordinal) >= 0);
            Assert.IsTrue(rebase.IndexOf("_activeNoteDragLastFacts = sourceRuntime",
                StringComparison.Ordinal) >= 0);
        }

        private static void AssertAdoptionBeforeSuppression(string method)
        {
            int adopt = method.IndexOf("AdoptTopology(topology)",
                StringComparison.Ordinal);
            int suppress = method.IndexOf("SetEventsSuppressed(true)",
                StringComparison.Ordinal);
            Assert.IsTrue(adopt >= 0);
            Assert.IsTrue(suppress > adopt);
        }

        internal static string SliceMethod(string source, string signature)
        {
            return SourceGuardText.RawSource.SliceMethod(source, signature);
        }

        internal static string ReadSource(string relativePath)
        {
            return SourceGuardText.RawSource.ReadSource(relativePath);
        }
    }
}
