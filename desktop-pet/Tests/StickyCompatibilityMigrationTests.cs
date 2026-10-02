using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyCompatibilityMigrationTests
    {
        [TestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        [DataRow(6)]
        [DataRow(7)]
        [DataRow(8)]
        [DataRow(9)]
        [DataRow(10)]
        [DataRow(11)]
        public async Task HistoricalFileUpgradePreservesOriginalBackupAndStabilizes(int version)
        {
            string directory = Path.Combine(Path.GetTempPath(), "penny-compat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string primary = Path.Combine(directory, "notes.dat");
            byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-v" + version + ".txt"));
            try
            {
                File.WriteAllBytes(primary, original);
                var loaded = StickyFeature.LoadFromFile(primary);
                Assert.IsTrue(loaded.LoadSucceeded);
                Assert.AreEqual(1, loaded.Count);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(primary),
                    "Reading a supported historical file must not rewrite it.");

                Assert.IsTrue((await loaded.SaveBarrierAsync()).Succeeded);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(primary + ".bak"),
                    "The first upgrade retains the exact old file, including its original encoding/newline.");
                string upgraded = File.ReadAllText(primary);
                Assert.IsTrue(upgraded.StartsWith(StickyNoteCodec.CurrentVersion + "|"));
                var reloaded = StickyFeature.LoadFromFile(primary);
                Assert.IsTrue((await reloaded.SaveBarrierAsync()).Succeeded);
                Assert.AreEqual(upgraded, File.ReadAllText(primary), "Repeating migration must be stable.");

                string export = Path.Combine(directory, "export.pennysticky");
                Assert.IsTrue((await reloaded.ExportSnapshotAsync(export)).Succeeded);
                var validated = StickyImportBackupValidator.Validate(File.ReadAllLines(export));
                Assert.IsTrue(validated.Succeeded, validated.ErrorMessage);
                var merged = StickyImportMergePlanner.Calculate(reloaded.GetAll(), validated.Notes);
                Assert.AreEqual(0, merged.AddedCount);
                Assert.AreEqual(0, merged.ConflictCount);
                Assert.AreEqual(1, merged.SkippedIdenticalCount);

                // .bak is rolling, not a permanent migration archive. Restore
                // a separately retained original to test the documented rollback.
                string rollback = Path.Combine(directory, "original.dat");
                File.WriteAllBytes(rollback, original);
                var old = StickyFeature.LoadFromFile(rollback);
                Assert.IsTrue(old.LoadSucceeded);
                Assert.AreEqual(loaded.GetAll()[0].Text, old.GetAll()[0].Text);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(rollback));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
