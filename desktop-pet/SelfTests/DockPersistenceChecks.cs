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

        private sealed class DockPersistenceCheckResult
        {
            internal bool SideTabOrderOk;
            internal bool DockRoundTripOk;
            internal bool MixedInsertionOk;
            internal bool WholeComponentRestoreOk;
            internal bool SnapshotSurvivesBrokenParentLinksOk;
            internal bool GroupSnapshotRoundTripOk;
            internal bool HiddenSlotRestartOk;
            internal bool LowerCloseRewiresNeighborsOk;
            internal bool ExpandAndTileRoundTripOk;
        }

        private static DockPersistenceCheckResult
            RunDockPersistenceChecks(string outputPath)
        {
            DockPersistenceCheckResult result =
                new DockPersistenceCheckResult();
            string tabOrderPath = outputPath + ".tab-order-test.dat";
            StickyFeature tabOrderRepository =
                StickyFeature.LoadFromFile(tabOrderPath);
            StickyNoteData tabA = tabOrderRepository.Create("A", Point.Empty);
            StickyNoteData tabB = tabOrderRepository.Create("B", Point.Empty);
            StickyNoteData tabC = tabOrderRepository.Create("C", Point.Empty);
            tabA.Visible = false;
            tabB.Visible = false;
            tabC.Visible = false;
            tabOrderRepository.SaveToFile(tabOrderPath);
            tabOrderRepository.ReorderHidden(tabA, 3);
            // ReorderHidden persists through the nonblocking writer; flush
            // before reloading so the round trip observes the new order.
            tabOrderRepository.WaitForPendingSaves();
            StickyFeature restoredTabOrder =
                StickyFeature.LoadFromFile(tabOrderPath);
            List<StickyNoteData> orderedTabs =
                restoredTabOrder.GetHiddenInTabOrder();
            result.SideTabOrderOk = orderedTabs.Count == 3 &&
                orderedTabs[0].Text == "B" && orderedTabs[1].Text == "C" &&
                orderedTabs[2].Text == "A" &&
                File.ReadAllText(tabOrderPath, Encoding.UTF8)
                    .StartsWith(StickyNoteCodec.CurrentVersion + "|");
            if (File.Exists(tabOrderPath)) File.Delete(tabOrderPath);
            if (File.Exists(tabOrderPath + ".bak"))
                File.Delete(tabOrderPath + ".bak");

            string dockPath = outputPath + ".dock-test.dat";
            StickyFeature dockRepository =
                StickyFeature.LoadFromFile(dockPath);
            StickyNoteData dockParent = dockRepository.Create("上层",
                new Point(100, 100));
            StickyNoteData dockChild = dockRepository.Create("下层",
                new Point(100, 330));
            StickyDockGroups.ApplyOrderedGroup(new[] { dockParent, dockChild });
            dockRepository.SaveToFile(dockPath);
            List<StickyNoteData> restoredDockNotes =
                StickyFeature.LoadFromFile(dockPath).GetAll();
            result.DockRoundTripOk = restoredDockNotes.Count == 2 &&
                restoredDockNotes.Exists(delegate(StickyNoteData value)
                {
                    return !String.IsNullOrEmpty(value.DockGroupId);
                });
            StickyNoteData dockInsertedTodo = dockRepository.Create(
                "中间待办", new Point(100, 330));
            dockInsertedTodo.IsTodoList = true;
            StickyDockOperations.MergeDockSnapshotsAfterParent(
                new[] { dockParent, dockChild }, dockParent, new[] { dockInsertedTodo });
            result.MixedInsertionOk =
                dockInsertedTodo.DockGroupOrder == 1 &&
                dockChild.DockGroupOrder == 2 &&
                dockInsertedTodo.IsTodoList && !dockParent.IsTodoList;

            dockParent.Visible = false;
            dockInsertedTodo.Visible = false;
            dockChild.Visible = false;
            List<StickyNoteData> storedDockOrder = StickyDockOperations
                .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                    dockChild, dockParent, dockInsertedTodo }, dockChild,
                    false);
            result.WholeComponentRestoreOk =
                StickyDockOperations.ShouldRestoreWholeDockComponent(
                    storedDockOrder.Count, true) &&
                storedDockOrder.Count == 3 &&
                Object.ReferenceEquals(storedDockOrder[0], dockParent) &&
                Object.ReferenceEquals(storedDockOrder[1], dockInsertedTodo) &&
                Object.ReferenceEquals(storedDockOrder[2], dockChild);
            StickyImportValidationResult brokenLinks = StickyImportBackupValidator.Validate(new[] {
                StickyNoteCodec.SerializeLine(dockChild, String.Empty),
                StickyNoteCodec.SerializeLine(dockInsertedTodo, "broken-parent"),
                StickyNoteCodec.SerializeLine(dockParent) });
            List<StickyNoteData> snapshotOrder = StickyDockGroups
                .GetOrderedGroup(brokenLinks.Notes, brokenLinks.Notes[1]);
            result.SnapshotSurvivesBrokenParentLinksOk =
                brokenLinks.Succeeded && snapshotOrder.Count == 3 &&
                snapshotOrder[0].Id == dockParent.Id &&
                snapshotOrder[1].Id == dockInsertedTodo.Id &&
                snapshotOrder[2].Id == dockChild.Id;
            dockRepository.SaveToFile(dockPath);
            StickyFeature persistedDockRepository =
                StickyFeature.LoadFromFile(dockPath);
            StickyNoteData persistedDockMember =
                persistedDockRepository.Find(dockInsertedTodo.Id);
            List<StickyNoteData> persistedDockOrder = StickyDockGroups
                .GetOrderedGroup(persistedDockRepository.GetAll(),
                    persistedDockMember);
            result.GroupSnapshotRoundTripOk = persistedDockOrder.Count == 3 &&
                persistedDockOrder[0].Id == dockParent.Id &&
                persistedDockOrder[1].Id == dockInsertedTodo.Id &&
                persistedDockOrder[2].Id == dockChild.Id &&
                persistedDockOrder[0].DockGroupOrder == 0 &&
                persistedDockOrder[1].DockGroupOrder == 1 &&
                persistedDockOrder[2].DockGroupOrder == 2 &&
                File.ReadAllText(dockPath, Encoding.UTF8)
                    .StartsWith(StickyNoteCodec.CurrentVersion + "|");
            dockParent.Visible = true;
            dockInsertedTodo.Visible = true;
            dockChild.Visible = true;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                dockParent, dockInsertedTodo, dockChild });
            dockInsertedTodo.Visible = false;
            result.LowerCloseRewiresNeighborsOk = dockInsertedTodo.DockGroupOrder == 1 &&
                StickyDockGroups.GetVisibleNeighbor(new[] { dockParent, dockInsertedTodo, dockChild },
                    dockChild, -1) == dockParent;
            if (File.Exists(dockPath)) File.Delete(dockPath);
            if (File.Exists(dockPath + ".bak")) File.Delete(dockPath + ".bak");

            string hiddenSlotPath = outputPath + ".hidden-slot-test.dat";
            StickyFeature hiddenSlotRepository =
                StickyFeature.LoadFromFile(hiddenSlotPath);
            StickyNoteData persistedHideA = hiddenSlotRepository.Create(
                "A", Point.Empty);
            StickyNoteData persistedHideB = hiddenSlotRepository.Create(
                "B", Point.Empty);
            StickyNoteData persistedHideC = hiddenSlotRepository.Create(
                "C", Point.Empty);
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                persistedHideA, persistedHideB, persistedHideC });
            persistedHideB.Visible = false;
            hiddenSlotRepository.SaveToFile(hiddenSlotPath);
            StickyFeature restoredHiddenSlotRepository =
                StickyFeature.LoadFromFile(hiddenSlotPath);
            StickyNoteData restoredHiddenMiddle =
                restoredHiddenSlotRepository.Find(persistedHideB.Id);
            List<StickyNoteData> restoredHiddenSlotOrder =
                StickyDockGroups.GetOrderedGroup(
                    restoredHiddenSlotRepository.GetAll(),
                    restoredHiddenMiddle);
            result.HiddenSlotRestartOk = restoredHiddenSlotOrder.Count == 3 &&
                !restoredHiddenSlotOrder[1].Visible &&
                restoredHiddenSlotOrder[1].Id == persistedHideB.Id &&
                restoredHiddenSlotOrder[1].DockGroupOrder == 1 &&
                StickyDockGroups.GetVisibleNeighbor(restoredHiddenSlotOrder,
                    restoredHiddenSlotOrder[2], -1) == restoredHiddenSlotOrder[0];
            if (File.Exists(hiddenSlotPath)) File.Delete(hiddenSlotPath);
            if (File.Exists(hiddenSlotPath + ".bak"))
                File.Delete(hiddenSlotPath + ".bak");

            string expandPath = outputPath + ".expand-and-tile-test.dat";
            StickyFeature expandRepository =
                StickyFeature.LoadFromFile(expandPath);
            StickyNoteData expandA = expandRepository.Create("普通",
                new Point(-5000, -5000));
            StickyNoteData expandB = expandRepository.Create("待办",
                new Point(-5000, -5000));
            expandB.IsTodoList = true;
            StickyNoteData expandC = expandRepository.Create("日程",
                new Point(-5000, -5000));
            expandC.IsSchedule = true;
            expandA.Width = 320;
            expandA.Height = 230;
            expandB.Width = 420;
            expandB.Height = 310;
            expandC.Width = 360;
            expandC.Height = 260;
            expandB.Visible = false;
            expandC.Visible = false;
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                expandA, expandB, expandC });
            StickyHostedRuntime expandRuntime = new StickyHostedRuntime();
            expandRuntime.AddNote(expandA.Id);
            List<DockLayoutTarget> expandTargets = StickyDockController.PrepareStickyExpandAndTileTargets(expandRepository.GetAll(),
                    new Rectangle(0, 0, 1920, 1040), 1.0);
            List<DockLayoutTarget> secondaryTargets = StickyDockController.PrepareStickyExpandAndTileTargets(new StickyNoteData[] {
                    new StickyNoteData(), new StickyNoteData() },
                    new Rectangle(-1920, 0, 1920, 1040), 1.0);
            bool planningPreservedActual = expandA.X == -5000 &&
                expandB.Width == 420 && expandC.Height == 260;
            // This persistence fixture supplies successful actual responses.
            // Native application is covered by RunStickyHostedLifecycleCheck.
            foreach (DockLayoutTarget target in expandTargets)
            {
                StickyNoteData note = expandRepository.Find(target.NoteId);
                note.X = target.X;
                note.Y = target.Y;
                note.Width = target.Width;
                note.Height = target.Height;
            }
            expandRepository.SaveToFile(expandPath);
            StickyFeature restoredExpandRepository =
                StickyFeature.LoadFromFile(expandPath);
            List<StickyNoteData> restoredExpanded =
                restoredExpandRepository.GetAll();
            result.ExpandAndTileRoundTripOk = planningPreservedActual &&
                expandRuntime.ContainsNote(expandA.Id) &&
                expandTargets.Count == 3 &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandA.Id;
                }) &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandB.Id;
                }) &&
                expandTargets.Exists(delegate(DockLayoutTarget target)
                {
                    return target.NoteId == expandC.Id;
                }) &&
                expandTargets[0].X != expandTargets[1].X &&
                expandTargets[0].X != expandTargets[2].X &&
                expandTargets[1].X != expandTargets[2].X &&
                secondaryTargets.TrueForAll(delegate(DockLayoutTarget target)
                {
                    return target.X >= -1920 && target.X < 0 &&
                        target.Y >= 0 && target.Y < 1040;
                }) &&
                restoredExpanded.Count == 3 &&
                restoredExpanded.TrueForAll(delegate(StickyNoteData note)
                {
                    return note.Visible &&
                        String.IsNullOrEmpty(note.DockGroupId) &&
                        note.DockGroupOrder == -1 &&
                        note.X >= 0 && note.Y >= 0 &&
                        note.X < 1920 && note.Y < 1040;
                }) &&
                restoredExpandRepository.Find(expandA.Id).Width == 320 &&
                restoredExpandRepository.Find(expandB.Id).Width == 320 &&
                restoredExpandRepository.Find(expandC.Id).Height == 360;
            if (File.Exists(expandPath)) File.Delete(expandPath);
            if (File.Exists(expandPath + ".bak"))
                File.Delete(expandPath + ".bak");
            return result;
        }
    }
}
