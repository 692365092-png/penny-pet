using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal sealed class DockWindowFacts
    {
        internal DockWindowFacts(string noteId, int x, int y,
            int width, int height, bool visible, bool topMost)
        {
            NoteId = noteId ?? String.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Visible = visible;
            TopMost = topMost;
        }

        internal string NoteId { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal bool Visible { get; private set; }
        internal bool TopMost { get; private set; }

        internal static DockWindowFacts FromData(StickyNoteData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new DockWindowFacts(source.Id, source.X, source.Y,
                source.Width, source.Height, source.Visible,
                source.AlwaysOnTop);
        }

        internal static DockWindowFacts FromSnapshot(
            StickyNoteUiSnapshot source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new DockWindowFacts(source.NoteId, source.X, source.Y,
                source.Width, source.Height, source.Visible,
                source.AlwaysOnTop);
        }

        internal DockLayoutTarget ToTarget(int x, int y)
        {
            return new DockLayoutTarget(NoteId, x, y, Width, Height,
                Visible, TopMost);
        }

        internal static DockWindowFacts FromTarget(DockLayoutTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new DockWindowFacts(target.NoteId, target.X, target.Y,
                target.Width, target.Height, target.Visible, target.TopMost);
        }
    }

    internal sealed class DockLayoutTarget
    {
        internal DockLayoutTarget(string noteId, int x, int y,
            int width, int height, bool visible, bool topMost)
        {
            NoteId = noteId ?? String.Empty;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Visible = visible;
            TopMost = topMost;
        }

        internal string NoteId { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal bool Visible { get; private set; }
        internal bool TopMost { get; private set; }
    }

    // Latest-wins mailbox for a live dock drag frame. The Pet UI thread
    // replaces the immutable plan on every mouse move; the Sticky STA takes
    // only the newest plan and applies it in one deferred native batch.
    internal sealed class DockPlanMailbox
    {
        internal readonly object Gate = new object();
        private long _nextSequence;
        internal DockPlacementPlan Current;
        internal bool ApplyQueued;
        internal long FinalPlanSequence;

        internal long NextSequence()
        {
            return ++_nextSequence;
        }

        internal DockPlacementPlan TakeLatest()
        {
            lock (Gate)
            {
                if (Current != null &&
                    Current.PlanSequence == FinalPlanSequence)
                {
                    return null;
                }
                DockPlacementPlan plan = Current;
                Current = null;
                ApplyQueued = false;
                return plan;
            }
        }

        internal void ReplaceWithFinal(DockPlacementPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            lock (Gate)
            {
                Current = plan;
                FinalPlanSequence = plan.PlanSequence;
                ApplyQueued = true;
            }
        }

        internal DockPlacementPlan TakeFinal(long planSequence)
        {
            lock (Gate)
            {
                return Current != null && FinalPlanSequence == planSequence &&
                    Current.PlanSequence == planSequence ? Current : null;
            }
        }

        internal void CompleteFinal(long planSequence)
        {
            lock (Gate)
            {
                if (Current != null && Current.PlanSequence == planSequence)
                    Current = null;
                if (FinalPlanSequence == planSequence)
                    FinalPlanSequence = 0;
                ApplyQueued = false;
            }
        }
    }
}
