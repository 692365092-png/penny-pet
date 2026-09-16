using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Pet-thread owner of the current native input. Drag and resize are mutually
    // exclusive; transport completion never decides which gesture owns input.
    internal sealed class DockGestureOwner
    {
        internal readonly DockInteractionSession Drag = new DockInteractionSession();
        internal readonly DockPlanMailbox Plans = new DockPlanMailbox();
        internal DockResizeSession Resize { get; private set; }
        internal DockInput Input { get; private set; }

        internal bool Matches(DockInput input) { return ReferenceEquals(Input, input); }

        internal long BeginDrag(DockWindowFacts source, IList<string> members,
            IDictionary<string, DockWindowFacts> baseline, long generation, DateTime startedUtc)
        {
            return Resize != null ? 0 : Drag.BeginGesture(source, members, baseline, generation, startedUtc);
        }

        // Return actions rather than invoking them: both owners and both queues
        // must be retired before a deferred hide/delete/restore can reenter.
        internal Action[] BeginInput(DockInput input)
        {
            Action[] drag = ResetDrag(true);
            Action[] resize = FinishResize();
            Input = input;
            if (drag.Length == 0) return resize;
            if (resize.Length == 0) return drag;
            Action[] deferred = new Action[drag.Length + resize.Length];
            drag.CopyTo(deferred, 0);
            resize.CopyTo(deferred, drag.Length);
            return deferred;
        }

        internal Action[] ResetDrag(bool clearMailbox)
        {
            Action[] deferred;
            Drag.Reset(out deferred);
            if (clearMailbox) Plans.Clear();
            return deferred;
        }

        internal bool TryBeginResize(DockResizeSession session)
        {
            if (session == null || Drag.IsActive || Resize != null || !Matches(session.Input)) return false;
            Resize = session;
            return true;
        }

        internal Action[] FinishResize(DockResizeSession expected = null)
        {
            if (expected != null && !ReferenceEquals(Resize, expected)) return Array.Empty<Action>();
            DockResizeSession previous = Resize;
            Resize = null;
            return previous == null ? Array.Empty<Action>() : previous.Finish();
        }
    }
}
