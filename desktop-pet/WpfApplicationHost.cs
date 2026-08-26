namespace PennyPet
{
    internal static class WpfApplicationHost
    {
        private static System.Windows.Application _application;

        internal static void Ensure()
        {
            if (System.Windows.Application.Current != null) return;
            _application = new System.Windows.Application();
            _application.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;
            _application.DispatcherUnhandledException += delegate(object sender,
                System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                ApplicationDiagnostics.ReportFatal("wpf-dispatcher", e.Exception);
            };
        }

        internal static void Shutdown()
        {
            try
            {
                if (_application != null) _application.Shutdown();
            }
            catch { }
        }
    }
}

