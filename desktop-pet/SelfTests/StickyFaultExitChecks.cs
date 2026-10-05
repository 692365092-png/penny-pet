using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static void RunStickyFaultExitChecks(string root, List<string> evidence)
        {
            using (var scene = new Pc2Scene(root, "sticky-fault-exit", true))
            using (var host = new LifecycleHost(DateTime.UtcNow))
            {
                scene.Start();
                var threadHost = (StickyUiThreadHost)Pc2Get(scene.Host, "_threadHost");
                bool faulted = false;
                threadHost.PostToDispatcher(() =>
                {
                    Pc2Call(threadHost, "HandleDispatcherFault", new InvalidOperationException("isolated fault"));
                    return StickyUiCommandResult.Handled();
                }, result => faulted = true, scene.Context);
                scene.Context.PumpUntil(() => faulted && threadHost.WaitForExit(0));
                Pc2Assert(scene.Host.IsFaultedAndExited, "faulted native owner has fully exited");
                host.Workspace = scene.Workspace;
                Task<bool> ordinary = (Task<bool>)Pc2Call(host.Persistence,
                    "PreparePersistenceOperationAsync", false);
                scene.Context.PumpUntil(() => ordinary.IsCompleted);
                Pc2Assert(!ordinary.Result && !host.Persistence.IsActive && host.Window.Enabled,
                    "a dead UI cannot accept an ordinary replacement operation");
                Task<bool> exit = (Task<bool>)Pc2Call(host.Persistence,
                    "PreparePersistenceOperationAsync", true);
                scene.Context.PumpUntil(() => exit.IsCompleted);
                Pc2Assert(exit.Result && host.Persistence.IsActive &&
                    scene.Repository.Find(scene.Notes[0].Id).Visible,
                    "exit can save the accepted model without changing its visibility");
                Task resumed = (Task)Pc2Call(host.Persistence, "ResumePersistenceOperationAsync");
                scene.Context.PumpUntil(() => resumed.IsCompleted);
                Pc2Assert(!resumed.IsFaulted && host.Window.Enabled && !host.Persistence.IsActive,
                    "cancel still releases the shell after a subsystem fault");
            }
            evidence.Add("faulted Sticky thread permits canonical-only exit after termination while rejecting ordinary replacement");
        }
    }
}
