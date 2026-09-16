using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // Source guard only; does not prove Windows dialog/runtime behavior.
    [TestClass]
    [TestCategory("ArchitectureSourceBoundary")]
    public sealed class StickyManagerDeleteContractTests
    {
        [TestMethod]
        public void DeleteWaitsForEveryExplicitCompletionBeforeRefresh()
        {
            string source = ReadSource("Features/StickyNotes/StickyNotes.cs");
            Assert.IsTrue(source.Contains("Action<StickyNoteData, Action<bool>> DeleteNote"));
            string method = SliceMethod(source, "private void DeleteSelectedNotes()");
            Assert.IsTrue(method.Contains("ManagerMode.Busy"));
            Assert.IsTrue(method.Contains("int pending = selected.Count"));
            Assert.IsTrue(method.Contains("delegate(bool succeeded)"));
            Assert.IsTrue(method.Contains("pending--;"));
            int gate = method.IndexOf("if (pending != 0) return;", StringComparison.Ordinal);
            Assert.IsTrue(gate > 0);
            Assert.IsTrue(method.IndexOf("RefreshList();", StringComparison.Ordinal) > gate);
            Assert.IsFalse(method.Contains("Thread.Sleep"));
            Assert.IsFalse(method.Contains("Timer"));
        }
    }
}
