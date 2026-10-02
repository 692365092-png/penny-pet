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

        private sealed class StickyHostedCheckResult
        {
            internal bool LifecycleOk;
            internal bool PerNoteSequenceOk;
            internal bool CloseAllBatchOk;
            internal bool HostedDockEffectOk;
            internal bool HostedGroupMoveOk;
            internal bool HostedTopMostOk;
            internal bool HostedHorizontalResizeOk;
            internal bool HostedDividerResizeOk;
            internal bool HostedHideReopenOk;
            internal bool HostedMiddleSplitOk;
            internal bool HostedThreeNoteInsertionOk;
            internal bool DockRestoreOk;
            internal bool HostedSetBoundsAtomicOk;
        }

        private static StickyHostedCheckResult RunStickyHostedLifecycleCheck()
        {
            StickyHostedCheckResult check = new StickyHostedCheckResult();
            int petThread = Thread.CurrentThread.ManagedThreadId;
            int eventThread = 0;
            StickyUiEvent lastEvent = null;
            HashSet<string> eventNoteIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            HashSet<StickyUiEventKind> eventKinds =
                new HashSet<StickyUiEventKind>();
            bool setBoundsInFlight = false;
            bool setBoundsLeakedHeaderDrag = false;
            SynchronizationContext petContext =
                new WindowsFormsSynchronizationContext();
            StickyNoteData canonical = new StickyNoteData();
            canonical.Text = "detached-before-post";
            canonical.X = -2400;
            canonical.Y = -2400;
            canonical.Width = 320;
            canonical.Height = 300;
            StickyNoteUiSnapshot detached =
                StickyNoteUiSnapshot.Capture(canonical);
            canonical.Text = "pet-owned-after-post";
            StickyNoteData second = new StickyNoteData();
            second.Text = "second-detached";
            second.X = -2000;
            second.Y = -2000;
            second.Width = 320;
            second.Height = 300;
            StickyNoteData third = new StickyNoteData();
            third.Text = "third-detached";
            third.X = -1800;
            third.Y = -1800;
            third.Width = 320;
            third.Height = 300;
            StickyNoteData todo = new StickyNoteData();
            todo.IsTodoList = true;
            todo.Text = "todo-detached";
            todo.TodoItems.Add(new StickyTodoItem("todo", false));
            todo.X = -1600;
            todo.Y = -1600;
            todo.Width = 320;
            todo.Height = 300;
            StickyNoteData schedule = new StickyNoteData();
            schedule.IsSchedule = true;
            schedule.Text = "schedule-detached";
            schedule.ScheduleItems.Add(new StickyScheduleItem(
                "schedule", DateTime.Today));
            schedule.X = -1200;
            schedule.Y = -1200;
            schedule.Width = 320;
            schedule.Height = 300;
            StickyNoteData reminder = new StickyNoteData();
            reminder.Text = "reminder-detached";
            reminder.ReminderUtcTicks = DateTime.UtcNow.AddHours(1).Ticks;
            reminder.X = -800;
            reminder.Y = -800;
            reminder.Width = 320;
            reminder.Height = 300;

            using (StickyUiHost host = new StickyUiHost())
            {
                host.Start();
                host.Configure(delegate(StickyUiEvent value)
                {
                    eventThread = Thread.CurrentThread.ManagedThreadId;
                    lastEvent = value;
                    if (value != null)
                    {
                        eventNoteIds.Add(value.NoteId);
                        eventKinds.Add(value.Kind);
                        if (value.Kind == StickyUiEventKind.HeaderDragMoved &&
                            setBoundsInFlight &&
                            String.Equals(value.NoteId, canonical.Id,
                                StringComparison.OrdinalIgnoreCase))
                            setBoundsLeakedHeaderDrag = true;
                    }
                }, petContext);
                StickyUiCommandResult created = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Create,
                        canonical.Id, false, detached), petContext);
                bool detachedOwnership = created != null &&
                    created.Status == StickyUiCommandStatus.Handled &&
                    created.Snapshot != null &&
                    created.Snapshot.Text == "detached-before-post" &&
                    canonical.Text == "pet-owned-after-post" &&
                    created.OwnerThreadId != petThread;
                StickyUiCommandResult secondCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            second.Id, false,
                            StickyNoteUiSnapshot.Capture(second)), petContext);
                StickyUiCommandResult thirdCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            third.Id, false,
                            StickyNoteUiSnapshot.Capture(third)), petContext);
                StickyUiCommandResult todoCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            todo.Id, false,
                            StickyNoteUiSnapshot.Capture(todo)), petContext);
                StickyUiCommandResult scheduleCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            schedule.Id, false,
                            StickyNoteUiSnapshot.Capture(schedule)), petContext);
                StickyUiCommandResult reminderCreated =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.Create,
                            reminder.Id, false,
                            StickyNoteUiSnapshot.Capture(reminder)), petContext);

                StickyUiCommandResult hidden = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Hide,
                        canonical.Id, false), petContext);
                StickyUiCommandResult shown = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Show,
                        canonical.Id, false), petContext);
                StickyUiCommandResult closedOne = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Close,
                        canonical.Id, false), petContext);
                StickyUiCommandResult reopened = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.Create,
                        canonical.Id, false, detached), petContext);
                StickyUiCommandResult targetPositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null,
                            new StickyUiBounds(100, 100, 320, 300)),
                        petContext);
                StickyUiCommandResult sourcePositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null,
                            new StickyUiBounds(100, 400, 320, 300)),
                        petContext);
                DockWindowFacts targetFacts = targetPositioned == null ? null :
                    HostedDockFacts(targetPositioned);
                DockWindowFacts sourceFacts = sourcePositioned == null ? null :
                    HostedDockFacts(sourcePositioned);
                bool dockHit = sourceFacts != null && targetFacts != null &&
                    CanDockBelowForCheck(new Rectangle(sourceFacts.X,
                        sourceFacts.Y, sourceFacts.Width, sourceFacts.Height),
                        new Rectangle(targetFacts.X, targetFacts.Y,
                            targetFacts.Width, targetFacts.Height), 20);
                List<StickyNoteData> dockOrder = dockHit
                    ? StickyDockOperations.MergeDockSnapshotsAfterParent(
                        new StickyNoteData[] { second }, second,
                        new StickyNoteData[] { canonical })
                    : new List<StickyNoteData>();
                List<Rectangle> hostedLayout = StickyDockController.CalculateUnifiedDockLayout(
                    new Size[] { new Size(targetFacts.Width, targetFacts.Height),
                        new Size(sourceFacts.Width, sourceFacts.Height) },
                    targetFacts.X, targetFacts.Y, targetFacts.Width);
                StickyUiCommandResult targetDocked = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            hostedLayout[0].X, hostedLayout[0].Y,
                            hostedLayout[0].Width, hostedLayout[0].Height)),
                    petContext);
                StickyUiCommandResult sourceDocked = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(
                            hostedLayout[1].X, hostedLayout[1].Y,
                            hostedLayout[1].Width, hostedLayout[1].Height)),
                    petContext);

                // Focused regression: a programmatic SetBounds must be atomic.
                // It must not leak HeaderDragMoved, and its result must carry a
                // strictly newer sequence with the final geometry immediately
                // (mixed-width merge normalizes width without a self-heal wait).
                List<Rectangle> mixedWidthLayout =
                    StickyDockController.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(320, 300), new Size(420, 420)
                    }, 80, 140, 320);
                StickyUiCommandResult mixedRoot =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                mixedWidthLayout[0].X,
                                mixedWidthLayout[0].Y,
                                mixedWidthLayout[0].Width,
                                mixedWidthLayout[0].Height)),
                        petContext);
                long canonicalBaseline = sourceDocked == null
                    ? 0 : sourceDocked.Sequence;
                long secondBaseline = targetDocked == null
                    ? 0 : targetDocked.Sequence;
                setBoundsInFlight = true;
                StickyUiCommandResult mixedSource =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                mixedWidthLayout[1].X,
                                mixedWidthLayout[1].Y,
                                mixedWidthLayout[1].Width,
                                mixedWidthLayout[1].Height)),
                        petContext);
                setBoundsInFlight = false;
                bool staleCannotOverwrite =
                    !StickyWorkspace.ShouldApplyHostedSequence(
                        canonicalBaseline, mixedSource.Sequence) &&
                    StickyWorkspace.ShouldApplyHostedSequence(mixedSource.Sequence,
                        canonicalBaseline);
                check.HostedSetBoundsAtomicOk =
                    mixedRoot != null && mixedSource != null &&
                    mixedRoot.Status == StickyUiCommandStatus.Handled &&
                    mixedSource.Status == StickyUiCommandStatus.Handled &&
                    mixedRoot.Facts.PhysicalBounds.Width == 320 &&
                    mixedSource.Facts.PhysicalBounds.Width == 320 &&
                    mixedSource.Sequence > canonicalBaseline &&
                    mixedRoot.Sequence > secondBaseline &&
                    staleCannotOverwrite && !setBoundsLeakedHeaderDrag;
                DisplaySurfaceSnapshot moveSurface = FakeSurface(1, 0, true, 1080, 1040, FakeTarget("mdp:move"));
                DockPlacementPlan movePlan = DockPlacementPlanner.Plan(
                    new DockGroupLogicalState(new LogicalPoint { X = 160, Y = 140 }, new[] {
                        new DockLogicalMember(second.Id, 320, 300), new DockLogicalMember(canonical.Id, 320, 300) }),
                    new WindowFacts(second.Id, "mdp:move", moveSurface.RuntimeGdiName,
                        new PhysicalRect(160, 140, 320, 300), 96, 1, 1), moveSurface, 96, 1, 1);
                PhysicalRect targetBounds = movePlan.WindowTargets[0].PhysicalBounds;
                PhysicalRect sourceBounds = movePlan.WindowTargets[1].PhysicalBounds;
                StickyUiCommandResult targetMoved = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            targetBounds.Left, targetBounds.Top,
                            targetBounds.Width, targetBounds.Height)),
                    petContext);
                StickyUiCommandResult sourceMoved = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(
                            sourceBounds.Left, sourceBounds.Top,
                            sourceBounds.Width, sourceBounds.Height)),
                    petContext);
                check.HostedGroupMoveOk = targetMoved.Facts.PhysicalBounds.Left == 160 &&
                    targetMoved.Facts.PhysicalBounds.Top == 140 &&
                    sourceMoved.Facts.PhysicalBounds.Left == 160 &&
                    sourceMoved.Facts.PhysicalBounds.Top == 440;

                StickyUiCommandResult targetPinned = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetTopMost,
                        second.Id, true), petContext);
                StickyUiCommandResult sourcePinned = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetTopMost,
                        canonical.Id, true), petContext);
                check.HostedTopMostOk = targetPinned.Snapshot.AlwaysOnTop &&
                    sourcePinned.Snapshot.AlwaysOnTop;

                StickyUiDockResizeRole groupedRole =
                    new StickyUiDockResizeRole(true, true, true,
                        true, 220, 700);
                StickyUiDockResizeRole groupBottomRole =
                    new StickyUiDockResizeRole(true, false, true,
                        false, 220, 700);
                StickyUiCommandResult targetRole = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetDockResizeRole,
                        second.Id, false, null, null, groupedRole), petContext);
                StickyUiCommandResult sourceRole = PostStickyCommandAndWait(
                    host, new StickyUiCommand(
                        StickyUiCommandKind.SetDockResizeRole,
                        canonical.Id, false, null, null, groupBottomRole),
                    petContext);
                List<Rectangle> resizedLayout =
                    StickyDockController.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(320, 230), new Size(320, 230)
                    }, 80, 140, 420);
                StickyUiCommandResult targetResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                resizedLayout[0].X, resizedLayout[0].Y,
                                resizedLayout[0].Width,
                                resizedLayout[0].Height)), petContext);
                StickyUiCommandResult sourceResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                resizedLayout[1].X, resizedLayout[1].Y,
                                resizedLayout[1].Width,
                                resizedLayout[1].Height)), petContext);
                check.HostedHorizontalResizeOk =
                    targetRole.Status == StickyUiCommandStatus.Handled &&
                    sourceRole.Status == StickyUiCommandStatus.Handled &&
                    targetResized.Facts.PhysicalBounds.Left == 80 &&
                    sourceResized.Facts.PhysicalBounds.Left == 80 &&
                    targetResized.Facts.PhysicalBounds.Width == 420 &&
                    sourceResized.Facts.PhysicalBounds.Width == 420;

                DockWindowFacts twoUpperRequested = new DockWindowFacts(
                    second.Id, 80, 140, 420, 500, true, true);
                List<DockLayoutTarget> twoDividerTargets =
                    CalculateDividerTargetsForCheck(twoUpperRequested,
                        HostedDockFacts(sourceResized));
                StickyUiCommandResult targetDividerResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                twoDividerTargets[0].X,
                                twoDividerTargets[0].Y,
                                twoDividerTargets[0].Width,
                                twoDividerTargets[0].Height)), petContext);
                StickyUiCommandResult sourceDividerResized =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                twoDividerTargets[1].X,
                                twoDividerTargets[1].Y,
                                twoDividerTargets[1].Width,
                                twoDividerTargets[1].Height)), petContext);
                bool twoDividerOk =
                    targetDividerResized.Facts.PhysicalBounds.Height == 500 &&
                    sourceDividerResized.Facts.PhysicalBounds.Height == 230 &&
                    targetDividerResized.Facts.PhysicalBounds.Top +
                        targetDividerResized.Facts.PhysicalBounds.Height ==
                        sourceDividerResized.Facts.PhysicalBounds.Top;

                // Reopen geometry must be screen-independent: the divider
                // fixture can exceed a small CI virtual work area, so compact
                // bounds are applied right before hide and the reopen must
                // preserve exactly those bounds (hide -> reopen == no drift).
                List<Rectangle> compactLayout =
                    StickyDockController.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(420, 230), new Size(420, 230)
                    }, 80, 140, 420);
                PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            compactLayout[0].X, compactLayout[0].Y,
                            compactLayout[0].Width,
                            compactLayout[0].Height)), petContext);
                PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(
                            compactLayout[1].X, compactLayout[1].Y,
                            compactLayout[1].Width,
                            compactLayout[1].Height)), petContext);
                StickyUiCommandResult targetHidden = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Hide,
                        second.Id, false), petContext);
                StickyUiCommandResult sourceHidden = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Hide,
                        canonical.Id, false), petContext);
                StickyUiCommandResult targetShown = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Show,
                        second.Id, false), petContext);
                StickyUiCommandResult sourceShown = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.Show,
                        canonical.Id, false), petContext);
                check.HostedHideReopenOk =
                    !targetHidden.Snapshot.Visible &&
                    !sourceHidden.Snapshot.Visible &&
                    targetShown.Snapshot.Visible && sourceShown.Snapshot.Visible &&
                    targetShown.Facts.PhysicalBounds.Left == 80 &&
                    sourceShown.Facts.PhysicalBounds.Top == 370;

                StickyUiCommandResult thirdPositioned =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null,
                            new StickyUiBounds(80, 740, 420, 300)),
                        petContext);
                List<StickyNoteData> threeOrder =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        dockOrder, second,
                        new StickyNoteData[] { third });
                List<Rectangle> threeLayout =
                    StickyDockController.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(420, 300), new Size(420, 300),
                        new Size(420, 300)
                    }, 80, 140, 420);
                StickyUiCommandResult targetInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null, new StickyUiBounds(
                                threeLayout[0].X, threeLayout[0].Y,
                                threeLayout[0].Width, threeLayout[0].Height)),
                        petContext);
                StickyUiCommandResult thirdInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null, new StickyUiBounds(
                                threeLayout[1].X, threeLayout[1].Y,
                                threeLayout[1].Width, threeLayout[1].Height)),
                        petContext);
                StickyUiCommandResult sourceInserted =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                threeLayout[2].X, threeLayout[2].Y,
                                threeLayout[2].Width, threeLayout[2].Height)),
                        petContext);
                check.HostedThreeNoteInsertionOk = threeOrder.Count == 3 &&
                    threeOrder[0].Id == second.Id &&
                    threeOrder[1].Id == third.Id &&
                    threeOrder[2].Id == canonical.Id &&
                    StickyDockGroups.GetVisibleNeighbor(threeOrder, third, -1) == second &&
                    StickyDockGroups.GetVisibleNeighbor(threeOrder, canonical, -1) == third &&
                    thirdInserted.Facts.PhysicalBounds.Top == 440 &&
                    sourceInserted.Facts.PhysicalBounds.Top == 740;
                StickyUiCommandResult targetThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            second.Id, false, null, null, groupedRole),
                        petContext);
                StickyUiCommandResult thirdThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            third.Id, false, null, null, groupedRole),
                        petContext);
                StickyUiCommandResult sourceThreeRole =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(
                            StickyUiCommandKind.SetDockResizeRole,
                            canonical.Id, false, null, null,
                            groupBottomRole), petContext);
                List<DockLayoutTarget> firstDivider =
                    CalculateDividerTargetsForCheck(
                        new DockWindowFacts(second.Id, 80, 140, 420, 500,
                            true, true),
                        HostedDockFacts(thirdInserted));
                List<Rectangle> firstDividerLayout =
                    StickyDockController.CalculateUnifiedDockLayout(new Size[]
                    {
                        new Size(420, firstDivider[0].Height),
                        new Size(420, firstDivider[1].Height),
                        new Size(420, sourceInserted.Facts.PhysicalBounds.Height)
                    }, 80, 140, 420);
                StickyUiCommandResult firstUpper = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        second.Id, false, null, new StickyUiBounds(
                            firstDividerLayout[0].X, firstDividerLayout[0].Y,
                            420, firstDividerLayout[0].Height)), petContext);
                StickyUiCommandResult firstLower = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        third.Id, false, null, new StickyUiBounds(
                            firstDividerLayout[1].X, firstDividerLayout[1].Y,
                            420, firstDividerLayout[1].Height)), petContext);
                StickyUiCommandResult firstTrailing =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null, new StickyUiBounds(
                                firstDividerLayout[2].X,
                                firstDividerLayout[2].Y, 420,
                                firstDividerLayout[2].Height)), petContext);
                List<DockLayoutTarget> secondDivider =
                    CalculateDividerTargetsForCheck(
                        new DockWindowFacts(third.Id, 80,
                            firstLower.Facts.PhysicalBounds.Top, 420, 500,
                            true, true),
                        HostedDockFacts(firstTrailing));
                StickyUiCommandResult secondUpper = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        third.Id, false, null, new StickyUiBounds(80,
                            firstLower.Facts.PhysicalBounds.Top,
                            420, secondDivider[0].Height)), petContext);
                StickyUiCommandResult secondLower = PostStickyCommandAndWait(
                    host, new StickyUiCommand(StickyUiCommandKind.SetBounds,
                        canonical.Id, false, null, new StickyUiBounds(80,
                            secondDivider[1].Y,
                            420, secondDivider[1].Height)), petContext);
                List<DockLayoutTarget> dividerMinimum =
                    CalculateDividerTargetsForCheck(
                        new DockWindowFacts(second.Id, 80, 140, 420, 50,
                            true, true),
                        HostedDockFacts(firstLower));
                List<DockLayoutTarget> dividerMaximum =
                    CalculateDividerTargetsForCheck(
                        new DockWindowFacts(second.Id, 80, 140, 420, 900,
                            true, true),
                        HostedDockFacts(firstLower));
                check.HostedDividerResizeOk = twoDividerOk &&
                    targetThreeRole.Status == StickyUiCommandStatus.Handled &&
                    thirdThreeRole.Status == StickyUiCommandStatus.Handled &&
                    sourceThreeRole.Status == StickyUiCommandStatus.Handled &&
                    firstUpper.Facts.PhysicalBounds.Height == 500 &&
                    firstLower.Facts.PhysicalBounds.Height == 300 &&
                    firstUpper.Facts.PhysicalBounds.Top + firstUpper.Facts.PhysicalBounds.Height ==
                        firstLower.Facts.PhysicalBounds.Top &&
                    firstTrailing.Facts.PhysicalBounds.Height == 300 &&
                    firstLower.Facts.PhysicalBounds.Top + firstLower.Facts.PhysicalBounds.Height ==
                        firstTrailing.Facts.PhysicalBounds.Top &&
                    secondUpper.Facts.PhysicalBounds.Height == 500 &&
                    secondLower.Facts.PhysicalBounds.Height == 300 &&
                    secondUpper.Facts.PhysicalBounds.Top + secondUpper.Facts.PhysicalBounds.Height ==
                        secondLower.Facts.PhysicalBounds.Top &&
                    dividerMinimum[0].Height == 220 &&
                    dividerMinimum[1].Height == 300 &&
                    dividerMaximum[0].Height == 700 &&
                    dividerMaximum[1].Height == 300 &&
                    !eventKinds.Contains(
                        StickyUiEventKind.DockDividerResizing);
                check.DockRestoreOk = VerifyHostedDockPersistence(
                    firstUpper, secondUpper, secondLower);

                List<StickyNoteData> splitRemainder =
                    StickyDockOperations.ExtractSingleDockMember(
                        threeOrder, third);
                StickyUiCommandResult targetAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            second.Id, false, null,
                            new StickyUiBounds(80, 140, 420, 300)),
                        petContext);
                StickyUiCommandResult sourceAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            canonical.Id, false, null,
                            new StickyUiBounds(80, 440, 420, 300)),
                        petContext);
                StickyUiCommandResult thirdAfterSplit =
                    PostStickyCommandAndWait(host,
                        new StickyUiCommand(StickyUiCommandKind.SetBounds,
                            third.Id, false, null,
                            new StickyUiBounds(600, 140, 420, 300)),
                        petContext);
                check.HostedMiddleSplitOk = splitRemainder.Count == 2 &&
                    splitRemainder[0].Id == second.Id &&
                    splitRemainder[1].Id == canonical.Id &&
                    StickyDockGroups.GetVisibleNeighbor(splitRemainder, canonical, -1) == second &&
                    String.IsNullOrEmpty(third.DockGroupId) &&
                    sourceAfterSplit.Facts.PhysicalBounds.Top == 440 &&
                    thirdAfterSplit.Facts.PhysicalBounds.Left == 600;
                StickyUiCommandResult closed = PostStickyCommandAndWait(host,
                    new StickyUiCommand(StickyUiCommandKind.CloseAll,
                        String.Empty, false), petContext);
                host.BeginShutdown();
                bool exited = host.WaitForExit(5000);
                bool closedBoth = closed != null &&
                    closed.Status == StickyUiCommandStatus.Handled &&
                    closed.FinalSnapshots != null &&
                    closed.FinalSnapshots.Length == 6;
                StickyUiFinalSnapshot finalSource = null;
                StickyUiFinalSnapshot finalTarget = null;
                if (closedBoth)
                    foreach (StickyUiFinalSnapshot item in closed.FinalSnapshots)
                    {
                        if (String.Equals(item.NoteId, canonical.Id,
                            StringComparison.OrdinalIgnoreCase))
                            finalSource = item;
                        if (String.Equals(item.NoteId, second.Id,
                            StringComparison.OrdinalIgnoreCase))
                            finalTarget = item;
                    }
                check.CloseAllBatchOk = finalSource != null &&
                    finalTarget != null && sourceAfterSplit != null &&
                    targetAfterSplit != null &&
                    finalSource.Sequence > sourceAfterSplit.Sequence &&
                    finalTarget.Sequence > targetAfterSplit.Sequence &&
                    finalSource.Facts.PhysicalBounds.Top == 440 &&
                    finalTarget.Facts.PhysicalBounds.Top == 140;
                WindowFacts staleProbe = sourceDocked.Facts;
                long appliedSequence = sourceDocked.Sequence;
                if (StickyWorkspace.ShouldApplyHostedSequence(reopened.Sequence,
                    appliedSequence)) staleProbe = reopened.Facts;
                check.PerNoteSequenceOk =
                    StickyWorkspace.ShouldApplyHostedSequence(sourceDocked.Sequence,
                        reopened.Sequence) &&
                    !StickyWorkspace.ShouldApplyHostedSequence(reopened.Sequence,
                        appliedSequence) &&
                    Object.ReferenceEquals(staleProbe, sourceDocked.Facts);
                check.HostedDockEffectOk = dockHit && dockOrder.Count == 2 &&
                    dockOrder[0].Id == second.Id &&
                    StickyDockGroups.GetVisibleNeighbor(dockOrder, dockOrder[1], -1) == second &&
                    targetDocked != null && sourceDocked != null &&
                    targetDocked.Status == StickyUiCommandStatus.Handled &&
                    sourceDocked.Status == StickyUiCommandStatus.Handled &&
                    targetDocked.Facts.PhysicalBounds.Left == hostedLayout[0].X &&
                    sourceDocked.Facts.PhysicalBounds.Top == hostedLayout[1].Y;
                check.LifecycleOk = detachedOwnership && hidden != null &&
                    hidden.Status == StickyUiCommandStatus.Handled &&
                    hidden.Snapshot != null && !hidden.Snapshot.Visible &&
                    shown != null &&
                    shown.Status == StickyUiCommandStatus.Handled &&
                    shown.Snapshot != null && shown.Snapshot.Visible &&
                    closedOne != null &&
                    closedOne.Status == StickyUiCommandStatus.Handled &&
                    reopened != null &&
                    reopened.Status == StickyUiCommandStatus.Handled &&
                    secondCreated != null &&
                    secondCreated.Status == StickyUiCommandStatus.Handled &&
                    secondCreated.OwnerThreadId == created.OwnerThreadId &&
                    thirdCreated != null &&
                    thirdCreated.Status == StickyUiCommandStatus.Handled &&
                    thirdPositioned != null &&
                    thirdPositioned.Status == StickyUiCommandStatus.Handled &&
                    todoCreated != null &&
                    todoCreated.Status == StickyUiCommandStatus.Handled &&
                    scheduleCreated != null &&
                    scheduleCreated.Status == StickyUiCommandStatus.Handled &&
                    reminderCreated != null &&
                    reminderCreated.Status == StickyUiCommandStatus.Handled &&
                    closedBoth && exited &&
                    eventThread == petThread && lastEvent != null &&
                    eventNoteIds.Contains(canonical.Id) &&
                    eventNoteIds.Contains(second.Id) &&
                    eventNoteIds.Contains(third.Id) &&
                    eventNoteIds.Contains(todo.Id) &&
                    eventNoteIds.Contains(schedule.Id) &&
                    eventNoteIds.Contains(reminder.Id) &&
                    eventKinds.Contains(StickyUiEventKind.FirstRendered) &&
                    eventKinds.Contains(StickyUiEventKind.SnapshotChanged) &&
                    eventKinds.Contains(StickyUiEventKind.Closed);
            }
            return check;
        }

        private static DockWindowFacts HostedDockFacts(StickyUiCommandResult result)
        {
            return result == null || result.Snapshot == null ? null :
                DockWindowFacts.FromWindowFacts(result.Facts,
                    result.Snapshot.Visible, result.Snapshot.AlwaysOnTop);
        }

        private static bool VerifyHostedDockPersistence(
            params StickyUiCommandResult[] results)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "penny-hosted-dock-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                StickyFeature repository =
                    StickyFeature.LoadFromFile(path);
                List<StickyNoteData> stored = new List<StickyNoteData>();
                if (results == null || results.Length < 2) return false;
                foreach (StickyUiCommandResult result in results)
                {
                    if (result == null || result.Snapshot == null || result.Facts == null)
                        return false;
                    StickyNoteUiSnapshot snapshot = result.Snapshot;
                    PhysicalRect actual = result.Facts.PhysicalBounds;
                    StickyNoteData note = repository.Create(String.Empty,
                        new Point(actual.Left, actual.Top));
                    if (note == null) return false;
                    snapshot.ApplyContentTo(note);
                    note.Id = snapshot.NoteId;
                    note.X = actual.Left;
                    note.Y = actual.Top;
                    note.Width = actual.Width;
                    note.Height = actual.Height;
                    stored.Add(note);
                }
                StickyDockGroups.ApplyOrderedGroup(stored);
                if (!repository.Save().Succeeded) return false;
                StickyFeature reopened =
                    StickyFeature.LoadFromFile(path);
                StickyNoteData member = reopened.Find(
                    stored[stored.Count - 1].Id);
                List<StickyNoteData> order = StickyDockGroups.GetOrderedGroup(
                    reopened.GetAll(), member);
                if (order.Count != results.Length) return false;
                for (int index = 0; index < order.Count; index++)
                {
                    if (order[index].Id != results[index].Snapshot.NoteId ||
                        order[index].X != results[index].Facts.PhysicalBounds.Left ||
                        order[index].Y != results[index].Facts.PhysicalBounds.Top ||
                        order[index].Width != results[index].Facts.PhysicalBounds.Width ||
                        order[index].Height != results[index].Facts.PhysicalBounds.Height ||
                        (index > 0 && StickyDockGroups.GetVisibleNeighbor(order, order[index], -1) !=
                            order[index - 1])) return false;
                }
                return true;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }
    }
}
