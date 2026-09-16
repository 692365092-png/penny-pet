using System;

namespace PennyPet
{
    internal enum PetBirthdayKind
    {
        None,
        Penny,
        User,
        Shared
    }

    internal static class PetBirthdayRule
    {
        internal const int PennyBirthMonth = 4;
        internal const int PennyBirthDay = 22;

        internal static PetBirthdayKind Resolve(int localMonth,
            int localDay, int userBirthMonth, int userBirthDay)
        {
            bool isPennyBirthday = localMonth == PennyBirthMonth &&
                localDay == PennyBirthDay;
            bool isUserBirthday = IsValidBirthday(userBirthMonth,
                userBirthDay) && localMonth == userBirthMonth &&
                localDay == userBirthDay;

            if (isPennyBirthday && isUserBirthday)
                return PetBirthdayKind.Shared;
            if (isUserBirthday)
                return PetBirthdayKind.User;
            if (isPennyBirthday)
                return PetBirthdayKind.Penny;
            return PetBirthdayKind.None;
        }

        internal static bool IsValidBirthday(int month, int day)
        {
            if (month < 1 || month > 12 || day < 1) return false;
            return day <= DateTime.DaysInMonth(2000, month);
        }

        internal static bool TryDeriveZodiac(int month, int day,
            out ZodiacSign sign)
        {
            sign = ZodiacSign.None;
            if (!IsValidBirthday(month, day)) return false;
            sign = DeriveZodiac(month, day);
            return true;
        }

        private static ZodiacSign DeriveZodiac(int month, int day)
        {
            if ((month == 3 && day >= 21) || (month == 4 && day <= 19))
                return ZodiacSign.Aries;
            if ((month == 4 && day >= 20) || (month == 5 && day <= 20))
                return ZodiacSign.Taurus;
            if ((month == 5 && day >= 21) || (month == 6 && day <= 20))
                return ZodiacSign.Gemini;
            if ((month == 6 && day >= 21) || (month == 7 && day <= 22))
                return ZodiacSign.Cancer;
            if ((month == 7 && day >= 23) || (month == 8 && day <= 22))
                return ZodiacSign.Leo;
            if ((month == 8 && day >= 23) || (month == 9 && day <= 22))
                return ZodiacSign.Virgo;
            if ((month == 9 && day >= 23) || (month == 10 && day <= 22))
                return ZodiacSign.Libra;
            if ((month == 10 && day >= 23) || (month == 11 && day <= 21))
                return ZodiacSign.Scorpio;
            if ((month == 11 && day >= 22) || (month == 12 && day <= 21))
                return ZodiacSign.Sagittarius;
            if ((month == 12 && day >= 22) || (month == 1 && day <= 19))
                return ZodiacSign.Capricorn;
            if ((month == 1 && day >= 20) || (month == 2 && day <= 18))
                return ZodiacSign.Aquarius;
            return ZodiacSign.Pisces;
        }
    }
}
