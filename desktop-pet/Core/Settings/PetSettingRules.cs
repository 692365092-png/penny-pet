using System;

namespace PennyPet
{
    // Pure value normalization shared by persistence and the desktop UI.
    internal static class PetSettingRules
    {
        internal static int NormalizePetScalePercent(int value)
        {
            int clamped = Math.Max(50, Math.Min(200, value));
            return ((clamped + 5) / 10) * 10;
        }

        internal static int NormalizeKeyboardTextScalePercent(int value)
        {
            if (value <= 80) return 60;
            if (value >= 125) return 150;
            return 100;
        }
    }
}
