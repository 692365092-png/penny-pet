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

        internal static DockWindowFacts FromWindowFacts(WindowFacts facts,
            bool visible, bool topMost)
        {
            if (facts == null) return null;
            PhysicalRect rect = facts.PhysicalBounds;
            return new DockWindowFacts(facts.WindowId, rect.Left, rect.Top,
                rect.Width, rect.Height, visible, topMost);
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

    // One detached follower batch for a vertical Dock divider gesture. The
    // target rects are physical pixels and cover only the members BELOW the
    // divider source; the source itself is Windows-live during WM_SIZING.
    internal sealed class DockDividerFollowerBatch
    {
        internal DockDividerFollowerBatch(
            long topologyGeneration,
            IList<DockWindowTarget> targets)
        {
            TopologyGeneration = topologyGeneration;
            Targets = new List<DockWindowTarget>(
                targets == null
                    ? new DockWindowTarget[0]
                    : targets);
        }

        internal long TopologyGeneration { get; private set; }
        internal IReadOnlyList<DockWindowTarget> Targets { get; private set; }
    }

    // Latest-wins mailbox for a live divider resize frame. The Pet UI thread
    // replaces the pending batch on every WM_SIZING tick; the Sticky STA takes
    // only the newest batch in one deferred apply, and a final batch replaces
    // and supersedes all pending live frames.
    internal sealed class DockDividerFollowerMailbox
    {
        internal readonly object Gate = new object();
        private DockDividerFollowerBatch _current;
        private bool _applyQueued;
        private bool _finalPending;

        internal bool HasPending
        {
            get
            {
                lock (Gate) return _current != null;
            }
        }

        internal bool FinalPending
        {
            get
            {
                lock (Gate) return _finalPending;
            }
        }

        // Replaces the live batch. Returns true when Pet must schedule a new
        // deferred apply; at most one apply is ever in flight.
        internal bool QueueLive(DockDividerFollowerBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            lock (Gate)
            {
                if (_finalPending) return false;
                _current = batch;
                if (_applyQueued) return false;
                _applyQueued = true;
                return true;
            }
        }

        // A final batch supersedes every pending live frame. Returns true when
        // Pet must schedule the final apply.
        internal bool QueueFinal(DockDividerFollowerBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            lock (Gate)
            {
                _current = batch;
                _finalPending = true;
                _applyQueued = true;
                return true;
            }
        }

        // Sticky STA, inside one deferred dispatcher frame. While a final
        // batch is pending, live applies no-op so they can never run after
        // the authoritative final frame.
        internal DockDividerFollowerBatch TakeLatest()
        {
            lock (Gate)
            {
                if (_finalPending) return null;
                DockDividerFollowerBatch batch = _current;
                _current = null;
                _applyQueued = false;
                return batch;
            }
        }

        internal DockDividerFollowerBatch TakeFinal()
        {
            lock (Gate)
            {
                if (!_finalPending) return null;
                return _current;
            }
        }

        internal void CompleteFinal()
        {
            lock (Gate)
            {
                _current = null;
                _finalPending = false;
                _applyQueued = false;
            }
        }
    }
}
