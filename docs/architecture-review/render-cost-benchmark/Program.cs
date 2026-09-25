using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace PennyPet
{
    internal static class RenderCostBenchmarkProgram
    {
        private const int Iterations = 240;
        private const int WarmupIterations = 24;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Usage: PennyPet.RenderCostBenchmark.exe <default|software> <report.json>");
                return 2;
            }

            string mode = (args[0] ?? String.Empty).Trim().ToLowerInvariant();
            if (mode != "default" && mode != "software")
            {
                Console.Error.WriteLine("Render mode must be default or software.");
                return 2;
            }

            string outputPath = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            if (mode == "software")
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            else
                RenderOptions.ProcessRenderMode = RenderMode.Default;

            WinForms.Application.EnableVisualStyles();
            WinForms.Application.SetCompatibleTextRenderingDefault(false);

            BenchmarkReport report = new BenchmarkReport
            {
                Mode = mode,
                OsVersion = Environment.OSVersion.VersionString,
                ProcessorCount = Environment.ProcessorCount,
                RemoteSession = WinForms.SystemInformation.TerminalServerSession,
                ProcessRenderMode = RenderOptions.ProcessRenderMode.ToString()
            };

            try
            {
                using (Graphics desktop = Graphics.FromHwnd(IntPtr.Zero))
                {
                    report.DpiX = desktop.DpiX;
                    report.DpiY = desktop.DpiY;
                }

                report.Wpf = MeasureTransparentWpf();
                report.LayeredSprite = MeasureLayeredSprite();
                report.Ok = report.Wpf != null && report.Wpf.Completed &&
                    report.LayeredSprite != null &&
                    report.LayeredSprite.Completed;
            }
            catch (Exception error)
            {
                report.Ok = false;
                report.Error = error.GetType().FullName + ": " + error.Message;
            }

            File.WriteAllText(outputPath,
                new JavaScriptSerializer().Serialize(report),
                new UTF8Encoding(false));
            Console.WriteLine(new JavaScriptSerializer().Serialize(report));
            return report.Ok ? 0 : 3;
        }

        private static WpfMeasurement MeasureTransparentWpf()
        {
            Window window = null;
            try
            {
                Border visual = new Border
                {
                    Width = 192,
                    Height = 208,
                    Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(
                            190, 235, 95, 70)),
                    CornerRadius = new CornerRadius(28),
                    Opacity = 0.95
                };
                TranslateTransform transform = new TranslateTransform();
                visual.RenderTransform = transform;

                Canvas root = new Canvas
                {
                    Background = System.Windows.Media.Brushes.Transparent
                };
                root.Children.Add(visual);

                window = new Window
                {
                    Width = 260,
                    Height = 260,
                    AllowsTransparency = true,
                    WindowStyle = WindowStyle.None,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = 24,
                    Top = 24,
                    Content = root
                };

                window.Show();
                Drain(window.Dispatcher);
                int tier = RenderCapability.Tier >> 16;

                for (int i = 0; i < WarmupIterations; i++)
                {
                    AnimateWpfFrame(visual, transform, i);
                    Drain(window.Dispatcher);
                }

                Process process = Process.GetCurrentProcess();
                process.Refresh();
                TimeSpan cpuBefore = process.TotalProcessorTime;
                long privateBefore = process.PrivateMemorySize64;
                int gdiBefore = GetGuiResources(process.Handle, 0);
                int userBefore = GetGuiResources(process.Handle, 1);

                Stopwatch elapsed = Stopwatch.StartNew();
                for (int i = 0; i < Iterations; i++)
                {
                    AnimateWpfFrame(visual, transform, i);
                    Drain(window.Dispatcher);
                }
                elapsed.Stop();

                process.Refresh();
                return new WpfMeasurement
                {
                    Completed = true,
                    RenderTier = tier,
                    Iterations = Iterations,
                    ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                    CpuMilliseconds =
                        (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                    PrivateBytesDelta = process.PrivateMemorySize64 - privateBefore,
                    GdiHandleDelta = GetGuiResources(process.Handle, 0) - gdiBefore,
                    UserHandleDelta = GetGuiResources(process.Handle, 1) - userBefore
                };
            }
            finally
            {
                if (window != null)
                {
                    try { window.Close(); }
                    catch { }
                }
            }
        }

        private static void AnimateWpfFrame(Border visual,
            TranslateTransform transform, int frame)
        {
            transform.X = frame % 9;
            transform.Y = (frame * 3) % 7;
            visual.Opacity = 0.72 + (frame % 20) * 0.01;
            visual.InvalidateVisual();
        }

        private static void Drain(Dispatcher dispatcher)
        {
            dispatcher.Invoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { }));
        }

        private static LayeredMeasurement MeasureLayeredSprite()
        {
            using (LayeredProbeForm form = new LayeredProbeForm())
            using (Bitmap bitmap = CreateProbeBitmap())
            {
                form.StartPosition = WinForms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(320, 40);
                form.ClientSize = bitmap.Size;
                form.FormBorderStyle = WinForms.FormBorderStyle.None;
                form.ShowInTaskbar = false;
                form.Show();
                WinForms.Application.DoEvents();

                for (int i = 0; i < WarmupIterations; i++)
                    LayeredSpriteRenderer.Show(form, bitmap,
                        (byte)(180 + i % 60));

                Process process = Process.GetCurrentProcess();
                process.Refresh();
                TimeSpan cpuBefore = process.TotalProcessorTime;
                long privateBefore = process.PrivateMemorySize64;
                int gdiBefore = GetGuiResources(process.Handle, 0);
                int userBefore = GetGuiResources(process.Handle, 1);

                Stopwatch elapsed = Stopwatch.StartNew();
                for (int i = 0; i < Iterations; i++)
                    LayeredSpriteRenderer.Show(form, bitmap,
                        (byte)(180 + i % 60));
                elapsed.Stop();

                process.Refresh();
                System.Drawing.Color corner = bitmap.GetPixel(0, 0);
                System.Drawing.Color center =
                    bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

                return new LayeredMeasurement
                {
                    Completed = true,
                    Iterations = Iterations,
                    ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                    CpuMilliseconds =
                        (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                    PrivateBytesDelta = process.PrivateMemorySize64 - privateBefore,
                    GdiHandleDelta = GetGuiResources(process.Handle, 0) - gdiBefore,
                    UserHandleDelta = GetGuiResources(process.Handle, 1) - userBefore,
                    SourcePixelFormat = bitmap.PixelFormat.ToString(),
                    TransparentCorner = corner.A == 0,
                    TranslucentCenter = center.A > 0 && center.A < 255,
                    LayeredStylePresent = form.LayeredStylePresent
                };
            }
        }

        private static Bitmap CreateProbeBitmap()
        {
            Bitmap bitmap = new Bitmap(192, 208,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(
                    System.Drawing.Color.FromArgb(192, 235, 95, 70)))
                {
                    graphics.FillEllipse(brush, 16, 12, 160, 184);
                }
            }
            return bitmap;
        }

        private sealed class LayeredProbeForm : WinForms.Form
        {
            private const int WsExLayered = 0x00080000;

            protected override WinForms.CreateParams CreateParams
            {
                get
                {
                    WinForms.CreateParams value = base.CreateParams;
                    value.ExStyle |= WsExLayered;
                    return value;
                }
            }

            internal bool LayeredStylePresent
            {
                get
                {
                    return (GetWindowLongPtr(Handle, -20).ToInt64() &
                        WsExLayered) != 0;
                }
            }
        }

        private sealed class BenchmarkReport
        {
            public bool Ok { get; set; }
            public string Error { get; set; }
            public string Mode { get; set; }
            public string OsVersion { get; set; }
            public int ProcessorCount { get; set; }
            public bool RemoteSession { get; set; }
            public string ProcessRenderMode { get; set; }
            public float DpiX { get; set; }
            public float DpiY { get; set; }
            public WpfMeasurement Wpf { get; set; }
            public LayeredMeasurement LayeredSprite { get; set; }
        }

        private sealed class WpfMeasurement
        {
            public bool Completed { get; set; }
            public int RenderTier { get; set; }
            public int Iterations { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public double CpuMilliseconds { get; set; }
            public long PrivateBytesDelta { get; set; }
            public int GdiHandleDelta { get; set; }
            public int UserHandleDelta { get; set; }
        }

        private sealed class LayeredMeasurement
        {
            public bool Completed { get; set; }
            public int Iterations { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public double CpuMilliseconds { get; set; }
            public long PrivateBytesDelta { get; set; }
            public int GdiHandleDelta { get; set; }
            public int UserHandleDelta { get; set; }
            public string SourcePixelFormat { get; set; }
            public bool TransparentCorner { get; set; }
            public bool TranslucentCenter { get; set; }
            public bool LayeredStylePresent { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(
            IntPtr process, int flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int index);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, index)
                : GetWindowLong32(hWnd, index);
        }
    }
}
