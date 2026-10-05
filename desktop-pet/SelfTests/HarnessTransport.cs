using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private static PersistenceResult WaitForPersistenceReceipt(Task<PersistenceResult> receipt)
        {
            Stopwatch deadline = Stopwatch.StartNew();
            while (!receipt.IsCompleted)
            {
                Application.DoEvents();
                if (deadline.Elapsed > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("Self-test persistence receipt did not complete.");
                Thread.Sleep(1);
            }
            return receipt.GetAwaiter().GetResult();
        }

        private static StickyUiCommandResult PostStickyCommandAndWait(
            StickyUiHost host, StickyUiCommand command,
            SynchronizationContext completionContext)
        {
            StickyUiCommandResult result = null;
            using (ManualResetEventSlim completed =
                new ManualResetEventSlim(false))
            {
                host.PostCommand(command, delegate(StickyUiCommandResult value)
                {
                    result = value;
                    completed.Set();
                }, completionContext);
                WaitForSignalWithUiPump(completed, 5000);
            }
            return result;
        }

        private static bool WaitForSignalWithUiPump(
            ManualResetEventSlim signal, int timeoutMilliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!signal.IsSet && timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return signal.IsSet;
        }
    }
}
