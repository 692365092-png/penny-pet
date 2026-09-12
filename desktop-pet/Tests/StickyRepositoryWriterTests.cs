using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyRepositoryWriterTests
    {
        private static string DirectoryForTest()
        {
            string path = Path.Combine(Path.GetTempPath(), "penny-writer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static IEnumerable<string> BlockedLines(StickyWriteRequest request,
            ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            foreach (StickyNoteData note in request.Snapshot)
                yield return StickyNoteCodec.SerializeLine(note);
        }

        [TestMethod]
        public void EmergencyExportCompletesWhilePrimaryAtomicWriteIsBlocked()
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            string export = Path.Combine(directory, "rescue.pennysticky");
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var written = new ConcurrentQueue<string>();
                var repository = new StickyNoteRepository(primary, request =>
                {
                    AtomicTextFile.WriteAllLines(primary, BlockedLines(request, entered, release), true);
                    written.Enqueue(request.Snapshot[0].Text);
                    return PersistenceResult.Success();
                });
                StickyNoteData note = repository.CreateDraft("captured", Point.Empty);
                repository.SaveAsync();
                Task<PersistenceResult> exported = null;
                bool independent = false;
                try
                {
                    Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                    note.Text = "newer content";
                    note.PreferredDisplayTargetKey = "mdp:one";
                    note.PreferredLocalLogicalWidth = 320;
                    note.PreferredLocalLogicalHeight = 333;
                    repository.SaveAsync();
                    exported = Task.Run(() => repository.ExportSnapshot(export));
                    independent = exported.Wait(TimeSpan.FromSeconds(2));
                    Assert.IsTrue(repository.HasUnsavedChanges);
                    Assert.IsTrue(repository.HasPendingSaves);
                }
                finally
                {
                    release.Set();
                    Assert.IsTrue(repository.WaitForPendingSaves(TimeSpan.FromSeconds(5)).Succeeded);
                }
                try
                {
                    Assert.IsTrue(independent, "A stalled primary write must not hold the export path.");
                    Assert.IsTrue(exported.Result.Succeeded);
                    CollectionAssert.AreEqual(new[] { "captured", "newer content" }, written.ToArray());
                    StickyNoteData saved = StickyNoteRepository.LoadFromFile(primary).Find(note.Id);
                    StickyNoteData rescued = StickyNoteRepository.LoadFromFile(export).Find(note.Id);
                    Assert.AreEqual("newer content", saved.Text);
                    Assert.AreEqual("newer content", rescued.Text);
                    Assert.AreEqual(333, rescued.PreferredLocalLogicalHeight);
                }
                finally { Directory.Delete(directory, true); }
            }
        }

        [TestMethod]
        public void ExportAndSaveCopyCannotClearAFailedWorkspaceSave()
        {
            string directory = DirectoryForTest();
            var failure = new IOException("disk full");
            var repository = new StickyNoteRepository(Path.Combine(directory, "notes.dat"),
                request => PersistenceResult.Failure(failure));
            try
            {
                repository.CreateDraft("unsaved", Point.Empty);
                Assert.IsFalse(repository.Save().Succeeded);
                Assert.IsTrue(repository.ExportSnapshot(Path.Combine(directory, "export.dat")).Succeeded);
                Assert.IsTrue(repository.SaveToFile(Path.Combine(directory, "copy.dat")).Succeeded);
                Assert.IsTrue(repository.HasUnsavedChanges);
                Assert.AreSame(failure, repository.LastSaveError);
                Assert.IsFalse(repository.WaitForPendingSaves().Succeeded);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void ExportCannotBypassOwnershipOfThePrimaryOrRollingBackup()
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            var repository = StickyNoteRepository.LoadFromFile(primary);
            try
            {
                repository.CreateDraft("original", Point.Empty);
                Assert.IsTrue(repository.Save().Succeeded);
                string before = File.ReadAllText(primary);
                Assert.IsFalse(repository.ExportSnapshot(Path.Combine(directory, ".", "notes.dat")).Succeeded);
                Assert.IsFalse(repository.ExportSnapshot(primary + ".bak").Succeeded);
                Assert.AreEqual(before, File.ReadAllText(primary));
                Assert.IsFalse(repository.HasUnsavedChanges);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void FailedImportBackupPublishesNeitherDiskNorMemoryReplacement()
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            string backup = Path.Combine(directory, "occupied");
            Directory.CreateDirectory(backup);
            var repository = StickyNoteRepository.LoadFromFile(primary);
            try
            {
                StickyNoteData original = repository.CreateDraft("original", Point.Empty);
                Assert.IsTrue(repository.Save().Succeeded);
                string before = File.ReadAllText(primary);
                var replacement = new[] { new StickyNoteData { Id = "replacement", Text = "new" } };
                Assert.IsFalse(repository.CommitFullRestore(replacement, backup).Succeeded);
                Assert.AreSame(original, repository.Find(original.Id));
                Assert.IsNull(repository.Find("replacement"));
                Assert.AreEqual(before, File.ReadAllText(primary));
                Assert.IsTrue(repository.Save().Succeeded);
                Assert.IsFalse(repository.HasUnsavedChanges);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void FullRestoreWritesPreChangeBackupAndPublishesAfterSuccess()
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            string backup = Path.Combine(directory, "before.pennysticky");
            var repository = StickyNoteRepository.LoadFromFile(primary);
            try
            {
                StickyNoteData original = repository.CreateDraft("original", Point.Empty);
                repository.SaveAsync();
                var replacement = new[] { new StickyNoteData { Id = "replacement", Text = "new" } };
                Assert.IsTrue(repository.CommitFullRestore(replacement, backup).Succeeded);
                Assert.IsNull(repository.Find(original.Id));
                Assert.AreEqual("new", repository.Find("replacement").Text);
                Assert.AreEqual("original", StickyNoteRepository.LoadFromFile(backup).Find(original.Id).Text);
                Assert.AreEqual("new", StickyNoteRepository.LoadFromFile(primary).Find("replacement").Text);
                Assert.IsFalse(repository.HasUnsavedChanges);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SavingPreservesHiddenSlotsAndIndependentHeightsWithoutRepairingTheLiveModel(bool async)
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            var repository = StickyNoteRepository.LoadFromFile(primary);
            try
            {
                var notes = new[] { repository.CreateDraft("A", Point.Empty),
                    repository.CreateDraft("B", Point.Empty), repository.CreateDraft("C", Point.Empty) };
                for (int i = 0; i < notes.Length; i++)
                {
                    notes[i].Height = 310 + i * 60;
                    notes[i].PreferredDisplayTargetKey = "mdp:one";
                    notes[i].PreferredLocalLogicalWidth = 320;
                    notes[i].PreferredLocalLogicalHeight = notes[i].Height;
                }
                StickyDockGroups.ApplyOrderedGroup(notes);
                notes[1].Visible = false;
                string[] before = Array.ConvertAll(notes, StickyNoteCodec.SerializeLine);
                if (async) repository.SaveAsync();
                else Assert.IsTrue(repository.Save().Succeeded);
                Assert.IsTrue(repository.WaitForPendingSaves().Succeeded);
                CollectionAssert.AreEqual(before, Array.ConvertAll(notes, StickyNoteCodec.SerializeLine));
                // Parse the raw file: repository load-time repair cannot mask a write regression.
                var saved = Array.ConvertAll(File.ReadAllLines(primary), StickyNoteCodec.ParseLine);
                Assert.AreEqual(3, saved.Length);
                for (int i = 0; i < saved.Length; i++)
                {
                    Assert.AreEqual(notes[i].Id, saved[i].Id);
                    Assert.AreEqual(i, saved[i].DockGroupOrder);
                    Assert.AreEqual(notes[i].Height, saved[i].Height);
                    Assert.AreEqual(notes[i].PreferredLocalLogicalHeight, saved[i].PreferredLocalLogicalHeight);
                }
                Assert.IsFalse(saved[1].Visible);
                Assert.AreEqual(saved[0].Id, saved[2].DockParentId);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(2)]
        public void DeleteOwnsMembershipAndPreservesHiddenSurvivorsAcrossRestart(int deletedIndex)
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            try
            {
                StickyNoteRepository repository = StickyNoteRepository.LoadFromFile(primary);
                StickyNoteData[] notes = { repository.CreateDraft("A", Point.Empty),
                    repository.CreateDraft("B", Point.Empty), repository.CreateDraft("C", Point.Empty),
                    repository.CreateDraft("D", Point.Empty) };
                StickyDockGroups.ApplyOrderedGroup(notes);
                notes[1].Visible = notes[3].Visible = false;
                string hiddenId = notes[1].Id;
                Assert.IsTrue(repository.Remove(notes[deletedIndex]));
                StickyNoteRepository restored = StickyNoteRepository.LoadFromFile(primary);
                List<StickyNoteData> group = StickyDockGroups.GetOrderedGroup(restored.GetAll(), restored.Find(hiddenId));
                Assert.AreEqual(3, group.Count);
                Assert.AreEqual(hiddenId, group[deletedIndex == 0 ? 0 : 1].Id);
                Assert.AreEqual(notes[3].Id, group[2].Id);
                Assert.IsFalse(group[2].Visible);
                Assert.IsNull(restored.Find(notes[deletedIndex].Id));
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void FutureSchemaIsRejectedBeforeAnyWriterOrExportCanReplaceIt()
        {
            string directory = DirectoryForTest();
            string primary = Path.Combine(directory, "notes.dat");
            const string future = "12|future payload";
            File.WriteAllText(primary, future);
            var repository = StickyNoteRepository.LoadFromFile(primary);
            try
            {
                Assert.IsTrue(repository.IsFutureSchemaBlocked);
                Assert.IsFalse(repository.Save().Succeeded);
                repository.SaveAsync();
                Assert.IsFalse(repository.ExportSnapshot(Path.Combine(directory, "copy.dat")).Succeeded);
                Assert.IsFalse(repository.HasPendingSaves);
                Assert.AreEqual(future, File.ReadAllText(primary));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
