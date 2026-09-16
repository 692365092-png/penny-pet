using System;

namespace PennyPet
{
    // Pure hysteresis and suppression rules for the pet hover state. Windows
    // cursor/bounds logic stays in PetForm; this layer only decides timing and
    // whether an ambient Hover bubble is allowed to show.
    internal static class PetHoverStabilityRules
    {
        internal const int EnterDwellMilliseconds = 100;
        internal const int LeaveGraceMilliseconds = 180;

        internal static bool ShouldCommitEnter(DateTime pendingEnterAtUtc,
            DateTime nowUtc)
        {
            if (pendingEnterAtUtc == default(DateTime) ||
                nowUtc < pendingEnterAtUtc) return false;
            return nowUtc - pendingEnterAtUtc >=
                TimeSpan.FromMilliseconds(EnterDwellMilliseconds);
        }

        internal static bool ShouldCommitLeave(DateTime pendingLeaveAtUtc,
            DateTime nowUtc)
        {
            if (pendingLeaveAtUtc == default(DateTime) ||
                nowUtc < pendingLeaveAtUtc) return false;
            return nowUtc - pendingLeaveAtUtc >=
                TimeSpan.FromMilliseconds(LeaveGraceMilliseconds);
        }

        internal static bool ShouldSuppressHover(bool stableMouseInside,
            bool menuVisible, bool dragging, bool silentMode,
            bool hoverSuppressed)
        {
            return !stableMouseInside || menuVisible || dragging ||
                silentMode || hoverSuppressed;
        }
    }
}
