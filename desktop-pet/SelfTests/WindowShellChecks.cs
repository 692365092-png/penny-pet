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

        private sealed class WindowShellCheckResult
        {
            internal bool StartupDefaultOk;
            internal bool StickyUiHostOk;
            internal StickyHostedCheckResult StickyHosted;
            internal bool ScaleRangeOk;
            internal bool DailyContentSettingsUiOk;
            internal bool ZodiacPreferenceSettingsUiOk;
            internal bool ReverseReminderStepOk;
            internal bool PinActionTextOk;
            internal bool TodoPinActionTextOk;
            internal bool ImeCompatibleEditorOk;
            internal bool SingleWindowStickyInputOk;
            internal bool StickyResizePaintingOk;
            internal StickyReminderWindowCheckResult ReminderChecks;
            internal StickyTodoWindowCheckResult TodoChecks;
            internal bool PersonaRuntimeCatalogOk;
            internal bool SolarTermAttachmentOk;
            internal bool PersonaLyricAnimationOk;
            internal bool SmallTalkAnimationProtectionOk;
            internal bool SolarPreservePlumbingOk;
            internal bool DisplayTopologyRuntimeOk;
            internal bool StickyContentApplySeparationOk;
            internal bool NativePlacementOk;
            internal bool NativeDisplayAbiOk;
            internal bool V11PreferredOk;
            internal bool TemporaryRehomeOk;
            internal bool DockCommitHandoffOk;
            internal bool DockTopologyReprojectOk;
            internal bool DockZOrderOk;
        }

        private static WindowShellCheckResult RunWindowShellChecks(
            StickyNoteData restoredNote)
        {
            WindowShellCheckResult result = new WindowShellCheckResult();
            result.StartupDefaultOk = !new PetSettings().StartAtLogin &&
                StartupRegistration.BuildCommand(
                    "C:\\Program Files\\Penny pet.exe") ==
                    "\"C:\\Program Files\\Penny pet.exe\"";
            using (StickyUiHost host = new StickyUiHost())
            using (ManualResetEventSlim handlerStarted =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseHandler =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim commandCompleted =
                new ManualResetEventSlim(false))
            using (ManualResetEventSlim shutdownCommandCompleted =
                new ManualResetEventSlim(false))
            {
                host.Start();
                int stickyThread = 0;
                host.SetCommandHandler(delegate(StickyUiCommand command)
                {
                    stickyThread = Thread.CurrentThread.ManagedThreadId;
                    handlerStarted.Set();
                    releaseHandler.Wait(5000);
                    return StickyUiCommandResult.Handled();
                });
                StickyUiCommand posted = new StickyUiCommand(
                    StickyUiCommandKind.Show, "note-1", true);
                StickyUiCommandResult commandResult = null;
                Stopwatch postTimer = Stopwatch.StartNew();
                host.PostCommand(posted, delegate(StickyUiCommandResult value)
                {
                    commandResult = value;
                    commandCompleted.Set();
                });
                postTimer.Stop();
                bool handlerRan = handlerStarted.Wait(5000);
                bool returnedBeforeCompletion = postTimer.ElapsedMilliseconds < 1000 &&
                    !commandCompleted.IsSet;
                releaseHandler.Set();
                bool completed = WaitForSignalWithUiPump(commandCompleted, 5000);
                host.BeginShutdown();
                host.WaitForExit(5000);
                StickyUiCommandResult afterShutdown = null;
                host.PostCommand(posted, delegate(StickyUiCommandResult value)
                {
                    afterShutdown = value;
                    shutdownCommandCompleted.Set();
                });
                bool shutdownCompleted = WaitForSignalWithUiPump(
                    shutdownCommandCompleted, 5000);
                result.StickyUiHostOk =
                    handlerRan && returnedBeforeCompletion && completed &&
                    commandResult != null &&
                    commandResult.Status == StickyUiCommandStatus.Handled &&
                    stickyThread != Thread.CurrentThread.ManagedThreadId &&
                    shutdownCompleted && afterShutdown != null &&
                    afterShutdown.Status == StickyUiCommandStatus.NotAccepted;
            }
            result.StickyHosted = RunStickyHostedLifecycleCheck();
            result.PersonaRuntimeCatalogOk =
                RunPersonaRuntimeCatalogCheck();
            result.SolarTermAttachmentOk =
                RunSolarTermAttachmentCheck();
            result.PersonaLyricAnimationOk =
                RunPersonaLyricAnimationCheck();
            result.SmallTalkAnimationProtectionOk =
                RunSmallTalkAnimationProtectionCheck();
            result.SolarPreservePlumbingOk =
                RunSolarPreservePlumbingCheck();
            result.DisplayTopologyRuntimeOk =
                RunDisplayTopologyRuntimeCheck();
            result.StickyContentApplySeparationOk =
                RunStickySnapshotSeparationCheck();
            result.NativePlacementOk =
                RunNativePlacementCheck();
            result.NativeDisplayAbiOk = RunNativeDisplayAbiCheck();
            result.V11PreferredOk =
                RunV11PreferredCheck();
            result.TemporaryRehomeOk =
                RunTemporaryRehomeCheck();
            result.DockCommitHandoffOk =
                RunDockCommitHandoffCheck();
            result.DockTopologyReprojectOk =
                RunDockTopologyReprojectCheck();
            result.DockZOrderOk =
                RunDockZOrderCheck();
            result.ScaleRangeOk =
                PetForm.NormalizeScalePercent(47) == 50 &&
                PetForm.NormalizeScalePercent(104) == 100 &&
                PetForm.NormalizeScalePercent(156) == 160 &&
                PetForm.NormalizeScalePercent(207) == 200 &&
                PetForm.ScaledPetSize(50) == new Size(96, 104) &&
                PetForm.ScaledPetSize(200) == new Size(384, 416);
            WeatherLocation testWeatherLocation;
            WeatherLocation.TryCreate("武汉", "湖北", "中国", 30.5928,
                114.3055, "Asia/Shanghai", out testWeatherLocation);
            using (PetWeatherSource weatherSource = new PetWeatherSource())
            using (DailyContentSettingsForm dailySettings =
                new DailyContentSettingsForm(false, true, true, true,
                    testWeatherLocation, ZodiacSign.Scorpio, 0, 0,
                    weatherSource))
            using (DailyContentSettingsForm unsetDailySettings =
                new DailyContentSettingsForm(true, true, true, false, null,
                    ZodiacSign.None, 0, 0, weatherSource))
            {
                PetSettingsData stored = new PetSettingsData
                {
                    DailyContentEnabled = false,
                    SolarTermEnabled = true,
                    AlmanacEnabled = true,
                    WeatherEnabled = true,
                    WeatherLocationName = "武汉",
                    WeatherLocationAdmin1 = "湖北",
                    WeatherLocationCountry = "中国",
                    WeatherLatitude = 30.5928,
                    WeatherLongitude = 114.3055,
                    WeatherTimezone = "Asia/Shanghai",
                    ZodiacSign = ZodiacSign.Scorpio
                };
                result.DailyContentSettingsUiOk =
                    !dailySettings.DailyContentEnabled &&
                    dailySettings.SolarTermEnabled &&
                    dailySettings.AlmanacEnabled &&
                    !dailySettings.SolarTermControlEnabledForTest &&
                    !dailySettings.AlmanacControlEnabledForTest &&
                    !dailySettings.WeatherControlEnabledForTest &&
                    !dailySettings.WeatherLocationButtonEnabledForTest;
                result.ZodiacPreferenceSettingsUiOk =
                    !dailySettings.ZodiacControlEnabledForTest &&
                    dailySettings.SelectedZodiacSign == ZodiacSign.Scorpio &&
                    dailySettings.ZodiacDisplayNameForTest == "天蝎座" &&
                    unsetDailySettings.ZodiacDisplayNameForTest ==
                        "暂未设置";
                dailySettings.SetZodiacSignForTest(ZodiacSign.Pisces);
                bool canceled = dailySettings.ApplyIfAccepted(stored,
                    DialogResult.Cancel);
                bool cancelKeepsStored = !canceled &&
                    stored.ZodiacSign == ZodiacSign.Scorpio;
                dailySettings.SetDailyContentEnabledForTest(true);
                dailySettings.SetAlmanacEnabledForTest(false);
                bool accepted = dailySettings.ApplyIfAccepted(stored,
                    DialogResult.OK);
                unsetDailySettings.SetWeatherEnabledForTest(true);
                bool missingCityRejected = !unsetDailySettings
                    .ApplyIfAccepted(new PetSettingsData(), DialogResult.OK);
                result.DailyContentSettingsUiOk =
                    result.DailyContentSettingsUiOk &&
                    dailySettings.DailyContentEnabled &&
                    dailySettings.SolarTermEnabled &&
                    dailySettings.SolarTermControlEnabledForTest &&
                    dailySettings.AlmanacControlEnabledForTest &&
                    !dailySettings.AlmanacEnabled &&
                    dailySettings.WeatherControlEnabledForTest &&
                    dailySettings.WeatherLocationButtonEnabledForTest &&
                    stored.WeatherEnabled &&
                    stored.WeatherLocationName == "武汉" &&
                    stored.WeatherTimezone == "Asia/Shanghai" &&
                    !stored.AlmanacEnabled &&
                    missingCityRejected;
                result.ZodiacPreferenceSettingsUiOk =
                    result.ZodiacPreferenceSettingsUiOk &&
                    cancelKeepsStored && accepted &&
                    dailySettings.ZodiacControlEnabledForTest &&
                    dailySettings.ZodiacDisplayNameForTest == "双鱼座" &&
                    stored.DailyContentEnabled &&
                    stored.SolarTermEnabled &&
                    stored.ZodiacSign == ZodiacSign.Pisces;
            }
            int dailyMenuClicks = 0;
            PetContextMenuCommands menuCommands =
                new PetContextMenuCommands();
            menuCommands.ShowDailyContentSettings =
                delegate { dailyMenuClicks++; };
            using (PetContextMenu contextMenu = new PetContextMenu(
                "Penny", false, false, false, menuCommands))
            {
                contextMenu.DailyContentItem.PerformClick();
                result.DailyContentSettingsUiOk =
                    result.DailyContentSettingsUiOk &&
                    contextMenu.DailyContentItem.Text == "个性化每日内容…" &&
                    contextMenu.Menu.Items.IndexOf(
                        contextMenu.DailyContentItem) <
                    contextMenu.Menu.Items.IndexOf(contextMenu.ScaleItem) &&
                    dailyMenuClicks == 1;
            }
            result.ReverseReminderStepOk =
                ReverseStepDateTimePicker.ReverseVirtualKey(0x26) == 0x28 &&
                ReverseStepDateTimePicker.ReverseVirtualKey(0x28) == 0x26;
            result.PinActionTextOk =
                StickyNoteWindow.PinActionText(false) == "置顶" &&
                StickyNoteWindow.PinActionText(true) == "取消置顶";
            StickyNoteData todoPinData = new StickyNoteData();
            todoPinData.IsTodoList = true;
            todoPinData.AlwaysOnTop = true;
            using (StickyNoteWindow note = new StickyNoteWindow(todoPinData))
                result.TodoPinActionTextOk = note.CurrentPinActionText ==
                    "取消置顶" && note.HeaderTypeIconVisibleForTest;
            using (StickyNoteWindow note = new StickyNoteWindow(restoredNote))
            {
                result.ReminderChecks = RunStickyReminderWindowChecks(note);
                result.TodoChecks = RunStickyTodoWindowChecks(note);
                result.ImeCompatibleEditorOk = note.UsesImeCompatibleEditor;
                result.SingleWindowStickyInputOk =
                    !note.UsesLegacyInputProxyForTest &&
                    note.LegacyInputProxyHandleForTest == IntPtr.Zero;
                result.StickyResizePaintingOk = note.UsesBufferedResizePainting;
            }
            return result;
        }
    }
}
