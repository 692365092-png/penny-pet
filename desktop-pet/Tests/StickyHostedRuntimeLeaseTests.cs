using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyHostedRuntimeLeaseTests
    {
        [TestMethod]
        public void CancelBeforeNativeClosePreservesLiveSessionsAndImeBarrier()
        {
            var runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RecordSequence("n", 50);
            runtime.SetImeComposition("n", true);
            runtime.SetInputFocus("n", true);
            runtime.RequestExit();
            Assert.IsFalse(runtime.TryBeginCloseAll());
            runtime.CancelExit();

            Assert.IsFalse(runtime.ExitRequested);
            Assert.IsFalse(runtime.ExitPrepared);
            Assert.IsTrue(runtime.ContainsNote("n"));
            Assert.IsTrue(runtime.HasImeComposition);
            Assert.IsTrue(runtime.HasInputFocus);
            Assert.IsFalse(runtime.CanApplySequence("n", 50));
            Assert.IsTrue(runtime.CanApplySequence("n", 51));
        }

        [TestMethod]
        public void CompleteCloseAllRetiresEverySessionBeforeReopening()
        {
            var runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RecordSequence("n", 50);
            runtime.AddNote("unavailable");
            runtime.SetInputFocus("n", true);
            Assert.IsTrue(runtime.TryBeginDelete("n"));
            runtime.RequestExit();
            Assert.IsTrue(runtime.TryBeginCloseAll());
            Assert.IsFalse(runtime.TryBeginCloseAll());
            runtime.CompleteCloseAll();

            Assert.AreEqual(0, runtime.NoteCount);
            Assert.IsFalse(runtime.CloseAllInFlight);
            Assert.IsFalse(runtime.HasInputFocus);
            Assert.IsFalse(runtime.CanApplySequence("n", 51));
            Assert.IsTrue(runtime.AddNote("n"));
            Assert.IsTrue(runtime.CanApplySequence("n", 1));
            Assert.IsTrue(runtime.TryBeginDelete("n"), "The previous session's pending delete has ended.");
        }

        [TestMethod]
        public void CancelPreparedExitAllowsAnotherCompleteExitAttempt()
        {
            var runtime = new StickyHostedRuntime();
            runtime.AddNote("n");
            runtime.RequestExit();
            Assert.IsTrue(runtime.TryBeginCloseAll());
            runtime.CompleteCloseAll();
            runtime.PrepareExit();
            Assert.IsTrue(runtime.ExitPrepared);
            runtime.CancelExit();

            Assert.IsFalse(runtime.ExitRequested);
            Assert.IsFalse(runtime.ExitPrepared);
            Assert.IsFalse(runtime.TryBeginCloseAll());
            runtime.AddNote("n");
            runtime.RequestExit();
            Assert.IsTrue(runtime.TryBeginCloseAll());
            runtime.CompleteCloseAll();
            runtime.PrepareExit();
            Assert.IsTrue(runtime.ExitPrepared);
        }

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
