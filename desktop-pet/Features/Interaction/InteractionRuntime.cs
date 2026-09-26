using System;
using System.Drawing;
using static PennyPet.PetAnimationController;

namespace PennyPet
{
    // Readiness queries and reads of ready frames must never decode or wait for
    // a decoder. Request queues optional work; Idle is ready before construction.
    internal interface IInteractionArt
    {
        bool IsReady(int row);
        void Request(int row);
        int FrameCount(int row);
        int FrameDuration(int row, int frame);
        int CycleDuration(int row);
    }

    // One Pet-STA owner. The shell supplies native input and ticks, then presents
    // read-only frames. No window, timer, bitmap or dispatcher lives here.
    internal sealed partial class InteractionRuntime
    {
        private readonly IInteractionArt _art;
        private readonly Random _random;
        private int _typingRow = ThinkingRow;
        private int _idleRow = IdleRow;
        private DateTime _typingUntilUtc;
        private DateTime _nextFrameUtc;
        private DateTime _enterCandidateUtc;
        private DateTime _leaveCandidateUtc;
        private DateTime _exitArtDeadlineUtc;
        private Point _mouseOrigin;
        private Point _windowOrigin;
        private bool _slowFrames;
        private bool _menuVisible;
        private bool _exiting;
        private bool _stopped;
        private int _pokeGeneration;

        internal InteractionRuntime(IInteractionArt art, DateTime now,
            Random random = null)
        {
            _art = art ?? throw new ArgumentNullException(nameof(art));
            _random = random ?? new Random();
            ScheduleNextFrame(now);
        }

        internal int Row { get; private set; }
        internal int Frame { get; private set; }
        internal bool TypingSession { get; private set; }
        internal bool ReminderAttentionActive { get; private set; }
        internal bool PointerDown { get; private set; }
        internal bool DragMoved { get; private set; }
        internal bool StableMouseInside { get; private set; }
        internal bool HoverSuppressed { get; private set; }
        internal bool ExitComplete { get; private set; }
        internal PetInteractionAnimationKind InteractionAnimationKind { get; private set; }
        internal int InteractionAnimationRow { get; private set; } = -1;
        private bool _smallTalkProtected;
        internal event Action FrameChanged;
        internal event Action HoverChanged;

        internal void Tick(DateTime now, bool menuVisible, bool slowFrames)
        {
            if (_stopped || ExitComplete) return;
            _menuVisible = menuVisible;
            _slowFrames = slowFrames;
            if (_exiting)
            {
                if (!_art.IsReady(WavingRow))
                {
                    _art.Request(WavingRow);
                    if (now >= _exitArtDeadlineUtc) { ExitComplete = true; return; }
                    AdvanceLoop(IdleRow, now);
                    return;
                }
                if (Row != WavingRow) { SetRow(WavingRow, now); return; }
                if (now < _nextFrameUtc) return;
                if (Frame >= _art.FrameCount(Row) - 1) ExitComplete = true;
                else { Frame++; PublishFrame(now); }
                return;
            }

            TickHover(now);
            if (TypingSession && now > _typingUntilUtc) TypingSession = false;
            int wanted = ChooseRow();
            bool ready = _art.IsReady(wanted);
            if (!ready) { _art.Request(wanted); wanted = IdleRow; }
            if (Row != wanted) { SetRow(wanted, now); return; }
            if (now < _nextFrameUtc) return;

            bool lastFrame = Frame >= _art.FrameCount(Row) - 1;
            if (ready && lastFrame && ReminderAttentionActive)
            {
                ReminderAttentionActive = false;
                _idleRow = IdleRow;
                SetRow(IdleRow, now);
            }
            else if (ready && lastFrame && InteractionAnimationKind !=
                PetInteractionAnimationKind.None && !(PointerDown && DragMoved))
            {
                CompleteInteractionAnimation();
                SetRow(ReadyRow(ChooseRow()), now);
            }
            else if (ready && lastFrame && !ReminderAttentionActive &&
                InteractionAnimationKind == PetInteractionAnimationKind.None &&
                !(PointerDown && DragMoved) && !TypingSession &&
                !(StableMouseInside && !HoverSuppressed && !_menuVisible) &&
                IsIdleAnimationRow(Row))
            {
                _idleRow = PickRandomIdleAnimationRow(_random, Row);
                SetRow(ReadyRow(_idleRow), now);
            }
            else AdvanceLoop(wanted, now);
        }

        private int ChooseRow()
        {
            if (ReminderAttentionActive) return NotificationRow;
            if (PointerDown && DragMoved) return FailedRow;
            if (InteractionAnimationKind != PetInteractionAnimationKind.None)
                return InteractionAnimationRow;
            if (TypingSession) return _typingRow;
            if (StableMouseInside && !HoverSuppressed && !_menuVisible) return HoverRow;
            return _idleRow;
        }

        private int ReadyRow(int row)
        {
            if (_art.IsReady(row)) return row;
            _art.Request(row);
            return IdleRow;
        }

        private void AdvanceLoop(int row, DateTime now)
        {
            if (Row != row) { SetRow(row, now); return; }
            if (now < _nextFrameUtc) return;
            Frame = (Frame + 1) % _art.FrameCount(Row);
            PublishFrame(now);
        }

        private void SetRow(int row, DateTime now)
        {
            Row = row;
            Frame = 0;
            PublishFrame(now);
        }

        private void ScheduleNextFrame(DateTime now)
        {
            int duration = _art.FrameDuration(Row, Frame);
            if (!_exiting && _slowFrames) duration = Math.Max(40, duration * 2);
            _nextFrameUtc = now.AddMilliseconds(duration);
        }

        private void PublishFrame(DateTime now)
        {
            ScheduleNextFrame(now);
            FrameChanged?.Invoke();
        }

        internal bool StartPoke(int row, PetInteractionAnimationKind kind,
            DateTime now, bool smallTalkProtected = false)
        {
            if (_stopped || _exiting || ReminderAttentionActive ||
                (kind != PetInteractionAnimationKind.EasterEgg &&
                 InteractionAnimationKind != PetInteractionAnimationKind.None)) return false;
            InteractionAnimationKind = kind;
            InteractionAnimationRow = row;
            _smallTalkProtected = smallTalkProtected;
            TypingSession = false;
            SetRow(ReadyRow(row), now);
            return true;
        }

        private void CompleteInteractionAnimation()
        {
            InteractionAnimationKind = PetInteractionAnimationKind.None;
            InteractionAnimationRow = -1;
            _smallTalkProtected = false;
        }

        internal void TakeReminder()
        {
            _pokeGeneration++;
            // A reminder outranks even a protected small-talk cycle.
            CompleteInteractionAnimation();
        }

        internal void BeginReminderAttention(DateTime now)
        {
            if (_stopped || _exiting) return;
            TakeReminder();
            ReminderAttentionActive = true;
            SetRow(ReadyRow(NotificationRow), now);
        }

        internal void Type(DateTime now)
        {
            if (_stopped || _exiting || PointerDown) return;
            if (!TypingSession)
            {
                _typingRow = PickRandomTypingAnimationRow(_random);
                TypingSession = true;
                _art.Request(_typingRow);
                int duration = _art.IsReady(_typingRow) ? _art.CycleDuration(_typingRow) : 2400;
                _typingUntilUtc = now.AddMilliseconds(duration + 80);
            }
            else if (now.AddMilliseconds(900) > _typingUntilUtc)
                _typingUntilUtc = now.AddMilliseconds(900);
        }

        internal void BeginPointer(Point cursor, Point window)
        {
            if (_stopped || _exiting) return;
            PointerDown = true;
            DragMoved = false;
            _mouseOrigin = cursor;
            _windowOrigin = window;
            HoverSuppressed = true;
            TypingSession = false;
            _art.Request(FailedRow);
            HoverChanged?.Invoke();
        }

        internal bool MovePointer(Point cursor, out Point location)
        {
            location = _windowOrigin;
            if (!PointerDown) return false;
            int dx = cursor.X - _mouseOrigin.X, dy = cursor.Y - _mouseOrigin.Y;
            if (!DragMoved && !MovementStartsDrag(dx, dy)) return false;
            if (!DragMoved)
            {
                DragMoved = true;
                _pokeGeneration++;
                if (!_smallTalkProtected) CompleteInteractionAnimation();
            }
            location.Offset(dx, dy);
            return true;
        }

        internal void RebasePointer(Point cursor, Point window)
        {
            if (!PointerDown) return;
            _mouseOrigin = cursor;
            _windowOrigin = window;
        }

        internal bool EndPointer(out Point clickLocation)
        {
            bool moved = DragMoved;
            clickLocation = _windowOrigin;
            PointerDown = false;
            DragMoved = false;
            return moved;
        }

        internal void CancelPointer()
        {
            PointerDown = false;
            DragMoved = false;
            _pokeGeneration++;
        }

        internal void MouseEnter(DateTime now)
        {
            if (_stopped || _exiting) return;
            _leaveCandidateUtc = default(DateTime);
            _enterCandidateUtc = now;
        }

        internal void MouseLeave(DateTime now, bool outsideBounds)
        {
            if (_stopped || _exiting) return;
            _enterCandidateUtc = default(DateTime);
            if (outsideBounds) CommitStableLeave();
            else _leaveCandidateUtc = now;
        }

        private void TickHover(DateTime now)
        {
            if (PetHoverStabilityRules.ShouldCommitEnter(_enterCandidateUtc, now))
            {
                _enterCandidateUtc = default(DateTime);
                if (!StableMouseInside)
                {
                    StableMouseInside = true;
                    _art.Request(HoverRow);
                    HoverChanged?.Invoke();
                }
            }
            if (PetHoverStabilityRules.ShouldCommitLeave(_leaveCandidateUtc, now))
                CommitStableLeave();
        }

        private void CommitStableLeave()
        {
            StableMouseInside = false;
            HoverSuppressed = false;
            _enterCandidateUtc = _leaveCandidateUtc = default(DateTime);
            HoverChanged?.Invoke();
        }

        internal void BeginExit(DateTime now)
        {
            if (_stopped || _exiting) return;
            _exiting = true;
            CancelPointer();
            TypingSession = false;
            ReminderAttentionActive = false;
            CompleteInteractionAnimation();
            CommitStableLeave();
            // A damaged/missing optional goodbye clip must not strand shutdown.
            _exitArtDeadlineUtc = now.AddSeconds(2);
            SetRow(ReadyRow(WavingRow), now);
        }

        internal void Stop()
        {
            _stopped = true;
            CancelPointer();
            FrameChanged = null;
            HoverChanged = null;
        }
    }
}
