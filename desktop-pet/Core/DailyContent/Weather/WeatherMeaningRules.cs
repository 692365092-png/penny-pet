using System;

namespace PennyPet
{
    internal static class WeatherMeaningRules
    {
        internal static WeatherMeaning? Select(WeatherForecastWindow forecast)
        {
            if (forecast == null || forecast.Today == null) return null;
            WeatherDaySummary today = forecast.Today;
            WeatherDaySummary yesterday = forecast.Yesterday;
            bool rainy = today.TotalPrecipitationMm > 0D &&
                (today.MaximumPrecipitationProbability >= 60D ||
                    today.LikelyPrecipitationHours >= 2);
            bool windy = today.MaximumWindGustKmh >= 50D ||
                today.MaximumWindSpeedKmh >= 35D;
            double maximumDelta = yesterday == null ? 0D :
                today.MaximumTemperatureC - yesterday.MaximumTemperatureC;
            bool cooling = yesterday != null && maximumDelta <= -5D;
            bool warming = yesterday != null && maximumDelta >= 5D;

            if (today.HasSnowCode || today.TotalSnowfallCm > 0D)
                return WeatherMeaning.Snow;
            if (rainy && windy) return WeatherMeaning.RainAndWind;
            if (rainy && cooling) return WeatherMeaning.RainAndCooling;
            if (rainy && today.TotalPrecipitationMm >= 15D)
                return WeatherMeaning.HeavyRain;
            if (rainy && today.LikelyPrecipitationHours >= 6)
                return WeatherMeaning.PersistentRain;
            if (windy) return WeatherMeaning.Windy;
            if (cooling) return WeatherMeaning.Cooling;
            if (warming) return WeatherMeaning.Warming;
            if (rainy && today.FirstLikelyPrecipitationHour >= 12)
                return WeatherMeaning.RainLater;
            if (today.MaximumApparentTemperatureC >= 35D)
                return WeatherMeaning.Hot;
            if (today.MinimumApparentTemperatureC <= 0D)
                return WeatherMeaning.Cold;
            if (today.MaximumTemperatureC - today.MinimumTemperatureC >= 10D)
                return WeatherMeaning.LargeTemperatureRange;
            return null;
        }
    }
}
