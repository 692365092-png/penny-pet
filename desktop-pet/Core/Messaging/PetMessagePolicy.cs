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
            // Product rule: an active due reminder is absolute priority.
            if (current.Value == PetMessageKind.ReminderDue) return false;
            if (current.Value == PetMessageKind.EasterEgg)
                return incoming == PetMessageKind.ReminderPreAlert;
            if (current.Value == PetMessageKind.SmallTalk &&
                (incoming == PetMessageKind.Hover ||
                    incoming == PetMessageKind.DailyGreeting ||
                    incoming == PetMessageKind.Discovery ||
                    incoming == PetMessageKind.Feedback)) return false;
            return true;
        }

        internal static bool ShouldSuppress(PetMessageKind kind,
            bool silentMode)
        {
            if (!silentMode) return false;
            return kind == PetMessageKind.Hover ||
                kind == PetMessageKind.DailyGreeting ||
                kind == PetMessageKind.Discovery ||
                kind == PetMessageKind.SmallTalk;
        }

        internal static bool IsProtectedReminder(PetMessageKind kind)
        {
            return kind == PetMessageKind.ReminderPreAlert ||
                kind == PetMessageKind.ReminderDue ||
                kind == PetMessageKind.EasterEgg;
        }

        internal static bool CanBreakReadability(PetMessageKind kind)
        {
            return kind == PetMessageKind.ReminderDue ||
                kind == PetMessageKind.ReminderPreAlert ||
                kind == PetMessageKind.EasterEgg;
        }
    }
}
