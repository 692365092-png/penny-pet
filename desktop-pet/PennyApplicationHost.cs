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
                DisplayTopologySnapshot startupTopology =
                    new WindowsDisplayTopologyProvider().Capture();
                StartupPetPlacementSnapshot startupPlacement =
                    ResolveStartupPetPlacement(preloadedSettings,
                        startupTopology);
                using (StartupLoadingThreadHost loading =
                    new StartupLoadingThreadHost())
                {
                    loading.Start(startupPlacement);
                    PetForm pet = new PetForm(preloadedSettings);
                    pet.StartupReady += delegate
                    {
                        loading.Close();
                    };
                    pet.FormClosed += delegate
                    {
                        loading.Close();
                    };
                    pet.Show();
                    loading.BringToFront();
                    Application.Run(pet);
                }
            }
            catch (UnsupportedStickySchemaException error)
            {
                MessageBox.Show(BuildFutureSchemaBlockedMessage(error),
                    "Penny pet - 便利贴数据版本不兼容",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        // One-time immutable prediction of the formal Pet's first-settled
        // placement. The loading thread only projects this rect; it never
        // writes settings, mutates preference or creates runtime authority.
        private static StartupPetPlacementSnapshot ResolveStartupPetPlacement(
            PetSettings settings, DisplayTopologySnapshot topology)
        {
            System.Drawing.Size logical = PetForm.ScaledPetSize(
                settings == null ? 100 : settings.ScalePercent);
            return PetPlacementPolicy.ResolveStartupPetPlacement(
                settings == null ? String.Empty :
                    settings.PetPreferredTargetKey,
                new LogicalPoint
                {
                    X = settings == null ? 0 :
                        settings.PetPreferredLocalLogicalX,
                    Y = settings == null ? 0 :
                        settings.PetPreferredLocalLogicalY
                },
                settings != null && settings.HasLocation,
                settings == null ? 0 : settings.X,
                settings == null ? 0 : settings.Y,
                Math.Max(1, logical.Width),
                Math.Max(1, logical.Height),
                topology);
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
