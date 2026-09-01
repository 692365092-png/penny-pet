namespace PennyPet
{
    internal static class PetMessagePolicy
    {
        internal static bool ShouldReplace(PetMessageKind? current,
            PetMessageKind incoming, bool exiting)
        {
            if (!current.HasValue || exiting) return true;
            if (incoming == PetMessageKind.ReminderDue) return true;
            if (current.Value == PetMessageKind.ReminderPreAlert) return false;
            if (current.Value != PetMessageKind.ReminderDue) return true;
            return incoming == PetMessageKind.Feedback;
        }

        internal static bool ShouldSuppress(PetMessageKind kind,
            bool silentMode)
        {
            if (!silentMode) return false;
            return kind == PetMessageKind.Hover ||
                kind == PetMessageKind.DailyGreeting ||
                kind == PetMessageKind.Discovery;
        }

        internal static bool IsProtectedReminder(PetMessageKind kind)
        {
            return kind == PetMessageKind.ReminderPreAlert ||
                kind == PetMessageKind.ReminderDue;
        }
    }
}
