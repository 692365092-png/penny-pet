using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class WindowFactsVersionRulesTests
    {
        private static DisplayTopologySnapshot Topology(long generation)
        {
            return new DisplayTopologySnapshot(generation,
                new[] { new DisplaySurfaceSnapshot("surface-1", "\\\\.\\DISPLAY1",
                    new PhysicalRect(0, 0, 1920, 1080),
                    new PhysicalRect(0, 0, 1920, 1040), true, 0,
                    new[] { new DisplayTargetIdentity("mdp:test", true,
                        "path", "display", 0, 0, 0) }) });
        }
        private static WindowFacts Facts(long generation, long sequence)
        { return new WindowFacts("note-a", "mdp:test", "\\\\.\\DISPLAY1", new PhysicalRect(100, 120, 320, 300), 96, generation, sequence); }

        [TestMethod]
        public void ExactGenerationPair_IsCurrent()
        { Assert.AreEqual(WindowFactsVersionDisposition.Current, WindowFactsVersionRules.Classify("note-a", 55, Facts(8, 55), Topology(8), 8)); }
        [TestMethod]
        public void InternallyConsistentOldPair_IsStillStale()
        { Assert.AreEqual(WindowFactsVersionDisposition.StaleTopology, WindowFactsVersionRules.Classify("note-a", 999, Facts(8, 999), Topology(8), 9)); }
        [TestMethod]
        public void MismatchedGenerationOrSequence_IsInvalid()
        {
            Assert.AreEqual(WindowFactsVersionDisposition.Invalid, WindowFactsVersionRules.Classify("note-a", 50, Facts(8, 50), Topology(9), 9));
            Assert.AreEqual(WindowFactsVersionDisposition.Invalid, WindowFactsVersionRules.Classify("note-a", 51, Facts(9, 50), Topology(9), 9));
        }
        [TestMethod]
        public void UnresolvableSurface_IsInvalid()
        {
            WindowFacts facts = new WindowFacts("note-a", "mdp:missing", "\\\\.\\DISPLAY99", new PhysicalRect(1,1,1,1), 96, 9, 50);
            Assert.AreEqual(WindowFactsVersionDisposition.Invalid, WindowFactsVersionRules.Classify("note-a", 50, facts, Topology(9), 9));
        }
        [TestMethod]
        public void DockSession_PreparingAndEpochInvalidateOldCallbacks()
        {
            DockInteractionSession session = new DockInteractionSession();
            long first = session.BeginPreparing("note-a", 5);
            Assert.IsFalse(session.CanPlan("note-a", 5));
            Assert.IsTrue(session.TryEnterDragging(first, 5));
            Assert.IsTrue(session.CanPlan("note-a", 5));
            long rebase = session.BeginRebase(6);
            Assert.AreNotEqual(first, rebase);
            Assert.IsFalse(session.Matches(first, 5, DockInteractionPhase.Dragging));
            Assert.IsTrue(session.TryEnterDragging(rebase, 6));
            long reset = session.Reset();
            Assert.IsFalse(session.Matches(rebase, 6, DockInteractionPhase.Dragging));
            Assert.AreNotEqual(rebase, reset);
        }
    }
}
