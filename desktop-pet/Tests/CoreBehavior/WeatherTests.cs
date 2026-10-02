using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {

        [TestMethod]
        public void WeatherMeaningRules_UseExplicitPriorityAndMundaneFallback()
        {
            WeatherDaySummary yesterday = WeatherDay(15, 30, 15, 30,
                0, 0, 0, null, 15, false);
            Assert.AreEqual(WeatherMeaning.Snow, SelectWeather(yesterday,
                WeatherDay(-4, 1, -8, 0, 5, 90, 8, 15, 60, true)));
            Assert.AreEqual(WeatherMeaning.RainAndWind, SelectWeather(
                yesterday, WeatherDay(18, 22, 17, 21, 5, 80, 3, 9, 55,
                    false)));
            Assert.AreEqual(WeatherMeaning.RainAndCooling, SelectWeather(
                yesterday, WeatherDay(16, 23, 15, 22, 5, 80, 3, 9, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.HeavyRain, SelectWeather(
                WeatherDay(15, 25, 15, 25, 0, 0, 0, null, 10, false),
                WeatherDay(17, 24, 17, 24, 16, 80, 3, 8, 20, false)));
            Assert.AreEqual(WeatherMeaning.PersistentRain, SelectWeather(
                yesterday, WeatherDay(18, 28, 18, 28, 5, 80, 6, 7, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.Windy, SelectWeather(yesterday,
                WeatherDay(18, 28, 18, 28, 0, 10, 0, null, 55, false)));
            Assert.AreEqual(WeatherMeaning.Cooling, SelectWeather(yesterday,
                WeatherDay(15, 23, 15, 23, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.Warming, SelectWeather(
                WeatherDay(10, 20, 10, 20, 0, 0, 0, null, 10, false),
                WeatherDay(15, 27, 15, 27, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.RainLater, SelectWeather(
                yesterday, WeatherDay(18, 28, 18, 28, 2, 80, 1, 15, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.Hot, SelectWeather(yesterday,
                WeatherDay(25, 33, 26, 36, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.Cold, SelectWeather(
                WeatherDay(1, 8, 1, 8, 0, 0, 0, null, 10, false),
                WeatherDay(1, 8, -1, 7, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.LargeTemperatureRange,
                SelectWeather(WeatherDay(10, 22, 10, 22, 0, 0, 0, null,
                    10, false), WeatherDay(10, 22, 5, 25, 0, 10, 0, null,
                    20, false)));
            Assert.IsNull(SelectWeather(WeatherDay(18, 26, 18, 27, 0, 0,
                0, null, 10, false), WeatherDay(18, 26, 18, 27, 0, 20, 0,
                null, 20, false)));
        }

        [TestMethod]
        public void WeatherMeaningRules_HeavyRainRequiresPrecipitationNotProbability()
        {
            WeatherDaySummary yesterday = WeatherDay(15, 25, 15, 25,
                0, 0, 0, null, 10, false);
            Assert.AreNotEqual(WeatherMeaning.HeavyRain, SelectWeather(
                yesterday, WeatherDay(17, 24, 17, 24, 3, 95, 2, 8, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.HeavyRain, SelectWeather(
                yesterday, WeatherDay(17, 24, 17, 24, 16, 80, 3, 8, 20,
                    false)));
        }

        [TestMethod]
        public void WeatherWording_IsDeterministicVariedAndCautious()
        {
            DateTime start = new DateTime(2026, 1, 1);
            foreach (WeatherMeaning meaning in Enum.GetValues(
                typeof(WeatherMeaning)))
            {
                string[] catalog = WeatherWordingCatalog.GetVariantsForTest(
                    meaning);
                int required = meaning == WeatherMeaning.RainLater ||
                    meaning == WeatherMeaning.Cooling ||
                    meaning == WeatherMeaning.Windy ||
                    meaning == WeatherMeaning.Hot ? 5 : 3;
                Assert.IsTrue(catalog.Length >= required, meaning.ToString());
                HashSet<string> selected = new HashSet<string>();
                for (int day = 0; day < 365; day++)
                {
                    DateTime date = start.AddDays(day);
                    WeatherDailySelection first = WeatherWordingCatalog.Select(
                        meaning, date, "30.5928,114.3055|Asia/Shanghai");
                    WeatherDailySelection retry = WeatherWordingCatalog.Select(
                        meaning, date, "30.5928,114.3055|Asia/Shanghai");
                    Assert.AreEqual(first.Text, retry.Text);
                    Assert.AreEqual(meaning, first.Meaning);
                    Assert.IsTrue(first.Text.Length >= 8 &&
                        first.Text.Length <= 28, first.Text);
                    Assert.IsFalse(first.Text.Contains("\n") ||
                        first.Text.EndsWith("。", StringComparison.Ordinal) ||
                        first.Text.EndsWith("！", StringComparison.Ordinal) ||
                        first.Text.EndsWith("？", StringComparison.Ordinal),
                        first.Text);
                    Assert.IsTrue(first.Text.Count(character =>
                        character == '，') <= 1, first.Text);
                    Assert.IsFalse(first.Text.Contains("预警"));
                    Assert.IsFalse(first.Text.Contains("一定"));
                    Assert.IsFalse(first.Text.Contains("保证"));
                    Assert.IsFalse(first.Text.Contains("空气今天跑得挺快") ||
                        first.Text.Contains("风会比较有存在感"), first.Text);
                    selected.Add(first.Text);
                }
                Assert.IsTrue(selected.Count >= required,
                    meaning + ": " + selected.Count);
            }
            CollectionAssert.Contains(
                WeatherWordingCatalog.GetVariantsForTest(
                    WeatherMeaning.Windy),
                "今天风比较大，出门注意一下");
            CollectionAssert.Contains(
                WeatherWordingCatalog.GetVariantsForTest(
                    WeatherMeaning.HeavyRain),
                "今天雨可能不小，低洼路段尽量绕开");
        }

        private static WeatherMeaning? SelectWeather(
            WeatherDaySummary yesterday, WeatherDaySummary today)
        {
            return WeatherMeaningRules.Select(new WeatherForecastWindow(
                yesterday, today, null, 0));
        }

        private static WeatherDaySummary WeatherDay(double minimumTemperature,
            double maximumTemperature, double minimumApparent,
            double maximumApparent, double precipitation,
            double precipitationProbability, int likelyHours,
            int? firstLikelyHour, double maximumWindGust, bool hasSnow)
        {
            return new WeatherDaySummary(new DateTime(2026, 9, 1),
                minimumTemperature, maximumTemperature, minimumApparent,
                maximumApparent, precipitationProbability, precipitation,
                hasSnow ? 1D : 0D, Math.Min(30D, maximumWindGust),
                maximumWindGust, firstLikelyHour, firstLikelyHour,
                likelyHours, hasSnow);
        }

        [TestMethod]
        public void WeatherLocation_ValidatesCoordinatesAndBuildsStableDisplay()
        {
            WeatherLocation location;
            Assert.IsTrue(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30.5928, 114.3055, "Asia/Shanghai", out location));
            Assert.AreEqual("武汉 · 湖北 · 中国", location.DisplayName);
            Assert.IsTrue(location.StableKey.Contains("Asia/Shanghai"));

            WeatherLocation invalid;
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                91D, 114D, "Asia/Shanghai", out invalid));
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30D, -181D, "Asia/Shanghai", out invalid));
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30D, 114D, "", out invalid));
        }
    }
}
