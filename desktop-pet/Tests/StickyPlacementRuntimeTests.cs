using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyPlacementRuntimeTests
    {
        private static WindowFacts Facts(long generation, long sequence)
        {
            return new WindowFacts("n-1", "target-key", "\\\\.\\DISPLAY1",
                new PhysicalRect(100, 100, 300, 230), 96, generation, sequence);
        }

        [TestMethod]
        public void CanAcceptEffective_RejectsNonIncreasingSameGeneration()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 10)));
            Assert.IsFalse(runtime.CanAcceptEffective("n-1", Facts(1, 9)));
            Assert.IsFalse(runtime.CanAcceptEffective("n-1", Facts(1, 10)));
            Assert.IsTrue(runtime.CanAcceptEffective("n-1", Facts(1, 11)));
        }

        [TestMethod]
        public void CanAcceptEffective_AcceptsHigherGeneration()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 10)));
            Assert.IsTrue(runtime.CanAcceptEffective("n-1", Facts(2, 1)));
        }

        [TestMethod]
        public void InvalidateEffective_PreservesTemporaryRehome()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 30)));
            runtime.MarkTemporaryRehome("n-1", "display-missing");
            Assert.IsTrue(runtime.InvalidateEffective("n-1"));
            Assert.IsNull(runtime.GetEffective("n-1"));
            Assert.IsTrue(runtime.IsTemporaryRehome("n-1"));
            Assert.AreEqual("display-missing", runtime.TemporaryReason("n-1"));
            Assert.IsFalse(runtime.UserMovedSinceRehome("n-1"));
            Assert.IsTrue(runtime.CanAcceptEffective("n-1", Facts(1, 1)));
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 1)));
        }

        [TestMethod]
        public void InvalidateEffective_AllowsLowerSequenceAfterSessionBoundary()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 30)));
            Assert.IsFalse(runtime.CanAcceptEffective("n-1", Facts(1, 1)));
            Assert.IsTrue(runtime.InvalidateEffective("n-1"));
            Assert.IsTrue(runtime.CanAcceptEffective("n-1", Facts(1, 1)));
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(1, 1)));
            Assert.IsFalse(runtime.CanAcceptEffective("n-1", Facts(1, 1)));
        }

        [TestMethod]
        public void TryUpdateEffective_UsesSameAcceptanceRule()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(2, 5)));
            Assert.IsFalse(runtime.TryUpdateEffective("n-1", Facts(2, 4)));
            Assert.AreEqual(5, runtime.GetEffective("n-1").WindowSequence);
            Assert.IsTrue(runtime.TryUpdateEffective("n-1", Facts(3, 1)));
            Assert.AreEqual(3, runtime.GetEffective("n-1").TopologyGeneration);
        }

        [TestMethod]
        public void CanAcceptEffective_RejectsNullOrEmpty()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            Assert.IsFalse(runtime.CanAcceptEffective(null, Facts(1, 1)));
            Assert.IsFalse(runtime.CanAcceptEffective(String.Empty, Facts(1, 1)));
            Assert.IsFalse(runtime.CanAcceptEffective("n-1", null));
            Assert.IsFalse(runtime.TryUpdateEffective("n-1", null));
        }
    }
}
