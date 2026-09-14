using System;

namespace PennyPet
{
    // Detailed display/window tracing is opt-in. Normal dragging must not
    // synchronously append diagnostic records on either UI thread.
    // Set PENNY_DISPLAY_TRACE=1 before launch to collect native evidence.
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
            return String.Equals(value, "1", StringComparison.Ordinal);
        }
    }
}
