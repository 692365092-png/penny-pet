using System;

namespace PennyPet
{
    internal struct SolarTermInfo
    {
        internal readonly SolarTerm Term;
        internal readonly string ChineseName;
        internal readonly int LongitudeDegrees;
        internal readonly DateTimeOffset InstantUtc;

        internal SolarTermInfo(SolarTerm term, string chineseName,
            int longitudeDegrees, DateTimeOffset instantUtc)
        {
            Term = term;
            ChineseName = chineseName;
            LongitudeDegrees = longitudeDegrees;
            InstantUtc = instantUtc;
        }
    }
}
