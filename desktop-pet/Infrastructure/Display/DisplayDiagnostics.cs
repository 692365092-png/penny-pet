using System;

namespace PennyPet
{
    // Lightweight structured display-event trace. Development builds keep it
    // on by default so topology evidence is captured during hand-testing; set
    // PENNY_DISPLAY_TRACE=0 to silence it in production-like runs.
    internal static class DisplayDiagnostics
    {
        internal static readonly bool Enabled = IsEnabled();

        internal static void Trace(string eventName, string detail)
        {
            if (!Enabled) return;
            ApplicationDiagnostics.WriteWindowLayerEvent(
                "display-" + (eventName ?? String.Empty),
                detail ?? String.Empty);
        }

        private static bool IsEnabled()
        {
            string value = Environment.GetEnvironmentVariable(
                "PENNY_DISPLAY_TRACE");
            return !String.Equals(value, "0", StringComparison.Ordinal);
        }
    }
}
