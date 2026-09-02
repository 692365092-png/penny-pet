using System;
using System.Threading.Tasks;

namespace PennyPet
{
    // Hosts the poke-triggered daily interaction without owning settings or UI.
    internal sealed class PetDailyContentCoordinator
    {
        private readonly Func<string> _lastBriefingDate;
        private readonly Func<bool> _silentMode;
        private readonly Func<bool> _dailyContentEnabled;
        private readonly Func<bool> _solarTermEnabled;
        private readonly Func<bool> _almanacEnabled;
        private readonly Func<bool> _weatherEnabled;
        private readonly Func<WeatherLocation> _weatherLocation;
        private readonly Func<WeatherLocation,
            Task<WeatherForecastWindow>> _weatherForecast;
        private readonly Func<ZodiacSign> _zodiacSign;
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
            Func<string, bool> showDailyGreeting,
            Action<string> recordBriefingDate)
        {
            _lastBriefingDate = lastBriefingDate ??
                throw new ArgumentNullException("lastBriefingDate");
            _silentMode = silentMode ??
                throw new ArgumentNullException("silentMode");
            _dailyContentEnabled = dailyContentEnabled ??
                throw new ArgumentNullException("dailyContentEnabled");
            _solarTermEnabled = solarTermEnabled ??
                throw new ArgumentNullException("solarTermEnabled");
            _almanacEnabled = almanacEnabled ??
                throw new ArgumentNullException("almanacEnabled");
            _weatherEnabled = weatherEnabled ??
                throw new ArgumentNullException("weatherEnabled");
            _weatherLocation = weatherLocation ??
                throw new ArgumentNullException("weatherLocation");
            _weatherForecast = weatherForecast ??
                throw new ArgumentNullException("weatherForecast");
            _zodiacSign = zodiacSign ??
                throw new ArgumentNullException("zodiacSign");
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
            if (!_dailyContentEnabled() ||
                PetMessagePolicy.ShouldSuppress(PetMessageKind.DailyGreeting,
                _silentMode()) || !DailyContentRules.ShouldShow(
                    _lastBriefingDate(), localNow)) return false;
            lock (_attemptGate)
            {
                if (_attemptInFlight) return true;
                _attemptInFlight = true;
            }
            try
            {
                DayPart dayPart = DailyContentRules.ResolveDayPart(localNow);
                SolarTermInfo? solarTerm = _solarTermEnabled()
                    ? SolarTermCalculator.FindForLocalDate(localNow)
                    : (SolarTermInfo?)null;
                AlmanacDailySelection almanac = null;
                if (_almanacEnabled())
                {
                    AlmanacDayInfo almanacDay =
                        AlmanacCalculator.Calculate(localNow);
                    if (almanacDay != null)
                        almanac = AlmanacDailySelector.Select(
                            almanacDay, localNow);
                }
                WeatherDailySelection weather = null;
                WeatherLocation location = _weatherEnabled()
                    ? _weatherLocation() : null;
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
                    _zodiacSign(), localNow);
                DailyBriefingContent content = new DailyBriefingContent(
                    solarTerm, weather, almanac, curatedLine, zodiacLine);
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
            return _dailyContentEnabled() &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.DailyGreeting, _silentMode()) &&
                DailyContentRules.ShouldShow(_lastBriefingDate(), localNow);
        }
    }
}
