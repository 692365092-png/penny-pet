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
        private Action<StickyUiCommand> _commandHandler;

        internal Dispatcher Dispatcher
        {
            get { return _dispatcher; }
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

        internal void Invoke(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _dispatcher.Invoke(action);
        }

        internal T Invoke<T>(Func<T> function)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            return _dispatcher.Invoke(function);
        }

        internal void BeginInvoke(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _dispatcher.BeginInvoke(action);
        }

        internal bool CheckAccess()
        {
            return _dispatcher != null && _dispatcher.CheckAccess();
        }

        internal void SetCommandHandler(Action<StickyUiCommand> handler)
        {
            lock (_gate) _commandHandler = handler;
        }

        internal void Post(StickyUiCommand command)
        {
            Action<StickyUiCommand> handler;
            lock (_gate) handler = _commandHandler;
            if (handler == null || _dispatcher == null ||
                _dispatcher.HasShutdownStarted ||
                _dispatcher.HasShutdownFinished) return;
            _dispatcher.BeginInvoke((Action)delegate { handler(command); });
        }

        internal void Shutdown()
        {
            Dispatcher dispatcher;
            Thread thread;
            lock (_gate)
            {
                dispatcher = _dispatcher;
                thread = _thread;
            }
            if (dispatcher != null && !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(5000);
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
