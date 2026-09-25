using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class KeyboardPrivacyWorkerTests
    {
        private static KeyboardFocusSnapshot Focus(bool native = true, long version = 1,
            int hwnd = 40, int[] runtimeId = null, long time = 100)
        { return new KeyboardFocusSnapshot(new IntPtr(10), 20, 30, new IntPtr(hwnd), runtimeId, native, version, time); }
        private static KeyboardInputEventArgs Key(string text, KeyboardFocusSnapshot focus = null)
        { return new KeyboardInputEventArgs(65, text, 3, focus ?? Focus()); }

        private sealed class Scene : IDisposable
        {
            internal readonly ConcurrentQueue<Action> Ui = new ConcurrentQueue<Action>();
            internal readonly ConcurrentQueue<string> Shown = new ConcurrentQueue<string>();
            internal readonly ManualResetEventSlim Posted = new ManualResetEventSlim();
            internal readonly KeyboardPrivacyWorker Worker;
            internal long Now = 100;
            internal KeyboardFocusSnapshot Current = Focus();
            internal int Checks, Posts;
            internal ApartmentState Apartment;
            internal Scene(Func<KeyboardFocusSnapshot, bool> inspect = null)
            {
                Worker = new KeyboardPrivacyWorker(focus =>
                {
                    Interlocked.Increment(ref Checks);
                    Apartment = Thread.CurrentThread.GetApartmentState();
                    return inspect == null || inspect(focus);
                }, focus => KeyboardFocusSnapshot.IsSameNativeInput(focus, Current), action =>
                {
                    Interlocked.Increment(ref Posts);
                    Ui.Enqueue(action); Posted.Set();
                }, input => Shown.Enqueue(input.DisplayText), () => Interlocked.Read(ref Now), 750);
                Worker.SetEnabled(true);
            }
            internal void AwaitPost() { Assert.IsTrue(Posted.Wait(3000), "worker did not post"); }
            internal void Pump() { while (Ui.TryDequeue(out Action action)) action(); }
            public void Dispose() { Worker.Dispose(); } // no join, even for a stuck provider
        }

        [TestMethod]
        public void NativeProof_IsRequiredEvenWhenSameHostAndLaterRuntimeIdMatches()
        {
            Assert.IsFalse(KeyboardFocusSnapshot.IsSameNativeInput(Focus(false), Focus(false)));
            Assert.IsFalse(KeyboardFocusSnapshot.IsSameTarget(Focus(runtimeId: null), Focus(runtimeId: new[] { 1 })));
            Assert.IsFalse(KeyboardFocusSnapshot.IsSameTarget(Focus(runtimeId: new[] { 1 }), Focus(runtimeId: new[] { 2 })));
            Assert.IsTrue(KeyboardFocusSnapshot.IsSameNativeInput(Focus(), Focus()));
            Assert.IsFalse(KeyboardFocusSnapshot.IsSameNativeInput(Focus(), Focus(version: 2)));
            Assert.IsFalse(KeyboardFocusSnapshot.IsSameNativeInput(Focus(), Focus(hwnd: 41)));
            Assert.IsFalse(KeyboardFocusSnapshot.IsNativeEditClass("Chrome_RenderWidgetHostHWND"));
            Assert.IsFalse(KeyboardFocusSnapshot.IsNativeEditClass("HwndWrapper[WPF]"));
            Assert.IsTrue(KeyboardFocusSnapshot.IsNativeEditClass("Edit"));
        }

        [TestMethod]
        [DataRow(null, true, true, true, false, false)]
        [DataRow(true, true, true, true, false, false)]
        [DataRow(false, false, true, true, false, false)]
        [DataRow(false, true, false, true, false, false)]
        [DataRow(false, true, true, false, false, false)]
        [DataRow(false, true, true, true, true, false)]
        [DataRow(false, true, true, true, false, true)]
        public void UnsupportedPasswordOrUnboundIdentity_CannotBorrowNativeInspection(
            bool? password, bool eventKnown, bool currentMatches, bool automationMatches, bool credential, bool expected)
        {
            Assert.AreEqual(expected, PetKeyboardPrivacyPolicy.CanPublishNativeInput(
                eventKnown, currentMatches, password, automationMatches, credential));
        }

        [TestMethod]
        public void SafeInput_RunsOnMtaAndPublishesOnlyWhenUiDispatches()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("A")); s.AwaitPost();
                Assert.AreEqual(ApartmentState.MTA, s.Apartment);
                Assert.IsTrue(s.Shown.IsEmpty);
                s.Pump();
                CollectionAssert.AreEqual(new[] { "A" }, s.Shown.ToArray());
            }
        }

        [TestMethod]
        public void SameHostVirtualPasswordSwitch_NeverStartsInspectionForUnknownEvent()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("secret", Focus(false)));
                // A later safe native key provides an explicit completion boundary.
                s.Worker.Offer(Key("safe")); s.AwaitPost(); s.Pump();
                Assert.AreEqual(1, s.Checks);
                CollectionAssert.AreEqual(new[] { "safe" }, s.Shown.ToArray());
            }
        }

        [TestMethod]
        public void FocusChangesWhileResultQueued_IsRecheckedBeforePublication()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("A")); s.AwaitPost();
                s.Current = Focus(version: 2);
                s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
            }
        }

        [TestMethod]
        public void FocusLeavesAndReturnsToSameHwnd_InvalidatesQueuedResult()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("A")); s.AwaitPost();
                s.Worker.Invalidate(); // focus event; target may look identical again
                s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
            }
        }

        [TestMethod]
        public void DisableThenReenable_CannotReviveOldUiDelivery()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("A")); s.AwaitPost();
                s.Worker.SetEnabled(false); s.Worker.SetEnabled(true);
                s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
            }
        }

        [TestMethod]
        public void Dispose_DropsAlreadyQueuedOutput()
        {
            using (var s = new Scene())
            {
                s.Worker.Offer(Key("A")); s.AwaitPost();
                s.Worker.Dispose(); s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
            }
        }

        [TestMethod]
        public void ExpiredAtHookOrUiDelivery_IsDropped()
        {
            using (var s = new Scene())
            {
                s.Now = 1000;
                s.Worker.Offer(Key("old"));
                s.Worker.Offer(Key("fresh", Focus(time: 1000))); s.AwaitPost();
                Assert.AreEqual(1, s.Checks);
                s.Now = 1751; s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
            }
        }

        [TestMethod]
        public void HungProvider_HasOneWorkerAndOneLatestPendingSlot()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var s = new Scene(focus => { entered.Set(); release.Wait(); return true; }))
            {
                try
                {
                    s.Worker.Offer(Key("first")); Assert.IsTrue(entered.Wait(3000));
                    for (int i = 0; i < 1000; i++) s.Worker.Offer(Key("key" + i));
                    Assert.AreEqual(1, s.Checks);
                    Assert.IsTrue(s.Ui.IsEmpty);
                    release.Set(); s.AwaitPost(); s.Pump();
                    Assert.AreEqual(2, s.Checks);
                    CollectionAssert.AreEqual(new[] { "key999" }, s.Shown.ToArray());
                }
                finally { release.Set(); }
            }
        }

        [TestMethod]
        public void ExpirationDuringBlockedProvider_DisposeDoesNotWaitOrPublish()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            using (var s = new Scene(focus => { entered.Set(); release.Wait(); returned.Set(); return true; }))
            {
                try
                {
                    s.Worker.Offer(Key("A")); Assert.IsTrue(entered.Wait(3000));
                    s.Now = 1000; s.Worker.Dispose();
                    Assert.IsFalse(returned.IsSet, "disposal must complete while provider remains blocked");
                    release.Set(); Assert.IsTrue(returned.Wait(3000));
                    s.Pump(); Assert.IsTrue(s.Shown.IsEmpty);
                }
                finally { release.Set(); }
            }
        }

        [TestMethod]
        public void ProviderFailure_DoesNotAuthorizeTheNextInput()
        {
            int calls = 0;
            using (var s = new Scene(focus =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("provider unavailable");
                return true;
            }))
            {
                s.Worker.Offer(Key("first"));
                Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, 3000));
                s.Worker.Offer(Key("second")); s.AwaitPost(); s.Pump();
                CollectionAssert.AreEqual(new[] { "second" }, s.Shown.ToArray());
            }
        }
    }
}
