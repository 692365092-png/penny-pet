using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class PetPersistenceRuntimeTests
    {
        [TestMethod]
        public void RepeatedFailuresArmOneRetryAndOneWarningPerEpisode()
        {
            var notes = new FakeTarget();
            var settings = new FakeTarget();
            var scheduler = new FakeScheduler();
            var notices = new List<PersistenceNoticeKind>();
            using (var runtime = new PetPersistenceRuntime(notes, settings,
                new InlineContext(), scheduler, TimeSpan.FromSeconds(5)))
            {
                runtime.Notice += (sender, e) => notices.Add(e.Kind);

                notes.Fail();
                notes.Fail();
                Assert.AreEqual(1, scheduler.ScheduleCount);
                CollectionAssert.AreEqual(
                    new[] { PersistenceNoticeKind.Warning }, notices);

                scheduler.Fire();
                Assert.AreEqual(1, notes.SaveRequests);
                Assert.AreEqual(2, scheduler.ScheduleCount);

                notes.CompleteSuccess();
                scheduler.Fire();
                CollectionAssert.AreEqual(new[]
                {
                    PersistenceNoticeKind.Warning,
                    PersistenceNoticeKind.Recovered
                }, notices);
                Assert.IsFalse(scheduler.IsScheduled);

                notes.Fail();
                Assert.AreEqual(3, scheduler.ScheduleCount);
                CollectionAssert.AreEqual(new[]
                {
                    PersistenceNoticeKind.Warning,
                    PersistenceNoticeKind.Recovered,
                    PersistenceNoticeKind.Warning
                }, notices);
            }
        }

        [TestMethod]
        public void PendingWriterIsNeverDuplicatedAndRetryUsesOwnerTarget()
        {
            var notes = new FakeTarget();
            var settings = new FakeTarget();
            var scheduler = new FakeScheduler();
            using (var runtime = new PetPersistenceRuntime(notes, settings,
                new InlineContext(), scheduler, TimeSpan.FromSeconds(5)))
            {
                notes.Fail();
                notes.HasPending = true;
                scheduler.Fire();
                Assert.AreEqual(0, notes.SaveRequests);
                Assert.IsTrue(scheduler.IsScheduled);

                notes.HasPending = false;
                scheduler.Fire();
                Assert.AreEqual(1, notes.SaveRequests);
                Assert.IsTrue(notes.HasPending);
            }
        }

        [TestMethod]
        public void ExistingDirtyStateRetriesWithoutInventingAWarning()
        {
            var notes = new FakeTarget { Dirty = true };
            var settings = new FakeTarget();
            var scheduler = new FakeScheduler();
            var notices = new List<PersistenceNoticeKind>();
            using (var runtime = new PetPersistenceRuntime(notes, settings,
                new InlineContext(), scheduler, TimeSpan.FromSeconds(5)))
            {
                runtime.Notice += (sender, e) => notices.Add(e.Kind);
                Assert.IsTrue(scheduler.IsScheduled);
                scheduler.Fire();
                Assert.AreEqual(1, notes.SaveRequests);
                notes.CompleteSuccess();
                scheduler.Fire();
                Assert.AreEqual(0, notices.Count);
            }
        }

        private sealed class FakeTarget : IPersistenceRetryTarget
        {
            internal bool Dirty;
            internal bool HasPending;
            internal int SaveRequests;

            public event EventHandler<PersistenceFailedEventArgs> SaveFailed;
            public bool HasUnsavedChanges { get { return Dirty; } }
            public bool HasPendingSaves { get { return HasPending; } }

            public void RequestAutosave()
            {
                SaveRequests++;
                HasPending = true;
            }

            internal void Fail()
            {
                Dirty = true;
                HasPending = false;
                SaveFailed?.Invoke(this, new PersistenceFailedEventArgs(
                    PersistenceResult.Failure(new IOException("busy")), 1));
            }

            internal void CompleteSuccess()
            {
                Dirty = false;
                HasPending = false;
            }
        }

        private sealed class FakeScheduler : IPersistenceRetryScheduler
        {
            private Action _due;
            internal int ScheduleCount;
            internal bool IsScheduled { get { return _due != null; } }

            public void Schedule(TimeSpan delay, Action due)
            {
                ScheduleCount++;
                _due = due;
            }

            public void Cancel() { _due = null; }

            internal void Fire()
            {
                Action due = _due;
                _due = null;
                due?.Invoke();
            }

            public void Dispose() { _due = null; }
        }

        private sealed class InlineContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
                d(state);
            }
        }
    }
}
