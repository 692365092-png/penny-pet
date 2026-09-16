using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyCodecBoundaryTests
    {
        private static string Fixture(int version)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-v" + version + ".txt"), Encoding.UTF8).Trim();
        }

        [TestMethod]
        public void HistoricalLayoutsRejectEveryTruncatedPrefix()
        {
            for (int version = 1; version <= 11; version++)
            {
                string[] fields = Fixture(version).Split('|');
                for (int count = 1; count < fields.Length; count++)
                    Assert.IsNull(StickyNoteCodec.ParseLine(String.Join("|", fields, 0, count)),
                        "Truncated v" + version + " with " + count + " fields must be rejected.");
            }
        }

        [TestMethod]
        public void HistoricalLayoutsContinueIgnoringTrailingFields()
        {
            for (int version = 1; version <= 11; version++)
            {
                string line = Fixture(version);
                string expected = StickyNoteCodec.SerializeLine(StickyNoteCodec.ParseLine(line));
                string actual = StickyNoteCodec.SerializeLine(StickyNoteCodec.ParseLine(line + "|unused|trailing"));
                Assert.AreEqual(expected, actual, "Extra fields changed v" + version + " decoding.");
            }
        }

        [TestMethod]
        public void OnlyExactSupportedVersionTokensAreAccepted()
        {
            string line = Fixture(11);
            string payload = line.Substring(line.IndexOf('|'));
            foreach (string token in new[] { "", "0", "12", "9999999999999999999",
                "01", "+1", " 1", "1 ", "1.0", "十一", "-1", "1\0" })
                Assert.IsNull(StickyNoteCodec.ParseLine(token + payload),
                    "An unrecognized wire version must not be normalized into a supported one.");
        }
    }
}
