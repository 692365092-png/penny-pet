using System;

namespace PennyPet
{
    internal static class PetBirthdayWordingCatalog
    {
        private static readonly DailyLineEntry[] Penny =
        {
            Line("BIRTHDAY-PENNY-APR22", "今天可是我的生日，四月二十二。"),
            Line("BIRTHDAY-PENNY-APR22-2", "今天对我来说有点特别。")
        };

        private static readonly DailyLineEntry[] User =
        {
            Line("BIRTHDAY-USER", "生日快乐，今天对你好一点。"),
            Line("BIRTHDAY-USER-2", "今天是你的日子呀。")
        };

        private static readonly DailyLineEntry[] Shared =
        {
            Line("BIRTHDAY-SHARED-APR22", "我们今天一起过生日呀。"),
            Line("BIRTHDAY-SHARED-APR22-2", "居然和我同一天生日，这一天算有点缘分。")
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
