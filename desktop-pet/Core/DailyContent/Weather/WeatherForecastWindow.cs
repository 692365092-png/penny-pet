using System;

namespace PennyPet
{
    internal sealed class WeatherForecastWindow
    {
        internal WeatherForecastWindow(WeatherDaySummary yesterday,
            WeatherDaySummary today, WeatherDaySummary tomorrow)
        {
            Yesterday = yesterday;
            Today = today;
            Tomorrow = tomorrow;
        }

        internal WeatherDaySummary Yesterday { get; private set; }
        internal WeatherDaySummary Today { get; private set; }
        internal WeatherDaySummary Tomorrow { get; private set; }

        internal WeatherDaySummary Find(DateTime localDate)
        {
            DateTime date = localDate.Date;
            if (Yesterday != null && Yesterday.Date == date) return Yesterday;
            if (Today != null && Today.Date == date) return Today;
            if (Tomorrow != null && Tomorrow.Date == date) return Tomorrow;
            return null;
        }
    }
}
