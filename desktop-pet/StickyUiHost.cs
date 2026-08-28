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

        internal StickyUiCommandResult SendCommand(StickyUiCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Func<StickyUiCommand, StickyUiCommandResult> handler;
            Dispatcher dispatcher;
            lock (_gate)
            {
                if (!_acceptingCommands) return StickyUiCommandResult.NotAccepted();
                handler = _commandHandler;
                dispatcher = _dispatcher;
            }
            if (handler == null || dispatcher == null ||
                dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
                return StickyUiCommandResult.NotHandled();
            try
            {
                return dispatcher.Invoke(
                    new Func<StickyUiCommandResult>(
                        delegate { return handler(command); }));
            }
            catch (Exception error)
            {
                return StickyUiCommandResult.Failed(error);
            }
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
