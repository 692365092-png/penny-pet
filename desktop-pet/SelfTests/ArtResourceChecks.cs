using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private sealed class ArtResourceCheckResult
        {
            internal int Width;
            internal int Height;
            internal bool AtlasOk;
            internal bool AnimationTimingOk;
            internal bool StartupCacheEmbeddedOk;
            internal bool StartupLazyLoadOk;
            internal bool InteractionPreloadOk;
            internal bool NotificationPlaybackOk;
            internal bool InnerOutlineOk;
            internal bool GreenHaloAbsent;
            internal bool ApplicationIconEmbeddedOk;
            internal bool ContactAuthorFeatureOk;
            internal int[] AnimationCycleDurations;
        }

        private static ArtResourceCheckResult RunArtResourceChecks()
        {
            ArtResourceCheckResult result = new ArtResourceCheckResult
            {
                AnimationTimingOk = true,
                StartupCacheEmbeddedOk =
                    PetArtPackage.HasEmbeddedStartupCacheForTest,
                AnimationCycleDurations = new int[
                    PetArtPackage.RuntimeStateNames.Length]
            };
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            {
                Bitmap rendered = art.GetFrame(0, 0);
                result.StartupLazyLoadOk = art.LoadedRuntimeStateCount == 1;
                result.InteractionPreloadOk = !art.IsRowLoaded(4);
                art.PreloadRow(4);
                result.InteractionPreloadOk = result.InteractionPreloadOk &&
                    art.IsRowLoaded(4) && art.IsRowLoaded(1) && art.IsRowLoaded(2) &&
                    art.LoadedRuntimeStateCount == 4;
                art.PreloadRow(9);
                result.NotificationPlaybackOk = art.IsRowLoaded(9) &&
                    art.GetFrame(9, 0) != null &&
                    PetAnimationController.AttentionAnimationRow(true) == 9 &&
                    PetAnimationController.AttentionAnimationRow(false) == 0;
                result.Width = rendered.Width;
                result.Height = rendered.Height;
                result.AtlasOk = result.Width == 192 && result.Height == 208;
                for (int row = 0;
                    row < PetArtPackage.RuntimeStateNames.Length; row++)
                {
                    result.AnimationCycleDurations[row] =
                        art.CycleDuration(row);
                    result.AnimationTimingOk = result.AnimationTimingOk &&
                        result.AnimationCycleDurations[row] > 0;
                    result.AtlasOk = result.AtlasOk &&
                        art.FrameCount(row) > 0 &&
                        result.AnimationCycleDurations[row] > 0;
                }
                int greenPixels = 0;
                for (int y = 0; y < rendered.Height; y++)
                {
                    for (int x = 0; x < rendered.Width; x++)
                    {
                        Color pixel = rendered.GetPixel(x, y);
                        if (pixel.A >= 16 &&
                            TouchesTransparency(rendered, x, y) &&
                            pixel.G > pixel.R + 40 &&
                            pixel.G > pixel.B + 40)
                            greenPixels++;
                    }
                }
                result.InnerOutlineOk = rendered.PixelFormat ==
                    PixelFormat.Format32bppPArgb;
                result.GreenHaloAbsent = greenPixels == 0;
            }
            using (Icon applicationIcon = Icon.ExtractAssociatedIcon(
                Assembly.GetExecutingAssembly().Location))
            {
                result.ApplicationIconEmbeddedOk = applicationIcon != null &&
                    applicationIcon.Width >= 16 && applicationIcon.Height >= 16;
            }
            bool contactArtworkEmbedded;
            using (Stream contactArtwork = typeof(ContactAuthorForm).Assembly
                .GetManifestResourceStream("PennyPet.ContactAuthor.Image"))
                contactArtworkEmbedded = contactArtwork != null;
            using (ContactAuthorForm contact = new ContactAuthorForm())
            {
                result.ContactAuthorFeatureOk = contactArtworkEmbedded &&
                    contact.CopyAndArtworkBehaviorConfigured &&
                    contact.DisplayedXiaohongshuNumber ==
                        ContactAuthorForm.XiaohongshuNumber &&
                    ContactAuthorForm.XiaohongshuProfileUrl ==
                        "https://www.xiaohongshu.com/user/profile/" +
                        "59bd4b0b51783a7612f6fc43" &&
                    ContactAuthorForm.XiaohongshuProfileUrl.IndexOf('?') < 0 &&
                    contact.XiaohongshuOnlyLayoutForTest;
            }
            return result;
        }
    }
}
