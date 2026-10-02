using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static bool RunStickyReminderHostChecks()
        {
            using (StickyUiHost host = new StickyUiHost())
            using (ManualResetEventSlim tick = new ManualResetEventSlim(false))
            {
                host.Configure(delegate(StickyUiEvent value) { },
                    new SynchronizationContext());
                host.Start();
                string before = null;
                int rebuilds = 0;
                ReminderItem reminder = new ReminderItem(
                    DateTime.UtcNow.AddMinutes(3), "shared reminder",
                    "reminder-clock-a");
                try
                {
                    ReminderHostOnSta(host, delegate
                    {
                        foreach (string id in new[] { "reminder-clock-a", "reminder-clock-b" })
                        {
                            StickyNoteData note = new StickyNoteData
                            {
                                Id = id, Title = id, Text = "keep body",
                                X = 120, Y = 120, Width = 300, Height = 240
                            };
                            ReminderHostCommand(host, StickyUiCommand.Create(
                                StickyNoteUiSnapshot.Capture(note), false));
                        }
                        Pc2Assert(Pc2Get(host, "_reminderClock") == null,
                            "no countdown timer before a visible reminder");

                        StickyUiCommandResult result = ReminderHostCommand(host,
                            StickyUiCommand.UpdateAllReminders(new[] { reminder }));
                        Pc2Assert(result.Snapshot == null && result.Facts == null,
                            "reminder-only update does not capture geometry/content");
                        foreach (StickyWindowSession session in ReminderHostSessions(host).Values)
                        {
                            StickyNoteWindow window = (StickyNoteWindow)Pc2Get(session, "_window");
                            Pc2Assert(window.ReminderBannerLineCount == 1 &&
                                window.ReminderBannerText.Contains("shared reminder"),
                                "both notes retain the shared reminder, regardless of SourceNoteId");
                        }
                        StickyNoteWindow first = ReminderHostWindow(host);
                        ((ListBox)Pc2Get(first, "_reminderList")).SelectedIndex = 0;
                        before = first.ReminderBannerText;
                        rebuilds = (int)Pc2Get(first, "_reminderBannerRebuildCount");
                        DispatcherTimer clock = (DispatcherTimer)Pc2Get(host, "_reminderClock");
                        Pc2Assert(clock.IsEnabled, "visible banner starts the shared clock");
                        clock.Tick += delegate { tick.Set(); };
                    });

                    // Deliberately do not pump the caller's UI: Sticky must tick independently.
                    Pc2Assert(tick.Wait(5000), "Sticky countdown advances while caller is blocked");
                    ReminderHostOnSta(host, delegate
                    {
                        StickyNoteWindow first = ReminderHostWindow(host);
                        Pc2Assert(first.ReminderBannerText != before &&
                            first.Data.Text == "keep body" &&
                            ((ListBox)Pc2Get(first, "_reminderList")).SelectedIndex == 0 &&
                            (int)Pc2Get(first, "_reminderBannerRebuildCount") == rebuilds,
                            "countdown changes in place without touching selection/body");
                        DispatcherTimer clock = (DispatcherTimer)Pc2Get(host, "_reminderClock");
                        ReminderHostCommand(host, StickyUiCommand.Hide("reminder-clock-a"));
                        ReminderHostCommand(host, StickyUiCommand.Hide("reminder-clock-b"));
                        Pc2Assert(!clock.IsEnabled, "all hidden banners stop the clock");
                        ReminderHostCommand(host, StickyUiCommand.Show("reminder-clock-a", false));
                        Pc2Assert(clock.IsEnabled, "reopening a banner resumes the clock");
                        ReminderHostCommand(host, StickyUiCommand.UpdateAllReminders(null));
                        Pc2Assert(!clock.IsEnabled && !first.HasReminderBanner,
                            "removing all reminders stops countdown work");
                        ReminderHostCommand(host, StickyUiCommand.UpdateAllReminders(new[] { reminder }));
                        ReminderHostCommand(host, StickyUiCommand.CloseAll());
                        Pc2Assert(!clock.IsEnabled && ReminderHostSessions(host).Count == 0,
                            "batch close stops the clock even when session events are suppressed");
                    });
                    return true;
                }
                finally
                {
                    host.BeginShutdown();
                    Pc2Assert(host.WaitForExit(5000), "reminder host shutdown");
                }
            }
        }

        private static Dictionary<string, StickyWindowSession> ReminderHostSessions(StickyUiHost host)
        {
            return (Dictionary<string, StickyWindowSession>)Pc2Get(host, "_sessions");
        }

        private static StickyNoteWindow ReminderHostWindow(StickyUiHost host)
        {
            return (StickyNoteWindow)Pc2Get(
                ReminderHostSessions(host)["reminder-clock-a"], "_window");
        }

        private static StickyUiCommandResult ReminderHostCommand(StickyUiHost host, StickyUiCommand command)
        {
            StickyUiCommandResult result = (StickyUiCommandResult)Pc2Call(host, "HandleCommand", command);
            Pc2Assert(result.Status == StickyUiCommandStatus.Handled, "reminder host command " + command.Kind);
            return result;
        }

        private static void ReminderHostOnSta(StickyUiHost host, Action check)
        {
            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                Exception failure = null;
                StickyUiCommandResult outcome = null;
                ((StickyUiThreadHost)Pc2Get(host, "_threadHost")).PostToDispatcher(
                    delegate
                    {
                        try { check(); }
                        catch (Exception error) { failure = error; }
                        return StickyUiCommandResult.Handled();
                    },
                    delegate(StickyUiCommandResult result) { outcome = result; done.Set(); },
                    null);
                Pc2Assert(done.Wait(10000), "reminder check finished on Sticky STA");
                if (failure != null) throw new InvalidOperationException("Sticky reminder check failed.", failure);
                Pc2Assert(outcome != null && outcome.Status == StickyUiCommandStatus.Handled,
                    "reminder check was accepted");
            }
        }
    }
}
