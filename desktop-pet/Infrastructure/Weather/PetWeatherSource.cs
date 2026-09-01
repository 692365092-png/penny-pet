using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace PennyPet
{
    internal sealed class PetWeatherSource : IDisposable
    {
        private static readonly TimeSpan FailureCooldown =
            TimeSpan.FromMinutes(15);
        private readonly object _gate = new object();
        private readonly HttpClient _httpClient;
        private readonly OpenMeteoGeocodingClient _geocoding;
        private readonly OpenMeteoForecastClient _forecast;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Dictionary<string, WeatherForecastWindow> _cache =
            new Dictionary<string, WeatherForecastWindow>(
                StringComparer.Ordinal);
        private readonly Queue<string> _cacheOrder = new Queue<string>();
        private string _inFlightKey;
        private Task<WeatherForecastWindow> _inFlight;
        private string _failedKey;
        private DateTimeOffset _retryAfterUtc;
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
            _httpClient.Timeout = TimeSpan.FromSeconds(3);
            if (!_httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "PennyPet/1.1"))
                throw new InvalidOperationException("Invalid user agent.");
            _geocoding = new OpenMeteoGeocodingClient(_httpClient);
            _forecast = new OpenMeteoForecastClient(_httpClient);
        }

        internal Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(
            string query)
        {
            ThrowIfDisposed();
            return _geocoding.SearchAsync(query);
        }

        internal Task<WeatherForecastWindow> GetForecastAsync(
            WeatherLocation location, DateTime localDate)
        {
            if (location == null)
                throw new ArgumentNullException(nameof(location));
            ThrowIfDisposed();
            string key = location.StableKey + "|" +
                localDate.Date.ToString("yyyy-MM-dd");
            lock (_gate)
            {
                WeatherForecastWindow cached;
                if (_cache.TryGetValue(key, out cached))
                    return Task.FromResult(cached);
                if (_failedKey == key && _utcNow() < _retryAfterUtc)
                    return Task.FromResult<WeatherForecastWindow>(null);
                if (_inFlightKey == key && _inFlight != null)
                    return _inFlight;
                _inFlightKey = key;
                _inFlight = FetchAndStoreAsync(location, localDate.Date, key);
                return _inFlight;
            }
        }

        internal void InvalidateCache()
        {
            lock (_gate)
            {
                _cache.Clear();
                _cacheOrder.Clear();
                _failedKey = null;
                _retryAfterUtc = DateTimeOffset.MinValue;
            }
        }

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

        private async Task<WeatherForecastWindow> FetchAndStoreAsync(
            WeatherLocation location, DateTime localDate, string key)
        {
            // Ensure the shared task is registered before even an in-memory
            // handler can complete, without returning to a caller's UI context.
            await Task.Delay(1).ConfigureAwait(false);
            try
            {
                lock (_gate) _forecastRequestCount++;
                WeatherForecastWindow value = await _forecast.FetchAsync(
                    location, localDate).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_cache.ContainsKey(key))
                    {
                        while (_cacheOrder.Count >= 3)
                            _cache.Remove(_cacheOrder.Dequeue());
                        _cacheOrder.Enqueue(key);
                    }
                    _cache[key] = value;
                    _failedKey = null;
                    _retryAfterUtc = DateTimeOffset.MinValue;
                }
                return value;
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("weather-forecast",
                    error);
                lock (_gate)
                {
                    _failedKey = key;
                    _retryAfterUtc = _utcNow().Add(FailureCooldown);
                }
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    if (_inFlightKey == key)
                    {
                        _inFlightKey = null;
                        _inFlight = null;
                    }
                }
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
