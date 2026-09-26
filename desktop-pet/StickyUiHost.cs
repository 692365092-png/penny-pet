using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Registry/facade for hosted sticky sessions. Window details stay inside
    // StickyWindowSession; STA mechanics stay inside StickyUiThreadHost.
    internal sealed class StickyUiHost : IDisposable
    {
        private readonly object _configurationGate = new object();
        private readonly StickyUiThreadHost _threadHost =
            new StickyUiThreadHost();
        private Func<StickyUiCommand, StickyUiCommandResult> _commandHandler;
        private Action<StickyUiEvent> _eventHandler;
        private SynchronizationContext _eventContext;
        private readonly Dictionary<string, StickyWindowSession> _sessions =
            new Dictionary<string, StickyWindowSession>(
                StringComparer.OrdinalIgnoreCase);
        private DisplayTopologySnapshot _currentTopology;
        // Sticky-STA only. Advance before posting input to Pet, including when
        // Pet is still waiting for an older finalization acknowledgment.
        private DockInput _currentDockInput;
        private System.Windows.Threading.DispatcherTimer _reminderClock;
        private StickyNoteTabsForm _leftNoteTabs;
        private StickyNoteTabsForm _rightNoteTabs;
        private StickySideTabsProjection _pendingSideTabsProjection;
        private bool _sideTabsProjectionPosted;
        private bool? _leftTabsCovered;
        private bool? _rightTabsCovered;
        private Action<string> _sideTabOpen;
        private Action<string> _sideTabDelete;
        private Action<string, int> _sideTabReorder;
        private IntPtr _modalZOrderFloor;
        private DockPulseIndicatorForm _dockPreviewIndicator;
        private DockPulseIndicatorForm _splitGuideIndicator;
        private string _dockPreviewParentNoteId;
        private string _dockPreviewChildNoteId;
        private readonly StickyDockLocalGestureRuntime _localDockGestures;
        private readonly StickyDockCommitQueue _dockCommitQueue;
        private StickyDockSceneProjection _deferredDockScene;
        private long _activeLocalDockDependency;
        private IReadOnlyList<DockWindowTarget>
            _pendingLocalDockRollback;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;

        internal StickyUiHost()
        {
            _localDockGestures = new StickyDockLocalGestureRuntime(
                CaptureLocalDockFacts, ApplyLocalDockFollowers);
            _dockCommitQueue = new StickyDockCommitQueue();
        }

        internal void Start()
        {
            _threadHost.Start();
        }

        internal void SetCommandHandler(
            Func<StickyUiCommand, StickyUiCommandResult> handler)
        {
            lock (_configurationGate) _commandHandler = handler;
        }

        internal void Configure(Action<StickyUiEvent> handler,
            SynchronizationContext eventContext)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (eventContext == null)
                throw new ArgumentNullException(nameof(eventContext));
            lock (_configurationGate)
            {
                _eventHandler = handler;
                _eventContext = eventContext;
                _commandHandler = HandleCommand;
            }
        }

        // Low-frequency model projection. R22 prepares the local visual
        // runtime here; R23 will switch gesture completion to commit/ack.
        internal void SetDockScene(StickyDockSceneProjection scene)
        {
            if (scene == null) return;
            _threadHost.PostToDispatcher(delegate
            {
                if (_localDockGestures.IsActive ||
                    _dockCommitQueue.PendingCount > 0)
                    _deferredDockScene = scene;
                else
                    _localDockGestures.SetScene(scene);
                return StickyUiCommandResult.Handled();
            }, null, null);
        }

        private WindowFacts CaptureLocalDockFacts(string noteId)
        {
            StickyWindowSession session;
            return TryGetSession(noteId, out session)
                ? session.CaptureVisibleFactsForChrome()
                : null;
        }

        private void PrepareLocalDockTopology()
        {
            DisplayTopologySnapshot topology;
            lock (_configurationGate) topology = _currentTopology;
            _localDockGestures.SetTopology(topology);
        }

        // Same-STA executor for the R22 candidate path. The source HWND stays
        // owned by the native moving/sizing loop; only followers are batched.
        // No content snapshot or Pet callback is involved.
        private bool ApplyLocalDockFollowers(
            IReadOnlyList<DockWindowTarget> targets,
            string sourceNoteId)
        {
            if (targets == null || targets.Count == 0) return true;

            StickyWindowSession source;
            if (!TryGetSession(sourceNoteId, out source)) return false;
            WindowFacts sourceFacts =
                source.CaptureVisibleFactsForChrome();
            if (sourceFacts == null) return false;

            DisplayTopologySnapshot topology;
            lock (_configurationGate) topology = _currentTopology;
            if (topology == null ||
                topology.Generation != sourceFacts.TopologyGeneration)
                return false;
            DisplaySurfaceSnapshot surface =
                topology.FindByRuntimeGdiName(
                    sourceFacts.RuntimeGdiName) ??
                topology.FindByTargetKey(sourceFacts.ActiveTargetKey);
            if (surface == null || sourceFacts.Dpi <= 0) return false;

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>(targets.Count);
            List<IntPtr> handles = new List<IntPtr>(targets.Count);
            List<PhysicalRect> rects =
                new List<PhysicalRect>(targets.Count);
            foreach (DockWindowTarget target in targets)
            {
                StickyWindowSession session;
                if (target == null ||
                    !TryGetSession(target.NoteId, out session) ||
                    session.PlacementHwnd == IntPtr.Zero)
                    return false;
                sessions.Add(session);
                handles.Add(session.PlacementHwnd);
                rects.Add(target.PhysicalBounds);
            }

            List<StickyWindowSession.DockDpiTransition> transitions =
                new List<StickyWindowSession.DockDpiTransition>();
            List<StickyWindowSession> transitionSessions =
                new List<StickyWindowSession>();
            bool placementAttempted = false;
            try
            {
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(true);
                foreach (StickyWindowSession session in sessions)
                {
                    StickyWindowSession.DockDpiTransition transition;
                    if (!session.TryPrepareDockTargetDpi(
                        surface, sourceFacts.Dpi, out transition))
                        return false;
                    transitionSessions.Add(session);
                    transitions.Add(transition);
                }

                WindowsBatchPlacementStatus status =
                    WindowsBatchWindowPlacementExecutor.Apply(
                        handles, rects);
                placementAttempted = true;
                bool corrected = false;
                if (status != WindowsBatchPlacementStatus.Applied)
                {
                    // EndDeferWindowPos is not transactional. One bounded
                    // correction is allowed, then actual HWND facts decide.
                    for (int index = 0; index < sessions.Count; index++)
                        sessions[index].SetBounds(new StickyUiBounds(
                            rects[index].Left, rects[index].Top,
                            rects[index].Width, rects[index].Height),
                            topology);
                    corrected = true;
                }

                List<int> mismatches = LocalDockPlacementMismatches(
                    sessions, rects, topology.Generation);
                if (mismatches.Count > 0 && !corrected)
                {
                    foreach (int index in mismatches)
                        sessions[index].SetBounds(new StickyUiBounds(
                            rects[index].Left, rects[index].Top,
                            rects[index].Width, rects[index].Height),
                            topology);
                    corrected = true;
                }

                return LocalDockPlacementMismatches(
                    sessions, rects,
                    topology.Generation).Count == 0;
            }
            finally
            {
                for (int index = transitions.Count - 1;
                    index >= 0; index--)
                    transitionSessions[index].CompleteDockTargetDpi(
                        transitions[index], placementAttempted);
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(false);
                ApplySideTabZOrder();
            }
        }

        private static List<int> LocalDockPlacementMismatches(
            IList<StickyWindowSession> sessions,
            IList<PhysicalRect> expected, long topologyGeneration)
        {
            List<int> result = new List<int>();
            if (sessions == null || expected == null ||
                sessions.Count != expected.Count)
                return result;
            for (int index = 0; index < sessions.Count; index++)
            {
                WindowFacts facts =
                    sessions[index].CaptureVisibleFactsForChrome();
                if (facts == null ||
                    facts.TopologyGeneration != topologyGeneration ||
                    !facts.PhysicalBounds.Equals(expected[index]))
                    result.Add(index);
            }
            return result;
        }

        internal void ConfigureSideTabs(
            Action<string> openNote,
            Action<string> deleteNote,
            Action<string, int> reorderNote)
        {
            lock (_configurationGate)
            {
                _sideTabOpen = openNote;
                _sideTabDelete = deleteNote;
                _sideTabReorder = reorderNote;
            }
        }

        // Latest-wins Pet projection. A Pet drag may publish many positions,
        // but the Sticky dispatcher never queues an unbounded trail of them.
        internal void UpdateSideTabs(StickySideTabsProjection projection)
        {
            if (projection == null) return;
            bool post = false;
            lock (_configurationGate)
            {
                _pendingSideTabsProjection = projection;
                if (!_sideTabsProjectionPosted)
                {
                    _sideTabsProjectionPosted = true;
                    post = true;
                }
            }
            if (post) PostSideTabsProjectionPump();
        }

        private void PostSideTabsProjectionPump()
        {
            _threadHost.PostToDispatcher(
                ApplyLatestSideTabsProjection, null, null);
        }

        private StickyUiCommandResult ApplyLatestSideTabsProjection()
        {
            StickySideTabsProjection projection;
            lock (_configurationGate)
            {
                projection = _pendingSideTabsProjection;
                _pendingSideTabsProjection = null;
            }

            if (projection != null)
                ApplySideTabsProjection(projection);

            bool again;
            lock (_configurationGate)
            {
                again = _pendingSideTabsProjection != null;
                if (!again) _sideTabsProjectionPosted = false;
            }
            if (again) PostSideTabsProjectionPump();

            return StickyUiCommandResult.Handled();
        }

        private void ApplySideTabsProjection(
            StickySideTabsProjection projection)
        {
            EnsureSideTabs();
            if (_leftNoteTabs == null || _rightNoteTabs == null)
                return;

            SideTabPhysicalMetrics metrics = projection.Metrics;
            _leftNoteTabs.ApplyPhysicalMetrics(metrics);
            _rightNoteTabs.ApplyPhysicalMetrics(metrics);

            List<SideTabSnapshot> notes =
                new List<SideTabSnapshot>(projection.Notes);
            int total = notes.Count;
            int overlap = SideTabLayoutPolicy.CalculatePhysicalOverlap(
                projection.PetBounds.Width, metrics);
            int desiredLeftCount =
                SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                    total,
                    new DockRect(projection.PetBounds.Left,
                        projection.PetBounds.Top,
                        projection.PetBounds.Width,
                        projection.PetBounds.Height),
                    new DockRect(projection.WorkArea.Left,
                        projection.WorkArea.Top,
                        projection.WorkArea.Width,
                        projection.WorkArea.Height),
                    metrics.Width, overlap, metrics.WindowMarginX);

            _leftNoteTabs.SetNotes(
                notes.GetRange(0, desiredLeftCount), 0);
            _rightNoteTabs.SetNotes(
                notes.GetRange(desiredLeftCount,
                    total - desiredLeftCount),
                desiredLeftCount);
            _leftNoteTabs.ShowNear(
                projection.PetBounds, projection.WorkArea);
            _rightNoteTabs.ShowNear(
                projection.PetBounds, projection.WorkArea);

            DisplayDiagnostics.Trace("SideTabsLayout",
                "topology=" + projection.TopologyGeneration +
                " dpi=" + metrics.Dpi + " total=" + total +
                " left=" + desiredLeftCount +
                " owner=sticky-sta");

            ApplySideTabZOrder();
        }

        private void EnsureSideTabs()
        {
            if (_leftNoteTabs != null && !_leftNoteTabs.IsDisposed &&
                _rightNoteTabs != null && !_rightNoteTabs.IsDisposed)
                return;

            _leftNoteTabs = CreateSideTabs(StickyTabSide.Left);
            _rightNoteTabs = CreateSideTabs(StickyTabSide.Right);
        }

        private StickyNoteTabsForm CreateSideTabs(StickyTabSide side)
        {
            return new StickyNoteTabsForm(
                side,
                id => PostSideTabOpen(id),
                id => PostSideTabDelete(id),
                (id, index) => PostSideTabReorder(id, index));
        }

        private void PostSideTabOpen(string noteId)
        {
            Action<string> handler;
            lock (_configurationGate) handler = _sideTabOpen;
            if (handler != null)
                PostSemantic(delegate { handler(noteId); });
        }

        private void PostSideTabDelete(string noteId)
        {
            Action<string> handler;
            lock (_configurationGate) handler = _sideTabDelete;
            if (handler != null)
                PostSemantic(delegate { handler(noteId); });
        }

        private void PostSideTabReorder(string noteId, int index)
        {
            Action<string, int> handler;
            lock (_configurationGate) handler = _sideTabReorder;
            if (handler != null)
                PostSemantic(delegate { handler(noteId, index); });
        }

        private void PostSemantic(Action action)
        {
            if (action == null) return;
            SynchronizationContext context;
            lock (_configurationGate) context = _eventContext;
            if (context != null)
            {
                context.Post(delegate { action(); }, null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { action(); });
        }

        internal void SetModalZOrderFloor(IntPtr hwnd)
        {
            lock (_configurationGate) _modalZOrderFloor = hwnd;
            PostChrome(ApplyModalFloorToChrome);
        }

        internal void ShowSplitGuide(Rectangle seam)
        {
            PostChrome(delegate
            {
                ClearSplitGuideOnThread();
                if (seam.IsEmpty) return;
                _splitGuideIndicator = new DockPulseIndicatorForm(
                    Color.FromArgb(255, 151, 62), 0);
                _splitGuideIndicator.ShowSeam(seam);
                KeepTransientBelowModal(_splitGuideIndicator);
            });
        }

        internal void UpdateSplitGuide(Rectangle seam)
        {
            if (seam.IsEmpty) return;
            PostChrome(delegate
            {
                if (_splitGuideIndicator == null ||
                    _splitGuideIndicator.IsDisposed) return;
                _splitGuideIndicator.UpdateSeam(seam);
                KeepTransientBelowModal(_splitGuideIndicator);
            });
        }

        internal void ClearSplitGuide()
        {
            PostChrome(ClearSplitGuideOnThread);
        }

        internal void UpdateDockPreview(
            string parentNoteId,
            string childNoteId,
            Rectangle seam)
        {
            PostChrome(delegate
            {
                string parent = parentNoteId ?? String.Empty;
                string child = childNoteId ?? String.Empty;
                bool same = String.Equals(parent,
                        _dockPreviewParentNoteId ?? String.Empty,
                        StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(child,
                        _dockPreviewChildNoteId ?? String.Empty,
                        StringComparison.OrdinalIgnoreCase);
                if (same)
                {
                    if (_dockPreviewIndicator != null &&
                        !_dockPreviewIndicator.IsDisposed &&
                        !seam.IsEmpty)
                        _dockPreviewIndicator.UpdateSeam(seam);
                    return;
                }

                ClearDockPreviewOnThread();
                if (String.IsNullOrEmpty(parent) || seam.IsEmpty)
                    return;

                _dockPreviewParentNoteId = parent;
                _dockPreviewChildNoteId = child;
                _dockPreviewIndicator = new DockPulseIndicatorForm(
                    Color.FromArgb(32, 160, 255), 0);
                _dockPreviewIndicator.ShowSeam(seam);
                KeepTransientBelowModal(_dockPreviewIndicator);
            });
        }

        internal void ClearDockPreview()
        {
            PostChrome(ClearDockPreviewOnThread);
        }

        internal void ShowTransientDockPulse(
            Rectangle seam, Color color)
        {
            if (seam.IsEmpty) return;
            PostChrome(delegate
            {
                DockPulseIndicatorForm indicator =
                    new DockPulseIndicatorForm(color, 720);
                indicator.ShowSeam(seam);
                KeepTransientBelowModal(indicator);
            });
        }

        private void PostChrome(Action action)
        {
            if (action == null) return;
            _threadHost.PostToDispatcher(delegate
            {
                action();
                return StickyUiCommandResult.Handled();
            }, null, null);
        }

        private void ClearSplitGuideOnThread()
        {
            if (_splitGuideIndicator != null &&
                !_splitGuideIndicator.IsDisposed)
                _splitGuideIndicator.Close();
            _splitGuideIndicator = null;
        }

        private void ClearDockPreviewOnThread()
        {
            if (_dockPreviewIndicator != null &&
                !_dockPreviewIndicator.IsDisposed)
                _dockPreviewIndicator.Close();
            _dockPreviewIndicator = null;
            _dockPreviewParentNoteId = null;
            _dockPreviewChildNoteId = null;
        }

        private void ApplySideTabZOrder()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null ||
                _leftNoteTabs.IsDisposed || _rightNoteTabs.IsDisposed)
                return;

            bool leftCovered = false;
            bool rightCovered = false;
            Rectangle leftBounds = _leftNoteTabs.Bounds;
            Rectangle rightBounds = _rightNoteTabs.Bounds;

            foreach (StickyWindowSession session in _sessions.Values)
            {
                WindowFacts facts =
                    session.CaptureVisibleFactsForChrome();
                if (facts == null) continue;

                PhysicalRect actual = facts.PhysicalBounds;
                Rectangle bounds = new Rectangle(
                    actual.Left, actual.Top,
                    actual.Width, actual.Height);
                leftCovered |= _leftNoteTabs.Visible &&
                    leftBounds.IntersectsWith(bounds);
                rightCovered |= _rightNoteTabs.Visible &&
                    rightBounds.IntersectsWith(bounds);
                if (leftCovered && rightCovered) break;
            }

            ApplySideTabCoverage(
                _leftNoteTabs, leftCovered, ref _leftTabsCovered,
                "SideTabsLeft");
            ApplySideTabCoverage(
                _rightNoteTabs, rightCovered, ref _rightTabsCovered,
                "SideTabsRight");
            ApplyModalFloorToChrome();
        }

        private void ApplySideTabCoverage(
            StickyNoteTabsForm tabs,
            bool covered,
            ref bool? previous,
            string diagnosticName)
        {
            if (!previous.HasValue || previous.Value != covered)
            {
                previous = covered;
                tabs.TopMost =
                    StickyNoteWindowRules.ShouldKeepSideTabsTopMost(
                        covered);
                if (!covered && tabs.Visible)
                    tabs.BringToFront();
                ApplicationDiagnostics.WriteWindowLayerEvent(
                    diagnosticName,
                    covered ? "covered" : "clear");
            }
        }

        private void ApplyModalFloorToChrome()
        {
            KeepTransientBelowModal(_leftNoteTabs);
            KeepTransientBelowModal(_rightNoteTabs);
            KeepTransientBelowModal(_dockPreviewIndicator);
            KeepTransientBelowModal(_splitGuideIndicator);
        }

        private void KeepTransientBelowModal(Form transient)
        {
            IntPtr floor;
            lock (_configurationGate) floor = _modalZOrderFloor;
            if (floor == IntPtr.Zero || transient == null ||
                transient.IsDisposed || !transient.Visible ||
                !transient.IsHandleCreated)
                return;

            SetWindowPos(transient.Handle, floor,
                0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate |
                SwpNoOwnerZOrder);
        }

        internal void SetFaultHandler(Action<Exception> handler)
        {
            if (handler == null) return;
            _threadHost.Faulted += delegate(Exception error)
            {
                SynchronizationContext context;
                lock (_configurationGate) context = _eventContext;
                if (context != null)
                {
                    context.Post(delegate { handler(error); }, null);
                    return;
                }
                ThreadPool.QueueUserWorkItem(delegate { handler(error); });
            };
        }

        internal void PostStartupRestore(StickyUiCommand command,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Func<StickyUiCommand, StickyUiCommandResult> handler;
            lock (_configurationGate) handler = _commandHandler;
            _threadHost.PostStartupRestore(delegate
            {
                StickyUiCommandResult result;
                try
                {
                    result = handler == null
                        ? StickyUiCommandResult.NotHandled()
                        : handler(command) ?? StickyUiCommandResult.NotHandled();
                }
                catch (Exception error)
                {
                    result = StickyUiCommandResult.Failed(error);
                }
                StickyUiThreadHost.PostCompletionForHost(
                    completionContext, completed, result);
            });
        }

        internal void PostCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed)
        {
            PostCommand(command, completed, SynchronizationContext.Current);
        }

        internal void PostCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            Func<StickyUiCommand, StickyUiCommandResult> handler;
            lock (_configurationGate) handler = _commandHandler;
            _threadHost.Post(command, handler, completed, completionContext);
        }

        // Dedicated latest-wins entry for a live Dock drag. This is not a
        // generic scheduler: the Pet thread replaces the immutable plan in
        // the mailbox and only one deferred native batch runs at a time.
        // Horizontal and divider gestures share one latest-frame/final transport.
        // Move all followers without showing or activating them, then capture
        // each member once. WindowFacts remain the actual geometry authority.
        // Host-owned current topology truth for the Dock stale gate and for
        // actual-facts capture. Pet publishes every settled snapshot here.
        internal void SetCurrentTopology(DisplayTopologySnapshot snapshot)
        {
            lock (_configurationGate) _currentTopology = snapshot;
            if (snapshot == null) return;
            _threadHost.PostToDispatcher(delegate
            {
                _pendingLocalDockRollback = null;
                if (!_localDockGestures.TryRebaseTopology(snapshot))
                {
                    _localDockGestures.Cancel();
                    _activeLocalDockDependency = 0;
                    ClearDockPreviewOnThread();
                    ClearSplitGuideOnThread();
                }
                return StickyUiCommandResult.Handled();
            }, null, null);
        }

        private bool IsCurrentTopology(DisplayTopologySnapshot topology)
        {
            if (topology == null) return false;
            DisplayTopologySnapshot current;
            lock (_configurationGate) current = _currentTopology;
            return current != null && current.Generation == topology.Generation;
        }

        // Pure Z-order sequence for one drag: the mature legacy invariant is
        // tail-to-root for the non-source members, then the source last, so a
        // moving Dock occupies one contiguous band with the dragged header on
        // top. Membership order is semantic and must never be re-sorted.
        internal static bool TryBuildDockDragRaiseOrder(
            IList<string> orderedNoteIds,
            string sourceNoteId,
            out string[] raiseOrder)
        {
            raiseOrder = null;
            if (orderedNoteIds == null ||
                orderedNoteIds.Count < 2 ||
                String.IsNullOrWhiteSpace(sourceNoteId))
                return false;

            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            bool sourceFound = false;
            for (int index = 0; index < orderedNoteIds.Count; index++)
            {
                string noteId = orderedNoteIds[index];
                if (String.IsNullOrWhiteSpace(noteId) ||
                    !seen.Add(noteId))
                    return false;
                if (String.Equals(noteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase))
                    sourceFound = true;
            }
            if (!sourceFound) return false;

            List<string> result = new List<string>(
                orderedNoteIds.Count);
            for (int index = orderedNoteIds.Count - 1;
                index >= 0; index--)
            {
                string noteId = orderedNoteIds[index];
                if (String.Equals(noteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(noteId);
            }
            result.Add(sourceNoteId);

            if (result.Count != orderedNoteIds.Count)
                return false;
            raiseOrder = result.ToArray();
            return true;
        }

        private StickyUiCommandResult HandleCommand(
            StickyUiCommand command)
        {
            StickyWindowSession session = null;
            try
            {
                switch (command.Kind)
                {
                    case StickyUiCommandKind.Create:
                        return CreateSession(command);
                    case StickyUiCommandKind.EnsureSession:
                        return EnsureSession(command);
                    case StickyUiCommandKind.Show:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Show(command.Flag, command.Topology, command.Placement)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.Hide:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Hide()
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.FocusPrimaryInput:
                        return TryGetSession(command.NoteId, out session)
                            ? session.FocusPrimaryInput()
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.SetTopMost:
                        return TryGetSession(command.NoteId, out session)
                            ? session.SetTopMost(command.Flag)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.SetDockResizeRole:
                        return TryGetSession(command.NoteId, out session)
                            ? session.SetDockResizeRole(command.DockResizeRole)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.SetBounds:
                        if (command.Input != null && !ReferenceEquals(command.Input, _currentDockInput))
                            return StickyUiCommandResult.NotHandled();
                        if (command.Topology != null && !IsCurrentTopology(command.Topology))
                            return StickyUiCommandResult.NotHandled();
                        return TryGetSession(command.NoteId, out session)
                            ? session.SetBounds(command.Bounds,
                                command.Topology)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.Reproject:
                        if (command.Topology == null || !IsCurrentTopology(command.Topology))
                            return StickyUiCommandResult.NotHandled();
                        return TryGetSession(command.NoteId, out session)
                            ? session.Reproject(command.ReprojectTarget,
                                command.Topology, command.Flag)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.ReprojectDockGroup:
                        return ApplyDockGroupReproject(command);
                    case StickyUiCommandKind.RestoreDockGroup:
                        return RestoreDockGroup(command);
                    case StickyUiCommandKind.CaptureWindowFacts:
                        if (!IsCurrentTopology(command.Topology))
                            return StickyUiCommandResult.NotHandled();
                        return TryGetSession(command.NoteId, out session)
                            ? session.CaptureCurrentFacts(command.Topology)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.AcknowledgeDockCommit:
                        return AcknowledgeLocalDockCommit(
                            command.DockCommitAck);
                    case StickyUiCommandKind.PrepareDockStructure:
                        return PrepareLocalDockStructure(
                            command.DockNoteIds);
                    case StickyUiCommandKind.UpdateReminders:
                        if (!TryGetSession(command.NoteId, out session))
                            return StickyUiCommandResult.NotHandled();
                        session.UpdateReminders(command.Reminders);
                        return StickyUiCommandResult.Handled();
                    case StickyUiCommandKind.UpdateAllReminders:
                        foreach (StickyWindowSession member in _sessions.Values)
                            member.UpdateReminders(command.Reminders);
                        return StickyUiCommandResult.Handled();
                    case StickyUiCommandKind.Close:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Close()
                            : StickyUiCommandResult.Handled();
                    case StickyUiCommandKind.CloseAll:
                        return CloseAllSessions();
                    default:
                        return StickyUiCommandResult.NotHandled();
                }
            }
            catch
            {
                if (session != null)
                {
                    session.CloseAfterFailure();
                    _sessions.Remove(command.NoteId);
                    RefreshReminderClock();
                }
                throw;
            }
            finally
            {
                switch (command.Kind)
                {
                    case StickyUiCommandKind.Create:
                    case StickyUiCommandKind.EnsureSession:
                    case StickyUiCommandKind.UpdateReminders:
                    case StickyUiCommandKind.UpdateAllReminders:
                    case StickyUiCommandKind.CloseAll:
                    case StickyUiCommandKind.RestoreDockGroup:
                        RefreshReminderClock();
                        break;
                }
            }
        }

        private void RefreshReminderClock()
        {
            bool active = false;
            foreach (StickyWindowSession session in _sessions.Values)
                if (session.HasVisibleReminders) { active = true; break; }
            if (active && _reminderClock == null)
            {
                _reminderClock = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background);
                _reminderClock.Interval = TimeSpan.FromSeconds(1);
                _reminderClock.Tick += delegate
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    foreach (StickyWindowSession session in _sessions.Values)
                        if (session.HasVisibleReminders)
                            session.RefreshReminderCountdown(nowUtc);
                };
            }
            if (_reminderClock != null) _reminderClock.IsEnabled = active;
        }

        private StickyUiCommandResult CreateSession(StickyUiCommand command)
        {
            if (command.Snapshot == null ||
                !String.Equals(command.NoteId, command.Snapshot.NoteId,
                    StringComparison.OrdinalIgnoreCase))
                return StickyUiCommandResult.NotHandled();
            StickyWindowSession existing;
            if (TryGetSession(command.NoteId, out existing))
                return existing.CurrentResult();

            StickyWindowSession session = new StickyWindowSession(
                command.Snapshot, SessionEventRaised, command.Placement);
            _sessions[command.NoteId] = session;
            session.ReminderVisibilityChanged += RefreshReminderClock;
            if (command.Reminders != null)
                session.UpdateReminders(command.Reminders);
            try
            {
                // Create-time temporary rehome: a single native placement on
                // the fallback surface, never two placements with a visible
                // intermediate position.
                if (command.ReprojectTarget != null)
                    return session.Reproject(command.ReprojectTarget,
                        command.Topology, command.Flag);
                return session.Show(command.Flag, command.Topology, command.Placement);
            }
            catch
            {
                session.CloseAfterFailure();
                _sessions.Remove(command.NoteId);
                throw;
            }
        }

        private StickyUiCommandResult EnsureSession(
            StickyUiCommand command)
        {
            if (command == null ||
                command.Snapshot == null ||
                !String.Equals(command.NoteId,
                    command.Snapshot.NoteId,
                    StringComparison.OrdinalIgnoreCase))
                return StickyUiCommandResult.NotHandled();

            StickyWindowSession existing;

            if (TryGetSession(command.NoteId, out existing))
            {
                if (command.Topology != null)
                    existing.AdoptTopology(command.Topology);

                if (command.Reminders != null)
                    existing.UpdateReminders(command.Reminders);

                DisplayDiagnostics.Trace(
                    "StickySessionEnsured",
                    "note=" + command.NoteId + " created=0");

                StickyUiCommandResult current =
                    existing.CurrentResult();

                return StickyUiCommandResult.SessionEnsured(
                    current.Snapshot,
                    current.Sequence,
                    false);
            }

            StickyWindowSession session =
                new StickyWindowSession(
                    command.Snapshot,
                    SessionEventRaised);

            _sessions[command.NoteId] = session;
            session.ReminderVisibilityChanged += RefreshReminderClock;

            try
            {
                if (command.Topology != null &&
                    !session.AdoptTopology(command.Topology))
                    throw new InvalidOperationException(
                        "Could not adopt topology.");

                if (command.Reminders != null)
                    session.UpdateReminders(command.Reminders);

                DisplayDiagnostics.Trace(
                    "StickySessionEnsured",
                    "note=" + command.NoteId + " created=1");

                // No Show(), no placement, no focus.
                StickyUiCommandResult current =
                    session.CurrentResult();

                return StickyUiCommandResult.SessionEnsured(
                    current.Snapshot,
                    current.Sequence,
                    true);
            }
            catch
            {
                session.CloseAfterFailure();
                _sessions.Remove(command.NoteId);
                throw;
            }
        }

        // Preparation, placement and visibility share one STA turn. A failed
        // attempt closes only sessions it created; existing HWNDs roll back in
        // ApplyDockGroupReproject and retain their original visibility/content.
        private StickyUiCommandResult RestoreDockGroup(StickyUiCommand command)
        {
            DockRestoreOperation operation = command.DockRestore;
            if (operation == null || operation.Cancellation.IsCancellationRequested ||
                !IsCurrentTopology(operation.Topology)) return StickyUiCommandResult.NotHandled();
            var created = new Dictionary<string, StickyWindowSession>(StringComparer.OrdinalIgnoreCase);
            bool completed = false;
            try
            {
                foreach (StickyNoteUiSnapshot snapshot in operation.Snapshots)
                {
                    if (operation.Cancellation.IsCancellationRequested) return StickyUiCommandResult.NotHandled();
                    StickyUiCommandResult ensured = EnsureSession(StickyUiCommand.EnsureSession(
                        snapshot, command.Reminders, operation.Topology));
                    if (ensured.Status != StickyUiCommandStatus.Handled) return ensured;
                    if (ensured.SessionCreated) created.Add(snapshot.NoteId, _sessions[snapshot.NoteId]);
                }
                StickyUiCommandResult result = ApplyDockGroupReproject(command);
                if (result.Status != StickyUiCommandStatus.Handled) return result;
                DockBatchResult batch = result.DockBatchResult;
                var members = new List<DockBatchMemberResult>(batch.Members.Count);
                foreach (DockBatchMemberResult member in batch.Members)
                    members.Add(new DockBatchMemberResult(member.NoteId, member.WindowSequence,
                        member.Facts, member.Snapshot, created.ContainsKey(member.NoteId)));
                completed = true;
                return StickyUiCommandResult.Handled(new DockBatchResult(batch.PlanSequence,
                    batch.TopologyGeneration, batch.TargetSurfaceId, batch.TargetDpi, members));
            }
            finally
            {
                if (!completed)
                    foreach (KeyValuePair<string, StickyWindowSession> pair in created)
                    {
                        pair.Value.CloseAfterFailure();
                        _sessions.Remove(pair.Key);
                    }
            }
        }

        private bool TryGetSession(string noteId,
            out StickyWindowSession session)
        {
            return _sessions.TryGetValue(noteId ?? String.Empty, out session) &&
                session != null && session.IsAvailable;
        }

        // One drag-start Z-order transaction for a whole Dock group. The
        // entire expected member set is validated first (ids, sessions, HWNDs,
        // current generation and epoch), then the tail-to-root non-source
        // members are raised and the source is raised last. No geometry,
        // persistence or focus effect may ride along.
        // R22 local driver. These methods stay on Sticky STA and intentionally
        // are not selected by SessionEventRaised until R23 can atomically
        // commit the resulting gesture completion.
        private long TryBeginLocalDockGesture(
            StickyDockLocalGestureKind kind, string sourceNoteId)
        {
            PrepareLocalDockTopology();
            long gestureId =
                _localDockGestures.TryBegin(kind, sourceNoteId);
            if (gestureId == 0) return 0;

            if (kind == StickyDockLocalGestureKind.HeaderDrag)
            {
                string parent =
                    _localDockGestures.SplitGuideParentNoteId;
                WindowFacts parentFacts =
                    CaptureLocalDockFacts(parent);
                if (parentFacts != null)
                    ShowSplitGuide(LocalDockSeam(
                        parentFacts.PhysicalBounds));
            }
            return gestureId;
        }

        private bool MoveLocalDockHeader(WindowFacts sourceFacts)
        {
            if (!_localDockGestures.MoveHeader(sourceFacts))
                return false;
            if (String.IsNullOrEmpty(
                _localDockGestures.SplitGuideParentNoteId))
                ClearSplitGuideOnThread();
            string parent =
                _localDockGestures.LastSnapTargetNoteId;
            WindowFacts parentFacts =
                CaptureLocalDockFacts(parent);
            UpdateDockPreview(
                String.IsNullOrEmpty(parent)
                    ? String.Empty : parent,
                String.Empty,
                parentFacts == null
                    ? Rectangle.Empty
                    : LocalDockSeam(
                        parentFacts.PhysicalBounds));
            return true;
        }

        private bool ResizeLocalDockHorizontal(
            int proposedLeft, int proposedWidth)
        {
            return _localDockGestures.ResizeHorizontal(
                proposedLeft, proposedWidth);
        }

        private bool ResizeLocalDockDivider(
            int proposedSourceHeight)
        {
            return _localDockGestures.ResizeDivider(
                proposedSourceHeight);
        }

        private StickyDockLocalGestureCompletion
            CompleteLocalDockGesture()
        {
            ClearDockPreviewOnThread();
            ClearSplitGuideOnThread();
            return _localDockGestures.Complete();
        }

        private static Rectangle LocalDockSeam(
            PhysicalRect bounds)
        {
            return new Rectangle(bounds.Left,
                bounds.Bottom - 3, bounds.Width, 6);
        }

        private bool TryHandleLocalDockEvent(
            StickyWindowSession session, StickyUiEvent value)
        {
            StickyDockLocalGestureKind kind;
            bool start = false;
            bool live = false;
            bool complete = false;

            switch (value.Kind)
            {
                case StickyUiEventKind.HeaderDragStarted:
                    kind = StickyDockLocalGestureKind.HeaderDrag;
                    start = true;
                    break;
                case StickyUiEventKind.HeaderDragMoved:
                    kind = StickyDockLocalGestureKind.HeaderDrag;
                    live = true;
                    break;
                case StickyUiEventKind.HeaderDragCompleted:
                    kind = StickyDockLocalGestureKind.HeaderDrag;
                    complete = true;
                    break;
                case StickyUiEventKind.DockHorizontalResizeStarted:
                    kind = StickyDockLocalGestureKind.HorizontalResize;
                    start = true;
                    break;
                case StickyUiEventKind.DockHorizontalResizing:
                    kind = StickyDockLocalGestureKind.HorizontalResize;
                    live = true;
                    break;
                case StickyUiEventKind.DockHorizontalResizeCompleted:
                    kind = StickyDockLocalGestureKind.HorizontalResize;
                    complete = true;
                    break;
                case StickyUiEventKind.DockDividerResizeStarted:
                    kind = StickyDockLocalGestureKind.DividerResize;
                    start = true;
                    break;
                case StickyUiEventKind.DockDividerResizing:
                    kind = StickyDockLocalGestureKind.DividerResize;
                    live = true;
                    break;
                case StickyUiEventKind.DockDividerResizeCompleted:
                    kind = StickyDockLocalGestureKind.DividerResize;
                    complete = true;
                    break;
                default:
                    return false;
            }

            if (start)
            {
                // Fail closed: never fall back to the old per-frame Pet path.
                if (_pendingLocalDockRollback != null)
                {
                    ApplyLocalDockCorrections(
                        _pendingLocalDockRollback);
                    _pendingLocalDockRollback = null;
                }
                if (!_dockCommitQueue.CanBeginGesture)
                    return true;
                _activeLocalDockDependency =
                    _dockCommitQueue.LatestPendingGestureId;
                if (TryBeginLocalDockGesture(
                    kind, value.NoteId) == 0)
                    _activeLocalDockDependency = 0;
                return true;
            }

            if (live)
            {
                if (!_localDockGestures.IsActive)
                    return true;
                bool applied =
                    kind == StickyDockLocalGestureKind.HeaderDrag
                        ? MoveLocalDockHeader(value.Facts)
                    : kind ==
                        StickyDockLocalGestureKind.HorizontalResize
                        ? ResizeLocalDockHorizontal(
                            value.Left, value.Width)
                        : ResizeLocalDockDivider(value.Height);
                if (!applied)
                {
                    _pendingLocalDockRollback =
                        _localDockGestures.CancelAndRestore();
                    _activeLocalDockDependency = 0;
                    ClearDockPreviewOnThread();
                    ClearSplitGuideOnThread();
                }
                return true;
            }

            if (complete)
            {
                if (!_localDockGestures.IsActive)
                {
                    if (_pendingLocalDockRollback != null)
                    {
                        ApplyLocalDockCorrections(
                            _pendingLocalDockRollback);
                        _pendingLocalDockRollback = null;
                    }
                    return true;
                }
                if (kind == StickyDockLocalGestureKind.HeaderDrag &&
                    value.Facts != null)
                {
                    MoveLocalDockHeader(value.Facts);
                    IReadOnlyList<DockWindowTarget> snap =
                        _localDockGestures
                            .BuildHeaderSnapTargets();
                    if (snap.Count > 0)
                        ApplyLocalDockCorrections(snap);
                }
                else if (kind ==
                    StickyDockLocalGestureKind.DividerResize &&
                    value.Height > 0)
                    ResizeLocalDockDivider(value.Height);

                StickyDockLocalGestureCompletion completion =
                    CompleteLocalDockGesture();
                if (completion == null)
                    return true;
                _localDockGestures.ApplyProvisional(completion);
                StickyDockGestureCommit commit =
                    CaptureLocalDockCommit(
                        completion,
                        _activeLocalDockDependency);
                _activeLocalDockDependency = 0;
                if (commit != null &&
                    _dockCommitQueue.TryAdd(commit))
                    PumpLocalDockCommits();
                return true;
            }
            return true;
        }

        private StickyDockGestureCommit CaptureLocalDockCommit(
            StickyDockLocalGestureCompletion completion,
            long dependsOnGestureId)
        {
            if (completion == null) return null;
            List<DockBatchMemberResult> members =
                new List<DockBatchMemberResult>();
            foreach (string noteId in
                completion.AffectedMemberIds)
            {
                StickyWindowSession member;
                if (!TryGetSession(noteId, out member))
                    return null;
                DockBatchMemberResult captured =
                    member.CaptureDockCommitMember();
                if (captured == null ||
                    captured.Facts == null)
                    return null;
                members.Add(captured);
            }
            Dictionary<string, long> versions =
                new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, long> pair in
                completion.BaselineVersions)
                versions[pair.Key] = pair.Value;
            return new StickyDockGestureCommit(
                completion.GestureId,
                dependsOnGestureId,
                completion.Intent,
                completion.SourceNoteId,
                completion.TargetNoteId,
                completion.TopologyGeneration,
                versions,
                members);
        }

        private void PumpLocalDockCommits()
        {
            StickyDockGestureCommit ready =
                _dockCommitQueue.PeekReady();
            if (ready == null) return;
            long sequence = 0;
            foreach (DockBatchMemberResult member in ready.Members)
                if (String.Equals(member.NoteId,
                    ready.SourceNoteId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    sequence = member.WindowSequence;
                    break;
                }
            PostEvent(StickyUiEvent.DockCommitRequested(
                ready, sequence));
        }

        private StickyUiCommandResult PrepareLocalDockStructure(
            IEnumerable<string> affectedNoteIds)
        {
            if (_pendingLocalDockRollback != null)
            {
                ApplyLocalDockCorrections(
                    _pendingLocalDockRollback);
                _pendingLocalDockRollback = null;
            }
            if (!_localDockGestures.AffectsStructure(
                affectedNoteIds))
                return StickyUiCommandResult.Handled();

            IReadOnlyList<DockWindowTarget> restore =
                _localDockGestures.CancelAndRestore();
            _activeLocalDockDependency = 0;
            ClearDockPreviewOnThread();
            ClearSplitGuideOnThread();
            if (restore.Count > 0)
                ApplyLocalDockCorrections(restore);
            return StickyUiCommandResult.Handled();
        }

        private StickyUiCommandResult AcknowledgeLocalDockCommit(
            StickyDockCommitAck ack)
        {
            if (ack == null)
                return StickyUiCommandResult.NotHandled();
            StickyDockCommitResolution resolution =
                _dockCommitQueue.Acknowledge(ack);
            if (!resolution.Matched)
                return StickyUiCommandResult.Handled();

            if (!resolution.Accepted &&
                ack.Corrections.Count > 0)
                ApplyLocalDockCorrections(
                    ack.Corrections);

            if (!resolution.Accepted &&
                _localDockGestures.IsActive &&
                (_activeLocalDockDependency ==
                    resolution.GestureId ||
                 ContainsGestureId(
                    resolution.CancelledDependents,
                    _activeLocalDockDependency)))
            {
                _localDockGestures.Cancel();
                _activeLocalDockDependency = 0;
                ClearDockPreviewOnThread();
                ClearSplitGuideOnThread();
            }

            if (ack.Scene != null)
            {
                if (_localDockGestures.IsActive ||
                    _dockCommitQueue.PendingCount > 0)
                    _deferredDockScene = ack.Scene;
                else
                {
                    _localDockGestures.SetScene(ack.Scene);
                    _deferredDockScene = null;
                }
            }

            PumpLocalDockCommits();
            if (!_localDockGestures.IsActive &&
                _dockCommitQueue.PendingCount == 0 &&
                _deferredDockScene != null)
            {
                _localDockGestures.SetScene(
                    _deferredDockScene);
                _deferredDockScene = null;
            }
            return StickyUiCommandResult.Handled();
        }

        private void ApplyLocalDockCorrections(
            IReadOnlyList<DockWindowTarget> targets)
        {
            DisplayTopologySnapshot topology;
            lock (_configurationGate)
                topology = _currentTopology;
            if (topology == null || targets == null)
                return;

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>();
            foreach (DockWindowTarget target in targets)
            {
                StickyWindowSession session;
                if (target == null ||
                    !TryGetSession(target.NoteId,
                        out session))
                    continue;
                sessions.Add(session);
                session.SetEventsSuppressed(true);
            }
            try
            {
                foreach (DockWindowTarget target in targets)
                {
                    StickyWindowSession session;
                    if (target == null ||
                        !TryGetSession(target.NoteId,
                            out session))
                        continue;
                    PhysicalRect rect =
                        target.PhysicalBounds;
                    session.SetBounds(
                        new StickyUiBounds(
                            rect.Left, rect.Top,
                            rect.Width, rect.Height),
                        topology);
                }
            }
            finally
            {
                foreach (StickyWindowSession session
                    in sessions)
                    session.SetEventsSuppressed(false);
                ApplySideTabZOrder();
            }
        }

        private static bool ContainsGestureId(
            IReadOnlyList<long> values, long gestureId)
        {
            if (values == null || gestureId <= 0)
                return false;
            foreach (long value in values)
                if (value == gestureId) return true;
            return false;
        }

        private void SessionEventRaised(StickyWindowSession session,
            StickyUiEvent value)
        {
            if (session == null || value == null) return;
            if (TryHandleLocalDockEvent(session, value))
            {
                ApplySideTabZOrder();
                return;
            }
            if (value.Kind == StickyUiEventKind.Closed)
            {
                StickyWindowSession current;
                if (_sessions.TryGetValue(value.NoteId, out current) &&
                    Object.ReferenceEquals(current, session))
                    _sessions.Remove(value.NoteId);
                RefreshReminderClock();
            }

            if (value.Kind == StickyUiEventKind.BoundsChanged ||
                value.Kind == StickyUiEventKind.HeaderDragStarted ||
                value.Kind == StickyUiEventKind.HeaderDragMoved ||
                value.Kind == StickyUiEventKind.HeaderDragCompleted ||
                value.Kind == StickyUiEventKind.DockHorizontalResizing ||
                value.Kind == StickyUiEventKind.DockDividerResizing ||
                value.Kind == StickyUiEventKind.UserResizeCompleted ||
                value.Kind == StickyUiEventKind.FirstRendered ||
                value.Kind == StickyUiEventKind.Closed)
                ApplySideTabZOrder();

            PostEvent(value);
        }

        private StickyUiCommandResult CloseAllSessions()
        {
            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>(_sessions.Values);
            bool imeActive = false;
            foreach (StickyWindowSession session in sessions)
            {
                if (!session.IsAvailable ||
                    !session.IsImeCompositionActive) continue;
                imeActive = true;
                session.ReportImeCompositionActive();
            }
            if (imeActive) return StickyUiCommandResult.NotAccepted();

            foreach (StickyWindowSession session in sessions)
                session.SetEventsSuppressed(true);
            try
            {
                List<StickyUiFinalSnapshot> finalSnapshots =
                    new List<StickyUiFinalSnapshot>();
                foreach (StickyWindowSession session in sessions)
                    if (session.IsAvailable)
                        finalSnapshots.Add(session.FlushAndCaptureFinal());
                foreach (StickyWindowSession session in sessions)
                    if (session.IsAvailable) session.CloseForBatch();
                _sessions.Clear();
                return StickyUiCommandResult.Handled(
                    finalSnapshots.ToArray());
            }
            finally
            {
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(false);
            }
        }

        // One narrow dispatcher frame for a live dock drag: take the newest
        // immutable plan, validate its topology generation, move every
        // follower in one deferred native batch, then publish final detached
        // snapshots. Geometry events stay suppressed so the drag never yields
        // a stale coordinate chase on the following members.
        // DRT-11 group topology transition. All members are hidden and
        // bootstrapped onto one surface before its real HWND DPI is known;
        // only then is one physical plan built and applied in one native
        // batch. Any failure restores every original rect and visibility.
        private StickyUiCommandResult ApplyDockGroupReproject(
            StickyUiCommand command)
        {
            DockGroupReprojectPlan request = command == null ? null :
                command.DockGroupReprojectPlan;
            DisplayTopologySnapshot topology;
            lock (_configurationGate) topology = _currentTopology;
            if (request == null || command.Topology == null ||
                (command.DockRestore != null && command.DockRestore.Cancellation.IsCancellationRequested) ||
                topology == null ||
                request.TopologyGeneration != topology.Generation ||
                command.Topology.Generation != topology.Generation)
                return StickyUiCommandResult.NotHandled();
            DisplaySurfaceSnapshot targetSurface =
                topology.FindByRuntimeSurfaceId(request.TargetSurfaceId);
            if (targetSurface == null) return StickyUiCommandResult.NotHandled();

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>();
            foreach (string noteId in request.MemberIds)
            {
                StickyWindowSession session;
                if (!TryGetSession(noteId, out session))
                    return StickyUiCommandResult.NotHandled();
                sessions.Add(session);
            }

            foreach (StickyWindowSession session in sessions)
                if (!session.AdoptTopology(topology))
                    return StickyUiCommandResult.NotHandled();

            List<StickyWindowSession.DockDpiTransition> transitions =
                new List<StickyWindowSession.DockDpiTransition>();
            bool placementApplied = false;
            foreach (StickyWindowSession session in sessions)
                session.SetEventsSuppressed(true);
            try
            {
                int targetDpi = 0;
                foreach (StickyWindowSession session in sessions)
                {
                    StickyWindowSession.DockDpiTransition transition;
                    int memberDpi;
                    if (!session.TryPrepareDockTargetSurface(targetSurface,
                        out transition, out memberDpi))
                        return StickyUiCommandResult.NotHandled();
                    transitions.Add(transition);
                    if (targetDpi > 0 && memberDpi != targetDpi)
                        return StickyUiCommandResult.NotHandled();
                    if (targetDpi == 0) targetDpi = memberDpi;
                }

                List<IntPtr> handles = new List<IntPtr>();
                foreach (StickyWindowSession session in sessions)
                {
                    IntPtr hwnd = session.PlacementHwnd;
                    if (hwnd == IntPtr.Zero)
                    {
                        DisplayDiagnostics.Trace(
                            "DockRestoreGroupRejected",
                            "stage=handle-after-bootstrap note=" +
                            session.NoteId);
                        return StickyUiCommandResult.NotHandled();
                    }
                    handles.Add(hwnd);
                }

                DockPlacementPlan plan;
                try
                {
                    plan = DockPlacementPlanner.PlanReproject(request,
                        targetSurface, targetDpi);
                }
                catch (ArgumentException)
                {
                    return StickyUiCommandResult.NotHandled();
                }
                List<PhysicalRect> rects = new List<PhysicalRect>();
                foreach (DockWindowTarget target in plan.WindowTargets)
                    rects.Add(target.PhysicalBounds);
                if (WindowsBatchWindowPlacementExecutor.Apply(handles,
                    rects) != WindowsBatchPlacementStatus.Applied)
                    return StickyUiCommandResult.NotHandled();

                DisplayTopologySnapshot current;
                lock (_configurationGate) current = _currentTopology;
                if (current == null ||
                    current.Generation != request.TopologyGeneration ||
                    (command.DockRestore != null && command.DockRestore.Cancellation.IsCancellationRequested))
                    return StickyUiCommandResult.NotHandled();
                List<DockBatchMemberResult> members =
                    new List<DockBatchMemberResult>();
                foreach (StickyWindowSession session in sessions)
                {
                    DockBatchMemberResult member =
                        session.CaptureDockMember(topology);
                    if (member == null || member.Facts == null ||
                        member.Facts.Dpi != targetDpi ||
                        !String.Equals(member.Facts.RuntimeGdiName,
                            targetSurface.RuntimeGdiName,
                            StringComparison.OrdinalIgnoreCase))
                        return StickyUiCommandResult.NotHandled();
                    members.Add(member);
                }
                lock (_configurationGate) current = _currentTopology;
                if (current == null ||
                    current.Generation != request.TopologyGeneration ||
                    (command.DockRestore != null && command.DockRestore.Cancellation.IsCancellationRequested))
                    return StickyUiCommandResult.NotHandled();
                if (command.Flag)
                {
                    foreach (StickyWindowSession session in sessions)
                    {
                        if (!session.TryShowCurrentPlacement())
                        {
                            DisplayDiagnostics.Trace("DockRestoreGroupRejected",
                                "stage=show-current-placement note=" + session.NoteId);
                            return StickyUiCommandResult.NotHandled();
                        }
                    }
                    if (command.DockRestore != null && command.DockRestore.Cancellation.IsCancellationRequested)
                        return StickyUiCommandResult.NotHandled();
                    foreach (StickyWindowSession session in sessions)
                        session.CommitRestoredVisibleState();
                }
                placementApplied = true;
                return StickyUiCommandResult.Handled(new DockBatchResult(
                    request.PlanSequence, request.TopologyGeneration,
                    request.TargetSurfaceId, targetDpi, members));
            }
            finally
            {
                for (int index = transitions.Count - 1;
                    index >= 0; index--)
                    sessions[index].CompleteDockTargetDpi(
                        transitions[index], placementApplied, command.Flag);
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(false);
            }
        }

        // One detached actual-facts capture for a dock-commit continuation.
        // Facts are captured with the host's current topology so the Pet can
        // only accept same-generation geometry.
        private void PostEvent(StickyUiEvent value)
        {
            if (value.BeginsDockInput) _currentDockInput = value.Input;
            Action<StickyUiEvent> handler;
            SynchronizationContext context;
            lock (_configurationGate)
            {
                handler = _eventHandler;
                context = _eventContext;
            }
            if (handler == null) return;
            if (context != null)
            {
                context.Post(delegate { handler(value); }, null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { handler(value); });
        }

        internal void StopAcceptingCommands()
        {
            _threadHost.StopAcceptingCommands();
        }

        internal void BeginShutdown()
        {
            _threadHost.BeginShutdown(CloseSessionsForShutdown);
        }

        private void CloseSessionsForShutdown()
        {
            if (_reminderClock != null) _reminderClock.Stop();
            foreach (StickyWindowSession session in
                new List<StickyWindowSession>(_sessions.Values))
                session.CloseForHostShutdown();
            _sessions.Clear();

            ClearDockPreviewOnThread();
            ClearSplitGuideOnThread();
            if (_leftNoteTabs != null && !_leftNoteTabs.IsDisposed)
                _leftNoteTabs.Close();
            if (_rightNoteTabs != null && !_rightNoteTabs.IsDisposed)
                _rightNoteTabs.Close();
            _leftNoteTabs = null;
            _rightNoteTabs = null;

            if (_reminderClock != null) _reminderClock.Stop();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        internal bool WaitForExit(int timeoutMilliseconds)
        {
            return _threadHost.WaitForExit(timeoutMilliseconds);
        }

        public void Dispose()
        {
            BeginShutdown();
        }
    }
}
