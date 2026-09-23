using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        // Adapt existing catalogue fixtures to the single snapshot input. This
        // legacy fixture shape is deliberately absent from product constructors.
        private static PetDailyContentCoordinator CreateDailyCoordinator(
            Func<string> lastDate, Func<bool> silent, Func<bool> enabled,
            Func<bool> solar, Func<bool> almanac, Func<bool> weather,
            Func<WeatherLocation> location,
            Func<WeatherLocation, Task<WeatherForecastWindow>> forecast,
            Func<ZodiacSign> zodiac, Func<int> month, Func<int> day,
            Func<string, bool> show, Action<string> record)
        {
            return new PetDailyContentCoordinator(() => new DailyContentPreferencesSnapshot(
                silent(), enabled(), solar(), almanac(), weather(), location(),
                zodiac(), month(), day(), lastDate()), forecast,
                (preferences, now, text) => show(text), now => record(DailyContentRules.DateKey(now)));
        }

        private static bool RunConversationRuntimeChecks()
        {
            return Task.Run(delegate
            {
                using (ConversationProbe p = new ConversationProbe())
                {
                    Task<ConversationAnimation> first = p.Poke();
                    Pc2Assert(!first.IsCompleted, "weather wait is asynchronous");
                    p.Poke().GetAwaiter().GetResult();
                    Pc2Assert(p.Loads.Count == 1 && p.Settings.DailyLedgerDaypartsMask == 0 &&
                        String.IsNullOrEmpty(p.Settings.LastDailyBriefingDate),
                        "an in-flight repeat does not consume a daily slot");
                    p.Loads[0].SetResult(null);
                    first.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 1 && p.Settings.LastDailyBriefingDate == "20350101" &&
                        p.Settings.DailyLedgerDaypartsMask == PetDaypartRule.ConsumedMask(DayPart.Morning),
                        "accepted opening records its original daypart exactly once");
                    p.Now = p.Now.AddHours(4);
                    Pc2Assert(p.Poke().GetAwaiter().GetResult() == ConversationAnimation.Notification &&
                        PetDaypartRule.IsConsumed(p.Settings.DailyLedgerDaypartsMask, DayPart.Midday) &&
                        p.Loads.Count == 1, "daypart check-in shares the ledger without fetching weather");
                }
                using (ConversationProbe p = new ConversationProbe())
                {
                    Task<ConversationAnimation> old = p.Poke();
                    p.Runtime.InvalidatePending();
                    Task<ConversationAnimation> newer = p.Poke();
                    p.Loads[0].SetResult(null);
                    old.GetAwaiter().GetResult();
                    p.Poke().GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 0 && p.Loads.Count == 2 && !newer.IsCompleted,
                        "old completion cannot publish or clear a newer pending attempt");
                    p.Loads[1].SetResult(null);
                    newer.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 1, "newer attempt remains eligible to commit");
                }
                using (ConversationProbe p = new ConversationProbe())
                {
                    Task<ConversationAnimation> pending = p.Poke();
                    p.Settings.SilentMode = true;
                    p.Runtime.InvalidatePending();
                    p.Loads[0].SetResult(null);
                    pending.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 0 && p.Settings.DailyLedgerDaypartsMask == 0,
                        "quiet-mode change invalidates pending speech without consuming content");
                }
                foreach (bool nextDate in new[] { false, true })
                using (ConversationProbe p = new ConversationProbe())
                {
                    Task<ConversationAnimation> pending = p.Poke();
                    p.Now = nextDate ? p.Now.AddDays(1) : p.Now.AddHours(4);
                    p.Loads[0].SetResult(null);
                    pending.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 0 && String.IsNullOrEmpty(p.Settings.LastDailyBriefingDate),
                        "weather completion cannot deliver yesterday's or the previous daypart's opening");
                    Task<ConversationAnimation> current = p.Poke();
                    p.Loads[1].SetResult(null);
                    current.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 1 && p.Settings.DailyLedgerDate == DailyContentRules.DateKey(p.Now),
                        "the next poke uses the current date and ledger");
                }
                using (ConversationProbe p = new ConversationProbe())
                {
                    p.Accept = false; // Same rejection contract as a foreground reminder.
                    Task<ConversationAnimation> rejected = p.Poke();
                    p.Loads[0].SetResult(null);
                    rejected.GetAwaiter().GetResult();
                    Pc2Assert(p.Settings.DailyLedgerDaypartsMask == 0 &&
                        String.IsNullOrEmpty(p.Settings.LastDailyBriefingDate),
                        "rejected presentation does not consume the opening or daypart");
                    p.Accept = true;
                    Task<ConversationAnimation> retry = p.Poke();
                    p.Loads[1].SetResult(null);
                    retry.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 1, "rejected content can be retried");
                }
                using (ConversationProbe p = new ConversationProbe())
                {
                    Task<ConversationAnimation> pending = p.Poke();
                    p.Runtime.Stop();
                    p.Loads[0].SetResult(null);
                    pending.GetAwaiter().GetResult();
                    Pc2Assert(p.Accepted == 0 && p.Poke().GetAwaiter().GetResult() == ConversationAnimation.None,
                        "stopped conversation never publishes late or future requests");
                }
                return true;
            }).GetAwaiter().GetResult();
        }

        private sealed class ConversationProbe : IDisposable
        {
            internal DateTimeOffset Now = new DateTimeOffset(2035, 1, 1, 9, 0, 0, TimeSpan.Zero);
            internal readonly PetSettings Settings;
            internal readonly ConversationRuntime Runtime;
            internal readonly List<TaskCompletionSource<WeatherForecastWindow>> Loads =
                new List<TaskCompletionSource<WeatherForecastWindow>>();
            internal bool Accept = true;
            internal int Accepted;
            internal ConversationProbe()
            {
                Settings = new PetSettings(_ => PersistenceResult.Success()) {
                    DailyContentEnabled = true, SolarTermEnabled = false, AlmanacEnabled = false,
                    WeatherEnabled = true, WeatherLocationName = "Test City",
                    WeatherLatitude = 30, WeatherLongitude = 114, WeatherTimezone = "UTC"
                };
                Runtime = new ConversationRuntime(Settings, location => {
                    TaskCompletionSource<WeatherForecastWindow> pending =
                        new TaskCompletionSource<WeatherForecastWindow>();
                    Loads.Add(pending);
                    return pending.Task;
                }, (kind, text) => { if (Accept) Accepted++; return Accept; }, () => Now, new Random(1));
            }
            internal Task<ConversationAnimation> Poke() { return Runtime.HandlePetPokedAsync(Now); }
            public void Dispose() { Runtime.Stop(); Settings.WaitForPendingSaves(); }
        }
    }
}
