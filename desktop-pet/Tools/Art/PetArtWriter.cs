using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace PennyPet
{
    // Compiled only into PennyPet.Tools. The desktop runtime reads these formats;
    // it never carries the encoder or generates resource packs.
    internal static class PetArtWriter
    {
        private sealed class RawAnimationClip : IDisposable
        {
            internal string StateName;
            internal string Source;
            internal Bitmap[] Frames;
            internal int[] Durations;

            public void Dispose()
            {
                if (Frames == null) return;
                foreach (Bitmap frame in Frames)
                    if (frame != null) frame.Dispose();
            }
        }

        internal static void WriteValidationReport(int canvasWidth, int canvasHeight,
            string outputPath)
        {
            using (PetArtPackage package = PetArtPackage.Load(canvasWidth, canvasHeight))
            {
                List<object> states = new List<object>();
                for (int row = 0; row < PetArtPackage.RuntimeStateNames.Length; row++)
                {
                    AnimationClip clip = package.GetClip(row);
                    int minimum = Int32.MaxValue;
                    int maximum = 0;
                    for (int frame = 0; frame < clip.FrameCount; frame++)
                    {
                        int duration = clip.FrameDuration(frame);
                        minimum = Math.Min(minimum, duration);
                        maximum = Math.Max(maximum, duration);
                    }
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["state"] = PetArtPackage.RuntimeStateNames[row];
                    item["source"] = clip.Source;
                    item["frames"] = clip.FrameCount;
                    item["cycleMs"] = package.CycleDuration(row);
                    item["minimumFrameMs"] = minimum == Int32.MaxValue ? 0 : minimum;
                    item["maximumFrameMs"] = maximum;
                    states.Add(item);
                }

                Dictionary<string, object> report = new Dictionary<string, object>();
                report["ok"] = true;
                report["displayName"] = package.DisplayName;
                report["artRoot"] = package.ArtRoot;
                report["canvasWidth"] = canvasWidth;
                report["canvasHeight"] = canvasHeight;
                report["states"] = states;

                string fullOutputPath = Path.GetFullPath(outputPath);
                string parent = Path.GetDirectoryName(fullOutputPath);
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(fullOutputPath, serializer.Serialize(report),
                    new UTF8Encoding(false));
            }
        }

        internal static void WriteReleasePack(int canvasWidth,
            int canvasHeight, string outputPath)
        {
            using (PetArtPackage package = PetArtPackage.Load(canvasWidth, canvasHeight))
            {
                PetArtManifest manifest = package.Manifest;
                List<RawAnimationClip> clips = new List<RawAnimationClip>();
                Dictionary<string, int> clipByState =
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int[] stateToClip = new int[PetArtPackage.RuntimeStateNames.Length];
                try
                {
                    for (int row = 0; row < PetArtPackage.RuntimeStateNames.Length; row++)
                    {
                        string terminalState =
                            PetArtRules.ResolveTerminalStateName(manifest,
                                PetArtPackage.RuntimeStateNames[row]);
                        int clipIndex;
                        if (!clipByState.TryGetValue(terminalState, out clipIndex))
                        {
                            PetArtStateDefinition definition =
                                manifest.states[terminalState];
                            RawAnimationClip raw = LoadRawClipForPack(package,
                                terminalState, definition);
                            clipIndex = clips.Count;
                            clips.Add(raw);
                            clipByState[terminalState] = clipIndex;
                        }
                        stateToClip[row] = clipIndex;
                    }

                    string fullPath = Path.GetFullPath(outputPath);
                    string parent = Path.GetDirectoryName(fullPath);
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    using (FileStream stream = new FileStream(fullPath,
                        FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    using (BinaryWriter writer = new BinaryWriter(stream,
                        Encoding.UTF8))
                    {
                        writer.Write(Encoding.ASCII.GetBytes("PPAP0003"));
                        writer.Write(PetArtPackage.RuntimeStateNames.Length);
                        writer.Write(clips.Count);
                        foreach (int clipIndex in stateToClip)
                            writer.Write(clipIndex);
                        foreach (RawAnimationClip clip in clips)
                            WritePackedClip(writer, clip);
                    }
                }
                finally
                {
                    foreach (RawAnimationClip clip in clips) clip.Dispose();
                }
            }
        }

        private static RawAnimationClip LoadRawClipForPack(PetArtPackage package,
            string stateName,
            PetArtStateDefinition definition)
        {
            if (!String.IsNullOrWhiteSpace(definition.file))
            {
                string path = package.ResolveAssetPath(definition.file, false);
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".gif")
                    return LoadRawGifClipForPack(package, stateName, path, definition);
                if (extension == ".png" || extension == ".jpg" ||
                    extension == ".jpeg")
                {
                    using (Image image = Image.FromFile(path))
                    {
                        Bitmap frame = CopyRawFrame(image);
                        return new RawAnimationClip
                        {
                            StateName = stateName,
                            Source = definition.file,
                            Frames = new[] { frame },
                            Durations = new[] { package.NormalizeDuration(
                                PetArtRules.DefaultFrameDuration(definition), definition) }
                        };
                    }
                }
                throw new InvalidDataException(
                    "发布资源包不支持的美术格式：" + path);
            }
            if (!String.IsNullOrWhiteSpace(definition.folder))
            {
                string folder = package.ResolveAssetPath(definition.folder, false);
                string[] files = Directory.GetFiles(folder, "*.png")
                    .OrderBy(path => Path.GetFileName(path),
                        StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length == 0)
                    throw new InvalidDataException(
                        "逐帧 PNG 文件夹是空的：" + folder);
                Bitmap[] frames = new Bitmap[files.Length];
                int[] durations = new int[files.Length];
                try
                {
                    int width = 0;
                    int height = 0;
                    for (int index = 0; index < files.Length; index++)
                    {
                        using (Image image = Image.FromFile(files[index]))
                        {
                            if (index == 0)
                            {
                                width = image.Width;
                                height = image.Height;
                            }
                            else if (image.Width != width || image.Height != height)
                            {
                                throw new InvalidDataException(
                                    "发布资源包要求逐帧 PNG 尺寸一致：" + folder);
                            }
                            frames[index] = CopyRawFrame(image);
                        }
                        int rawDuration = definition.durationsMs != null &&
                            index < definition.durationsMs.Length
                            ? definition.durationsMs[index]
                            : PetArtRules.DefaultFrameDuration(definition);
                        durations[index] = package.NormalizeDuration(rawDuration,
                            definition);
                    }
                    return new RawAnimationClip
                    {
                        StateName = stateName,
                        Source = definition.folder,
                        Frames = frames,
                        Durations = durations
                    };
                }
                catch
                {
                    foreach (Bitmap frame in frames)
                        if (frame != null) frame.Dispose();
                    throw;
                }
            }
            throw new InvalidDataException(
                "状态没有 file、folder 或 alias：" + stateName);
        }

        private static RawAnimationClip LoadRawGifClipForPack(PetArtPackage package,
            string stateName,
            string path, PetArtStateDefinition definition)
        {
            using (Image gif = Image.FromFile(path))
            {
                FrameDimension dimension = new FrameDimension(
                    gif.FrameDimensionsList[0]);
                int count = gif.GetFrameCount(dimension);
                if (count <= 0)
                    throw new InvalidDataException("GIF 没有动画帧：" + path);
                int[] rawDurations = PetArtPackage.ReadGifDurations(gif, count,
                    PetArtRules.DefaultFrameDuration(definition));
                Bitmap[] frames = new Bitmap[count];
                int[] durations = new int[count];
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        gif.SelectActiveFrame(dimension, index);
                        frames[index] = CopyRawFrame(gif);
                        durations[index] = package.NormalizeDuration(
                            rawDurations[index], definition);
                    }
                    return new RawAnimationClip
                    {
                        StateName = stateName,
                        Source = definition.file,
                        Frames = frames,
                        Durations = durations
                    };
                }
                catch
                {
                    foreach (Bitmap frame in frames)
                        if (frame != null) frame.Dispose();
                    throw;
                }
            }
        }

        private static Bitmap CopyRawFrame(Image image)
        {
            Bitmap frame = new Bitmap(image.Width, image.Height,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(image, 0, 0);
            }
            return frame;
        }

        private static void WritePackedClip(BinaryWriter writer,
            RawAnimationClip clip)
        {
            if (clip.Frames == null || clip.Frames.Length == 0)
                throw new InvalidDataException(
                    "发布资源包动画没有帧：" + clip.StateName);
            int width = clip.Frames[0].Width;
            int height = clip.Frames[0].Height;
            int pixelCount = checked(width * height);
            Dictionary<int, int> paletteMap;
            int[] palette = BuildClipPalette(clip.Frames, pixelCount,
                out paletteMap);
            int pixelEncoding = palette == null ? 0 :
                (palette.Length <= 256 ? 1 : 2);
            int frameBytes = checked(pixelCount *
                (pixelEncoding == 0 ? 4 : pixelEncoding));
            writer.Write(clip.StateName ?? String.Empty);
            writer.Write(clip.Source ?? String.Empty);
            writer.Write(width);
            writer.Write(height);
            writer.Write(clip.Frames.Length);
            foreach (int duration in clip.Durations) writer.Write(duration);
            writer.Write(pixelEncoding);
            writer.Write(palette == null ? 0 : palette.Length);
            if (palette != null)
                foreach (int color in palette) writer.Write(color);
            long lengthPosition = writer.BaseStream.Position;
            writer.Write(0);
            writer.Write((long)frameBytes * clip.Frames.Length);
            writer.Flush();
            long dataStart = writer.BaseStream.Position;

            byte[] current = new byte[frameBytes];
            byte[] previous = new byte[frameBytes];
            byte[] encoded = new byte[frameBytes];
            int[] argbPixels = pixelEncoding == 0
                ? null : new int[pixelCount];
            using (DeflateStream deflate = new DeflateStream(writer.BaseStream,
                CompressionLevel.Optimal, true))
            {
                for (int frameIndex = 0; frameIndex < clip.Frames.Length;
                    frameIndex++)
                {
                    if (pixelEncoding == 0)
                    {
                        CopyBitmapPixels(clip.Frames[frameIndex], current);
                    }
                    else
                    {
                        CopyBitmapArgbPixels(clip.Frames[frameIndex], argbPixels);
                        if (pixelEncoding == 1)
                        {
                            for (int pixel = 0; pixel < pixelCount; pixel++)
                                current[pixel] = (byte)paletteMap[
                                    argbPixels[pixel]];
                        }
                        else
                        {
                            for (int pixel = 0; pixel < pixelCount; pixel++)
                            {
                                int paletteIndex = paletteMap[argbPixels[pixel]];
                                int offset = pixel * 2;
                                current[offset] = (byte)paletteIndex;
                                current[offset + 1] =
                                    (byte)(paletteIndex >> 8);
                            }
                        }
                    }
                    if (frameIndex == 0)
                    {
                        deflate.Write(current, 0, current.Length);
                    }
                    else
                    {
                        for (int index = 0; index < current.Length; index++)
                            encoded[index] = (byte)(current[index] ^ previous[index]);
                        deflate.Write(encoded, 0, encoded.Length);
                    }
                    byte[] swap = previous;
                    previous = current;
                    current = swap;
                }
            }
            writer.Flush();
            long dataEnd = writer.BaseStream.Position;
            int compressedLength = checked((int)(dataEnd - dataStart));
            writer.BaseStream.Position = lengthPosition;
            writer.Write(compressedLength);
            writer.BaseStream.Position = dataEnd;
        }

        private static int[] BuildClipPalette(Bitmap[] frames, int pixelCount,
            out Dictionary<int, int> paletteMap)
        {
            paletteMap = new Dictionary<int, int>();
            List<int> colors = new List<int>();
            int[] pixels = new int[pixelCount];
            foreach (Bitmap frame in frames)
            {
                CopyBitmapArgbPixels(frame, pixels);
                for (int index = 0; index < pixels.Length; index++)
                {
                    int color = pixels[index];
                    int paletteIndex;
                    if (paletteMap.TryGetValue(color, out paletteIndex))
                        continue;
                    if (colors.Count >= UInt16.MaxValue)
                    {
                        paletteMap = null;
                        return null;
                    }
                    paletteIndex = colors.Count;
                    colors.Add(color);
                    paletteMap[color] = paletteIndex;
                }
            }
            return colors.ToArray();
        }

        private static void CopyBitmapArgbPixels(Bitmap frame,
            int[] destination)
        {
            int rowPixels = frame.Width;
            if (destination.Length != rowPixels * frame.Height)
                throw new ArgumentException("像素缓冲区尺寸不正确。",
                    nameof(destination));
            BitmapData data = frame.LockBits(new Rectangle(0, 0,
                frame.Width, frame.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride),
                        destination, y * rowPixels, rowPixels);
            }
            finally { frame.UnlockBits(data); }
        }

        private static void CopyBitmapPixels(Bitmap frame, byte[] destination)
        {
            int rowBytes = checked(frame.Width * 4);
            if (destination.Length != rowBytes * frame.Height)
                throw new ArgumentException("像素缓冲区尺寸不正确。",
                    nameof(destination));
            BitmapData data = frame.LockBits(new Rectangle(0, 0,
                frame.Width, frame.Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride),
                        destination, y * rowBytes, rowBytes);
            }
            finally { frame.UnlockBits(data); }
        }

        internal static void WriteStartupCache(int canvasWidth,
            int canvasHeight, string outputPath)
        {
            using (PetArtPackage package = PetArtPackage.Load(canvasWidth, canvasHeight))
            {
                AnimationClip clip = package.GetClip(0);
                string fullPath = Path.GetFullPath(outputPath);
                string parent = Path.GetDirectoryName(fullPath);
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                using (FileStream stream = new FileStream(fullPath,
                    FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(stream,
                    Encoding.UTF8))
                {
                    int pixelCount = checked(canvasWidth * canvasHeight);
                    Dictionary<int, int> paletteMap;
                    int[] palette = BuildClipPalette(clip.Frames, pixelCount,
                        out paletteMap);
                    int pixelEncoding = palette == null ? 0 :
                        (palette.Length <= 256 ? 1 : 2);
                    writer.Write(Encoding.ASCII.GetBytes("PCAF0003"));
                    writer.Write(canvasWidth);
                    writer.Write(canvasHeight);
                    writer.Write(clip.FrameCount);
                    for (int index = 0; index < clip.FrameCount; index++)
                        writer.Write(clip.FrameDuration(index));
                    writer.Write(pixelEncoding);
                    writer.Write(palette == null ? 0 : palette.Length);
                    if (palette != null)
                        foreach (int color in palette) writer.Write(color);
                    int frameBytes = checked(pixelCount *
                        (pixelEncoding == 0 ? 4 : pixelEncoding));
                    byte[] current = new byte[frameBytes];
                    byte[] previous = new byte[frameBytes];
                    byte[] encoded = new byte[frameBytes];
                    int[] argbPixels = pixelEncoding == 0
                        ? null : new int[pixelCount];
                    writer.Flush();
                    using (DeflateStream deflate = new DeflateStream(stream,
                        CompressionLevel.Optimal, true))
                    {
                        for (int frameIndex = 0;
                            frameIndex < clip.Frames.Length; frameIndex++)
                        {
                            Bitmap frame = clip.Frames[frameIndex];
                            if (pixelEncoding == 0)
                            {
                                CopyBitmapPixels(frame, current);
                            }
                            else
                            {
                                CopyBitmapArgbPixels(frame, argbPixels);
                                if (pixelEncoding == 1)
                                {
                                    for (int pixel = 0; pixel < pixelCount; pixel++)
                                        current[pixel] = (byte)paletteMap[
                                            argbPixels[pixel]];
                                }
                                else
                                {
                                    for (int pixel = 0; pixel < pixelCount;
                                        pixel++)
                                    {
                                        int paletteIndex = paletteMap[
                                            argbPixels[pixel]];
                                        int offset = pixel * 2;
                                        current[offset] = (byte)paletteIndex;
                                        current[offset + 1] =
                                            (byte)(paletteIndex >> 8);
                                    }
                                }
                            }
                            if (frameIndex == 0)
                                deflate.Write(current, 0, current.Length);
                            else
                            {
                                for (int index = 0; index < current.Length;
                                    index++)
                                    encoded[index] = (byte)(current[index] ^
                                        previous[index]);
                                deflate.Write(encoded, 0, encoded.Length);
                            }
                            byte[] swap = previous;
                            previous = current;
                            current = swap;
                        }
                    }
                }
            }
        }

    }
}
