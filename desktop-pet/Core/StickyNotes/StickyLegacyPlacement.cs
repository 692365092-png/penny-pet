using System;

namespace PennyPet
{
    // An unresolved v10 file input, never a mirror of the current HWND.
    // Resolving its GDI name establishes PreferredPlacement and consumes it.
    internal sealed class StickyLegacyPlacement
    {
        internal StickyLegacyPlacement(string runtimeGdiName, LogicalRect logical)
        {
            if (String.IsNullOrWhiteSpace(runtimeGdiName))
                throw new ArgumentException("A legacy display name is required.", nameof(runtimeGdiName));
            if (logical.Width <= 0 || logical.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(logical));
            RuntimeGdiName = runtimeGdiName;
            Logical = logical;
        }

        internal string RuntimeGdiName { get; }
        internal LogicalRect Logical { get; }
    }
}
