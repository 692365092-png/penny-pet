using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void AnimationTick_DoesNotPauseForOwnNoteFocus()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string tick = Between(source, "private void AnimationTick",
                "private int RuntimeFrameCount");

            Assert.IsFalse(tick.Contains("HasFocusedOwnNoteTextInput()"),
                "Animation must not pause merely because a note editor has focus.");
            Assert.IsFalse(tick.Contains("ShouldPauseOwnNoteAnimation"),
                "Animation must not pause for own-note IME composition.");
        }

        [TestMethod]
        public void AnimationRendering_DoesNotChangeWindowFocusOrActivation()
        {
            string runtime = ReadSource("PetAnimationRuntime.cs");
            string renderer = ReadSource("LayeredSpriteRenderer.cs");

            Assert.IsFalse(runtime.Contains(".Activate(") ||
                runtime.Contains(".Focus(") || runtime.Contains(".BringToFront("),
                "Animation runtime must not activate or focus windows.");
            Assert.IsFalse(renderer.Contains(".Activate(") ||
                renderer.Contains(".Focus(") || renderer.Contains(".BringToFront("),
                "Layered renderer must not activate or focus windows.");
        }

        [TestMethod]
        public void StartupPreload_IncludesTypingAnimationRows()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string warmup = Between(source, "int[] warmRows =",
                "foreach (int row in warmRows)");

            Assert.IsTrue(warmup.Contains("WaitingRow") &&
                warmup.Contains("ThinkingRow"),
                "Typing animation rows must be preloaded during startup.");
        }

        [TestMethod]
        public void StickyPersistence_BarriersAreBoundedAndExitResolvesBothFailures()
        {
            string repository = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string writer = ReadSource("Features/StickyNotes/StickyNoteWriter.cs");
            string wait = RawSource.SliceMethod(writer,
                "internal PersistenceResult Flush(TimeSpan timeout)");
            string commit = Between(repository,
                "private PersistenceResult CommitPreparedSnapshot",
                "internal PersistenceResult CommitImportedMerge");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string exit = Between(coordinator,
                "private bool FlushPersistenceBeforeExit()",
                "private bool ExportUnsavedStickyNotes()");

            Assert.IsTrue(wait.Contains("Monitor.Wait(_gate, remaining)") &&
                wait.Contains("TimeoutException") &&
                wait.Contains("PersistenceResult.Failure(new TimeoutException("),
                "Pending-save barriers must return a bounded failure.");
            Assert.IsTrue(commit.Contains("WaitForPendingSaves()") &&
                commit.Contains("if (pending.Error is TimeoutException) return pending;"),
                "Import and full restore must stop when pending saves time out.");
            int emergencyExport = exit.IndexOf(
                "if (!ExportUnsavedStickyNotes()) return false;",
                StringComparison.Ordinal);
            int settingsResolution = exit.IndexOf(
                "if (!settingsResult.Succeeded)", StringComparison.Ordinal);
            Assert.IsTrue(emergencyExport >= 0 &&
                settingsResolution > emergencyExport &&
                !exit.Contains("return ExportUnsavedStickyNotes();"),
                "Emergency Sticky export must not silently resolve a Settings failure.");
        }

        [TestMethod]
        public void DailyContent_OnlyRunsForNonDragPetMouseUp()
        {
            string animation = ReadSource("PetAnimationRuntime.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");
            int mouseUp = animation.IndexOf("private void PetMouseUp",
                StringComparison.Ordinal);
            int nextMethod = animation.IndexOf(
                "private async void HandlePetPoked", mouseUp,
                StringComparison.Ordinal);
            string body = animation.Substring(mouseUp, nextMethod - mouseUp);
            int poke = body.IndexOf("HandlePetPoked()",
                StringComparison.Ordinal);

            Assert.IsTrue(body.Contains("if (wasDrag)") &&
                body.Contains("else") && poke >= 0,
                "A valid non-drag mouse-up must enter one poke boundary.");
            Assert.IsFalse(body.Contains("_dailyContentCoordinator") ||
                body.Contains("StartOrdinaryPokeAnimation"),
                "PetMouseUp must not duplicate poke side effects.");
            Assert.IsFalse(startup.Contains("HandlePetPoked"),
                "Startup must never trigger daily content.");
        }

        [TestMethod]
        public void SmallTalkRuntime_IsOwnedByCoordinator()
        {
            string form = ReadSource("PetForm.cs");
            string animation = ReadSource("PetAnimationRuntime.cs");
            string coordinator = ReadSource("PetSmallTalkCoordinator.cs");
            string poke = Between(animation,
                "private async void HandlePetPoked",
                "private void StartOrdinaryPokeAnimation");

            Assert.IsTrue(form.Contains(
                    "private readonly PetSmallTalkCoordinator") &&
                poke.Contains("StartOrdinaryPokeAnimation(nowUtc)") &&
                poke.Contains("IsOpeningEligible") &&
                poke.Contains("StartNotificationPokeAnimation(nowUtc)") &&
                poke.Contains(".HandlePetPokedAsync") &&
                poke.Contains("if (dailyHandled)") &&
                poke.Contains("_daypartCheckInCoordinator.HandlePetPoked") &&
                poke.Contains("_smallTalkCoordinator.HandlePetPoked(nowUtc)") &&
                !poke.Contains(".Wait(") && !poke.Contains(".Result"),
                "PetForm must preserve Easter, Daily, Daypart, SmallTalk, animation order.");
            Assert.IsFalse(form.Contains("SmallTalkPhrases") ||
                form.Contains("_smallTalkRandom") ||
                form.Contains("_lastSmallTalkIndex") ||
                form.Contains("_lastSmallTalkUtc") ||
                animation.Contains("TryShowSmallTalk"),
                "PetForm must not retain a second SmallTalk runtime state.");
            Assert.IsTrue(coordinator.Contains(
                    "PetSmallTalkPolicy.IsWindowExpired") &&
                coordinator.Contains("PetSmallTalkPolicy.ShouldSpeak") &&
                coordinator.Contains("PetMessagePolicy.ShouldSuppress") &&
                coordinator.Contains("if (!_show(") &&
                coordinator.Contains("_loopableQuotaRemaining--") &&
                coordinator.Contains("TryUseMeaningful"),
                "The coordinator must own eligibility, selection and accepted state.");
            Assert.IsFalse(coordinator.Contains("PetForm") ||
                coordinator.Contains("PetBubbleCoordinator") ||
                coordinator.Contains("System.Windows.Forms") ||
                coordinator.Contains("KeyboardOverlayForm"),
                "SmallTalk runtime must remain independent of Windows UI details.");
        }

        [TestMethod]
        public void AlmanacDailyContent_IsNarrowDeterministicAndIntegrated()
        {
            string calculator = ReadSource(
                "Core/Calendar/Almanac/AlmanacCalculator.cs");
            string semantic = ReadSource(
                "Core/DailyContent/Almanac/AlmanacSemanticCatalog.cs");
            string selector = ReadSource(
                "Core/DailyContent/Almanac/AlmanacDailySelector.cs");
            string wording = ReadSource(
                "Core/DailyContent/Almanac/AlmanacWordingCatalog.cs");
            string content = ReadSource(
                "Core/DailyContent/DailyBriefingContent.cs");
            string composer = ReadSource(
                "Core/DailyContent/DailyBriefingComposer.cs");
            string coordinator = ReadSource("PetDailyContentCoordinator.cs");
            string form = ReadSource("PetForm.cs");
            string settingsForm = ReadSource("DailyContentSettingsForm.cs");
            string settings = ReadSource(
                "Core/Settings/PetSettingsData.cs");
            string commands = ReadSource(
                "Infrastructure/SelfTestCommandRouter.cs");
            string resolver = ReadSource(
                "Infrastructure/EmbeddedAssemblyResolver.cs");
            string coreProject = ReadSource("PennyPet.Core.csproj");
            string windowsProject = ReadSource("PennyPet.Windows.csproj");
            string notices = ReadSource("../THIRD_PARTY_NOTICES.md");

            Assert.IsTrue(calculator.Contains("Solar.FromYmdHms(") &&
                calculator.Contains("localNow.Year") &&
                calculator.Contains("localNow.Month") &&
                calculator.Contains("localNow.Day") &&
                calculator.Contains("GetDayYi(1)") &&
                calculator.Contains("GetDayJi(1)"),
                "The adapter must use local civil date and explicit sect 1.");
            Assert.IsFalse(calculator.Contains(".DayYi") ||
                calculator.Contains(".DayJi") ||
                calculator.Contains("UtcDateTime"),
                "The adapter must not use implicit sect or UTC date.");
            Assert.IsTrue(semantic.Contains("TryGetValue") &&
                selector.Contains("YiJiConflict") &&
                selector.Contains("StringComparer.Ordinal") &&
                content.Contains("AlmanacDailySelection Almanac") &&
                composer.Contains("content.Almanac") &&
                coordinator.Contains("AlmanacCalculator.Calculate") &&
                coordinator.Contains("AlmanacDailySelector.Select"),
                "Raw terms must cross the whitelist before the shared budget.");
            Assert.IsFalse(semantic.Contains("Contains(\"") ||
                selector.Contains("Random") ||
                selector.Contains("GetHashCode") ||
                selector.Contains("PetSettings") ||
                selector.Contains("System.Windows.Forms") ||
                semantic.Contains("Lunar.") || selector.Contains("Lunar.") ||
                wording.Contains("Lunar."),
                "Selection must use exact mapping and remain platform neutral.");
            Assert.IsTrue(coreProject.Contains(
                    "Include=\"lunar-csharp\" Version=\"1.6.8\"") &&
                windowsProject.Contains(
                    "Include=\"lunar-csharp\" Version=\"1.6.8\"") &&
                windowsProject.Contains("PennyPet.Dependencies.lunar.dll") &&
                resolver.Contains("LunarAssemblyName = \"lunar\"") &&
                resolver.Contains("LunarResourceName") &&
                notices.Contains("## lunar-csharp") &&
                notices.Contains("Version: `1.6.8`"),
                "Package, notice and single-file allowlist must be exact.");
            Assert.IsTrue(commands.Contains("--almanac-probe=") &&
                commands.Contains("--daily-briefing-probe="),
                "Both pure diagnostic seams must remain available.");
            Assert.IsFalse(
                semantic.Contains("Provider") ||
                selector.Contains("Manager") || selector.Contains("Engine"),
                "Almanac must remain narrow and avoid speculative framework.");
            Assert.IsTrue(
                settings.Contains("AlmanacEnabled") &&
                settingsForm.Contains("传统黄历（民俗）"),
                "Almanac preference must be wired into settings and UI.");
        }

        [TestMethod]
        public void WeatherDailyContent_UsesOptInAsyncBoundedInfrastructure()
        {
            string meaning = ReadSource(
                "Core/DailyContent/Weather/WeatherMeaningRules.cs");
            string wording = ReadSource(
                "Core/DailyContent/Weather/WeatherWordingCatalog.cs");
            string source = ReadSource(
                "Infrastructure/Weather/PetWeatherSource.cs");
            string geocoding = ReadSource(
                "Infrastructure/Weather/OpenMeteoGeocodingClient.cs");
            string client = ReadSource(
                "Infrastructure/Weather/OpenMeteoForecastClient.cs");
            string coordinator = ReadSource("PetDailyContentCoordinator.cs");
            string preferences = ReadSource(
                "Core/DailyContent/DailyContentPreferencesSnapshot.cs");
            string animation = ReadSource("PetAnimationRuntime.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");
            string commands = ReadSource(
                "Infrastructure/SelfTestCommandRouter.cs");
            string coreProject = ReadSource("PennyPet.Core.csproj");
            string resolver = ReadSource(
                "Infrastructure/EmbeddedAssemblyResolver.cs");

            Assert.IsFalse(meaning.Contains("HttpClient") ||
                meaning.Contains("https://") || wording.Contains("Random") ||
                wording.Contains("GetHashCode"),
                "Weather meaning and wording must remain deterministic Core rules.");
            Assert.IsTrue(source.Contains("new HttpClient(") &&
                source.Contains("TimeSpan.FromSeconds(3)") &&
                source.Contains("TimeSpan.FromSeconds(8)") &&
                source.Contains("CancellationTokenSource.CreateLinkedTokenSource") &&
                source.Contains("FailureCooldown") &&
                source.Contains("TimeSpan.FromMinutes(15)") &&
                source.Contains("Queue<string>") &&
                source.Contains("_cacheOrder.Count >= 3") &&
                source.Contains("_inFlightKey == key"),
                "Weather transport must own one bounded cache/in-flight/cooldown.");
            Assert.IsTrue(geocoding.Contains("CancellationToken") &&
                geocoding.Contains("HttpCompletionOption.ResponseContentRead") &&
                geocoding.Contains("EnsureSuccessStatusCode"),
                "Geocoding must accept an explicit per-request cancellation deadline.");
            Assert.IsTrue(client.Contains("past_days=1") &&
                client.Contains("forecast_days=2") &&
                client.Contains("temperature_2m") &&
                client.Contains("apparent_temperature") &&
                client.Contains("precipitation_probability") &&
                client.Contains("precipitation\"") &&
                client.Contains("snowfall") &&
                client.Contains("weather_code") &&
                client.Contains("wind_speed_10m") &&
                client.Contains("wind_gusts_10m") &&
                !client.Contains("apikey"),
                "Forecast request must keep the reviewed eight-variable shape.");
            string poke = Between(animation,
                "private async void HandlePetPoked",
                "private void StartOrdinaryPokeAnimation");
            Assert.IsTrue(poke.IndexOf("StartNotificationPokeAnimation(nowUtc)",
                    StringComparison.Ordinal) <
                poke.IndexOf(".HandlePetPokedAsync", StringComparison.Ordinal) &&
                coordinator.Contains("await _weatherForecast") &&
                coordinator.Contains("WeatherMeaningRules.Select") &&
                coordinator.Contains("WeatherWordingCatalog.Select") &&
                !poke.Contains(".Wait(") && !poke.Contains(".Result") &&
                !coordinator.Contains(".Wait(") &&
                !coordinator.Contains(".Result"),
                "Poke animation must start before the asynchronous weather path.");
            Assert.IsTrue(preferences.Contains(
                    "sealed class DailyContentPreferencesSnapshot") &&
                preferences.Contains("WeatherLocation WeatherLocation") &&
                preferences.Contains("ZodiacSign ZodiacSign") &&
                preferences.Contains("int BirthdayMonth") &&
                preferences.Contains("int BirthdayDay") &&
                preferences.Contains("string LastBriefingDate") &&
                coordinator.Contains(
                    "DailyContentPreferencesSnapshot preferences = _preferences();") &&
                !coordinator.Contains("private readonly Func<ZodiacSign>") &&
                animation.Contains("private async Task HandlePetPokedAsync()") &&
                animation.Contains(
                    "ApplicationDiagnostics.ReportNonFatal(\"pet-poke\", error)"),
                "One immutable preference snapshot must span each async attempt and the UI boundary must observe failures.");
            Assert.IsFalse(startup.Contains("GetForecastAsync") ||
                startup.Contains("SearchLocationsAsync"),
                "Startup must make zero weather requests.");
            string locationDialog = ReadSource("WeatherLocationDialog.cs");
            Assert.IsTrue(locationDialog.Contains("requestedQuery") &&
                locationDialog.Contains("currentQuery") &&
                locationDialog.Contains("搜索内容已变化") &&
                locationDialog.Contains("_searchCancellation"),
                "Weather location search must snapshot the query and cancel on close.");
            Assert.IsFalse(locationDialog.Contains("QueryKeyDown") ||
                locationDialog.Contains("_query.Enabled = false") ||
                locationDialog.Contains("_query.Focus()") ||
                locationDialog.Contains("AcceptButton = _ok") ||
                locationDialog.Contains("TopMost = true"),
                "Weather search must not steal IME Enter or focus.");
            Assert.IsTrue(commands.Contains("--weather-api-probe=") &&
                !coreProject.Contains("System.Net.Http") &&
                !resolver.Contains("System.Net.Http"),
                "Live probing stays explicit and no new managed dependency is embedded.");
        }

        [TestMethod]
        public void OwnedModalWindows_UseSharedLayerBoundaryWithoutSuppressingKeys()
        {
            string layers = ReadSource("PetWindowLayerCoordinator.cs");
            string menu = ReadSource("PetMenuActions.cs");
            string settings = ReadSource("DailyContentSettingsForm.cs");
            string reminders = ReadSource("PetReminderWindowsCoordinator.cs");
            string sticky = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string bubble = ReadSource("PetBubbleCoordinator.cs");
            string keyboard = ReadSource(
                "Features/KeyboardOverlay/PetKeyboardOverlayCoordinator.cs");
            string overlay = ReadSource(
                "Features/KeyboardOverlay/KeyboardOverlayForm.cs");
            string dialog = ReadSource("WeatherLocationDialog.cs");

            Assert.IsTrue(layers.Contains("List<Form> _modalStack") &&
                layers.Contains("DialogResult ShowModal") &&
                layers.Contains("ModalZOrderFloor") &&
                layers.Contains("KeepTransientBelowModal") &&
                layers.Contains("SetWindowPos(transient.Handle, floor.Handle") &&
                layers.Contains("SwpNoActivate") &&
                layers.Contains("finally") &&
                layers.Contains("_modalStack.Remove(dialog)"),
                "Pet modal ownership and transient z-order must share one bounded runtime stack.");
            Assert.IsTrue(menu.Contains(
                    "_windowLayers.ShowModal(this, dialog)") &&
                settings.Contains(
                    "_windowLayers.ShowModal(this, dialog)") &&
                reminders.Contains(
                    "_windowLayers.ShowModal(this, dialog)") &&
                sticky.Contains(
                    "_windowLayers.ShowModal(this, manager)") &&
                !menu.Contains("ShowOwnedModalDialog") &&
                !menu.Contains("_ownedModalUi"),
                "Pet-owned Form dialogs, including nested weather settings, must use the shared layer boundary.");
            Assert.IsTrue(keyboard.Contains("_windowLayers.HasActiveModal") &&
                keyboard.Contains("HasFocusedOwnNoteTextInput() ||") &&
                keyboard.Contains("ShowKeyRepeatCount(this, displayText") &&
                keyboard.Contains("_keyOverlay.UpdatePosition(this)") &&
                keyboard.Contains(
                    "_windowLayers.KeepTransientBelowModal(_keyOverlay)") &&
                keyboard.Contains(
                    "_windowLayers.KeepTransientBelowModal(_leftNoteTabs)") &&
                bubble.Contains("ApplyWindowLayer()") &&
                bubble.Contains(
                    "_windowLayers.KeepTransientBelowModal(_bubble)") &&
                keyboard.Contains("SensitiveInputDetector.IsSensitiveFocus") &&
                !keyboard.Contains("ModalAvoidanceBounds") &&
                !overlay.Contains("avoidBounds"),
                "Pet chrome must stay below modal windows without moving keyboard hints away from the Pet.");
            Assert.IsTrue(dialog.Contains("FormattingEnabled = true") &&
                dialog.Contains("ClientSize = new Size(410, 255)") &&
                dialog.Contains("_results.Size = new Size(364, 96)"),
                "Weather results must use their display projection in a compact window.");
        }

        [TestMethod]
        public void PetMouseDown_OnlyClosesHoverBubble()
        {
            string animation = ReadSource("PetAnimationRuntime.cs");
            string mouseDown = Between(animation,
                "private void PetMouseDown",
                "private void PetMouseMove");
            string bubble = ReadSource("PetBubbleCoordinator.cs");
            string form = ReadSource("PetForm.cs");
            string hover = ReadSource("PetHoverRuntime.cs");

            Assert.IsTrue(mouseDown.Contains(
                    "_hoverSuppressedUntilStableLeave = true") &&
                mouseDown.Contains("HideHoverBubble()"),
                "Mouse-down must end the ambient Hover session.");
            Assert.IsFalse(mouseDown.Contains(
                "CloseCurrentBubbleWithoutRestoringHover"),
                "Mouse-down must not close foreground user messages.");
            Assert.IsTrue(hover.Contains(
                    "_hoverSuppressedUntilStableLeave = false") &&
                hover.Contains("CommitStableLeave") &&
                bubble.Contains(
                    "PetHoverStabilityRules.ShouldSuppressHover"),
                "Stable leave must release the latch and Hover requests must honor it.");
        }

        [TestMethod]
        public void StickyUiHosted_ExternalCloseUsesAsyncFinalSnapshotProtocol()
        {
            string form = ReadSource("PetForm.cs");
            string closing = Between(form,
                "protected override void OnFormClosing",
                "protected override void OnFormClosed");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(closing.Contains("e.Cancel = true") &&
                closing.Contains("BeginHostedStickyExitIfNeeded()"),
                "External close must pause before PetForm disposal.");
            int apply = coordinator.IndexOf(
                "ApplyHostedStickySnapshot(\n" +
                "                                finalSnapshot.Snapshot",
                StringComparison.Ordinal);
            int prepared = coordinator.IndexOf(
                "_hostedRuntime.PrepareExit()", StringComparison.Ordinal);
            Assert.IsTrue(apply >= 0 && prepared > apply,
                "Final snapshot must reach the canonical owner before close resumes.");
        }
    }
}
