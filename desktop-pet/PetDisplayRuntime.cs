using System;
using System.Drawing;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private const string PetWindowFactsId = "pet";

        private long _petWindowSequence;
        private WindowFacts _petEffectiveFacts;
        private bool _petPlacementInitialized;
        private bool _petTemporaryRehome;
        private bool _petUserMovedSinceTemporaryRehome;
        private bool _petProgrammaticPlacement;
        // Synchronous same-STA guard: during a Pet active-drag DPI handoff,
        // intermediate LocationChanged/SizeChanged must not drive SideTabs /
        // bubble follower layout. This is not distributed version state.
        private bool _petDpiDragHandoffActive;

        private int ActualPetDpi(int fallbackDpi = 96)
        {
            if (IsHandleCreated && Handle != IntPtr.Zero)
            {
                int dpi = NativeDisplayConfig.GetDpiForWindow(Handle);
                if (dpi > 0) return dpi;
            }
            return Math.Max(96, fallbackDpi);
        }

        private bool TrySetPetTopLeft(int x, int y)
        {
            if (!IsHandleCreated || Handle == IntPtr.Zero) return false;

            return NativeDisplayConfig.SetWindowPos(
                Handle, IntPtr.Zero, x, y, 0, 0,
                NativeDisplayConfig.SWP_NOSIZE |
                NativeDisplayConfig.SWP_NOZORDER |
                NativeDisplayConfig.SWP_NOACTIVATE);
        }

        private bool BootstrapPetOntoSurface(DisplaySurfaceSnapshot surface)
        {
            if (surface == null) return false;

            return TrySetPetTopLeft(
                surface.WorkArea.Left + 1,
                surface.WorkArea.Top + 1);
        }

        private DisplaySurfaceSnapshot FindPetSurface(
            WindowFacts facts, DisplayTopologySnapshot topology)
        {
            if (facts == null || topology == null ||
                facts.TopologyGeneration != topology.Generation)
                return null;

            return topology.FindByRuntimeGdiName(facts.RuntimeGdiName);
        }

        private DisplaySurfaceSnapshot FindLegacyPetSurface(
            DisplayTopologySnapshot topology)
        {
            if (topology == null || !_settings.HasLocation) return null;

            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
                if (PetPlacementPolicy.ContainsLegacyPoint(
                    surface, _settings.X, _settings.Y))
                    return surface;

            return null;
        }

        private void UpdatePetCompatibilityLocation(
            WindowFacts facts, bool save)
        {
            if (facts == null) return;

            _settings.HasLocation = true;
            _settings.X = facts.PhysicalBounds.Left;
            _settings.Y = facts.PhysicalBounds.Top;
            _settings.ScalePercent = _scalePercent;

            if (save) _settings.Save();
        }

        private bool CommitPetPreferredFromFacts(
            WindowFacts facts,
            DisplayTopologySnapshot topology,
            string reason)
        {
            if (facts == null || topology == null ||
                facts.TopologyGeneration != topology.Generation)
                return false;

            string targetKey;
            LogicalPoint local;

            if (!PetPlacementPolicy.TryBuildPreferredPoint(
                facts, topology, _settings.PetPreferredTargetKey,
                out targetKey, out local))
                return false;

            _settings.PetPreferredTargetKey = targetKey;
            _settings.PetPreferredLocalLogicalX = local.X;
            _settings.PetPreferredLocalLogicalY = local.Y;

            UpdatePetCompatibilityLocation(facts, false);

            _petEffectiveFacts = facts;
            _petTemporaryRehome = false;
            _petUserMovedSinceTemporaryRehome = false;

            _settings.Save();

            DisplayDiagnostics.Trace("UserPlacementCommitted",
                "window=pet reason=" + (reason ?? String.Empty) +
                " topology=" + topology.Generation +
                " target=" + targetKey +
                " logical=(" + local.X + "," + local.Y + ")" +
                " physical=(" + facts.PhysicalBounds.Left + "," +
                facts.PhysicalBounds.Top + ")");

            return true;
        }

        private bool TryPlacePetAtPreferred(
            DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface,
            LogicalPoint preferredPoint,
            string reason)
        {
            if (topology == null || surface == null ||
                !IsHandleCreated || Handle == IntPtr.Zero)
                return false;

            WindowFacts before = CapturePetWindowFacts(topology);
            bool alreadyOnTarget = before != null &&
                String.Equals(before.RuntimeGdiName, surface.RuntimeGdiName,
                    StringComparison.OrdinalIgnoreCase);

            _petProgrammaticPlacement = true;
            try
            {
                if (!alreadyOnTarget && !BootstrapPetOntoSurface(surface))
                    return false;

                int dpi = ActualPetDpi();
                ApplyCurrentDisplayScale(dpi);

                PhysicalPoint requested = PetPlacementPolicy.ProjectLocalPoint(
                    preferredPoint, surface, dpi);

                PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                    requested, surface.WorkArea, Width, Height);

                if (!TrySetPetTopLeft(clamped.X, clamped.Y))
                    return false;

                WindowFacts actual = CapturePetWindowFacts(topology);

                if (actual == null ||
                    actual.TopologyGeneration != topology.Generation ||
                    !String.Equals(actual.RuntimeGdiName,
                        surface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                _petEffectiveFacts = actual;
                UpdatePetCompatibilityLocation(actual, false);

                DisplayDiagnostics.Trace("PetPlacementResolved",
                    "reason=" + (reason ?? String.Empty) +
                    " topology=" + topology.Generation +
                    " target=" + surface.RuntimeSurfaceId +
                    " dpi=" + actual.Dpi +
                    " physical=(" + actual.PhysicalBounds.Left + "," +
                    actual.PhysicalBounds.Top + "," +
                    actual.PhysicalBounds.Width + "," +
                    actual.PhysicalBounds.Height + ")");

                PositionNoteTabs();
                return true;
            }
            finally
            {
                _petProgrammaticPlacement = false;
            }
        }

        private bool TryPlacePetDefault(
            DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface,
            string reason)
        {
            if (topology == null || surface == null ||
                !IsHandleCreated || Handle == IntPtr.Zero)
                return false;

            _petProgrammaticPlacement = true;
            try
            {
                if (!BootstrapPetOntoSurface(surface))
                    return false;

                int dpi = ActualPetDpi();
                ApplyCurrentDisplayScale(dpi);

                PhysicalPoint target = PetPlacementPolicy.DefaultBottomRight(
                    surface.WorkArea, Width, Height, dpi);

                if (!TrySetPetTopLeft(target.X, target.Y))
                    return false;

                WindowFacts actual = CapturePetWindowFacts(topology);
                if (actual == null ||
                    actual.TopologyGeneration != topology.Generation)
                    return false;

                _petEffectiveFacts = actual;
                UpdatePetCompatibilityLocation(actual, false);

                DisplayDiagnostics.Trace("PetPlacementResolved",
                    "reason=" + (reason ?? String.Empty) +
                    " topology=" + topology.Generation +
                    " target=" + surface.RuntimeSurfaceId +
                    " dpi=" + actual.Dpi);

                PositionNoteTabs();
                return true;
            }
            finally
            {
                _petProgrammaticPlacement = false;
            }
        }

        private bool TryPlacePetLegacy(
            DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface)
        {
            if (topology == null || surface == null ||
                !_settings.HasLocation || !IsHandleCreated ||
                Handle == IntPtr.Zero)
                return false;

            _petProgrammaticPlacement = true;
            try
            {
                if (!TrySetPetTopLeft(_settings.X, _settings.Y))
                    return false;

                int dpi = ActualPetDpi();
                ApplyCurrentDisplayScale(dpi);

                PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                    new PhysicalPoint { X = _settings.X, Y = _settings.Y },
                    surface.WorkArea, Width, Height);

                if (!TrySetPetTopLeft(clamped.X, clamped.Y))
                    return false;

                WindowFacts actual = CapturePetWindowFacts(topology);

                return actual != null &&
                    actual.TopologyGeneration == topology.Generation &&
                    String.Equals(actual.RuntimeGdiName,
                        surface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _petProgrammaticPlacement = false;
            }
        }

        private bool CommitPetUserPlacement()
        {
            if (_petProgrammaticPlacement) return false;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            WindowFacts facts = CapturePetWindowFacts(topology);

            if (topology == null || facts == null ||
                facts.TopologyGeneration != topology.Generation)
            {
                if (_petTemporaryRehome)
                    _petUserMovedSinceTemporaryRehome = true;

                if (facts != null)
                    UpdatePetCompatibilityLocation(facts, true);

                return false;
            }

            if (CommitPetPreferredFromFacts(
                facts, topology, "PetDragCompleted"))
                return true;

            // Ephemeral-only surface: do not fabricate durable identity.
            if (_petTemporaryRehome)
                _petUserMovedSinceTemporaryRehome = true;

            UpdatePetCompatibilityLocation(facts, true);
            return false;
        }

        private void EstablishInitialPetPreference(
            DisplayTopologySnapshot topology,
            string reason)
        {
            WindowFacts facts = CapturePetWindowFacts(topology);
            if (facts == null || topology == null) return;

            CommitPetPreferredFromFacts(facts, topology, reason);
        }

        private void InitializePetDisplayPlacement()
        {
            if (_petPlacementInitialized || !IsHandleCreated ||
                IsDisposed || Disposing)
                return;

            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (topology == null || topology.Surfaces.Count == 0)
                return;

            _petPlacementInitialized = true;

            DisplaySurfaceSnapshot preferred =
                topology.FindByTargetKey(_settings.PetPreferredTargetKey);

            if (preferred != null)
            {
                TryPlacePetAtPreferred(
                    topology,
                    preferred,
                    new LogicalPoint
                    {
                        X = _settings.PetPreferredLocalLogicalX,
                        Y = _settings.PetPreferredLocalLogicalY
                    },
                    "StartupPreferred");

                _petTemporaryRehome = false;
                RefreshNoteTabs();
                return;
            }

            DisplaySurfaceSnapshot legacy = FindLegacyPetSurface(topology);

            if (String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey) &&
                legacy != null &&
                TryPlacePetLegacy(topology, legacy))
            {
                EstablishInitialPetPreference(topology, "LegacyXYMigration");
                RefreshNoteTabs();
                return;
            }

            DisplaySurfaceSnapshot fallback = topology.PrimaryOrFirst();
            if (fallback == null) return;

            if (TryPlacePetDefault(
                topology,
                fallback,
                String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey)
                    ? "InitialDefault"
                    : "StartupPreferredMissing"))
            {
                if (String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey))
                {
                    EstablishInitialPetPreference(topology, "InitialDefault");
                }
                else
                {
                    _petTemporaryRehome = true;
                    _petUserMovedSinceTemporaryRehome = false;

                    DisplayDiagnostics.Trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + fallback.RuntimeSurfaceId);
                }
            }

            RefreshNoteTabs();
        }

        private void ReconcilePetDisplayPlacement(
            DisplayTopologySnapshot topology,
            string reason)
        {
            if (topology == null || IsDisposed || Disposing ||
                !IsHandleCreated)
                return;

            if (!_petPlacementInitialized)
            {
                InitializePetDisplayPlacement();
                return;
            }

            WindowFacts actual = CapturePetWindowFacts(topology);

            if (actual != null)
                _petEffectiveFacts = actual;

            // User drag owns the HWND until mouse-up.
            if (_dragging)
            {
                PositionNoteTabs();
                return;
            }

            DisplaySurfaceSnapshot preferred =
                topology.FindByTargetKey(_settings.PetPreferredTargetKey);

            if (preferred != null)
            {
                if (_petTemporaryRehome &&
                    _petUserMovedSinceTemporaryRehome)
                {
                    PositionNoteTabs();
                    return;
                }

                bool moved = TryPlacePetAtPreferred(
                    topology,
                    preferred,
                    new LogicalPoint
                    {
                        X = _settings.PetPreferredLocalLogicalX,
                        Y = _settings.PetPreferredLocalLogicalY
                    },
                    _petTemporaryRehome ? "PreferredReturned" : "TopologyRepair");

                if (moved)
                {
                    if (_petTemporaryRehome)
                    {
                        DisplayDiagnostics.Trace("PreferredReturned",
                            "window=pet topology=" + topology.Generation +
                            " target=" + preferred.RuntimeSurfaceId);
                    }

                    _petTemporaryRehome = false;
                    _petUserMovedSinceTemporaryRehome = false;
                }

                return;
            }

            // If Windows already left Pet on a valid current surface,
            // accept that as temporary Effective and do not fight Windows.
            DisplaySurfaceSnapshot active = FindPetSurface(actual, topology);

            if (active != null)
            {
                _petTemporaryRehome =
                    !String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey);

                if (_petTemporaryRehome)
                {
                    DisplayDiagnostics.Trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + active.RuntimeSurfaceId +
                        " reason=WindowsEffective");
                }

                PositionNoteTabs();
                return;
            }

            DisplaySurfaceSnapshot fallback =
                FallbackDisplayPolicy.ResolveFallbackSurface(
                    topology,
                    _settings.PetPreferredTargetKey,
                    actual == null
                        ? new PhysicalRect(_settings.X, _settings.Y,
                            Math.Max(1, Width), Math.Max(1, Height))
                        : actual.PhysicalBounds,
                    actual == null ? String.Empty : actual.RuntimeGdiName);

            if (fallback == null) return;

            if (TryPlacePetDefault(topology, fallback, "TopologyFallback"))
            {
                _petTemporaryRehome =
                    !String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey);

                _petUserMovedSinceTemporaryRehome = false;

                if (_petTemporaryRehome)
                {
                    DisplayDiagnostics.Trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + fallback.RuntimeSurfaceId +
                        " reason=TopologyFallback");
                }
            }
        }

        private bool IsPetVisibleInTopology(
            Point location,
            Size size,
            DisplayTopologySnapshot topology)
        {
            if (topology == null) return false;

            PhysicalRect candidate = new PhysicalRect(
                location.X, location.Y,
                Math.Max(1, size.Width),
                Math.Max(1, size.Height));

            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                PhysicalRect work = surface.WorkArea;

                int left = Math.Max(candidate.Left, work.Left);
                int top = Math.Max(candidate.Top, work.Top);
                int right = Math.Min(candidate.Right, work.Right);
                int bottom = Math.Min(candidate.Bottom, work.Bottom);

                if (right - left >= 48 && bottom - top >= 48)
                    return true;
            }

            return false;
        }

        private void EnsurePetVisibleOnCurrentTopology()
        {
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (topology == null || topology.Surfaces.Count == 0) return;

            if (IsPetVisibleInTopology(Location, Size, topology)) return;

            DisplaySurfaceSnapshot fallback = topology.PrimaryOrFirst();
            if (fallback == null) return;

            TryPlacePetDefault(topology, fallback, "EnsureVisible");

            WindowFacts facts = CapturePetWindowFacts(topology);
            if (facts != null)
                UpdatePetCompatibilityLocation(facts, true);
        }

        private void KeepPetFullyVisibleOnCurrentSurface()
        {
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            WindowFacts facts = CapturePetWindowFacts(topology);
            DisplaySurfaceSnapshot surface = FindPetSurface(facts, topology);

            if (surface == null) return;

            PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                new PhysicalPoint { X = Left, Y = Top },
                surface.WorkArea, Width, Height);

            if (clamped.X == Left && clamped.Y == Top) return;

            _petProgrammaticPlacement = true;
            try
            {
                TrySetPetTopLeft(clamped.X, clamped.Y);
            }
            finally
            {
                _petProgrammaticPlacement = false;
            }

            WindowFacts after = CapturePetWindowFacts(topology);
            if (after != null)
                UpdatePetCompatibilityLocation(after, false);
        }
    }
}
