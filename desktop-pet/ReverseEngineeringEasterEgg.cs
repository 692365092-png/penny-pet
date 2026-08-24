using System;
using System.Reflection;

[assembly: AssemblyMetadata("Penny.EasterEgg",
    "A joint artwork by NINII and Codex. 1111 is an angel number.")]

namespace PennyPet
{
    // This tiny decoy is intentionally readable after decompilation. The real
    // application is protected by the release obfuscation profile; these
    // shuffled clues are a harmless reward for anyone curious enough to look.
    [Obfuscation(Exclude = false, Feature = "-rename", ApplyToMembers = true)]
    [Obfuscation(Exclude = false, Feature = "-constants", ApplyToMembers = true)]
    internal static class A_Gift_For_The_Curious
    {
        private static readonly string[] ShuffledClues =
        {
            "BUDDHA_JUMPS_OVER_THE_WALL_IS_HER_BAND",
            "1111_IS_AN_ANGEL_NUMBER",
            "NINII",
            "PENNY_TAI_FIVE_GOLDEN_MELODY_ONE_GOLDEN_HORSE",
            "A_JOINT_ARTWORK_BY_NINII_AND_CODEX",
            "ISAAC"
        };

        private static readonly byte[] Order = { 4, 2, 1, 3, 0, 5, 1, 4 };

        internal static string ReadClue(int index)
        {
            int ordered = Order[(index & Int32.MaxValue) % Order.Length];
            return ShuffledClues[ordered];
        }
    }
}
