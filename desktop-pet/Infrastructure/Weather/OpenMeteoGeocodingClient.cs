using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PennyPet
{
    internal sealed class OpenMeteoGeocodingClient
    {
        internal const string Endpoint =
            "https://geocoding-api.open-meteo.com/v1/search";
        private readonly HttpClient _httpClient;

        internal OpenMeteoGeocodingClient(HttpClient httpClient)
        {
            _httpClient = httpClient ??
                throw new ArgumentNullException(nameof(httpClient));
        }

        internal async Task<IReadOnlyList<WeatherLocation>> SearchAsync(
            string query)
        {
            return await SearchAsync(query, CancellationToken.None)
                .ConfigureAwait(false);
        }

        internal async Task<IReadOnlyList<WeatherLocation>> SearchAsync(
            string query, CancellationToken cancellationToken)
        {
            Uri uri = BuildUri(query);
            using (HttpResponseMessage response = await _httpClient.GetAsync(
                uri, HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                return ParseResults(json);
            }
        }

        private static IReadOnlyList<WeatherLocation> ParseResults(
            string json)
        {
            GeocodingResponse response = new JavaScriptSerializer()
                .Deserialize<GeocodingResponse>(json);
            List<WeatherLocation> results = new List<WeatherLocation>();
            if (response == null || response.results == null) return results;
            foreach (GeocodingResult item in response.results)
            {
                WeatherLocation location;
                if (item != null && WeatherLocation.TryCreate(item.name,
                    item.admin1, item.country, item.latitude, item.longitude,
                    item.timezone, out location))
                    results.Add(location);
            }
            return results;
        }

        internal static Uri BuildUri(string query)
        {
            string normalized = (query ?? String.Empty).Trim();
            if (normalized.Length < 2)
                throw new ArgumentException("City query is too short.",
                    nameof(query));
            return new Uri(Endpoint + "?name=" +
                Uri.EscapeDataString(normalized) +
                "&count=5&language=zh&format=json");
        }

        private sealed class GeocodingResponse
        {
            public GeocodingResult[] results { get; set; }
        }

        private sealed class GeocodingResult
        {
            public string name { get; set; }
            public string admin1 { get; set; }
            public string country { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public string timezone { get; set; }
        }
    }
}
