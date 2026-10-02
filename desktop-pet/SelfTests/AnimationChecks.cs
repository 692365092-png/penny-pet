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

        private sealed class AnimationCheckResult
        {
            internal bool SmoothTimingOk;
            internal bool GoodbyeOk;
            internal bool NotificationOk;
            internal bool NotificationSingleCycleOk;
            internal bool DragUsesSecondIdleRowOk;
            internal bool IdleRandomRowsOk;
            internal bool TypingRandomRowsOk;
            internal bool IdleThoughtProbabilityReducedOk;
            internal bool GuitarFailureProbabilityReducedOk;
            internal bool ManualRandomPoolOk;
            internal bool ManualSpecialProbabilityReducedOk;
            internal bool ManualFullCycleGuardOk;
            internal bool PokeBurstOk;
            internal bool ClickDragThresholdOk;
        }

        private static AnimationCheckResult RunAnimationChecks(
            int[] cycleDurations)
        {
            AnimationCheckResult result = new AnimationCheckResult();
            result.SmoothTimingOk = cycleDurations[0] >= 2000 &&
                cycleDurations[5] >= 2200 &&
                cycleDurations[6] >= 1800 &&
                cycleDurations[7] >= 2400 &&
                cycleDurations[8] >= 2000;
            result.GoodbyeOk = cycleDurations[3] >= 1200;
            result.NotificationOk = cycleDurations[9] >= 1200;
            result.NotificationSingleCycleOk =
                !PetAnimationController.ReminderAnimationCycleComplete(
                    true, 9, 1, 4) &&
                PetAnimationController.ReminderAnimationCycleComplete(
                    true, 9, 3, 4) &&
                !PetAnimationController.ReminderAnimationCycleComplete(
                    false, 9, 3, 4) &&
                !PetAnimationController.ReminderAnimationCycleComplete(
                    true, 0, 3, 4);
            result.DragUsesSecondIdleRowOk =
                PetAnimationController.FailedRow == 5;
            Random random = new Random(20260810);
            HashSet<int> idleChoices = new HashSet<int>();
            HashSet<int> typingChoices = new HashSet<int>();
            int idleRow = -1;
            bool idleNoImmediateRepeat = true;
            for (int i = 0; i < 256; i++)
            {
                int next = PetAnimationController.PickRandomIdleAnimationRow(
                    random, idleRow);
                idleNoImmediateRepeat = idleNoImmediateRepeat &&
                    (next == 0 || next != idleRow);
                idleChoices.Add(next);
                idleRow = next;
                typingChoices.Add(
                    PetAnimationController.PickRandomTypingAnimationRow(random));
            }
            result.IdleRandomRowsOk = idleNoImmediateRepeat &&
                idleChoices.Count == 3 &&
                PetAnimationController.IsIdleAnimationRow(0) &&
                PetAnimationController.IsIdleAnimationRow(5) &&
                PetAnimationController.IsIdleAnimationRow(8) &&
                !PetAnimationController.IsIdleAnimationRow(7);
            result.TypingRandomRowsOk = typingChoices.Count == 2 &&
                PetAnimationController.IsTypingAnimationRow(6) &&
                PetAnimationController.IsTypingAnimationRow(7) &&
                !PetAnimationController.IsTypingAnimationRow(8);
            Random probabilityRandom = new Random(20260820);
            int firstThought = 0;
            int secondThought = 0;
            for (int i = 0; i < 100000; i++)
            {
                int selected =
                    PetAnimationController.PickRandomIdleAnimationRow(
                        probabilityRandom, -1);
                if (selected == 5) firstThought++;
                if (selected == 8) secondThought++;
            }
            result.IdleThoughtProbabilityReducedOk =
                PetAnimationController.IdleThoughtProbabilityDenominator == 20 &&
                firstThought >= 4300 && firstThought <= 5700 &&
                secondThought >= 4300 && secondThought <= 5700;
            int failedGuitar = 0;
            for (int i = 0; i < 60000; i++)
                if (PetAnimationController.PickRandomTypingAnimationRow(
                    probabilityRandom) == 7) failedGuitar++;
            result.GuitarFailureProbabilityReducedOk =
                PetAnimationController.GuitarFailureProbabilityDenominator == 6 &&
                failedGuitar >= 9500 && failedGuitar <= 10500;
            Random manualRandom = new Random(20260811);
            HashSet<int> manualRows = new HashSet<int>();
            int manualRow = -1;
            bool manualNoImmediateRepeat = true;
            for (int i = 0; i < 256; i++)
            {
                int next = PetAnimationController.PickRandomManualAnimationRow(
                    manualRandom, manualRow);
                manualNoImmediateRepeat = manualNoImmediateRepeat &&
                    next != manualRow;
                manualRows.Add(next);
                manualRow = next;
            }
            result.ManualRandomPoolOk = manualNoImmediateRepeat &&
                manualRows.Count == 6 &&
                PetAnimationController.IsManualAnimationRow(0) &&
                PetAnimationController.IsManualAnimationRow(4) &&
                PetAnimationController.IsManualAnimationRow(5) &&
                PetAnimationController.IsManualAnimationRow(6) &&
                PetAnimationController.IsManualAnimationRow(7) &&
                PetAnimationController.IsManualAnimationRow(8) &&
                !PetAnimationController.IsManualAnimationRow(9) &&
                !PetAnimationController.IsManualAnimationRow(1) &&
                !PetAnimationController.IsManualAnimationRow(2) &&
                !PetAnimationController.IsManualAnimationRow(3);
            Random manualProbabilityRandom = new Random(20260821);
            int manualFirstThought = 0;
            int manualFailedGuitar = 0;
            int manualSecondThought = 0;
            for (int i = 0; i < 42000; i++)
            {
                int selected =
                    PetAnimationController.PickRandomManualAnimationRow(
                        manualProbabilityRandom, -1);
                if (selected == 5) manualFirstThought++;
                if (selected == 7) manualFailedGuitar++;
                if (selected == 8) manualSecondThought++;
            }
            result.ManualSpecialProbabilityReducedOk =
                manualFirstThought >= 2200 && manualFirstThought <= 2900 &&
                manualFailedGuitar >= 2200 && manualFailedGuitar <= 2900 &&
                manualSecondThought >= 2200 && manualSecondThought <= 2900;
            result.ManualFullCycleGuardOk = RunInteractionCycleCheck() &&
                RunReadyArtReadCheck();
            DateTime burstStart = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            PetPokeBurstTracker burst = new PetPokeBurstTracker();
            bool earlyTrigger = false;
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                earlyTrigger |= burst.RegisterPoke(
                    burstStart.AddMilliseconds((poke - 1) * 100));
            bool targetTrigger = burst.RegisterPoke(
                burstStart.AddMilliseconds(4900));
            bool repeatedTrigger = burst.RegisterPoke(
                burstStart.AddMilliseconds(5000));
            PetPokeBurstTracker resetBurst = new PetPokeBurstTracker();
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                resetBurst.RegisterPoke(
                    burstStart.AddMilliseconds((poke - 1) * 100));
            bool afterPause = resetBurst.RegisterPoke(
                burstStart.AddMilliseconds(5201));
            result.PokeBurstOk = !earlyTrigger && targetTrigger &&
                !repeatedTrigger && !afterPause;
            result.ClickDragThresholdOk =
                !PetAnimationController.MovementStartsDrag(5, 0) &&
                !PetAnimationController.MovementStartsDrag(4, 4) &&
                PetAnimationController.MovementStartsDrag(6, 0) &&
                PetAnimationController.MovementStartsDrag(5, 4);
            return result;
        }

        private sealed class SequenceRandom : Random
        {
            private readonly int[] _values;
            private int _index;

            internal SequenceRandom(params int[] values)
            {
                _values = values ?? new int[0];
            }

            public override int Next(int maxValue)
            {
                if (maxValue <= 0) return 0;
                int value = _index < _values.Length ? _values[_index++] : 0;
                return (value & Int32.MaxValue) % maxValue;
            }
        }
    }
}
