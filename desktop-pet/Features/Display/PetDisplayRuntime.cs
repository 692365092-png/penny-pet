using System;

namespace PennyPet
{
    internal sealed class PetDisplayRuntime
    {
        private readonly IPetDisplayWindow _window;
        private readonly PetSettings _settings;
        private readonly Func<DisplayTopologySnapshot> _topology;
        private readonly Action<string, string> _trace;
        private long _windowSequence;
        private bool _initialized;
        private bool _temporaryRehome;
        private bool _userMovedSinceTemporaryRehome;
        private int _placementDepth;

        internal PetDisplayRuntime(IPetDisplayWindow window, PetSettings settings,
            Func<DisplayTopologySnapshot> topology, Action<string, string> trace = null)
        {
            _window = window;
            _settings = settings;
            _topology = topology;
            _trace = trace ?? ((name, details) => { });
        }

        internal WindowFacts EffectiveFacts { get; private set; }
        internal DisplayTopologySnapshot EffectiveTopology { get; private set; }
        internal bool IsTemporarilyRehomed { get { return _temporaryRehome; } }

        private bool IsCurrent(DisplayTopologySnapshot topology)
        { return topology != null && Object.ReferenceEquals(topology, _topology()); }

        internal bool MoveForDpiHandoff(int x, int y)
        {
            _placementDepth++;
            try { return _window.MoveTopLeft(x, y); }
            finally { _placementDepth--; }
        }

        private bool BootstrapPetOntoSurface(DisplaySurfaceSnapshot surface)
        {
            return surface != null && _window.MoveTopLeft(
                surface.WorkArea.Left + 1, surface.WorkArea.Top + 1);
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
            _settings.ScalePercent = _window.ScalePercent;

            if (save) _settings.SaveAsync();
        }

        private bool CommitPetPreferredFromFacts(
            WindowFacts facts,
            DisplayTopologySnapshot topology,
            string reason)
        {
            if (facts == null || !IsCurrent(topology) ||
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

            _temporaryRehome = false;
            _userMovedSinceTemporaryRehome = false;

            _settings.SaveAsync();

            _trace("UserPlacementCommitted",
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
            if (!IsCurrent(topology) || surface == null ||
                !_window.IsAvailable)
                return false;

            WindowFacts before = Capture(topology);
            if (!IsCurrent(topology)) return false;
            bool alreadyOnTarget = before != null &&
                String.Equals(before.RuntimeGdiName, surface.RuntimeGdiName,
                    StringComparison.OrdinalIgnoreCase);

            _placementDepth++;
            try
            {
                if (!alreadyOnTarget && !BootstrapPetOntoSurface(surface))
                    return false;

                int dpi = _window.GetDpi(96);
                _window.ApplyScale(dpi);
                if (!IsCurrent(topology)) return false;

                PhysicalPoint requested = PetPlacementPolicy.ProjectLocalPoint(
                    preferredPoint, surface, dpi);

                PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                    requested, surface.WorkArea, _window.Bounds.Width, _window.Bounds.Height);

                if (!_window.MoveTopLeft(clamped.X, clamped.Y))
                    return false;

                WindowFacts actual = Capture(topology);

                if (actual == null ||
                    actual.TopologyGeneration != topology.Generation ||
                    !String.Equals(actual.RuntimeGdiName,
                        surface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                UpdatePetCompatibilityLocation(actual, false);

                _trace("PetPlacementResolved",
                    "reason=" + (reason ?? String.Empty) +
                    " topology=" + topology.Generation +
                    " target=" + surface.RuntimeSurfaceId +
                    " dpi=" + actual.Dpi +
                    " physical=(" + actual.PhysicalBounds.Left + "," +
                    actual.PhysicalBounds.Top + "," +
                    actual.PhysicalBounds.Width + "," +
                    actual.PhysicalBounds.Height + ")");

                _window.PlacementChanged();
                return true;
            }
            finally
            {
                _placementDepth--;
            }
        }

        private bool TryPlacePetDefault(
            DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface,
            string reason)
        {
            if (!IsCurrent(topology) || surface == null ||
                !_window.IsAvailable)
                return false;

            _placementDepth++;
            try
            {
                if (!BootstrapPetOntoSurface(surface))
                    return false;

                int dpi = _window.GetDpi(96);
                _window.ApplyScale(dpi);
                if (!IsCurrent(topology)) return false;

                PhysicalPoint target = PetPlacementPolicy.DefaultBottomRight(
                    surface.WorkArea, _window.Bounds.Width, _window.Bounds.Height, dpi);

                if (!_window.MoveTopLeft(target.X, target.Y))
                    return false;

                WindowFacts actual = Capture(topology);
                if (actual == null ||
                    actual.TopologyGeneration != topology.Generation)
                    return false;

                UpdatePetCompatibilityLocation(actual, false);

                _trace("PetPlacementResolved",
                    "reason=" + (reason ?? String.Empty) +
                    " topology=" + topology.Generation +
                    " target=" + surface.RuntimeSurfaceId +
                    " dpi=" + actual.Dpi);

                _window.PlacementChanged();
                return true;
            }
            finally
            {
                _placementDepth--;
            }
        }

        private bool TryPlacePetLegacy(
            DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot surface)
        {
            if (!IsCurrent(topology) || surface == null ||
                !_settings.HasLocation || !_window.IsAvailable)
                return false;

            _placementDepth++;
            try
            {
                if (!_window.MoveTopLeft(_settings.X, _settings.Y))
                    return false;

                int dpi = _window.GetDpi(96);
                _window.ApplyScale(dpi);
                if (!IsCurrent(topology)) return false;

                PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                    new PhysicalPoint { X = _settings.X, Y = _settings.Y },
                    surface.WorkArea, _window.Bounds.Width, _window.Bounds.Height);

                if (!_window.MoveTopLeft(clamped.X, clamped.Y))
                    return false;

                WindowFacts actual = Capture(topology);

                return actual != null &&
                    actual.TopologyGeneration == topology.Generation &&
                    String.Equals(actual.RuntimeGdiName,
                        surface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _placementDepth--;
            }
        }

        internal bool CommitUserPlacement()
        {
            if (_placementDepth != 0) return false;
            DisplayTopologySnapshot topology = _topology();
            WindowFacts facts = Capture(topology);

            if (topology == null || facts == null ||
                facts.TopologyGeneration != topology.Generation)
            {
                if (_temporaryRehome)
                    _userMovedSinceTemporaryRehome = true;

                if (facts != null)
                    UpdatePetCompatibilityLocation(facts, true);

                return false;
            }

            if (CommitPetPreferredFromFacts(
                facts, topology, "PetDragCompleted"))
                return true;

            // Ephemeral-only surface: do not fabricate durable identity.
            if (_temporaryRehome)
                _userMovedSinceTemporaryRehome = true;

            UpdatePetCompatibilityLocation(facts, true);
            return false;
        }

        private void EstablishInitialPetPreference(
            DisplayTopologySnapshot topology,
            string reason)
        {
            WindowFacts facts = Capture(topology);
            if (facts == null || topology == null) return;

            CommitPetPreferredFromFacts(facts, topology, reason);
        }

        internal void Initialize()
        {
            if (_initialized || !_window.IsAvailable)
                return;

            DisplayTopologySnapshot topology = _topology();
            if (topology == null || topology.Surfaces.Count == 0)
                return;

            _initialized = true;

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

                _temporaryRehome = false;
                _window.PlacementChanged();
                return;
            }

            DisplaySurfaceSnapshot legacy = FindLegacyPetSurface(topology);

            if (String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey) &&
                legacy != null &&
                TryPlacePetLegacy(topology, legacy))
            {
                EstablishInitialPetPreference(topology, "LegacyXYMigration");
                _window.PlacementChanged();
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
                    _temporaryRehome = true;
                    _userMovedSinceTemporaryRehome = false;

                    _trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + fallback.RuntimeSurfaceId);
                }
            }

            _window.PlacementChanged();
        }

        internal void Reconcile(
            DisplayTopologySnapshot topology,
            string reason)
        {
            if (!IsCurrent(topology) || !_window.IsAvailable || _placementDepth != 0)
                return;

            if (!_initialized)
            {
                Initialize();
                return;
            }

            WindowFacts actual = Capture(topology);
            if (!IsCurrent(topology)) return;

            // User drag owns the HWND until mouse-up.
            if (_window.IsUserDragging)
            {
                _window.PlacementChanged();
                return;
            }

            DisplaySurfaceSnapshot preferred =
                topology.FindByTargetKey(_settings.PetPreferredTargetKey);

            if (preferred != null)
            {
                if (_temporaryRehome &&
                    _userMovedSinceTemporaryRehome)
                {
                    _window.PlacementChanged();
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
                    _temporaryRehome ? "PreferredReturned" : "TopologyRepair");

                if (moved)
                {
                    if (_temporaryRehome)
                    {
                        _trace("PreferredReturned",
                            "window=pet topology=" + topology.Generation +
                            " target=" + preferred.RuntimeSurfaceId);
                    }

                    _temporaryRehome = false;
                    _userMovedSinceTemporaryRehome = false;
                }

                return;
            }

            // If Windows already left Pet on a valid current surface,
            // accept that as temporary Effective and do not fight Windows.
            DisplaySurfaceSnapshot active = FindPetSurface(actual, topology);

            if (active != null)
            {
                _temporaryRehome =
                    !String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey);

                if (_temporaryRehome)
                {
                    _trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + active.RuntimeSurfaceId +
                        " reason=WindowsEffective");
                }

                _window.PlacementChanged();
                return;
            }

            DisplaySurfaceSnapshot fallback =
                FallbackDisplayPolicy.ResolveFallbackSurface(
                    topology,
                    _settings.PetPreferredTargetKey,
                    actual == null
                        ? new PhysicalRect(_settings.X, _settings.Y,
                            Math.Max(1, _window.Bounds.Width), Math.Max(1, _window.Bounds.Height))
                        : actual.PhysicalBounds,
                    actual == null ? String.Empty : actual.RuntimeGdiName);

            if (fallback == null) return;

            if (TryPlacePetDefault(topology, fallback, "TopologyFallback"))
            {
                _temporaryRehome =
                    !String.IsNullOrWhiteSpace(_settings.PetPreferredTargetKey);

                _userMovedSinceTemporaryRehome = false;

                if (_temporaryRehome)
                {
                    _trace("TemporaryRehome",
                        "window=pet topology=" + topology.Generation +
                        " target=" + fallback.RuntimeSurfaceId +
                        " reason=TopologyFallback");
                }
            }
        }

        internal bool IsVisible(PhysicalRect candidate, DisplayTopologySnapshot topology)
        {
            if (topology == null) return false;

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

        internal void EnsureVisible()
        {
            DisplayTopologySnapshot topology = _topology();
            if (topology == null || topology.Surfaces.Count == 0) return;

            if (IsVisible(_window.Bounds, topology)) return;

            DisplaySurfaceSnapshot fallback = topology.PrimaryOrFirst();
            if (fallback == null) return;

            TryPlacePetDefault(topology, fallback, "EnsureVisible");

            WindowFacts facts = Capture(topology);
            if (facts != null)
                UpdatePetCompatibilityLocation(facts, true);
        }

        internal void KeepFullyVisible()
        {
            DisplayTopologySnapshot topology = _topology();
            WindowFacts facts = Capture(topology);
            DisplaySurfaceSnapshot surface = FindPetSurface(facts, topology);

            if (surface == null) return;

            PhysicalPoint clamped = PetPlacementPolicy.ClampTopLeft(
                new PhysicalPoint { X = _window.Bounds.Left, Y = _window.Bounds.Top },
                surface.WorkArea, _window.Bounds.Width, _window.Bounds.Height);

            if (clamped.X == _window.Bounds.Left && clamped.Y == _window.Bounds.Top) return;

            _placementDepth++;
            try
            {
                _window.MoveTopLeft(clamped.X, clamped.Y);
            }
            finally
            {
                _placementDepth--;
            }

            WindowFacts after = Capture(topology);
            if (after != null)
                UpdatePetCompatibilityLocation(after, false);
        }
        internal WindowFacts Capture(DisplayTopologySnapshot topology)
        {
            if (!_window.IsAvailable || !IsCurrent(topology)) return null;
            WindowFacts facts = _window.CaptureFacts(topology, ++_windowSequence);
            // Native calls can reenter the STA. Do not relabel an old capture
            // with the new topology or publish it over a newer effective pair.
            if (facts == null || !IsCurrent(topology) ||
                facts.TopologyGeneration != topology.Generation ||
                (EffectiveFacts != null && facts.WindowSequence <= EffectiveFacts.WindowSequence))
                return null;
            EffectiveFacts = facts;
            EffectiveTopology = topology;
            return facts;
        }

        internal void CaptureForSave()
        {
            WindowFacts facts = Capture(_topology());
            if (facts != null) UpdatePetCompatibilityLocation(facts, false);
            else
            {
                PhysicalRect bounds = _window.Bounds;
                _settings.HasLocation = true;
                _settings.X = bounds.Left;
                _settings.Y = bounds.Top;
                _settings.ScalePercent = _window.ScalePercent;
            }
        }
    }
}
