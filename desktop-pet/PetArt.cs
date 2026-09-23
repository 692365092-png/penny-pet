using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace PennyPet
{
    internal sealed class PetArtPackage : IDisposable
    {
        private const string EmbeddedManifestResourceName =
            "PennyPet.Art.Manifest";
        private const string EmbeddedStartupCacheResourceName =
            "PennyPet.Art.StartupCache";
        private const string EmbeddedReleasePackResourceName =
            "PennyPet.Art.ReleasePack";
        private const string EmbeddedArtPackageRevision = "1.0-r2";

        internal static readonly string[] RuntimeStateNames =
        {
            "idle", "running-right", "running-left", "waving", "hover",
            "failed", "waiting", "thinking", "review", "notification"
        };

        private readonly int _canvasWidth;
        private readonly int _canvasHeight;
        private readonly string _artRoot;
        private readonly bool _usesEmbeddedArt;
        private readonly PetArtManifest _manifest;
        private readonly PetArtRenderSettings _render;
        private readonly object _resolveGate = new object();
        private readonly Dictionary<string, AnimationClip> _resolved =
            new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        private readonly AnimationClip[] _runtimeClips;
        private readonly Dictionary<int, AnimationClip> _packedLoadedClips =
            new Dictionary<int, AnimationClip>();
        private int[] _packedStateToClip;
        private PackedClipMetadata[] _packedClips;
        private bool _packedMetadataAttempted;
        private bool _disposed;
        private bool _loadedStartupCache;

        private sealed class PackedClipMetadata
        {
            internal string StateName;
            internal string Source;
            internal int Width;
            internal int Height;
            internal int FrameCount;
            internal int[] Durations;
            internal int PixelEncoding;
            internal int[] Palette;
            internal long DataOffset;
            internal int CompressedLength;
            internal long UncompressedLength;
        }

        private PetArtPackage(string artRoot, PetArtManifest manifest,
            int canvasWidth, int canvasHeight)
        {
            _artRoot = Path.GetFullPath(artRoot);
            _usesEmbeddedArt = String.Equals(
                _artRoot.TrimEnd(Path.DirectorySeparatorChar),
                EmbeddedArtRoot().TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            _manifest = manifest;
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _render = PetArtRules.NormalizeRenderSettings(manifest.render);
            _runtimeClips = new AnimationClip[RuntimeStateNames.Length];
            // A release build regenerates this cache from the current art, so
            // use it even when a development art folder sits next to the EXE.
            // This keeps test and friend-facing startup behavior identical.
            TryLoadEmbeddedStartupCache();
        }

        internal string DisplayName
        {
            get
            {
                return String.IsNullOrWhiteSpace(_manifest.displayName)
                    ? "Penny pet" : _manifest.displayName.Trim();
            }
        }

        internal PetArtManifest Manifest { get { return _manifest; } }

        internal string ArtRoot
        {
            get { return _artRoot; }
        }

        internal static bool HasEmbeddedStartupCacheForTest
        {
            get
            {
                Assembly assembly = typeof(PetArtPackage).Assembly;
                using (Stream releasePack = assembly.GetManifestResourceStream(
                    EmbeddedReleasePackResourceName))
                    if (releasePack != null && releasePack.Length > 1024)
                        return true;
                using (Stream startupCache = assembly.GetManifestResourceStream(
                    EmbeddedStartupCacheResourceName))
                    return startupCache != null && startupCache.Length > 1024;
            }
        }

        internal bool LoadedStartupCache
        {
            get { return _loadedStartupCache; }
        }

        internal static PetArtPackage Load(int canvasWidth, int canvasHeight)
        {
            string manifestPath = FindExternalManifestPath();
            string json;
            string root;
            if (!String.IsNullOrEmpty(manifestPath))
            {
                json = File.ReadAllText(manifestPath);
                root = Path.GetDirectoryName(manifestPath);
            }
            else
            {
                Assembly assembly = typeof(PetArtPackage).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(
                    EmbeddedManifestResourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException(
                            "找不到内置 Penny 美术清单。",
                            EmbeddedManifestResourceName);
                    using (StreamReader reader = new StreamReader(stream,
                        Encoding.UTF8, true)) json = reader.ReadToEnd();
                }
                // Lazy release-pack decoding does not need to write the
                // embedded manifest or GIFs to disk.
                root = EmbeddedArtRoot();
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            PetArtManifest manifest = serializer.Deserialize<PetArtManifest>(json);
            if (manifest == null)
                throw new InvalidDataException("pet-art.json 无法解析。");
            if (manifest.schemaVersion != 1)
                throw new InvalidDataException("pet-art.json 的 schemaVersion 必须为 1。");
            if (manifest.states == null || manifest.states.Count == 0)
                throw new InvalidDataException("pet-art.json 没有 states。");

            return new PetArtPackage(root, manifest, canvasWidth, canvasHeight);
        }

        private void TryLoadEmbeddedStartupCache()
        {
            Stream stream = null;
            List<Bitmap> created = new List<Bitmap>();
            try
            {
                stream = typeof(PetArtPackage).Assembly.GetManifestResourceStream(
                    EmbeddedStartupCacheResourceName);
                if (stream == null) return;
                using (BinaryReader reader = new BinaryReader(stream,
                    Encoding.UTF8))
                {
                    stream = null;
                    string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    if ((magic != "PCAF0001" && magic != "PCAF0002" &&
                        magic != "PCAF0003") ||
                        width != _canvasWidth ||
                        height != _canvasHeight || count <= 0 || count > 500)
                        return;
                    int[] durations = new int[count];
                    for (int index = 0; index < count; index++)
                        durations[index] = reader.ReadInt32();
                    int pixelEncoding = 0;
                    int[] palette = new int[0];
                    if (magic == "PCAF0003")
                    {
                        pixelEncoding = reader.ReadInt32();
                        int paletteCount = reader.ReadInt32();
                        if (pixelEncoding < 0 || pixelEncoding > 2 ||
                            paletteCount < 0 || paletteCount > UInt16.MaxValue ||
                            (pixelEncoding == 0 && paletteCount != 0) ||
                            (pixelEncoding == 1 &&
                                (paletteCount <= 0 || paletteCount > 256)) ||
                            (pixelEncoding == 2 && paletteCount <= 0))
                            return;
                        palette = new int[paletteCount];
                        for (int index = 0; index < paletteCount; index++)
                            palette[index] = reader.ReadInt32();
                    }
                    int rowBytes = width * 4;
                    byte[] row = new byte[rowBytes];
                    Stream pixelStream = reader.BaseStream;
                    DeflateStream deflate = null;
                    if (magic == "PCAF0002" || magic == "PCAF0003")
                    {
                        deflate = new DeflateStream(reader.BaseStream,
                            CompressionMode.Decompress, true);
                        pixelStream = deflate;
                    }
                    try
                    {
                        if (magic == "PCAF0003")
                        {
                            int pixelCount = checked(width * height);
                            int frameBytes = checked(pixelCount *
                                (pixelEncoding == 0 ? 4 : pixelEncoding));
                            byte[] packed = new byte[frameBytes];
                            byte[] previous = new byte[frameBytes];
                            byte[] bgra = pixelEncoding == 0
                                ? null : new byte[checked(pixelCount * 4)];
                            for (int index = 0; index < count; index++)
                            {
                                ReadExactly(pixelStream, packed, 0,
                                    packed.Length);
                                if (index == 0)
                                    Buffer.BlockCopy(packed, 0, previous, 0,
                                        packed.Length);
                                else
                                {
                                    for (int byteIndex = 0;
                                        byteIndex < packed.Length; byteIndex++)
                                        previous[byteIndex] = (byte)(
                                            previous[byteIndex] ^
                                            packed[byteIndex]);
                                }
                                byte[] sourcePixels = previous;
                                if (pixelEncoding != 0)
                                {
                                    ExpandPalettePixels(previous,
                                        pixelEncoding, palette, bgra);
                                    sourcePixels = bgra;
                                }
                                created.Add(CreateBitmapFromPixels(width,
                                    height, sourcePixels));
                            }
                        }
                        else
                        {
                            for (int index = 0; index < count; index++)
                            {
                                Bitmap frame = new Bitmap(width, height,
                                    PixelFormat.Format32bppPArgb);
                                created.Add(frame);
                                BitmapData data = frame.LockBits(new Rectangle(
                                    0, 0, width, height), ImageLockMode.WriteOnly,
                                    PixelFormat.Format32bppPArgb);
                                try
                                {
                                    for (int y = 0; y < height; y++)
                                    {
                                        ReadExactly(pixelStream, row, 0,
                                            rowBytes);
                                        Marshal.Copy(row, 0,
                                            IntPtr.Add(data.Scan0,
                                                y * data.Stride), rowBytes);
                                    }
                                }
                                finally { frame.UnlockBits(data); }
                            }
                        }
                    }
                    finally { if (deflate != null) deflate.Dispose(); }
                    AnimationClip clip = new AnimationClip(
                        "embedded-startup-cache", created.ToArray(), durations);
                    _runtimeClips[0] = clip;
                    _resolved[RuntimeStateNames[0]] = clip;
                    _loadedStartupCache = true;
                    created.Clear();
                }
            }
            catch
            {
                // A damaged optional cache must never prevent the original GIF
                // path from starting the pet.
            }
            finally
            {
                if (stream != null) stream.Dispose();
                foreach (Bitmap frame in created) frame.Dispose();
            }
        }

        private bool TryLoadEmbeddedReleasePackMetadata()
        {
            _packedMetadataAttempted = true;
            try
            {
                using (Stream stream = typeof(PetArtPackage).Assembly
                    .GetManifestResourceStream(EmbeddedReleasePackResourceName))
                {
                    if (stream == null || !stream.CanSeek) return false;
                    using (BinaryReader reader = new BinaryReader(stream,
                        Encoding.UTF8))
                    {
                        string magic = Encoding.ASCII.GetString(
                            reader.ReadBytes(8));
                        int stateCount = reader.ReadInt32();
                        int clipCount = reader.ReadInt32();
                        if (magic != "PPAP0003" ||
                            stateCount != RuntimeStateNames.Length ||
                            clipCount <= 0 || clipCount > RuntimeStateNames.Length)
                            return false;
                        int[] stateToClip = new int[stateCount];
                        for (int index = 0; index < stateCount; index++)
                        {
                            stateToClip[index] = reader.ReadInt32();
                            if (stateToClip[index] < 0 ||
                                stateToClip[index] >= clipCount)
                                return false;
                        }
                        PackedClipMetadata[] clips =
                            new PackedClipMetadata[clipCount];
                        for (int clipIndex = 0; clipIndex < clipCount;
                            clipIndex++)
                        {
                            PackedClipMetadata clip = new PackedClipMetadata();
                            clip.StateName = reader.ReadString();
                            clip.Source = reader.ReadString();
                            clip.Width = reader.ReadInt32();
                            clip.Height = reader.ReadInt32();
                            clip.FrameCount = reader.ReadInt32();
                            if (clip.Width <= 0 || clip.Height <= 0 ||
                                clip.FrameCount <= 0 || clip.FrameCount > 1000)
                                return false;
                            clip.Durations = new int[clip.FrameCount];
                            for (int frame = 0; frame < clip.FrameCount; frame++)
                                clip.Durations[frame] = reader.ReadInt32();
                            clip.PixelEncoding = reader.ReadInt32();
                            int paletteCount = reader.ReadInt32();
                            if (clip.PixelEncoding < 0 ||
                                clip.PixelEncoding > 2 || paletteCount < 0 ||
                                paletteCount > UInt16.MaxValue ||
                                (clip.PixelEncoding == 0 && paletteCount != 0) ||
                                (clip.PixelEncoding == 1 &&
                                    (paletteCount <= 0 || paletteCount > 256)) ||
                                (clip.PixelEncoding == 2 && paletteCount <= 0))
                                return false;
                            clip.Palette = new int[paletteCount];
                            for (int paletteIndex = 0;
                                paletteIndex < paletteCount; paletteIndex++)
                                clip.Palette[paletteIndex] = reader.ReadInt32();
                            clip.CompressedLength = reader.ReadInt32();
                            clip.UncompressedLength = reader.ReadInt64();
                            int bytesPerPixel = clip.PixelEncoding == 0
                                ? 4 : clip.PixelEncoding;
                            long expectedLength = (long)clip.Width * clip.Height *
                                bytesPerPixel * clip.FrameCount;
                            if (clip.CompressedLength <= 0 ||
                                clip.UncompressedLength != expectedLength ||
                                stream.Position + clip.CompressedLength >
                                    stream.Length)
                                return false;
                            clip.DataOffset = stream.Position;
                            stream.Position += clip.CompressedLength;
                            clips[clipIndex] = clip;
                        }
                        _packedStateToClip = stateToClip;
                        _packedClips = clips;
                        return true;
                    }
                }
            }
            catch
            {
                _packedStateToClip = null;
                _packedClips = null;
                return false;
            }
        }

        private AnimationClip LoadPackedClip(int clipIndex)
        {
            AnimationClip existing;
            if (_packedLoadedClips.TryGetValue(clipIndex, out existing))
                return existing;
            if (_packedClips == null || clipIndex < 0 ||
                clipIndex >= _packedClips.Length)
                throw new InvalidDataException("发布资源包动画索引无效。");
            PackedClipMetadata metadata = _packedClips[clipIndex];
            PetArtStateDefinition definition;
            if (!_manifest.states.TryGetValue(metadata.StateName,
                out definition) || definition == null)
                throw new InvalidDataException(
                    "发布资源包状态不在清单中：" + metadata.StateName);

            Bitmap[] frames = new Bitmap[metadata.FrameCount];
            int pixelCount = checked(metadata.Width * metadata.Height);
            int packedFrameBytes = checked(pixelCount *
                (metadata.PixelEncoding == 0 ? 4 : metadata.PixelEncoding));
            byte[] previous = new byte[packedFrameBytes];
            byte[] encoded = new byte[previous.Length];
            byte[] bgraPixels = metadata.PixelEncoding == 0
                ? null : new byte[checked(pixelCount * 4)];
            try
            {
                using (Stream resource = typeof(PetArtPackage).Assembly
                    .GetManifestResourceStream(EmbeddedReleasePackResourceName))
                {
                    if (resource == null || !resource.CanSeek)
                        throw new InvalidDataException("发布资源包已丢失。");
                    resource.Position = metadata.DataOffset;
                    using (DeflateStream deflate = new DeflateStream(resource,
                        CompressionMode.Decompress, true))
                    {
                        for (int frameIndex = 0;
                            frameIndex < metadata.FrameCount; frameIndex++)
                        {
                            ReadExactly(deflate, encoded, 0, encoded.Length);
                            if (frameIndex == 0)
                            {
                                Buffer.BlockCopy(encoded, 0, previous, 0,
                                    encoded.Length);
                            }
                            else
                            {
                                for (int index = 0; index < encoded.Length; index++)
                                    previous[index] = (byte)(previous[index] ^
                                        encoded[index]);
                            }
                            byte[] sourcePixels = previous;
                            if (metadata.PixelEncoding != 0)
                            {
                                ExpandPalettePixels(previous,
                                    metadata.PixelEncoding, metadata.Palette,
                                    bgraPixels);
                                sourcePixels = bgraPixels;
                            }
                            using (Bitmap source = CreateBitmapFromPixels(
                                metadata.Width, metadata.Height, sourcePixels))
                                frames[frameIndex] = NormalizeFrame(source,
                                    definition);
                        }
                    }
                }
                AnimationClip clip = new AnimationClip(
                    "embedded-pack:" + metadata.Source, frames,
                    (int[])metadata.Durations.Clone());
                _packedLoadedClips[clipIndex] = clip;
                if (_packedStateToClip != null &&
                    _packedStateToClip.Length > 0 &&
                    _packedStateToClip[0] == clipIndex)
                    _loadedStartupCache = true;
                return clip;
            }
            catch
            {
                foreach (Bitmap frame in frames)
                    if (frame != null) frame.Dispose();
                throw;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer,
            int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                    throw new EndOfStreamException(
                        "Penny 发布动画资源包不完整。");
                offset += read;
                count -= read;
            }
        }

        private static Bitmap CreateBitmapFromPixels(int width, int height,
            byte[] pixels)
        {
            Bitmap frame = new Bitmap(width, height,
                PixelFormat.Format32bppPArgb);
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int rowBytes = checked(width * 4);
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * rowBytes,
                        IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
            finally { frame.UnlockBits(data); }
            return frame;
        }

        private static void ExpandPalettePixels(byte[] packed,
            int pixelEncoding, int[] palette, byte[] bgra)
        {
            int pixelCount = bgra.Length / 4;
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int paletteIndex = pixelEncoding == 1
                    ? packed[pixel]
                    : packed[pixel * 2] | (packed[pixel * 2 + 1] << 8);
                if (paletteIndex < 0 || paletteIndex >= palette.Length)
                    throw new InvalidDataException(
                        "Penny 发布动画调色板索引无效。");
                int color = palette[paletteIndex];
                int offset = pixel * 4;
                bgra[offset] = (byte)color;
                bgra[offset + 1] = (byte)(color >> 8);
                bgra[offset + 2] = (byte)(color >> 16);
                bgra[offset + 3] = (byte)(color >> 24);
            }
        }

        private static string FindExternalManifestPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string currentDirectory = Environment.CurrentDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "art", "pet-art.json"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "art", "pet-art.json")),
                Path.Combine(currentDirectory, "art", "pet-art.json")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            return null;
        }

        private static string EmbeddedArtRoot()
        {
            return Path.Combine(WindowsDataPaths.PennyPetDirectory,
                "embedded-art", EmbeddedArtPackageRevision);
        }

        private static string EmbeddedResourceNameForPath(string relativePath)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(relativePath ?? String.Empty);
            string key = Convert.ToBase64String(bytes).TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');
            return "PennyPet.Art.File." + key;
        }

        private static void WriteEmbeddedResourceIfNeeded(Assembly assembly,
            string resourceName, string outputPath)
        {
            if (File.Exists(outputPath)) return;
            string directory = Path.GetDirectoryName(outputPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporaryPath = outputPath + ".tmp";
            try
            {
                using (Stream input = assembly.GetManifestResourceStream(resourceName))
                {
                    if (input == null)
                        throw new FileNotFoundException("内置 Penny 美术资源缺失：" +
                            resourceName);
                    using (FileStream output = new FileStream(temporaryPath,
                        FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
                try { File.Move(temporaryPath, outputPath); }
                catch (IOException)
                {
                    if (!File.Exists(outputPath)) throw;
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private AnimationClip ResolveState(string stateName,
            HashSet<string> resolving)
        {
            AnimationClip existing;
            if (_resolved.TryGetValue(stateName, out existing)) return existing;
            if (!resolving.Add(stateName))
                throw new InvalidDataException("美术状态 alias 形成循环：" + stateName);

            PetArtStateDefinition definition;
            if (!_manifest.states.TryGetValue(stateName, out definition) ||
                definition == null)
            {
                string fallback = String.IsNullOrWhiteSpace(_manifest.fallbackState)
                    ? "idle" : _manifest.fallbackState.Trim();
                if (String.Equals(fallback, stateName,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("缺少美术状态：" + stateName);
                AnimationClip fallbackClip = ResolveState(fallback, resolving);
                _resolved[stateName] = fallbackClip;
                resolving.Remove(stateName);
                return fallbackClip;
            }

            AnimationClip clip;
            if (!String.IsNullOrWhiteSpace(definition.alias))
            {
                clip = ResolveState(definition.alias.Trim(), resolving);
            }
            else if (!String.IsNullOrWhiteSpace(definition.file))
            {
                string path = ResolveAssetPath(definition.file, true);
                clip = LoadFileClip(path, definition);
            }
            else if (!String.IsNullOrWhiteSpace(definition.folder))
            {
                string path = ResolveAssetPath(definition.folder, false);
                clip = LoadFolderClip(path, definition);
            }
            else
            {
                throw new InvalidDataException("状态没有 file、folder 或 alias：" +
                    stateName);
            }

            _resolved[stateName] = clip;
            resolving.Remove(stateName);
            return clip;
        }

        internal string ResolveAssetPath(string relativePath, bool embeddedFile)
        {
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException("美术路径必须相对 art 文件夹：" + relativePath);
            string root = Path.GetFullPath(_artRoot).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(_artRoot, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("美术路径越出了 art 文件夹：" + relativePath);
            if (embeddedFile && _usesEmbeddedArt && !File.Exists(full))
            {
                string normalized = relativePath.Trim();
                if (normalized.IndexOf("..", StringComparison.Ordinal) >= 0)
                    throw new InvalidDataException("内置美术路径无效：" + normalized);
                WriteEmbeddedResourceIfNeeded(typeof(PetArtPackage).Assembly,
                    EmbeddedResourceNameForPath(normalized), full);
            }
            if (!File.Exists(full) && !Directory.Exists(full))
                throw new FileNotFoundException("找不到美术资源：" + relativePath, full);
            return full;
        }

        private AnimationClip LoadFileClip(string path,
            PetArtStateDefinition definition)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".gif") return LoadGifClip(path, definition);
            if (extension == ".png" || extension == ".webp" ||
                extension == ".jpg" || extension == ".jpeg")
            {
                using (Image image = Image.FromFile(path))
                {
                    Bitmap frame = NormalizeFrame(image, definition);
                    return new AnimationClip(path, new[] { frame },
                        new[] { NormalizeDuration(DefaultDuration(definition), definition) });
                }
            }
            throw new InvalidDataException("不支持的美术格式：" + path);
        }

        private AnimationClip LoadGifClip(string path,
            PetArtStateDefinition definition)
        {
            using (Image gif = Image.FromFile(path))
            {
                FrameDimension dimension = new FrameDimension(gif.FrameDimensionsList[0]);
                int count = gif.GetFrameCount(dimension);
                if (count <= 0) throw new InvalidDataException("GIF 没有动画帧：" + path);
                int[] rawDurations = ReadGifDurations(gif, count,
                    DefaultDuration(definition));
                Bitmap[] frames = new Bitmap[count];
                int[] durations = new int[count];
                for (int index = 0; index < count; index++)
                {
                    gif.SelectActiveFrame(dimension, index);
                    using (Bitmap decoded = new Bitmap(gif.Width, gif.Height,
                        PixelFormat.Format32bppArgb))
                    using (Graphics graphics = Graphics.FromImage(decoded))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImageUnscaled(gif, 0, 0);
                        frames[index] = NormalizeFrame(decoded, definition);
                    }
                    durations[index] = NormalizeDuration(rawDurations[index], definition);
                }
                return new AnimationClip(path, frames, durations);
            }
        }

        private AnimationClip LoadFolderClip(string folder,
            PetArtStateDefinition definition)
        {
            string[] files = Directory.GetFiles(folder, "*.png")
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
                throw new InvalidDataException("逐帧 PNG 文件夹是空的：" + folder);
            Bitmap[] frames = new Bitmap[files.Length];
            int[] durations = new int[files.Length];
            for (int index = 0; index < files.Length; index++)
            {
                using (Image image = Image.FromFile(files[index]))
                    frames[index] = NormalizeFrame(image, definition);
                int raw = definition.durationsMs != null &&
                    index < definition.durationsMs.Length
                    ? definition.durationsMs[index] : DefaultDuration(definition);
                durations[index] = NormalizeDuration(raw, definition);
            }
            return new AnimationClip(folder, frames, durations);
        }

        internal static int[] ReadGifDurations(Image image, int frameCount, int fallback)
        {
            int[] result = Enumerable.Repeat(fallback, frameCount).ToArray();
            try
            {
                PropertyItem property = image.GetPropertyItem(0x5100);
                int available = property == null || property.Value == null
                    ? 0 : property.Value.Length / 4;
                for (int index = 0; index < frameCount && index < available; index++)
                {
                    int hundredths = BitConverter.ToInt32(property.Value, index * 4);
                    result[index] = hundredths <= 0 ? fallback : hundredths * 10;
                }
            }
            catch (ArgumentException) { }
            return result;
        }

        private int DefaultDuration(PetArtStateDefinition definition)
        {
            return PetArtRules.DefaultFrameDuration(definition);
        }

        internal int NormalizeDuration(int milliseconds,
            PetArtStateDefinition definition)
        {
            return PetArtRules.NormalizeFrameDuration(milliseconds,
                definition, _render);
        }

        private Bitmap NormalizeFrame(Image source,
            PetArtStateDefinition definition)
        {
            return PetArtFrameRenderer.Render(source, definition, _render,
                _canvasWidth, _canvasHeight);
        }

        internal int FrameCount(int row)
        {
            return GetClip(row).FrameCount;
        }

        internal int FrameDuration(int row, int frame)
        {
            return GetClip(row).FrameDuration(frame);
        }

        internal int CycleDuration(int row)
        {
            AnimationClip clip = GetClip(row);
            int total = 0;
            for (int frame = 0; frame < clip.FrameCount; frame++)
                total += clip.FrameDuration(frame);
            return total;
        }

        internal Bitmap GetFrame(int row, int frame)
        {
            AnimationClip clip = GetClip(row);
            int index = Math.Max(0, frame) % clip.FrameCount;
            return clip.Frames[index];
        }

        internal bool IsRowLoaded(int row)
        {
            if (row < 0 || row >= _runtimeClips.Length) return false;
            lock (_resolveGate)
            {
                return !_disposed && _runtimeClips[row] != null;
            }
        }

        internal void PreloadRow(int row)
        {
            GetClip(row);
        }

        internal AnimationClip GetClip(int row)
        {
            if (row < 0 || row >= _runtimeClips.Length)
                throw new ArgumentOutOfRangeException(nameof(row));
            lock (_resolveGate)
            {
                if (_disposed) throw new ObjectDisposedException("PetArtPackage");
                AnimationClip clip = _runtimeClips[row];
                if (clip == null)
                {
                    if (_usesEmbeddedArt && !_packedMetadataAttempted)
                        TryLoadEmbeddedReleasePackMetadata();
                    if (_packedStateToClip != null &&
                        row < _packedStateToClip.Length)
                        clip = LoadPackedClip(_packedStateToClip[row]);
                    else
                        clip = ResolveState(RuntimeStateNames[row],
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    _runtimeClips[row] = clip;
                }
                if (clip == null || clip.FrameCount == 0)
                    throw new InvalidDataException("美术状态没有可播放帧：" +
                        RuntimeStateNames[row]);
                return clip;
            }
        }

        internal int LoadedRuntimeStateCount
        {
            get
            {
                lock (_resolveGate)
                {
                    int count = 0;
                    foreach (AnimationClip clip in _runtimeClips)
                        if (clip != null) count++;
                    return count;
                }
            }
        }

        public void Dispose()
        {
            lock (_resolveGate)
            {
                if (_disposed) return;
                _disposed = true;
                HashSet<AnimationClip> unique = new HashSet<AnimationClip>();
                foreach (AnimationClip clip in _runtimeClips)
                    if (clip != null && unique.Add(clip)) clip.Dispose();
                foreach (AnimationClip clip in _resolved.Values)
                    if (clip != null && unique.Add(clip)) clip.Dispose();
                foreach (AnimationClip clip in _packedLoadedClips.Values)
                    if (clip != null && unique.Add(clip)) clip.Dispose();
                _resolved.Clear();
                _packedLoadedClips.Clear();
                Array.Clear(_runtimeClips, 0, _runtimeClips.Length);
            }
        }
    }
}
