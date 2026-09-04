namespace PennyPet
{
    // One immutable view of every preference used by a daily-content attempt.
    internal sealed class DailyContentPreferencesSnapshot
    {
        internal DailyContentPreferencesSnapshot(bool silentMode,
            bool dailyContentEnabled, bool solarTermEnabled,
            bool almanacEnabled, bool weatherEnabled,
            WeatherLocation weatherLocation, ZodiacSign zodiacSign,
            int birthdayMonth, int birthdayDay, string lastBriefingDate)
        {
            SilentMode = silentMode;
            DailyContentEnabled = dailyContentEnabled;
            SolarTermEnabled = solarTermEnabled;
            AlmanacEnabled = almanacEnabled;
            WeatherEnabled = weatherEnabled;
            WeatherLocation = weatherLocation;
            ZodiacSign = zodiacSign;
            BirthdayMonth = birthdayMonth;
            BirthdayDay = birthdayDay;
            LastBriefingDate = lastBriefingDate;
        }

        internal bool SilentMode { get; private set; }
        internal bool DailyContentEnabled { get; private set; }
        internal bool SolarTermEnabled { get; private set; }
        internal bool AlmanacEnabled { get; private set; }
        internal bool WeatherEnabled { get; private set; }
        internal WeatherLocation WeatherLocation { get; private set; }
        internal ZodiacSign ZodiacSign { get; private set; }
        internal int BirthdayMonth { get; private set; }
        internal int BirthdayDay { get; private set; }
        internal string LastBriefingDate { get; private set; }
    }
}
