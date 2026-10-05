using System;
using System.Collections.Generic;
using System.Threading;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static void RunStickyCommandShutdownChecks(List<string> evidence)
        {
            var context = new Pc2Context();
            using (var host = new StickyUiThreadHost())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                host.Start();
                int normalCallbacks = 0, startupCallbacks = 0, effects = 0;
                StickyUiCommandResult normal = null, startup = null;
                host.PostToDispatcher(() =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                    return StickyUiCommandResult.Handled();
                }, null, null);
                try
                {
                    Pc2Assert(entered.Wait(TimeSpan.FromSeconds(5)), "shutdown dispatcher gate entered");
                    host.PostToDispatcher(() => { effects++; return StickyUiCommandResult.Handled(); },
                        value => { normal = value; normalCallbacks++; }, context);
                    host.PostStartupRestore(() => { effects++; return StickyUiCommandResult.Handled(); },
                        value => { startup = value; startupCallbacks++; }, context);
                    host.BeginShutdown(null);
                    context.PumpUntil(() => normal != null && startup != null);
                    Pc2Assert(normal.Status == StickyUiCommandStatus.NotAccepted &&
                        startup.Status == StickyUiCommandStatus.NotAccepted && effects == 0,
                        "shutdown completes both accepted queues without running their window effects");
                }
                finally { release.Set(); }
                context.PumpUntil(() => host.WaitForExit(0));
                bool marker = false;
                context.Post(delegate { marker = true; }, null);
                context.PumpUntil(() => marker);
                Pc2Assert(normalCallbacks == 1 && startupCallbacks == 1 && effects == 0,
                    "aborted dispatcher work cannot complete its receipt twice");
            }
            evidence.Add("accepted Normal and Background commands resolve once when Sticky dispatcher shuts down");
        }
    }
}
