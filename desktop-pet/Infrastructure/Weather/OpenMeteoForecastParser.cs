using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace PennyPet
{
    internal sealed class OpenMeteoForecastParser
    {
        internal WeatherForecastWindow Parse(string json, DateTime localDate)
        {
            if (String.IsNullOrWhiteSpace(json))
                throw new FormatException("Weather response is empty.");
            ForecastResponse response = new JavaScriptSerializer()
                .Deserialize<ForecastResponse>(json);
            HourlyData hourly = response == null ? null : response.hourly;
            Validate(hourly);

            Dictionary<DateTime, DayAccumulator> days =
                new Dictionary<DateTime, DayAccumulator>();
            for (int i = 0; i < hourly.time.Length; i++)
            {
                DateTime time;
                if (!DateTime.TryParseExact(hourly.time[i],
                    "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out time))
                    throw new FormatException("Weather response has bad time.");
                DayAccumulator day;
                if (!days.TryGetValue(time.Date, out day))
                {
                    day = new DayAccumulator(time.Date);
                    days.Add(time.Date, day);
                }
                day.Add(time.Hour, hourly.temperature_2m[i],
                    hourly.apparent_temperature[i],
                    hourly.precipitation_probability[i],
                    hourly.precipitation[i], hourly.snowfall[i],
                    hourly.weather_code[i], hourly.wind_speed_10m[i],
                    hourly.wind_gusts_10m[i]);
            }

            return new WeatherForecastWindow(
                Build(days, localDate.Date.AddDays(-1)),
                Build(days, localDate.Date),
                Build(days, localDate.Date.AddDays(1)));
        }

        private static WeatherDaySummary Build(
            Dictionary<DateTime, DayAccumulator> days, DateTime date)
        {
            DayAccumulator value;
            return days.TryGetValue(date.Date, out value)
                ? value.ToSummary() : null;
        }

        private static void Validate(HourlyData hourly)
        {
            if (hourly == null || hourly.time == null ||
                hourly.time.Length == 0)
                throw new FormatException("Weather response has no hourly data.");
            int count = hourly.time.Length;
            if (!HasCount(hourly.temperature_2m, count) ||
                !HasCount(hourly.apparent_temperature, count) ||
                !HasCount(hourly.precipitation_probability, count) ||
                !HasCount(hourly.precipitation, count) ||
                !HasCount(hourly.snowfall, count) ||
                !HasCount(hourly.weather_code, count) ||
                !HasCount(hourly.wind_speed_10m, count) ||
                !HasCount(hourly.wind_gusts_10m, count))
                throw new FormatException(
                    "Weather hourly arrays have different lengths.");
        }

        private static bool HasCount(Array values, int count)
        {
            return values != null && values.Length == count;
        }

        private sealed class ForecastResponse
        {
            public HourlyData hourly { get; set; }
        }

        private sealed class HourlyData
        {
            public string[] time { get; set; }
            public double[] temperature_2m { get; set; }
            public double[] apparent_temperature { get; set; }
            public double[] precipitation_probability { get; set; }
            public double[] precipitation { get; set; }
            public double[] snowfall { get; set; }
            public int[] weather_code { get; set; }
            public double[] wind_speed_10m { get; set; }
            public double[] wind_gusts_10m { get; set; }
        }

        private sealed class DayAccumulator
        {
            private readonly DateTime _date;
            private double _minimumTemperature = Double.MaxValue;
            private double _maximumTemperature = Double.MinValue;
            private double _minimumApparent = Double.MaxValue;
            private double _maximumApparent = Double.MinValue;
            private double _maximumProbability;
            private double _totalPrecipitation;
            private double _totalSnowfall;
            private double _maximumWindSpeed;
            private double _maximumWindGust;
            private int? _firstLikelyHour;
            private int? _lastLikelyHour;
            private int _likelyHours;
            private bool _hasSnowCode;

            internal DayAccumulator(DateTime date)
            {
                _date = date.Date;
            }

            internal void Add(int hour, double temperature,
                double apparentTemperature, double probability,
                double precipitation, double snowfall, int weatherCode,
                double windSpeed, double windGust)
            {
                _minimumTemperature = Math.Min(_minimumTemperature,
                    temperature);
                _maximumTemperature = Math.Max(_maximumTemperature,
                    temperature);
                _minimumApparent = Math.Min(_minimumApparent,
                    apparentTemperature);
                _maximumApparent = Math.Max(_maximumApparent,
                    apparentTemperature);
                _maximumProbability = Math.Max(_maximumProbability,
                    probability);
                _totalPrecipitation += Math.Max(0D, precipitation);
                _totalSnowfall += Math.Max(0D, snowfall);
                _maximumWindSpeed = Math.Max(_maximumWindSpeed, windSpeed);
                _maximumWindGust = Math.Max(_maximumWindGust, windGust);
                if ((probability >= 60D && precipitation > 0D) ||
                    snowfall > 0D)
                {
                    if (!_firstLikelyHour.HasValue)
                        _firstLikelyHour = hour;
                    _lastLikelyHour = hour;
                    _likelyHours++;
                }
                if ((weatherCode >= 71 && weatherCode <= 77) ||
                    weatherCode == 85 || weatherCode == 86)
                    _hasSnowCode = true;
            }

            internal WeatherDaySummary ToSummary()
            {
                return new WeatherDaySummary(_date, _minimumTemperature,
                    _maximumTemperature, _minimumApparent, _maximumApparent,
                    _maximumProbability, _totalPrecipitation, _totalSnowfall,
                    _maximumWindSpeed, _maximumWindGust, _firstLikelyHour,
                    _lastLikelyHour, _likelyHours, _hasSnowCode);
            }
        }
    }
}
