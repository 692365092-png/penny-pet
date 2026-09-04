using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Desktop-native geometry. Values are physical pixels, never WPF DIP.
    internal struct PhysicalRect
    {
        internal PhysicalRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        internal int Left { get; private set; }
        internal int Top { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int Right { get { return Left + Width; } }
        internal int Bottom { get { return Top + Height; } }
        internal bool IsValid { get { return Width > 0 && Height > 0; } }
    }

    // Durable physical-target identity is deliberately separate from the
    // runtime desktop surface/GDI name that currently exposes that target.
    internal sealed class DisplayTargetIdentity
    {
        internal DisplayTargetIdentity(string stableKey, bool isDurable,
            string monitorDevicePath, string friendlyName,
            ushort edidManufacturerId, ushort edidProductCodeId,
            uint connectorInstance)
        {
            StableKey = (stableKey ?? String.Empty).Trim();
            IsDurable = isDurable;
            MonitorDevicePath = (monitorDevicePath ?? String.Empty).Trim();
            FriendlyName = (friendlyName ?? String.Empty).Trim();
            EdidManufacturerId = edidManufacturerId;
            EdidProductCodeId = edidProductCodeId;
            ConnectorInstance = connectorInstance;
        }

        internal string StableKey { get; private set; }
        internal bool IsDurable { get; private set; }
        internal string MonitorDevicePath { get; private set; }
        internal string FriendlyName { get; private set; }
        internal ushort EdidManufacturerId { get; private set; }
        internal ushort EdidProductCodeId { get; private set; }
        internal uint ConnectorInstance { get; private set; }
    }

    // One independent coordinate surface in the current virtual desktop.
    // A mirrored surface may expose more than one physical target identity.
    internal sealed class DisplaySurfaceSnapshot
    {
        private readonly DisplayTargetIdentity[] _targets;

        internal DisplaySurfaceSnapshot(string runtimeSurfaceId,
            string runtimeGdiName, PhysicalRect bounds,
            PhysicalRect workArea, bool isPrimary, int rotationDegrees,
            IEnumerable<DisplayTargetIdentity> targets)
        {
            RuntimeSurfaceId = (runtimeSurfaceId ?? String.Empty).Trim();
            RuntimeGdiName = (runtimeGdiName ?? String.Empty).Trim();
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
            RotationDegrees = rotationDegrees;
            _targets = targets == null
                ? new DisplayTargetIdentity[0]
                : new List<DisplayTargetIdentity>(targets).ToArray();
            Targets = Array.AsReadOnly(_targets);
        }

        internal string RuntimeSurfaceId { get; private set; }
        internal string RuntimeGdiName { get; private set; }
        internal PhysicalRect Bounds { get; private set; }
        internal PhysicalRect WorkArea { get; private set; }
        internal bool IsPrimary { get; private set; }
        internal int RotationDegrees { get; private set; }
        internal IReadOnlyList<DisplayTargetIdentity> Targets
            { get; private set; }

        // A mirrored surface can expose several physical targets. Target
        // enumeration order is never an identity and never a durable-preferred
        // selection rule; DRT-6 must choose deterministically (prefer an
        // existing preferred key that belongs to this surface) without
        // relying on QueryDisplayConfig enumeration order.
    }

    // Immutable topology truth for one semantic generation. Display order is
    // never an identity; callers resolve by stable/runtime keys or primary.
    internal sealed class DisplayTopologySnapshot
    {
        private readonly DisplaySurfaceSnapshot[] _surfaces;

        internal DisplayTopologySnapshot(long generation,
            IEnumerable<DisplaySurfaceSnapshot> surfaces)
        {
            Generation = generation;
            _surfaces = surfaces == null
                ? new DisplaySurfaceSnapshot[0]
                : new List<DisplaySurfaceSnapshot>(surfaces).ToArray();
            DisplayTopologyRules.Validate(generation, _surfaces);
            Surfaces = Array.AsReadOnly(_surfaces);
        }

        internal long Generation { get; private set; }
        internal IReadOnlyList<DisplaySurfaceSnapshot> Surfaces
            { get; private set; }

        internal DisplaySurfaceSnapshot FindByTargetKey(string key)
        {
            return DisplayTopologyRules.FindByTargetKey(_surfaces, key);
        }

        internal DisplaySurfaceSnapshot FindByRuntimeGdiName(string name)
        {
            return DisplayTopologyRules.FindByRuntimeGdiName(_surfaces, name);
        }

        internal DisplaySurfaceSnapshot FindByRuntimeSurfaceId(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            foreach (DisplaySurfaceSnapshot surface in _surfaces)
                if (String.Equals(surface.RuntimeSurfaceId, id,
                    StringComparison.OrdinalIgnoreCase)) return surface;
            return null;
        }

        internal DisplaySurfaceSnapshot PrimaryOrFirst()
        {
            return DisplayTopologyRules.PrimaryOrFirst(_surfaces);
        }

        // Re-brands the same immutable surface set with a new semantic
        // generation. Only DisplayTopologyRuntime may own generations; the
        // capture provider never assigns a semantic generation.
        internal DisplayTopologySnapshot WithGeneration(long generation)
        {
            return new DisplayTopologySnapshot(generation, _surfaces);
        }
    }
}
