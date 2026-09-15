using System;
using System.Collections.Generic;

namespace PennyPet
{
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

        internal void Clear()
        {
            lock (Gate) { Current = null; ApplyQueued = false; FinalPlanSequence = 0; }
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
                if (FinalPlanSequence != planSequence) return;
                if (Current != null && Current.PlanSequence == planSequence)
                    Current = null;
                if (FinalPlanSequence == planSequence)
                    FinalPlanSequence = 0;
                ApplyQueued = false;
            }
        }
    }

    // Physical resize targets: below the source for a divider, every other
    // visible member for a horizontal resize. Windows owns the source HWND.
    internal sealed class DockResizeBatch
    {
        internal DockResizeBatch(
            long topologyGeneration,
            IList<DockWindowTarget> targets, DockInput input = null)
        {
            TopologyGeneration = topologyGeneration;
            Input = input;
            Targets = new List<DockWindowTarget>(
                targets == null
                    ? new DockWindowTarget[0]
                    : targets).AsReadOnly();
        }

        internal long TopologyGeneration { get; private set; }
        internal DockInput Input { get; private set; }
        internal IReadOnlyList<DockWindowTarget> Targets { get; private set; }
    }

    // Latest-wins mailbox for a live resize frame. The Pet UI thread
    // replaces the pending batch on every WM_SIZING tick; the Sticky STA takes
    // only the newest batch in one deferred apply, and a final batch replaces
    // and supersedes all pending live frames.
    internal sealed class DockResizeMailbox
    {
        private readonly object Gate = new object();
        private DockResizeBatch _current;
        private bool _applyQueued;
        private bool _finalStarted;
        private bool _closed;

        internal bool HasPending
        {
            get
            {
                lock (Gate) return _current != null;
            }
        }

        // Replaces the live batch. Returns true when Pet must schedule a new
        // deferred apply; at most one apply is ever in flight.
        internal bool QueueLive(DockResizeBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            lock (Gate)
            {
                if (_closed || _finalStarted) return false;
                _current = batch;
                if (_applyQueued) return false;
                _applyQueued = true;
                return true;
            }
        }

        // A final batch supersedes every pending live frame. Returns true when
        // Pet must schedule the final apply.
        internal bool QueueFinal(DockResizeBatch batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            lock (Gate)
            {
                if (_closed) return false;
                _current = batch;
                _finalStarted = true;
                _applyQueued = true;
                return true;
            }
        }

        // Sticky STA, inside one deferred dispatcher frame. While a final
        // batch is pending, live applies no-op so they can never run after
        // the authoritative final frame.
        internal DockResizeBatch TakeLatest()
        {
            lock (Gate)
            {
                if (_closed || _finalStarted) return null;
                DockResizeBatch batch = _current;
                _current = null;
                _applyQueued = false;
                return batch;
            }
        }

        internal DockResizeBatch TakeFinal(DockResizeBatch expected)
        {
            lock (Gate)
            {
                return !_closed && _finalStarted && ReferenceEquals(_current, expected)
                    ? _current : null;
            }
        }

        internal void CompleteFinal(DockResizeBatch expected)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_current, expected)) return;
                _current = null;
                _applyQueued = false;
                // Live stays closed while Pet accepts/corrects the result.
            }
        }

        internal void Cancel()
        {
            lock (Gate)
            {
                _closed = true;
                _current = null;
                _applyQueued = false;
            }
        }
    }
}
