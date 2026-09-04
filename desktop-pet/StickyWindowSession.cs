using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns one hosted WPF window and all of its STA-local lifecycle state.
    internal sealed class StickyWindowSession
    {
        private readonly string _noteId;
        private readonly StickyNoteWindow _window;
        private readonly WindowsWindowPlacementExecutor
            _placementExecutor;
        private readonly Action<StickyWindowSession, StickyUiEvent>
            _eventHandler;
        private StickyNoteUiSnapshot _lastSnapshot;
        private long _sequence;
        private bool _hideAfterImeComposition;
        private bool _applyingBounds;
        private bool _eventsSuppressed;
        // Runtime topology truth owned by Pet's DisplayTopologyRuntime and
        // passed across the typed boundary with every Create/Show command.
        // The sticky STA must never capture Windows topology itself.
        private DisplayTopologySnapshot _topology;

        internal StickyWindowSession(StickyNoteUiSnapshot snapshot,
            Action<StickyWindowSession, StickyUiEvent> eventHandler)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            _noteId = snapshot.NoteId;
            _lastSnapshot = snapshot;
            _eventHandler = eventHandler;
            // Hosted windows never derive desktop placement from WPF Left/Top;
            // the native placement executor owns the real HWND geometry.
            _window = new StickyNoteWindow(snapshot.CreateWorkingCopy(),
                false, false, true);
            _placementExecutor = new WindowsWindowPlacementExecutor(
                _window);
            WireEvents();
        }

        internal string NoteId { get { return _noteId; } }
        internal bool IsAvailable
        {
            get { return _window != null && !_window.IsDisposed; }
        }
        internal bool IsImeCompositionActive
        {
            get
            {
                return IsAvailable &&
                    _window.IsImeCompositionActiveForHost;
            }
        }

        internal StickyUiCommandResult Show(bool edit,
            DisplayTopologySnapshot topology)
        {
            _topology = topology ?? _topology;
            if (TryShowAtPhysicalBounds(edit))
            {
                if (!edit) EmitSnapshot(StickyUiEventKind.SnapshotChanged);
                return CurrentResult();
            }
            if (edit)
            {
                _window.ShowAndEdit();
            }
            else
            {
                _window.ShowRestored();
                EmitSnapshot(StickyUiEventKind.SnapshotChanged);
            }
            return CurrentResult();
        }

        private bool TryShowAtPhysicalBounds(bool edit)
        {
            if (!IsAvailable) return false;
            StickyNoteData data = _window.Data;
            // Only the canonical standalone path uses the native placement
            // executor. A dock member is repositioned by the owner via
            // SetBounds in the same batch, so its geometry stays untouched.
            bool hasCanonical =
                !String.IsNullOrWhiteSpace(data.DisplayId) &&
                data.LocalLogicalWidth > 0 && data.LocalLogicalHeight > 0;
            bool hasPreferred =
                !String.IsNullOrWhiteSpace(data.PreferredDisplayTargetKey) &&
                data.PreferredLocalLogicalWidth > 0 &&
                data.PreferredLocalLogicalHeight > 0;
            if ((!hasCanonical && !hasPreferred) ||
                !String.IsNullOrEmpty(data.DockGroupId)) return false;
            NativePlacementPlan plan = ResolvePlacementPlan(data);
            if (plan == null) return false;
            // Suppress the intermediate BoundsChanged echo raised while the
            // HWND is placed, so the transient position can never leak a
            // corrupt canonical placement before the final rect lands.
            bool previousApplying = _applyingBounds;
            _applyingBounds = true;
            try
            {
                return PlaceAtNativeBounds(plan, edit);
            }
            finally { _applyingBounds = previousApplying; }
        }

        // Bootstrap sequence for one standalone window:
        //   resolve target surface -> EnsureHandle -> hidden MOVE into the
        //   target work area -> GetDpiForWindow -> project preferred logical
        //   rect to physical pixels -> SetWindowPos exact rect -> Show ->
        //   capture actual facts -> at most one corrective placement.
        private bool PlaceAtNativeBounds(NativePlacementPlan plan, bool edit)
        {
            StickyNoteData data = _window.Data;
            data.Visible = true;
            _window.ApplyTopMostWindowState(data.AlwaysOnTop);
            _placementExecutor.EnsureHandle();
            if (plan.WorkArea.IsValid &&
                !_placementExecutor.MoveHiddenToSurface(plan.WorkArea))
                return false;
            int dpi = _placementExecutor.GetDpiForWindow();
            if (dpi <= 0) return false;
            PhysicalRect requested = plan.Resolve(dpi);
            if (!requested.IsValid ||
                !_placementExecutor.SetWindowPosExact(requested)) return false;
            _placementExecutor.Show();

            // Verify the actual facts once. A single corrective placement is
            // allowed outside tolerance; after that the real Windows facts
            // win and a remaining mismatch is only reported as diagnostics.
            long generation = plan.Topology == null
                ? 0 : plan.Topology.Generation;
            WindowFacts facts = _placementExecutor.CaptureFacts(_noteId,
                generation, _sequence, plan.Topology);
            if (facts != null &&
                !DisplayGeometry.IsWithinPlacementTolerance(requested,
                    facts.PhysicalBounds,
                    WindowsWindowPlacementExecutor.PlacementTolerancePixels))
            {
                _placementExecutor.SetWindowPosExact(requested);
                facts = _placementExecutor.CaptureFacts(_noteId,
                    generation, _sequence, plan.Topology);
                if (facts != null &&
                    !DisplayGeometry.IsWithinPlacementTolerance(requested,
                        facts.PhysicalBounds,
                        WindowsWindowPlacementExecutor.PlacementTolerancePixels))
                {
                    TracePlacementMismatch(requested, facts);
                }
            }

            if (edit)
            {
                _window.Activate();
                _window.FocusPrimaryInputForTest();
            }
            return true;
        }

        private void TracePlacementMismatch(PhysicalRect requested,
            WindowFacts facts)
        {
            if (facts == null) return;
            DisplayDiagnostics.Trace("PlacementResolved",
                "note=" + _noteId + " correctiveMismatch requested=(" +
                requested.Left + "," + requested.Top + "," +
                requested.Width + "," + requested.Height + ") actual=(" +
                facts.PhysicalBounds.Left + "," + facts.PhysicalBounds.Top +
                "," + facts.PhysicalBounds.Width + "," +
                facts.PhysicalBounds.Height + ") dpi=" + facts.Dpi);
        }

        // Resolve the v10 DisplayId + LocalLogicalRect against the currently
        // live topology, preferring the v11 durable target key first. When
        // neither resolves the persisted physical compatibility rect is used
        // as a visible fallback; no rehome policy is applied yet.
        private NativePlacementPlan ResolvePlacementPlan(
            StickyNoteData data)
        {
            DisplayTopologySnapshot topology = _topology;
            DisplaySurfaceSnapshot surface = null;
            LogicalRect preferred = new LogicalRect();
            if (topology != null &&
                !String.IsNullOrWhiteSpace(data.PreferredDisplayTargetKey) &&
                data.PreferredLocalLogicalWidth > 0 &&
                data.PreferredLocalLogicalHeight > 0)
            {
                surface = topology.FindByTargetKey(
                    data.PreferredDisplayTargetKey);
                if (surface != null)
                {
                    preferred.X = data.PreferredLocalLogicalX;
                    preferred.Y = data.PreferredLocalLogicalY;
                    preferred.Width = data.PreferredLocalLogicalWidth;
                    preferred.Height = data.PreferredLocalLogicalHeight;
                }
            }
            if (surface == null && topology != null)
            {
                surface = topology.FindByRuntimeGdiName(
                    data.DisplayId ?? String.Empty);
                if (surface != null)
                {
                    preferred.X = data.LocalLogicalX;
                    preferred.Y = data.LocalLogicalY;
                    preferred.Width = data.LocalLogicalWidth;
                    preferred.Height = data.LocalLogicalHeight;
                }
            }
            if (surface != null)
            {
                return NativePlacementPlan.Preferred(topology, surface,
                    preferred);
            }

            if (data.Width > 0 && data.Height > 0)
            {
                System.Drawing.Rectangle persisted =
                    new System.Drawing.Rectangle(
                        data.X, data.Y, data.Width, data.Height);
                // Without a Pet-owned topology this session cannot place the
                // note authoritatively; the legacy ShowRestored path owns the
                // visibility fallback instead of a second topology capture.
                if (topology == null) return null;
                PhysicalRect fallback = new PhysicalRect(
                    persisted.Left, persisted.Top,
                    persisted.Width, persisted.Height);
                DisplaySurfaceSnapshot nearest =
                    FindSurfaceWithLargestIntersection(topology, fallback);
                if (nearest == null) nearest = topology.PrimaryOrFirst();
                int left = Math.Max(nearest.WorkArea.Left,
                    Math.Min(persisted.Left, nearest.WorkArea.Left +
                        nearest.WorkArea.Width - persisted.Width));
                int top = Math.Max(nearest.WorkArea.Top,
                    Math.Min(persisted.Top, nearest.WorkArea.Top +
                        nearest.WorkArea.Height - persisted.Height));
                return NativePlacementPlan.Physical(topology,
                    nearest.WorkArea,
                    new PhysicalRect(left, top, persisted.Width,
                        persisted.Height));
            }
            return null;
        }

        private static DisplaySurfaceSnapshot FindSurfaceWithLargestIntersection(
            DisplayTopologySnapshot topology, PhysicalRect rect)
        {
            if (topology == null) return null;
            DisplaySurfaceSnapshot best = null;
            long bestArea = 0;
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                long overlapWidth = Math.Max(0L,
                    (long)Math.Min(surface.Bounds.Right, rect.Right) -
                    Math.Max(surface.Bounds.Left, rect.Left));
                long overlapHeight = Math.Max(0L,
                    (long)Math.Min(surface.Bounds.Bottom, rect.Bottom) -
                    Math.Max(surface.Bounds.Top, rect.Top));
                long area = overlapWidth * overlapHeight;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = surface;
                }
            }
            return best;
        }

        internal StickyUiCommandResult Hide()
        {
            if (IsImeCompositionActive) _hideAfterImeComposition = true;
            else _window.HideNote();
            return CurrentResult();
        }

        internal StickyUiCommandResult FocusPrimaryInput()
        {
            _window.FocusPrimaryInputForTest();
            return CurrentResult();
        }

        internal StickyUiCommandResult SetTopMost(bool topMost)
        {
            _window.ApplyTopMostWindowState(topMost);
            return CurrentResult();
        }

        internal StickyUiCommandResult SetDockResizeRole(
            StickyUiDockResizeRole role)
        {
            if (role == null) return StickyUiCommandResult.NotHandled();
            _window.SetDockResizeRole(role.Grouped, role.ResizeTop,
                role.ResizeBottom, role.SplitBottom,
                role.DividerMinimumHeight, role.DividerMaximumHeight);
            return CurrentResult();
        }

        internal StickyUiCommandResult UpdateReminders(
            IEnumerable<ReminderItem> reminders)
        {
            if (!IsAvailable) return StickyUiCommandResult.NotHandled();
            _window.UpdateReminderBanner(reminders ??
                new ReminderItem[0]);
            return CurrentResult();
        }

        internal StickyUiCommandResult SetBounds(StickyUiBounds bounds,
            DisplayTopologySnapshot topology = null)
        {
            if (bounds == null) return StickyUiCommandResult.NotHandled();
            _topology = topology ?? _topology;
            _applyingBounds = true;
            try
            {
                // Bounds are physical pixels (Dock/restore targets). Position
                // the HWND at physical pixels so per-monitor DPI keeps the note
                // on the correct monitor instead of re-interpreting physical as
                // DIP, which double-scales follower notes on a high-DPI display.
                _window.ShowAtPhysicalBounds(
                    new System.Drawing.Rectangle(
                        bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    false);
            }
            finally { _applyingBounds = false; }
            // Authoritative geometry mutation: publish one strictly newer
            // sequence with the final snapshot so the command result can never
            // collide with (or be overwritten by) a leaked intermediate event.
            _lastSnapshot = CaptureSnapshot();
            _sequence++;
            // A topology-driven rehome needs actual HWND facts on the Pet
            // side. Reuse the ordinary typed BoundsChanged path after the
            // programmatic feedback guard has been released. Dock effects do
            // not pass topology and therefore keep their existing behavior.
            if (topology != null && !_eventsSuppressed)
            {
                WindowFacts facts = CaptureWindowFacts(_sequence);
                TraceWindowFacts(facts, _lastSnapshot);
                Raise(StickyUiEvent.FromSnapshot(
                    StickyUiEventKind.BoundsChanged, _lastSnapshot,
                    _sequence, facts, _topology));
            }
            return StickyUiCommandResult.Handled(_lastSnapshot, _sequence);
        }

        // STA-local HWND for the native deferred batch executor. It never
        // crosses to the Pet thread; only the Sticky STA orchestrator reads it.
        internal IntPtr PlacementHwnd
        {
            get
            {
                if (!IsAvailable) return IntPtr.Zero;
                try
                {
                    return new System.Windows.Interop.WindowInteropHelper(
                        _window).Handle;
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }
        }

        // Visible-safe native reprojection for hotplug rehome and preferred
        // return. The window is temporarily hidden before the target-surface
        // bootstrap so the work-area inset and the exact final rect can never
        // be exposed as intermediate visible positions. DPI truth comes only
        // from GetDpiForWindow after the HWND sits on the target surface.
        internal StickyUiCommandResult Reproject(
            StickyUiReprojectTarget target,
            DisplayTopologySnapshot topology, bool focusPrimary)
        {
            if (target == null || !IsAvailable)
                return StickyUiCommandResult.NotHandled();
            if (IsImeCompositionActive)
                return StickyUiCommandResult.NotAccepted();
            _topology = topology ?? _topology;
            DisplaySurfaceSnapshot surface = _topology == null ? null
                : _topology.FindByRuntimeGdiName(
                    target.SurfaceRuntimeGdiName);
            LogicalRect logical = new LogicalRect
            {
                X = target.LogicalX,
                Y = target.LogicalY,
                Width = target.LogicalWidth,
                Height = target.LogicalHeight
            };
            if (surface == null || logical.Width <= 0 ||
                logical.Height <= 0) return StickyUiCommandResult.NotHandled();

            bool wasVisible = _window.IsVisible;
            System.Drawing.Rectangle previousBounds = _window.PhysicalBounds;
            bool previousApplying = _applyingBounds;
            long resultSequence = ++_sequence;
            _applyingBounds = true;
            WindowFacts facts = null;
            bool succeeded = false;
            try
            {
                _window.ApplyTopMostWindowState(
                    _window.Data.AlwaysOnTop);
                _placementExecutor.EnsureHandle();
                if (wasVisible) _window.Hide();
                if (!_placementExecutor.MoveHiddenToSurface(
                    surface.WorkArea)) return StickyUiCommandResult.NotHandled();
                int dpi = _placementExecutor.GetDpiForWindow();
                if (dpi <= 0) return StickyUiCommandResult.NotHandled();
                PhysicalRect projected = DisplayGeometry.ProjectLocalRect(
                    logical, surface.Bounds.Left, surface.Bounds.Top,
                    dpi / 96.0);
                if (target.CenterInWorkArea)
                    projected = StickySpawnPolicy.CenterInWorkArea(
                        surface.WorkArea, projected.Width, projected.Height);
                if (!projected.IsValid ||
                    !_placementExecutor.SetWindowPosExact(projected))
                    return StickyUiCommandResult.NotHandled();
                if (wasVisible || target.ShowAfterPlacement)
                    _placementExecutor.Show();
                facts = CorrectReprojectionOnce(projected, resultSequence);
                if (facts == null)
                    facts = _placementExecutor.CaptureFacts(_noteId,
                        _topology == null ? 0 : _topology.Generation,
                        resultSequence, _topology);
                if (facts == null || _topology == null ||
                    facts.TopologyGeneration != _topology.Generation ||
                    facts.WindowSequence != resultSequence)
                    return StickyUiCommandResult.NotHandled();
                succeeded = true;
                if (focusPrimary)
                {
                    _window.Activate();
                    _window.FocusPrimaryInputForTest();
                }
            }
            finally
            {
                if (!succeeded)
                    RollbackReproject(wasVisible, previousBounds);
                _applyingBounds = previousApplying;
            }
            if (!succeeded) return StickyUiCommandResult.NotHandled();
            _lastSnapshot = CaptureContentSnapshotForNativeResult();
            return StickyUiCommandResult.Handled(_lastSnapshot,
                resultSequence,
                facts, _topology);
        }

        private WindowFacts CorrectReprojectionOnce(PhysicalRect requested,
            long resultSequence)
        {
            long generation = _topology == null ? 0 : _topology.Generation;
            WindowFacts facts = _placementExecutor.CaptureFacts(_noteId,
                generation, resultSequence, _topology);
            if (facts == null) return null;
            if (
                DisplayGeometry.IsWithinPlacementTolerance(requested,
                    facts.PhysicalBounds,
                    WindowsWindowPlacementExecutor.PlacementTolerancePixels))
                return facts;
            _placementExecutor.SetWindowPosExact(requested);
            facts = _placementExecutor.CaptureFacts(_noteId, generation,
                resultSequence, _topology);
            if (facts != null &&
                !DisplayGeometry.IsWithinPlacementTolerance(requested,
                    facts.PhysicalBounds,
                    WindowsWindowPlacementExecutor.PlacementTolerancePixels))
                TracePlacementMismatch(requested, facts);
            return facts;
        }

        // A follower that is still owned by its previous monitor cannot be
        // projected at the source DPI. Park it hidden on the plan's one
        // target surface first; the host then moves every follower in one
        // final batch and restores the original visibility.
        internal bool TryPrepareDockTargetDpi(
            DisplaySurfaceSnapshot surface, int targetDpi,
            out DockDpiTransition transition)
        {
            transition = null;
            if (!IsAvailable || surface == null || targetDpi <= 0)
                return false;
            _placementExecutor.EnsureHandle();
            int currentDpi = _placementExecutor.GetDpiForWindow();
            if (currentDpi <= 0) return false;
            bool wasVisible = _window.IsVisible;
            System.Drawing.Rectangle previousBounds =
                _window.PhysicalBounds;
            if (currentDpi == targetDpi)
            {
                transition = new DockDpiTransition(previousBounds,
                    wasVisible, false);
                return true;
            }

            bool previousApplying = _applyingBounds;
            _applyingBounds = true;
            try
            {
                if (wasVisible) _window.Hide();
                if (!_placementExecutor.MoveHiddenToSurface(
                        surface.WorkArea) ||
                    _placementExecutor.GetDpiForWindow() != targetDpi)
                {
                    RollbackReproject(wasVisible, previousBounds);
                    return false;
                }
                transition = new DockDpiTransition(previousBounds,
                    wasVisible, true);
                return true;
            }
            finally { _applyingBounds = previousApplying; }
        }

        internal void CompleteDockTargetDpi(DockDpiTransition transition,
            bool placementApplied)
        {
            if (transition == null) return;
            bool previousApplying = _applyingBounds;
            _applyingBounds = true;
            try
            {
                if (!placementApplied)
                    RollbackReproject(transition.WasVisible,
                        transition.PreviousBounds);
                else if (transition.WasMoved && transition.WasVisible)
                    _placementExecutor.Show();
                else if (transition.WasMoved)
                    _window.Hide();
            }
            finally { _applyingBounds = previousApplying; }
        }

        // Transactional failure path: a reprojection must never leave a
        // repository-visible window hidden. Restore the previous physical
        // bounds and visibility whenever any bootstrap step fails.
        private void RollbackReproject(bool wasVisible,
            System.Drawing.Rectangle previousBounds)
        {
            try
            {
                if (previousBounds != System.Drawing.Rectangle.Empty)
                    _placementExecutor.SetWindowPosExact(new PhysicalRect(
                        previousBounds.Left, previousBounds.Top,
                        previousBounds.Width, previousBounds.Height));
                if (wasVisible) _placementExecutor.Show();
                else _window.Hide();
            }
            catch
            {
                // Rollback is best-effort; the bounded visibility restore
                // above already ran whenever the handle was usable.
            }
        }

        // Captures one detached member result (actual facts + content
        // snapshot) for a native Dock batch or a dock-commit capture.
        internal DockBatchMemberResult CaptureDockMember(
            DisplayTopologySnapshot topology)
        {
            _sequence++;
            _lastSnapshot = CaptureContentSnapshotForNativeResult();
            WindowFacts facts = CaptureFactsWith(topology);
            return new DockBatchMemberResult(_noteId, _sequence, facts,
                _lastSnapshot);
        }

        private StickyNoteUiSnapshot CaptureContentSnapshotForNativeResult()
        {
            return StickyNoteUiSnapshot.FromContentData(_window.Data);
        }

        private WindowFacts CaptureFactsWith(DisplayTopologySnapshot topology)
        {
            IntPtr hwnd = PlacementHwnd;
            if (hwnd == IntPtr.Zero) return null;
            return WindowsWindowFactsReader.Capture(hwnd, _noteId,
                topology == null ? 0 : topology.Generation,
                _sequence, topology);
        }

        internal StickyUiCommandResult Close()
        {
            if (IsImeCompositionActive)
                return StickyUiCommandResult.NotAccepted();
            _window.FlushPendingChanges();
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            long sequence = _sequence;
            _window.CloseForApplicationExit();
            return StickyUiCommandResult.Handled(snapshot, sequence);
        }

        internal sealed class DockDpiTransition
        {
            internal DockDpiTransition(
                System.Drawing.Rectangle previousBounds,
                bool wasVisible, bool wasMoved)
            {
                PreviousBounds = previousBounds;
                WasVisible = wasVisible;
                WasMoved = wasMoved;
            }

            internal System.Drawing.Rectangle PreviousBounds
                { get; private set; }
            internal bool WasVisible { get; private set; }
            internal bool WasMoved { get; private set; }

        }

        internal StickyUiFinalSnapshot FlushAndCaptureFinal()
        {
            _window.FlushPendingChanges();
            _lastSnapshot = CaptureSnapshot();
            _sequence++;
            return new StickyUiFinalSnapshot(_lastSnapshot, _sequence);
        }

        internal void ReportImeCompositionActive()
        {
            Raise(StickyUiEvent.Signal(
                StickyUiEventKind.ImeCompositionChanged, _noteId, true,
                _sequence));
        }

        internal void SetEventsSuppressed(bool suppressed)
        {
            _eventsSuppressed = suppressed;
        }

        internal void CloseForBatch()
        {
            UnwireEvents();
            if (IsAvailable) _window.CloseForApplicationExit();
        }

        internal void CloseAfterFailure()
        {
            UnwireEvents();
            try
            {
                if (IsAvailable) _window.CloseForApplicationExit();
            }
            catch { }
        }

        internal void CloseForHostShutdown()
        {
            if (!IsAvailable) return;
            if (IsImeCompositionActive) UnwireEvents();
            else _window.FlushPendingChanges();
            _window.CloseForApplicationExit();
        }

        internal StickyUiCommandResult CurrentResult()
        {
            if (IsAvailable) _lastSnapshot = CaptureSnapshot();
            return StickyUiCommandResult.Handled(_lastSnapshot, _sequence);
        }

        private void WireEvents()
        {
            _window.NoteChanged += NoteChanged;
            _window.TypingActivity += TypingActivity;
            _window.InputFocusChanged += InputFocusChanged;
            _window.ImeCompositionChanged += ImeCompositionChanged;
            _window.Shown += Shown;
            _window.LocationChanged += BoundsChanged;
            _window.SizeChanged += BoundsChanged;
            _window.HeaderDragStarted += HeaderDragStarted;
            _window.HeaderDragMoved += HeaderDragMoved;
            _window.HeaderDragCompleted += HeaderDragCompleted;
            _window.UserResizeCompleted += UserResizeCompleted;
            _window.DockHorizontalResizing += DockHorizontalResizing;
            _window.DockDividerResizeStarted += DockDividerResizeStarted;
            _window.DockDividerResizing += DockDividerResizing;
            _window.DockDividerResizeCompleted += DockDividerResizeCompleted;
            _window.CancelReminderRequested += CancelReminderRequested;
            _window.ModifyReminderRequested += ModifyReminderRequested;
            _window.DeleteReminderRequested += DeleteReminderRequested;
            _window.CloseRequested += CloseRequested;
            _window.DeleteRequested += DeleteRequested;
            _window.NewNoteRequested += NewNoteRequested;
            _window.NewTodoRequested += NewTodoRequested;
            _window.NewScheduleRequested += NewScheduleRequested;
            _window.FormClosed += WindowClosed;
        }

        private void UnwireEvents()
        {
            _window.NoteChanged -= NoteChanged;
            _window.TypingActivity -= TypingActivity;
            _window.InputFocusChanged -= InputFocusChanged;
            _window.ImeCompositionChanged -= ImeCompositionChanged;
            _window.Shown -= Shown;
            _window.LocationChanged -= BoundsChanged;
            _window.SizeChanged -= BoundsChanged;
            _window.HeaderDragStarted -= HeaderDragStarted;
            _window.HeaderDragMoved -= HeaderDragMoved;
            _window.HeaderDragCompleted -= HeaderDragCompleted;
            _window.UserResizeCompleted -= UserResizeCompleted;
            _window.DockHorizontalResizing -= DockHorizontalResizing;
            _window.DockDividerResizeStarted -= DockDividerResizeStarted;
            _window.DockDividerResizing -= DockDividerResizing;
            _window.DockDividerResizeCompleted -= DockDividerResizeCompleted;
            _window.CancelReminderRequested -= CancelReminderRequested;
            _window.ModifyReminderRequested -= ModifyReminderRequested;
            _window.DeleteReminderRequested -= DeleteReminderRequested;
            _window.CloseRequested -= CloseRequested;
            _window.DeleteRequested -= DeleteRequested;
            _window.NewNoteRequested -= NewNoteRequested;
            _window.NewTodoRequested -= NewTodoRequested;
            _window.NewScheduleRequested -= NewScheduleRequested;
            _window.FormClosed -= WindowClosed;
        }

        private void NoteChanged(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.SnapshotChanged);
        }

        private void TypingActivity(object sender, EventArgs e)
        {
            Raise(StickyUiEvent.Signal(StickyUiEventKind.TypingActivity,
                _noteId, true, _sequence));
        }

        private void InputFocusChanged(object sender, EventArgs e)
        {
            Raise(StickyUiEvent.Signal(StickyUiEventKind.InputFocusChanged,
                _noteId, _window.HasFocusedTextInput, _sequence));
        }

        private void ImeCompositionChanged(object sender,
            ImeCompositionEventArgs e)
        {
            Raise(StickyUiEvent.Signal(
                StickyUiEventKind.ImeCompositionChanged, _noteId,
                e != null && e.Active, _sequence));
            if (e != null && !e.Active && _hideAfterImeComposition &&
                IsAvailable)
            {
                _hideAfterImeComposition = false;
                EmitSnapshot(StickyUiEventKind.CloseRequested);
            }
        }

        private void Shown(object sender, EventArgs e)
        {
            Raise(StickyUiEvent.Signal(StickyUiEventKind.FirstRendered,
                _noteId, true, _sequence));
        }

        private void BoundsChanged(object sender, EventArgs e)
        {
            if (_applyingBounds) return;
            if (_window.DockDividerResizeActive) return;
            EmitSnapshot(StickyUiEventKind.BoundsChanged);
        }

        private void HeaderDragStarted(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragStarted);
        }

        private void HeaderDragMoved(object sender, EventArgs e)
        {
            // A programmatic bounds mutation is not a user drag. Suppress the
            // WPF LocationChanged echo so canonical state only receives the
            // authoritative final snapshot from SetBounds.
            if (_applyingBounds) return;
            EmitSnapshot(StickyUiEventKind.HeaderDragMoved);
        }

        private void HeaderDragCompleted(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragCompleted);
        }

        private void UserResizeCompleted(object sender, EventArgs e)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            WindowFacts facts = CaptureWindowFacts(_sequence);
            TraceWindowFacts(facts, snapshot);
            Raise(StickyUiEvent.FromSnapshot(
                StickyUiEventKind.UserResizeCompleted, snapshot, _sequence,
                facts, _topology));
        }

        private void DockHorizontalResizing(object sender,
            DockHorizontalResizeEventArgs e)
        {
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(StickyUiEvent.HorizontalResize(snapshot, _sequence,
                e == null ? 0 : e.Left, e == null ? 0 : e.Width));
        }

        private void DockDividerResizeStarted(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(StickyUiEventKind.DockDividerResizeStarted,
                e);
        }

        private void DockDividerResizing(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(StickyUiEventKind.DockDividerResizing, e);
        }

        private void DockDividerResizeCompleted(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(
                StickyUiEventKind.DockDividerResizeCompleted, e);
        }

        private void EmitDockDividerResize(StickyUiEventKind kind,
            DockDividerResizeEventArgs e)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(StickyUiEvent.DividerResize(kind, snapshot, _sequence,
                e == null ? snapshot.Height : e.Height));
        }

        private void CancelReminderRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.CancelReminderRequested);
        }

        private void ModifyReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            RaiseReminderRequest(StickyUiEventKind.ModifyReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void DeleteReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            RaiseReminderRequest(StickyUiEventKind.DeleteReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void CloseRequested(object sender, EventArgs e)
        {
            if (IsImeCompositionActive) _hideAfterImeComposition = true;
            else EmitSnapshot(StickyUiEventKind.CloseRequested);
        }

        private void DeleteRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.DeleteRequested);
        }

        private void NewNoteRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewNoteRequested);
        }

        private void NewTodoRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewTodoRequested);
        }

        private void NewScheduleRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewScheduleRequested);
        }

        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            if (_eventsSuppressed) return;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(_window.Data);
            _lastSnapshot = snapshot;
            _sequence++;
            UnwireEvents();
            Raise(StickyUiEvent.Signal(StickyUiEventKind.InputFocusChanged,
                _noteId, false, _sequence));
            Raise(StickyUiEvent.FromSnapshot(StickyUiEventKind.Closed, snapshot,
                _sequence));
        }

        private void RaiseRequest(StickyUiEventKind kind)
        {
            Raise(StickyUiEvent.Signal(kind, _noteId, false, _sequence));
        }

        private void RaiseReminderRequest(StickyUiEventKind kind,
            ReminderItem reminder)
        {
            Raise(StickyUiEvent.ReminderRequest(kind, _noteId, reminder,
                _sequence));
        }

        private void EmitSnapshot(StickyUiEventKind kind)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            WindowFacts facts = CaptureWindowFacts(_sequence);
            TraceWindowFacts(facts, snapshot);
            Raise(StickyUiEvent.FromSnapshot(kind, snapshot, _sequence,
                facts, _topology));
        }

        private WindowFacts CaptureWindowFacts(long sequence)
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                hwnd = new System.Windows.Interop.WindowInteropHelper(
                    _window).Handle;
            }
            catch
            {
                return null;
            }
            return WindowsWindowFactsReader.Capture(hwnd, _noteId,
                _topology == null ? 0 : _topology.Generation, sequence,
                _topology);
        }

        private void TraceWindowFacts(WindowFacts facts,
            StickyNoteUiSnapshot snapshot)
        {
            if (facts == null) return;
            string oldScale = snapshot != null &&
                snapshot.LocalLogicalWidth > 0 && snapshot.X != 0
                    ? ((double)snapshot.X /
                        snapshot.LocalLogicalWidth).ToString("0.###")
                    : "-";
            DisplayDiagnostics.Trace("WindowFacts",
                "note=" + _noteId + " seq=" + facts.WindowSequence +
                " dpi=" + facts.Dpi + " gdi=" + facts.RuntimeGdiName +
                " physical=(" + facts.PhysicalBounds.Left + "," +
                facts.PhysicalBounds.Top + "," +
                facts.PhysicalBounds.Width + "," +
                facts.PhysicalBounds.Height + ")" +
                " oldPhysical=(" +
                (snapshot == null ? "-" :
                    snapshot.X + "," + snapshot.Y + "," +
                    snapshot.Width + "," + snapshot.Height) + ")" +
                " oldDisplay=" +
                (snapshot == null ? "-" : snapshot.DisplayId ?? "-") +
                " oldScale=" + oldScale);
        }

        private StickyNoteUiSnapshot CaptureSnapshot()
        {
            // Canonical placement is derived from the real physical window
            // bounds so mixed-DPI monitor origins never warp the stored
            // DisplayId + LocalLogicalRect. The compatibility X/Y/Width/Height
            // are the physical projection of the same placement, which feeds
            // the native placement executor and the existing Dock/legacy
            // runtime, never a second independent source of truth.
            CaptureCanonicalPlacement();
            return StickyNoteUiSnapshot.FromData(_window.Data);
        }

        private void CaptureCanonicalPlacement()
        {
            if (!IsAvailable) return;
            try
            {
                System.Drawing.Rectangle physical = _window.PhysicalBounds;
                if (physical == System.Drawing.Rectangle.Empty) return;
                WindowsDisplayMetrics metrics =
                    WindowsDisplayResolver.ResolvePhysicalRect(
                        physical.Left, physical.Top,
                        physical.Right, physical.Bottom);
                if (metrics != null)
                {
                    StickyCanonicalPlacement placement =
                        StickyPlacementMath.FromPhysicalRect(
                            metrics.DisplayId, metrics.PhysicalLeft,
                            metrics.PhysicalTop, metrics.Scale,
                            physical.Left, physical.Top,
                            physical.Width, physical.Height);
                    placement.ApplyTo(_window.Data);
                    return;
                }
            }
            catch
            {
                // A temporary DPI / display query failure must never erase the
                // canonical placement or write DIP into the physical fields.
                // Fall through and preserve the last valid canonical geometry.
            }
            // Only a note that never owned a valid canonical placement may
            // continue on the legacy DIP compatibility path. A note that
            // already has DisplayId + LocalLogicalRect keeps it unchanged.
            if (IsCanonicalValid(_window.Data)) return;
            _window.Data.X = _window.Left;
            _window.Data.Y = _window.Top;
            _window.Data.Width = _window.Width;
            _window.Data.Height = _window.Height;
        }

        private static bool IsCanonicalValid(StickyNoteData note)
        {
            return note != null &&
                !String.IsNullOrWhiteSpace(note.DisplayId) &&
                note.LocalLogicalWidth > 0 && note.LocalLogicalHeight > 0;
        }

        private void Raise(StickyUiEvent value)
        {
            if (_eventsSuppressed || _eventHandler == null) return;
            _eventHandler(this, value);
        }

        // Immutable placement intent resolved for one show: either the v10
        // preferred display-local logical rect (projected with the real HWND
        // DPI) or a persisted physical fallback when the saved display is gone.
        private sealed class NativePlacementPlan
        {
            private NativePlacementPlan(DisplayTopologySnapshot topology,
                DisplaySurfaceSnapshot surface, LogicalRect preferredLocal,
                PhysicalRect workArea, PhysicalRect physicalFallback)
            {
                Topology = topology;
                Surface = surface;
                PreferredLocal = preferredLocal;
                WorkArea = workArea;
                PhysicalFallback = physicalFallback;
            }

            internal DisplayTopologySnapshot Topology { get; private set; }
            internal DisplaySurfaceSnapshot Surface { get; private set; }
            internal LogicalRect PreferredLocal { get; private set; }
            internal PhysicalRect WorkArea { get; private set; }
            internal PhysicalRect PhysicalFallback { get; private set; }

            internal static NativePlacementPlan Preferred(
                DisplayTopologySnapshot topology,
                DisplaySurfaceSnapshot surface, LogicalRect preferredLocal)
            {
                return new NativePlacementPlan(topology, surface,
                    preferredLocal, surface.WorkArea,
                    new PhysicalRect());
            }

            internal static NativePlacementPlan Physical(
                DisplayTopologySnapshot topology,
                PhysicalRect workArea, PhysicalRect physicalFallback)
            {
                return new NativePlacementPlan(topology, null,
                    new LogicalRect(), workArea, physicalFallback);
            }

            internal static NativePlacementPlan Physical(
                DisplayTopologySnapshot topology, PhysicalRect physicalFallback)
            {
                return Physical(topology, new PhysicalRect(),
                    physicalFallback);
            }

            internal PhysicalRect Resolve(int dpi)
            {
                if (Surface == null) return PhysicalFallback;
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                return DisplayGeometry.ProjectLocalRect(PreferredLocal,
                    Surface.Bounds.Left, Surface.Bounds.Top, scale);
            }
        }
    }
}
