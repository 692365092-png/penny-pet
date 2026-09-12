using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void LayeredRenderer_DoesNotKeepGlobalBitmapHandleCache()
        {
            string source = ReadSource("LayeredSpriteRenderer.cs");
            Assert.IsFalse(source.Contains("Dictionary<Bitmap, IntPtr>") ||
                source.Contains("BitmapHandles"),
                "Layered renderer must not keep a global HBITMAP cache.");
        }

        [TestMethod]
        public void StickyPersistence_AllEntryPointsUseOneQueuedWriter()
        {
            string source = ReadSource("Features/StickyNotes/StickyNoteRepository.cs");
            foreach (string entry in new[] { "internal void SaveAsync()",
                "internal PersistenceResult SaveToFile", "internal PersistenceResult ExportSnapshot",
                "private PersistenceResult CommitPreparedSnapshot" })
            {
                string body = RawSource.SliceMethod(source, entry);
                Assert.IsTrue(body.Contains("_writer.Enqueue("), entry);
                Assert.IsFalse(body.Contains("AtomicTextFile.WriteAllLines"), entry);
            }
            string physicalWrite = RawSource.SliceMethod(source,
                "private static PersistenceResult WriteSnapshot");
            Assert.IsFalse(physicalWrite.Contains("NormalizeAll") ||
                physicalWrite.Contains("generation") || physicalWrite.Contains("lock ("),
                "The single writer must only persist its detached request.");
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
                save.IndexOf("CloneNotes(_notes)",
                    StringComparison.Ordinal),
                "A blocked repository must reject save before snapshot generation.");
            Assert.IsTrue(pet.Contains("if (_notes.IsFutureSchemaBlocked)") &&
                pet.Contains("throw _notes.FutureSchemaError;") &&
                host.Contains("catch (UnsupportedStickySchemaException error)") &&
                host.Contains("BuildFutureSchemaBlockedMessage(error)"),
                "Startup must show the dedicated message and exit before Pet UI continues.");
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
    }
}
