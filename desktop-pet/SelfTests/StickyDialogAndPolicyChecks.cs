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

        private sealed class StickyDialogCheckResult
        {
            internal bool ReminderSizePreviewOk;
            internal bool ReminderLiveSizePreviewOk;
            internal bool UnforcedMultilingualImeOk;
            internal bool StandaloneReminderNoAutoStickyOptionOk;
            internal bool RenameInitialFocusOk;
            internal bool AppearanceLocationOk;
            internal bool ReminderDefaultCurrentTimeOk;
        }

        private static StickyDialogCheckResult RunStickyDialogChecks()
        {
            StickyDialogCheckResult result = new StickyDialogCheckResult();
            result.StandaloneReminderNoAutoStickyOptionOk = true;
            using (ReminderDialog dialog = new ReminderDialog(
                "预览", 10.5F, false))
            {
                result.ReminderSizePreviewOk =
                    dialog.ExerciseSizePreviewForTest();
                result.UnforcedMultilingualImeOk =
                    dialog.UsesUnforcedMultilingualIme;
                foreach (Control control in dialog.Controls)
                {
                    if (control is CheckBox && control.Text.IndexOf(
                        "创建桌面便利贴", StringComparison.Ordinal) >= 0)
                        result.StandaloneReminderNoAutoStickyOptionOk = false;
                }
                result.StandaloneReminderNoAutoStickyOptionOk =
                    result.StandaloneReminderNoAutoStickyOptionOk &&
                    dialog.ClientSize.Height == 487;
            }
            using (StickyNoteWindow note =
                new StickyNoteWindow(new StickyNoteData()))
                result.ReminderLiveSizePreviewOk =
                    new StickyWindowInteractionDriver(note).ExerciseReminderLiveSizePreviewForTest();
            using (NoteTitleDialog dialog = new NoteTitleDialog("周计划"))
            {
                result.RenameInitialFocusOk = dialog.TitleInputIsInitialActive;
                result.UnforcedMultilingualImeOk &=
                    dialog.UsesUnforcedMultilingualIme;
            }
            Point below = StickyNoteWindow.CalculateAppearanceDialogLocation(
                new Rectangle(300, 100, 320, 300),
                new Size(520, 260),
                new Rectangle(0, 0, 1200, 900));
            Point above = StickyNoteWindow.CalculateAppearanceDialogLocation(
                new Rectangle(850, 700, 320, 180),
                new Size(520, 260),
                new Rectangle(0, 0, 1200, 900));
            result.AppearanceLocationOk = below.Y == 408 && below.X >= 0 &&
                above.Y < 700 && above.X >= 0 && above.X + 520 <= 1200;
            result.ReminderDefaultCurrentTimeOk = Math.Abs(
                (ReminderDialog.DefaultSuggestedLocal() -
                    DateTime.Now).TotalSeconds) < 3;
            return result;
        }

        private sealed class StickyWindowPolicyCheckResult
        {
            internal bool HighDpiLayoutOk;
            internal bool ResourceLimitsOk;
            internal bool SoftPaletteOk;
            internal bool FullWidthNormalizationOk;
            internal bool ManagerMarqueeBatchDeleteOk;
            internal bool ManagerSortingOk;
            internal bool ManagerImportPreviewOk;
            internal bool ManagerResponsiveLayoutOk;
            internal bool NativeSnapDisabledOk;
            internal bool SteadyDockGuideOk;
            internal bool OrdinaryLinkDetectionOk;
        }

        private static StickyWindowPolicyCheckResult
            RunStickyWindowPolicyChecks(StickyFeature repository)
        {
            StickyWindowPolicyCheckResult result =
                new StickyWindowPolicyCheckResult();
            result.HighDpiLayoutOk =
                StickyNoteWindow.MinimumNoteSizeForDpi(96) ==
                    new Size(280, 220) &&
                StickyNoteWindow.MinimumNoteSizeForDpi(192) ==
                    new Size(560, 440) &&
                StickyNoteWindow.HeaderRowHeightForDpi(192) >=
                    StickyNoteWindow.HeaderRowHeightForDpi(96) * 2 - 1;
            result.ResourceLimitsOk =
                StickyNoteLimits.MaximumNotes == 100 &&
                StickyNoteLimits.MaximumTodoItemsPerNote == 500 &&
                StickyNoteLimits.MaximumBodyCharacters == 4000000 &&
                StickyNoteLimits.MaximumRichTextCharacters == 16000000 &&
                StickyFeature.CanCreateAtCount(true, 0) &&
                StickyFeature.CanCreateAtCount(true,
                    StickyNoteLimits.MaximumNotes - 1) &&
                !StickyFeature.CanCreateAtCount(true,
                    StickyNoteLimits.MaximumNotes) &&
                !StickyFeature.CanCreateAtCount(false, 0);
            result.SoftPaletteOk =
                StickyNoteWindow.PaletteColorForTest(0).ToArgb() ==
                    Color.FromArgb(255, 255, 117, 112).ToArgb() &&
                StickyNoteWindow.PaletteColorForTest(32).ToArgb() ==
                    Color.FromArgb(255, 239, 240, 241).ToArgb();
            result.FullWidthNormalizationOk =
                StickyNoteWindow.NormalizeFullWidthLatin(
                    "中文ｃｔｒｌＥｎｇｌｉｓｈ１２３") ==
                    "中文ctrlEnglish123";
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                 delegate { return repository.GetAll(); },
                 new StickyNotesManagerCommands()))
            {
                manager.StartPosition = FormStartPosition.Manual;
                manager.Location = new Point(-32000, -32000);
                manager.Opacity = 0D;
                manager.Show();
                bool defaultLayout = manager.HasNonOverlappingLayoutForTest &&
                    manager.ImportActionMatchesModeForTest;
                manager.ResizeToMinimumForTest();
                result.ManagerResponsiveLayoutOk = defaultLayout &&
                    manager.HasNonOverlappingLayoutForTest &&
                    manager.ImportActionMatchesModeForTest;
                result.ManagerMarqueeBatchDeleteOk =
                    manager.SupportsMarqueeBatchDelete;
            }
            StickyNoteData sortAlpha = new StickyNoteData
            {
                Id = "manager-sort-alpha",
                Title = "Alpha",
                ModifiedUtcTicks = 100
            };
            StickyNoteData sortBeta = new StickyNoteData
            {
                Id = "manager-sort-beta",
                Title = "Beta",
                IsTodoList = true,
                ModifiedUtcTicks = 200,
                ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks
            };
            StickyNoteData sortGamma = new StickyNoteData
            {
                Id = "manager-sort-gamma",
                Title = "Gamma",
                IsSchedule = true,
                ModifiedUtcTicks = 300,
                ReminderUtcTicks = DateTime.UtcNow.AddHours(1).Ticks
            };
            List<StickyNoteData> sortNotes = new List<StickyNoteData>
                { sortGamma, sortAlpha, sortBeta };
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return sortNotes; },
                new StickyNotesManagerCommands()))
            {
                manager.RefreshForTest();
                manager.SortColumnForTest(0);
                List<string> nameAsc = manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(0);
                List<string> nameDesc = manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(1);
                List<string> statusAsc = manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(2);
                List<string> reminderAsc = manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(3);
                List<string> modifiedAsc = manager.DisplayedTitlesForTest();
                result.ManagerSortingOk =
                    nameAsc.Count == 3 && nameAsc[0] == "Alpha" &&
                    nameAsc[2] == "Gamma" && nameDesc[0] == "Gamma" &&
                    nameDesc[2] == "Alpha" &&
                    statusAsc[0] == "Alpha" && statusAsc[1] == "Beta" &&
                    statusAsc[2] == "Gamma" &&
                    reminderAsc[0] == "Gamma" &&
                    reminderAsc[1] == "Beta" &&
                    reminderAsc[2] == "Alpha" &&
                    modifiedAsc[0] == "Alpha" &&
                    modifiedAsc[2] == "Gamma" &&
                    manager.SortIndicatorForTest(3).EndsWith("▲",
                        StringComparison.Ordinal);
            }
            StickyNoteData previewCurrent = new StickyNoteData
            {
                Id = "manager-preview-current",
                Title = "当前版本",
                Text = "current"
            };
            StickyNoteData previewConflict = previewCurrent.CloneForPersistence();
            previewConflict.Text = "imported";
            StickyNoteData previewNew = new StickyNoteData
            {
                Id = "manager-preview-new",
                Title = "待导入",
                Text = "new"
            };
            StickyNoteData previewExisting = new StickyNoteData
            {
                Id = "manager-preview-existing",
                Title = "已存在",
                Text = "same"
            };
            StickyNoteData previewExistingBackup =
                previewExisting.CloneForPersistence();
            List<StickyNoteData> previewCurrentNotes =
                new List<StickyNoteData> { previewCurrent, previewExisting };
            List<StickyNoteData> previewImportedNotes =
                new List<StickyNoteData> { previewConflict, previewNew,
                    previewExistingBackup };
            StickyImportMergeResult previewPlan =
                StickyImportMergePlanner.Calculate(previewCurrentNotes,
                    previewImportedNotes);
            int previewDeleteCalls = 0;
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return previewCurrentNotes; },
                new StickyNotesManagerCommands
                {
                    PrepareImport = delegate
                    {
                        return Task.FromResult(new StickyNotesImportPreview(previewPlan,
                            previewImportedNotes));
                    },
                    ConfirmImport = delegate { return Task.FromResult(false); },
                    DeleteNote = delegate { previewDeleteCalls++; }
                 }))
            {
                manager.StartPosition = FormStartPosition.Manual;
                manager.Location = new Point(-32000, -32000);
                manager.Opacity = 0D;
                manager.Show();
                manager.BeginImportPreviewForTest(
                    new StickyNotesImportPreview(previewPlan,
                        previewImportedNotes));
                manager.ResizeToMinimumForTest();
                result.ManagerResponsiveLayoutOk =
                    result.ManagerResponsiveLayoutOk &&
                    manager.HasNonOverlappingLayoutForTest &&
                    manager.ImportActionMatchesModeForTest;
                List<string> previewBeforeDelete =
                    manager.DisplayedTitlesForTest();
                manager.SelectAllForTest();
                manager.DeleteKeyForTest();
                List<string> previewAfterDelete =
                    manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(0);
                List<string> previewTitles = manager.DisplayedTitlesForTest();
                manager.SortColumnForTest(1);
                List<string> previewStatuses = manager.DisplayedStatusesForTest();
                manager.SearchForTest("待导入");
                List<string> searchedPreview = manager.DisplayedTitlesForTest();
                manager.SearchForTest(String.Empty);
                manager.CancelImportPreviewForTest();
                result.ManagerResponsiveLayoutOk =
                    result.ManagerResponsiveLayoutOk &&
                    manager.HasNonOverlappingLayoutForTest &&
                    manager.ImportActionMatchesModeForTest;
                List<string> afterCancel = manager.DisplayedTitlesForTest();
                result.ManagerImportPreviewOk =
                    !manager.IsImportPreviewForTest &&
                    previewPlan.AddedCount == 2 &&
                    previewPlan.ConflictCount == 1 &&
                    previewDeleteCalls == 0 &&
                    previewBeforeDelete.Count == previewAfterDelete.Count &&
                    previewTitles.Count == 3 &&
                    previewTitles.Contains("待导入") &&
                    previewTitles.Contains("当前版本") &&
                    previewStatuses.Count == 3 &&
                    previewStatuses[0] == "待导入" &&
                    previewStatuses[1] == "已存在" &&
                    previewStatuses[2] == "冲突副本" &&
                    searchedPreview.Count == 1 &&
                    searchedPreview[0] == "待导入" &&
                    afterCancel.Count == 2 &&
                    previewCurrentNotes.Count == 2 &&
                    previewCurrentNotes[0].Text == "current";
            }
            long styleWithMaximize = 0x00040000L | 0x00010000L;
            result.NativeSnapDisabledOk =
                StickyNoteWindow.RemoveMaximizeStyle(styleWithMaximize) ==
                    0x00040000L;
            using (DockPulseIndicatorForm guide =
                new DockPulseIndicatorForm(Color.DeepSkyBlue, 0))
                result.SteadyDockGuideOk = guide.UsesSteadyOpacityForTest;
            StickyNoteData linkData = new StickyNoteData();
            linkData.Text = "C:\\Users\\Penny pet\\进度表.xlsx\r\n" +
                "https://www.baidu.com/";
            using (StickyNoteWindow note = new StickyNoteWindow(linkData, true))
                result.OrdinaryLinkDetectionOk =
                    new StickyWindowInteractionDriver(note).ExerciseOrdinaryLinkRefreshForTest();
            return result;
        }
    }
}
