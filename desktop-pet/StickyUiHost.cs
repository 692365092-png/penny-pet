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
                            ? session.SetBounds(command.Bounds)
                            : StickyUiCommandResult.NotHandled();
                    case StickyUiCommandKind.ApplyDockBoundsBatch:
                        return ApplyDockBoundsBatch(command.DockBatchLayout);
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
            try { return session.Show(command.Flag, command.Topology); }
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

        // One narrow dispatcher frame for a live dock drag: apply the latest
        // follower layout once, then publish the final canonical snapshots.
        // Intermediate geometry events are suppressed so the drag never yields
        // a stale coordinate chase on the following members.
        private StickyUiCommandResult ApplyDockBoundsBatch(
            DockBatchLayout layout)
        {
            if (layout == null) return StickyUiCommandResult.NotHandled();
            List<DockLayoutTarget> targets;
            string sourceNoteId;
            lock (layout.Gate)
            {
                targets = new List<DockLayoutTarget>(layout.Targets);
                sourceNoteId = layout.SourceNoteId ?? String.Empty;
                layout.ApplyQueued = false;
            }
            if (targets.Count == 0) return StickyUiCommandResult.Handled();

            List<StickyWindowSession> sessions =
                new List<StickyWindowSession>();
            List<DockLayoutTarget> followers =
                new List<DockLayoutTarget>();
            foreach (DockLayoutTarget target in targets)
            {
                if (String.Equals(target.NoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                StickyWindowSession session;
                if (!TryGetSession(target.NoteId, out session)) continue;
                sessions.Add(session);
                followers.Add(target);
            }
            if (followers.Count == 0) return StickyUiCommandResult.Handled();

            foreach (StickyWindowSession session in sessions)
                session.SetEventsSuppressed(true);
            List<StickyUiCommandResult> results =
                new List<StickyUiCommandResult>();
            try
            {
                for (int index = 0; index < followers.Count; index++)
                {
                    DockLayoutTarget target = followers[index];
                    results.Add(sessions[index].SetBounds(
                        new StickyUiBounds(target.X, target.Y,
                            target.Width, target.Height)));
                }
            }
            finally
            {
                foreach (StickyWindowSession session in sessions)
                    session.SetEventsSuppressed(false);
            }

            List<StickyUiFinalSnapshot> finals =
                new List<StickyUiFinalSnapshot>();
            foreach (StickyUiCommandResult result in results)
            {
                if (result == null || result.Snapshot == null) continue;
                finals.Add(new StickyUiFinalSnapshot(
                    result.Snapshot, result.Sequence));
            }
            return StickyUiCommandResult.Handled(
                finals.ToArray());
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
