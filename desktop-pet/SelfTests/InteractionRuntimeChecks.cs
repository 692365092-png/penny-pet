using System;
using System.Drawing;
using System.Threading;
using static PennyPet.PetAnimationController;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private sealed class InteractionTestArt : IInteractionArt
        {
            public bool IsReady(int row) { return true; }
            public void Request(int row) { }
            public int FrameCount(int row) { return 3; }
            public int FrameDuration(int row, int frame) { return 40; }
            public int CycleDuration(int row) { return 120; }
        }

        private static bool RunInteractionCycleCheck()
        {
            DateTime start = DateTime.UtcNow;
            InteractionRuntime runtime = new InteractionRuntime(new InteractionTestArt(), start);
            bool first = runtime.StartPoke(HoverRow, PetInteractionAnimationKind.OrdinaryPoke, start);
            bool blocked = runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, start);
            bool easter = runtime.StartPoke(FailedRow, PetInteractionAnimationKind.EasterEgg, start);
            for (int frame = 1; frame <= 3; frame++)
                runtime.Tick(start.AddMilliseconds(frame * 40), false, false);
            bool next = runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke,
                start.AddMilliseconds(120));
            return first && !blocked && easter && next && runtime.Row == WaitingRow;
        }

        private static bool RunProtectedInteractionCheck()
        {
            DateTime start = DateTime.UtcNow;
            InteractionRuntime runtime = new InteractionRuntime(new InteractionTestArt(), start);
            if (!runtime.StartPoke(HoverRow, PetInteractionAnimationKind.OrdinaryPoke, start, true)) return false;
            runtime.BeginPointer(Point.Empty, Point.Empty);
            Point location;
            runtime.MovePointer(new Point(10, 0), out location);
            if (runtime.InteractionAnimationKind == PetInteractionAnimationKind.None) return false;
            runtime.EndPointer(out location);
            runtime.BeginReminderAttention(start);
            if (runtime.InteractionAnimationKind != PetInteractionAnimationKind.None ||
                runtime.Row != NotificationRow) return false;
            for (int frame = 1; frame <= 3; frame++)
                runtime.Tick(start.AddMilliseconds(frame * 40), false, false);
            return !runtime.ReminderAttentionActive && runtime.Row == IdleRow;
        }

        private static bool RunReadyArtReadCheck()
        {
            if (!RunArtTaskSharingCheck()) throw new InvalidOperationException("Art task sharing failed.");
            if (!RunArtRetryCheck()) throw new InvalidOperationException("Art retry bound failed.");
            if (!RunArtShutdownCheck()) throw new InvalidOperationException("Art late-result disposal failed.");
            if (!RunArtAliasCheck()) throw new InvalidOperationException("Art row alias sharing failed.");
            return true;
        }
    }
}
