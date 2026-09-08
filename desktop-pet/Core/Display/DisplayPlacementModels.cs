using System;

namespace PennyPet
{
    internal sealed class WindowPlacementPreference
    {
        internal WindowPlacementPreference(string preferredTargetKey,
            LogicalRect localLogicalRect)
        {
            PreferredTargetKey = (preferredTargetKey ?? String.Empty).Trim();
            LocalLogicalRect = localLogicalRect;
        }

        internal string PreferredTargetKey { get; private set; }
        internal LogicalRect LocalLogicalRect { get; private set; }

        internal bool IsValid
        {
            get
            {
                return !String.IsNullOrWhiteSpace(PreferredTargetKey) &&
                    LocalLogicalRect.Width > 0 &&
                    LocalLogicalRect.Height > 0;
            }
        }
    }

    // Detached facts captured from an actual window. No HWND or UI object may
    // cross this Core boundary.
    internal sealed class WindowFacts
    {
        internal WindowFacts(string windowId, string activeTargetKey,
            string runtimeGdiName, PhysicalRect physicalBounds, int dpi,
            long topologyGeneration, long windowSequence)
        {
            WindowId = (windowId ?? String.Empty).Trim();
            ActiveTargetKey = (activeTargetKey ?? String.Empty).Trim();
            RuntimeGdiName = (runtimeGdiName ?? String.Empty).Trim();
            PhysicalBounds = physicalBounds;
            Dpi = dpi;
            TopologyGeneration = topologyGeneration;
            WindowSequence = windowSequence;
        }

        internal string WindowId { get; private set; }
        internal string ActiveTargetKey { get; private set; }
        internal string RuntimeGdiName { get; private set; }
        internal PhysicalRect PhysicalBounds { get; private set; }
        internal int Dpi { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal long WindowSequence { get; private set; }
        internal double Scale { get { return Math.Max(1, Dpi) / 96.0; } }
    }

    // Preferred and effective placements remain distinct so a temporary
    // rehome cannot silently overwrite the user's durable preference.
    internal sealed class WindowPlacementRuntimeState
    {
        internal WindowPlacementRuntimeState(
            WindowPlacementPreference preferred, WindowFacts effective,
            bool isTemporaryRehome, bool userMovedSinceRehome,
            string temporaryReason)
        {
            Preferred = preferred;
            Effective = effective;
            IsTemporaryRehome = isTemporaryRehome;
            UserMovedSinceRehome = userMovedSinceRehome;
            TemporaryReason = temporaryReason ?? String.Empty;
        }

        internal WindowPlacementPreference Preferred { get; private set; }
        internal WindowFacts Effective { get; private set; }
        internal bool IsTemporaryRehome { get; private set; }
        internal bool UserMovedSinceRehome { get; private set; }
        internal string TemporaryReason { get; private set; }
    }
}
