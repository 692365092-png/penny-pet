using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockInputHandoffTests
    {
        private static long StartDrag(DockGestureOwner owner)
        {
            long epoch = owner.BeginDrag(new DockWindowFacts("a", 0, 0, 300, 230, true, false),
                new[] { "a", "b" }, null, 7, DateTime.UtcNow);
            Assert.IsTrue(owner.Drag.TryEnterDragging(epoch, 7));
            return epoch;
        }

        private static WindowFacts Facts(string id, long sequence = 1, long generation = 7)
        {
            return new WindowFacts(id, "mdp:one", "DISPLAY1",
                new PhysicalRect(0, id == "a" ? 0 : 230, 300, 230), 96, generation, sequence);
        }

        private static DockResizeSession Resize(DockInput input, int kind, long generation = 7)
        {
            return DockResizeSession.TryStart(kind == 0 ? DockResizeKind.Horizontal : DockResizeKind.Divider,
                "a", new[] { Facts("a", generation: generation), Facts("b", generation: generation) }, input: input);
        }

        private static StickyUiEvent ResizeEvent(DockInput input, int kind, bool final = false,
            long sequence = 2, long generation = 7)
        {
            var snapshot = StickyNoteUiSnapshot.Capture(new StickyNoteData { Id = "a", Visible = true });
            var topology = StickyGeometryAuthorityTests.Topology(generation);
            StickyUiEvent value = kind == 0
                ? final ? StickyUiEvent.FromSnapshot(StickyUiEventKind.DockHorizontalResizeCompleted,
                    snapshot, sequence, Facts("a", sequence, generation), topology)
                    : StickyUiEvent.HorizontalResize(snapshot, sequence, 50, 350, Facts("a", sequence, generation), topology)
                : StickyUiEvent.DividerResize(final ? StickyUiEventKind.DockDividerResizeCompleted :
                    StickyUiEventKind.DockDividerResizing, snapshot, sequence, 300, Facts("a", sequence, generation), topology);
            value.Input = input;
            return value;
        }

        private static DockPlacementPlan Plan(DockInput input, long epoch, long sequence = 1)
        {
            return new DockPlacementPlan(7, sequence, "a", "surface", 96,
                new[] { new DockWindowTarget("a", Facts("a").PhysicalBounds),
                    new DockWindowTarget("b", Facts("b").PhysicalBounds) }, epoch, input);
        }

        [TestMethod]
        public void NativeStartRejectsAnAlreadyTakenPlanBeforePetReceivesTheNewInput()
        {
            var owner = new DockGestureOwner();
            var first = new DockInput();
            owner.BeginInput(first);
            StartDrag(owner);
            long finalEpoch = owner.Drag.BeginFinalizing(7, null);
            var final = Plan(first, finalEpoch);
            var finalMailbox = owner.Plans;
            finalMailbox.QueueFinal(final);
            var taken = finalMailbox.TakeFinal(final);
            Assert.IsTrue(DockExecutionRules.CanExecute(taken, 7, finalEpoch, first));

            // Same source, topology and Pet epoch; only the native input changed.
            var second = new DockInput();
            Assert.IsFalse(DockExecutionRules.CanExecute(taken, 7, finalEpoch, second));
            Assert.IsTrue(owner.Drag.IsFinalizing, "Pet has not processed the start yet.");
            Assert.IsTrue(owner.Matches(first));
            owner.BeginInput(second);
            long next = StartDrag(owner);
            Assert.IsTrue(owner.Plans.QueueLive(Plan(second, next, 2)));
            finalMailbox.CompleteFinal(final);
            Assert.IsFalse(owner.Drag.TryFinish(finalEpoch, 7, out _, out _));
            Assert.IsTrue(owner.Drag.IsActive);
            Assert.AreEqual(2L, owner.Plans.TakeLatest().PlanSequence);
        }

        [TestMethod]
        public void HeaderHandoffReleasesDeferredActionsBeforeTheyCanObserveOrReplaceOldOwners()
        {
            var owner = new DockGestureOwner();
            owner.BeginInput(new DockInput());
            StartDrag(owner);
            long old = owner.Drag.BeginFinalizing(7, null);
            var final = Plan(owner.Input, old);
            owner.Plans.QueueFinal(final);
            bool ran = false;
            var nextInput = new DockInput();
            owner.Drag.Mutations.Defer("a", null, () => {
                Assert.IsFalse(owner.Drag.IsActive);
                Assert.IsNull(owner.Resize);
                Assert.IsNull(owner.Plans.TakeFinal(final));
                Assert.IsTrue(owner.Matches(nextInput));
                Assert.IsTrue(owner.TryBeginResize(Resize(nextInput, 0)));
                ran = true;
            });
            Action[] deferred = owner.BeginInput(nextInput);
            Assert.IsFalse(ran);
            foreach (Action action in deferred) action();
            Assert.IsTrue(ran);
            Assert.IsFalse(owner.Drag.TryFinish(old, 7, out _, out _));
            Assert.IsTrue(owner.Resize.IsResizing);
            Assert.AreEqual(0L, owner.BeginDrag(new DockWindowFacts("a", 0, 0, 300, 230, true, false),
                new[] { "a", "b" }, null, 7, DateTime.UtcNow), "Resize owns the gesture until the next input.");
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void ResizeHandoffToHeaderRetiresFinalAndKeepsNewDragWhenOldCallbackArrives(int kind)
        {
            var owner = new DockGestureOwner();
            var oldInput = new DockInput();
            owner.BeginInput(oldInput);
            var old = Resize(oldInput, kind);
            Assert.IsTrue(owner.TryBeginResize(old));
            var final = old.BeginFinal(ResizeEvent(oldInput, kind, true));
            var next = new DockInput();
            int released = 0;
            old.Mutations.Defer("b", null, () => {
                Assert.IsNull(owner.Resize);
                Assert.IsFalse(owner.Drag.IsActive);
                released++;
            });
            foreach (Action action in owner.BeginInput(next)) action();
            StartDrag(owner);
            Assert.IsFalse(old.IsCurrentFinal(final));
            Assert.IsNull(old.Mailbox.TakeFinal(final));
            Assert.AreEqual(0, owner.FinishResize(old).Length);
            Assert.AreEqual(1, released);
            Assert.IsTrue(owner.Drag.IsActive);
            Assert.IsFalse(owner.TryBeginResize(Resize(next, kind)));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void ConsecutiveResizesOfSameWindowRejectOldEventsEvenWithHigherSequences(int kind)
        {
            var owner = new DockGestureOwner();
            var first = new DockInput();
            owner.BeginInput(first);
            var previous = Resize(first, kind);
            owner.TryBeginResize(previous);
            previous.BeginFinal(ResizeEvent(first, kind, true));
            var second = new DockInput();
            owner.BeginInput(second);
            var current = Resize(second, kind);
            Assert.IsTrue(owner.TryBeginResize(current));
            Assert.IsFalse(current.QueueLive(ResizeEvent(first, kind, sequence: 100), out _));
            Assert.IsNull(current.BeginFinal(ResizeEvent(first, kind, true, sequence: 101)));
            Assert.IsTrue(current.QueueLive(ResizeEvent(second, kind), out bool post));
            Assert.IsTrue(post);
            Assert.AreSame(second, current.Mailbox.TakeLatest().Input);
            var final = current.BeginFinal(ResizeEvent(second, kind, true, sequence: 3));
            Assert.AreSame(second, final.Input);
            Assert.AreEqual(0, owner.FinishResize(previous).Length);
            Assert.AreSame(current, owner.Resize);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void TopologyRecoveryKeepsInputIdentityAndCorrectionCannotReintroduceAnOldInput(int kind)
        {
            var owner = new DockGestureOwner();
            var input = new DockInput();
            owner.BeginInput(input);
            owner.TryBeginResize(Resize(input, kind));
            owner.FinishResize();
            var recovered = Resize(input, kind, 8);
            Assert.IsTrue(owner.TryBeginResize(recovered));
            var final = recovered.BeginFinal(ResizeEvent(input, kind, true, generation: 8));
            var actual = new DockBatchResult(0, 8, new[] { new DockBatchMemberResult("b", 4,
                new WindowFacts("b", "mdp:one", "DISPLAY1", new PhysicalRect(10, 280, 320, 240), 96, 8, 4), null) });
            var correction = recovered.TryCorrect(final, actual);
            Assert.IsNotNull(correction);
            Assert.AreSame(input, correction.Input);
            Assert.AreSame(correction, recovered.Mailbox.TakeFinal(correction));
        }

        [TestMethod]
        public void PlannerAndCommandsRetainTheNativeIdentityThroughFinalizationAndRebase()
        {
            var input = new DockInput();
            var topology = StickyGeometryAuthorityTests.Topology();
            var facts = Facts("a");
            var group = new DockGroupLogicalState(new LogicalPoint { X = 0, Y = 0 }, new[] {
                new DockLogicalMember("a", 300, 230), new DockLogicalMember("b", 300, 230) });
            var plan = DockPlacementPlanner.Plan(group, facts, topology.FindByRuntimeGdiName("DISPLAY1"), 96, 7, 1, 9, input);
            Assert.IsTrue(DockExecutionRules.CanExecute(plan, 7, 9, input));
            Assert.IsFalse(DockExecutionRules.CanExecute(plan, 8, 9, input));
            Assert.IsFalse(DockExecutionRules.CanExecute(plan, 7, 10, input));
            Assert.AreSame(input, StickyUiCommand.CaptureDockFacts(new[] { "a", "b" }, topology, 9, input).Input);
            Assert.AreSame(input, StickyUiCommand.RaiseDockGroupForDrag(new[] { "a", "b" }, "a", topology, 9, input).Input);
            Assert.AreSame(input, StickyUiCommand.SetBounds("b", new StickyUiBounds(0, 230, 300, 230), input: input).Input);
        }
    }
}
