using System;
using System.Collections.Generic;
using System.Threading;

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
        internal void PostLatestDockPlan(DockPlanMailbox mailbox,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (mailbox == null)
                throw new ArgumentNullException(nameof(mailbox));
            _threadHost.PostDockPlan(mailbox, ApplyLatestDockPlan,
                completed, completionContext);
        }

        internal void PostFinalDockPlan(DockPlanMailbox mailbox,
            long planSequence, Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (mailbox == null)
                throw new ArgumentNullException(nameof(mailbox));
            _threadHost.PostDockPlan(mailbox, delegate(DockPlanMailbox value)
            {
                return ApplyFinalDockPlan(value, planSequence);
            }, completed, completionContext);
        }

        // Host-owned current topology truth for the Dock stale gate and for
        // actual-facts capture. Pet publishes every settled snapshot here.
        internal void SetCurrentTopology(DisplayTopologySnapshot snapshot)
        {
            lock (_configurationGate) _currentTopology = snapshot;
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
                    case StickyUiCommandKind.Show:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Show(command.Flag, command.Topology)
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
                        return TryGetSession(command.NoteId, out session)
                            ? session.SetBounds(command.Bounds,
                                command.Topology)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.Reproject:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Reproject(command.ReprojectTarget,
                                command.Topology, command.Flag)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.ReprojectDockGroup:
                        return ApplyDockGroupReproject(command);
                    case StickyUiCommandKind.CaptureDockFacts:
                        return CaptureDockFactsForCommit(command);
                    case StickyUiCommandKind.UpdateReminders:
                        return TryGetSession(command.NoteId, out session)
                            ? session.UpdateReminders(command.Reminders)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.Close:
                        return TryGetSession(command.NoteId, out session)
                            ? session.Close()
                            : StickyUiCommandResult.NotHandled();
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
                }
                throw;
            }
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
                command.Snapshot, SessionEventRaised);
            _sessions[command.NoteId] = session;
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
                return session.Show(command.Flag, command.Topology);
            }
            catch
            {
                session.CloseAfterFailure();
                _sessions.Remove(command.NoteId);
                throw;
            }
        }

        private bool TryGetSession(string noteId,
            out StickyWindowSession session)
        {
            return _sessions.TryGetValue(noteId ?? String.Empty, out session) &&
                session != null && session.IsAvailable;
        }

        private void SessionEventRaised(StickyWindowSession session,
            StickyUiEvent value)
        {
            if (session == null || value == null) return;
            if (value.Kind == StickyUiEventKind.Closed)
            {
                StickyWindowSession current;
                if (_sessions.TryGetValue(value.NoteId, out current) &&
                    Object.ReferenceEquals(current, session))
                    _sessions.Remove(value.NoteId);
            }
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
        private StickyUiCommandResult ApplyLatestDockPlan(
            DockPlanMailbox mailbox)
        {
            DockPlacementPlan plan = mailbox == null
                ? null : mailbox.TakeLatest();
            if (plan == null || plan.WindowTargets.Count == 0)
                return StickyUiCommandResult.Handled();
            return ApplyDockPlan(plan);
        }

        private StickyUiCommandResult ApplyFinalDockPlan(
            DockPlanMailbox mailbox, long planSequence)
        {
            DockPlacementPlan plan = mailbox == null
                ? null : mailbox.TakeFinal(planSequence);
            if (plan == null || plan.WindowTargets.Count == 0)
                return StickyUiCommandResult.NotHandled();
            try
            {
                return ApplyDockPlan(plan);
            }
            finally
            {
                mailbox.CompleteFinal(planSequence);
            }
        }

        private StickyUiCommandResult ApplyDockPlan(DockPlacementPlan plan)
        {
            // Stale gate against the host-owned current topology generation:
            // a plan built for generation G is never applied after Pet has
            // published G+1.
            DisplayTopologySnapshot topology;
            lock (_configurationGate) topology = _currentTopology;
            if (topology == null ||
                plan.TopologyGeneration != topology.Generation)
            {
                DisplayDiagnostics.Trace("DockPlanStale",
                    "plan=" + plan.PlanSequence + " planGeneration=" +
                    plan.TopologyGeneration + " currentGeneration=" +
                    (topology == null ? -1 : topology.Generation));
                return StickyUiCommandResult.NotHandled();
            }
            DisplaySurfaceSnapshot targetSurface =
                topology.FindByRuntimeSurfaceId(plan.TargetSurfaceId);
            if (targetSurface == null || plan.TargetDpi <= 0)
                return StickyUiCommandResult.NotHandled();

            List<StickyWindowSession> expectedSessions =
                new List<StickyWindowSession>();
            List<IntPtr> handles = new List<IntPtr>();
            List<PhysicalRect> rects = new List<PhysicalRect>();
            HashSet<string> expectedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (DockWindowTarget target in plan.WindowTargets)
            {
                if (target == null || !expectedIds.Add(target.NoteId))
                    return StickyUiCommandResult.NotHandled();
                StickyWindowSession session;
                if (!TryGetSession(target.NoteId, out session))
                {
                    DisplayDiagnostics.Trace("DockBatchApplied",
                        "missing session note=" + target.NoteId +
                        " plan=" + plan.PlanSequence);
                    return StickyUiCommandResult.NotHandled();
                }
                IntPtr handle = session.PlacementHwnd;
                if (handle == IntPtr.Zero)
                {
                    DisplayDiagnostics.Trace("DockBatchApplied",
                        "zero HWND note=" + target.NoteId +
                        " plan=" + plan.PlanSequence);
                    return StickyUiCommandResult.NotHandled();
                }
                expectedSessions.Add(session);
                if (String.Equals(target.NoteId, plan.SourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                handles.Add(handle);
                rects.Add(target.PhysicalBounds);
            }
            if (expectedSessions.Count != plan.WindowTargets.Count)
                return StickyUiCommandResult.NotHandled();

            List<StickyWindowSession> transitionSessions =
                new List<StickyWindowSession>();
            List<StickyWindowSession.DockDpiTransition> transitions =
                new List<StickyWindowSession.DockDpiTransition>();
            bool placementApplied = false;
            foreach (StickyWindowSession session in expectedSessions)
                session.SetEventsSuppressed(true);
            try
            {
                for (int index = 0;
                    index < plan.WindowTargets.Count; index++)
                {
                    DockWindowTarget target = plan.WindowTargets[index];
                    if (String.Equals(target.NoteId, plan.SourceNoteId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    StickyWindowSession.DockDpiTransition transition;
                    StickyWindowSession session = expectedSessions[index];
                    if (!session.TryPrepareDockTargetDpi(targetSurface,
                        plan.TargetDpi, out transition))
                    {
                        DisplayDiagnostics.Trace("DockBatchApplied",
                            "target DPI bootstrap failed note=" +
                            target.NoteId + " plan=" + plan.PlanSequence);
                        return StickyUiCommandResult.NotHandled();
                    }
                    transitionSessions.Add(session);
                    transitions.Add(transition);
                }
                if (handles.Count > 0)
                {
                    WindowsBatchPlacementStatus status =
                        WindowsBatchWindowPlacementExecutor.Apply(
                            handles, rects);
                    if (status != WindowsBatchPlacementStatus.Applied)
                    {
                        DisplayDiagnostics.Trace("DockBatchApplied",
                            "batch failed status=" + status +
                            " plan=" + plan.PlanSequence +
                            " followers=" + handles.Count);
                        DisplayTopologySnapshot current;
                        lock (_configurationGate) current = _currentTopology;
                        if (current != null &&
                            current.Generation != plan.TopologyGeneration)
                            return StickyUiCommandResult.NotHandled();
                        // Bounded per-window fallback, once, no loop.
                        int followerIndex = 0;
                        foreach (DockWindowTarget target in plan.WindowTargets)
                        {
                            if (String.Equals(target.NoteId,
                                plan.SourceNoteId,
                                StringComparison.OrdinalIgnoreCase)) continue;
                            StickyWindowSession session;
                            if (!TryGetSession(target.NoteId, out session))
                                return StickyUiCommandResult.NotHandled();
                            PhysicalRect rect = rects[followerIndex++];
                            session.SetBounds(new StickyUiBounds(
                                rect.Left, rect.Top, rect.Width, rect.Height));
                        }
                    }
                }
                List<DockBatchMemberResult> members =
                    new List<DockBatchMemberResult>();
                foreach (StickyWindowSession session in expectedSessions)
                {
                    DockBatchMemberResult member =
                        session.CaptureDockMember(topology);
                    if (member == null || member.Facts == null ||
                        member.Facts.Dpi != plan.TargetDpi ||
                        !String.Equals(member.Facts.RuntimeGdiName,
                            targetSurface.RuntimeGdiName,
                            StringComparison.OrdinalIgnoreCase))
                        return StickyUiCommandResult.NotHandled();
                    members.Add(member);
                }
                DisplayTopologySnapshot finalTopology;
                lock (_configurationGate) finalTopology = _currentTopology;
                if (finalTopology == null ||
                    finalTopology.Generation != plan.TopologyGeneration)
                    return StickyUiCommandResult.NotHandled();
                placementApplied = true;
                return StickyUiCommandResult.Handled(new DockBatchResult(
                    plan.PlanSequence, plan.TopologyGeneration,
                    plan.TargetSurfaceId, plan.TargetDpi, members));
            }
            finally
            {
                for (int index = transitions.Count - 1;
                    index >= 0; index--)
                    transitionSessions[index].CompleteDockTargetDpi(
                        transitions[index], placementApplied);
                foreach (StickyWindowSession session in expectedSessions)
                    session.SetEventsSuppressed(false);
            }
        }

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
                topology == null ||
                request.TopologyGeneration != topology.Generation ||
                command.Topology.Generation != topology.Generation)
                return StickyUiCommandResult.NotHandled();
            DisplaySurfaceSnapshot targetSurface =
                topology.FindByRuntimeSurfaceId(request.TargetSurfaceId);
            if (targetSurface == null) return StickyUiCommandResult.NotHandled();

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>();
            List<IntPtr> handles = new List<IntPtr>();
            foreach (DockLogicalMember member in request.Group.Members)
            {
                StickyWindowSession session;
                if (member == null ||
                    !TryGetSession(member.NoteId, out session) ||
                    session.PlacementHwnd == IntPtr.Zero)
                    return StickyUiCommandResult.NotHandled();
                sessions.Add(session);
                handles.Add(session.PlacementHwnd);
            }
            if (sessions.Count != request.Group.Members.Count)
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
                    current.Generation != request.TopologyGeneration)
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
                    current.Generation != request.TopologyGeneration)
                    return StickyUiCommandResult.NotHandled();
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
                        transitions[index], placementApplied);
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(false);
            }
        }

        // One detached actual-facts capture for a dock-commit continuation.
        // Facts are captured with the host's current topology so the Pet can
        // only accept same-generation geometry.
        private StickyUiCommandResult CaptureDockFactsForCommit(
            StickyUiCommand command)
        {
            if (command == null || command.DockNoteIds == null ||
                command.DockNoteIds.Length == 0)
                return StickyUiCommandResult.Handled();
            DisplayTopologySnapshot topology;
            lock (_configurationGate) topology = _currentTopology;
            List<DockBatchMemberResult> members =
                new List<DockBatchMemberResult>();
            foreach (string noteId in command.DockNoteIds)
            {
                StickyWindowSession session;
                if (!TryGetSession(noteId, out session)) continue;
                DockBatchMemberResult member =
                    session.CaptureDockMember(topology);
                if (member != null) members.Add(member);
            }
            return StickyUiCommandResult.Handled(new DockBatchResult(0,
                topology == null ? 0 : topology.Generation, members));
        }

        private void PostEvent(StickyUiEvent value)
        {
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
            foreach (StickyWindowSession session in
                new List<StickyWindowSession>(_sessions.Values))
                session.CloseForHostShutdown();
            _sessions.Clear();
        }

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
