using System;
using System.Threading;

namespace PennyPet
{
    // Holds reminder coordination state and product rules. ReminderSchedule
    // remains the data model; PetForm remains the Windows UI integration point.
    internal sealed class PetReminderCoordinator
    {
        internal const int DueReminderBubbleDurationMilliseconds = 0;

        private int _animationGeneration;

        internal long LastBannerSecond { get; set; }
        internal ReminderItem PreAlertItem { get; set; }

        internal PetReminderCoordinator()
        {
            LastBannerSecond = Int64.MinValue;
        }

        internal int NextAnimationGeneration()
        {
            return Interlocked.Increment(ref _animationGeneration);
        }

        internal int CurrentAnimationGeneration
        {
            get { return _animationGeneration; }
        }

        internal static bool ShouldRefreshReminderBanner(long previousSecond,
            long currentSecond)
        {
            return previousSecond != currentSecond;
        }

        internal static bool ShouldReplaceBubble(bool currentIsDueReminder,
            bool currentIsPreAlert, bool incomingIsDueReminder, bool exiting)
        {
            // An at-time reminder is persistent against pet clicks, but it is
            // not allowed to block later feedback. Any later application
            // bubble replaces it. Pre-alert countdowns keep their older rule.
            if (currentIsDueReminder) return true;
            return !currentIsPreAlert || incomingIsDueReminder || exiting;
        }

        internal static bool IsPreAlertWindow(TimeSpan remaining)
        {
            return remaining > TimeSpan.Zero &&
                remaining <= TimeSpan.FromSeconds(20);
        }

        internal static bool ShouldShowPreAlert(ReminderItem item,
            TimeSpan remaining)
        {
            return item != null && item.PreAlertEnabled &&
                IsPreAlertWindow(remaining);
        }

        internal static bool ShouldRunReminderClock(bool exiting)
        {
            return !exiting;
        }

        internal static bool ShouldRestoreReminderAfterLaunch(
            ReminderItem item, DateTime launchedUtc)
        {
            return item != null && item.DeadlineUtc > launchedUtc;
        }
    }

}
