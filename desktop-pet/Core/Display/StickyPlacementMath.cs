using System;

namespace PennyPet
{
    // Canonical placement for a Sticky. The source of truth is the
    // display-local logical rect (in DIP units) plus the display identity.
    // The physical rect is a derived compatibility projection used by the
    // existing Dock/legacy runtime which still reads X/Y/Width/Height.
    internal sealed class StickyCanonicalPlacement
    {
        internal StickyCanonicalPlacement(string displayId,
            int localX, int localY, int localWidth, int localHeight,
            int physicalLeft, int physicalTop, int physicalWidth,
            int physicalHeight)
        {
            DisplayId = displayId ?? String.Empty;
            LocalX = localX;
            LocalY = localY;
            LocalWidth = Math.Max(1, localWidth);
            LocalHeight = Math.Max(1, localHeight);
            PhysicalLeft = physicalLeft;
            PhysicalTop = physicalTop;
            PhysicalWidth = Math.Max(1, physicalWidth);
            PhysicalHeight = Math.Max(1, physicalHeight);
        }

        internal string DisplayId { get; private set; }
        internal int LocalX { get; private set; }
        internal int LocalY { get; private set; }
        internal int LocalWidth { get; private set; }
        internal int LocalHeight { get; private set; }
        internal int PhysicalLeft { get; private set; }
        internal int PhysicalTop { get; private set; }
        internal int PhysicalWidth { get; private set; }
        internal int PhysicalHeight { get; private set; }

        // Write the placement into canonical StickyNoteData fields. The
        // compatibility X/Y/Width/Height are the projection of that same
        // placement back to physical pixels, which is what the existing
        // Dock/legacy runtime and the native WPF placement executor expect.
        internal void ApplyTo(StickyNoteData note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            note.DisplayId = DisplayId;
            note.LocalLogicalX = LocalX;
            note.LocalLogicalY = LocalY;
            note.LocalLogicalWidth = LocalWidth;
            note.LocalLogicalHeight = LocalHeight;
            note.X = PhysicalLeft;
            note.Y = PhysicalTop;
            note.Width = PhysicalWidth;
            note.Height = PhysicalHeight;
        }

        internal static StickyCanonicalPlacement FromData(StickyNoteData note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            return new StickyCanonicalPlacement(
                note.DisplayId ?? String.Empty,
                note.LocalLogicalX, note.LocalLogicalY,
                note.LocalLogicalWidth, note.LocalLogicalHeight,
                note.X, note.Y, note.Width, note.Height);
        }

        internal bool IsValid
        {
            get
            {
                return !String.IsNullOrWhiteSpace(DisplayId) &&
                    LocalWidth > 0 && LocalHeight > 0;
            }
        }
    }

    // Platform-neutral placement math. It consumes only raw physical origins
    // and scale so the logic is fully unit-testable in the Core assembly and
    // never depends on a Windows monitor handle.
    internal static class StickyPlacementMath
    {
        internal static StickyCanonicalPlacement FromPhysicalRect(
            string displayId, int physicalOriginX, int physicalOriginY,
            double scale, int physicalLeft, int physicalTop,
            int physicalWidth, int physicalHeight)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            LogicalPoint localTopLeft = DisplayGeometry.PhysicalToLocal(
                physicalLeft, physicalTop, physicalOriginX, physicalOriginY,
                safeScale);
            int localWidth = Math.Max(1,
                (int)Math.Round(Math.Max(1, physicalWidth) / safeScale));
            int localHeight = Math.Max(1,
                (int)Math.Round(Math.Max(1, physicalHeight) / safeScale));
            PhysicalPoint compatTopLeft = DisplayGeometry.LocalToPhysical(
                localTopLeft.X, localTopLeft.Y, physicalOriginX,
                physicalOriginY, safeScale);
            return new StickyCanonicalPlacement(
                displayId ?? String.Empty,
                localTopLeft.X, localTopLeft.Y,
                localWidth, localHeight,
                compatTopLeft.X, compatTopLeft.Y,
                (int)Math.Round(localWidth * safeScale),
                (int)Math.Round(localHeight * safeScale));
        }

        internal static StickyCanonicalPlacement FromSpawn(
            string displayId, int physicalOriginX, int physicalOriginY,
            double scale, DockRect petPhysical, DockRect workPhysical,
            DockSize sizeLogical, int gap)
        {
            double safeScale = scale > 0.0 ? scale : 1.0;
            int sizePhysicalWidth = Math.Max(1,
                (int)Math.Round(Math.Max(1, sizeLogical.Width) * safeScale));
            int sizePhysicalHeight = Math.Max(1,
                (int)Math.Round(Math.Max(1, sizeLogical.Height) * safeScale));
            int gapPhysical = Math.Max(0,
                (int)Math.Round(Math.Max(0, gap) * safeScale));

            // Prefer the left of the pet, else the right; reject a horizontal
            // placement that would clip the note under the taskbar or off-screen.
            int requestedLeft = petPhysical.Left - sizePhysicalWidth - gapPhysical;
            if (requestedLeft < workPhysical.Left)
                requestedLeft = petPhysical.Right + gapPhysical;
            int left = Math.Max(workPhysical.Left,
                Math.Min(requestedLeft,
                    Math.Max(workPhysical.Left,
                        workPhysical.Right - sizePhysicalWidth)));

            // Vertically centre on the pet so the note sits beside it instead
            // of falling to the bottom edge when the pet is low on the screen.
            int centerTop = petPhysical.Top +
                (Math.Max(1, petPhysical.Height) - sizePhysicalHeight) / 2;
            int top = Math.Max(workPhysical.Top,
                Math.Min(centerTop,
                    workPhysical.Bottom - sizePhysicalHeight));

            LogicalPoint localTopLeft = DisplayGeometry.PhysicalToLocal(
                left, top, physicalOriginX, physicalOriginY, safeScale);
            return new StickyCanonicalPlacement(
                displayId ?? String.Empty,
                localTopLeft.X, localTopLeft.Y,
                Math.Max(1, (int)Math.Round(sizePhysicalWidth / safeScale)),
                Math.Max(1, (int)Math.Round(sizePhysicalHeight / safeScale)),
                left, top, sizePhysicalWidth, sizePhysicalHeight);
        }
    }
}
