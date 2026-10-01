using System;

namespace PennyPet
{
    internal static class PetBirthdayWordingCatalog
    {
        private static readonly DailyLineEntry[] Penny =
        {
            Line("BIRTHDAY-PENNY-APR22", "今天是我生日欸，四月二十二号。"),
            Line("BIRTHDAY-PENNY-APR22-2", "今天是我生日，所以有点特别。")
        };

        private static readonly DailyLineEntry[] User =
        {
            Line("BIRTHDAY-USER", "生日快乐。今天就对自己好一点吧。"),
            Line("BIRTHDAY-USER-2", "生日快乐呀，今天想怎么过？")
        };

        private static readonly DailyLineEntry[] Shared =
        {
            Line("BIRTHDAY-SHARED-APR22", "原来我们是同一天生日。生日快乐呀。"),
            Line("BIRTHDAY-SHARED-APR22-2", "居然和我同一天生日，还挺有缘的。")
        };

        internal static DailyLineEntry Select(PetBirthdayKind kind,
            DateTime localDate)
        {
            DailyLineEntry[] entries;
            switch (kind)
            {
                case PetBirthdayKind.Penny:
                    entries = Penny;
                    break;
                case PetBirthdayKind.User:
                    entries = User;
                    break;
                case PetBirthdayKind.Shared:
                    entries = Shared;
                    break;
                default:
                    return null;
            }
            int dayNumber = localDate.Year * 372 + localDate.Month * 31 +
                localDate.Day;
            return entries[PositiveModulo(dayNumber, entries.Length)];
        }

        private static DailyLineEntry Line(string id, string text)
        {
            return new DailyLineEntry(id, text);
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
