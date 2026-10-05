using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace PennyPet
{
    // Owns only the sticky STA and its asynchronous scheduling boundary.
    internal sealed class StickyUiThreadHost : IDisposable
    {
        private readonly object _gate = new object();
        private Thread _thread;
        private Dispatcher _dispatcher;
        private Exception _startupError;
        private bool _acceptingCommands = true;
        private bool _faulted;
        private readonly HashSet<PendingCommand> _pendingCommands = new HashSet<PendingCommand>();
        private readonly Queue<Action> _startupRestoreQueue = new Queue<Action>();
        private bool _startupRestorePumpPosted;
        private const long StartupRestoreBudgetMilliseconds = 6;

        internal event Action<Exception> Faulted;

        internal bool IsFaultedAndExited
        {
            get
            {
                lock (_gate)
                    return _faulted && _thread != null && !_thread.IsAlive;
            }
        }

        internal void Start()
        {
            lock (_gate)
            {
                if (_thread != null) return;
                using (ManualResetEventSlim ready =
                    new ManualResetEventSlim(false))
                {
                    _thread = new Thread(new ThreadStart(delegate
                    {
                        try
                        {
                            _dispatcher = Dispatcher.CurrentDispatcher;
                            _dispatcher.UnhandledException +=
                                DispatcherUnhandledException;
                            _dispatcher.ShutdownStarted += delegate { StopAcceptingCommands(); };
                            ready.Set();
                            Dispatcher.Run();
                        }
                        catch (Exception error)
                        {
                            _startupError = error;
                            ready.Set();
                        }
                    }));
                    _thread.IsBackground = true;
                    _thread.SetApartmentState(ApartmentState.STA);
                    _thread.Name = "Penny sticky UI";
                    _thread.Start();
                    if (!ready.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException(
                            "Sticky UI thread did not start in time.");
                    if (_startupError != null)
                        throw new InvalidOperationException(
                            "Sticky UI thread failed to start.",
                            _startupError);
                }
            }
        }

        internal void Post(StickyUiCommand command,
            Func<StickyUiCommand, StickyUiCommandResult> handler,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            // When the owner thread has exited, none of its HWNDs can remain.
            // Hidden notes can still be deleted after a Sticky subsystem fault.
            if (command.Kind == StickyUiCommandKind.Close)
            {
                bool exited;
                lock (_gate) exited = _thread != null && !_thread.IsAlive;
                if (exited)
                {
                    PostCompletion(completionContext, completed, StickyUiCommandResult.Handled());
                    return;
                }
            }
            PostToDispatcher(delegate { return handler(command); },
                completed, completionContext);
        }

        internal void PostStartupRestore(Action invoke)
        {
            if (invoke == null) throw new ArgumentNullException(nameof(invoke));
            PostStartupRestore(() => { invoke(); return StickyUiCommandResult.Handled(); }, null, null);
        }

        internal void PostStartupRestore(Func<StickyUiCommandResult> invoke,
            Action<StickyUiCommandResult> completed, SynchronizationContext completionContext)
        {
            if (invoke == null) throw new ArgumentNullException(nameof(invoke));
            var pending = new PendingCommand(completed, completionContext);
            Dispatcher dispatcher;
            bool postPump = false;
            lock (_gate)
            {
                dispatcher = _dispatcher;
                if (_acceptingCommands && dispatcher != null &&
                    !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    _pendingCommands.Add(pending);
                    _startupRestoreQueue.Enqueue(() => ExecutePending(pending, invoke));
                    if (!_startupRestorePumpPosted)
                    {
                        _startupRestorePumpPosted = true;
                        postPump = true;
                    }
                }
                else dispatcher = null;
            }
            if (dispatcher == null)
            {
                pending.Complete(StickyUiCommandResult.NotAccepted());
                return;
            }
            if (!postPump) return;
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PumpStartupRestore));
            }
            catch (Exception error)
            {
                CompletePending(pending, StickyUiCommandResult.Failed(error));
                StopAcceptingCommands();
            }
        }

        private void PumpStartupRestore()
        {
            Stopwatch budget = Stopwatch.StartNew();
            while (budget.ElapsedMilliseconds < StartupRestoreBudgetMilliseconds)
            {
                Action work;
                lock (_gate)
                {
                    if (_startupRestoreQueue.Count == 0)
                    {
                        _startupRestorePumpPosted = false;
                        return;
                    }
                    work = _startupRestoreQueue.Dequeue();
                }
                try { work(); }
                catch (Exception error)
                {
                    Action<Exception> faulted = Faulted;
                    if (faulted != null) faulted(error);
                }
            }
            Dispatcher dispatcher;
            lock (_gate)
            {
                if (_startupRestoreQueue.Count == 0)
                {
                    _startupRestorePumpPosted = false;
                    return;
                }
                dispatcher = _dispatcher;
            }
            if (dispatcher != null && !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
                dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(PumpStartupRestore));
        }

        // Thread transport: callers own payload selection and cancellation.
        internal void PostToDispatcher(
            Func<StickyUiCommandResult> invoke,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            var pending = new PendingCommand(completed, completionContext);
            Dispatcher dispatcher;
            StickyUiCommandResult rejected = null;
            lock (_gate)
            {
                dispatcher = _dispatcher;
                if (!_acceptingCommands)
                    rejected = StickyUiCommandResult.NotAccepted();
                else if (invoke == null || dispatcher == null ||
                    dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    rejected = StickyUiCommandResult.NotHandled();
                else _pendingCommands.Add(pending);
            }
            if (rejected != null)
            {
                pending.Complete(rejected);
                return;
            }
            try
            {
                DispatcherOperation operation = dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(() => ExecutePending(pending, invoke)));
                operation.Aborted += delegate
                {
                    CompletePending(pending, StickyUiCommandResult.NotAccepted());
                };
                if (operation.Status == DispatcherOperationStatus.Aborted)
                    CompletePending(pending, StickyUiCommandResult.NotAccepted());
            }
            catch (Exception error)
            {
                CompletePending(pending, StickyUiCommandResult.Failed(error));
            }
        }

        private void ExecutePending(PendingCommand pending, Func<StickyUiCommandResult> invoke)
        {
            if (pending.IsCompleted) return;
            StickyUiCommandResult result;
            try { result = invoke() ?? StickyUiCommandResult.NotHandled(); }
            catch (Exception error) { result = StickyUiCommandResult.Failed(error); }
            CompletePending(pending, result);
        }

        private void CompletePending(PendingCommand pending, StickyUiCommandResult result)
        {
            lock (_gate) _pendingCommands.Remove(pending);
            pending.Complete(result);
        }

        internal void StopAcceptingCommands()
        {
            PendingCommand[] pending;
            lock (_gate)
            {
                _acceptingCommands = false;
                pending = new List<PendingCommand>(_pendingCommands).ToArray();
                _pendingCommands.Clear();
                _startupRestoreQueue.Clear();
                _startupRestorePumpPosted = false;
            }
            foreach (PendingCommand command in pending)
                command.Complete(StickyUiCommandResult.NotAccepted());
        }

        private sealed class PendingCommand
        {
            private readonly Action<StickyUiCommandResult> _completed;
            private readonly SynchronizationContext _context;
            private int _resolved;

            internal PendingCommand(Action<StickyUiCommandResult> completed, SynchronizationContext context)
            {
                _completed = completed;
                _context = context;
            }

            internal bool IsCompleted { get { return Volatile.Read(ref _resolved) != 0; } }

            internal void Complete(StickyUiCommandResult result)
            {
                if (Interlocked.Exchange(ref _resolved, 1) == 0)
                    PostCompletion(_context, _completed, result);
            }
        }

        private void DispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            HandleDispatcherFault(e == null ? null : e.Exception);
            // Contain the fault inside the sticky subsystem instead of letting
            // it crash the whole desktop pet. The dispatcher is shut down right
            // after the first fault; this flag only prevents process-level
            // default handling of that contained exception.
            e.Handled = true;
        }

        private void HandleDispatcherFault(Exception error)
        {
            bool firstFault;
            Action<Exception> handler;
            Dispatcher dispatcher;
            lock (_gate)
            {
                firstFault = !_faulted;
                _faulted = true;
                _acceptingCommands = false;
                handler = Faulted;
                dispatcher = _dispatcher;
            }
            ApplicationDiagnostics.ReportNonFatal(
                "sticky-ui-dispatcher-fault",
                error ?? new InvalidOperationException(
                    "Sticky UI dispatcher fault without an exception."));
            StopAcceptingCommands();
            if (firstFault && handler != null)
            {
                try { handler(error); }
                catch { }
            }
            if (firstFault && dispatcher != null &&
                !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
            {
                try
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
                catch { }
            }
        }

        internal void BeginShutdown(Action beforeDispatcherShutdown)
        {
            StopAcceptingCommands();
            Dispatcher dispatcher;
            lock (_gate) dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished) return;
            dispatcher.BeginInvoke(DispatcherPriority.Send,
                new Action(delegate
                {
                    try
                    {
                        if (beforeDispatcherShutdown != null)
                            beforeDispatcherShutdown();
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Send);
                    }
                }));
        }

        internal bool WaitForExit(int timeoutMilliseconds)
        {
            Thread thread;
            lock (_gate) thread = _thread;
            if (thread != null && thread != Thread.CurrentThread)
                return thread.Join(timeoutMilliseconds);
            return thread == null || thread == Thread.CurrentThread;
        }

        internal static void PostCompletionForHost(
            SynchronizationContext context,
            Action<StickyUiCommandResult> completed,
            StickyUiCommandResult result)
        {
            PostCompletion(context, completed, result);
        }

        private static void PostCompletion(SynchronizationContext context,
            Action<StickyUiCommandResult> completed,
            StickyUiCommandResult result)
        {
            if (completed == null) return;
            if (context != null)
            {
                context.Post(delegate { completed(result); }, null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { completed(result); });
        }

        public void Dispose()
        {
            BeginShutdown(null);
        }
    }
}
