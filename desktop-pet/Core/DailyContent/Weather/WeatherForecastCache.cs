using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PennyPet
{
    // One request per location, with at most three completed cache entries.
    // Transport owns deadlines/fallback; this owner knows only values and time.
    internal sealed class WeatherForecastCache
    {
        private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(15);
        private readonly object _gate = new object();
        private readonly Func<WeatherLocation, Task<WeatherForecastWindow>> _fetch;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Dictionary<string, Task<WeatherForecastWindow>> _inFlight =
            new Dictionary<string, Task<WeatherForecastWindow>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Queue<string> _cacheOrder = new Queue<string>();

        internal WeatherForecastCache(Func<WeatherLocation, Task<WeatherForecastWindow>> fetch,
            Func<DateTimeOffset> utcNow)
        {
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        internal Task<WeatherForecastWindow> GetAsync(WeatherLocation location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            string key = location.StableKey;
            TaskCompletionSource<WeatherForecastWindow> completion;
            lock (_gate)
            {
                Entry entry;
                if (_cache.TryGetValue(key, out entry))
                {
                    DateTimeOffset now = _utcNow();
                    WeatherForecastWindow value = entry.Value;
                    if (value != null && value.Today != null &&
                        value.Today.Date == now.ToOffset(TimeSpan.FromSeconds(value.UtcOffsetSeconds)).Date)
                        return Task.FromResult(value);
                    if (value == null && now < entry.RetryAfterUtc)
                        return Task.FromResult<WeatherForecastWindow>(null);
                }
                Task<WeatherForecastWindow> pending;
                if (_inFlight.TryGetValue(key, out pending)) return pending;
                completion = new TaskCompletionSource<WeatherForecastWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight.Add(key, completion.Task);
            }
            // Registration precedes execution, even for a synchronously completed fetch.
            _ = CompleteAsync(location, key, completion);
            return completion.Task;
        }

        internal void Invalidate()
        {
            lock (_gate)
            {
                _cache.Clear();
                _cacheOrder.Clear();
            }
        }

        private async Task CompleteAsync(WeatherLocation location, string key,
            TaskCompletionSource<WeatherForecastWindow> completion)
        {
            try
            {
                WeatherForecastWindow value = await _fetch(location).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_cache.ContainsKey(key))
                    {
                        if (_cacheOrder.Count == 3) _cache.Remove(_cacheOrder.Dequeue());
                        _cacheOrder.Enqueue(key);
                    }
                    _cache[key] = new Entry(value, value == null ? _utcNow().Add(FailureCooldown) : DateTimeOffset.MinValue);
                    _inFlight.Remove(key);
                }
                completion.SetResult(value);
            }
            catch (Exception error)
            {
                lock (_gate) _inFlight.Remove(key);
                completion.SetException(error);
            }
        }

        private sealed class Entry
        {
            internal readonly WeatherForecastWindow Value;
            internal readonly DateTimeOffset RetryAfterUtc;
            internal Entry(WeatherForecastWindow value, DateTimeOffset retryAfterUtc)
            { Value = value; RetryAfterUtc = retryAfterUtc; }
        }
    }
}
