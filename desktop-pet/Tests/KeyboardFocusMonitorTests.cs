using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class KeyboardFocusMonitorTests
    {
        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void PartialHookFailureIsUnavailableCleansUpAndCanRetry(int failAt)
        {
            int registrations = 0;
            var released = new List<IntPtr>();
            bool fail = true;
            using (var monitor = new KeyboardFocusMonitor(() => { }, eventId =>
            {
                int index = registrations++;
                return fail && index == failAt ? IntPtr.Zero : new IntPtr(index + 1);
            }, hook => released.Add(hook)))
            {
                Assert.IsFalse(monitor.Start());
                Assert.IsFalse(monitor.IsActive);
                Assert.IsFalse(KeyboardFocusMonitor.IsRunning);
                Assert.AreEqual(failAt, released.Count);
                fail = false;
                Assert.IsTrue(monitor.Start());
                Assert.IsTrue(monitor.IsActive);
                Assert.IsTrue(KeyboardFocusMonitor.IsRunning);
                int before = registrations;
                Assert.IsTrue(monitor.Start());
                Assert.AreEqual(before, registrations);
            }
            Assert.AreEqual(failAt + 3, released.Count);
            Assert.IsFalse(KeyboardFocusMonitor.IsRunning);
        }
    }
}
