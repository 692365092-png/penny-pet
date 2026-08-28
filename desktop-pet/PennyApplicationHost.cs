using System;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class PennyApplicationHost
    {
        private static Mutex _singleInstance;

        internal static void Run()
        {
            bool createdNew;
            // Local\ scopes the mutex to the current interactive session.
            // Global\ would also block other terminal-service users from
            // running a desktop pet in their own session, which is not wanted.
            _singleInstance = new Mutex(true, "Local\\PennyPet.SingleInstance",
                out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Penny pet 已经在桌面上啦。", "Penny pet");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationDiagnostics.Initialize();
            try
            {
                PetSettings preloadedSettings = PetSettings.Load();
                using (StartupLoadingForm loading = new StartupLoadingForm(
                    preloadedSettings))
                {
                    loading.Show();
                    Application.DoEvents();
                    PetForm pet = new PetForm(preloadedSettings);
                    pet.StartupReady += delegate
                    {
                        if (!loading.IsDisposed) loading.Close();
                    };
                    pet.FormClosed += delegate
                    {
                        if (!loading.IsDisposed) loading.Close();
                    };
                    pet.Show();
                    Application.DoEvents();
                    loading.BringToFront();
                    Application.Run(pet);
                }
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportFatal("application-run", error);
                MessageBox.Show(
                    "Penny pet 启动失败。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            WpfApplicationHost.Shutdown();
            GC.KeepAlive(_singleInstance);
        }
    }
}
