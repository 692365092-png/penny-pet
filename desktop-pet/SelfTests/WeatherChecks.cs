using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private sealed class WeatherCheckResult
        {
            internal bool ForecastFixtureParsingOk;
            internal bool ForecastRequestShapeOk;
            internal bool GeocodingRequestAndSelectionOk;
            internal bool NoStartupRequestOk;
            internal bool SameDayCacheAndInFlightOk;
            internal bool BoundedCacheInvalidationOk;
            internal bool FailureCooldownOk;
            internal bool MeaningAndWordingOk;
            internal bool DailyCoordinatorWeatherOk;
            internal bool DailyCoordinatorFailureFallbackOk;
            internal bool DailyCoordinatorInFlightOk;
            internal bool DailyCoordinatorPreferenceSnapshotOk;
            internal bool ConversationOwnershipOk;
            internal bool RejectedBubbleReusesForecastOk;
            internal bool LocationDialogLayoutOk;
        }

        private sealed class WeatherFixtureHandler : HttpMessageHandler
        {
            private readonly string _forecastJson;
            private readonly bool _failForecast;
            private TaskCompletionSource<HttpResponseMessage> _heldForecast;

            internal void HoldNextForecast()
            {
                _heldForecast = new TaskCompletionSource<HttpResponseMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal void ReleaseForecast()
            {
                var held = Interlocked.Exchange(ref _heldForecast, null);
                if (held != null) held.SetResult(JsonResponse(_forecastJson));
            }

            internal WeatherFixtureHandler(string forecastJson,
                bool failForecast)
            {
                _forecastJson = forecastJson;
                _failForecast = failForecast;
            }

            internal int RequestCount;
            internal int ForecastCount;
            internal int GeocodingCount;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref RequestCount);
                if (request.RequestUri.Host.StartsWith("geocoding-api.",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref GeocodingCount);
                    return Task.FromResult(JsonResponse(
                        "{\"results\":[{\"name\":\"武汉\"," +
                        "\"admin1\":\"湖北\",\"country\":\"中国\"," +
                        "\"latitude\":30.5928,\"longitude\":114.3055," +
                        "\"timezone\":\"Asia/Shanghai\"}]}"));
                }
                Interlocked.Increment(ref ForecastCount);
                if (_failForecast)
                    return Task.FromResult(new HttpResponseMessage(
                        System.Net.HttpStatusCode.ServiceUnavailable));
                var held = Volatile.Read(ref _heldForecast);
                return held == null ? Task.FromResult(JsonResponse(_forecastJson)) : held.Task;
            }

            private static HttpResponseMessage JsonResponse(string json)
            {
                HttpResponseMessage response = new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK);
                response.Content = new StringContent(json, Encoding.UTF8,
                    "application/json");
                return response;
            }
        }

        private static WeatherCheckResult RunWeatherChecks()
        {
            WeatherCheckResult result = new WeatherCheckResult();
            string fixture = ReadWeatherFixture("weather-rain-later.json");

            DateTime date = new DateTime(2026, 9, 1);
            DateTimeOffset cityNow = new DateTimeOffset(2026, 9, 1, 8, 0, 0,
                TimeSpan.FromHours(8));
            WeatherForecastWindow parsed = new OpenMeteoForecastParser()
                .Parse(fixture, cityNow);
            result.ForecastFixtureParsingOk = parsed.Yesterday != null &&
                parsed.Today != null && parsed.Tomorrow != null &&
                parsed.Today.MinimumTemperatureC == 22D &&
                parsed.Today.MaximumTemperatureC == 27D &&
                Math.Abs(parsed.Today.TotalPrecipitationMm - 4.2D) < 0.001D &&
                parsed.Today.MaximumPrecipitationProbability == 85D &&
                parsed.Today.FirstLikelyPrecipitationHour == 16 &&
                parsed.Today.LastLikelyPrecipitationHour == 16 &&
                parsed.Today.LikelyPrecipitationHours == 1;

            WeatherLocation location;
            WeatherLocation.TryCreate("武汉", "湖北", "中国", 30.5928,
                114.3055, "Asia/Shanghai", out location);
            using (PetWeatherSource dialogSource = new PetWeatherSource())
            using (WeatherLocationDialog dialog =
                new WeatherLocationDialog(dialogSource))
                result.LocationDialogLayoutOk =
                    dialog.UsesCompactFormattedResultsForTest(location);
            string forecastUrl = Uri.UnescapeDataString(
                OpenMeteoForecastClient.BuildUri(location).AbsoluteUri);
            result.ForecastRequestShapeOk = forecastUrl.StartsWith(
                    OpenMeteoForecastClient.Endpoint,
                    StringComparison.Ordinal) &&
                forecastUrl.Contains("hourly=" + String.Join(",",
                    OpenMeteoForecastClient.HourlyVariables)) &&
                OpenMeteoForecastClient.HourlyVariables.Length == 8 &&
                forecastUrl.Contains("past_days=1") &&
                forecastUrl.Contains("forecast_days=2") &&
                forecastUrl.Contains("timezone=Asia/Shanghai") &&
                forecastUrl.Contains("temperature_unit=celsius") &&
                forecastUrl.Contains("wind_speed_unit=kmh") &&
                forecastUrl.Contains("precipitation_unit=mm") &&
                forecastUrl.IndexOf("apikey", StringComparison.OrdinalIgnoreCase)
                    < 0;

            DateTimeOffset handlerClock = new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                TimeSpan.Zero);
            WeatherFixtureHandler handler = new WeatherFixtureHandler(
                fixture, false);
            using (PetWeatherSource source = new PetWeatherSource(
                new HttpClient(handler), delegate { return handlerClock; }))
            {
                result.NoStartupRequestOk = handler.RequestCount == 0;
                IReadOnlyList<WeatherLocation> locations = source
                    .SearchLocationsAsync("武汉").GetAwaiter().GetResult();
                result.GeocodingRequestAndSelectionOk =
                    handler.GeocodingCount == 1 && locations.Count == 1 &&
                    locations[0].DisplayName == "武汉 · 湖北 · 中国" &&
                    locations[0].Timezone == "Asia/Shanghai" &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "count=5") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "language=zh") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.Contains(
                        "format=json") &&
                    OpenMeteoGeocodingClient.BuildUri("武汉").Query.IndexOf(
                        "apikey", StringComparison.OrdinalIgnoreCase) < 0;
                Task<WeatherForecastWindow> first, concurrent;
                handler.HoldNextForecast();
                try
                {
                    first = source.GetForecastAsync(location);
                    concurrent = source.GetForecastAsync(location);
                }
                finally { handler.ReleaseForecast(); }
                WeatherForecastWindow firstValue = first.GetAwaiter()
                    .GetResult();
                WeatherForecastWindow cached = source.GetForecastAsync(
                    location).GetAwaiter().GetResult();
                result.SameDayCacheAndInFlightOk =
                    Object.ReferenceEquals(first, concurrent) &&
                    Object.ReferenceEquals(firstValue, cached) &&
                    handler.ForecastCount == 1 &&
                    source.ForecastRequestCountForTest == 1;
                handlerClock = new DateTimeOffset(2026, 9, 1, 16, 0, 0,
                    TimeSpan.Zero);
                source.GetForecastAsync(location).GetAwaiter().GetResult();
                bool refetchedAfterCityDayChange = handler.ForecastCount == 2;
                source.GetForecastAsync(location).GetAwaiter().GetResult();
                bool reusedAfterCityDayChange = handler.ForecastCount == 2;
                source.InvalidateCache();
                source.GetForecastAsync(location).GetAwaiter().GetResult();
                result.BoundedCacheInvalidationOk =
                    refetchedAfterCityDayChange &&
                    reusedAfterCityDayChange && handler.ForecastCount == 3;
            }

            WeatherFixtureHandler failing = new WeatherFixtureHandler(
                fixture, true);
            using (PetWeatherSource source = new PetWeatherSource(
                new HttpClient(failing), delegate
                {
                    return new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                        TimeSpan.Zero);
                }))
            {
                WeatherForecastWindow failed = source.GetForecastAsync(
                    location).GetAwaiter().GetResult();
                WeatherForecastWindow cooledDown = source.GetForecastAsync(
                    location).GetAwaiter().GetResult();
                result.FailureCooldownOk = failed == null &&
                    cooledDown == null && failing.ForecastCount == 1;
            }

            string[] fixtureNames =
            {
                "weather-clear.json", "weather-rain-later.json",
                "weather-cooling.json", "weather-rain-cooling.json",
                "weather-windy.json", "weather-snow.json"
            };
            WeatherMeaning?[] expectedMeanings =
            {
                null, WeatherMeaning.RainLater, WeatherMeaning.Cooling,
                WeatherMeaning.RainAndCooling, WeatherMeaning.Windy,
                WeatherMeaning.Snow
            };
            bool meaningsOk = true;
            for (int i = 0; i < fixtureNames.Length; i++)
            {
                WeatherForecastWindow window = new OpenMeteoForecastParser()
                    .Parse(ReadWeatherFixture(fixtureNames[i]), cityNow);
                meaningsOk &= WeatherMeaningRules.Select(window) ==
                    expectedMeanings[i];
            }
            foreach (WeatherMeaning meaning in Enum.GetValues(
                typeof(WeatherMeaning)))
            {
                HashSet<string> variants = new HashSet<string>();
                for (int day = 0; day < 365; day++)
                {
                    WeatherDailySelection selected =
                        WeatherWordingCatalog.Select(meaning,
                            date.AddDays(day), location.StableKey);
                    variants.Add(selected.Text);
                    meaningsOk &= selected.Text == WeatherWordingCatalog
                        .Select(meaning, date.AddDays(day),
                            location.StableKey).Text &&
                        selected.Text.Length <= 60;
                }
                int required = meaning == WeatherMeaning.RainLater ||
                    meaning == WeatherMeaning.Cooling ||
                    meaning == WeatherMeaning.Windy ||
                    meaning == WeatherMeaning.Hot ? 5 : 3;
                meaningsOk &= variants.Count >= required;
            }
            result.MeaningAndWordingOk = meaningsOk;

            WeatherForecastWindow rainLater = new OpenMeteoForecastParser()
                .Parse(fixture, cityNow);
            string lastDate = String.Empty;
            string shownText = null;
            int dailyForecastCalls = 0;
            int dailyShowCount = 0;
            PetDailyContentCoordinator daily =
                CreateDailyCoordinator(
                    delegate { return lastDate; },
                    delegate { return false; }, delegate { return true; },
                    delegate { return false; },
                    delegate { return true; },
                    delegate { return true; },
                    delegate { return location; },
                    delegate
                    {
                        dailyForecastCalls++;
                        return Task.FromResult(rainLater);
                    },
                    delegate { return ZodiacSign.None; },
                    delegate { return 0; },
                    delegate { return 0; },
                    delegate(string text)
                    {
                        dailyShowCount++;
                        shownText = text;
                        return true;
                    },
                    delegate(string value) { lastDate = value; });
            bool weatherShown = RunDaily(daily,
                new DateTimeOffset(date, TimeSpan.FromHours(8)));
            string expectedWeather = WeatherWordingCatalog.Select(
                WeatherMeaning.RainLater, date, location.StableKey).Text;
            result.DailyCoordinatorWeatherOk = weatherShown &&
                dailyForecastCalls == 1 && dailyShowCount == 1 &&
                shownText.Contains(expectedWeather) && lastDate == "20260901";

            lastDate = String.Empty;
            shownText = null;
            dailyForecastCalls = 0;
            PetDailyContentCoordinator unavailable =
                CreateDailyCoordinator(
                    delegate { return lastDate; },
                    delegate { return false; }, delegate { return true; },
                    delegate { return false; },
                    delegate { return true; },
                    delegate { return true; },
                    delegate { return location; },
                    delegate
                    {
                        dailyForecastCalls++;
                        return Task.FromResult<WeatherForecastWindow>(null);
                    },
                    delegate { return ZodiacSign.None; },
                    delegate { return 0; },
                    delegate { return 0; },
                    delegate(string text)
                    {
                        shownText = text;
                        return true;
                    },
                    delegate(string value) { lastDate = value; });
            bool fallbackShown = RunDaily(unavailable,
                new DateTimeOffset(date, TimeSpan.FromHours(8)));
            result.DailyCoordinatorFailureFallbackOk = fallbackShown &&
                dailyForecastCalls == 1 &&
                !String.IsNullOrWhiteSpace(shownText) &&
                !shownText.Contains(expectedWeather) &&
                lastDate == "20260901";

            result.ConversationOwnershipOk = RunConversationRuntimeChecks();

            result.DailyCoordinatorInFlightOk = Task.Run(delegate
            {
                string pendingDate = String.Empty;
                int pendingFetches = 0;
                int pendingShows = 0;
                TaskCompletionSource<WeatherForecastWindow> pending =
                    new TaskCompletionSource<WeatherForecastWindow>();
                PetDailyContentCoordinator pendingDaily =
                    CreateDailyCoordinator(
                        delegate { return pendingDate; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return true; },
                        delegate { return location; },
                        delegate
                        {
                            pendingFetches++;
                            return pending.Task;
                        },
                        delegate { return ZodiacSign.None; },
                        delegate { return 0; },
                        delegate { return 0; },
                        delegate { pendingShows++; return true; },
                        delegate(string value) { pendingDate = value; });
                DateTimeOffset pendingNow = new DateTimeOffset(date,
                    TimeSpan.FromHours(8));
                Task<bool> firstAttempt = pendingDaily.HandlePetPokedAsync(
                    pendingNow);
                Task<bool> secondAttempt = pendingDaily.HandlePetPokedAsync(
                    pendingNow.AddMinutes(1));
                bool secondConsumed = secondAttempt.GetAwaiter().GetResult();
                pending.SetResult(rainLater);
                bool firstShown = firstAttempt.GetAwaiter().GetResult();
                return secondConsumed && firstShown && pendingFetches == 1 &&
                    pendingShows == 1 && pendingDate == "20260901";
            }).GetAwaiter().GetResult();

            result.DailyCoordinatorPreferenceSnapshotOk = Task.Run(delegate
            {
                int birthdayMonth = date.Month;
                int birthdayDay = date.Day;
                int birthdayReads = 0;
                string snapshotText = null;
                TaskCompletionSource<WeatherForecastWindow> pending =
                    new TaskCompletionSource<WeatherForecastWindow>();
                PetDailyContentCoordinator snapshotDaily =
                    CreateDailyCoordinator(
                        delegate { return String.Empty; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return false; },
                        delegate { return false; },
                        delegate { return true; },
                        delegate { return location; },
                        delegate { return pending.Task; },
                        delegate { return ZodiacSign.None; },
                        delegate
                        {
                            birthdayReads++;
                            return birthdayMonth;
                        },
                        delegate
                        {
                            birthdayReads++;
                            return birthdayDay;
                        },
                        delegate(string text)
                        {
                            snapshotText = text;
                            return true;
                        },
                        delegate { });
                DateTimeOffset snapshotNow = new DateTimeOffset(date,
                    TimeSpan.FromHours(8));
                Task<bool> attempt = snapshotDaily.HandlePetPokedAsync(
                    snapshotNow);
                birthdayMonth = 1;
                birthdayDay = 1;
                pending.SetResult(rainLater);
                bool shown = attempt.GetAwaiter().GetResult();
                DailyLineEntry expected = PetBirthdayWordingCatalog.Select(
                    PetBirthdayKind.User, date);
                return shown && birthdayReads == 2 && expected != null &&
                    snapshotText != null && snapshotText.Contains(expected.Text);
            }).GetAwaiter().GetResult();

            result.RejectedBubbleReusesForecastOk = Task.Run(delegate
            {
                WeatherFixtureHandler retryHandler =
                    new WeatherFixtureHandler(fixture, false);
                using (PetWeatherSource retrySource = new PetWeatherSource(
                    new HttpClient(retryHandler), delegate
                    {
                        return new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                            TimeSpan.Zero);
                    }))
                {
                    string retryDate = String.Empty;
                    bool accept = false;
                    int attempts = 0;
                    PetDailyContentCoordinator retryDaily =
                        CreateDailyCoordinator(
                            delegate { return retryDate; },
                            delegate { return false; },
                            delegate { return true; },
                            delegate { return false; },
                            delegate { return true; },
                            delegate { return true; },
                            delegate { return location; },
                            delegate(WeatherLocation target)
                            {
                                return retrySource.GetForecastAsync(target);
                            },
                            delegate { return ZodiacSign.None; },
                            delegate { return 0; },
                            delegate { return 0; },
                            delegate { attempts++; return accept; },
                            delegate(string value) { retryDate = value; });
                    DateTimeOffset retryNow = new DateTimeOffset(date,
                        TimeSpan.FromHours(8));
                    bool rejected = !RunDaily(retryDaily, retryNow);
                    accept = true;
                    bool accepted = RunDaily(retryDaily,
                        retryNow.AddMinutes(1));
                    return rejected && accepted && attempts == 2 &&
                        retryHandler.ForecastCount == 1 &&
                        retryDate == "20260901";
                }
            }).GetAwaiter().GetResult();
            return result;
        }

        private static string ReadWeatherFixture(string fileName)
        {
            string resourceName = "PennyPet.Tests.Fixtures." + fileName;
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "Missing weather fixture: " + resourceName);
                using (StreamReader reader = new StreamReader(stream,
                    Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
    }
}
