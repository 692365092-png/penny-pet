using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.PetAnimationController;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class InteractionRuntimeTests
    {
        private static readonly DateTime Start = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Art : IInteractionArt
        {
            internal readonly HashSet<int> Missing = new HashSet<int>();
            internal readonly HashSet<int> Requested = new HashSet<int>();
            public bool IsReady(int row) { return !Missing.Contains(row); }
            public void Request(int row) { Requested.Add(row); }
            public int FrameCount(int row) { RequireReady(row); return 3; }
            public int FrameDuration(int row, int frame) { RequireReady(row); return 40; }
            public int CycleDuration(int row) { RequireReady(row); return 120; }
            private void RequireReady(int row)
            { Assert.IsTrue(IsReady(row), "Playback tried to decode an unavailable row: " + row); }
        }

        private static InteractionRuntime Create(Art art = null)
        { return new InteractionRuntime(art ?? new Art(), Start, new Random(42)); }

        private static void Tick(InteractionRuntime runtime, int ms, bool menu = false, bool slow = false)
        { runtime.Tick(Start.AddMilliseconds(ms), menu, slow); }

        [TestMethod]
        public void MissingPoke_PlaysIdleThenOneWholeRequestedCycle()
        {
            var art = new Art(); art.Missing.Add(WaitingRow);
            var runtime = Create(art);
            Assert.IsTrue(runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, Start));
            Assert.AreEqual(IdleRow, runtime.Row);
            Tick(runtime, 40); Assert.AreEqual(1, runtime.Frame);
            Tick(runtime, 80); Assert.AreEqual(2, runtime.Frame);
            Tick(runtime, 120); Assert.AreEqual(0, runtime.Frame);
            Assert.AreEqual(PetInteractionAnimationKind.OrdinaryPoke, runtime.InteractionAnimationKind);
            Assert.IsTrue(art.Requested.Contains(WaitingRow));
            art.Missing.Remove(WaitingRow);
            Tick(runtime, 140); Assert.AreEqual(WaitingRow, runtime.Row); Assert.AreEqual(0, runtime.Frame);
            Tick(runtime, 180); Assert.AreEqual(1, runtime.Frame);
            Tick(runtime, 220); Assert.AreEqual(2, runtime.Frame);
            Tick(runtime, 260); Assert.AreEqual(PetInteractionAnimationKind.None, runtime.InteractionAnimationKind);
            Assert.AreEqual(IdleRow, runtime.Row);
        }

        [TestMethod]
        public void Drag_CancelsPendingPokeAndRebasesAtDpiChange()
        {
            var art = new Art(); art.Missing.Add(WaitingRow);
            var runtime = Create(art);
            runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, Start);
            runtime.BeginPointer(new Point(10, 20), new Point(100, 200));
            Point location;
            Assert.IsFalse(runtime.MovePointer(new Point(14, 24), out location));
            Assert.IsTrue(runtime.MovePointer(new Point(16, 20), out location));
            Assert.AreEqual(new Point(106, 200), location);
            Assert.AreEqual(PetInteractionAnimationKind.None, runtime.InteractionAnimationKind);
            runtime.RebasePointer(new Point(16, 20), new Point(120, 230));
            Assert.IsTrue(runtime.MovePointer(new Point(17, 22), out location));
            Assert.AreEqual(new Point(121, 232), location);
            art.Missing.Remove(WaitingRow);
            Tick(runtime, 40); Assert.AreEqual(FailedRow, runtime.Row);
            runtime.EndPointer(out location);
            Tick(runtime, 80); Assert.AreEqual(IdleRow, runtime.Row);
            Assert.IsFalse(runtime.PointerDown);
        }

        [TestMethod]
        public void CaptureLoss_DoesNotTurnIntoPokeOrKeepDragging()
        {
            var runtime = Create(); Point location;
            runtime.BeginPointer(Point.Empty, new Point(100, 200));
            runtime.MovePointer(new Point(7, 0), out location);
            runtime.CancelPointer();
            Assert.IsFalse(runtime.PointerDown); Assert.IsFalse(runtime.DragMoved);
            Assert.IsFalse(runtime.MovePointer(new Point(9, 0), out location));
            Assert.AreEqual(PetInteractionAnimationKind.None, runtime.InteractionAnimationKind);
            runtime.BeginPointer(new Point(20, 30), new Point(200, 300));
            Assert.IsFalse(runtime.EndPointer(out location));
            Assert.AreEqual(new Point(200, 300), location);
        }

        [TestMethod]
        public void Hover_UsesDwellGraceAndPointerSuppressionUntilStableLeave()
        {
            var runtime = Create(); int changes = 0;
            runtime.HoverChanged += () => changes++;
            runtime.MouseEnter(Start);
            Tick(runtime, 99); Assert.IsFalse(runtime.StableMouseInside);
            Tick(runtime, 100); Assert.IsTrue(runtime.StableMouseInside); Assert.AreEqual(HoverRow, runtime.Row);
            runtime.BeginPointer(Point.Empty, Point.Empty);
            Point click; runtime.EndPointer(out click);
            Assert.IsTrue(runtime.HoverSuppressed);
            runtime.MouseLeave(Start.AddMilliseconds(110), false);
            Tick(runtime, 289); Assert.IsTrue(runtime.StableMouseInside); Assert.IsTrue(runtime.HoverSuppressed);
            Tick(runtime, 290); Assert.IsFalse(runtime.StableMouseInside); Assert.IsFalse(runtime.HoverSuppressed);
            runtime.MouseEnter(Start.AddMilliseconds(300));
            Tick(runtime, 400); Assert.AreEqual(HoverRow, runtime.Row);
            Tick(runtime, 440, menu: true); Assert.AreEqual(IdleRow, runtime.Row);
            runtime.MouseLeave(Start.AddMilliseconds(450), true);
            Assert.IsFalse(runtime.StableMouseInside);
            Assert.AreEqual(5, changes);
        }

        [TestMethod]
        public void Typing_OutranksHoverAndExpiresWithoutPausingForEditorFocus()
        {
            var runtime = Create(); runtime.MouseEnter(Start); Tick(runtime, 100);
            runtime.Type(Start.AddMilliseconds(100)); Tick(runtime, 110, slow: true);
            Assert.IsTrue(IsTypingAnimationRow(runtime.Row)); Assert.AreEqual(0, runtime.Frame);
            Tick(runtime, 189, slow: true); Assert.AreEqual(0, runtime.Frame);
            Tick(runtime, 190, slow: true); Assert.AreEqual(1, runtime.Frame);
            runtime.Type(Start.AddMilliseconds(200)); // extend to 1100
            Tick(runtime, 1000, slow: true); Assert.IsTrue(runtime.TypingSession);
            Tick(runtime, 1101, slow: true); Assert.IsFalse(runtime.TypingSession); Assert.AreEqual(HoverRow, runtime.Row);
        }

        [TestMethod]
        public void PokePriority_OrdinaryCannotRestartButEasterEggCanReplace()
        {
            var runtime = Create();
            Assert.IsTrue(runtime.StartPoke(HoverRow, PetInteractionAnimationKind.OrdinaryPoke, Start));
            Tick(runtime, 40);
            Assert.IsFalse(runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, Start));
            Assert.AreEqual(1, runtime.Frame);
            Assert.IsTrue(runtime.StartPoke(FailedRow, PetInteractionAnimationKind.EasterEgg, Start.AddMilliseconds(40)));
            Assert.AreEqual(0, runtime.Frame); Assert.AreEqual(FailedRow, runtime.Row);
            Tick(runtime, 80); Tick(runtime, 120); Tick(runtime, 160);
            Assert.AreEqual(PetInteractionAnimationKind.None, runtime.InteractionAnimationKind);
        }

        [TestMethod]
        public void ProtectedSmallTalk_SurvivesDragButCannotBlockReminder()
        {
            var runtime = Create(); Point location;
            runtime.StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, Start, true);
            runtime.BeginPointer(Point.Empty, Point.Empty);
            runtime.MovePointer(new Point(10, 0), out location);
            Tick(runtime, 20); Assert.AreEqual(FailedRow, runtime.Row);
            Assert.AreEqual(PetInteractionAnimationKind.OrdinaryPoke, runtime.InteractionAnimationKind);
            runtime.EndPointer(out location); Tick(runtime, 30); Assert.AreEqual(WaitingRow, runtime.Row);
            runtime.BeginReminderAttention(Start.AddMilliseconds(40));
            Assert.AreEqual(NotificationRow, runtime.Row);
            Assert.AreEqual(PetInteractionAnimationKind.None, runtime.InteractionAnimationKind);
            Assert.IsFalse(runtime.StartPoke(FailedRow, PetInteractionAnimationKind.EasterEgg, Start));
            Tick(runtime, 80); Tick(runtime, 120); Tick(runtime, 160);
            Assert.IsFalse(runtime.ReminderAttentionActive); Assert.AreEqual(IdleRow, runtime.Row);
        }

        [TestMethod]
        public void MissingReminder_IsNotConsumedByFallbackIdleCycle()
        {
            var art = new Art(); art.Missing.Add(NotificationRow);
            var runtime = Create(art); runtime.BeginReminderAttention(Start);
            Tick(runtime, 40); Tick(runtime, 80); Tick(runtime, 120);
            Assert.IsTrue(runtime.ReminderAttentionActive);
            art.Missing.Remove(NotificationRow); Tick(runtime, 140);
            Assert.AreEqual(NotificationRow, runtime.Row);
            Tick(runtime, 180); Tick(runtime, 220); Tick(runtime, 260);
            Assert.IsFalse(runtime.ReminderAttentionActive);
        }

        [TestMethod]
        public void Exit_PlaysFullGoodbyeOrClosesAfterMissingArtBudget()
        {
            var runtime = Create(); runtime.BeginExit(Start);
            Assert.AreEqual(WavingRow, runtime.Row);
            runtime.Type(Start); runtime.BeginPointer(Point.Empty, Point.Empty);
            Assert.IsFalse(runtime.TypingSession); Assert.IsFalse(runtime.PointerDown);
            Tick(runtime, 40); Tick(runtime, 80); Assert.IsFalse(runtime.ExitComplete);
            Tick(runtime, 120); Assert.IsTrue(runtime.ExitComplete);

            var art = new Art(); art.Missing.Add(WavingRow); runtime = Create(art);
            runtime.BeginExit(Start); Assert.AreEqual(IdleRow, runtime.Row);
            Tick(runtime, 1999); Assert.IsFalse(runtime.ExitComplete);
            Tick(runtime, 2000); Assert.IsTrue(runtime.ExitComplete);
            art.Missing.Remove(WavingRow); Tick(runtime, 2100);
            Assert.AreEqual(IdleRow, runtime.Row); // late completion cannot reopen shutdown
        }

        [TestMethod]
        public void Stop_RejectsInputsAndFramePublication()
        {
            var runtime = Create(); int frames = 0; runtime.FrameChanged += () => frames++;
            runtime.Stop(); runtime.Type(Start); runtime.BeginPointer(Point.Empty, Point.Empty);
            runtime.MouseEnter(Start); runtime.BeginReminderAttention(Start); runtime.BeginExit(Start);
            Assert.IsFalse(runtime.StartPoke(HoverRow, PetInteractionAnimationKind.OrdinaryPoke, Start));
            Tick(runtime, 5000);
            Assert.AreEqual(0, frames); Assert.IsFalse(runtime.StableMouseInside);
        }
    }
}
