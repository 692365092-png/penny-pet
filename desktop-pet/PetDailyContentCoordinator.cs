using System;
using System.Threading.Tasks;

namespace PennyPet
{
    // Hosts the poke-triggered daily interaction without owning settings or UI.
    internal sealed class PetDailyContentCoordinator
    {
        private readonly Func<DailyContentPreferencesSnapshot> _preferences;
        private readonly Func<WeatherLocation,
            Task<WeatherForecastWindow>> _weatherForecast;
        private readonly Func<string, bool> _showDailyGreeting;
        private readonly Action<string> _recordBriefingDate;
        private readonly object _attemptGate = new object();
        private bool _attemptInFlight;

        internal PetDailyContentCoordinator(Func<string> lastBriefingDate,
            Func<bool> silentMode, Func<bool> dailyContentEnabled,
            Func<bool> solarTermEnabled, Func<bool> almanacEnabled,
            Func<bool> weatherEnabled,
            Func<WeatherLocation> weatherLocation,
            Func<WeatherLocation, Task<WeatherForecastWindow>>
                weatherForecast,
            Func<ZodiacSign> zodiacSign,
            Func<int> userBirthdayMonth,
            Func<int> userBirthdayDay,
            Func<string, bool> showDailyGreeting,
            Action<string> recordBriefingDate)
        {
            if (lastBriefingDate == null)
                throw new ArgumentNullException("lastBriefingDate");
            if (silentMode == null)
                throw new ArgumentNullException("silentMode");
            if (dailyContentEnabled == null)
                throw new ArgumentNullException("dailyContentEnabled");
            if (solarTermEnabled == null)
                throw new ArgumentNullException("solarTermEnabled");
            if (almanacEnabled == null)
                throw new ArgumentNullException("almanacEnabled");
            if (weatherEnabled == null)
                throw new ArgumentNullException("weatherEnabled");
            if (weatherLocation == null)
                throw new ArgumentNullException("weatherLocation");
            if (zodiacSign == null)
                throw new ArgumentNullException("zodiacSign");
            if (userBirthdayMonth == null)
                throw new ArgumentNullException("userBirthdayMonth");
            if (userBirthdayDay == null)
                throw new ArgumentNullException("userBirthdayDay");
            _preferences = delegate
            {
                return new DailyContentPreferencesSnapshot(silentMode(),
                    dailyContentEnabled(), solarTermEnabled(),
                    almanacEnabled(), weatherEnabled(), weatherLocation(),
                    zodiacSign(), userBirthdayMonth(), userBirthdayDay(),
                    lastBriefingDate());
            };
            _weatherForecast = weatherForecast ??
                throw new ArgumentNullException("weatherForecast");
            _showDailyGreeting = showDailyGreeting ??
                throw new ArgumentNullException("showDailyGreeting");
            _recordBriefingDate = recordBriefingDate ??
                throw new ArgumentNullException("recordBriefingDate");
        }

        // true means this poke was handled/claimed by DailyContent,
        // including an already in-flight daily attempt.
        internal async Task<bool> HandlePetPokedAsync(
            DateTimeOffset localNow)
        {
            DailyContentPreferencesSnapshot preferences = _preferences();
            if (!preferences.DailyContentEnabled ||
                PetMessagePolicy.ShouldSuppress(PetMessageKind.DailyGreeting,
                preferences.SilentMode) || !DailyContentRules.ShouldShow(
                    preferences.LastBriefingDate, localNow)) return false;
            lock (_attemptGate)
            {
                if (_attemptInFlight) return true;
                _attemptInFlight = true;
            }
            try
            {
                DayPart dayPart = DailyContentRules.ResolveDayPart(localNow);
                SolarTermInfo? solarTerm = preferences.SolarTermEnabled
                    ? SolarTermCalculator.FindForLocalDate(localNow)
                    : (SolarTermInfo?)null;
                AlmanacDailySelection almanac = null;
                if (preferences.AlmanacEnabled)
                {
                    AlmanacDayInfo almanacDay =
                        AlmanacCalculator.Calculate(localNow);
                    if (almanacDay != null)
                        almanac = AlmanacDailySelector.Select(
                            almanacDay, localNow);
                }
                WeatherDailySelection weather = null;
                WeatherLocation location = preferences.WeatherEnabled
                    ? preferences.WeatherLocation : null;
                if (location != null)
                {
                    WeatherForecastWindow forecast = await _weatherForecast(
                        location);
                    WeatherMeaning? meaning = WeatherMeaningRules.Select(
                        forecast);
                    if (meaning.HasValue)
                        weather = WeatherWordingCatalog.Select(
                            meaning.Value, localNow.Date,
                            location.StableKey);
                }
                DailyLineEntry curatedLine = CuratedDailyLineSelector.Select(
                    localNow);
                DailyLineEntry zodiacLine = ZodiacDailySelector.Select(
                    preferences.ZodiacSign, localNow);
                PetBirthdayKind birthdayKind = PetBirthdayRule.Resolve(
                    localNow.Month, localNow.Day,
                    preferences.BirthdayMonth, preferences.BirthdayDay);
                DailyLineEntry birthdayLine =
                    PetBirthdayWordingCatalog.Select(birthdayKind,
                        localNow.Date);
                DailyBriefingContent content = new DailyBriefingContent(
                    solarTerm, weather, almanac, curatedLine, zodiacLine,
                    birthdayLine, birthdayKind);
                string text = DailyBriefingComposer.Compose(dayPart,
                    localNow.Date, content);
                if (!_showDailyGreeting(text)) return false;
                _recordBriefingDate(DailyContentRules.DateKey(localNow));
                return true;
            }
            finally
            {
                lock (_attemptGate) _attemptInFlight = false;
            }
        }

        internal bool IsOpeningEligible(DateTimeOffset localNow)
        {
            DailyContentPreferencesSnapshot preferences = _preferences();
            return preferences.DailyContentEnabled &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.DailyGreeting, preferences.SilentMode) &&
                DailyContentRules.ShouldShow(preferences.LastBriefingDate,
                    localNow);
        }
    }
}
