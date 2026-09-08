using System;
using System.Collections.Generic;
using System.Globalization;

namespace PennyPet
{
    internal sealed class WeatherLocation
    {
        private WeatherLocation(string name, string admin1, string country,
            double latitude, double longitude, string timezone)
        {
            Name = name.Trim();
            Admin1 = (admin1 ?? String.Empty).Trim();
            Country = (country ?? String.Empty).Trim();
            Latitude = latitude;
            Longitude = longitude;
            Timezone = timezone.Trim();
        }

        internal string Name { get; private set; }
        internal string Admin1 { get; private set; }
        internal string Country { get; private set; }
        internal double Latitude { get; private set; }
        internal double Longitude { get; private set; }
        internal string Timezone { get; private set; }

        internal string DisplayName
        {
            get
            {
                List<string> parts = new List<string>();
                AddDistinct(parts, Name);
                AddDistinct(parts, Admin1);
                AddDistinct(parts, Country);
                return String.Join(" · ", parts.ToArray());
            }
        }

        internal string StableKey
        {
            get
            {
                return Latitude.ToString("R", CultureInfo.InvariantCulture) +
                    "," + Longitude.ToString("R", CultureInfo.InvariantCulture) +
                    "|" + Timezone;
            }
        }

        internal static bool TryCreate(string name, string admin1,
            string country, double latitude, double longitude,
            string timezone, out WeatherLocation location)
        {
            location = null;
            if (String.IsNullOrWhiteSpace(name) ||
                String.IsNullOrWhiteSpace(timezone) ||
                Double.IsNaN(latitude) || Double.IsInfinity(latitude) ||
                Double.IsNaN(longitude) || Double.IsInfinity(longitude) ||
                latitude < -90D || latitude > 90D ||
                longitude < -180D || longitude > 180D)
                return false;
            location = new WeatherLocation(name, admin1, country, latitude,
                longitude, timezone);
            return true;
        }

        private static void AddDistinct(List<string> parts, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            foreach (string existing in parts)
                if (String.Equals(existing, value,
                    StringComparison.OrdinalIgnoreCase)) return;
            parts.Add(value.Trim());
        }
    }
}
