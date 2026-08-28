namespace PennyPet
{
    internal static class PetStartupRules
    {
        internal static bool CanReleaseStartupLoading(bool uiReady,
            bool artReady)
        {
            return uiReady && artReady;
        }
    }
}
