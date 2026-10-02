using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {

        [TestMethod]
        public void FutureStickyFixture_UsesTheNextVersionAndOpaquePayload()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-vFuture.txt");
            int futureVersion = StickyNoteCodec.CurrentVersion + 1;
            string line = File.ReadAllText(fixture, Encoding.UTF8).Trim()
                .Replace("{VERSION}", futureVersion.ToString());

            Assert.AreEqual(futureVersion.ToString(),
                line.Substring(0, line.IndexOf('|')));
            Assert.IsNull(StickyNoteCodec.ParseLine(line),
                "The current codec must not interpret a future payload.");
        }

        [TestMethod]
        public void StickyImportBackupValidator_AcceptsHistoricalCodecFixtures()
        {
            for (int version = 1; version <= StickyNoteCodec.CurrentVersion;
                version++)
            {
                string fixture = Path.Combine(AppContext.BaseDirectory,
                    "Tests", "Fixtures", "sticky-v" + version + ".txt");
                StickyImportValidationResult result =
                    StickyImportBackupValidator.Validate(new[]
                    {
                        File.ReadAllText(fixture, Encoding.UTF8).Trim()
                    });
                Assert.IsTrue(result.Succeeded,
                    "Fixture v" + version + " should validate: " +
                    result.ErrorMessage);
                Assert.AreEqual(1, result.Notes.Count);
            }
        }

        [TestMethod]
        public void CurrentCodecOutput_IsAcceptedByImportValidator()
        {
            StickyNoteData note = new StickyNoteData { Id = "current-codec" };
            string line = StickyNoteCodec.SerializeLine(note);

            StickyImportValidationResult result =
                StickyImportBackupValidator.Validate(new[] { line });

            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            Assert.AreEqual(StickyNoteCodec.CurrentFieldCount,
                line.Split('|').Length);
            Assert.AreEqual(1, result.Notes.Count);
        }

        [TestMethod]
        public void V10Fixture_IsAccepted()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-v10.txt");

            StickyImportValidationResult result =
                StickyImportBackupValidator.Validate(new[]
                {
                    File.ReadAllText(fixture, Encoding.UTF8).Trim()
                });

            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            Assert.AreEqual(1, result.Notes.Count);
            Assert.AreEqual("legacy-v10", result.Notes[0].Id);
        }

        [TestMethod]
        public void V10CanonicalPlacement_RoundTripsThroughStrictBackupValidator()
        {
            StickyNoteData note = new StickyNoteData
            {
                Id = "v10-canonical",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY3",
                new LogicalRect { X = -150, Y = 40, Width = 320, Height = 300 })};

            StickyImportValidationResult result =
                StickyImportBackupValidator.Validate(new[]
                {
                    StickyNoteCodec.SerializeLine(note)
                });

            Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            StickyNoteData restored = result.Notes.Single();
            Assert.AreEqual(note.LegacyPlacement.RuntimeGdiName, restored.LegacyPlacement.RuntimeGdiName);
            Assert.AreEqual(note.LegacyPlacement.Logical.X, restored.LegacyPlacement.Logical.X);
            Assert.AreEqual(note.LegacyPlacement.Logical.Y, restored.LegacyPlacement.Logical.Y);
            Assert.AreEqual(note.LegacyPlacement.Logical.Width,
                restored.LegacyPlacement.Logical.Width);
            Assert.AreEqual(note.LegacyPlacement.Logical.Height,
                restored.LegacyPlacement.Logical.Height);
        }

        [TestMethod]
        public void V10MalformedCanonicalFields_AreRejected()
        {
            StickyNoteData note = new StickyNoteData
            {
                Id = "v10-malformed",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY1",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 300 })};
            string[] valid = StickyNoteCodec.SerializeLine(note).Split('|');
            List<string[]> malformed = new List<string[]>();

            string[] badDisplayEncoding = (string[])valid.Clone();
            badDisplayEncoding[27] = "%%%";
            malformed.Add(badDisplayEncoding);

            string[] oversizedDisplayId = (string[])valid.Clone();
            oversizedDisplayId[27] = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(new String('D',
                    StickyNoteCodec.MaximumDisplayIdCharacters + 1)));
            malformed.Add(oversizedDisplayId);

            string[] incompleteLegacy = (string[])valid.Clone();
            incompleteLegacy[27] = String.Empty;
            malformed.Add(incompleteLegacy);

            string[] whitespaceDisplayId = (string[])valid.Clone();
            whitespaceDisplayId[27] = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(" "));
            malformed.Add(whitespaceDisplayId);

            string[] zeroWidth = (string[])valid.Clone();
            zeroWidth[30] = "0";
            malformed.Add(zeroWidth);

            string[] oversizedHeight = (string[])valid.Clone();
            oversizedHeight[31] = (StickyNoteCodec.MaximumLocalLogicalValue + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            malformed.Add(oversizedHeight);

            string[] nonNumericX = (string[])valid.Clone();
            nonNumericX[28] = "not-a-number";
            malformed.Add(nonNumericX);

            string[] outOfRangeY = (string[])valid.Clone();
            outOfRangeY[29] = (StickyNoteCodec.MaximumLocalLogicalValue + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            malformed.Add(outOfRangeY);

            foreach (string[] fields in malformed)
            {
                StickyImportValidationResult result =
                    StickyImportBackupValidator.Validate(new[]
                    {
                        String.Join("|", fields)
                    });
                Assert.IsFalse(result.Succeeded,
                    "Malformed v10 canonical fields must fail strict validation.");
                Assert.AreEqual(0, result.Notes.Count);
            }
        }

        [TestMethod]
        public void StickyImportBackupValidator_RejectsMalformedAndDuplicateBackup()
        {
            StickyNoteData note = new StickyNoteData { Id = "backup-note" };
            string valid = StickyNoteCodec.SerializeLine(note);
            string[] fields = valid.Split('|');

            string missingId = String.Join("|", fields.Select(
                (value, index) => index == 1 ? String.Empty : value));
            StickyImportValidationResult missing =
                StickyImportBackupValidator.Validate(new[] { missingId });
            Assert.IsFalse(missing.Succeeded);
            Assert.AreEqual(0, missing.Notes.Count);

            string badNumber = String.Join("|", fields.Select(
                (value, index) => index == 7 ? "not-a-number" : value));
            StickyImportValidationResult malformed =
                StickyImportBackupValidator.Validate(new[] { badNumber });
            Assert.IsFalse(malformed.Succeeded);
            Assert.AreEqual(0, malformed.Notes.Count);

            StickyImportValidationResult duplicate =
                StickyImportBackupValidator.Validate(new[] { valid, valid });
            Assert.IsFalse(duplicate.Succeeded);
            Assert.AreEqual(0, duplicate.Notes.Count);

            string[] unsupportedFields = (string[])fields.Clone();
            unsupportedFields[0] = "42";
            StickyImportValidationResult unsupported =
                StickyImportBackupValidator.Validate(new[]
                {
                    String.Join("|", unsupportedFields)
                });
            Assert.IsFalse(unsupported.Succeeded);

            string[] encodedFields = (string[])fields.Clone();
            encodedFields[26] = "%%%";
            StickyImportValidationResult badEncoding =
                StickyImportBackupValidator.Validate(new[]
                {
                    String.Join("|", encodedFields)
                });
            Assert.IsFalse(badEncoding.Succeeded);
        }

        [TestMethod]
        public void StickyImportBackupValidator_RejectsCorruptedV1Body()
        {
            string prefix = "1|v1-body|1|1|-1122868|10|20|300|240|" +
                "637000000000000000|637000000000000001|0|";
            StickyImportValidationResult validEmpty =
                StickyImportBackupValidator.Validate(new[] { prefix });
            Assert.IsTrue(validEmpty.Succeeded);

            StickyImportValidationResult malformed =
                StickyImportBackupValidator.Validate(new[] { prefix + "%%%" });
            Assert.IsFalse(malformed.Succeeded);

            string missingBody = prefix.Substring(0, prefix.Length - 1);
            StickyImportValidationResult missing =
                StickyImportBackupValidator.Validate(new[] { missingBody });
            Assert.IsFalse(missing.Succeeded);
        }
    }
}
