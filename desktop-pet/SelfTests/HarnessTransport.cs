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

namespace PennyPet
{
    internal static partial class SelfTest
    {

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
