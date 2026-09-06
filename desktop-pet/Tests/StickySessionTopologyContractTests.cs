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
        public void DockFactsBarrier_GuardsPreparingRebaseAndFinalizing()
        {
            string dock = ReadSource("Features/StickyNotes/PetStickyDockCoordinator.cs");
            string rebase = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            Assert.IsTrue(dock.IndexOf("TryApplyDockFactsBarrier(result, refreshIds",
                StringComparison.Ordinal) >= 0);
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

        private static string SliceMethod(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Method not found: " + signature);
            int open = source.IndexOf('{', start);
            Assert.IsTrue(open >= 0);
            int depth = 0;
            for (int index = open; index < source.Length; index++)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0)
                    return source.Substring(start, index - start + 1);
            }
            Assert.Fail("Unclosed method: " + signature);
            return String.Empty;
        }

        private static string ReadSource(string relativePath)
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string project = Path.Combine(current.FullName, "desktop-pet",
                    relativePath);
                if (File.Exists(project)) return File.ReadAllText(project);
                current = current.Parent;
            }
            throw new FileNotFoundException("Could not locate " + relativePath);
        }
    }
}
