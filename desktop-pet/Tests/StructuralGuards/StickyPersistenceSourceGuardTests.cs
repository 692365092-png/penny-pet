using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
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
        public void StickyManager_UsesBoundedImportPreviewModes()
        {
            string manager = ReadSource(
                "Features/StickyNotes/StickyNotes.cs");
            string persistence = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();

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
            string presentation = ReadSource("PetStickyPresentation.cs");
            Assert.IsTrue(presentation.Contains("commands.PrepareImport = PrepareStickyNotesImport") &&
                presentation.Contains("commands.ConfirmImport = CommitStickyNotesImport") &&
                presentation.Contains("commands.FullRestore = RestoreStickyNotesBackup") &&
                manager.Contains("高级：完整恢复…"),
                "The manager must receive typed prepare/confirm commands from PetForm.");
        }

        [TestMethod]
        public void Drt6_CodecWritesV11WhileKeepingHistoricalVersions()
        {
            string codec = ReadSource("Core/StickyNotes/StickyNoteCodec.cs");

            Assert.IsTrue(codec.Contains(
                    "internal const int VersionElevenFieldCount = 37") &&
                codec.Contains("CurrentVersion = VersionEleven"),
                "The codec must emit the documented v11 format; historical parsing is covered by fixture tests.");
            Assert.IsTrue(codec.Contains(
                    "Encode(preferred?.PreferredTargetKey") &&
                codec.Contains("local.Width.ToString("),
                "v11 must persist the durable preferred target and local rect.");
        }

        [TestMethod]
        public void Drt6Supplement_SpawnUsesCenteredPolicyWithoutCascade()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();

            Assert.IsTrue(coordinator.Contains(
                    "StickySpawnPolicy.PlanCenteredSpawn(") &&
                coordinator.Contains("PrepareStickyNoteDraft(") &&
                coordinator.Contains("Notes.CreateDraft(text, Point.Empty)"),
                "Spawn must route through the centered pure policy on a draft.");
            Assert.IsFalse(coordinator.Contains("% 7) * 18") ||
                coordinator.Contains("12 + offset") ||
                coordinator.Contains("StickyPlacementMath.FromSpawn("),
                "The cascade and beside-pet spawn paths must be retired.");
        }

        [TestMethod]
        public void R17_PersistenceRetryOwnership_IsOutsidePetForm()
        {
            string form = ReadSource("PetForm.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string runtime = ReadSource(
                "Infrastructure/Persistence/PetPersistenceRuntime.cs");

            Assert.IsTrue(form.Contains("new PetPersistenceRuntime(") &&
                form.Contains("_persistence.Notice += PersistenceNoticeReceived"),
                "Pet composition should create one application persistence runtime.");
            Assert.IsFalse(form.Contains("_persistenceRetryTimer") ||
                form.Contains("RetryUnsavedPersistence") ||
                form.Contains("SaveFailed += PersistenceSaveFailed"),
                "PetForm must not poll writer state or subscribe to raw save failures.");
            Assert.IsTrue(coordinator.Contains("PersistenceNoticeReceived") &&
                !coordinator.Contains("RetryUnsavedPersistence") &&
                !coordinator.Contains("_lastPersistenceWarningUtc"),
                "Pet presentation should consume only high-level persistence notices.");
            Assert.IsTrue(runtime.Contains("HasPendingSaves") &&
                runtime.Contains("RequestAutosave()") &&
                runtime.Contains("_retryArmed") &&
                runtime.Contains("PersistenceNoticeKind.Recovered"),
                "The persistence runtime must own single retry scheduling and recovery state.");
        }

    }
}
