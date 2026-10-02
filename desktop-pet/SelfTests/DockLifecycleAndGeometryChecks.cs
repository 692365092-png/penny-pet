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

        private sealed class DockLifecycleCheckResult
        {
            internal bool ScheduleMixedTypesOk;
            internal bool GroupRestoreAtomicOk;
            internal bool MiddleExtractionOk;
            internal bool HiddenSlotPreservedOk;
            internal bool HiddenSlotReopenOk;
            internal bool PartialHiddenMergeOk;
            internal bool SecondRestoreCycleOk;
            internal bool RepeatedRestoreCyclesOk;
            internal bool CloseHierarchyOk;
            internal bool SplitGestureOk;
            internal bool RootDragNeverSplitsOk;
        }

        private static DockLifecycleCheckResult RunDockLifecycleChecks()
        {
            DockLifecycleCheckResult result = new DockLifecycleCheckResult();
            StickyNoteData mixedOrdinary = new StickyNoteData();
            StickyNoteData mixedTodo = new StickyNoteData();
            mixedTodo.IsTodoList = true;
            StickyNoteData mixedSchedule = new StickyNoteData();
            mixedSchedule.IsSchedule = true;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                mixedOrdinary, mixedTodo, mixedSchedule });
            List<StickyNoteData> mixedScheduleOrder = StickyDockGroups
                .GetOrderedGroup(new StickyNoteData[] { mixedSchedule,
                    mixedOrdinary, mixedTodo }, mixedSchedule);
            result.ScheduleMixedTypesOk = mixedScheduleOrder.Count == 3 &&
                Object.ReferenceEquals(mixedScheduleOrder[0], mixedOrdinary) &&
                Object.ReferenceEquals(mixedScheduleOrder[1], mixedTodo) &&
                Object.ReferenceEquals(mixedScheduleOrder[2], mixedSchedule) &&
                mixedTodo.IsTodoList && !mixedTodo.IsSchedule &&
                mixedSchedule.IsSchedule && !mixedSchedule.IsTodoList;
            result.GroupRestoreAtomicOk =
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    3, false) &&
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    3, true) &&
                !StickyDockOperations.ShouldRestoreWholeDockComponent(
                    1, true);

            StickyNoteData extractA = new StickyNoteData();
            StickyNoteData extractB = new StickyNoteData();
            StickyNoteData extractC = new StickyNoteData();
            StickyNoteData extractD = new StickyNoteData();
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                extractA, extractB, extractC, extractD });
            List<StickyNoteData> afterMiddleExtraction = StickyDockOperations
                .ExtractSingleDockMember(new StickyNoteData[] { extractA,
                    extractB, extractC, extractD }, extractB);
            result.MiddleExtractionOk = afterMiddleExtraction.Count == 3 &&
                Object.ReferenceEquals(afterMiddleExtraction[0], extractA) &&
                Object.ReferenceEquals(afterMiddleExtraction[1], extractC) &&
                Object.ReferenceEquals(afterMiddleExtraction[2], extractD) &&
                StickyDockGroups.GetVisibleNeighbor(afterMiddleExtraction, extractC, -1) == extractA &&
                StickyDockGroups.GetVisibleNeighbor(afterMiddleExtraction, extractD, -1) == extractC &&
                String.IsNullOrEmpty(extractB.DockGroupId) &&
                extractB.DockGroupOrder == -1;

            StickyNoteData hideA = new StickyNoteData();
            StickyNoteData hideB = new StickyNoteData();
            StickyNoteData hideC = new StickyNoteData();
            StickyNoteData hideD = new StickyNoteData();
            List<StickyNoteData> hideSnapshot =
                new List<StickyNoteData>(new StickyNoteData[] {
                    hideA, hideB, hideC, hideD });
            StickyDockGroups.ApplyOrderedGroup(hideSnapshot);
            string hiddenGroupId = hideB.DockGroupId;
            hideB.Visible = false;
            result.HiddenSlotPreservedOk = hideB.DockGroupId == hiddenGroupId &&
                hideB.DockGroupOrder == 1 &&
                StickyDockGroups.GetVisibleNeighbor(hideSnapshot, hideC, -1) == hideA &&
                StickyDockGroups.GetVisibleNeighbor(hideSnapshot, hideD, -1) == hideC;
            List<StickyNoteData> hiddenMemberOpenOrder =
                StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                    hideD, hideB, hideA, hideC }, hideB);
            foreach (StickyNoteData member in hiddenMemberOpenOrder)
                member.Visible = true;
            StickyDockGroups.ApplyOrderedGroup(hiddenMemberOpenOrder);
            result.HiddenSlotReopenOk = hiddenMemberOpenOrder.Count == 4 &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[0], hideA) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[1], hideB) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[2], hideC) &&
                Object.ReferenceEquals(hiddenMemberOpenOrder[3], hideD) &&
                StickyDockGroups.GetVisibleNeighbor(hideSnapshot, hideB, -1) == hideA &&
                StickyDockGroups.GetVisibleNeighbor(hideSnapshot, hideC, -1) == hideB &&
                StickyDockGroups.GetVisibleNeighbor(hideSnapshot, hideD, -1) == hideC;

            StickyNoteData mergeA = new StickyNoteData();
            StickyNoteData mergeB = new StickyNoteData();
            StickyNoteData mergeC = new StickyNoteData();
            StickyNoteData mergeD = new StickyNoteData();
            StickyNoteData mergeE = new StickyNoteData();
            List<StickyNoteData> mergeTarget =
                new List<StickyNoteData>(new StickyNoteData[] {
                    mergeA, mergeB, mergeC });
            List<StickyNoteData> mergeSource =
                new List<StickyNoteData>(new StickyNoteData[] {
                    mergeD, mergeE });
            StickyDockGroups.ApplyOrderedGroup(mergeTarget);
            StickyDockGroups.ApplyOrderedGroup(mergeSource);
            mergeB.Visible = false;
            mergeE.Visible = false;
            StickyDockGroups.ApplyOrderedGroup(mergeTarget);
            StickyDockGroups.ApplyOrderedGroup(mergeSource);
            List<StickyNoteData> mergedPartialSnapshots = StickyDockOperations
                .MergeDockSnapshotsAfterParent(mergeTarget, mergeA,
                    mergeSource);
            result.PartialHiddenMergeOk = mergedPartialSnapshots.Count == 5 &&
                Object.ReferenceEquals(mergedPartialSnapshots[0], mergeA) &&
                Object.ReferenceEquals(mergedPartialSnapshots[1], mergeD) &&
                Object.ReferenceEquals(mergedPartialSnapshots[2], mergeE) &&
                Object.ReferenceEquals(mergedPartialSnapshots[3], mergeB) &&
                Object.ReferenceEquals(mergedPartialSnapshots[4], mergeC) &&
                StickyDockGroups.GetVisibleNeighbor(mergedPartialSnapshots, mergeD, -1) == mergeA &&
                StickyDockGroups.GetVisibleNeighbor(mergedPartialSnapshots, mergeC, -1) == mergeD;

            StickyDockOperations.MergeDockSnapshotsAfterParent(
                afterMiddleExtraction, extractA, new[] { extractB });
            List<StickyNoteData> closeSecondCycleOrder = StickyDockGroups.GetVisibleGroup(
                new[] { extractD, extractC, extractA, extractB }, extractD);
            result.SecondRestoreCycleOk = closeSecondCycleOrder.Count == 4 &&
                Object.ReferenceEquals(closeSecondCycleOrder[0], extractA) &&
                Object.ReferenceEquals(closeSecondCycleOrder[1], extractB) &&
                Object.ReferenceEquals(closeSecondCycleOrder[2], extractC) &&
                Object.ReferenceEquals(closeSecondCycleOrder[3], extractD) &&
                StickyDockGroups.GetVisibleNeighbor(closeSecondCycleOrder, extractB, -1) == extractA &&
                StickyDockGroups.GetVisibleNeighbor(closeSecondCycleOrder, extractC, -1) == extractB &&
                StickyDockGroups.GetVisibleNeighbor(closeSecondCycleOrder, extractD, -1) == extractC;

            List<StickyNoteData> repeatedMembers =
                new List<StickyNoteData>();
            for (int index = 0; index < 6; index++)
                repeatedMembers.Add(new StickyNoteData());
            StickyDockGroups.ApplyOrderedGroup(repeatedMembers);
            result.RepeatedRestoreCyclesOk = true;
            for (int cycle = 0; cycle < 18; cycle++)
            {
                List<StickyNoteData> current = StickyDockGroups
                    .GetOrderedGroup(repeatedMembers, repeatedMembers[0]);
                if (current.Count != repeatedMembers.Count)
                {
                    result.RepeatedRestoreCyclesOk = false;
                    break;
                }
                int extractIndex = 1 + cycle % (current.Count - 1);
                StickyNoteData moved = current[extractIndex];
                List<StickyNoteData> remainder = StickyDockOperations
                    .ExtractSingleDockMember(current, moved);
                int targetIndex = cycle % remainder.Count;
                StickyNoteData cycleParent = remainder[targetIndex];
                List<StickyNoteData> committedCycle = StickyDockOperations
                    .MergeDockSnapshotsAfterParent(remainder, cycleParent, new[] { moved });
                List<StickyNoteData> randomOpenOrder = StickyDockGroups
                    .GetOrderedGroup(new StickyNoteData[] {
                        repeatedMembers[5], repeatedMembers[2],
                        repeatedMembers[0], repeatedMembers[4],
                        repeatedMembers[1], repeatedMembers[3] }, moved);
                if (committedCycle.Count != repeatedMembers.Count ||
                    randomOpenOrder.Count != repeatedMembers.Count)
                {
                    result.RepeatedRestoreCyclesOk = false;
                    break;
                }
                for (int position = 0; position < randomOpenOrder.Count;
                    position++)
                {
                    StickyNoteData member = randomOpenOrder[position];
                    StickyNoteData expectedParent = position == 0 ? null : randomOpenOrder[position - 1];
                    if (member.DockGroupOrder != position ||
                        StickyDockGroups.GetVisibleNeighbor(randomOpenOrder, member, -1) != expectedParent)
                    {
                        result.RepeatedRestoreCyclesOk = false;
                        break;
                    }
                }
                if (!result.RepeatedRestoreCyclesOk) break;
            }
            result.CloseHierarchyOk =
                StickyDockOperations.ShouldCollapseWholeDockGroup(0, 3) &&
                !StickyDockOperations.ShouldCollapseWholeDockGroup(1, 3) &&
                !StickyDockOperations.ShouldCollapseWholeDockGroup(0, 1);
            result.SplitGestureOk =
                StickyDockOperations.CancelsDockSplitHold(120, 20, 0) &&
                !StickyDockOperations.CancelsDockSplitHold(600, 20, 0) &&
                !StickyDockOperations.CancelsDockSplitHold(120, 3, 3);
            result.RootDragNeverSplitsOk =
                !StickyDockOperations.IsDockSplitEligible(String.Empty, 3) &&
                StickyDockOperations.IsDockSplitEligible("parent-note", 3) &&
                !StickyDockOperations.IsDockSplitEligible("parent-note", 1);
            return result;
        }

        private sealed class DockGeometryCheckResult
        {
            internal bool BottomDockingOk;
            internal bool UnifiedGroupResizeOk;
            internal bool RootAnchorPreservedOk;
            internal bool DividerMovesFollowingChainOk;
            internal bool DividerIndependentRangeOk;
            internal bool DividerPreservesDownstreamHeightsOk;
            internal bool DividerLiveSessionTargetsOk;
            internal bool WideNarrowDockingOk;
            internal bool LongCoordinateGuardOk;
            internal bool FirstDragRecoveryOk;
            internal bool DetachedGroupReturnsOnScreenOk;
            internal bool ScreenRecoveryAnchorOk;
            internal bool DetachedGroupTranslationOk;
            internal bool ExecutorNeutralDockVisualSeamOk;
        }

        private static DockGeometryCheckResult RunDockGeometryChecks()
        {
            DockGeometryCheckResult result = new DockGeometryCheckResult();
            result.BottomDockingOk =
                CanDockBelowForCheck(new Rectangle(100, 330, 320, 300),
                    new Rectangle(100, 30, 320, 300), 20) &&
                !CanDockBelowForCheck(new Rectangle(170, 330, 320, 300),
                    new Rectangle(100, 30, 320, 300), 20);
            List<Rectangle> unifiedLayout = StickyDockController.CalculateUnifiedDockLayout(
                new Size[] { new Size(320, 300), new Size(500, 240),
                    new Size(380, 260) }, 120, 80, 460);
            result.UnifiedGroupResizeOk = unifiedLayout.Count == 3 &&
                unifiedLayout[0] == new Rectangle(120, 80, 460, 300) &&
                unifiedLayout[1] == new Rectangle(120, 380, 460, 240) &&
                unifiedLayout[2] == new Rectangle(120, 620, 460, 260);
            result.RootAnchorPreservedOk = unifiedLayout.Count == 3 &&
                unifiedLayout[0].Location == new Point(120, 80);
            List<DockLayoutTarget> dividerTargets =
                CalculateDividerTargetsForCheck(
                    new DockWindowFacts("upper", 120, 80, 420, 500,
                        true, true),
                    new DockWindowFacts("lower", 120, 310, 420, 230,
                        true, true));
            result.DividerMovesFollowingChainOk =
                dividerTargets.Count == 2 &&
                dividerTargets[0].Height == 500 &&
                dividerTargets[1].Y == 580 &&
                dividerTargets[1].Height == 230 &&
                dividerTargets[1].X == 120 &&
                dividerTargets[1].Width == 420 &&
                dividerTargets[1].TopMost;
            result.DividerIndependentRangeOk =
                StickyDockController.CalculateDockDividerHeight(50) == 220 &&
                StickyDockController.CalculateDockDividerHeight(500) == 500 &&
                StickyDockController.CalculateDockDividerHeight(900) == 700;
            List<Rectangle> beforeDividerLayout =
                StickyDockController.CalculateUnifiedDockLayout(
                    new Size[] { new Size(420, 300),
                        new Size(420, 300), new Size(420, 300) },
                    120, 80, 420);
            List<Rectangle> afterDividerLayout =
                StickyDockController.CalculateUnifiedDockLayout(
                    new Size[] { new Size(420, 350),
                        new Size(420, 300), new Size(420, 300) },
                    120, 80, 420);
            result.DividerPreservesDownstreamHeightsOk =
                beforeDividerLayout.Count == 3 &&
                afterDividerLayout.Count == 3 &&
                afterDividerLayout[1].Height == 300 &&
                afterDividerLayout[2].Height == 300 &&
                afterDividerLayout[1].Top ==
                    beforeDividerLayout[1].Top + 50 &&
                afterDividerLayout[2].Top ==
                    beforeDividerLayout[2].Top + 50;
            result.DividerLiveSessionTargetsOk = RunLocalDividerCycleCheck();
            result.WideNarrowDockingOk = CanDockBelowForCheck(
                new Rectangle(80, 400, 900, 300),
                new Rectangle(400, 100, 280, 300), 20) &&
                CanDockBelowForCheck(new Rectangle(400, 400, 280, 300),
                    new Rectangle(80, 100, 900, 300), 20);
            result.LongCoordinateGuardOk =
                StickyDockOperations.IsDockCoordinateRangeSafe(100,
                    new int[] { 700, 700, 700 }, 30000) &&
                !StickyDockOperations.IsDockCoordinateRangeSafe(29000,
                    new int[] { 700, 700 }, 30000);
            DisplaySurfaceSnapshot dragSurface = FakeSurface(1, 0, true, 1080, 1040, FakeTarget("mdp:drag"));
            DockPlacementPlan translated = DockPlacementPlanner.Plan(
                new DockGroupLogicalState(new LogicalPoint { X = 115, Y = 190 }, new[] {
                    new DockLogicalMember("root", 320, 300), new DockLogicalMember("child", 320, 240) }),
                new WindowFacts("root", "mdp:drag", dragSurface.RuntimeGdiName,
                    new PhysicalRect(115, 190, 320, 300), 96, 1, 1), dragSurface, 96, 1, 1);
            result.DetachedGroupTranslationOk = translated.WindowTargets.Count == 2 &&
                translated.WindowTargets[0].NoteId == "root" &&
                translated.WindowTargets[0].PhysicalBounds.Left == 115 && translated.WindowTargets[0].PhysicalBounds.Top == 190 &&
                translated.WindowTargets[1].NoteId == "child" &&
                translated.WindowTargets[1].PhysicalBounds.Left == 115 && translated.WindowTargets[1].PhysicalBounds.Top == 490;
            Rectangle recoveredDrag = StickyNoteWindow
                .CalculateRecoveredHeaderDragBounds(
                    new Rectangle(100, 100, 320, 300),
                    new Rectangle(0, 0, 1920, 1080),
                    new Point(500, 400), new Point(20, 10), true);
            result.FirstDragRecoveryOk = recoveredDrag ==
                new Rectangle(480, 390, 320, 300);
            Point reachable = StickyDockController.CalculateHeaderReachableTranslation(
                new Rectangle(100, -200, 400, 32),
                new Rectangle(0, 0, 1200, 900));
            result.DetachedGroupReturnsOnScreenOk = reachable.Y == 200;
            Point recoveredPrimary = StickyWorkspace.CalculateStickyRecoveryAnchor(
                new Rectangle(0, 0, 1920, 1040),
                new Rectangle(20, 700, 192, 208),
                new Size(320, 300), 0);
            Point recoveredSecondary = StickyWorkspace.CalculateStickyRecoveryAnchor(
                new Rectangle(-1920, 0, 1920, 1040),
                new Rectangle(-300, 700, 192, 208),
                new Size(320, 300), 1);
            result.ScreenRecoveryAnchorOk =
                recoveredPrimary.X >= 0 && recoveredPrimary.Y >= 0 &&
                recoveredPrimary.Y <= 1008 &&
                recoveredSecondary.X >= -1920 &&
                recoveredSecondary.X <= -320 &&
                recoveredSecondary.Y >= 0 &&
                recoveredSecondary.Y <= 1008;
            result.ExecutorNeutralDockVisualSeamOk =
                (Rectangle)typeof(StickyUiHost).GetMethod("LocalDockSeam",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                    new object[] { new PhysicalRect(-860, 140, 420, 310) }) ==
                    new Rectangle(-860, 447, 420, 6);
            return result;
        }
    }
}
