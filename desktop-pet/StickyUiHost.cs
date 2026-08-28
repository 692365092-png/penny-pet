using System;
using System.Threading;
using System.Windows.Threading;

namespace PennyPet
{
    // One STA thread and Dispatcher for all future sticky-note WPF windows.
    // Existing windows remain on the WinForms UI thread until they are moved
    // incrementally; this host is the stable execution boundary first.
    internal sealed class StickyUiHost : IDisposable
    {
        private readonly object _gate = new object();
        private Thread _thread;
        private Dispatcher _dispatcher;
        private Exception _startupError;
        private Func<StickyUiCommand, StickyUiCommandResult> _commandHandler;
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

        internal void SetCommandHandler(
            Func<StickyUiCommand, StickyUiCommandResult> handler)
        {
            lock (_gate) _commandHandler = handler;
        }

        internal void PostCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Func<StickyUiCommand, StickyUiCommandResult> handler;
            Dispatcher dispatcher;
            SynchronizationContext replyContext = SynchronizationContext.Current;
            bool acceptingCommands;
            lock (_gate)
            {
                acceptingCommands = _acceptingCommands;
                handler = _commandHandler;
                dispatcher = _dispatcher;
            }
            if (!acceptingCommands)
            {
                PostCompletion(replyContext, completed,
                    StickyUiCommandResult.NotAccepted());
                return;
            }
            if (handler == null || dispatcher == null ||
                dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                PostCompletion(replyContext, completed,
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
                        PostCompletion(replyContext, completed, result);
                    }));
            }
            catch (Exception error)
            {
                PostCompletion(replyContext, completed,
                    StickyUiCommandResult.Failed(error));
            }
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

        internal void StopAcceptingCommands()
        {
            lock (_gate) _acceptingCommands = false;
        }

        internal void BeginShutdown()
        {
            StopAcceptingCommands();
            Dispatcher dispatcher;
            lock (_gate) dispatcher = _dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }

        internal bool WaitForExit(int timeoutMilliseconds)
        {
            Thread thread;
            lock (_gate) thread = _thread;
            if (thread != null && thread != Thread.CurrentThread)
                return thread.Join(timeoutMilliseconds);
            return thread == null || thread == Thread.CurrentThread;
        }

        public void Dispose()
        {
            BeginShutdown();
            WaitForExit(5000);
        }
    }
}
