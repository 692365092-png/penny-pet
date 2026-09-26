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
        private readonly Func<DailyContentPreferencesSnapshot, DateTimeOffset, string, bool> _showDailyGreeting;
        private readonly Action<DateTimeOffset> _recordBriefingDate;
        private int _generation;
        private bool _attemptInFlight;

        internal PetDailyContentCoordinator(Func<DailyContentPreferencesSnapshot> preferences,
            Func<WeatherLocation, Task<WeatherForecastWindow>> weatherForecast,
            Func<DailyContentPreferencesSnapshot, DateTimeOffset, string, bool> showDailyGreeting,
            Action<DateTimeOffset> recordBriefingDate)
        {
            _preferences = preferences;
            _weatherForecast = weatherForecast;
            _showDailyGreeting = showDailyGreeting;
            _recordBriefingDate = recordBriefingDate;
        }

        // Called on the owning UI context. Old completions cannot clear or
        // publish a newer attempt after settings changes or foreground takeover.
        internal void Invalidate()
        {
            ++_generation;
            _attemptInFlight = false;
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
            if (_attemptInFlight) return true;
            _attemptInFlight = true;
            int generation = ++_generation;
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
                    WeatherForecastWindow forecast = null;
                    try { forecast = await _weatherForecast(location); }
                    catch (Exception error)
                    {
                        if (generation == _generation)
                            ApplicationDiagnostics.ReportNonFatal("daily-weather", error);
                    }
                    if (generation != _generation) return true;
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
                if (generation != _generation) return true;
                if (!_showDailyGreeting(preferences, localNow, text)) return false;
                _recordBriefingDate(localNow);
                return true;
            }
            finally
            {
                if (generation == _generation) _attemptInFlight = false;
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
