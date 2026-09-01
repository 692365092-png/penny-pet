using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns the WinForms timer-to-animation bridge, art preload and rendering.
    // Animation selection policy remains in PetAnimationController.
    internal sealed partial class PetForm
    {
        private void AnimationTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (_exiting)
            {
                if (_row != WavingRow)
                {
                    _row = WavingRow;
                    _frame = 0;
                    ScheduleNextFrame(now);
                    RenderCurrentFrame();
                    return;
                }
                if (now < _nextFrameUtc) return;
                if (_frame >= RuntimeFrameCount(WavingRow) - 1)
                {
                    Close();
                    return;
                }
                _frame++;
                ScheduleNextFrame(now);
                RenderCurrentFrame();
                return;
            }
            if (_typingSession && now > _typingUntilUtc)
                _typingSession = false;
            if (_animation.InteractionAnimationKind !=
                PetInteractionAnimationKind.None &&
                !(_dragging && _dragMoved))
            {
                int interactionRow = _animation.InteractionAnimationRow;
                if (!_art.IsRowLoaded(interactionRow))
                {
                    QueueArtPreload(interactionRow);
                    return;
                }
                if (_row != interactionRow)
                {
                    _row = interactionRow;
                    _frame = 0;
                    ScheduleNextFrame(now);
                    RenderCurrentFrame();
                    return;
                }
                if (now < _nextFrameUtc) return;
                if (_frame >= RuntimeFrameCount(_row) - 1)
                {
                    _animation.CompleteInteractionAnimation();
                    _row = ChooseRow();
                    _frame = 0;
                }
                else
                {
                    _frame++;
                }
                ScheduleNextFrame(now);
                RenderCurrentFrame();
                return;
            }
            int wanted = ChooseRow();
            if (_row != wanted)
            {
                _row = wanted;
                _frame = 0;
                ScheduleNextFrame(now);
                RenderCurrentFrame();
                return;
            }
            if (now < _nextFrameUtc) return;
            if (PetAnimationController.ReminderAnimationCycleComplete(
                _reminderAttentionActive,
                _row, _frame, RuntimeFrameCount(_row)))
            {
                _reminderAttentionActive = false;
                _idleRow = IdleRow;
                _row = IdleRow;
                _frame = 0;
                ScheduleNextFrame(now);
                RenderCurrentFrame();
                return;
            }
            if (PetAnimationController.IsIdleAnimationRow(_row) &&
                _frame >= RuntimeFrameCount(_row) - 1)
            {
                _idleRow = PetAnimationController.PickRandomIdleAnimationRow(
                    _random, _row);
                QueueArtPreload(_idleRow);
                _row = _art.IsRowLoaded(_idleRow) ? _idleRow : IdleRow;
                _frame = 0;
            }
            else
            {
                _frame = (_frame + 1) % RuntimeFrameCount(_row);
            }
            ScheduleNextFrame(now);
            RenderCurrentFrame();
        }

        private int RuntimeFrameCount(int row)
        {
            return _art.FrameCount(row);
        }

        private int RuntimeFrameDuration(int row, int frame)
        {
            return _art.FrameDuration(row, frame);
        }

        private void ScheduleNextFrame(DateTime now)
        {
            int duration = RuntimeFrameDuration(_row, _frame);
            if (!_exiting && HasFocusedOwnNoteTextInput())
                duration = Math.Max(40, duration * 2);
            _nextFrameUtc = now.AddMilliseconds(duration);
        }

        private int RuntimeAnimationCycleDuration(int row)
        {
            return _art.CycleDuration(row);
        }

        private int ChooseRow()
        {
            return _animation.ChooseRow(_exiting, _dragging && _dragMoved,
                _mouseInside, _menu.Visible, _art.IsRowLoaded);
        }

        internal static float DueReminderBubbleFontSizePoints(
            int bubbleScalePercent)
        {
            return KeyboardOverlayForm.TextFontSizePoints(
                bubbleScalePercent);
        }

        private void PetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _exiting) return;
            _dragging = true;
            _dragMoved = false;
            QueueArtPreload(FailedRow);
            if (_bubbleCoordinator.IsCurrent(PetMessageKind.Hover))
                _bubbleCoordinator.CloseIfCurrent(PetMessageKind.Hover);
            _keyOverlay.HideImmediately();
            _typingSession = false;
            _dragMouseOrigin = Cursor.Position;
            _dragWindowOrigin = Location;
            Capture = true;
        }

        private void PetMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = Cursor.Position;
            int dx = now.X - _dragMouseOrigin.X;
            int dy = now.Y - _dragMouseOrigin.Y;
            if (!_dragMoved &&
                !PetAnimationController.MovementStartsDrag(dx, dy)) return;
            if (!_dragMoved)
            {
                _dragMoved = true;
                _animation.CancelInteractionAnimation();
            }
            Location = new Point(_dragWindowOrigin.X + dx, _dragWindowOrigin.Y + dy);
            _keyOverlay.UpdatePosition(this);
        }

        private void PetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_dragging) return;
            bool wasDrag = _dragMoved;
            _dragging = false;
            _dragMoved = false;
            Capture = false;
            if (wasDrag)
                SaveLocation();
            else
            {
                Location = _dragWindowOrigin;
                HandlePetPoked();
            }
            ShowNextPendingBubble();
        }

        private async void HandlePetPoked()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_pokeBurstTracker.RegisterPoke(nowUtc))
            {
                StartPokeEasterEgg(nowUtc);
                return;
            }
            StartOrdinaryPokeAnimation(nowUtc);
            bool dailyHandled = await _dailyContentCoordinator
                .HandlePetPokedAsync(DateTimeOffset.Now);
            if (_exiting || IsDisposed || Disposing) return;
            if (!dailyHandled)
                _smallTalkCoordinator.HandlePetPoked(nowUtc);
        }

        private void StartOrdinaryPokeAnimation(DateTime nowUtc)
        {
            if (_bubbleCoordinator.CurrentKind.HasValue &&
                PetMessagePolicy.IsProtectedForegroundMessage(
                    _bubbleCoordinator.CurrentKind.Value)) return;
            int row = PetAnimationController.PickRandomManualAnimationRow(
                _random, _row);
            if (!_animation.TryStartOrdinaryPoke(row)) return;
            _typingSession = false;
            QueueArtPreload(row);
            if (!_art.IsRowLoaded(row)) return;
            _row = row;
            _frame = 0;
            ScheduleNextFrame(nowUtc);
            RenderCurrentFrame();
        }

        private void StartPokeEasterEgg(DateTime nowUtc)
        {
            if (_bubbleCoordinator.CurrentKind.HasValue &&
                PetMessagePolicy.IsProtectedForegroundMessage(
                    _bubbleCoordinator.CurrentKind.Value)) return;
            if (!_animation.TryStartEasterEgg(FailedRow)) return;
            bool shown = _bubbleCoordinator.Show(PetBubbleRequest.EasterEgg(
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent)));
            if (!shown)
            {
                _animation.CancelInteractionAnimation();
                return;
            }
            _typingSession = false;
            QueueArtPreload(FailedRow);
            if (!_art.IsRowLoaded(FailedRow)) return;
            _row = FailedRow;
            _frame = 0;
            ScheduleNextFrame(nowUtc);
            RenderCurrentFrame();
        }

        private void TriggerTypingAnimation()
        {
            DateTime now = DateTime.UtcNow;
            if (!_typingSession)
            {
                _typingRow =
                    PetAnimationController.PickRandomTypingAnimationRow(
                        _random);
                _typingSession = true;
                QueueArtPreload(_typingRow);
                int duration = _art.IsRowLoaded(_typingRow)
                    ? RuntimeAnimationCycleDuration(_typingRow) : 2400;
                _typingUntilUtc = now.AddMilliseconds(duration + 80);
            }
            else
            {
                DateTime trailing = now.AddMilliseconds(900);
                if (trailing > _typingUntilUtc) _typingUntilUtc = trailing;
            }
        }

        private void QueueStartupInteractionPreload()
        {
            // Keep loading visible until hover and drag are both decoded and
            // scaled for the user's current pet size. Otherwise the first
            // mouse interaction still performs expensive frame work after the
            // loading image has disappeared.
            Thread preloadThread = new Thread(new ThreadStart(delegate
            {
                int[] warmRows = { HoverRow, FailedRow, WaitingRow, ThinkingRow };
                foreach (int row in warmRows)
                {
                    if (_art.IsRowLoaded(row)) continue;
                    bool ownsPreload = ReserveArtPreload(row);
                    try
                    {
                        if (ownsPreload) _art.PreloadRow(row);
                        else
                        {
                            for (int wait = 0; wait < 200 &&
                                !_art.IsRowLoaded(row); wait++)
                                Thread.Sleep(10);
                            if (!_art.IsRowLoaded(row)) _art.PreloadRow(row);
                        }
                    }
                    catch (Exception error)
                    {
                        if (!_exiting && !IsDisposed)
                            ApplicationDiagnostics.ReportNonFatal(
                                "art-preload-" + row, error);
                    }
                    finally
                    {
                        if (ownsPreload) CompleteArtPreload(row);
                    }
                }
                if (_exiting || IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (_exiting || IsDisposed) return;
                        try
                        {
                            foreach (int row in warmRows)
                                if (_art.IsRowLoaded(row))
                                    EnsureRenderedRow(row);
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "startup-interaction-render", error);
                        }
                        // Idle is the safe fallback if an optional animation
                        // is damaged; never leave the loading window stranded.
                        _startupArtReady = _art.IsRowLoaded(IdleRow);
                        TryRaiseStartupReady();
                    });
                }
                catch (InvalidOperationException) { }
            }));
            preloadThread.IsBackground = true;
            preloadThread.Priority = ThreadPriority.BelowNormal;
            preloadThread.Name = "Penny animation warmup";
            preloadThread.Start();
        }

        private void QueueArtPreload(int row)
        {
            if (!ReserveArtPreload(row)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _art.PreloadRow(row); }
                catch (Exception error)
                {
                    if (!_exiting && !IsDisposed)
                        ApplicationDiagnostics.ReportNonFatal(
                            "art-preload-" + row, error);
                }
                finally { CompleteArtPreload(row); }
            });
        }

        private bool ReserveArtPreload(int row)
        {
            if (_art == null) return false;
            return _artPreloads.TryReserve(row, _art.IsRowLoaded(row),
                DateTime.UtcNow);
        }

        private void CompleteArtPreload(int row)
        {
            bool loaded = _art != null && _art.IsRowLoaded(row);
            _artPreloads.Complete(row, loaded, DateTime.UtcNow);
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
            EnsureRenderedRow(_row);
            Bitmap[] rowFrames = _renderedFrames[_row];
            if (rowFrames == null || rowFrames.Length == 0) return;
            if (_frame < 0 || _frame >= rowFrames.Length) _frame = 0;
            Bitmap frame = rowFrames[_frame];
            if (frame != null) LayeredSpriteRenderer.Show(this, frame);
        }

        private void BuildRenderedFrameCache()
        {
            _renderedFrames = new Bitmap[PetArtPackage.RuntimeStateNames.Length][];
            _renderedFramesOwnBitmaps = _scalePercent != 100;
            EnsureRenderedRow(_row);
        }

        private void EnsureRenderedRow(int row)
        {
            if (_renderedFrames == null || row < 0 ||
                row >= _renderedFrames.Length)
                throw new ArgumentOutOfRangeException("row");
            if (_renderedFrames[row] != null) return;
            Dictionary<Bitmap, Bitmap> scaled = new Dictionary<Bitmap, Bitmap>();
            int count = RuntimeFrameCount(row);
            Bitmap[] rendered = new Bitmap[count];
            for (int frame = 0; frame < count; frame++)
            {
                Bitmap source = _art.GetFrame(row, frame);
                if (_scalePercent == 100)
                {
                    rendered[frame] = source;
                }
                else
                {
                    Bitmap resized;
                    if (!scaled.TryGetValue(source, out resized))
                    {
                        resized = ResizeFrame(source,
                            ScaledPetSize(_scalePercent));
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
