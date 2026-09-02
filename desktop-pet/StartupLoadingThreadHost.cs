using System;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class StartupLoadingThreadHost : IDisposable
    {
        private const int ReadyTimeoutMilliseconds = 5000;
        private const int ExitTimeoutMilliseconds = 5000;
        private readonly object _sync = new object();
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly ManualResetEvent _exited = new ManualResetEvent(false);
        private Thread _thread;
        private StartupLoadingForm _form;
        private Exception _failure;
        private bool _disposed;

        internal void Start(PetSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            if (_thread != null)
                throw new InvalidOperationException(
                    "Startup loading thread has already started.");
            _thread = new Thread(new ThreadStart(delegate
            {
                Run(settings);
            }));
            _thread.Name = "Penny startup loading";
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.WaitOne(ReadyTimeoutMilliseconds))
                throw new TimeoutException(
                    "Startup loading window did not become ready in time.");
            if (_failure != null)
                throw new InvalidOperationException(
                    "Startup loading window could not be shown.", _failure);
        }

        internal void Close()
        {
            Post(delegate(StartupLoadingForm form) { form.Close(); });
        }

        internal void BringToFront()
        {
            Post(delegate(StartupLoadingForm form) { form.BringToFront(); });
        }

        private void Run(PetSettings settings)
        {
            try
            {
                using (StartupLoadingForm form = new StartupLoadingForm(
                    settings))
                {
                    lock (_sync) _form = form;
                    form.Shown += delegate
                    {
                        form.BeginInvoke((MethodInvoker)delegate
                        {
                            _ready.Set();
                        });
                    };
                    Application.Run(form);
                }
            }
            catch (Exception error)
            {
                _failure = error;
            }
            finally
            {
                lock (_sync) _form = null;
                _ready.Set();
                _exited.Set();
            }
        }

        private void Post(Action<StartupLoadingForm> action)
        {
            StartupLoadingForm form;
            lock (_sync) form = _form;
            if (form == null) return;
            try
            {
                form.BeginInvoke((MethodInvoker)delegate
                {
                    if (!form.IsDisposed) action(form);
                });
            }
            catch (InvalidOperationException)
            {
                // The loading thread has already closed the form.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            Thread thread = _thread;
            if (thread == null || !thread.IsAlive ||
                _exited.WaitOne(ExitTimeoutMilliseconds))
            {
                _ready.Dispose();
                _exited.Dispose();
                return;
            }
            // The background thread may still Set() these handles later, so
            // they must not be disposed here. It is already a background
            // thread and therefore cannot block process exit.
            ApplicationDiagnostics.ReportNonFatal(
                "startup-loading-exit-timeout",
                new TimeoutException(
                    "Startup loading thread did not exit in time."));
        }
    }
}
