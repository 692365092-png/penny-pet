using System;
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

        internal event Action<Exception> Faulted;

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
            PostToDispatcher(delegate { return handler(command); },
                completed, completionContext);
        }

        // Narrow latest-wins dispatch for one immutable Dock plan. This is a
        // dedicated Dock entry, not a generic scheduler: only the newest
        // mailbox plan is ever invoked.
        internal void PostDockPlan(DockPlanMailbox mailbox,
            Func<DockPlanMailbox, StickyUiCommandResult> handler,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (mailbox == null)
                throw new ArgumentNullException(nameof(mailbox));
            PostToDispatcher(delegate { return handler(mailbox); },
                completed, completionContext);
        }

        private void PostToDispatcher(
            Func<StickyUiCommandResult> invoke,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            Dispatcher dispatcher;
            bool acceptingCommands;
            lock (_gate)
            {
                acceptingCommands = _acceptingCommands;
                dispatcher = _dispatcher;
            }
            if (!acceptingCommands)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.NotAccepted());
                return;
            }
            if (invoke == null || dispatcher == null ||
                dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.NotHandled());
                return;
            }
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(delegate
                    {
                        StickyUiCommandResult result;
                        try
                        {
                            result = invoke() ??
                                StickyUiCommandResult.NotHandled();
                        }
                        catch (Exception error)
                        {
                            result = StickyUiCommandResult.Failed(error);
                        }
                        PostCompletion(completionContext, completed, result);
                    }));
            }
            catch (Exception error)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.Failed(error));
            }
        }

        internal void StopAcceptingCommands()
        {
            lock (_gate) _acceptingCommands = false;
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
