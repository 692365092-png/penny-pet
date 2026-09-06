using System;

namespace PennyPet
{
    internal enum WindowFactsVersionDisposition
    {
        Invalid,
        StaleTopology,
        Current
    }

    // Pure current-generation validation for detached HWND facts. Sequence
    // establishes per-window ordering; it never substitutes for topology.
    internal static class WindowFactsVersionRules
    {
        internal static WindowFactsVersionDisposition Classify(
            string expectedWindowId, long expectedWindowSequence,
            WindowFacts facts, DisplayTopologySnapshot capturedTopology,
            long currentTopologyGeneration)
        {
            if (String.IsNullOrWhiteSpace(expectedWindowId) || facts == null ||
                capturedTopology == null ||
                !String.Equals(expectedWindowId, facts.WindowId,
                    StringComparison.OrdinalIgnoreCase) ||
                facts.WindowSequence != expectedWindowSequence ||
                facts.TopologyGeneration != capturedTopology.Generation)
                return WindowFactsVersionDisposition.Invalid;

            DisplaySurfaceSnapshot surface = null;
            if (!String.IsNullOrWhiteSpace(facts.ActiveTargetKey))
                surface = capturedTopology.FindByTargetKey(facts.ActiveTargetKey);
            if (surface == null && !String.IsNullOrWhiteSpace(facts.RuntimeGdiName))
                surface = capturedTopology.FindByRuntimeGdiName(facts.RuntimeGdiName);
            if (surface == null) return WindowFactsVersionDisposition.Invalid;
            return currentTopologyGeneration >= 0 &&
                currentTopologyGeneration == capturedTopology.Generation
                ? WindowFactsVersionDisposition.Current
                : WindowFactsVersionDisposition.StaleTopology;
        }
    }
}
