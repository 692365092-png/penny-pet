using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    internal sealed class OpenMeteoForecastClient
    {
        internal const string Endpoint =
            "https://api.open-meteo.com/v1/forecast";
        internal static readonly string[] HourlyVariables =
        {
            "temperature_2m",
            "apparent_temperature",
            "precipitation_probability",
            "precipitation",
            "snowfall",
            "weather_code",
            "wind_speed_10m",
            "wind_gusts_10m"
        };

        private readonly HttpClient _httpClient;
        private readonly OpenMeteoForecastParser _parser =
            new OpenMeteoForecastParser();

        internal OpenMeteoForecastClient(HttpClient httpClient)
        {
            _httpClient = httpClient ??
                throw new ArgumentNullException(nameof(httpClient));
        }

        internal async Task<WeatherForecastWindow> FetchAsync(
            WeatherLocation location, DateTimeOffset utcNow)
        {
            return await FetchAsync(location, utcNow,
                CancellationToken.None).ConfigureAwait(false);
        }

        internal async Task<WeatherForecastWindow> FetchAsync(
            WeatherLocation location, DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            if (location == null)
                throw new ArgumentNullException(nameof(location));
            using (HttpResponseMessage response = await _httpClient.GetAsync(
                BuildUri(location), HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                return _parser.Parse(json, utcNow);
            }
        }

        internal static Uri BuildUri(WeatherLocation location)
        {
            if (location == null)
                throw new ArgumentNullException(nameof(location));
            return new Uri(Endpoint + "?latitude=" +
                location.Latitude.ToString("R", CultureInfo.InvariantCulture) +
                "&longitude=" + location.Longitude.ToString("R",
                    CultureInfo.InvariantCulture) +
                "&hourly=" + Uri.EscapeDataString(String.Join(",",
                    HourlyVariables)) +
                "&past_days=1&forecast_days=2&timezone=" +
                Uri.EscapeDataString(location.Timezone) +
                "&temperature_unit=celsius&wind_speed_unit=kmh" +
                "&precipitation_unit=mm");
        }
    }
}
