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
            if (handler == null || dispatcher == null ||
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
                            result = handler(command) ??
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
