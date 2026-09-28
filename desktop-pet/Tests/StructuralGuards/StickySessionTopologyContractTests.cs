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
            string update = SliceMethod(source,
                "internal void SetCurrentTopology(");
            Assert.IsTrue(update.IndexOf("session.AdoptTopology(snapshot)",
                StringComparison.Ordinal) < update.IndexOf(
                    "_localDockGestures.TryRebaseTopology(snapshot)", StringComparison.Ordinal));
            string local = SliceMethod(source, "private bool ApplyLocalDockFollowers(");
            StringAssert.Contains(local, "topology.Generation != sourceFacts.TopologyGeneration");
            AssertAdoptionBeforeSuppression(SliceMethod(source,
                "private StickyUiCommandResult ApplyDockGroupReproject("));
        }

        [TestMethod]
        public void CaptureDockFacts_IsAllOrNothing()
        {
            string capture = SliceMethod(ReadSource("StickyUiHost.cs"),
                "private StickyDockGestureCommit CaptureLocalDockCommit(");
            StringAssert.Contains(capture, "completion.AffectedMemberIds");
            StringAssert.Contains(capture, "!TryGetSession(noteId, out member)");
            StringAssert.Contains(capture, "captured == null");
            StringAssert.Contains(capture, "captured.Facts == null");
            StringAssert.Contains(capture, "return null;");
            Assert.IsFalse(capture.Contains("continue;"),
                "A missing member cannot silently produce a partial commit.");
        }

        [TestMethod]
        public void DockFactsBarrier_GuardsLocalRebaseAndFinalCommit()
        {
            string runtime = ReadSource("Features/StickyNotes/StickyDockLocalGestureRuntime.cs");
            string rebase = SliceMethod(runtime, "internal bool TryRebaseTopology(");
            StringAssert.Contains(rebase, "current.TopologyGeneration !=");
            StringAssert.Contains(rebase, "topology.Generation ||");
            string commit = SliceMethod(
                ReadSource("Features/StickyNotes/StickyDockController.cs"),
                "private bool TryApplyLocalDockGestureCommit(");
            StringAssert.Contains(commit, "commit.TopologyGeneration");
            StringAssert.Contains(commit, "StickyDockCommitVersion.Compute(note)");
            StringAssert.Contains(commit, "_workspace.Facts.TryPrepare(");
            StringAssert.Contains(commit, "member.Facts.TopologyGeneration !=");
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
