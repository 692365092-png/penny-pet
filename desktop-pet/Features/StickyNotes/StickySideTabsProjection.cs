using System;
using System.Collections.Generic;
using System.Drawing;

namespace PennyPet
{
    // Immutable Pet -> Sticky projection for side-tab content and placement.
    // The Sticky STA owns every SideTab HWND and derives live overlap locally.
    internal sealed class StickySideTabsProjection
    {
        private readonly SideTabSnapshot[] _notes;

        internal StickySideTabsProjection(
            IEnumerable<SideTabSnapshot> notes,
            Rectangle petBounds,
            Rectangle workArea,
            SideTabPhysicalMetrics metrics,
            long topologyGeneration)
        {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));

            _notes = notes == null
                ? new SideTabSnapshot[0]
                : new List<SideTabSnapshot>(notes).ToArray();
            Notes = Array.AsReadOnly(_notes);
            PetBounds = petBounds;
            WorkArea = workArea;
            Metrics = metrics;
            TopologyGeneration = topologyGeneration;
        }

        internal IReadOnlyList<SideTabSnapshot> Notes { get; private set; }
        internal Rectangle PetBounds { get; private set; }
        internal Rectangle WorkArea { get; private set; }
        internal SideTabPhysicalMetrics Metrics { get; private set; }
        internal long TopologyGeneration { get; private set; }
    }
}
