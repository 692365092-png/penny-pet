using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyHostedRuntimeLeaseTests
    {
        [TestMethod]
        public void SynchronizeSessionLease_AllowsNewSessionSequenceBaseline()
        {
            StickyHostedRuntime runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RecordSequence("n", 50);
            runtime.SynchronizeSessionLease("n", 2);
            Assert.IsFalse(runtime.CanApplySequence("n", 2));
            Assert.IsTrue(runtime.CanApplySequence("n", 3));
        }

        [TestMethod]
        public void SynchronizeSessionLease_PreservesImeAndFocus()
        {
            StickyHostedRuntime runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.SetImeComposition("n", true);
            runtime.SetInputFocus("n", true);
            runtime.SynchronizeSessionLease("n", 0);
            Assert.IsTrue(runtime.HasImeComposition);
            Assert.IsTrue(runtime.HasInputFocus);
        }

        [TestMethod]
        public void SynchronizeSessionLease_PreservesDeletePending()
        {
            StickyHostedRuntime runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            Assert.IsTrue(runtime.TryBeginDelete("n"));
            runtime.SynchronizeSessionLease("n", 0);
            Assert.IsFalse(runtime.TryBeginDelete("n"));
        }

        [TestMethod]
        public void SynchronizeSessionLease_AddsOnlyAcknowledgedMember()
        {
            StickyHostedRuntime runtime = new StickyHostedRuntime();
            runtime.AddNote("other");
            runtime.RecordSequence("other", 70);
            runtime.SynchronizeSessionLease("N", -1);
            Assert.IsTrue(runtime.ContainsNote("n"));
            Assert.IsFalse(runtime.CanApplySequence("n", 0));
            Assert.IsTrue(runtime.CanApplySequence("n", 1));
            Assert.IsFalse(runtime.CanApplySequence("other", 70));
            Assert.IsTrue(runtime.CanApplySequence("other", 71));
        }
    }
}
