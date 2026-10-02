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

        private sealed class StickyEditorCheckResult
        {
            internal bool ImeAnimationGuardOk;
            internal bool ImeAutoSaveGuardOk;
            internal bool DeferredInitialFocusSafeOk;
            internal bool RichTextToolbarOk;
            internal bool SmoothFormatInteractionOk;
            internal bool StableFormatSelectorModelOk;
            internal bool FormatToolbarFocusOk;
            internal bool FormatSelectorsAlwaysBlackOk;
            internal bool BodyTextColorSwitchOk;
            internal bool DockResizeRoleOk;
            internal bool NativeWindowStyleAppliedOk;
            internal bool GroupTopMostSyncOk;
            internal bool MultilingualInputOk;
            internal bool TabSwitchContentPreservedOk;
            internal bool FirstFormatCommitOk;
            internal bool EmptyNoteFormattingOk;
            internal bool CaretTypingFormatSwitchOk;
            internal bool SingleNativeImeCommitOk;
            internal bool UnifiedContextMenusOk;
        }

        private static StickyEditorCheckResult RunStickyEditorChecks()
        {
            StickyEditorCheckResult result = new StickyEditorCheckResult();
            DateTime imeGuardNow = DateTime.UtcNow;
            result.ImeAnimationGuardOk =
                ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010D) &&
                ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010F) &&
                !ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010E) &&
                PetAnimationController.ShouldPauseOwnNoteAnimation(
                    true, DateTime.MinValue, imeGuardNow) &&
                PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                    imeGuardNow.AddMilliseconds(1), imeGuardNow) &&
                !PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                    imeGuardNow.AddMilliseconds(-1), imeGuardNow);
            result.ImeAutoSaveGuardOk = StickyNoteWindow.ShouldDeferAutoSave(
                true, DateTime.MinValue, imeGuardNow) &&
                StickyNoteWindow.ShouldDeferAutoSave(false,
                    imeGuardNow.AddMilliseconds(-200), imeGuardNow) &&
                !StickyNoteWindow.ShouldDeferAutoSave(false,
                    imeGuardNow.AddSeconds(-2), imeGuardNow);
            result.DeferredInitialFocusSafeOk =
                StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 0, false) &&
                !StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 1, false) &&
                !StickyNoteWindow.ShouldApplyDeferredInitialFocus(0, 0, true);

            StickyNoteData richToolbarData = new StickyNoteData();
            richToolbarData.Text = "格式工具栏测试";
            richToolbarData.Width = 360;
            richToolbarData.Height = 260;
            using (StickyNoteWindow richToolbarNote =
                new StickyNoteWindow(richToolbarData))
            {
                result.RichTextToolbarOk =
                    richToolbarNote.HasRichTextFormattingToolbar &&
                    richToolbarNote.HeaderTypeIconVisibleForTest &&
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseRichTextFormattingForTest();
                result.SmoothFormatInteractionOk =
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseSmoothFormatInteractionForTest();
                result.StableFormatSelectorModelOk =
                    richToolbarNote.UsesStableListFormatSelectors && RunStickyContentViewChecks();
                result.FormatToolbarFocusOk =
                    richToolbarNote.FormatControlsPreserveSelectionForTest;
                result.FormatSelectorsAlwaysBlackOk =
                    richToolbarNote.FormatSelectorsAlwaysBlackForTest;
                result.BodyTextColorSwitchOk =
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseBodyTextColorSwitchForTest();
                result.DockResizeRoleOk =
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseDockResizeRoleForTest();
                result.NativeWindowStyleAppliedOk =
                    richToolbarNote.NativeMaximizeStyleDisabledForTest;
                result.GroupTopMostSyncOk =
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseGroupTopMostForTest();
                result.MultilingualInputOk =
                    new StickyWindowInteractionDriver(richToolbarNote).ExerciseMultilingualInputForTest();
                result.TabSwitchContentPreservedOk = new StickyWindowInteractionDriver(richToolbarNote).ExerciseReminderSwitchContentPreservationForTest();
            }

            using (StickyNoteWindow firstFormatNote =
                new StickyNoteWindow(new StickyNoteData()))
            {
                result.FirstFormatCommitOk =
                    new StickyWindowInteractionDriver(firstFormatNote).ExerciseFirstFormatCommitForTest();
                result.EmptyNoteFormattingOk =
                    new StickyWindowInteractionDriver(firstFormatNote).ExerciseEmptyNoteFormattingForTest();
                result.CaretTypingFormatSwitchOk =
                    new StickyWindowInteractionDriver(firstFormatNote).ExerciseCaretTypingFormatSwitchForTest();
                result.SingleNativeImeCommitOk = new StickyWindowInteractionDriver(firstFormatNote).ExerciseSingleNativeImeCommitAfterFormatForTest();
                result.UnifiedContextMenusOk =
                    new StickyWindowInteractionDriver(firstFormatNote).ExerciseUnifiedNoteContextMenusForTest();
            }
            return result;
        }

        private sealed class StickyReminderWindowCheckResult
        {
            internal bool BannerCountdownOk;
            internal bool CompactBannerOk;
            internal bool SelectionActionsOk;
            internal bool InlineCreationActionsRemovedOk;
            internal bool FirstClickStableOk;
            internal bool BlankAreaClearOk;
            internal bool BannerRefreshInPlaceOk;
        }

        private static StickyReminderWindowCheckResult
            RunStickyReminderWindowChecks(StickyNoteWindow note)
        {
            StickyReminderWindowCheckResult result =
                new StickyReminderWindowCheckResult();
            List<ReminderItem> bannerItems = new List<ReminderItem>();
            bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddSeconds(65),
                "中文提醒项目", null, 24F, true));
            bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddHours(2),
                "第二条提醒"));
            note.UpdateReminderBanner(bannerItems);
            result.BannerCountdownOk = note.ReminderBannerLineCount == 2 &&
                note.ReminderBannerText.Contains("中文提醒项目") &&
                note.ReminderBannerText.Contains("第二条提醒") &&
                StickyNoteWindow.FormatCountdown(TimeSpan.FromSeconds(65)) ==
                    "1分5秒" &&
                StickyNoteWindow.FormatCountdown(TimeSpan.Zero) == "现在";
            result.CompactBannerOk = Math.Abs(
                note.ReminderBannerFirstFontSize - 24F) < 0.2F;
            result.SelectionActionsOk =
                new StickyWindowInteractionDriver(note).ExerciseReminderSelectionActionsForTest();
            result.InlineCreationActionsRemovedOk =
                new StickyWindowInteractionDriver(note).ExerciseInlineCreationActionsRemovedForTest();
            result.FirstClickStableOk =
                new StickyWindowInteractionDriver(note).ExerciseReminderFirstClickStabilityForTest(
                    out result.BlankAreaClearOk,
                    out result.BannerRefreshInPlaceOk);
            return result;
        }

        private sealed class StickyTodoWindowCheckResult
        {
            internal bool GroupingOk;
            internal bool MarkerRoundTripOk;
            internal bool PlainTextProjectionOk;
            internal bool FixedTypeActionsOk;
            internal bool WrapAndInlineEditOk;
            internal bool OverallFontSizeOk;
            internal bool DedicatedRowContextMenusOk;
        }

        private static StickyTodoWindowCheckResult
            RunStickyTodoWindowChecks(StickyNoteWindow note)
        {
            StickyTodoWindowCheckResult result =
                new StickyTodoWindowCheckResult();
            result.GroupingOk = note.VisibleTodoItemCount == 2 &&
                note.TodoGroupCount == 3;
            bool completedMarker;
            string cleaned = StickyNoteWindow.ParseTodoTextLine(
                "[ ][ ][x] 最推荐的修改方法", out completedMarker);
            bool pendingMarker;
            string pending = StickyNoteWindow.ParseTodoTextLine(
                "[ ] 普通项目", out pendingMarker);
            result.MarkerRoundTripOk = cleaned == "最推荐的修改方法" &&
                completedMarker && pending == "普通项目" && !pendingMarker;
            List<StickyTodoItem> switchItems = new List<StickyTodoItem>();
            switchItems.Add(new StickyTodoItem("[ ] 第一项", false));
            switchItems.Add(new StickyTodoItem("[x] 第二项", true));
            result.PlainTextProjectionOk =
                StickyNoteWindow.BuildPlainTextFromTodos(switchItems) ==
                    "第一项" + Environment.NewLine + "第二项";
            using (StickyNoteWindow todoStressNote =
                new StickyNoteWindow(new StickyNoteData()))
            {
                result.FixedTypeActionsOk =
                    new StickyWindowInteractionDriver(todoStressNote).ExerciseFixedNoteTypeActionsForTest();
                result.WrapAndInlineEditOk =
                    new StickyWindowInteractionDriver(todoStressNote).ExerciseTodoWrapAndInlineEditForTest();
                result.OverallFontSizeOk =
                    new StickyWindowInteractionDriver(todoStressNote).ExerciseTodoOverallFontSizeForTest();
                result.DedicatedRowContextMenusOk =
                    new StickyWindowInteractionDriver(todoStressNote).ExerciseDedicatedRowContextMenusForTest();
            }
            return result;
        }

        private sealed class StickyScheduleWindowCheckResult
        {
            internal bool CountdownOk;
            internal bool FontChoicesOk;
            internal bool DateMouseWheelOk;
            internal bool PinMarkerToggleOk;
        }

        private static StickyScheduleWindowCheckResult
            RunStickyScheduleWindowChecks()
        {
            StickyScheduleWindowCheckResult result =
                new StickyScheduleWindowCheckResult();
            result.CountdownOk = StickyNoteWindow.FormatScheduleCountdown(
                DateTime.Today.AddDays(6), DateTime.Today) == "6天" &&
                StickyNoteWindow.FormatScheduleCountdown(DateTime.Today,
                    DateTime.Today) == "今天" &&
                StickyNoteWindow.FormatScheduleCountdown(
                    DateTime.Today.AddDays(-2), DateTime.Today) == "已过2天";
            result.FontChoicesOk =
                StickyNoteWindow.ScheduleFontSizeLabel(9F) == "特小 9" &&
                StickyNoteWindow.ScheduleFontSizeLabel(10.5F) == "小 10.5" &&
                StickyNoteWindow.ScheduleFontSizeLabel(12F) == "小 10.5" &&
                StickyNoteWindow.ScheduleFontSizeLabel(16F) == "中 16" &&
                StickyNoteWindow.ScheduleFontSizeLabel(22F) == "大 22" &&
                StickyNoteWindow.ScheduleFontSizeLabel(48F) == "特大 48";
            result.DateMouseWheelOk =
                ScheduleItemDialog.StepDateWithMouseWheel(
                    DateTime.Today, 120) == DateTime.Today.AddDays(-1) &&
                ScheduleItemDialog.StepDateWithMouseWheel(
                    DateTime.Today, -240) == DateTime.Today.AddDays(2);
            StickyNoteData data = new StickyNoteData();
            data.IsSchedule = true;
            using (StickyNoteWindow note = new StickyNoteWindow(data))
                result.PinMarkerToggleOk = note.HeaderTypeIconVisibleForTest &&
                    new StickyWindowInteractionDriver(note).ExerciseSchedulePinMarkerForTest();
            return result;
        }

        private sealed class StickyFontCheckResult
        {
            internal bool SizeParsingOk;
            internal bool ChineseFontsFirstOk;
            internal bool InstalledFontListCacheOk;
            internal bool SharedFontLifetimeOk;
        }

        private static StickyFontCheckResult RunStickyFontChecks()
        {
            StickyFontCheckResult result = new StickyFontCheckResult();
            float parsedFive;
            float parsedNumeric;
            result.SizeParsingOk =
                StickyNoteWindow.TryParseFontSize("五号", out parsedFive) &&
                Math.Abs(parsedFive - 10.5F) < 0.01F &&
                StickyNoteWindow.TryParseFontSize("18 磅", out parsedNumeric) &&
                Math.Abs(parsedNumeric - 18F) < 0.01F &&
                !StickyNoteWindow.TryParseFontSize("100", out parsedNumeric);
            result.ChineseFontsFirstOk =
                StickyNoteWindow.IsChineseFontNameForTest("微软雅黑") &&
                StickyNoteWindow.IsChineseFontNameForTest(
                    "Noto Serif SC SemiBold") &&
                StickyNoteWindow.IsChineseFontNameForTest(
                    "Microsoft YaHei UI") &&
                !StickyNoteWindow.IsChineseFontNameForTest("Arial") &&
                !StickyNoteWindow.IsChineseFontNameForTest("Noto Sans JP") &&
                StickyNoteWindow.FontNameSortsBeforeForTest(
                    "Noto Sans SC", "Arial");
            result.InstalledFontListCacheOk =
                StickyNoteWindow.InstalledFontNamesCachedForTest();
            Font first = StickyNoteWindow.CreateSafeFont(
                "Microsoft YaHei UI", 18F, FontStyle.Regular);
            Font second = StickyNoteWindow.CreateSafeFont(
                "Microsoft YaHei UI", 18F, FontStyle.Regular);
            bool usable;
            try
            {
                byte ignoredCharacterSet = second.GdiCharSet;
                usable = ignoredCharacterSet == second.GdiCharSet;
            }
            catch
            {
                usable = false;
            }
            result.SharedFontLifetimeOk = Object.ReferenceEquals(first, second) &&
                usable;
            return result;
        }
    }
}
