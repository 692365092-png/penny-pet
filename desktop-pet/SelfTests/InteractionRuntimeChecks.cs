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
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            using (ManualResetEvent locked = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent read = new ManualResetEvent(false))
            {
                art.PreloadRow(IdleRow);
                AnimationClip expected = art.GetLoadedClip(IdleRow);
                object gate = Pc2Get(art, "_resolveGate");
                Exception failure = null;
                bool ready = false;
                // Hold the exact decoder gate from another thread. A ready-row
                // render read must complete before that thread is released.
                Thread decoder = new Thread(() => {
                    lock (gate) { locked.Set(); release.WaitOne(); }
                });
                Thread reader = new Thread(() => {
                    try
                    {
                        ready = art.IsRowLoaded(IdleRow) &&
                            Object.ReferenceEquals(expected, art.GetLoadedClip(IdleRow)) &&
                            expected.Frames[0] != null && expected.FrameDuration(0) > 0;
                    }
                    catch (Exception error) { failure = error; }
                    finally { read.Set(); }
                });
                decoder.IsBackground = reader.IsBackground = true;
                decoder.Start();
                bool completed;
                bool readerStarted = false;
                try
                {
                    if (!locked.WaitOne(5000)) return false;
                    reader.Start();
                    readerStarted = true;
                    completed = read.WaitOne(5000);
                }
                finally
                {
                    release.Set();
                    decoder.Join();
                    if (readerStarted) reader.Join();
                }
                if (failure != null) throw failure;
                return completed && ready;
            }
        }
    }
}
