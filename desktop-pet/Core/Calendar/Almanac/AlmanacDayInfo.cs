using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal sealed class AlmanacDayInfo
    {
        internal AlmanacDayInfo(int year, int month, int day,
            IEnumerable<string> yi, IEnumerable<string> ji)
        {
            Year = year;
            Month = month;
            Day = day;
            Yi = CopyTerms(yi);
            Ji = CopyTerms(ji);
        }

        internal int Year { get; private set; }
        internal int Month { get; private set; }
        internal int Day { get; private set; }
        internal IReadOnlyList<string> Yi { get; private set; }
        internal IReadOnlyList<string> Ji { get; private set; }

        private static IReadOnlyList<string> CopyTerms(
            IEnumerable<string> source)
        {
            List<string> terms = new List<string>();
            if (source != null)
                foreach (string term in source)
                    if (!String.IsNullOrWhiteSpace(term))
                        terms.Add(term.Trim());
            return terms.AsReadOnly();
        }
    }
}
