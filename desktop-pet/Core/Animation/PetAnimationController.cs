using System;

namespace PennyPet
{
    // Keeps animation state and selection policy independent from the WinForms
    // timer, art decoding and layered-window renderer owned by PetForm.
    internal sealed class PetAnimationController
    {
        internal const int IdleRow = 0;
        internal const int RightRow = 1;
        internal const int LeftRow = 2;
        internal const int WavingRow = 3;
        internal const int HoverRow = 4;
        internal const int FailedRow = 5;
        internal const int WaitingRow = 6;
        internal const int ThinkingRow = 7;
        internal const int ReviewRow = 8;
        internal const int NotificationRow = 9;
        internal const int IdleThoughtProbabilityDenominator = 20;
        internal const int GuitarFailureProbabilityDenominator = 6;
        internal const int ManualAnimationCooldownMilliseconds = 600;
        internal const int DragClickThresholdPixels = 6;

        private static readonly int[] ManualAnimationRows =
            { IdleRow, HoverRow, FailedRow, WaitingRow, ThinkingRow, ReviewRow,
                NotificationRow };

        internal PetAnimationController()
        {
            TypingRow = ThinkingRow;
            IdleRowState = IdleRow;
            ManualAnimationRow = -1;
        }

        internal int Row { get; set; }
        internal int Frame { get; set; }
        internal bool TypingSession { get; set; }
        internal int TypingRow { get; set; }
        internal int IdleRowState { get; set; }
        internal DateTime TypingUntilUtc { get; set; }
        internal bool ReminderAttentionActive { get; set; }
        internal DateTime NextFrameUtc { get; set; }
        internal DateTime ManualAnimationCooldownUntilUtc { get; set; }
        internal bool ManualAnimationActive { get; set; }
        internal int ManualAnimationRow { get; set; }

        internal int ChooseRow(bool exiting, bool draggingAndMoved,
            bool mouseInside, bool menuVisible, Func<int, bool> isRowLoaded)
        {
            if (isRowLoaded == null)
                throw new ArgumentNullException(nameof(isRowLoaded));
            if (exiting) return isRowLoaded(WavingRow) ? WavingRow : IdleRow;
            if (draggingAndMoved) return isRowLoaded(FailedRow)
                ? FailedRow : IdleRow;
            if (ManualAnimationActive) return isRowLoaded(ManualAnimationRow)
                ? ManualAnimationRow : IdleRow;
            if (TypingSession) return isRowLoaded(TypingRow)
                ? TypingRow : IdleRow;
            if (ReminderAttentionActive)
                return AttentionAnimationRow(isRowLoaded(NotificationRow));
            if (mouseInside && !menuVisible)
                return isRowLoaded(HoverRow) ? HoverRow : IdleRow;
            return isRowLoaded(IdleRowState) ? IdleRowState : IdleRow;
        }

        internal static bool ReminderAnimationCycleComplete(bool active,
            int row, int frame, int frameCount)
        {
            return active && row == NotificationRow && frameCount > 0 &&
                frame >= frameCount - 1;
        }

        internal static int AttentionAnimationRow(bool notificationLoaded)
        {
            return notificationLoaded ? NotificationRow : IdleRow;
        }

        internal static int PickRandomTypingAnimationRow(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return random.Next(GuitarFailureProbabilityDenominator) == 0
                ? ThinkingRow : WaitingRow;
        }

        internal static int PickRandomIdleAnimationRow(Random random,
            int currentRow)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int selected = random.Next(IdleThoughtProbabilityDenominator);
            int candidate = selected < IdleThoughtProbabilityDenominator - 2
                ? IdleRow : (selected == IdleThoughtProbabilityDenominator - 2
                    ? FailedRow : ReviewRow);
            // Ordinary idle may repeat; the two thought clips do not repeat
            // immediately. Together they occupy 2/20 = 10% of idle cycles.
            if (candidate == currentRow && candidate != IdleRow) return IdleRow;
            return candidate;
        }

        internal static bool IsIdleAnimationRow(int row)
        {
            return row == IdleRow || row == FailedRow || row == ReviewRow;
        }

        internal static bool IsTypingAnimationRow(int row)
        {
            return row == WaitingRow || row == ThinkingRow;
        }

        internal static bool ShouldPauseOwnNoteAnimation(bool composing,
            DateTime quietUntilUtc, DateTime nowUtc)
        {
            return composing || nowUtc < quietUntilUtc;
        }

        internal static bool IsManualAnimationRow(int row)
        {
            foreach (int candidate in ManualAnimationRows)
                if (candidate == row) return true;
            return false;
        }

        internal static int PickRandomManualAnimationRow(Random random,
            int currentRow)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int availableWeight = 0;
            foreach (int candidate in ManualAnimationRows)
            {
                if (candidate == currentRow) continue;
                availableWeight += ManualAnimationWeight(candidate);
            }
            if (availableWeight <= 0) return IdleRow;
            int selected = random.Next(availableWeight);
            foreach (int candidate in ManualAnimationRows)
            {
                if (candidate == currentRow) continue;
                int weight = ManualAnimationWeight(candidate);
                if (selected < weight) return candidate;
                selected -= weight;
            }
            return ManualAnimationRows[0];
        }

        private static int ManualAnimationWeight(int row)
        {
            // Keep both thought clips and the failed-guitar clip rare when the
            // user clicks the pet for a random animation as well.
            return row == FailedRow || row == ReviewRow || row == ThinkingRow
                ? 2 : 9;
        }

        internal static bool ManualAnimationClickReady(DateTime nowUtc,
            DateTime cooldownUntilUtc)
        {
            return nowUtc >= cooldownUntilUtc;
        }

        internal static bool MovementStartsDrag(int dx, int dy)
        {
            return dx * dx + dy * dy >=
                DragClickThresholdPixels * DragClickThresholdPixels;
        }
    }
}
