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
                return StickyUiCommandResult.Handled();
            }

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>();
            List<IntPtr> handles = new List<IntPtr>();
            List<PhysicalRect> rects = new List<PhysicalRect>();
            foreach (DockWindowTarget target in plan.WindowTargets)
            {
                if (String.Equals(target.NoteId, plan.SourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                StickyWindowSession session;
                if (!TryGetSession(target.NoteId, out session)) continue;
                IntPtr handle = session.PlacementHwnd;
                if (handle == IntPtr.Zero) continue;
                sessions.Add(session);
                handles.Add(handle);
                rects.Add(target.PhysicalBounds);
            }
            if (sessions.Count == 0) return StickyUiCommandResult.Handled();

            foreach (StickyWindowSession session in sessions)
                session.SetEventsSuppressed(true);
            try
            {
                WindowsBatchPlacementStatus status =
                    WindowsBatchWindowPlacementExecutor.Apply(
                        handles, rects);
                if (status != WindowsBatchPlacementStatus.Applied)
                {
                    DisplayDiagnostics.Trace("DockBatchApplied",
                        "batch failed status=" + status +
                        " plan=" + plan.PlanSequence +
                        " followers=" + sessions.Count);
                    DisplayTopologySnapshot current;
                    lock (_configurationGate) current = _currentTopology;
                    if (current != null &&
                        current.Generation != plan.TopologyGeneration)
                        return StickyUiCommandResult.Handled();
                    // Bounded per-window fallback, once, no loop.
                    for (int index = 0; index < sessions.Count; index++)
                    {
                        PhysicalRect rect = rects[index];
                        sessions[index].SetBounds(new StickyUiBounds(
                            rect.Left, rect.Top, rect.Width, rect.Height));
                    }
                }
                List<DockBatchMemberResult> members =
                    new List<DockBatchMemberResult>();
                foreach (StickyWindowSession session in sessions)
                {
                    DockBatchMemberResult member =
                        session.CaptureDockMember(topology);
                    if (member != null) members.Add(member);
                }
                return StickyUiCommandResult.Handled(new DockBatchResult(
                    plan.PlanSequence, plan.TopologyGeneration, members));
            }
            finally
            {
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
