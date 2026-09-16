using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    internal sealed class PetWeatherSource : IDisposable
    {
        internal static readonly TimeSpan ForecastRequestTimeout =
            TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan GeocodingRequestTimeout =
            TimeSpan.FromSeconds(8);
        private readonly object _gate = new object();
        private readonly HttpClient _httpClient;
        private readonly OpenMeteoGeocodingClient _geocoding;
        private readonly OpenMeteoForecastClient _forecast;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly WeatherForecastCache _cache;
        private int _forecastRequestCount;
        private bool _disposed;

        internal PetWeatherSource() : this(new HttpClient(),
            delegate { return DateTimeOffset.UtcNow; })
        {
        }

        internal PetWeatherSource(HttpClient httpClient,
            Func<DateTimeOffset> utcNow)
        {
            _httpClient = httpClient ??
                throw new ArgumentNullException(nameof(httpClient));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            // Individual request deadlines keep the forecast's quiet 3s
            // fallback independent from the user's slower city search.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;
            Version version = typeof(PetWeatherSource).Assembly
                .GetName().Version;
            string userAgent = "PennyPet/" + (version == null
                ? "1.0.0"
                : version.ToString(3));
            if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
                userAgent))
                throw new InvalidOperationException("Invalid user agent.");
            _geocoding = new OpenMeteoGeocodingClient(_httpClient);
            _forecast = new OpenMeteoForecastClient(_httpClient);
            _cache = new WeatherForecastCache(FetchForecastAsync, _utcNow);
        }

        internal Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(
            string query)
        {
            return SearchLocationsAsync(query, CancellationToken.None);
        }

        internal async Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(
            string query, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using (CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken))
            {
                timeout.CancelAfter(GeocodingRequestTimeout);
                try
                {
                    return await _geocoding.SearchAsync(query, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new TimeoutException(
                        "Weather city search exceeded its deadline.");
                }
            }
        }

        internal Task<WeatherForecastWindow> GetForecastAsync(
            WeatherLocation location)
        {
            ThrowIfDisposed();
            return _cache.GetAsync(location);
        }

        internal void InvalidateCache() { _cache.Invalidate(); }

        internal int ForecastRequestCountForTest
        {
            get { lock (_gate) return _forecastRequestCount; }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _httpClient.Dispose();
        }

        private async Task<WeatherForecastWindow> FetchForecastAsync(WeatherLocation location)
        {
            try
            {
                lock (_gate) _forecastRequestCount++;
                using (CancellationTokenSource timeout = new CancellationTokenSource())
                {
                    timeout.CancelAfter(ForecastRequestTimeout);
                    return await _forecast.FetchAsync(location, _utcNow(), timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("weather-forecast", error);
                return null;
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_gate)
                if (_disposed) throw new ObjectDisposedException(
                    nameof(PetWeatherSource));
        }
    }
}
