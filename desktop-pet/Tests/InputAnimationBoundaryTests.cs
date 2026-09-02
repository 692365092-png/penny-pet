using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    // These tests inspect source structure and architectural boundaries.
    // They do not prove runtime WPF/WinForms/IME/Dock behavior.
    [TestCategory("ArchitectureSourceBoundary")]
    [TestClass]
    public sealed class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void StartupLoading_UsesBootstrapOnlyEmbeddedVisual()
        {
            string loading = ReadSource("StartupLoadingForm.cs");
            string loadingThread = ReadSource(
                "StartupLoadingThreadHost.cs");
            string host = ReadSource("PennyApplicationHost.cs");
            string animation = ReadSource("PetAnimationRuntime.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");

            Assert.IsTrue(loading.Contains("PennyPet.Startup.Loading") &&
                loading.Contains("GetManifestResourceStream(ResourceName)") &&
                loading.Contains("PetSettingRules.NormalizePetScalePercent") &&
                loading.Contains("CalculateImageBounds(source.Size, size)") &&
                loading.Contains("canvas.Height - height") &&
                loading.Contains("graphics.Clear(Color.Transparent)"),
                "Loading must use its embedded asset on a bottom-aligned proportional Pet canvas.");
            Assert.IsFalse(loading.Contains("PetArtPackage") ||
                loading.Contains("StickyUiHost") ||
                loading.Contains("StickyUiThreadHost") ||
                loading.Contains("StickyNoteRepository") ||
                loading.Contains("StickyHostedRuntime") ||
                loading.Contains("WpfApplicationHost") ||
                loading.Contains("PetForm."),
                "Bootstrap loading must not depend on runtime art or sticky state.");
            int showLoading = host.IndexOf("loading.Start(preloadedSettings);",
                StringComparison.Ordinal);
            int constructPet = host.IndexOf("new PetForm(preloadedSettings)",
                StringComparison.Ordinal);
            Assert.IsTrue(showLoading >= 0 && constructPet > showLoading,
                "The loading form must be shown before PetForm construction.");
            Assert.IsTrue(loadingThread.Contains("new Thread(") &&
                loadingThread.Contains(
                    "SetApartmentState(ApartmentState.STA)") &&
                loadingThread.Contains("IsBackground = true") &&
                loadingThread.Contains("Application.Run(form)") &&
                loadingThread.Contains("form.BeginInvoke(") &&
                loadingThread.Contains("_ready.Set()"),
                "Loading must own a responsive message loop on a dedicated STA.");
            Assert.IsFalse(loadingThread.Contains("new PetForm") ||
                loadingThread.Contains("PetArtPackage") ||
                loadingThread.Contains("StickyUiHost") ||
                loadingThread.Contains("StickyNoteRepository"),
                "The loading thread must own only bootstrap presentation.");
            string closeLoading = Between(loadingThread,
                "internal void Close()", "internal void BringToFront()");
            string postLoading = Between(loadingThread,
                "private void Post(", "public void Dispose()");
            Assert.IsTrue(closeLoading.Contains("Post(") &&
                postLoading.Contains("form.BeginInvoke(") &&
                loadingThread.Contains("_exited.WaitOne()"),
                "Close must marshal to loading STA and disposal must await exit.");
            Assert.IsTrue(host.Contains("pet.StartupReady += delegate") &&
                host.Contains("loading.Close();") &&
                host.Contains("loading.BringToFront();") &&
                !host.Contains("Application.DoEvents()"),
                "StartupReady must close loading without DoEvents workarounds.");
            int releaseFrame = startup.IndexOf(
                "_startupDisplaySuppressed = false;", StringComparison.Ordinal);
            int renderFrame = startup.IndexOf("RenderCurrentFrame();",
                StringComparison.Ordinal);
            Assert.IsTrue(animation.Contains(
                    "if (_startupDisplaySuppressed || !IsHandleCreated") &&
                startup.Contains("_startupUiReady, _startupArtReady") &&
                releaseFrame >= 0 && renderFrame > releaseFrame,
                "Normal Pet frames must remain suppressed until startup readiness.");
        }

        [TestMethod]
        public void Autosave_DoesNotRefreshSideTabs()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source,
                "if (value.Kind == StickyUiEventKind.SnapshotChanged)",
                "if (value.Kind == StickyUiEventKind.Closed)");
            string apply = Between(source,
                "private bool ApplyHostedStickySnapshot",
                "private void ClearHostedDockResizeSession");

            Assert.IsTrue(handler.Contains("ApplyHostedStickySnapshot(") &&
                apply.Contains("if (persist) _notes.SaveAsync();"),
                "NoteChanged must persist note data.");
            Assert.IsTrue(apply.Contains("RefreshMenuText();"),
                "NoteChanged must refresh menu text.");
            Assert.IsTrue(apply.Contains("if (visibilityChanged ||") &&
                apply.Contains("RefreshNoteTabs();"),
                "Content autosave must refresh tabs only for visibility or hidden-title changes.");
        }

        [TestMethod]
        public void SideTabs_KeepTopMostAndOnlyRebuildForSplitChanges()
        {
            string source = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string zOrder = Between(source, "private void ApplyNoteTabZOrder",
                "private void PositionNoteTabs");
            string position = Between(source, "private void PositionNoteTabs",
                "private void ShowStickyNotesManager");
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(zOrder.Contains(".TopMost =") &&
                zOrder.Contains("BringToFront()") &&
                !zOrder.Contains("RaiseVisibleNotesAboveTabs") &&
                !zOrder.Contains("_noteWindows") &&
                !zOrder.Contains("_notes.GetAll"),
                "Side-tab chrome must own its TopMost policy without legacy Window routing.");
            Assert.IsTrue(tabs.Contains("ShowWithoutActivation") &&
                tabs.Contains("WS_EX_NOACTIVATE"),
                "Stable TopMost tabs must remain non-activating.");
            Assert.IsTrue(position.Contains("IsLayoutSplitCurrent") &&
                position.Contains("_noteTabsSignature = String.Empty") &&
                position.Contains("RefreshNoteTabs();") &&
                position.Contains("ShowNear(Bounds, work)"),
                "Positioning must rebuild only an invalid split and otherwise reposition.");
            Assert.IsTrue(form.Contains("WmSettingChange") &&
                form.Contains("WmDisplayChange") &&
                form.Contains("BeginInvoke(new Action(PositionNoteTabs))") &&
                tabs.Contains("TopMost = true") &&
                tabs.Contains("BringToFront()") &&
                !tabs.Contains("Activate()"),
                "Display/work-area changes must revalidate non-activating tab chrome.");
        }

        [TestMethod]
        public void OwnNoteTyping_StillTriggersAnimation()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source,
                "if (value.Kind == StickyUiEventKind.TypingActivity)",
                "if (value.Kind == StickyUiEventKind.InputFocusChanged)");

            Assert.IsTrue(handler.Contains("TriggerTypingAnimation();"),
                "Own-note typing should restore the typing animation.");
            Assert.IsFalse(handler.Contains("_typingSession = false"),
                "Own-note typing must not clear the animation session.");
        }

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
        public void LayeredRenderer_DoesNotKeepGlobalBitmapHandleCache()
        {
            string source = ReadSource("LayeredSpriteRenderer.cs");
            Assert.IsFalse(source.Contains("Dictionary<Bitmap, IntPtr>") ||
                source.Contains("BitmapHandles"),
                "Layered renderer must not keep a global HBITMAP cache.");
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
        public void StickyPersistence_AllWritersShareGenerationCheckedIoGate()
        {
            string source = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string synchronous = Between(source,
                "internal PersistenceResult SaveToFile",
                "internal PersistenceResult ExportSnapshot");
            string asynchronous = Between(source, "private void AsyncWriterLoop",
                "internal PersistenceResult SaveToFile");
            string physicalWrite = Between(source,
                "private PersistenceResult WriteSnapshot",
                "internal static bool RepairForDisplay");

            Assert.IsFalse(synchronous.Contains("WaitForPendingSaves();"),
                "Synchronous saves must not rely on a race-prone wait-before-write.");
            Assert.IsTrue(synchronous.Contains("WriteSnapshot(filePath, snapshot,"),
                "Synchronous saves must use the shared physical writer.");
            Assert.IsTrue(asynchronous.Contains("WriteSnapshot(_filePath, snapshot,"),
                "Asynchronous saves must use the shared physical writer.");
            int ioGate = physicalWrite.IndexOf("lock (_ioGate)",
                StringComparison.Ordinal);
            int generationCheck = physicalWrite.IndexOf(
                "generation < _lastWrittenGeneration", StringComparison.Ordinal);
            int diskWrite = physicalWrite.IndexOf("AtomicTextFile.WriteAllLines",
                StringComparison.Ordinal);
            Assert.IsTrue(ioGate >= 0 && generationCheck > ioGate &&
                diskWrite > generationCheck,
                "Generation must be checked after winning the IO gate and before disk write.");
        }

        [TestMethod]
        public void StickyUiHost_CommandBoundaryIsAsynchronous()
        {
            string host = ReadSource("StickyUiHost.cs");
            string threadHost = ReadSource("StickyUiThreadHost.cs");

            Assert.IsTrue(host.Contains("PostCommand(") &&
                host.Contains("_threadHost.Post("),
                "Sticky UI commands must use the asynchronous post boundary.");
            Assert.IsTrue(threadHost.Contains("dispatcher.BeginInvoke("),
                "Sticky UI commands must be dispatched asynchronously.");
            Assert.IsFalse(threadHost.Contains("dispatcher.Invoke("),
                "Pet UI must never synchronously invoke the sticky STA.");
            Assert.IsFalse(host.Contains("SendCommand(") ||
                threadHost.Contains("SendCommand("),
                "The old synchronous command API must not remain available.");
        }

        [TestMethod]
        public void StickyUiHosted_UsesDetachedTypedOwnershipBoundary()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string pet = ReadSource("PetForm.cs");
            string runtime = ReadSource(
                "Features/StickyNotes/StickyHostedRuntime.cs");

            Assert.IsTrue(commands.Contains("StickyNoteUiSnapshot") &&
                commands.Contains("CreateWorkingCopy()") &&
                commands.Contains("StickyUiEventKind"),
                "Hosted notes must exchange typed values and detached copies.");
            Assert.IsTrue(host.Contains(
                "Dictionary<string, StickyWindowSession> _sessions") &&
                session.Contains("private readonly StickyNoteWindow _window") &&
                session.Contains("private long _sequence") &&
                session.Contains("snapshot.CreateWorkingCopy()"),
                "Only sticky STA sessions may own hosted WPF windows.");
            Assert.IsTrue(coordinator.Contains(
                "StickyNoteUiSnapshot.FromData(note)") &&
                coordinator.Contains("snapshot.ApplyTo(canonical)") &&
                pet.Contains("StickyHostedRuntime _hostedRuntime") &&
                runtime.Contains("Dictionary<string, long> _appliedSequences"),
                "Pet must apply each note using an independent sequence.");
        }

        [TestMethod]
        public void StickyUiHosted_DoesNotSynchronouslyWaitAcrossUiThreads()
        {
            string threadHost = ReadSource("StickyUiThreadHost.cs");
            string posted = Between(threadHost, "internal void Post(",
                "internal void StopAcceptingCommands");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsFalse(posted.Contains("Dispatcher.Invoke") ||
                posted.Contains(".Wait(") || posted.Contains(".Result"),
                "Command dispatch must never synchronously wait on the sticky STA.");
            Assert.IsFalse(coordinator.Contains("Control.Invoke") ||
                coordinator.Contains("Dispatcher.Invoke") ||
                coordinator.Contains("Task.Wait") ||
                coordinator.Contains("Task.Result"),
                "Pet-side hosted coordination must stay fully asynchronous.");
        }

        [TestMethod]
        public void StickyUiHosted_ReportsInputFocusForOverlayPrivacy()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string overlay = ReadSource(
                "Features/KeyboardOverlay/PetKeyboardOverlayCoordinator.cs");
            string hook = ReadSource(
                "Features/KeyboardOverlay/GlobalKeyboardActivity.cs");

            Assert.IsTrue(commands.Contains("InputFocusChanged") &&
                session.Contains("_window.HasFocusedTextInput") &&
                coordinator.Contains(
                    "_hostedRuntime.SetInputFocus(value.NoteId, value.Flag)"),
                "Sticky STA must asynchronously report a plain focus flag.");
            Assert.IsTrue(overlay.Contains(
                    "ShouldSuppressOwnApplicationInput(focusSnapshot)") &&
                overlay.Contains("HasFocusedOwnNoteTextInput() ||") &&
                overlay.Contains("_windowLayers.HasActiveModal") &&
                overlay.Contains("focusSnapshot.ProcessId ==") &&
                overlay.Contains(
                    "PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput"),
                "Only Sticky text input and an active owned modal may allow own-process overlay input.");
            Assert.IsTrue(hook.Contains("ShouldPublishKey(injected)") &&
                !hook.Contains("ownProcessId") &&
                !hook.Contains("foregroundProcessId"),
                "The hook must capture physical own-process keys for policy evaluation.");
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
                poke.Contains("_dailyContentCoordinator") &&
                poke.Contains(".HandlePetPokedAsync") &&
                poke.Contains("if (!dailyHandled)") &&
                poke.Contains("_smallTalkCoordinator.HandlePetPoked(nowUtc)") &&
                !poke.Contains(".Wait(") && !poke.Contains(".Result"),
                "PetForm must preserve Easter, Daily, SmallTalk, animation order.");
            Assert.IsFalse(form.Contains("SmallTalkPhrases") ||
                form.Contains("_smallTalkRandom") ||
                form.Contains("_lastSmallTalkIndex") ||
                form.Contains("_lastSmallTalkUtc") ||
                animation.Contains("TryShowSmallTalk"),
                "PetForm must not retain a second SmallTalk runtime state.");
            Assert.IsTrue(coordinator.Contains("DefaultPhrases") &&
                coordinator.Contains("PetSmallTalkPolicy.ShouldAttempt") &&
                coordinator.Contains("PetMessagePolicy.ShouldSuppress") &&
                coordinator.Contains("if (!_show(") &&
                coordinator.Contains("_lastShownUtc = nowUtc"),
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
                content.Contains("AlmanacLine") &&
                composer.Contains("content.AlmanacLine") &&
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
            string client = ReadSource(
                "Infrastructure/Weather/OpenMeteoForecastClient.cs");
            string coordinator = ReadSource("PetDailyContentCoordinator.cs");
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
                source.Contains("FailureCooldown") &&
                source.Contains("TimeSpan.FromMinutes(15)") &&
                source.Contains("Queue<string>") &&
                source.Contains("_cacheOrder.Count >= 3") &&
                source.Contains("_inFlightKey == key"),
                "Weather transport must own one bounded cache/in-flight/cooldown.");
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
            Assert.IsTrue(poke.IndexOf("StartOrdinaryPokeAnimation(nowUtc)",
                    StringComparison.Ordinal) <
                poke.IndexOf(".HandlePetPokedAsync", StringComparison.Ordinal) &&
                coordinator.Contains("await _weatherForecast") &&
                coordinator.Contains("WeatherMeaningRules.Select") &&
                coordinator.Contains("WeatherWordingCatalog.Select") &&
                !poke.Contains(".Wait(") && !poke.Contains(".Result") &&
                !coordinator.Contains(".Wait(") &&
                !coordinator.Contains(".Result"),
                "Poke animation must start before the asynchronous weather path.");
            Assert.IsFalse(startup.Contains("GetForecastAsync") ||
                startup.Contains("SearchLocationsAsync"),
                "Startup must make zero weather requests.");
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

            Assert.IsTrue(mouseDown.Contains(
                "_bubbleCoordinator.IsCurrent(PetMessageKind.Hover)") &&
                mouseDown.Contains(
                    "_bubbleCoordinator.CloseIfCurrent(PetMessageKind.Hover)"),
                "Mouse-down may close only the ambient Hover bubble.");
            Assert.IsFalse(mouseDown.Contains(
                "CloseCurrentBubbleWithoutRestoringHover"),
                "Mouse-down must not close foreground user messages.");
        }

        [TestMethod]
        public void PetLocationChange_RepositionsCurrentBubble()
        {
            string form = ReadSource("PetForm.cs");
            int locationChanged = form.IndexOf("LocationChanged += delegate",
                StringComparison.Ordinal);
            int sizeChanged = form.IndexOf("SizeChanged += delegate",
                locationChanged, StringComparison.Ordinal);
            string body = form.Substring(locationChanged,
                sizeChanged - locationChanged);

            Assert.IsTrue(body.Contains("RepositionCurrentBubble()"),
                "Pet movement must reposition rather than close its Bubble.");
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

        [TestMethod]
        public void StickyUiRegistry_CloseAllPreflightsImeAndSuppressesEvents()
        {
            string host = ReadSource("StickyUiHost.cs");
            int preflight = host.IndexOf(
                "session.IsImeCompositionActive",
                StringComparison.Ordinal);
            int batch = host.IndexOf("session.SetEventsSuppressed(true)",
                StringComparison.Ordinal);

            Assert.IsTrue(preflight >= 0 && batch > preflight &&
                host.Contains("session.FlushAndCaptureFinal()") &&
                host.Contains("StickyUiFinalSnapshot"),
                "CloseAll must preflight every IME before a quiet final batch.");
        }

        [TestMethod]
        public void StickyUiRegistry_DeletesOnlyAfterHandledCloseAndForwardsRequests()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string dock = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            int handled = dock.IndexOf(
                "result.Status != StickyUiCommandStatus.Handled",
                StringComparison.Ordinal);
            int remove = dock.IndexOf("_notes.Remove(note)",
                StringComparison.Ordinal);

            Assert.IsTrue(commands.Contains("DeleteRequested") &&
                commands.Contains("NewNoteRequested") &&
                commands.Contains("NewTodoRequested") &&
                commands.Contains("NewScheduleRequested") &&
                session.Contains("_window.DeleteRequested +=") &&
                session.Contains("RaiseRequest("),
                "Window-level application requests must cross as typed events.");
            Assert.IsTrue(handled >= 0 && remove > handled,
                "Canonical deletion must happen only after handled close.");
        }

        [TestMethod]
        public void StickyUiHosted_FinalArchitectureHasSingleWindowExecutor()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string form = ReadSource("PetForm.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dock = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string persistence = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string reminder = ReadSource("PetReminderWindowsCoordinator.cs");
            string menu = ReadSource("PetMenuActions.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string codec = ReadSource("Core/StickyNotes/StickyNoteCodec.cs");
            string petOwned = startup + form + coordinator + dock +
                persistence + reminder + menu;

            Assert.IsTrue(startup.Contains("ShowHostedSticky(") &&
                coordinator.Contains("StartHostedSticky(") &&
                host.Contains("Dictionary<string, StickyWindowSession> _sessions") &&
                session.Contains("new StickyNoteWindow("),
                "Startup and creation must route through the hosted session executor.");
            Assert.IsFalse(petOwned.Contains("_noteWindows") ||
                petOwned.Contains("GetOrCreateStickyNoteWindow") ||
                petOwned.Contains("FallBackHostedStickyToLegacy") ||
                petOwned.Contains("RestoreStickyDockComponent") ||
                petOwned.Contains("new StickyNoteWindow("),
                "PetForm must not retain a legacy Sticky Window executor.");
            Assert.IsFalse(host.Contains("StickyNoteRepository") ||
                host.Contains("IsTodoList") || host.Contains("IsSchedule"),
                "The host must own sessions, not canonical persistence or content modes.");
            Assert.IsTrue(codec.Contains("versionOne") &&
                codec.Contains("versionNine"),
                "Removing the executor must retain legacy persistence readers.");
        }

        [TestMethod]
        public void StickyDock_UsesDetachedFactsAndTypedHostedEffectBoundary()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(form.Contains("_activeDockGroupIds") &&
                form.Contains("_activeDockCurrentFacts") &&
                coordinator.Contains("CalculateDockTranslationTargets") &&
                coordinator.Contains("ApplyDockTargets"),
                "Dock session geometry must be note-id/facts based.");
            Assert.IsFalse(coordinator.Contains("Object.ReferenceEquals") ||
                coordinator.Contains("member.Location ="),
                "Dock decisions and group motion must not use Window/model identity.");
        }

        [TestMethod]
        public void StartupRestore_TracksExpectedAndRenderedNoteIds()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(startup.Contains(
                "_expectedFirstRenderNoteIds.Clear()"),
                "Startup restore must reset expected first-render ids.");
            Assert.IsTrue(startup.Contains(
                "_renderedFirstRenderNoteIds.Clear()"),
                "Startup restore must reset rendered first-render ids.");
            Assert.IsTrue(startup.Contains(
                "_expectedFirstRenderNoteIds.Add(member.Id)"),
                "Startup restore must expect each restored note member.");
            Assert.IsTrue(startup.Contains(
                "AllExpectedNotesHaveFirstRendered()"),
                "Startup completion must wait on the expected set.");
            Assert.IsTrue(form.Contains("_expectedFirstRenderNoteIds") &&
                form.Contains("_renderedFirstRenderNoteIds"),
                "PetForm must own the startup readiness id sets.");
        }

        [TestMethod]
        public void HostedRuntime_ConsolidatesOnlyPetThreadProtocolState()
        {
            string form = ReadSource("PetForm.cs");
            string runtime = ReadSource(
                "Features/StickyNotes/StickyHostedRuntime.cs");

            Assert.IsTrue(form.Contains(
                "StickyHostedRuntime _hostedRuntime") &&
                form.Contains("_expectedFirstRenderNoteIds") &&
                form.Contains("_renderedFirstRenderNoteIds"),
                "Hosted runtime must not absorb shared startup readiness state.");
            Assert.IsFalse(form.Contains("_hostedNoteIds") ||
                form.Contains("_hostedAppliedSequences") ||
                form.Contains("_hostedImeComposing") ||
                form.Contains("_hostedInputFocused") ||
                form.Contains("_hostedDeletePending") ||
                form.Contains("_hostedExitRequested") ||
                form.Contains("_hostedCloseAllInFlight") ||
                form.Contains("_hostedExitPrepared"),
                "PetForm must not scatter hosted protocol state.");
            Assert.IsTrue(runtime.Contains("_noteIds") &&
                runtime.Contains("_appliedSequences") &&
                runtime.Contains("_imeComposing") &&
                runtime.Contains("_inputFocused") &&
                runtime.Contains("_deletePending") &&
                runtime.Contains("ExitRequested") &&
                runtime.Contains("CloseAllInFlight") &&
                runtime.Contains("ExitPrepared"),
                "Runtime must own hosted membership, sequence, input and exit state.");
            Assert.IsFalse(runtime.Contains("StickyNoteWindow") ||
                runtime.Contains("StickyNoteRepository"),
                "Hosted runtime must not own WPF windows or persistence.");
        }

        [TestMethod]
        public void HostedFirstRendered_UpdatesPetReadiness()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator =
                ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string command = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");

            Assert.IsTrue(command.Contains("FirstRendered"),
                "StickyUiEventKind must include FirstRendered.");
            Assert.IsTrue(session.Contains(
                "StickyUiEventKind.FirstRendered"),
                "StickyUiHost must emit FirstRendered.");
            Assert.IsTrue(coordinator.Contains(
                "MarkFirstRendered(value.NoteId)"),
                "Pet coordinator must mark hosted first render.");
        }

        [TestMethod]
        public void HostedCreateFailure_PreservesDataWithoutLegacyFallback()
        {
            string coordinator =
                ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string fallback = Between(coordinator,
                "private void HandleHostedStickyFailure",
                "private static void ReportHostedStickyCommandFailure");

            Assert.IsTrue(fallback.Contains("note.Visible = false") &&
                fallback.Contains("_notes.SaveAsync();") &&
                fallback.Contains("RefreshNoteTabs();"),
                "A failed hosted window must keep canonical content accessible in Side Tabs.");
            Assert.IsFalse(fallback.Contains("GetOrCreateStickyNoteWindow") ||
                fallback.Contains("ShowStickyNote("),
                "Hosted failure must not silently reintroduce the legacy executor.");
        }

        [TestMethod]
        public void SideTabs_UseTypedSnapshotAndNoteIdActions()
        {
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string snapshots = ReadSource(
                "Features/StickyNotes/SideTabSnapshot.cs");

            Assert.IsTrue(snapshots.Contains("NoteId") &&
                snapshots.Contains("ColorArgb") &&
                snapshots.Contains("Visible"),
                "Side tabs must consume a pure value snapshot.");
            Assert.IsTrue(tabs.Contains("Action<string> _openNote") &&
                tabs.Contains("Action<string> _deleteNote") &&
                tabs.Contains("Action<string, int> _reorderNote"),
                "Side tab user actions must be typed note-id actions.");
            Assert.IsTrue(tabs.Contains(
                "SetNotes(IList<SideTabSnapshot>"),
                "Side tabs must accept snapshot input.");
        }

        [TestMethod]
        public void DockTypedProtocol_HasBoundsCommandAndEvents()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(commands.Contains("SetBounds") &&
                commands.Contains("StickyUiBounds") &&
                commands.Contains("BoundsChanged"),
                "Dock protocol must expose typed bounds command/event data.");
            Assert.IsTrue(host.Contains("StickyUiCommandKind.SetBounds") &&
                session.Contains("StickyUiEventKind.BoundsChanged"),
                "StickyUiHost must execute and report typed bounds changes.");
        }

        [TestMethod]
        public void StickyTypedProtocol_ExposesReminderUpdateCommand()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(
                commands.Contains("UpdateReminders") &&
                commands.Contains("CopyReminders") &&
                host.Contains("StickyUiCommandKind.UpdateReminders") &&
                session.Contains("UpdateReminders("),
                "Hosted reminder parity needs a detached update command.");
        }

        [TestMethod]
        public void StickyTypedProtocol_ProductionUsesNamedPayloadFactories()
        {
            string protocol = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(protocol.Contains("StickyUiCommand Create(") &&
                protocol.Contains("StickyUiCommand SetBounds(") &&
                protocol.Contains("StickyUiCommand SetDockResizeRole(") &&
                protocol.Contains("StickyUiEvent FromSnapshot(") &&
                protocol.Contains("StickyUiEvent Signal(") &&
                protocol.Contains("StickyUiEvent HorizontalResize(") &&
                protocol.Contains("StickyUiEvent DividerResize("),
                "Protocol must expose factories for its payload shapes.");
            Assert.IsFalse(windowCoordinator.Contains(
                    "new StickyUiCommand(") ||
                dockCoordinator.Contains("new StickyUiCommand(") ||
                session.Contains("new StickyUiEvent("),
                "Production call sites must not guess long protocol payloads.");
        }

        [TestMethod]
        public void DockTypedProtocol_ForwardsHeaderDragAndResizeEvents()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(commands.Contains("HeaderDragStarted") &&
                commands.Contains("HeaderDragMoved") &&
                commands.Contains("HeaderDragCompleted") &&
                commands.Contains("DockHorizontalResizing") &&
                commands.Contains("DockDividerResizeStarted") &&
                commands.Contains("DockDividerResizing") &&
                commands.Contains("DockDividerResizeCompleted") &&
                commands.Contains("SetDockResizeRole") &&
                commands.Contains("CloseRequested"),
                "Dock protocol must expose header drag and resize event kinds.");
            Assert.IsTrue(session.Contains("_window.HeaderDragStarted +=") &&
                session.Contains("_window.HeaderDragMoved +=") &&
                session.Contains("_window.HeaderDragCompleted +=") &&
                session.Contains("_window.DockHorizontalResizing +=") &&
                session.Contains("_window.DockDividerResizeStarted +=") &&
                session.Contains("_window.DockDividerResizing +=") &&
                session.Contains("_window.DockDividerResizeCompleted +=") &&
                session.Contains("EmitSnapshot(") &&
                host.Contains("StickyUiCommandKind.SetDockResizeRole") &&
                session.Contains("role.SplitBottom") &&
                session.Contains("role.DividerMinimumHeight") &&
                session.Contains("role.DividerMaximumHeight") &&
                session.Contains("_window.DockDividerResizeActive") &&
                session.Contains("if (_applyingBounds) return;") &&
                session.Contains("StickyUiEventKind.CloseRequested"),
                "StickyUiHost must forward dock drag/resize events.");
        }

        [TestMethod]
        public void HostedDividerLiveResize_UsesExplicitLeanLifecycle()
        {
            string native = ReadSource(
                "Features/StickyNotes/StickyNativeWindowBehavior.cs");
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string liveResize = Between(dockCoordinator,
                "private bool ResizeHostedStickyDockDivider",
                "private bool MatchesHostedDockResizeSession");
            string progress = Between(windowCoordinator,
                "if (value.Kind == StickyUiEventKind.DockDividerResizing)",
                "if (value.Kind == StickyUiEventKind.DockDividerResizeCompleted)");

            Assert.IsTrue(native.Contains("WmEnterSizeMove") &&
                native.Contains("WmSizing") &&
                native.Contains("WmExitSizeMove") &&
                native.Contains("DockDividerResizeStarted") &&
                native.Contains("DockDividerResizing") &&
                native.Contains("DockDividerResizeCompleted"),
                "Native sizing must publish an explicit divider lifecycle.");
            Assert.IsTrue(liveResize.Contains(
                    "CalculateDockMemberResizeTargets") &&
                liveResize.Contains("ApplyDockTargets(changed, sourceNoteId)") &&
                !liveResize.Contains("LayoutDockChain") &&
                !liveResize.Contains("RefreshDockResizeRoles"),
                "Live ticks must move only changed followers from stable facts.");
            Assert.IsFalse(progress.Contains("SaveAsync") ||
                progress.Contains("RefreshDockResizeRoles"),
                "Live progress must not save or refresh resize roles.");
            Assert.IsTrue(windowCoordinator.Contains(
                "CompleteHostedStickyDockDivider(value)") &&
                windowCoordinator.Contains("finally { ClearHostedDockResizeSession(); }") &&
                windowCoordinator.Contains("_notes.SaveAsync();"),
                "Completion must save once and clear the transient session.");
        }

        [TestMethod]
        public void HostedDock_ReusesNeutralSessionAndTypedEffectBoundary()
        {
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            Assert.IsTrue(windowCoordinator.Contains(
                "BeginStickyDockDrag(facts)") &&
                windowCoordinator.Contains("MoveStickyDockDrag(facts)") &&
                windowCoordinator.Contains("CompleteStickyDockDrag(facts)"),
                "Hosted drag facts must enter the existing Dock session.");
            Assert.IsTrue(dockCoordinator.Contains(
                "StickyUiCommand.SetBounds(") &&
                dockCoordinator.Contains("StickyUiCommand.SetTopMost(") &&
                dockCoordinator.Contains("ApplyDockTargets") &&
                dockCoordinator.Contains("ResizeStickyDockGroup") &&
                dockCoordinator.Contains("ResizeHostedStickyDockDivider") &&
                dockCoordinator.Contains("CalculateDockDividerTargets") &&
                dockCoordinator.Contains("CloseStickyDockNote") &&
                !dockCoordinator.Contains("_noteWindows") &&
                !dockCoordinator.Contains("StickyNoteWindow"),
                "Dock effects must terminate at the hosted typed boundary.");
            Assert.IsFalse(windowCoordinator.Contains("DockMergeRequested") ||
                windowCoordinator.Contains("DockAttached") ||
                windowCoordinator.Contains("DockCompleted") ||
                dockCoordinator.Contains("HostedDockCoordinator"),
                "The minimum E2E must not add a second Dock protocol.");
        }

        [TestMethod]
        public void DockVisualFeedback_UsesDetachedFactsOnHostedPath()
        {
            string form = ReadSource("PetForm.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string begin = Between(coordinator,
                "private void BeginStickyDockDrag",
                "private void MoveStickyDockDrag");
            string moveVisuals = Between(coordinator,
                "RememberActiveDockFacts(moveTargets);",
                "private void CompleteStickyDockDrag");
            string mergeVisuals = Between(coordinator,
                "List<StickyNoteData> mergedSnapshot",
                "CommitVisibleDockOrder(seed);");
            string helpers = Between(coordinator,
                "private void ShowSplitGuide",
                "private DockTarget FindDockTarget");

            Assert.IsTrue(begin.Contains("ShowSplitGuide(seed, groupFacts)") &&
                !begin.Contains("StickyNoteWindow"),
                "Hosted split candidates must receive a detached guide.");
            Assert.IsTrue(moveVisuals.Contains(
                    "UpdateSplitGuide(seed, _activeDockCurrentFacts)") &&
                moveVisuals.Contains("UpdateDockPreview(seed, previewFacts)") &&
                !moveVisuals.Contains("_activeNoteDragHosted"),
                "Hosted drag must update previews from detached facts.");
            Assert.IsTrue(mergeVisuals.Contains("ShowTransientDockPulse") &&
                !mergeVisuals.Contains("if (!_activeNoteDragHosted)"),
                "Hosted merge must publish the detached seam pulse.");
            Assert.IsTrue(helpers.Contains(
                    "CalculateDockVisualSeam(parentFacts)") &&
                helpers.Contains("IDictionary<string, DockWindowFacts>") &&
                !helpers.Contains("parent.Bounds") &&
                !helpers.Contains("StickyDockOperations"),
                "Visual helpers must use detached geometry without changing Dock rules.");
            Assert.IsTrue(form.Contains(
                    "private string _dockPreviewParentNoteId") &&
                form.Contains("private string _dockPreviewChildNoteId") &&
                !form.Contains("StickyNoteWindow _dockPreviewParent") &&
                !form.Contains("StickyNoteWindow _dockPreviewChild"),
                "Preview identity must be note-id based, not Window based.");
        }

        [TestMethod]
        public void StickyRecovery_ExpandsAllThroughOwnedEffectBoundaries()
        {
            string form = ReadSource("PetForm.cs");
            string menu = ReadSource("PetContextMenu.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string action = Between(coordinator,
                "private void ExpandAndTileAllStickyNotesToPetScreen",
                "internal static List<DockLayoutTarget>");
            string preparation = Between(coordinator,
                "PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,",
                "internal static List<Rectangle> CalculateStickyRecoveryLayout");

            Assert.IsTrue(menu.Contains("展开全部并平铺到此屏幕") &&
                form.Contains("ExpandAndTileAllStickyNotesToPetScreen"),
                "The menu must expose the new expand-and-tile product action.");
            Assert.IsTrue(action.Contains("ShowHostedSticky(note, false, false)") &&
                action.Contains("ApplyDockTarget(target, null)") &&
                !action.Contains("ShowStickyNote("),
                "Every note must use the hosted effect edge.");
            Assert.IsFalse(action.Contains("GetOrCreateStickyNoteWindow") ||
                action.Contains("seed.Visible"),
                "Recovery must neither skip hidden notes nor use a universal legacy route.");
            Assert.IsTrue(preparation.Contains(
                "StickyDockGroups.ClearMembership(note)") &&
                preparation.Contains("note.Visible = true") &&
                preparation.Contains("CalculateStickyRecoveryLayout"),
                "Preparation must detach, expand, and independently tile every note.");
        }

        [TestMethod]
        public void DockParticipantEligibility_DoesNotDependOnStickySubtypeOrExecutor()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string operations = ReadSource(
                "Core/StickyNotes/StickyDockOperations.cs");
            string geometry = ReadSource(
                "Core/StickyNotes/StickyDockGeometry.cs");
            string gate = Between(coordinator,
                "private bool IsDockParticipant",
                "private string FindDockChild");

            Assert.IsTrue(coordinator.Contains(
                "CanUseDockComponents("),
                "Hosted dock gate must be used for hosted drag targets.");
            Assert.IsFalse(gate.Contains("!note.IsTodoList") ||
                gate.Contains("!note.IsSchedule"),
                "Todo and Schedule must be allowed to dock with ordinary notes.");
            Assert.IsFalse(gate.Contains("IsHostedSticky"),
                "Dock participant eligibility must not depend on live session membership.");
            Assert.IsFalse(gate.Contains("ReminderUtcTicks"),
                "Reminder is not a sticky subtype and must not affect Dock eligibility.");
            Assert.IsFalse(operations.Contains("IsTodoList") ||
                operations.Contains("IsSchedule") ||
                geometry.Contains("IsTodoList") ||
                geometry.Contains("IsSchedule"),
                "Core Dock rules must remain independent of Sticky content mode.");
        }

        private static string ReadSource(string relativePath)
        {
            string root = FindDesktopPetDirectory();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
        }

        private static string Between(string text, string startMarker, string endMarker)
        {
            int start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing marker: " + startMarker);
            int end = text.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start);
        }

        private static string FindDesktopPetDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Windows.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet Windows source directory.");
        }
    }
}
