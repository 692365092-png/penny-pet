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
        public void StickyNoteCodec_RoundTripsCurrentVersionWithoutWindowsTypes()
        {
            StickyNoteData source = new StickyNoteData
            {
                Id = "codec-note",
                Title = "旅行清单",
                Text = "证件与充电器",
                RichTextRtf = "{\\rtf1\\ansi test}",
                FontFamilyName = "Microsoft YaHei UI",
                FontSizeTwips = 320,
                ColorArgb = unchecked((int)0xFF112233),
                TextColorArgb = unchecked((int)0xFFFFFFFF),
                BackgroundOpacityPercent = 75,
                DockGroupId = "codec-note",
                DockGroupOrder = 0,
                IsSchedule = true,
                IsTodoList = true
            };
            source.ScheduleItems.Add(new StickyScheduleItem("出发",
                new DateTime(2030, 5, 4), true));

            StickyNoteData restored = StickyNoteCodec.ParseLine(
                StickyNoteCodec.SerializeLine(source));

            Assert.IsNotNull(restored);
            Assert.AreEqual(source.Id, restored.Id);
            Assert.AreEqual(source.Title, restored.Title);
            Assert.AreEqual(source.RichTextRtf, restored.RichTextRtf);
            Assert.AreEqual(source.ColorArgb, restored.ColorArgb);
            Assert.AreEqual(source.TextColorArgb, restored.TextColorArgb);
            Assert.IsTrue(restored.IsSchedule);
            Assert.IsFalse(restored.IsTodoList);
            Assert.AreEqual(1, restored.ScheduleItems.Count);
            Assert.AreEqual("出发", restored.ScheduleItems[0].Text);
            Assert.IsTrue(restored.ScheduleItems[0].IsPinned);
        }

        [TestMethod]
        public void StickyNoteCodec_RoundTripsThreeTodoStates()
        {
            StickyNoteData source = new StickyNoteData { IsTodoList = true };
            source.TodoItems.Add(new StickyTodoItem("未完成",
                StickyTodoState.Pending));
            source.TodoItems.Add(new StickyTodoItem("进行中",
                StickyTodoState.InProgress));
            source.TodoItems.Add(new StickyTodoItem("已完成",
                StickyTodoState.Completed));

            StickyNoteData restored = StickyNoteCodec.ParseLine(
                StickyNoteCodec.SerializeLine(source));

            Assert.AreEqual(StickyTodoState.Pending,
                restored.TodoItems[0].State);
            Assert.AreEqual(StickyTodoState.InProgress,
                restored.TodoItems[1].State);
            Assert.AreEqual(StickyTodoState.Completed,
                restored.TodoItems[2].State);
        }

        private static StickyNoteData ParsePreferredFile(string key, string x, string y, string width, string height)
        {
            string[] fields = StickyNoteCodec.SerializeLine(new StickyNoteData { Id = "v11-input" }).Split('|');
            fields[32] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key));
            fields[33] = x; fields[34] = y; fields[35] = width; fields[36] = height;
            return StickyNoteCodec.ParseLine(String.Join("|", fields));
        }

        [TestMethod]
        public void StickyNoteCodec_V11RoundTripsPreferredPlacement()
        {
            StickyNoteData source = new StickyNoteData
            {
                Id = "v11-preferred",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY2",
                new LogicalRect { X = -150, Y = 40, Width = 320, Height = 300 }),
                PreferredPlacement = new WindowPlacementPreference("mdp:home",
                    new LogicalRect { X = -150, Y = 40, Width = 320, Height = 300 })
            };
            string line = StickyNoteCodec.SerializeLine(source);
            Assert.IsTrue(line.StartsWith("11|", StringComparison.Ordinal));
            Assert.AreEqual(StickyNoteCodec.CurrentFieldCount,
                line.Split('|').Length);

            StickyNoteData restored = StickyNoteCodec.ParseLine(line);
            Assert.AreEqual("mdp:home",
                restored.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(-150, restored.PreferredPlacement.LocalLogicalRect.X);
            Assert.AreEqual(40, restored.PreferredPlacement.LocalLogicalRect.Y);
            Assert.AreEqual(320, restored.PreferredPlacement.LocalLogicalRect.Width);
            Assert.AreEqual(300, restored.PreferredPlacement.LocalLogicalRect.Height);
        }

        [TestMethod]
        public void StickyNoteCodec_V11InvalidPreferredKeyClearsLocalRect()
        {
            StickyNoteData restored = ParsePreferredFile(String.Empty, "10", "20", "320", "300");
            Assert.IsNull(restored.PreferredPlacement);
        }

        [TestMethod]
        public void StickyNoteCodec_V11HugePreferredSizeIsClamped()
        {
            StickyNoteData restored = ParsePreferredFile("mdp:huge", "0", "0", "123456789", "300");
            Assert.AreEqual(StickyNoteCodec.MaximumLocalLogicalValue, restored.PreferredPlacement.LocalLogicalRect.Width);
            Assert.AreEqual(300, restored.PreferredPlacement.LocalLogicalRect.Height);
        }

        [TestMethod]
        public void StickyNoteCodec_V11KeyWithNonPositiveSizeDegradesToUnset()
        {
            StickyNoteData restored = ParsePreferredFile("mdp:negative", "5", "6", "320", "-987654321");
            Assert.IsNull(restored.PreferredPlacement);
        }

        [TestMethod]
        [DataRow("bad", "30", "320", "300", true, 0, 30)]
        [DataRow("-10", "bad", "320", "300", true, -10, 0)]
        [DataRow("10", "20", "bad", "300", false, 0, 0)]
        [DataRow("10", "20", "320", "0", false, 0, 0)]
        public void StickyNoteCodec_PreferredFieldsRetainHistoricalMalformedNumberRules(
            string x, string y, string width, string height, bool valid, int expectedX, int expectedY)
        {
            var note = ParsePreferredFile("  mdp:home  ", x, y, width, height);
            if (!valid) { Assert.IsNull(note.PreferredPlacement); return; }
            Assert.AreEqual("mdp:home", note.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(expectedX, note.PreferredPlacement.LocalLogicalRect.X);
            Assert.AreEqual(expectedY, note.PreferredPlacement.LocalLogicalRect.Y);
        }

        [TestMethod]
        [DataRow(1024, true)]
        [DataRow(1025, false)]
        public void StickyNoteCodec_PreferredKeyLimitIsCheckedAtTheFileBoundary(int length, bool valid)
        {
            var note = ParsePreferredFile(new string('k', length), "-10", "20", "320", "300");
            Assert.AreEqual(valid, note.PreferredPlacement != null);
            var saved = StickyNoteCodec.SerializeLine(note).Split('|');
            if (!valid) CollectionAssert.AreEqual(new[] { "", "0", "0", "0", "0" },
                saved.Skip(32).ToArray());
        }

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
        public void StickyNoteCodec_LoadsEveryHistoricalGoldenFixture(
            int version)
        {
            string fixture = Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-v" + version + ".txt");
            StickyNoteData restored = StickyNoteCodec.ParseLine(
                File.ReadAllText(fixture, Encoding.UTF8).Trim(), out string legacyParent);

            Assert.IsNotNull(restored, "Fixture v" + version +
                " must remain readable.");
            Assert.AreEqual("legacy-v" + version, restored.Id);
            Assert.AreEqual("Legacy body", restored.Text);
            if (version >= 2)
            {
                Assert.AreEqual("Legacy title", restored.Title);
                Assert.AreEqual(1, restored.TodoItems.Count);
                Assert.AreEqual("done", restored.TodoItems[0].Text);
            }
            if (version >= 4)
                Assert.AreEqual("{\\rtf1\\ansi legacy}",
                    restored.RichTextRtf);
            if (version >= 5)
                Assert.AreEqual("Arial", restored.FontFamilyName);
            if (version >= 7)
                Assert.AreEqual("group-root", legacyParent);
            if (version >= 8)
            {
                Assert.AreEqual("group-root", restored.DockGroupId);
                Assert.AreEqual(3, restored.DockGroupOrder);
            }
            if (version >= 9)
            {
                Assert.IsTrue(restored.IsSchedule);
                Assert.IsFalse(restored.IsTodoList);
                Assert.AreEqual(1, restored.ScheduleItems.Count);
                Assert.AreEqual("2030 schedule",
                    restored.ScheduleItems[0].Text);
            }
            if (version >= 10)
            {
                Assert.AreEqual("\\\\.\\DISPLAY1", restored.LegacyPlacement.RuntimeGdiName);
                Assert.AreEqual(10, restored.LegacyPlacement.Logical.X);
                Assert.AreEqual(20, restored.LegacyPlacement.Logical.Y);
                Assert.AreEqual(300, restored.LegacyPlacement.Logical.Width);
                Assert.AreEqual(240, restored.LegacyPlacement.Logical.Height);
            }
            if (version >= 11)
            {
                Assert.AreEqual("mdp:legacy-1",
                    restored.PreferredPlacement.PreferredTargetKey);
                Assert.AreEqual(10, restored.PreferredPlacement.LocalLogicalRect.X);
                Assert.AreEqual(20, restored.PreferredPlacement.LocalLogicalRect.Y);
                Assert.AreEqual(300, restored.PreferredPlacement.LocalLogicalRect.Width);
                Assert.AreEqual(240, restored.PreferredPlacement.LocalLogicalRect.Height);
            }
        }

        [TestMethod]
        public void StickyNoteCodec_RoundTripsDisplayLocalRect()
        {
            StickyNoteData note = new StickyNoteData
            {
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY1",
                new LogicalRect { X = 120, Y = 80, Width = 320, Height = 300 })};
            StickyNoteData restored = StickyNoteCodec.ParseLine(
                StickyNoteCodec.SerializeLine(note));

            Assert.AreEqual("\\\\.\\DISPLAY1", restored.LegacyPlacement.RuntimeGdiName);
            Assert.AreEqual(120, restored.LegacyPlacement.Logical.X);
            Assert.AreEqual(80, restored.LegacyPlacement.Logical.Y);
            Assert.AreEqual(320, restored.LegacyPlacement.Logical.Width);
            Assert.AreEqual(300, restored.LegacyPlacement.Logical.Height);
            Assert.IsTrue(StickyNoteCodec.SerializeLine(restored)
                .StartsWith(
                    StickyNoteCodec.CurrentVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "|", StringComparison.Ordinal));
        }

        [TestMethod]
        public void StickyNoteCodec_RepairsV10OnlyAtTheFileBoundary()
        {
            var valid = ParseLegacyFile("DISPLAY1", "10", "20", "320", "300");
            Assert.AreEqual("DISPLAY1", valid.LegacyPlacement.RuntimeGdiName);
            Assert.AreEqual(10, valid.LegacyPlacement.Logical.X);
            Assert.AreEqual(320, valid.LegacyPlacement.Logical.Width);
            var missing = ParseLegacyFile(String.Empty, "5", "6", "320", "300");
            Assert.IsNull(missing.LegacyPlacement);
            var oversized = ParseLegacyFile("DISPLAY1", "0", "0", "2147483647", "2147483647");
            Assert.AreEqual(20000, oversized.LegacyPlacement.Logical.Width);
            Assert.AreEqual(20000, oversized.LegacyPlacement.Logical.Height);
        }

        private static StickyNoteData ParseLegacyFile(string gdi, string x, string y, string width, string height)
        {
            string[] fields = StickyNoteCodec.SerializeLine(new StickyNoteData { Id = "legacy-input" }).Split('|');
            fields[27] = Convert.ToBase64String(Encoding.UTF8.GetBytes(gdi));
            fields[28] = x; fields[29] = y; fields[30] = width; fields[31] = height;
            return StickyNoteCodec.ParseLine(String.Join("|", fields));
        }
    }
}
