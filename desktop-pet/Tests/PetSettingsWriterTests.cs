using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class PetSettingsWriterTests
    {
        [TestMethod]
        public void AutosaveCapturesSettingsBeforeTheWorkerAndCoalescesPendingChanges()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var written = new ConcurrentQueue<int>();
                var settings = new PetSettings(request =>
                {
                    entered.Set();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    written.Enqueue(PetSettingsCodec.Parse(request.Lines).ScalePercent);
                    return PersistenceResult.Success();
                });
                settings.ScalePercent = 100;
                settings.SaveAsync();
                try
                {
                    Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                    settings.ScalePercent = 125;
                    settings.SaveAsync();
                    settings.ScalePercent = 150;
                    settings.SaveAsync();
                    settings.ScalePercent = 200; // No save: must not leak into the captured request.
                    Assert.IsTrue(settings.HasUnsavedChanges);
                }
                finally { release.Set(); }
                Assert.IsTrue(settings.WaitForPendingSaves().Succeeded);
                CollectionAssert.AreEqual(new[] { 100, 150 }, written.ToArray());
                Assert.IsFalse(settings.HasUnsavedChanges);
            }
        }

        [TestMethod]
        public void FailureStaysDirtyUntilTheRetryActuallyWritesTheNewestSettings()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var failure = new IOException("busy");
                int attempts = 0;
                int savedScale = 0;
                var settings = new PetSettings(request =>
                {
                    if (++attempts == 1) return PersistenceResult.Failure(failure);
                    entered.Set();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    savedScale = PetSettingsCodec.Parse(request.Lines).ScalePercent;
                    return PersistenceResult.Success();
                });
                settings.SaveAsync();
                Assert.IsFalse(settings.WaitForPendingSaves().Succeeded);
                Assert.IsTrue(settings.HasUnsavedChanges);
                Assert.AreSame(failure, settings.LastSaveError);
                settings.ScalePercent = 180;
                settings.SaveAsync();
                try
                {
                    Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                    Assert.IsTrue(settings.HasPendingSaves);
                    Assert.IsTrue(settings.HasUnsavedChanges);
                    Assert.IsInstanceOfType<TimeoutException>(
                        settings.WaitForPendingSaves(TimeSpan.Zero).Error);
                }
                finally { release.Set(); }
                Assert.IsTrue(settings.WaitForPendingSaves().Succeeded);
                Assert.AreEqual(180, savedScale);
                Assert.IsFalse(settings.HasUnsavedChanges);
                Assert.IsNull(settings.LastSaveError);
            }
        }

        [TestMethod]
        public void FailureNotificationReturnsToTheCapturedModelContext()
        {
            var context = new QueuedContext();
            SynchronizationContext previous = SynchronizationContext.Current;
            PetSettings settings;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                settings = new PetSettings(request => PersistenceResult.Failure(new IOException("full")));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            int notifications = 0;
            settings.SaveFailed += (sender, error) =>
            {
                Assert.AreSame(settings, sender);
                Assert.AreEqual(1, error.ConsecutiveFailures);
                notifications++;
            };
            settings.SaveAsync();
            Assert.IsFalse(settings.WaitForPendingSaves().Succeeded);
            Assert.AreEqual(0, notifications);
            context.Drain();
            Assert.AreEqual(1, notifications);
        }

        [TestMethod]
        public void AStalledNotesWriterDoesNotHoldSettingsWrites()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var notes = new PersistenceWriter<StickyWriteRequest>(request =>
                {
                    entered.Set();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    return PersistenceResult.Success();
                });
                notes.Enqueue(new StickyWriteRequest(new StickyNoteData[0]));
                try
                {
                    Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                    var settings = new PetSettings(request => PersistenceResult.Success());
                    settings.SaveAsync();
                    Assert.IsTrue(settings.WaitForPendingSaves(TimeSpan.FromSeconds(2)).Succeeded);
                    Assert.IsFalse(settings.HasUnsavedChanges);
                    Assert.IsTrue(notes.HasPending);
                }
                finally
                {
                    release.Set();
                    Assert.IsTrue(notes.Flush(TimeSpan.FromSeconds(5)).Succeeded);
                }
            }
        }

        [TestMethod]
        public void QueuedSettingsWritePreservesTheUnreadablePrimaryBeforeReplacingIt()
        {
            string directory = Path.Combine(Path.GetTempPath(), "penny-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string primary = Path.Combine(directory, "settings.ini");
            try
            {
                string unreadable = new string('x', 1024 * 1024 + 1);
                File.WriteAllText(primary, unreadable);
                File.WriteAllLines(primary + ".bak", PetSettingsCodec.Serialize(new PetSettingsData { ScalePercent = 150 }));
                PetSettings settings = PetSettings.LoadFromFile(primary);
                Assert.AreEqual(150, settings.ScalePercent);
                Assert.IsTrue(settings.SaveToFile(primary).Succeeded);
                string[] preserved = Directory.GetFiles(directory, "settings.ini.corrupt-*");
                Assert.AreEqual(1, preserved.Length);
                Assert.AreEqual(unreadable, File.ReadAllText(preserved[0]));
                Assert.AreEqual(150, PetSettings.LoadFromFile(primary).ScalePercent);
                Assert.IsFalse(settings.HasUnsavedChanges);
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Action> _posted = new ConcurrentQueue<Action>();
            public override void Post(SendOrPostCallback callback, object state)
            { _posted.Enqueue(() => callback(state)); }
            internal void Drain()
            {
                Action next;
                while (_posted.TryDequeue(out next)) next();
            }
        }
    }
}
