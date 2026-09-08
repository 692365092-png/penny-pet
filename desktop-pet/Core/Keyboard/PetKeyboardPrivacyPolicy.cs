using System;

namespace PennyPet
{
    // Pure policy for optional keyboard-activity display. Platform evidence
    // gathering and first-use wording remain in each platform adapter.
    internal static class PetKeyboardPrivacyPolicy
    {
        internal static bool RequiresFirstUseNotice(bool desiredEnabled,
            bool noticeAccepted)
        {
            return desiredEnabled && !noticeAccepted;
        }

        internal static bool ShouldStartHook(bool desiredEnabled,
            bool noticeAccepted)
        {
            return desiredEnabled && noticeAccepted;
        }

        internal static bool ShouldDisableUnacknowledgedLegacyOptIn(
            bool storedEnabled, bool noticeAccepted)
        {
            return storedEnabled && !noticeAccepted;
        }

        internal static bool ShouldSuppressCapturedInput(
            bool sensitiveTargetDetected, bool inspectionAvailable)
        {
            return !inspectionAvailable || sensitiveTargetDetected;
        }

        internal static bool ShouldSuppressOwnApplicationInput(
            bool ownApplicationInput, bool focusedStickyTextInput)
        {
            return ownApplicationInput && !focusedStickyTextInput;
        }
    }
}
