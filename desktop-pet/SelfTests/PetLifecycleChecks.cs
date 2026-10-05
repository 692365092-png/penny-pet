using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        // No files, hooks or tray: exercise the production workflow owners
        // across the shell-before-runtime interval with a real window surface.
        private sealed class LifecycleHost : IPetStartupHost, IPetPersistenceHost, IDisposable
        {
            public Form Window { get; private set; } = new Form();
            public bool IsExiting { get; set; }
            public bool PersistenceActive { get { return Persistence.IsActive; } }
            public PetSettings Settings { get; } = new PetSettings();
            public GlobalKeyboardActivity Keyboard { get { return null; } }
            public StickyFeature Notes { get { return null; } }
            public StickyWorkspace Workspace { get; set; }
            public ReminderRuntime Reminders { get { return null; } }
            internal readonly PetStartupCoordinator Startup;
            internal readonly PetPersistenceCoordinator Persistence;
            internal bool MenuEnabled = true;
            internal int Pauses, Resumes, Renders, Publications;

            internal LifecycleHost(DateTime launchedUtc)
            {
                Persistence = new PetPersistenceCoordinator(this);
                Startup = new PetStartupCoordinator(this, launchedUtc);
            }

            public void SetMenuEnabled(bool enabled) { MenuEnabled = enabled; }
            public void StopConversation() { Pauses++; }
            public void ResumeConversation() { Resumes++; }
            public void ResumeRuntimeComposition() { Startup.ResumeDeferredRuntimeComposition(); }
            public void ShowBubble(string text) { }
            public void CaptureLocationForSave() { }
            public void StickyDatasetReplaced() { Startup.StickyDatasetReplaced(); }
            public void FinishExitSequence() { IsExiting = true; Startup.Dispose(); }
            public void RefreshKeyboardMenu() { }
            public void RefreshMenu() { }
            public void RenderFrame() { Renders++; }
            public void PublishPreparedStickyRuntime(StickyLoadResult prepared) { Publications++; }
            public void AbortStartupComposition() { IsExiting = true; Startup.Dispose(); }
            public void Dispose() { Startup.Dispose(); Window.Dispose(); }
        }

        private static void RunPetLifecycleChecks(List<string> evidence)
        {
            DateTime launch = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            using (var host = new LifecycleHost(launch))
            {
                int ready = 0;
                host.Startup.ShellReady += delegate(object sender, EventArgs args)
                {
                    Pc2Assert(ReferenceEquals(sender, host.Window), "shell event retains window sender");
                    ready++;
                };
                host.Startup.ArtReady(false);
                Pc2Assert(host.Startup.DisplaySuppressed && ready == 0,
                    "mandatory art gates shell rendering");
                host.Startup.ArtReady(true);
                host.Startup.ArtReady(true);
                Pc2Assert(!host.Startup.DisplaySuppressed && ready == 1 && host.Renders == 1,
                    "shell renders and announces readiness once");
                Pc2Assert(host.Startup.LaunchedUtc == launch,
                    "runtime attach retains the original launch timestamp");

                Task<bool> prepared = (Task<bool>)Pc2Call(host.Persistence, "PreparePersistenceOperationAsync", false);
                Pc2Assert(prepared.IsCompleted && prepared.Result && host.Persistence.IsActive,
                    "persistence owns the operation before a runtime exists");
                Pc2Assert(!host.Window.Enabled && !host.MenuEnabled && host.Pauses == 1,
                    "operation suspends shell entry points and conversation");
                Task<bool> duplicate = (Task<bool>)Pc2Call(host.Persistence, "PreparePersistenceOperationAsync", false);
                Pc2Assert(duplicate.IsCompleted && !duplicate.Result && host.Pauses == 1,
                    "overlapping operation is rejected without overwriting resume state");

                Pc2Call(host.Startup, "CompleteRuntimeComposition", null, null);
                host.Startup.ResumeDeferredRuntimeComposition();
                Pc2Assert(host.Publications == 0, "publication stays deferred until persistence releases");
                Task resumed = (Task)Pc2Call(host.Persistence, "ResumePersistenceOperationAsync");
                Pc2Assert(resumed.IsCompleted && !resumed.IsFaulted && !host.Persistence.IsActive,
                    "cancelled operation releases persistence state");
                Pc2Assert(host.Window.Enabled && host.MenuEnabled && host.Resumes == 1 &&
                    host.Publications == 1, "resume restores the window and publishes the deferred runtime");
                host.Startup.ResumeDeferredRuntimeComposition();
                Pc2Assert(host.Publications == 1, "publication completion is consumed once");

                host.Window.Enabled = false;
                prepared = (Task<bool>)Pc2Call(host.Persistence, "PreparePersistenceOperationAsync", false);
                Pc2Assert(prepared.IsCompleted && prepared.Result, "second operation starts");
                resumed = (Task)Pc2Call(host.Persistence, "ResumePersistenceOperationAsync");
                Pc2Assert(resumed.IsCompleted && !resumed.IsFaulted && !host.Window.Enabled,
                    "resume preserves an already disabled owner");
            }

            using (var host = new LifecycleHost(launch))
            {
                Task<bool> prepared = (Task<bool>)Pc2Call(host.Persistence, "PreparePersistenceOperationAsync", false);
                Pc2Assert(prepared.IsCompleted && prepared.Result, "exit preparation starts");
                Pc2Call(host.Startup, "CompleteRuntimeComposition", null, null);
                host.FinishExitSequence();
                Task resumed = (Task)Pc2Call(host.Persistence, "ResumePersistenceOperationAsync");
                Pc2Assert(resumed.IsCompleted && !resumed.IsFaulted, "late resume completes safely");
                Pc2Call(host.Startup, "CompleteRuntimeComposition", null, null);
                host.Startup.ArtReady(true);
                host.Startup.BeginDeferredStartupWork();
                Pc2Assert(host.Publications == 0 && host.Renders == 0 && host.Resumes == 0,
                    "exit discards pending and late startup work without restarting conversation");
                Pc2Assert(Pc2Get(host.Startup, "_startupWorkTimer") == null,
                    "disposed lifecycle cannot restart its timer");
            }
            evidence.Add("persistence and startup owners preserve pause, cancellation, readiness and exit ordering");
        }
    }
}
