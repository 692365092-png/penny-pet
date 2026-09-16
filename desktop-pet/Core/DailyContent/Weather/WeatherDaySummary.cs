using System;

namespace PennyPet
{
    internal sealed class WeatherDaySummary
    {
        internal WeatherDaySummary(DateTime date,
            double minimumTemperatureC, double maximumTemperatureC,
            double minimumApparentTemperatureC,
            double maximumApparentTemperatureC,
            double maximumPrecipitationProbability,
            double totalPrecipitationMm, double totalSnowfallCm,
            double maximumWindSpeedKmh, double maximumWindGustKmh,
            int? firstLikelyPrecipitationHour,
            int? lastLikelyPrecipitationHour,
            int likelyPrecipitationHours, bool hasSnowCode)
        {
            Date = date.Date;
            MinimumTemperatureC = minimumTemperatureC;
            MaximumTemperatureC = maximumTemperatureC;
            MinimumApparentTemperatureC = minimumApparentTemperatureC;
            MaximumApparentTemperatureC = maximumApparentTemperatureC;
            MaximumPrecipitationProbability =
                maximumPrecipitationProbability;
            TotalPrecipitationMm = totalPrecipitationMm;
            TotalSnowfallCm = totalSnowfallCm;
            MaximumWindSpeedKmh = maximumWindSpeedKmh;
            MaximumWindGustKmh = maximumWindGustKmh;
            FirstLikelyPrecipitationHour = firstLikelyPrecipitationHour;
            LastLikelyPrecipitationHour = lastLikelyPrecipitationHour;
            LikelyPrecipitationHours = likelyPrecipitationHours;
            HasSnowCode = hasSnowCode;
        }

        internal DateTime Date { get; private set; }
        internal double MinimumTemperatureC { get; private set; }
        internal double MaximumTemperatureC { get; private set; }
        internal double MinimumApparentTemperatureC { get; private set; }
        internal double MaximumApparentTemperatureC { get; private set; }
        internal double MaximumPrecipitationProbability { get; private set; }
        internal double TotalPrecipitationMm { get; private set; }
        internal double TotalSnowfallCm { get; private set; }
        internal double MaximumWindSpeedKmh { get; private set; }
        internal double MaximumWindGustKmh { get; private set; }
        internal int? FirstLikelyPrecipitationHour { get; private set; }
        internal int? LastLikelyPrecipitationHour { get; private set; }
        internal int LikelyPrecipitationHours { get; private set; }
        internal bool HasSnowCode { get; private set; }
    }
}
