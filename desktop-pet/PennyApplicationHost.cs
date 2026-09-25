using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class PennyApplicationHost
    {
        private static Mutex _singleInstance;

        internal static void Run()
        {
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
                PetForm pet = new PetForm(settings);
                bool runtimeLoadStarted = false;
                pet.ShellReady += delegate
                {
                    if (runtimeLoadStarted) return;
                    runtimeLoadStarted = true;
                    BeginRuntimeComposition(pet);
                };
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

        private static void BeginRuntimeComposition(PetForm pet)
        {
            Task.Run(delegate
            {
                return StickyFeature.PrepareLoad();
            }).ContinueWith(delegate(Task<StickyLoadResult> task)
            {
                Exception failure = task.IsFaulted && task.Exception != null
                    ? task.Exception.GetBaseException() : null;
                StickyLoadResult prepared = task.Status ==
                    TaskStatus.RanToCompletion ? task.Result : null;

                if (pet == null || pet.IsDisposed || pet.Disposing) return;
                try
                {
                    pet.BeginInvoke((MethodInvoker)delegate
                    {
                        CompleteRuntimeComposition(pet, prepared, failure);
                    });
                }
                catch (InvalidOperationException)
                {
                    // The shell closed while disk preparation was completing.
                    // Never recreate windows or publish late runtime state.
                }
            }, TaskScheduler.Default);
        }

        private static void CompleteRuntimeComposition(PetForm pet,
            StickyLoadResult prepared, Exception failure)
        {
            if (pet == null || pet.IsDisposed || pet.Disposing ||
                pet.IsExitingForComposition) return;

            UnsupportedStickySchemaException future =
                failure as UnsupportedStickySchemaException ??
                StickyFeature.PreparedFutureSchemaError(prepared);
            if (future != null)
            {
                MessageBox.Show(pet, BuildFutureSchemaBlockedMessage(future),
                    "Penny pet - 便利贴数据版本不兼容",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pet.AbortStartupComposition();
                return;
            }

            if (failure != null)
            {
                ApplicationDiagnostics.ReportFatal(
                    "runtime-composition-load", failure);
                MessageBox.Show(pet,
                    "Penny pet 无法安全恢复便利贴数据。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pet.AbortStartupComposition();
                return;
            }

            try
            {
                pet.AttachPreparedStickyRuntime(prepared);
            }
            catch (UnsupportedStickySchemaException error)
            {
                MessageBox.Show(pet, BuildFutureSchemaBlockedMessage(error),
                    "Penny pet - 便利贴数据版本不兼容",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pet.AbortStartupComposition();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportFatal(
                    "runtime-composition-publish", error);
                MessageBox.Show(pet,
                    "Penny pet 无法完成后台功能初始化。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pet.AbortStartupComposition();
            }
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
