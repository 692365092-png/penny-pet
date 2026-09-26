using System;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static AnimationClip CreateAssetTestClip()
        {
            return new AnimationClip("asset-test", new[] { new Bitmap(2, 2) }, new[] { 40 });
        }

        private static bool RunArtTaskSharingCheck()
        {
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var ready = new ArtClipAsset(CreateAssetTestClip, CreateAssetTestClip()))
            {
                int decodes = 0;
                using (var asset = new ArtClipAsset(() => {
                    Interlocked.Increment(ref decodes);
                    started.Set();
                    if (!release.Wait(5000)) throw new TimeoutException();
                    return CreateAssetTestClip();
                }))
                {
                    Task<AnimationClip> first = asset.LoadAsync();
                    try
                    {
                        if (!started.Wait(5000)) return false;
                        var requests = new Task<AnimationClip>[20];
                        Parallel.For(0, requests.Length, i => requests[i] = asset.LoadAsync());
                        foreach (var request in requests)
                            if (!Object.ReferenceEquals(first, request)) return false;
                        // A held decoder does not block another asset's ready reads.
                        if (asset.Ready != null || ready.Ready.Frames[0].Width != 2 ||
                            !ready.LoadAsync().IsCompleted || decodes != 1) return false;
                    }
                    finally { release.Set(); }
                    if (!first.Wait(5000)) return false;
                    return Object.ReferenceEquals(first.Result, asset.Ready) &&
                        Object.ReferenceEquals(first, asset.LoadAsync()) && decodes == 1;
                }
            }
        }

        private static bool RunArtRetryCheck()
        {
            DateTime now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int decodes = 0;
            using (var asset = new ArtClipAsset(() => {
                Interlocked.Increment(ref decodes);
                throw new InvalidDataException("broken optional clip");
            }, null, () => now))
            {
                var first = asset.LoadAsync();
                if (!SpinWait.SpinUntil(() => first.IsCompleted, 5000) || !first.IsFaulted) return false;
                if (!Object.ReferenceEquals(first, asset.LoadAsync())) return false;
                now = now.AddSeconds(1);
                var second = asset.LoadAsync();
                if (Object.ReferenceEquals(first, second) ||
                    !SpinWait.SpinUntil(() => second.IsCompleted, 5000) || !second.IsFaulted) return false;
                now = now.AddDays(1);
                return decodes == 2 && asset.Ready == null &&
                    Object.ReferenceEquals(second, asset.LoadAsync());
            }
        }

        private static bool RunArtShutdownCheck()
        {
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                AnimationClip decoded = CreateAssetTestClip();
                using (var asset = new ArtClipAsset(() => {
                    started.Set();
                    if (!release.Wait(5000)) throw new TimeoutException();
                    return decoded;
                }))
                {
                    var pending = asset.LoadAsync();
                    try
                    {
                        if (!started.Wait(5000)) return false;
                        asset.Dispose(); // Must finish before the decoder is released.
                        if (!pending.IsCanceled || asset.Ready != null ||
                            !asset.LoadAsync().IsCanceled) return false;
                    }
                    finally { release.Set(); }
                    // The late result belongs to the worker and must be released.
                    return SpinWait.SpinUntil(() => Volatile.Read(ref decoded.Frames[0]) == null, 5000)
                        && asset.Ready == null;
                }
            }
        }

        private static bool RunArtAliasCheck()
        {
            using (var art = PetArtPackage.Load(192, 208))
            {
                art.PreloadRow(0);
                AnimationClip idle = art.GetLoadedClip(0);
                var tasks = new Task<AnimationClip>[PetArtPackage.RuntimeStateNames.Length];
                for (int row = 0; row < tasks.Length; row++) tasks[row] = art.LoadRowAsync(row);
                if (!Task.WaitAll(tasks, 30000)) return false;
                for (int row = 0; row < tasks.Length; row++)
                {
                    if (!art.IsRowLoaded(row) ||
                        !Object.ReferenceEquals(tasks[row].Result, art.GetLoadedClip(row)) ||
                        !Object.ReferenceEquals(tasks[row], art.LoadRowAsync(row))) return false;
                    string terminal = PetArtRules.ResolveTerminalStateName(art.Manifest,
                        PetArtPackage.RuntimeStateNames[row]);
                    for (int other = 0; other < row; other++)
                        if (String.Equals(terminal, PetArtRules.ResolveTerminalStateName(art.Manifest,
                                PetArtPackage.RuntimeStateNames[other]), StringComparison.OrdinalIgnoreCase) &&
                            !Object.ReferenceEquals(tasks[row], tasks[other])) return false;
                }
                if (!Object.ReferenceEquals(idle, art.GetLoadedClip(0))) return false;
                art.Dispose();
                for (int row = 0; row < tasks.Length; row++)
                    if (art.IsRowLoaded(row) || !art.LoadRowAsync(row).IsCanceled) return false;
                return RunExternalArtAliasCheck();
            }
        }

        private static bool RunExternalArtAliasCheck()
        {
            string root = Path.Combine(Path.GetTempPath(), "penny-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var bitmap = new Bitmap(2, 2)) bitmap.Save(Path.Combine(root, "idle.png"), System.Drawing.Imaging.ImageFormat.Png);
                var manifest = new PetArtManifest {
                    fallbackState = "idle",
                    states = new Dictionary<string, PetArtStateDefinition> {
                        { "idle", new PetArtStateDefinition { file = "idle.png" } },
                        { "hover", new PetArtStateDefinition { alias = "idle" } },
                        { "waiting", new PetArtStateDefinition { alias = "hover" } },
                        { "review", new PetArtStateDefinition { alias = "review" } }
                    }
                };
                using (var art = (PetArtPackage)Activator.CreateInstance(typeof(PetArtPackage),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { root, manifest, 2, 2 }, null))
                {
                    var idle = art.LoadRowAsync(0);
                    if (!Object.ReferenceEquals(idle, art.LoadRowAsync(4)) ||
                        !Object.ReferenceEquals(idle, art.LoadRowAsync(6)) ||
                        !Object.ReferenceEquals(idle, art.LoadRowAsync(9))) return false;
                    if (!idle.Wait(5000) || !Object.ReferenceEquals(idle.Result, art.GetLoadedClip(4)))
                        return false;
                    var invalid = art.LoadRowAsync(8);
                    if (!SpinWait.SpinUntil(() => invalid.IsCompleted, 5000) || !invalid.IsFaulted ||
                        art.IsRowLoaded(8) || !art.IsRowLoaded(0)) return false;
                    art.Dispose();
                    return idle.Result.Frames[0] == null;
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
