using System;
using Lunar;

namespace PennyPet
{
    internal static class AlmanacCalculator
    {
        internal const int Sect = 1;

        internal static AlmanacDayInfo Calculate(DateTimeOffset localNow)
        {
            try
            {
                Solar solar = Solar.FromYmdHms(localNow.Year,
                    localNow.Month, localNow.Day, 12, 0, 0);
                Lunar.Lunar lunar = solar.Lunar;
                return new AlmanacDayInfo(localNow.Year, localNow.Month,
                    localNow.Day, lunar.GetDayYi(1),
                    lunar.GetDayJi(1));
            }
            catch
            {
                return null;
            }
        }
    }
}
