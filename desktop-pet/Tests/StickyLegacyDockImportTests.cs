using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyLegacyDockImportTests
    {
        private static string Line(string id, string parent, int version = 7, bool visible = true,
            string group = "")
        {
            var note = new StickyNoteData { Id = id, Text = id, Visible = visible, DockGroupId = group };
            string[] fields = StickyNoteCodec.SerializeLine(note, parent).Split('|');
            fields[0] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return String.Join("|", fields, 0, version == 7 ? 23 : StickyNoteCodec.CurrentFieldCount);
        }

        [TestMethod]
        [DataRow(7)]
        [DataRow(11)]
        public void BackupMigratesParentOnlyGroupsBeforeMergeIncludingHiddenSlots(int version)
        {
            var validated = StickyImportBackupValidator.Validate(new[] {
                Line("tail", "middle", version), Line("middle", "root", version, false), Line("root", "", version) });
            Assert.IsTrue(validated.Succeeded, validated.ErrorMessage);
            var source = StickyDockGroups.GetOrderedGroup(validated.Notes, validated.Notes[0]);
            CollectionAssert.AreEqual(new[] { "root", "middle", "tail" }, source.Select(n => n.Id).ToArray());
            Assert.IsFalse(source[1].Visible);
            var merged = StickyImportMergePlanner.Calculate(null, validated.Notes);
            var result = StickyDockGroups.GetOrderedGroup(merged.MergedSnapshot, merged.MergedSnapshot[0]);
            CollectionAssert.AreEqual(new[] { "root", "middle", "tail" }, result.Select(n => n.Id).ToArray());
            Assert.IsTrue(result.All(n => !n.Visible));
            Assert.IsTrue(source[0].Visible, "Merge must leave the validated backup detached and unchanged.");
        }

        [TestMethod]
        public void PartialV7ImportDetachesNewMembersBeforeTheyCanJoinAnExistingNote()
        {
            var validated = StickyImportBackupValidator.Validate(new[] {
                Line("root", ""), Line("middle", "root"), Line("tail", "middle") });
            Assert.IsTrue(validated.Succeeded, validated.ErrorMessage);
            var existing = new StickyNoteData { Id = "root", Text = "root", X = 900, Y = 800 };
            var merge = StickyImportMergePlanner.Calculate(new[] { existing }, validated.Notes);
            Assert.AreEqual(2, merge.AddedCount);
            Assert.IsTrue(merge.MergedSnapshot.All(n => String.IsNullOrEmpty(n.DockGroupId)));
            Assert.AreEqual(900, merge.MergedSnapshot.Single(n => n.Id == "root").X);
            Assert.AreEqual(800, existing.Y);
        }

        [TestMethod]
        public void ExplicitSingletonCannotBePulledIntoALegacyGroupAfterItsOrderIsNormalized()
        {
            var validated = StickyImportBackupValidator.Validate(new[] {
                Line("explicit", "", 11, group: "existing"), Line("legacy", "explicit") });
            Assert.IsTrue(validated.Succeeded, validated.ErrorMessage);
            Assert.AreEqual(2, validated.Notes.Count);
            Assert.IsTrue(validated.Notes.All(n => String.IsNullOrEmpty(n.DockGroupId)));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DuplicateLoadRowCannotReplaceTheFirstAcceptedLegacyParent(bool legacyDirectory)
        {
            string directory = Path.Combine(Path.GetTempPath(), "penny-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "notes.dat");
                File.WriteAllLines(path, new[] { Line("child", "root"), Line("child", "other"),
                    Line("root", ""), Line("other", "") });
                var repository = legacyDirectory
                    ? StickyFeature.LoadFromFileWithLegacyCandidates(Path.Combine(directory, "current.dat"), new[] { path })
                    : StickyFeature.LoadFromFile(path);
                Assert.IsTrue(repository.LoadSucceeded);
                var group = StickyDockGroups.GetOrderedGroup(repository.GetAll(), repository.Find("child"));
                CollectionAssert.AreEqual(new[] { "root", "child" }, group.Select(n => n.Id).ToArray());
                Assert.AreEqual(String.Empty, repository.Find("other").DockGroupId);
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void StrictSalvageMigratesAcceptedLegacyRowsAndWritesCompatibilityLinks()
        {
            string directory = Path.Combine(Path.GetTempPath(), "penny-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "notes.dat");
                File.WriteAllLines(path, new[] { Line("child", "root"), "corrupt row", Line("root", "") });
                var repository = StickyFeature.LoadFromFile(path);
                Assert.IsTrue(repository.LoadSucceeded);
                Assert.IsTrue(repository.RecoveredFromPartialSalvage);
                var group = StickyDockGroups.GetOrderedGroup(repository.GetAll(), repository.Find("root"));
                CollectionAssert.AreEqual(new[] { "root", "child" }, group.Select(n => n.Id).ToArray());
                string childLine = File.ReadAllLines(path).Single(line => line.Split('|')[1] == "child");
                Assert.IsTrue(childLine.StartsWith("11|", StringComparison.Ordinal));
                StickyNoteCodec.ParseLine(childLine, out string parent);
                Assert.AreEqual("root", parent);
                var restarted = StickyFeature.LoadFromFile(path);
                Assert.AreEqual("root", restarted.Find("child").DockGroupId);
                Assert.AreEqual(1, restarted.Find("child").DockGroupOrder);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
