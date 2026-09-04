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
                loadingThread.Contains(
                    "_ready.WaitOne(ReadyTimeoutMilliseconds)") &&
                loadingThread.Contains(
                    "_exited.WaitOne(ExitTimeoutMilliseconds)") &&
                !loadingThread.Contains("_exited.WaitOne()"),
                "Close must marshal to loading STA and both waits must be bounded.");
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
                "private bool ApplyHostedStickyEvent",
                "private void ClearHostedDockResizeSession");

            Assert.IsTrue(handler.Contains("ApplyHostedStickyEvent(") &&
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
                form.Contains("WmDeviceChange") &&
                form.Contains("NotifyPotentialChange") &&
                form.Contains("DisplayTopologyRuntime") &&
                !form.Contains("BeginInvoke(new Action(PositionNoteTabs))") &&
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
        public void StickyPersistence_FutureSchemaFailsClosedBeforeRecovery()
        {
            string repository = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string exception = ReadSource(
                "Features/StickyNotes/UnsupportedStickySchemaException.cs");
            string host = ReadSource("PennyApplicationHost.cs");
            string pet = ReadSource("PetForm.cs");
            string load = Between(repository,
                "internal static StickyNoteRepository LoadFromFile(string filePath)",
                "private static bool TryPopulateFromFile");
            string populate = Between(repository,
                "private static bool TryPopulateFromFile",
                "private static void AddParsedLine");
            string save = Between(repository,
                "internal PersistenceResult SaveToFile",
                "internal PersistenceResult ExportSnapshot");

            Assert.IsTrue(exception.Contains("DetectedVersion") &&
                exception.Contains("MaximumSupportedVersion") &&
                exception.Contains("SourcePath"),
                "Future schema must have an explicit failure classification.");
            int primaryBlock = load.IndexOf(
                "primaryError as UnsupportedStickySchemaException",
                StringComparison.Ordinal);
            int backupProbe = load.IndexOf(
                "string backupPath = filePath + \".bak\"",
                StringComparison.Ordinal);
            Assert.IsTrue(primaryBlock >= 0 && backupProbe > primaryBlock,
                "A future primary must block before any backup fallback.");
            int preflight = populate.IndexOf(
                "InspectSchemaVersions(lines, filePath)",
                StringComparison.Ordinal);
            int parse = populate.IndexOf("AddParsedLine(repository, line)",
                StringComparison.Ordinal);
            Assert.IsTrue(preflight >= 0 && parse > preflight,
                "Every file must be version-preflighted before payload parsing.");
            Assert.IsTrue(save.IndexOf("if (!_loadSucceeded)",
                    StringComparison.Ordinal) <
                save.IndexOf("generation = ++_requestedGeneration",
                    StringComparison.Ordinal),
                "A blocked repository must reject save before snapshot generation.");
            Assert.IsTrue(pet.Contains("if (_notes.IsFutureSchemaBlocked)") &&
                pet.Contains("throw _notes.FutureSchemaError;") &&
                host.Contains("catch (UnsupportedStickySchemaException error)") &&
                host.Contains("BuildFutureSchemaBlockedMessage(error)"),
                "Startup must show the dedicated message and exit before Pet UI continues.");
        }

        [TestMethod]
        public void StickyPersistence_BarriersAreBoundedAndExitResolvesBothFailures()
        {
            string repository = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string wait = Between(repository,
                "internal PersistenceResult WaitForPendingSaves(TimeSpan timeout)",
                "private void AsyncWriterLoop");
            string commit = Between(repository,
                "private PersistenceResult CommitPreparedSnapshot",
                "internal PersistenceResult CommitImportedMerge");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string exit = Between(coordinator,
                "private bool FlushPersistenceBeforeExit()",
                "private bool ExportUnsavedStickyNotes()");

            Assert.IsTrue(wait.Contains("Monitor.Wait(_saveGate, remaining)") &&
                wait.Contains("TimeoutException") &&
                wait.Contains("PersistenceResult.Failure(error)"),
                "Pending-save barriers must return a bounded failure.");
            Assert.IsTrue(commit.Contains("WaitForPendingSaves()") &&
                commit.Contains("if (!pendingSaves.Succeeded) return pendingSaves;"),
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
                coordinator.Contains("ApplyHostedStickyFactsGeometry") &&
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
        public void DailyBriefing_UsesCoreSentenceBudgetAndEndingPolicy()
        {
            string ending = ReadSource(
                "Core/Messaging/PetSentenceEndingPolicy.cs");
            string content = ReadSource(
                "Core/DailyContent/DailyBriefingContent.cs");
            string composer = ReadSource(
                "Core/DailyContent/DailyBriefingComposer.cs");
            string coordinator = ReadSource("PetDailyContentCoordinator.cs");
            string bubble = ReadSource("PetBubbleCoordinator.cs");

            Assert.IsTrue(content.Contains("DailyBriefingSentence") &&
                content.Contains("PetSentenceIntent") &&
                composer.Contains("selected.Count == 3") &&
                composer.Contains("PetSentenceEndingPolicy.Apply") &&
                coordinator.Contains("localNow.Date, content"),
                "Daily content must carry explicit semantic sentence facts.");
            Assert.IsTrue(ending.Contains("PetSentenceRole") &&
                ending.Contains("PetSentenceIntent") &&
                ending.Contains("PetSentenceContentKind") &&
                ending.Contains("2166136261") &&
                ending.Contains("16777619") &&
                !ending.Contains("GetHashCode") &&
                !ending.Contains("System.Windows") &&
                !ending.Contains("PetSettings") &&
                !ending.Contains("History"),
                "Sentence endings must remain deterministic stateless Core rules.");
            Assert.IsFalse(bubble.Contains("PetSentenceEndingPolicy") ||
                coordinator.Contains("呢～") || coordinator.Contains("喔～"),
                "Bubble and Windows coordination must not construct endings.");
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
                "Core/StickyNotes/SideTabSnapshot.cs");

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
            Assert.IsFalse(snapshots.Contains("ToDisplayData"),
                "SideTabSnapshot must remain a detached projection, not a fake note adapter.");
            Assert.IsFalse(tabs.Contains("new StickyNoteData"),
                "Side tab display code must not reconstruct canonical note objects.");
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
                    "CalculateDockVisualSeamPhysical(parentFacts)") &&
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
            string menu = ReadSource("PetContextMenu.cs");
            string manager = ReadSource(
                "Features/StickyNotes/StickyNotes.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string action = Between(coordinator,
                "private void ExpandAndTileAllStickyNotesToPetScreen",
                "internal static List<DockLayoutTarget>");
            string preparation = Between(coordinator,
                "PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,",
                "internal static List<Rectangle> CalculateStickyRecoveryLayout");

            Assert.IsTrue(manager.Contains("桌面整理") &&
                manager.Contains("收起全部") &&
                manager.Contains("展开全部") &&
                manager.Contains("平铺到当前屏幕") &&
                coordinator.Contains("ExpandAndTileAllStickyNotesToPetScreen") &&
                !menu.Contains("Menu.Items.Add(RecoverWindowsItem)") &&
                !menu.Contains("Menu.Items.Add(BackupNotesItem)") &&
                !menu.Contains("Menu.Items.Add(ImportNotesItem)") &&
                !menu.Contains("Menu.Items.Add(RestoreNotesItem)"),
                "Desktop recovery actions must live in the management console.");
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
                preparation.Contains("cascadeStep"),
                "Preparation must detach, expand, and independently cascade every note.");
        }

        [TestMethod]
        public void StickyManager_UsesBoundedImportPreviewModes()
        {
            string manager = ReadSource(
                "Features/StickyNotes/StickyNotes.cs");
            string persistence = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(manager.Contains("ManagerMode") &&
                manager.Contains("ImportPreview") &&
                manager.Contains("BeginImportPreview") &&
                manager.Contains("ManagerFormClosing") &&
                manager.Contains("ClearImportPreview();") &&
                manager.Contains("PrepareImport") &&
                manager.Contains("ConfirmImport"),
                "Manager must keep import planning inside the existing form.");
            Assert.IsFalse(manager.Contains("ImportBackup"),
                "Manager import must enter the preview boundary, not commit directly.");
            Assert.IsTrue(persistence.Contains("PrepareStickyNotesImport") &&
                persistence.Contains("CommitStickyNotesImport") &&
                persistence.Contains("ImportPlansMatch") &&
                persistence.Contains("CommitImportedMerge"),
                "Import must read, plan, revalidate, then use the existing commit owner.");
            Assert.IsTrue(coordinator.Contains("PrepareImport = PrepareStickyNotesImport") &&
                coordinator.Contains("ConfirmImport = CommitStickyNotesImport") &&
                coordinator.Contains("FullRestore = RestoreStickyNotesBackup") &&
                manager.Contains("高级：完整恢复…"),
                "The manager must receive typed prepare/confirm commands from PetForm.");
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

        [TestMethod]
        public void DynamicDisplayCoreContracts_RemainPlatformAndUiIndependent()
        {
            string topology = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");
            string placement = ReadSource(
                "Core/Display/DisplayPlacementModels.cs");
            string rules = ReadSource(
                "Core/Display/DisplayTopologyRules.cs");
            string combined = topology + placement + rules;

            Assert.IsFalse(combined.Contains("using System.Windows") ||
                combined.Contains("IntPtr") ||
                combined.Contains("DllImport") ||
                combined.Contains("QueryDisplayConfig(") ||
                combined.Contains("GetDpiForWindow("),
                "DRT Core contracts must contain no Windows handle or UI dependency.");
            Assert.IsTrue(topology.Contains(
                    "IReadOnlyList<DisplaySurfaceSnapshot> Surfaces") &&
                topology.Contains(
                    "IReadOnlyList<DisplayTargetIdentity> Targets") &&
                placement.Contains("WindowPlacementPreference Preferred") &&
                placement.Contains("WindowFacts Effective"),
                "Topology collections must be immutable and preferred/effective placement must stay separate.");
            Assert.IsTrue(ReadSource("PetForm.cs").Contains(
                    "DisplayTopologyRuntime") &&
                ReadSource("PetForm.cs").Contains(
                    "NotifyPotentialChange") &&
                !ReadSource("StickyUiHost.cs").Contains(
                    "DisplayTopologySnapshot"),
                "DRT-3 wiring must go through DisplayTopologyRuntime only.");
        }

        [TestMethod]
        public void Drt5_NativePlacementExecutor_OwnsTypedHiddenBootstrap()
        {
            string executor = ReadSource(
                "Infrastructure/Display/WindowsWindowPlacementExecutor.cs");
            string native = ReadSource(
                "Infrastructure/Display/NativeDisplayConfig.cs");

            Assert.IsTrue(executor.Contains("WindowInteropHelper") &&
                executor.Contains(".EnsureHandle()") &&
                executor.Contains("internal int GetDpiForWindow()") &&
                executor.Contains("internal bool SetWindowPosExact(") &&
                executor.Contains("internal void Show()") &&
                executor.Contains("WindowsWindowFactsReader.Capture("),
                "The executor must own the typed native placement bootstrap.");
            string hiddenMove = Between(executor,
                "internal bool MoveHiddenToSurface(PhysicalRect workArea)",
                "internal int GetDpiForWindow()");
            Assert.IsTrue(hiddenMove.Contains("SWP_NOACTIVATE") &&
                hiddenMove.Contains("SWP_NOZORDER") &&
                hiddenMove.Contains("SWP_NOSIZE"),
                "The hidden move must never activate, reorder or resize.");
            Assert.IsFalse(hiddenMove.Contains("SWP_SHOWWINDOW") ||
                hiddenMove.Contains("SW_SHOW"),
                "The hidden bootstrap move must never show the window.");
            Assert.IsTrue(native.Contains(
                    "static extern bool SetWindowPos(") &&
                native.Contains("static extern bool ShowWindow("),
                "SetWindowPos/ShowWindow must be declared as typed natives.");
        }

        [TestMethod]
        public void Drt5_HostedWindowConstructor_DoesNotOwnDesktopPlacement()
        {
            string wpf = ReadSource(
                "Features/StickyNotes/StickyNoteWpf.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(wpf.Contains("hostedNativePlacement") &&
                wpf.Contains("data.LocalLogicalWidth") &&
                wpf.Contains("data.LocalLogicalHeight"),
                "Hosted construction must size from the logical DIP model.");
            string hostedBranch = Between(wpf,
                "if (hostedNativePlacement)", "else");
            Assert.IsFalse(hostedBranch.Contains("base.Left = data.X") ||
                hostedBranch.Contains("base.Top = data.Y") ||
                hostedBranch.Contains("data.Width") ||
                hostedBranch.Contains("data.Height"),
                "The hosted path must not feed physical fields into WPF placement.");
            Assert.IsTrue(session.Contains(
                    "new StickyNoteWindow(snapshot.CreateWorkingCopy(),") &&
                session.Contains("false, false, true)"),
                "Hosted sessions must use the native-placement constructor.");
        }

        [TestMethod]
        public void Drt5_Session_PlacesExactlyBeforeShowAndVerifiesFacts()
        {
            string session = ReadSource("StickyWindowSession.cs");
            int ensure = session.IndexOf(
                "_placementExecutor.EnsureHandle()",
                StringComparison.Ordinal);
            int setExact = session.IndexOf(
                "_placementExecutor.SetWindowPosExact(requested)",
                StringComparison.Ordinal);
            int show = session.IndexOf("_placementExecutor.Show()",
                StringComparison.Ordinal);
            int capture = session.IndexOf(
                "_placementExecutor.CaptureFacts(",
                StringComparison.Ordinal);

            Assert.IsTrue(ensure >= 0 && show > ensure,
                "The HWND must be created before the window is shown.");
            Assert.IsTrue(setExact > ensure && show > setExact,
                "The exact physical rect must land before Show.");
            Assert.IsTrue(capture > show,
                "Actual WindowFacts must be captured after Show.");
            Assert.IsTrue(session.Contains(
                    "IsWithinPlacementTolerance") &&
                session.Contains("MoveHiddenToSurface(plan.WorkArea)") &&
                session.Contains("GetDpiForWindow()"),
                "The standalone path must run the full hidden bootstrap.");

            string placement = Between(session,
                "private bool PlaceAtNativeBounds(NativePlacementPlan plan, bool edit)",
                "private void TracePlacementMismatch");
            Assert.IsFalse(placement.Contains("_window.ShowAtPhysicalBounds") ||
                placement.Contains("ShowRestoredAtPhysicalBounds"),
                "The standalone native path must not reuse the legacy show helper.");
        }

        [TestMethod]
        public void DrtCloseout_TopologyGenerationOwnedOnlyByRuntime()
        {
            string provider = ReadSource(
                "Infrastructure/Display/WindowsDisplayTopologyProvider.cs");
            string runtime = ReadSource(
                "Infrastructure/Display/DisplayTopologyRuntime.cs");
            string models = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");

            Assert.IsTrue(provider.Contains(
                    "return new DisplayTopologySnapshot(0, surfaces)") &&
                runtime.Contains(".WithGeneration(") &&
                runtime.Contains("Generation++") &&
                models.Contains("WithGeneration(long generation)"),
                "Only DisplayTopologyRuntime may assign semantic generations.");
            Assert.IsFalse(provider.Contains("Generation++"),
                "The capture provider must not own semantic generation.");
        }

        [TestMethod]
        public void DrtCloseout_StickySessionNeverRecapturesTopology()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string host = ReadSource("StickyUiHost.cs");

            Assert.IsFalse(session.Contains(
                    "new WindowsDisplayTopologyProvider()"),
                "The sticky STA must not capture Windows topology itself.");
            Assert.IsTrue(session.Contains("_topology") &&
                session.Contains("topology.FindByRuntimeGdiName("),
                "Placement must resolve against the Pet-owned topology.");
            Assert.IsFalse(host.Contains("DisplayTopologySnapshot"),
                "The host facade must only forward the detached typed snapshot.");
        }

        [TestMethod]
        public void DrtCloseout_GeometryEventsUseFactsAsGeometryTruth()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(session.Contains(
                    "WindowsWindowFactsReader.Capture(hwnd, _noteId,") &&
                session.Contains("_topology == null ? 0 : _topology.Generation") &&
                session.Contains("facts, _topology)"),
                "Facts must be captured with the Pet-owned topology generation.");
            Assert.IsTrue(coordinator.Contains(
                    "ApplyHostedStickyFactsGeometry") &&
                coordinator.Contains("StickyPlacementMath.FromPhysicalRect(") &&
                coordinator.Contains(
                    "value.Snapshot.ApplyContentTo(canonical)"),
                "Geometry events must derive v10 geometry from facts, never snapshot.ApplyTo.");

            string dragHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.HeaderDragStarted ||",
                "if (value.Kind == StickyUiEventKind.BoundsChanged)");
            Assert.IsTrue(dragHandler.Contains(
                    "DockWindowFacts.FromData(canonical)") &&
                !dragHandler.Contains("ApplyHostedStickySnapshot"),
                "Drag geometry must flow from facts-derived canonical state.");
            string boundsHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.BoundsChanged)",
                "if (value.Kind == StickyUiEventKind.DockDividerResizeStarted)");
            Assert.IsTrue(boundsHandler.Contains(
                    "ApplyHostedStickyEvent(value, false)") &&
                !boundsHandler.Contains("ApplyHostedStickySnapshot"),
                "BoundsChanged must not apply the full snapshot.");
        }

        [TestMethod]
        public void DrtCloseout_HostedNeverWritesPhysicalIntoWpfDips()
        {
            string behavior = ReadSource(
                "Features/StickyNotes/StickyNativeWindowBehavior.cs");
            string wpf = ReadSource(
                "Features/StickyNotes/StickyNoteWpf.cs");

            string recover = Between(behavior,
                "private void RecoverUnexpectedMaximize()",
                "internal static Rectangle CalculateRecoveredHeaderDragBounds");
            Assert.IsFalse(recover.Contains("base.Left = Data.X") ||
                recover.Contains("base.Top = Data.Y") ||
                recover.Contains("base.Width = Math.Max(MinWidth") ||
                recover.Contains("base.Height = Math.Max(MinHeight"),
                "Maximize recovery must not write persisted physical fields into WPF DIP.");
            Assert.IsTrue(recover.Contains("_lastValidLeft"),
                "Recovery must restore the last valid DIP geometry.");

            string ensure = Between(wpf,
                "private void EnsureOnScreen()",
                "private void EnsureOnScreenNative()");
            Assert.IsTrue(ensure.Contains("_hostedNativePlacement") &&
                ensure.Contains("EnsureOnScreenNative();"),
                "Hosted windows must clamp on the native HWND, not in WPF DIP.");
            string nativeEnsure = Between(wpf,
                "private void EnsureOnScreenNative()",
                "private void CacheLastValidDips()");
            Assert.IsTrue(nativeEnsure.Contains("EnsureHandle()") &&
                nativeEnsure.Contains("SetWindowPos(") &&
                !nativeEnsure.Contains("Data.X"),
                "The hosted clamp must be a native physical-pixel move.");
        }

        [TestMethod]
        public void DrtCloseout_MirrorTargets0IsNotDurablePreferenceRule()
        {
            string reader = ReadSource(
                "Infrastructure/Display/WindowsWindowFactsReader.cs");
            string models = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");

            Assert.IsTrue(reader.Contains(
                    "Targets[0] is NOT a durable") &&
                reader.Contains("DRT-6 must choose"),
                "Facts capture must document Targets[0] as an active-target hint only.");
            Assert.IsTrue(models.Contains(
                    "never a durable-preferred") &&
                models.Contains("QueryDisplayConfig enumeration order"),
                "Mirrored-surface target order must never become an identity rule.");
        }

        [TestMethod]
        public void Drt6_CodecWritesV11WhileKeepingHistoricalVersions()
        {
            string codec = ReadSource("Core/StickyNotes/StickyNoteCodec.cs");

            Assert.IsTrue(codec.Contains(
                    "internal const int VersionElevenFieldCount = 37") &&
                codec.Contains("CurrentVersion = VersionEleven") &&
                codec.Contains("bool versionEleven = fields.Length >= 37") &&
                codec.Contains("versionTen || versionEleven"),
                "The codec must emit v11 and keep v1-v10 parsing paths intact.");
            Assert.IsTrue(codec.Contains(
                    "Encode(note.PreferredDisplayTargetKey") &&
                codec.Contains("note.PreferredLocalLogicalWidth.ToString("),
                "v11 must persist the durable preferred target and local rect.");
        }

        [TestMethod]
        public void Drt6_SessionRestoresPreferredBeforeV10Display()
        {
            string session = ReadSource("StickyWindowSession.cs");
            int preferred = session.IndexOf(
                "data.PreferredDisplayTargetKey",
                StringComparison.Ordinal);
            int legacy = session.IndexOf(
                "topology.FindByRuntimeGdiName(",
                StringComparison.Ordinal);

            Assert.IsTrue(preferred >= 0 && legacy > preferred,
                "Restore must resolve the v11 preferred target before the v10 DisplayId.");
            Assert.IsTrue(session.Contains(
                    "data.PreferredLocalLogicalWidth > 0") &&
                session.Contains("topology.FindByTargetKey("),
                "The preferred local rect must be projected against its durable target.");
        }

        [TestMethod]
        public void Drt6_OnlyUserReasonsCommitPreferred()
        {
            string rules = ReadSource(
                "Core/StickyNotes/StickyPlacementRules.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(rules.Contains("internal enum PlacementReason") &&
                rules.Contains("CanCommitPreferred(PlacementReason reason)") &&
                rules.Contains("case PlacementReason.UserMoveCommit:") &&
                rules.Contains("case PlacementReason.DockCommit:"),
                "Preferred commit must be gated by placement reason.");
            Assert.IsTrue(coordinator.Contains(
                    "PlacementReason.UserResizeCommit") &&
                coordinator.Contains("PlacementReason.Spawn") &&
                coordinator.Contains("PlacementReason.ExpandAndTile"),
                "Pet must only commit preferred at user-gesture call sites.");
        }

        [TestMethod]
        public void Drt6_PreferredStaysOutsideFullSnapshotApply()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string apply = Between(commands,
                "internal void ApplyTo(StickyNoteData target)",
                "internal void ApplyPreferredTo(StickyNoteData target)");

            Assert.IsTrue(commands.Contains(
                    "internal void ApplyPreferredTo(StickyNoteData target)") &&
                commands.Contains("ApplyPreferredTo(copy)"),
                "Working copies must carry the preferred placement separately.");
            Assert.IsFalse(apply.Contains("PreferredDisplayTargetKey"),
                "Full snapshot application must never clobber preferred placement.");
        }

        [TestMethod]
        public void Drt6_EffectiveRuntimeIsSeparateFromRepository()
        {
            string runtime = ReadSource(
                "Features/StickyNotes/StickyPlacementRuntime.cs");

            Assert.IsTrue(runtime.Contains(
                    "Dictionary<string, NotePlacementState> _states") &&
                runtime.Contains("internal void UpdateEffective("),
                "Effective WindowFacts must live in runtime memory.");
            Assert.IsFalse(runtime.Contains("StickyNoteRepository") ||
                runtime.Contains("SaveAsync") ||
                runtime.Contains("SetWindowPos") ||
                runtime.Contains("WindowInteropHelper") ||
                runtime.Contains("StickyNoteWindow("),
                "The runtime store must not own persistence or UI objects.");
        }

        [TestMethod]
        public void Drt6Supplement_SpawnUsesCenteredPolicyWithoutCascade()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(coordinator.Contains(
                    "StickySpawnPolicy.PlanCenteredSpawn(") &&
                coordinator.Contains("PrepareStickyNoteDraft(") &&
                coordinator.Contains("_notes.CreateDraft(text, Point.Empty)"),
                "Spawn must route through the centered pure policy on a draft.");
            Assert.IsFalse(coordinator.Contains("% 7) * 18") ||
                coordinator.Contains("12 + offset") ||
                coordinator.Contains("StickyPlacementMath.FromSpawn("),
                "The cascade and beside-pet spawn paths must be retired.");
        }

        [TestMethod]
        public void Drt6Supplement_CreateDraftDoesNotPersistIntermediateState()
        {
            string repository = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string create = Between(repository,
                "public StickyNoteData Create(string text, Point location)",
                "public List<StickyNoteData> GetAll()");
            string draft = Between(repository,
                "internal StickyNoteData CreateDraft(string text, Point location)",
                "public List<StickyNoteData> GetAll()");

            Assert.IsTrue(create.Contains("CreateDraft(text, location)") &&
                create.Contains("Save();"),
                "Create() must delegate to CreateDraft and own the only save.");
            Assert.IsTrue(draft.Contains("_notes.Add(note);") &&
                !draft.Contains("Save()"),
                "CreateDraft must add to memory without persisting.");
        }

        [TestMethod]
        public void Drt6Supplement_PetFactsAlignedWithSingleTopologySnapshot()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(coordinator.Contains(
                    "DisplayTopologySnapshot topology = CurrentTopologySnapshot();") &&
                coordinator.Contains("CapturePetWindowFacts(topology)") &&
                coordinator.Contains(
                    "WindowsWindowFactsReader.Capture(Handle, \"pet\","),
                "Pet facts must be captured against the same attempt topology.");
            string fallback = Between(coordinator,
                "private void ApplyLegacySpawnFallback",
                "private static void TraceSpawnPlacement");
            Assert.IsFalse(fallback.Contains("PreferredDisplayTargetKey"),
                "The legacy fallback must never fabricate a durable preference.");
        }

        [TestMethod]
        public void Drt6Supplement_CreationDoesNotDependOnDisplayIndexOrCount()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string spawn = Between(coordinator,
                "private StickyNoteData PrepareStickyNoteDraft",
                "private void ApplyLegacySpawnFallback");

            Assert.IsFalse(spawn.Contains("Screen.AllScreens") ||
                spawn.Contains("Screen.PrimaryScreen") ||
                spawn.Contains("_notes.GetAll().Count") ||
                spawn.Contains("MonitorFromRect"),
                "Spawn must follow the Pet surface, never display order or note count.");
        }

        [TestMethod]
        public void Drt7_RehomeSkipsDockMembersAndUsesCorePolicy()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string reconcile = Between(coordinator,
                "private void HandleStickyTopologyChanged",
                "private void CompleteTemporaryRehome");

            Assert.IsTrue(reconcile.Contains(
                    "if (!String.IsNullOrEmpty(note.DockGroupId)) continue;") &&
                reconcile.Contains(
                    "FallbackDisplayPolicy.ResolveFallbackSurface("),
                "DRT-7 must rehome standalone notes only through the Core fallback policy.");
            Assert.IsFalse(reconcile.Contains(
                    "CommitHostedStickyPreferred") ||
                reconcile.Contains("PreferredDisplayTargetKey ="),
                "Temporary rehome must never commit or rewrite the durable preferred.");
        }

        [TestMethod]
        public void Drt7_FallbackPolicyIsPlatformIndependent()
        {
            string policy = ReadSource("Core/Display/FallbackDisplayPolicy.cs");

            Assert.IsFalse(policy.Contains("System.Windows") ||
                policy.Contains("IntPtr") ||
                policy.Contains("DllImport") ||
                policy.Contains("WindowsDisplay"),
                "The fallback policy must stay pure Core.");
            Assert.IsTrue(policy.Contains("PrimaryOrFirst()") &&
                policy.Contains("FindByRuntimeGdiName("),
                "The policy must fall back through Pet surface and primary.");
        }

        [TestMethod]
        public void Drt7_UserCommitBlocksReturnButReturnRestores()
        {
            string runtime = ReadSource(
                "Features/StickyNotes/StickyPlacementRuntime.cs");

            Assert.IsTrue(runtime.Contains("MarkUserPlacementCommit(") &&
                runtime.Contains("MarkReturnedToPreferred(") &&
                runtime.Contains("MarkTemporaryRehome("),
                "The runtime must model temporary, user-moved and returned states.");
            string commit = Between(runtime,
                "internal void MarkUserPlacementCommit(string noteId)",
                "internal void MarkReturnedToPreferred(string noteId)");
            Assert.IsTrue(commit.Contains(
                    "userMoved = state.IsTemporaryRehome") &&
                commit.Contains("state.UserMovedSinceRehome"),
                "A commit during a temporary stay must record user intent.");
        }

        [TestMethod]
        public void Drt7_TopologyRehomePublishesFreshWindowFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(coordinator.Contains(
                    "StickyUiCommand.Reproject(rehomedNoteId,") &&
                commands.Contains(
                    "DisplayTopologySnapshot topology = null") &&
                host.Contains(
                    "session.SetBounds(command.Bounds,") &&
                host.Contains("command.Topology"),
                "A topology rehome must carry its immutable snapshot to the Sticky STA.");
            string setBounds = Between(session,
                "internal StickyUiCommandResult SetBounds(",
                "internal StickyUiCommandResult Close()");
            Assert.IsTrue(setBounds.Contains(
                    "_topology = topology ?? _topology") &&
                setBounds.Contains(
                    "StickyUiEventKind.BoundsChanged") &&
                setBounds.Contains("CaptureWindowFacts(_sequence)"),
                "The completed rehome must publish actual HWND facts through the existing typed path.");
        }

        [TestMethod]
        public void Drt7_MissingStartupTargetUsesDetachedTemporarySnapshot()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string start = Between(coordinator,
                "private void StartHostedSticky(",
                "private bool IsHostedSticky(");

            Assert.IsTrue(start.Contains(
                    "TryBuildTemporaryRehomeTarget(note") &&
                start.Contains("StickyUiCommand.Create(") &&
                start.Contains("rehomeTarget)") &&
                start.Contains(
                    "preferred-display-missing-at-restore"),
                "Startup restore must attach a typed rehome target without rewriting the durable preferred target.");
            Assert.IsFalse(start.Contains("temporary.ApplyTo(note)"),
                "Temporary startup geometry must not mutate the canonical note before the hosted result succeeds.");
        }

        [TestMethod]
        public void Drt67Closeout_StandaloneDragCommitUsesEventAuthority()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dragCommit = Between(coordinator,
                "private void CommitDraggedNotePreferred",
                "private void CommitUserMovedPreferred");

            Assert.IsTrue(dragCommit.Contains(
                    "TryBuildPreference(value.Facts, value.Topology,") &&
                dragCommit.Contains("PlacementReason.UserMoveCommit"),
                "The dragged note preference must come from capture-time facts and topology.");
            Assert.IsFalse(dragCommit.Contains("CurrentTopologySnapshot("),
                "A G-generation drag commit must never read the Current generation.");

            string legacy = Between(coordinator,
                "Transition-only legacy commit",
                "private bool ApplyHostedStickySnapshot");
            Assert.IsTrue(legacy.Contains(
                    "String.Equals(id, seed.Id,") &&
                legacy.Contains("continue;") &&
                legacy.Contains("Transition-only"),
                "The legacy dock-member commit must skip the dragged note and stay marked transition-only.");
        }

        [TestMethod]
        public void Drt67Closeout_RehomeUsesNativeReprojectNotMonitorDpi()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsFalse(coordinator.Contains(
                    "ResolveTemporaryRehomePlacement") ||
                coordinator.Contains("WindowsDisplayResolver.ResolveDisplay"),
                "GetDpiForMonitor must not shape the temporary rehome.");
            Assert.IsTrue(coordinator.Contains(
                    "TryBuildTemporaryRehomeTarget(") &&
                coordinator.Contains("StickyUiCommand.Reproject("),
                "Rehome must flow through the typed native reproject command.");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private void CorrectReprojectionOnce");
            Assert.IsTrue(reproject.Contains("GetDpiForWindow()") &&
                reproject.Contains("MoveHiddenToSurface(") &&
                reproject.Contains("SetWindowPosExact(projected)"),
                "The reproject must use the real window DPI on the sticky STA.");
            Assert.IsFalse(reproject.Contains("PreferredDisplayTargetKey"),
                "Reprojection must never modify the durable preferred fields.");
        }

        [TestMethod]
        public void Drt67Closeout_PreferredReturnHidesBeforeBootstrap()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private void CorrectReprojectionOnce");
            int hide = reproject.IndexOf("_window.Hide()",
                StringComparison.Ordinal);
            int move = reproject.IndexOf("MoveHiddenToSurface(",
                StringComparison.Ordinal);

            Assert.IsTrue(reproject.Contains("bool wasVisible =") &&
                reproject.Contains("if (wasVisible) _window.Hide();") &&
                hide >= 0 && move > hide,
                "A visible window must be hidden before the target-surface bootstrap.");
            Assert.IsTrue(reproject.Contains(
                    "StickySpawnPolicy.CenterInWorkArea("),
                "The rehome path must center-fit the preferred logical size.");
            string correction = Between(session,
                "private void CorrectReprojectionOnce",
                "internal StickyUiCommandResult Close()");
            Assert.IsTrue(correction.Contains("IsWithinPlacementTolerance") &&
                correction.Contains("SetWindowPosExact(requested)"),
                "The reproject must allow one bounded correction.");
        }

        [TestMethod]
        public void Drt67Closeout_SpawnFallbackCentersWithoutDurable()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string fallback = Between(coordinator,
                "private void ApplyLegacySpawnFallback",
                "private static void TraceSpawnPlacement");

            Assert.IsTrue(fallback.Contains(
                    "StickySpawnPolicy.CenterInWorkArea(") &&
                fallback.Contains("Screen.FromRectangle(Bounds)"),
                "The degraded spawn fallback must center on Penny's current working area.");
            Assert.IsFalse(fallback.Contains("Left - 332") ||
                fallback.Contains("Right + 12") ||
                fallback.Contains("PreferredDisplayTargetKey"),
                "The fallback must not use beside-pet placement or fabricate a durable identity.");
        }

        [TestMethod]
        public void Drt9_DockUsesImmutableMailboxAndNativeDeferBatch()
        {
            string host = ReadSource("StickyUiHost.cs");
            string batch = ReadSource(
                "Infrastructure/Display/WindowsBatchWindowPlacementExecutor.cs");
            string native = ReadSource(
                "Infrastructure/Display/NativeDisplayConfig.cs");
            string dock = ReadSource(
                "Features/StickyNotes/DockWindowFacts.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            Assert.IsTrue(dock.Contains(
                    "internal sealed class DockPlanMailbox") &&
                dock.Contains("TakeLatest()") &&
                !dock.Contains("DockBatchLayout"),
                "The mutable DockBatchLayout must be retired for the immutable mailbox.");
            Assert.IsTrue(host.Contains("PostLatestDockPlan(") &&
                host.Contains("mailbox.TakeLatest()") &&
                host.Contains("WindowsBatchWindowPlacementExecutor.Apply("),
                "The host must apply the newest plan through the native batch executor.");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyLatestDockPlan(",
                "private void PostEvent");
            Assert.IsFalse(apply.Contains("SetBounds(new StickyUiBounds"),
                "The batch must not fall back to per-session SetBounds.");
            Assert.IsTrue(batch.Contains("BeginDeferWindowPos(") &&
                batch.Contains("DeferWindowPos(") &&
                batch.Contains("EndDeferWindowPos(") &&
                native.Contains(
                    "static extern IntPtr BeginDeferWindowPos("),
                "Followers must move in one native deferred batch.");
            Assert.IsTrue(coordinator.Contains(
                    "_stickyUiHost.PostLatestDockPlan(") &&
                coordinator.Contains("new DockPlacementPlan("),
                "The drag coordinator must post immutable plans into the mailbox.");
        }

        [TestMethod]
        public void Drt9_LiveBatchNeverWritesDurablePreferred()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string batch = Between(coordinator,
                "private void ApplyLiveDockBatch",
                "private void SetActiveDockGroup");
            Assert.IsFalse(batch.Contains("PreferredDisplayTargetKey") ||
                batch.Contains("CommitHostedStickyPreferred"),
                "A live drag batch must never commit the durable preferred placement.");

            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyLatestDockPlan(",
                "private void PostEvent");
            Assert.IsFalse(apply.Contains("Preferred"),
                "The STA batch executor must not touch durable preferred fields.");
        }

        [TestMethod]
        public void Drt10_LiveDragUsesPlannerDrivenBySourceFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string plannerPath = Between(coordinator,
                "private List<DockLayoutTarget> PlanLiveDockTargets",
                "private void CompleteStickyDockDrag");

            Assert.IsTrue(plannerPath.Contains(
                    "_placementRuntime.GetEffective(") &&
                plannerPath.Contains("DockPlacementPlanner.Plan(") &&
                plannerPath.Contains("DisplayGeometry.PhysicalToLocal(") &&
                plannerPath.Contains("BuildDockChainOrder(seed)"),
                "The live drag must be planned from the source window's actual facts.");
            Assert.IsFalse(plannerPath.Contains("WindowsDisplayResolver") ||
                plannerPath.Contains("Screen.FromRectangle") ||
                plannerPath.Contains("CalculateDockTranslationTargets"),
                "Followers must never pick a target display or translate old coordinates.");
            string move = Between(coordinator,
                "private void MoveStickyDockDrag",
                "private List<DockLayoutTarget> PlanLiveDockTargets");
            Assert.IsTrue(move.Contains("PlanLiveDockTargets(seed, facts)") &&
                !move.Contains("CalculateDockTranslationTargets("),
                "The live move path must route through the planner.");
        }

        [TestMethod]
        public void Drt10_PlanSurfaceAndDpiComeFromSourceFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string batch = Between(coordinator,
                "private void ApplyLiveDockBatch",
                "private void SetActiveDockGroup");

            Assert.IsTrue(batch.Contains(
                    "_placementRuntime.GetEffective(sourceNoteId)") &&
                batch.Contains("targetDpi = sourceFacts.Dpi") &&
                batch.Contains("FindByRuntimeGdiName("),
                "The batch plan surface and DPI must derive from the source facts.");
            Assert.IsFalse(batch.Contains("WindowsDisplayResolver") ||
                batch.Contains("MonitorFromRect"),
                "The live batch must not re-guess the target display.");
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
