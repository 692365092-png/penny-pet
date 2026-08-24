using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class ApplicationDiagnostics
    {
        private static readonly object LogGate = new object();
        private static readonly Dictionary<string, DateTime> LastNonFatalLogUtc =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static int _initialized;
        private static int _fatalHandling;

        internal static string LogFilePath
        {
            get
            {
                string directory = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "PennyPet");
                return Path.Combine(directory, "diagnostics.log");
            }
        }

        internal static void Initialize()
        {
            if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender,
                ThreadExceptionEventArgs e)
            {
                HandleUiThreadFailure(e == null ? null : e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender,
                UnhandledExceptionEventArgs e)
            {
                Exception error = e == null ? null : e.ExceptionObject as Exception;
                Write("unhandled-background", error,
                    e != null && e.IsTerminating ? "terminating" : "non-terminating");
            };
        }

        internal static void ReportNonFatal(string context, Exception error)
        {
            if (_initialized == 0) return;
            string key = context ?? "non-fatal";
            DateTime now = DateTime.UtcNow;
            lock (LogGate)
            {
                DateTime last;
                if (LastNonFatalLogUtc.TryGetValue(key, out last) &&
                    now - last < TimeSpan.FromMinutes(1)) return;
                LastNonFatalLogUtc[key] = now;
            }
            Write(key, error, "recovered");
        }

        internal static void ReportFatal(string context, Exception error)
        {
            Write(context, error, "fatal");
        }

        private static void HandleUiThreadFailure(Exception error)
        {
            Write("unhandled-ui", error, "fatal");
            if (Interlocked.Exchange(ref _fatalHandling, 1) != 0) return;
            try
            {
                MessageBox.Show(
                    "Penny pet 遇到了意外错误，将安全退出。\n\n" +
                    "诊断记录已保存到：\n" + LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            try { Application.Exit(); }
            catch { }
        }

        private static void Write(string context, Exception error, string outcome)
        {
            try
            {
                lock (LogGate)
                {
                    string path = LogFilePath;
                    string directory = Path.GetDirectoryName(path);
                    if (!String.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    {
                        string previous = path + ".previous";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    StringBuilder text = new StringBuilder();
                    text.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "] " + (context ?? "unknown") + " / " + (outcome ?? String.Empty));
                    if (error == null)
                        text.AppendLine("Unknown exception");
                    else
                        text.AppendLine(error.ToString());
                    text.AppendLine();
                    File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never become another application failure.
            }
        }
    }

    internal static class AtomicTextFile
    {
        internal static void WriteAllLines(string filePath,
            IEnumerable<string> lines, bool keepBackup)
        {
            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp";
            File.WriteAllLines(temporary,
                new List<string>(lines ?? new string[0]).ToArray(),
                new UTF8Encoding(false));
            if (File.Exists(fullPath))
            {
                string backup = keepBackup ? fullPath + ".bak" : null;
                File.Replace(temporary, fullPath, backup, true);
            }
            else
            {
                File.Move(temporary, fullPath);
            }
        }
    }
}
