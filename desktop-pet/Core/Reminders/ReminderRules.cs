using System;

namespace PennyPet
{
    internal static class ReminderRules
    {
        internal const int DueReminderBubbleDurationMilliseconds = 0;

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

        internal static bool ShouldRestoreReminderAfterLaunch(
            ReminderItem item, DateTime launchedUtc)
        {
            return item != null && item.DeadlineUtc > launchedUtc;
        }
    }

}
