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
                Pc2Assert(scene.Repository.CommitFullRestore(new[] { restored }).Succeeded,
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
                startup.StickyDatasetReplaced();
                Pc2Assert(((Queue<StickyNoteData>)Pc2Get(startup, "_startupVisibleNotes")).Count == 0 &&
                    ((HashSet<string>)Pc2Get(startup, "_expectedFirstRenderNoteIds")).Count == 0,
                    "replacement rebuilds startup queue and first-render expectations from the new dataset");
                startup.Dispose();
            }
            evidence.Add("startup restore respects hide, queued cancellation, replacement identity and first-render reset");
        }
    }
}
