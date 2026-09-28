using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class AsyncPersistenceBarrierTests
    {
        [TestMethod]
        public async Task CancelledWaitThenEdit_KeepsOldBarrierBeforeNewSnapshot()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var written = new ConcurrentQueue<string>();
            var feature = new StickyFeature("unused.dat", request =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                written.Enqueue(request.Snapshot[0].Text);
                return PersistenceResult.Success();
            });
            StickyNoteData note = feature.CreateDraft("exit snapshot", Point.Empty);
            Task<PersistenceResult> first = feature.SaveBarrierAsync();
            Task<PersistenceResult> second = null;
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
                Assert.IsFalse(first.IsCompleted, "The owner got a receipt without blocking on disk.");
                // Returning to editing abandons only this wait, never the old write.
                note.Text = "edited after cancelling exit";
                second = feature.SaveBarrierAsync();
                Assert.IsFalse(second.IsCompleted);
            }
            finally { release.Set(); }
            Assert.IsTrue((await first).Succeeded);
            Assert.IsTrue((await second).Succeeded);
            CollectionAssert.AreEqual(new[] { "exit snapshot", "edited after cancelling exit" }, written.ToArray());
        }

        [TestMethod]
        public async Task Replacement_WaitsBehindOldWriteAndPublishesOnlyAfterReceipt()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var written = new ConcurrentQueue<StickyWriteRequest>();
            var feature = new StickyFeature("unused.dat", request =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                written.Enqueue(request);
                return PersistenceResult.Success();
            });
            feature.CreateDraft("old", Point.Empty);
            Task<PersistenceResult> old = feature.SaveBarrierAsync();
            Task<PersistenceResult> replacement = null;
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
                replacement = feature.CommitFullRestoreAsync(new[] { new StickyNoteData { Text = "restored" } });
                Assert.IsFalse(replacement.IsCompleted);
                Assert.AreEqual("old", feature.GetAll()[0].Text);
                feature.SaveAsync(); // A retry cannot enqueue the frozen old model after the import.
                Assert.IsFalse((await feature.SaveBarrierAsync()).Succeeded);
            }
            finally { release.Set(); }
            Assert.IsTrue((await old).Succeeded);
            Assert.IsTrue((await replacement).Succeeded);
            Assert.AreEqual("restored", feature.GetAll()[0].Text);
            StickyWriteRequest[] requests = written.ToArray();
            Assert.AreEqual(2, requests.Length);
            Assert.AreEqual("old", requests[1].BackupSnapshot[0].Text);
            Assert.AreEqual("restored", requests[1].Snapshot[0].Text);
        }

        [TestMethod]
        public async Task FailedReplacement_PreservesModelAndAllowsSubsequentSave()
        {
            bool fail = true;
            var feature = new StickyFeature("unused.dat", request => fail
                ? PersistenceResult.Failure(new IOException("disk unavailable"))
                : PersistenceResult.Success());
            StickyNoteData original = feature.CreateDraft("last edit", Point.Empty);
            Assert.IsFalse((await feature.CommitFullRestoreAsync(
                new[] { new StickyNoteData { Text = "replacement" } })).Succeeded);
            Assert.AreSame(original, feature.Find(original.Id));
            original.Text = "continued editing";
            fail = false;
            Assert.IsTrue((await feature.SaveBarrierAsync()).Succeeded);
        }
    }
}
