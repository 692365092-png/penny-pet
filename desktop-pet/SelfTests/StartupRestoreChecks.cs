using System;
using System.Collections.Generic;
using System.Threading;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static void RunStartupRestoreChecks(string root, List<string> evidence)
        {
            using (var scene = new Pc2Scene(root, "startup-restore-cancellation", true))
            {
                foreach (StickyNoteData member in scene.Notes)
                    StickyDockGroups.ClearMembership(member);
                scene.Hosted.CompleteCloseAll();
                scene.Start();
                scene.Host.Configure(scene.Workspace.HostedStickyEventReceived, scene.Context);
                var threadHost = (StickyUiThreadHost)Pc2Get(scene.Host, "_threadHost");
                using (var entered = new ManualResetEventSlim())
                using (var release = new ManualResetEventSlim())
                {
                    threadHost.PostToDispatcher(() =>
                    {
                        entered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                        return StickyUiCommandResult.Handled();
                    }, null, null);
                    try
                    {
                        Pc2Assert(entered.Wait(TimeSpan.FromSeconds(5)), "restore dispatcher gate entered");
                        StickyNoteData note = scene.Notes[0];
                        scene.Workspace.QueueStartupStickyRestore(note);
                        note.Visible = false;
                        scene.Workspace.PostHostedStickyHide(note);
                    }
                    finally { release.Set(); }
                    bool drained = false;
                    threadHost.PostStartupRestore(() => scene.Context.Post(delegate { drained = true; }, null));
                    scene.Context.PumpUntil(() => drained);
                    Pc2Assert(!scene.Hosted.ContainsNote(scene.Notes[0].Id) &&
                        scene.Send(StickyUiCommand.CaptureWindowFacts(scene.Notes[0].Id,
                            scene.Topology)).Status == StickyUiCommandStatus.NotHandled,
                        "hide cancels a queued startup create before it opens a native window");
                }

                StickyNoteData old = scene.Notes[1];
                var restored = old.CloneForPersistence();
                restored.Text = "replacement content";
                restored.Visible = false;
                Pc2Assert(WaitForPersistenceReceipt(scene.Repository.CommitFullRestoreAsync(new[] { restored })).Succeeded,
                    "isolated replacement committed");
                scene.Workspace.InvalidateStartupStickyRestores();
                scene.Workspace.QueueStartupStickyRestore(old);
                Pc2Assert(!scene.Hosted.ContainsNote(old.Id) &&
                    scene.Repository.Find(old.Id).Text == "replacement content",
                    "an old object with the same NoteId cannot reopen or overwrite a replacement");
                scene.Workspace.QueueStartupStickyRestore(scene.Repository.Find(old.Id));
                Pc2Assert(!scene.Hosted.ContainsNote(old.Id), "hidden startup intent stays hidden");

                var startup = (PetStartupCoordinator)Pc2Get(scene.Pet, "_startup");
                startup.BeginDeferredStartupWork();
                object inputsPhase = Pc2Get(startup, "_startupWorkPhase");
                startup.StickyDatasetReplaced();
                Pc2Assert(Pc2Get(startup, "_startupWorkPhase").Equals(inputsPhase),
                    "replacement cannot skip keyboard and startup preference initialization");
                Pc2Set(startup, "_startupWorkPhase", Enum.Parse(inputsPhase.GetType(), "RestoreNotes"));
                startup.StickyDatasetReplaced();
                Pc2Assert(((Queue<StickyNoteData>)Pc2Get(startup, "_startupVisibleNotes")).Count == 0 &&
                    ((HashSet<string>)Pc2Get(startup, "_expectedFirstRenderNoteIds")).Count == 0,
                    "replacement rebuilds startup queue and first-render expectations from the new dataset");
                startup.Dispose();
            }
            using (var scene = new Pc2Scene(root, "startup-dock-hide-before-ack", true))
            using (var nativeDone = new ManualResetEventSlim())
            {
                scene.Hosted.CompleteCloseAll();
                scene.Start();
                scene.Host.Configure(scene.Workspace.HostedStickyEventReceived, scene.Context);
                var operations = (DockRestoreOperations)Pc2Get(scene.Workspace.Dock, "_dockRestores");
                scene.Workspace.QueueStartupStickyRestore(scene.Notes[0]);
                scene.Context.PumpUntil(() => operations.Snapshot().Length == 1);
                var threadHost = (StickyUiThreadHost)Pc2Get(scene.Host, "_threadHost");
                threadHost.PostStartupRestore(() => nativeDone.Set());
                Pc2Assert(nativeDone.Wait(TimeSpan.FromSeconds(5)), "native restore completed before its owner ACK");
                foreach (StickyNoteData member in scene.Notes) member.Visible = false;
                foreach (StickyNoteData member in scene.Notes) scene.Workspace.PostHostedStickyHide(member);
                foreach (StickyNoteData member in scene.Notes)
                {
                    StickyUiCommandResult captured = scene.Send(StickyUiCommand.CaptureWindowFacts(member.Id, scene.Topology));
                    Pc2Assert(captured.Status == StickyUiCommandStatus.NotHandled ||
                        (captured.Snapshot != null && !captured.Snapshot.Visible),
                        "cancelled Dock restore must hide a window created before its ACK");
                    Pc2Assert(!scene.Repository.Find(member.Id).Visible, "cancelled restore cannot republish visibility");
                }
            }
            evidence.Add("startup restore respects hide, queued cancellation, replacement identity and first-render reset");
        }
    }
}
