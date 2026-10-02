using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PennyPet
{
    internal static class ArtPackageBenchmark
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 2) return 2;
            var result = new Dictionary<string, object>();
            try
            {
                if (args[0] != "startup" && args[0] != "verify")
                    throw new ArgumentException("Expected startup or verify mode.");
                using (Process process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    long privateBefore = process.PrivateMemorySize64;
                    TimeSpan cpuBefore = process.TotalProcessorTime;
                    Stopwatch watch = Stopwatch.StartNew();
                    using (PetArtPackage package = PetArtPackage.Load(192, 208))
                    {
                        AnimationClip idle = package.GetClip(0);
                        watch.Stop();
                        process.Refresh();
                        result["idleReadyMs"] = watch.Elapsed.TotalMilliseconds;
                        result["idleCpuMs"] = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                        result["idlePrivateBytesDelta"] = process.PrivateMemorySize64 - privateBefore;
                        result["idlePeakWorkingSetBytes"] = process.PeakWorkingSet64;
                        result["idleSource"] = idle.Source;
                        result["idleFrames"] = idle.FrameCount;
                        RequireEmbedded(idle, 0);
                        var loaded = new List<string>();
                        for (int row = 0; row < PetArtPackage.RuntimeStateNames.Length; row++)
                            if (package.IsRowLoaded(row)) loaded.Add(PetArtPackage.RuntimeStateNames[row]);
                        result["initialLoadedStates"] = loaded;

                        if (args[0] == "verify")
                        {
                            var states = new List<object>();
                            for (int row = 0; row < PetArtPackage.RuntimeStateNames.Length; row++)
                            {
                                AnimationClip clip = package.GetClip(row);
                                RequireEmbedded(clip, row);
                                states.Add(new { state = PetArtPackage.RuntimeStateNames[row],
                                    frames = clip.FrameCount, digest = Digest(clip) });
                            }
                            process.Refresh();
                            result["allStatesPeakWorkingSetBytes"] = process.PeakWorkingSet64;
                            result["states"] = states;
                        }
                    }
                }
                result["releasePackBytes"] = ResourceLength("PennyPet.Art.ReleasePack");
                result["startupCacheBytes"] = ResourceLength("PennyPet.Art.StartupCache");
                result["executableBytes"] = new FileInfo(Assembly.GetExecutingAssembly().Location).Length;
                result["is64Bit"] = Environment.Is64BitProcess;
                result["runtime"] = Environment.Version.ToString();
                result["ok"] = true;
            }
            catch (Exception error)
            {
                result["ok"] = false;
                result["error"] = error.ToString();
            }
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(result), new UTF8Encoding(false));
            return (bool)result["ok"] ? 0 : 1;
        }

        private static void RequireEmbedded(AnimationClip clip, int row)
        {
            if (clip.FrameCount == 0 || (!clip.Source.StartsWith("embedded-pack:", StringComparison.Ordinal) &&
                !(row == 0 && clip.Source == "embedded-startup-cache")))
                throw new InvalidDataException("Probe did not use the embedded art path: " + clip.Source);
        }

        private static long ResourceLength(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                return stream == null ? 0 : stream.Length;
        }

        // Hash every premultiplied BGRA byte (including alpha), dimensions and
        // per-frame duration. Verification runs outside the startup measurement.
        private static string Digest(AnimationClip clip)
        {
            using (SHA256 hash = SHA256.Create())
            using (var sink = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(sink, Encoding.UTF8, true))
            {
                writer.Write(clip.FrameCount);
                for (int index = 0; index < clip.FrameCount; index++)
                {
                    Bitmap frame = clip.Frames[index];
                    writer.Write(frame.Width);
                    writer.Write(frame.Height);
                    writer.Write(clip.FrameDuration(index));
                    var pixels = new byte[frame.Width * 4];
                    BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        for (int y = 0; y < frame.Height; y++)
                        {
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, 0, pixels.Length);
                            writer.Write(pixels);
                        }
                    }
                    finally { frame.UnlockBits(data); }
                }
                writer.Flush();
                sink.FlushFinalBlock();
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }
    }
}
