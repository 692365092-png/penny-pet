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
            DateTime launchedUtc = DateTime.UtcNow;
            bool createdNew;
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
                PetSettings settings = PetSettings.Load();
                PetForm pet = new PetForm(settings, launchedUtc);
                pet.EnableRuntimeComposition();
                pet.Show();
                Application.Run(pet);
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

        internal static string BuildFutureSchemaBlockedMessage(
            UnsupportedStickySchemaException error)
        {
            return "便利贴数据由更新版本的 Penny 创建。\n\n" +
                "当前程序最多支持数据版本 v" +
                error.MaximumSupportedVersion + "，\n" +
                "检测到的数据版本为 v" + error.DetectedVersion +
                "。\n\n" +
                "为防止覆盖或丢失便利贴，\n" +
                "当前版本不会读取或修改这些数据。\n\n" +
                "请关闭此版本并使用更新版本的 Penny。";
        }
    }
}
