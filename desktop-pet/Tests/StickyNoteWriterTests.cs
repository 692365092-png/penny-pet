using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyNoteWriterTests
    {
        private static StickyWriteRequest Request(string text, bool workspace = true)
        {
            return new StickyWriteRequest("notes.dat",
                new List<StickyNoteData> { new StickyNoteData { Text = text } },
                updatesWorkspace: workspace);
        }

        [TestMethod]
        public void AutosavesCoalesceWithoutCrossingExplicitBarriers()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var writes = new ConcurrentQueue<string>();
                int active = 0;
                var writer = new StickyNoteWriter(request =>
                {
                    Assert.AreEqual(1, Interlocked.Increment(ref active));
                    string text = request.Snapshot[0].Text;
                    if (text == "active")
                    {
                        entered.Set();
                        Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    }
                    writes.Enqueue(text);
                    Interlocked.Decrement(ref active);
                    return PersistenceResult.Success();
                });
                Task<PersistenceResult> first = writer.Enqueue(Request("active"), true);
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                try
                {
                    var obsolete = writer.Enqueue(Request("obsolete"), true);
                    var latest = writer.Enqueue(Request("latest"), true);
                    Assert.AreSame(obsolete, latest);
                    writer.Enqueue(Request("save"));
                    writer.Enqueue(Request("export", false));
                    writer.Enqueue(Request("after"), true);
                    Assert.IsTrue(writer.IsDirty);
                    Assert.IsTrue(writer.HasPending);
                }
                finally { release.Set(); }
                Assert.IsTrue(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
                Assert.IsTrue(first.Result.Succeeded);
                CollectionAssert.AreEqual(new[] { "active", "latest", "save", "export", "after" },
                    writes.ToArray());
                Assert.IsFalse(writer.IsDirty);
            }
        }

        [TestMethod]
        public void FailedWriteDoesNotDiscardTheNewerQueuedSnapshot()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int failures = 0;
                var writer = new StickyNoteWriter(request =>
                {
                    if (request.Snapshot[0].Text == "fail")
                    {
                        entered.Set();
                        Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                        return PersistenceResult.Failure(new IOException("busy"));
                    }
                    return PersistenceResult.Success();
                });
                writer.Failed += (sender, args) => Interlocked.Increment(ref failures);
                var failed = writer.Enqueue(Request("fail"), true);
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                Task<PersistenceResult> saved;
                try { saved = writer.Enqueue(Request("newer"), true); }
                finally { release.Set(); }
                Assert.IsTrue(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
                Assert.IsFalse(failed.Result.Succeeded);
                Assert.IsTrue(saved.Result.Succeeded);
                Assert.AreEqual(1, failures);
                Assert.IsFalse(writer.IsDirty);
                Assert.IsNull(writer.LastError);
            }
        }

        [TestMethod]
        public void ExportCannotClearAFailedWorkspaceSave()
        {
            var failure = new IOException("disk full");
            var writer = new StickyNoteWriter(request => request.UpdatesWorkspace
                ? PersistenceResult.Failure(failure) : PersistenceResult.Success());
            Assert.IsFalse(writer.Enqueue(Request("workspace")).Result.Succeeded);
            Assert.IsTrue(writer.Enqueue(Request("export", false)).Result.Succeeded);
            Assert.IsFalse(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
            Assert.IsTrue(writer.IsDirty);
            Assert.AreSame(failure, writer.LastError);
        }

        [TestMethod]
        public void RetryClearsFailureOnlyAfterItsWriteSucceeds()
        {
            int count = 0;
            var writer = new StickyNoteWriter(request => ++count == 1
                ? PersistenceResult.Failure(new IOException("busy"))
                : PersistenceResult.Success());
            Assert.IsFalse(writer.Enqueue(Request("first")).Result.Succeeded);
            Assert.IsTrue(writer.IsDirty);
            Assert.IsTrue(writer.Enqueue(Request("retry")).Result.Succeeded);
            Assert.IsTrue(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
            Assert.IsFalse(writer.IsDirty);
        }

        [TestMethod]
        public void TimeoutDoesNotCancelTheQueuedSave()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var writer = new StickyNoteWriter(request =>
                {
                    entered.Set();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    return PersistenceResult.Success();
                });
                var saved = writer.Enqueue(Request("save"));
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                try
                {
                    var result = writer.Flush(TimeSpan.Zero);
                    Assert.IsInstanceOfType<TimeoutException>(result.Error);
                    Assert.IsFalse(saved.IsCompleted);
                    Assert.IsTrue(writer.IsDirty);
                }
                finally { release.Set(); }
                Assert.IsTrue(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
                Assert.IsTrue(saved.Result.Succeeded);
            }
        }

        [TestMethod]
        public void ObserverExceptionCannotStrandTheQueue()
        {
            var writer = new StickyNoteWriter(request => request.Snapshot[0].Text == "fail"
                ? PersistenceResult.Failure(new IOException()) : PersistenceResult.Success());
            writer.Failed += (sender, args) => throw new InvalidOperationException("observer");
            writer.Enqueue(Request("fail"));
            var next = writer.Enqueue(Request("next"));
            Assert.IsTrue(writer.Flush(TimeSpan.FromSeconds(5)).Succeeded);
            Assert.IsTrue(next.Result.Succeeded);
        }
    }
}
