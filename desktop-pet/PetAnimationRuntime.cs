using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PennyPet
{
    // Native input, clock and bitmap presentation adapters for InteractionRuntime.
    internal sealed partial class PetForm : IInteractionArt
    {
        private void AnimationTick(object sender, EventArgs e)
        {
            _interaction.Tick(DateTime.UtcNow, _menu.Visible, HasFocusedOwnNoteTextInput());
            if (_interaction.ExitComplete) Close();
        }

        bool IInteractionArt.IsReady(int row) { return _art.IsRowLoaded(row); }
        void IInteractionArt.Request(int row) { QueueArtPreload(row); }
        int IInteractionArt.FrameCount(int row) { return _art.GetLoadedClip(row).FrameCount; }
        int IInteractionArt.FrameDuration(int row, int frame)
        { return _art.GetLoadedClip(row).FrameDuration(frame); }
        int IInteractionArt.CycleDuration(int row)
        {
            AnimationClip clip = _art.GetLoadedClip(row);
            int total = 0;
            for (int frame = 0; frame < clip.FrameCount; frame++)
                total += clip.FrameDuration(frame);
            return total;
        }

        internal static float DueReminderBubbleFontSizePoints(int bubbleScalePercent)
        {
            return KeyboardOverlayForm.TextFontSizePoints(bubbleScalePercent);
        }

        private void PetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _exiting) return;
            _interaction.BeginPointer(Cursor.Position, Location);
            HideHoverBubble();
            _keyOverlay.HideImmediately();
            Capture = true;
        }

        private void PetMouseMove(object sender, MouseEventArgs e)
        {
            Point location;
            if (!_interaction.MovePointer(Cursor.Position, out location)) return;
            Location = location;
            _keyOverlay.UpdatePosition(this);
        }

        private void PetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_interaction.PointerDown) return;
            Point clickLocation;
            bool wasDrag = _interaction.EndPointer(out clickLocation);
            Capture = false;
            if (wasDrag)
            {
                CommitPetUserPlacement();
                ReconcilePetDisplayPlacement(CurrentTopologySnapshot(), "PetDragCompleted");
            }
            else
            {
                Location = clickLocation;
                HandlePetPoked();
            }
            ShowNextPendingBubble();
        }

        private void PetMouseCaptureChanged(object sender, EventArgs e)
        {
            if (Capture || !_interaction.PointerDown) return;
            _interaction.CancelPointer();
            ReconcilePetDisplayPlacement(CurrentTopologySnapshot(), "PetDragCancelled");
        }

        private async void HandlePetPoked()
        {
            try
            {
                await _interaction.PokeAsync(_conversation, HasProtectedInteractionMessage,
                    ShowPokeEasterEgg);
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("pet-poke", error);
            }
        }

        private bool HasProtectedInteractionMessage()
        {
            return _bubbleCoordinator.CurrentKind.HasValue &&
                PetMessagePolicy.IsProtectedForegroundMessage(_bubbleCoordinator.CurrentKind.Value);
        }

        private bool ShowPokeEasterEgg()
        {
            bool shown = _bubbleCoordinator.Show(PetBubbleRequest.EasterEgg(
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(_settings.KeyOverlayScalePercent)));
            if (shown) System.Media.SystemSounds.Asterisk.Play();
            return shown;
        }

        internal void TriggerTypingAnimation()
        {
            _interaction.Type(DateTime.UtcNow);
        }

        private async void QueueStartupInteractionPreload()
        {
            int[] warmRows = { HoverRow, FailedRow, WaitingRow, ThinkingRow, WavingRow };
            foreach (int row in warmRows)
            {
                if (_exiting || IsDisposed) return;
                try { await _art.LoadRowAsync(row); }
                catch (Exception error)
                {
                    if (!_exiting && !IsDisposed)
                        ApplicationDiagnostics.ReportNonFatal("art-preload-" + row, error);
                }
            }
            if (_exiting || IsDisposed) return;
            try
            {
                foreach (int row in warmRows)
                    if (_art.IsRowLoaded(row)) EnsureRenderedRow(row);
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("startup-interaction-render", error);
            }
            _startupArtReady = _art.IsRowLoaded(IdleRow);
            TryRaiseStartupReady();
        }

        private void QueueArtPreload(int row)
        {
            _art.LoadRowAsync(row);
        }

        internal static int NormalizeScalePercent(int value)
        {
            return PetSettingRules.NormalizePetScalePercent(value);
        }

        internal static Size ScaledPetSize(int scalePercent)
        {
            int normalized = NormalizeScalePercent(scalePercent);
            return new Size(CellWidth * normalized / 100,
                CellHeight * normalized / 100);
        }

        private void RenderCurrentFrame()
        {
            if (_startupDisplaySuppressed || !IsHandleCreated || IsDisposed)
                return;
            EnsureRenderedRow(_interaction.Row);
            Bitmap[] rowFrames = _renderedFrames[_interaction.Row];
            if (rowFrames == null || rowFrames.Length == 0) return;
            Bitmap frame = rowFrames[_interaction.Frame];
            if (frame != null) LayeredSpriteRenderer.Show(this, frame);
        }

        private void BuildRenderedFrameCache(Size? targetSize = null)
        {
            _renderedTargetSize = targetSize ?? ScaledPetSize(_scalePercent);
            _renderedFrames = new Bitmap[PetArtPackage.RuntimeStateNames.Length][];
            _renderedFramesOwnBitmaps =
                _renderedTargetSize != ScaledPetSize(100);
            EnsureRenderedRow(_interaction.Row);
        }

        private void EnsureRenderedRow(int row)
        {
            if (_renderedFrames == null || row < 0 ||
                row >= _renderedFrames.Length)
                throw new ArgumentOutOfRangeException("row");
            if (_renderedFrames[row] != null) return;
            Dictionary<Bitmap, Bitmap> scaled = new Dictionary<Bitmap, Bitmap>();
            AnimationClip clip = _art.GetLoadedClip(row);
            int count = clip.FrameCount;
            Bitmap[] rendered = new Bitmap[count];
            for (int frame = 0; frame < count; frame++)
            {
                Bitmap source = clip.Frames[frame];
                if (source.Width == _renderedTargetSize.Width &&
                    source.Height == _renderedTargetSize.Height)
                {
                    rendered[frame] = source;
                }
                else
                {
                    Bitmap resized;
                    if (!scaled.TryGetValue(source, out resized))
                    {
                        resized = ResizeFrame(source, _renderedTargetSize);
                        scaled[source] = resized;
                    }
                    rendered[frame] = resized;
                }
            }
            _renderedFrames[row] = rendered;
        }

        private static Bitmap ResizeFrame(Bitmap original, Size size)
        {
            Bitmap result = new Bitmap(size.Width, size.Height,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(original, new Rectangle(Point.Empty, size),
                    new Rectangle(Point.Empty, original.Size), GraphicsUnit.Pixel);
            }
            return result;
        }

        private void DisposeRenderedFrameCache()
        {
            if (_renderedFrames == null) return;
            if (_renderedFramesOwnBitmaps)
            {
                HashSet<Bitmap> disposed = new HashSet<Bitmap>();
                foreach (Bitmap[] row in _renderedFrames)
                {
                    if (row == null) continue;
                    foreach (Bitmap frame in row)
                        if (frame != null && disposed.Add(frame)) frame.Dispose();
                }
            }
            _renderedFrames = null;
            _renderedFramesOwnBitmaps = false;
        }

    }
}
