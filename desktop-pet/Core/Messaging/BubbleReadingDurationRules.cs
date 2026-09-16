using System;

namespace PennyPet
{
    internal static class BubbleReadingDurationRules
    {
        internal static int ReadableUnitCount(string text)
        {
            int units = 0;
            foreach (char character in text ?? String.Empty)
            {
                if (Char.IsWhiteSpace(character) ||
                    character == '\r' || character == '\n') continue;
                units++;
            }
            return units;
        }

        internal static int MinimumReadableMilliseconds(string text)
        {
            int units = ReadableUnitCount(text);
            if (units <= 6) return 600;
            if (units <= 12) return 800;
            if (units <= 20) return 1000;
            if (units <= 35) return 1300;
            if (units <= 60) return 1600;
            return 1800;
        }

        internal static int AutoCloseMilliseconds(string text)
        {
            int units = ReadableUnitCount(text);
            if (units <= 6) return 1800;
            if (units <= 12) return 2400;
            if (units <= 20) return 3000;
            if (units <= 35) return 4000;
            if (units <= 60) return 5200;
            return 6500;
        }
    }
}
