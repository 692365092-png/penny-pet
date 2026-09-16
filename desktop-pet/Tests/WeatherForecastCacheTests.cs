using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class WeatherForecastCacheTests
    {
        private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        private static WeatherLocation Location(int id)
        {
            WeatherLocation result;
            Assert.IsTrue(WeatherLocation.TryCreate("city-" + id, "", "", id, 0, "UTC", out result));
            return result;
        }
        private static WeatherForecastWindow Forecast(DateTime date, int offset = 0)
        {
            return new WeatherForecastWindow(null,
                new WeatherDaySummary(date, 10, 20, 10, 20, 0, 0, 0, 0, 0, null, null, 0, false), null, offset);
        }

        [TestMethod]
        public async Task SwitchingCitiesSharesEachLocationsInFlightRequest()
        {
            var first = new TaskCompletionSource<WeatherForecastWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<WeatherForecastWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requests = 0;
            var cache = new WeatherForecastCache(location => { requests++; return location.Latitude == 1 ? first.Task : second.Task; }, () => Day);
            Task<WeatherForecastWindow> a = cache.GetAsync(Location(1));
            Task<WeatherForecastWindow> b = cache.GetAsync(Location(2));
            Assert.AreSame(a, cache.GetAsync(Location(1)));
            Assert.AreSame(b, cache.GetAsync(Location(2)));
            Assert.AreEqual(2, requests);
            second.SetResult(Forecast(Day.Date));
            await b;
            Assert.AreSame(a, cache.GetAsync(Location(1)), "Completing B must not retire A.");
            first.SetResult(Forecast(Day.Date));
            await a;
        }

        [TestMethod]
        public async Task SynchronousCompletionCannotLeaveAStuckRegistration()
        {
            int requests = 0;
            var cache = new WeatherForecastCache(_ => { requests++; return Task.FromResult(Forecast(Day.Date)); }, () => Day);
            var a = await cache.GetAsync(Location(1));
            Assert.AreSame(a, await cache.GetAsync(Location(1)));
            cache.Invalidate();
            Assert.AreNotSame(a, await cache.GetAsync(Location(1)));
            Assert.AreEqual(2, requests);
        }

        [TestMethod]
        public async Task OtherCitiesSuccessDoesNotClearFailureCooldown()
        {
            DateTimeOffset now = Day;
            int failedRequests = 0;
            var cache = new WeatherForecastCache(location => {
                if (location.Latitude == 1) { failedRequests++; return Task.FromResult<WeatherForecastWindow>(null); }
                return Task.FromResult(Forecast(now.Date));
            }, () => now);
            Assert.IsNull(await cache.GetAsync(Location(1)));
            Assert.IsNotNull(await cache.GetAsync(Location(2)));
            Assert.IsNull(await cache.GetAsync(Location(1)));
            Assert.AreEqual(1, failedRequests);
            now = now.AddMinutes(15);
            Assert.IsNull(await cache.GetAsync(Location(1)));
            Assert.AreEqual(2, failedRequests);
        }

        [TestMethod]
        public async Task CachedDayUsesTheForecastsCityOffset()
        {
            DateTimeOffset now = Day.AddHours(3);
            int requests = 0;
            var cache = new WeatherForecastCache(_ => { requests++; return Task.FromResult(Forecast(now.ToOffset(TimeSpan.FromHours(8)).Date, 8 * 3600)); }, () => now);
            await cache.GetAsync(Location(1));
            now = now.AddMinutes(30);
            await cache.GetAsync(Location(1));
            Assert.AreEqual(1, requests);
            now = now.AddHours(1);
            await cache.GetAsync(Location(1));
            Assert.AreEqual(2, requests);
        }

        [TestMethod]
        public async Task CompletedCacheRemainsBoundedToThreeLocations()
        {
            var requests = new Dictionary<int, int>();
            var cache = new WeatherForecastCache(location => {
                int id = (int)location.Latitude;
                requests[id] = requests.ContainsKey(id) ? requests[id] + 1 : 1;
                return Task.FromResult(Forecast(Day.Date));
            }, () => Day);
            for (int i = 1; i <= 4; i++) await cache.GetAsync(Location(i));
            for (int i = 2; i <= 4; i++) await cache.GetAsync(Location(i));
            Assert.AreEqual(1, requests[2]); Assert.AreEqual(1, requests[3]); Assert.AreEqual(1, requests[4]);
            await cache.GetAsync(Location(1));
            Assert.AreEqual(2, requests[1]);
        }

        [TestMethod]
        public async Task FaultedProviderReleasesItsRegistrationForRetry()
        {
            int attempts = 0;
            var cache = new WeatherForecastCache(_ => {
                if (++attempts == 1) throw new InvalidOperationException("provider failed");
                return Task.FromResult(Forecast(Day.Date));
            }, () => Day);
            try { await cache.GetAsync(Location(1)); Assert.Fail("Expected provider failure."); }
            catch (InvalidOperationException error) { Assert.AreEqual("provider failed", error.Message); }
            Assert.IsNotNull(await cache.GetAsync(Location(1)));
            Assert.AreEqual(2, attempts);
        }

        [TestMethod]
        public async Task InvalidatingCompletedCacheDoesNotDuplicateAnActiveRequest()
        {
            var response = new TaskCompletionSource<WeatherForecastWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requests = 0;
            var cache = new WeatherForecastCache(_ => { requests++; return response.Task; }, () => Day);
            var first = cache.GetAsync(Location(1));
            cache.Invalidate();
            Assert.AreSame(first, cache.GetAsync(Location(1)));
            response.SetResult(Forecast(Day.Date));
            Assert.IsNotNull(await first);
            Assert.AreEqual(1, requests);
        }
    }
}
