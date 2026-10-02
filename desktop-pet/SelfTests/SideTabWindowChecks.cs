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

        private sealed class StickySideTabCheckResult
        {
            internal bool OverflowOk;
            internal bool DragPreviewOk;
            internal bool DeferredDropCommitOk;
            internal bool PreviewClearsBothSidesOk;
            internal bool ExplicitSourceKeepsTargetFirstOk;
            internal bool TargetNeverMarkedAsSourceOk;
            internal bool ExclusiveCanvasStateOk;
            internal bool ReverseBoundaryRolloverOk;
            internal bool BoundaryEdgeDropOk;
            internal bool ScaledGapOk;
            internal bool VectorIconColorOk;
            internal bool DeleteCommandOk;
            internal bool ZOrderPolicyOk;
            internal bool LayoutInvalidationOk;
        }

        private static StickySideTabCheckResult RunStickySideTabChecks(
            StickyNoteData restoredNote)
        {
            StickySideTabCheckResult result = new StickySideTabCheckResult();
            Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(9);
            result.OverflowOk = leftCount == 5 &&
                9 - leftCount == 4 &&
                StickyNoteTabsForm.ScreenCapacity(workArea) >= 9 - leftCount;
            result.DragPreviewOk =
                StickyNoteTabsForm.PreviewInsertionGap >= 10 &&
                StickyNoteTabsForm.DragSourceVisualOffset >= 6 &&
                StickyNoteTabsForm.DragSourceVisualOffset <= 12 &&
                StickyNoteTabsForm.PetGap == -20 &&
                !String.IsNullOrEmpty(StickyNoteTabsForm.DragDataFormat) &&
                StickyNoteTabsForm.CalculateDropIndex(0, 4) == 0 &&
                StickyNoteTabsForm.CalculateDropIndex(95, 4) == 3 &&
                StickyNoteTabsForm.PreviewTargetTop(0, -1, 1) == 0 &&
                StickyNoteTabsForm.PreviewTargetTop(1, -1, 1) ==
                    StickyNoteTabsForm.TabHeight +
                    StickyNoteTabsForm.TabGap +
                    StickyNoteTabsForm.PreviewInsertionGap &&
                StickyNoteTabsForm.PreviewTargetTop(0, 2, 0) ==
                    StickyNoteTabsForm.PreviewInsertionGap;
            StickyTabDropSession dropSession = new StickyTabDropSession();
            string dropNoteId = "drop-note";
            object dropSource = new object();
            int dropCommits = 0;
            dropSession.Begin(dropNoteId, dropSource);
            bool dropQueued = dropSession.QueueCommit(dropNoteId,
                delegate { dropCommits++; });
            result.DeferredDropCommitOk = dropQueued && dropCommits == 0 &&
                dropSession.IsSource(dropSource) &&
                dropSession.Complete(dropNoteId) && dropCommits == 1 &&
                String.IsNullOrEmpty(dropSession.ActiveNoteId) &&
                !dropSession.Complete(dropNoteId);
            StickyNoteData previewNote = new StickyNoteData();
            StickyNoteData boundaryNote = new StickyNoteData();
            StickyNoteData targetNote = new StickyNoteData();
            SideTabSnapshot previewItem = SideTabSnapshot.FromData(previewNote);
            SideTabSnapshot boundaryItem = SideTabSnapshot.FromData(boundaryNote);
            SideTabSnapshot targetItem = SideTabSnapshot.FromData(targetNote);
            using (StickyNoteTabsForm left = new StickyNoteTabsForm(
                StickyTabSide.Left, delegate(string noteId) { }))
            using (StickyNoteTabsForm right = new StickyNoteTabsForm(
                StickyTabSide.Right, delegate(string noteId) { }))
            {
                left.SetNotes(new List<SideTabSnapshot> { previewItem }, 0);
                right.SetNotes(new List<SideTabSnapshot>
                    { boundaryItem, targetItem }, 1);
                left.Hide();
                right.Hide();
                StickyNoteTabsForm.BeginDragSession(previewNote.Id, left);
                left.ShowDropPreviewForTest(previewNote.Id, 0);
                bool leftWasTarget = left.HasDropPreviewForTest;
                right.ShowDropPreviewForTest(previewNote.Id, 2);
                result.ExplicitSourceKeepsTargetFirstOk =
                    right.TabTopForTest(targetNote.Id) == 0 &&
                    !right.TabVisibleForTest(boundaryNote.Id) &&
                    left.HasBoundaryRolloverForTest(boundaryNote.Id, false);
                result.TargetNeverMarkedAsSourceOk =
                    !right.HasDragSourceVisualForTest(previewNote.Id);
                result.ExclusiveCanvasStateOk =
                    left.HasStableDragCanvasForTest &&
                    right.HasStableDragCanvasForTest;
                bool targetIsExclusive = leftWasTarget &&
                    !left.HasDropPreviewForTest &&
                    right.HasDropPreviewForTest &&
                    result.ExplicitSourceKeepsTargetFirstOk &&
                    result.TargetNeverMarkedAsSourceOk &&
                    result.ExclusiveCanvasStateOk &&
                    left.HasDragSourceVisualForTest(previewNote.Id);
                StickyNoteTabsForm.EndDragSession(previewNote.Id);
                result.PreviewClearsBothSidesOk = targetIsExclusive &&
                    !left.HasDropPreviewForTest &&
                    !right.HasDropPreviewForTest &&
                    left.HasStableDragCanvasForTest &&
                    right.HasStableDragCanvasForTest &&
                    right.TabVisibleForTest(boundaryNote.Id) &&
                    !left.HasBoundaryRolloverForTest(boundaryNote.Id, false) &&
                    !left.HasDragSourceVisualForTest(previewNote.Id);
                StickyNoteTabsForm.BeginDragSession(previewNote.Id, left);
                right.ShowDropPreviewForTest(previewNote.Id, 0);
                result.BoundaryEdgeDropOk =
                    right.TabVisibleForTest(boundaryNote.Id) &&
                    !left.HasBoundaryRolloverForTest(boundaryNote.Id, false) &&
                    left.HasDragSourceVisualForTest(previewNote.Id);
                StickyNoteTabsForm.EndDragSession(previewNote.Id);
            }
            StickyNoteData reverseTop = new StickyNoteData();
            StickyNoteData reverseBoundary = new StickyNoteData();
            StickyNoteData reverseSource = new StickyNoteData();
            StickyNoteData reverseTail = new StickyNoteData();
            SideTabSnapshot reverseTopItem = SideTabSnapshot.FromData(reverseTop);
            SideTabSnapshot reverseBoundaryItem = SideTabSnapshot.FromData(reverseBoundary);
            SideTabSnapshot reverseSourceItem = SideTabSnapshot.FromData(reverseSource);
            SideTabSnapshot reverseTailItem = SideTabSnapshot.FromData(reverseTail);
            using (StickyNoteTabsForm left = new StickyNoteTabsForm(
                StickyTabSide.Left, delegate(string noteId) { }))
            using (StickyNoteTabsForm right = new StickyNoteTabsForm(
                StickyTabSide.Right, delegate(string noteId) { }))
            {
                left.SetNotes(new List<SideTabSnapshot>
                    { reverseTopItem, reverseBoundaryItem }, 0);
                right.SetNotes(new List<SideTabSnapshot>
                    { reverseSourceItem, reverseTailItem }, 2);
                left.Hide();
                right.Hide();
                StickyNoteTabsForm.BeginDragSession(reverseSource.Id, right);
                left.ShowDropPreviewForTest(reverseSource.Id, 1);
                result.ReverseBoundaryRolloverOk =
                    left.TabTopForTest(reverseTop.Id) == 0 &&
                    !left.TabVisibleForTest(reverseBoundary.Id) &&
                    right.HasBoundaryRolloverForTest(reverseBoundary.Id, true) &&
                    right.HasStableDragCanvasForTest;
                StickyNoteTabsForm.EndDragSession(reverseSource.Id);
                result.ReverseBoundaryRolloverOk =
                    result.ReverseBoundaryRolloverOk &&
                    left.TabVisibleForTest(reverseBoundary.Id) &&
                    !right.HasBoundaryRolloverForTest(reverseBoundary.Id, true);
                StickyNoteTabsForm.BeginDragSession(reverseSource.Id, right);
                left.ShowDropPreviewForTest(reverseSource.Id, 2);
                result.BoundaryEdgeDropOk = result.BoundaryEdgeDropOk &&
                    left.TabVisibleForTest(reverseBoundary.Id) &&
                    !right.HasBoundaryRolloverForTest(reverseBoundary.Id, true) &&
                    right.HasDragSourceVisualForTest(reverseSource.Id);
                StickyNoteTabsForm.EndDragSession(reverseSource.Id);
            }
            int fullOverlap = StickyNoteTabsForm.PetOverlapForWidth(192);
            int doubleOverlap = StickyNoteTabsForm.PetOverlapForWidth(384);
            result.ScaledGapOk =
                Math.Abs((44 - fullOverlap) - (44 - 20) / 2.0) < 1.0 &&
                Math.Abs((88 - doubleOverlap) - (88 - 20) / 2.0) < 1.0 &&
                doubleOverlap > fullOverlap;
            Color paper = Color.FromArgb(255, 118, 169, 242);
            Color ink = StickyNoteTabControl.TypeIconColor(paper);
            result.VectorIconColorOk = ink.ToArgb() != Color.Black.ToArgb() &&
                ink.GetBrightness() < paper.GetBrightness();
            using (StickyNoteTabControl tab = new StickyNoteTabControl(
                SideTabSnapshot.FromData(restoredNote), StickyTabSide.Left,
                delegate(string noteId) { },
                delegate(string noteId) { }, SideTabPhysicalMetrics.ForDpi(96)))
                result.DeleteCommandOk = tab.HasDeleteCommand;
            result.ZOrderPolicyOk =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(false) &&
                !StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true);
            const int layoutNoteCount = 14;
            int balancedLeft = StickyNoteTabsForm.CalculateLeftCount(
                layoutNoteCount);
            result.LayoutInvalidationOk =
                StickyNoteTabsForm.IsLayoutSplitCurrent(balancedLeft,
                    layoutNoteCount - balancedLeft) &&
                !StickyNoteTabsForm.IsLayoutSplitCurrent(balancedLeft + 1,
                    layoutNoteCount - balancedLeft - 1) &&
                typeof(StickyNoteTabsForm).GetMethod(
                    "CalculateLeftCount",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null) != null &&
                typeof(StickyNoteTabsForm).GetMethod(
                    "PreferredLeftCapacity") == null;
            result.LayoutInvalidationOk &= RunSideTabPhysicalProjectionChecks();
            return result;
        }

        private static bool RunSideTabPhysicalProjectionChecks()
        {
            List<SideTabSnapshot> notes = new List<SideTabSnapshot>();
            for (int index = 0; index < 3; index++)
                notes.Add(SideTabSnapshot.FromData(new StickyNoteData()));
            using (StickyNoteTabsForm tabs = new StickyNoteTabsForm(
                StickyTabSide.Left, delegate(string id) { }))
            {
                tabs.Location = new Point(-32000, -32000);
                tabs.Opacity = 0;
                tabs.SetNotes(notes);
                tabs.Hide();
                Control first = tabs.Controls[0];
                // Exercise repeated live control projection, not just constants.
                for (int round = 0; round < 20; round++)
                foreach (int dpi in new[] { 96, 120, 144, 192, 96 })
                {
                    SideTabPhysicalMetrics metrics = SideTabPhysicalMetrics.ForDpi(dpi);
                    tabs.ApplyPhysicalMetrics(metrics);
                    int row = metrics.Height + metrics.Gap;
                    if (tabs.AutoScaleMode != AutoScaleMode.None ||
                        tabs.ClientSize != new Size(metrics.Width,
                            3 * row - metrics.Gap) ||
                        !Object.ReferenceEquals(first, tabs.Controls[0])) return false;
                    foreach (Control control in tabs.Controls)
                    {
                        StickyNoteTabControl tab = control as StickyNoteTabControl;
                        if (tab == null || tab.Bounds != new Rectangle(
                            0, tab.ListIndex * row, metrics.Width, metrics.Height))
                            return false;
                        if (tab.Font.Unit != GraphicsUnit.Pixel ||
                            Math.Abs(tab.Font.Size - 8.5F * dpi / 72F) > 0.001F ||
                            tab.Font.Style != FontStyle.Bold) return false;
                        Font sameDpiFont = tab.Font;
                        tab.ApplyPhysicalMetrics(SideTabPhysicalMetrics.ForDpi(dpi));
                        if (!Object.ReferenceEquals(sameDpiFont, tab.Font)) return false;
                    }
                    if (StickyNoteTabsForm.CalculateDropIndex(row * 2, 3, metrics) != 2 ||
                        StickyNoteTabsForm.PreviewTargetTop(1, -1, 1, metrics) !=
                            row + metrics.PreviewInsertionGap) return false;
                    tabs.ShowDropPreviewForTest(notes[0].NoteId, 2);
                    if (!tabs.HasDropPreviewForTest) return false;
                    tabs.SetNotes(new List<SideTabSnapshot>(notes), 7);
                    if (!Object.ReferenceEquals(first, tabs.Controls[0]) ||
                        !tabs.HasDropPreviewForTest ||
                        (int)Pc2Get(tabs, "_globalStartIndex") != 7) return false;
                    SideTabPhysicalMetrics next = SideTabPhysicalMetrics.ForDpi(
                        dpi == 192 ? 96 : 192);
                    tabs.ApplyPhysicalMetrics(next);
                    if (tabs.HasDropPreviewForTest || tabs.Controls.Count != 3 ||
                        tabs.ClientSize != new Size(next.Width,
                            3 * (next.Height + next.Gap) - next.Gap)) return false;
                }
                // A type-only change must update the icon even when title and
                // color are identical. Old string signatures omitted the type.
                SideTabSnapshot original = notes[0];
                notes[0] = SideTabSnapshot.FromData(new StickyNoteData
                {
                    Id = original.NoteId, Title = original.DisplayTitle,
                    ColorArgb = original.ColorArgb, Visible = original.Visible,
                    IsTodoList = true
                });
                tabs.SetNotes(notes);
                if (!first.IsDisposed ||
                    !((StickyNoteTabControl)tabs.Controls[0]).Snapshot.IsTodoList)
                    return false;
                SideTabSnapshot moved = notes[0];
                notes.RemoveAt(0);
                notes.Add(moved);
                tabs.SetNotes(notes);
                if (((StickyNoteTabControl)tabs.Controls[2]).Snapshot.NoteId != moved.NoteId)
                    return false;
                tabs.SetNotes(new List<SideTabSnapshot>());
                if (tabs.Visible || tabs.NoteCount != 0 || tabs.Controls.Count != 0)
                    return false;
            }
            return true;
        }
    }
}
