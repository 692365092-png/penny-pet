using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static bool RunReminderRuntimeChecks()
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            Pc2Context context = new Pc2Context();
            SynchronizationContext.SetSynchronizationContext(context);
            PetSettings settings = new PetSettings(_ => PersistenceResult.Success());
            StickyNoteRepository notes = new StickyNoteRepository(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "reminder-runtime-test.dat"),
                _ => PersistenceResult.Success());
            ReminderSchedule schedule = new ReminderSchedule();
            ReminderViewProbe view = new ReminderViewProbe();
            try
            {
                using (ReminderRuntime runtime = new ReminderRuntime(schedule, settings, notes, view))
                {
                    view.Runtime = runtime;
                    DateTime now = DateTime.UtcNow;
                    DateTime deadline = now.AddMinutes(10);
                    StickyNoteData note = notes.CreateDraft("keep body", Point.Empty);
                    note.Visible = false;
                    settings.Reminders.Add(new ReminderItem(now.AddMinutes(-1), "expired"));
                    settings.Reminders.Add(new ReminderItem(deadline, "linked", note.Id, 10.5F, true));
                    settings.Reminders.Add(new ReminderItem(deadline, "orphan", "missing"));
                    runtime.Restore(now);
                    Pc2Assert(schedule.Count == 1 && settings.Reminders.Count == 1 &&
                        note.ReminderUtcTicks == deadline.Ticks && view.Changes == 0,
                        "restore reconciles expiry and links before UI startup");
                    runtime.Tick(deadline);
                    Pc2Assert(view.Deliveries == 0, "clock does not execute before Start");
                    settings.SilentMode = true; // Quiet ambient speech must not disable explicit reminders.
                    runtime.Start();
                    view.SuppressPreAlert = true;
                    runtime.Tick(deadline.AddSeconds(-10));
                    view.SuppressPreAlert = false;
                    runtime.Tick(deadline.AddSeconds(-9));
                    Pc2Assert(!view.LastUpdate, "a rejected pre-alert never becomes current");
                    runtime.Tick(deadline.AddSeconds(-8));
                    Pc2Assert(view.LastUpdate, "accepted pre-alert updates in place");
                    ReminderItem original = schedule.Next;
                    ReminderItem edited = runtime.Edit(original, deadline.AddMinutes(1),
                        "edited", 12, true);
                    Pc2Assert(edited != null && view.Current == null &&
                        runtime.Edit(original, deadline, "stale", 10, false) == null,
                        "edit closes obsolete pre-alert and rejects an obsolete dialog result");
                    Pc2Assert(runtime.CancelForNote(note) == 1 &&
                        note.ReminderUtcTicks == 0 && note.Text == "keep body",
                        "linked cancellation preserves content");

                    // Reentrant presentation and repeated ticks consume only once.
                    schedule.Restore(new[] { new ReminderItem(deadline, "due", note.Id) });
                    view.OnDue = () => runtime.Tick(deadline);
                    runtime.Tick(deadline);
                    view.OnDue = null;
                    runtime.Tick(deadline);
                    Pc2Assert(view.Deliveries == 1 && schedule.Count == 0 &&
                        note.Visible && note.ReminderUtcTicks == 0 && view.Linked == note,
                        "due is consumed before presentation and opens its linked note");
                    view.CloseCurrentMessage();
                    CompleteReminderArt(view.Loads[0], context);
                    Pc2Assert(view.Animations == 0, "dismissed due bubble cannot start late animation");

                    runtime.Add(deadline, "A", 10, false);
                    runtime.Tick(deadline);
                    runtime.Add(deadline, "B", 10, false);
                    runtime.Tick(deadline);
                    CompleteReminderArt(view.Loads[2], context);
                    CompleteReminderArt(view.Loads[1], context);
                    Pc2Assert(view.Animations == 1 && view.AnimationThread == Thread.CurrentThread.ManagedThreadId,
                        "only the newest intent starts animation, on the owning STA");
                    runtime.Add(deadline, "cancel-all", 10, false);
                    runtime.Tick(deadline);
                    runtime.CancelAll();
                    CompleteReminderArt(view.Loads[3], context);
                    Pc2Assert(view.Animations == 1 && settings.Reminders.Count == 0,
                        "cancel-all invalidates pending attention and persists empty schedule");

                    // Exercise the actual WinForms timer, not only explicit Tick calls.
                    int delivered = view.Deliveries;
                    schedule.Restore(new[] { new ReminderItem(DateTime.UtcNow.AddSeconds(-1), "clock") });
                    context.PumpUntil(() => view.Deliveries > delivered);
                    TaskCompletionSource<int> late = view.Loads[view.Loads.Count - 1];
                    runtime.Stop();
                    schedule.Restore(new[] { new ReminderItem(deadline, "stopped") });
                    runtime.Tick(deadline);
                    CompleteReminderArt(late, context);
                    Pc2Assert(view.Deliveries == delivered + 1 && view.Animations == 1,
                        "Stop blocks ticks and invalidates outstanding art work");

                    schedule.Restore(new[] {
                        new ReminderItem(deadline, "restored-away", note.Id),
                        new ReminderItem(deadline, "standalone") });
                    Pc2Assert(notes.CommitFullRestore(new StickyNoteData[0]).Succeeded,
                        "replace note model through the actual full-restore entry");
                    runtime.ReconcileNoteLinks();
                    Pc2Assert(schedule.Count == 1 && schedule.Next.Text == "standalone" &&
                        settings.Reminders.Count == 1 && notes.Count == 0,
                        "full restore removes orphan reminder links and preserves standalone reminders");
                }
                return true;
            }
            finally
            {
                settings.WaitForPendingSaves();
                notes.WaitForPendingSaves();
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private static void CompleteReminderArt(TaskCompletionSource<int> load, Pc2Context context)
        {
            Task.Run(() => load.SetResult(0)).GetAwaiter().GetResult();
            bool drained = false;
            context.Post(_ => drained = true, null);
            context.PumpUntil(() => drained);
        }

        private sealed class ReminderViewProbe : IReminderPresentation
        {
            internal ReminderRuntime Runtime;
            internal PetMessageKind? Current;
            internal bool SuppressPreAlert, LastUpdate;
            internal int Changes, Deliveries, Animations, AnimationThread;
            internal StickyNoteData Linked;
            internal Action OnDue;
            internal readonly List<TaskCompletionSource<int>> Loads = new List<TaskCompletionSource<int>>();
            public void RemindersChanged() { Changes++; }
            public bool TryShowPreAlert(string text, bool updateCurrent)
            {
                if (SuppressPreAlert || Current == PetMessageKind.ReminderDue) return false;
                LastUpdate = updateCurrent;
                Current = PetMessageKind.ReminderPreAlert;
                return true;
            }
            public void ClosePreAlert()
            {
                if (Current == PetMessageKind.ReminderPreAlert) CloseCurrentMessage();
            }
            public void CloseCurrentMessage()
            {
                PetMessageKind? closed = Current;
                Current = null;
                if (closed.HasValue) Runtime.MessageClosed(closed.Value);
            }
            public void RefreshHover() { }
            public void ShowDue(ReminderItem item, StickyNoteData linkedNote)
            {
                Current = PetMessageKind.ReminderDue;
                Deliveries++;
                Linked = linkedNote;
                if (OnDue != null) OnDue();
            }
            public Task PrepareAttentionAsync()
            {
                TaskCompletionSource<int> load = new TaskCompletionSource<int>();
                Loads.Add(load);
                return load.Task;
            }
            public void BeginAttention()
            {
                Animations++;
                AnimationThread = Thread.CurrentThread.ManagedThreadId;
            }
        }
    }
}
