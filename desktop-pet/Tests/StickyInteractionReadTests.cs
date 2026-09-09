using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyInteractionReadTests
    {
        [TestMethod]
        [DataRow(-20, -20, 40, 40, true)]
        [DataRow(10, 0, 20, 20, false)]
        [DataRow(0, 10, 20, 20, false)]
        [DataRow(-10, 0, 10, 10, false)]
        [DataRow(0, -10, 10, 10, false)]
        [DataRow(0, 0, 0, 20, false)]
        [DataRow(0, 0, 20, -1, false)]
        public void SideTabCoverage_UsesPhysicalOverlapAndExcludesTouchingEdges(
            int x, int y, int width, int height, bool expected)
        {
            StickyNoteData note = new StickyNoteData {
                Visible = true, X = x, Y = y, Width = width, Height = height
            };
            Assert.AreEqual(expected, StickyNoteWindowRules.AnyVisibleNoteOverlaps(
                new[] { note }, new DockRect(0, 0, 10, 10)));
        }

        [TestMethod]
        public void SideTabCoverage_IsOrderIndependentAndObservesCurrentVisibility()
        {
            StickyNoteData hidden = new StickyNoteData {
                Visible = false, X = -1000, Y = -1000, Width = 2000, Height = 2000
            };
            StickyNoteData right = new StickyNoteData {
                Visible = true, X = 40, Y = -50, Width = 80, Height = 100
            };
            StickyNoteData left = new StickyNoteData {
                Visible = true, X = -120, Y = -50, Width = 80, Height = 100
            };
            List<StickyNoteData> notes = new List<StickyNoteData> {
                hidden, right, left, null
            };
            IReadOnlyList<StickyNoteData> view = notes.AsReadOnly();
            DockRect leftStrip = new DockRect(-100, 0, 10, 10);
            Assert.IsTrue(StickyNoteWindowRules.AnyVisibleNoteOverlaps(view, leftStrip));
            Assert.IsTrue(StickyNoteWindowRules.AnyVisibleNoteOverlaps(
                view, new DockRect(100, 0, 10, 10)));
            Assert.IsFalse(StickyNoteWindowRules.AnyVisibleNoteOverlaps(
                view, new DockRect(0, 0, 10, 10)));
            Assert.AreSame(hidden, notes[0]);
            Assert.AreSame(right, notes[1]);
            Assert.AreSame(left, notes[2]);
            notes.Reverse();
            Assert.IsTrue(StickyNoteWindowRules.AnyVisibleNoteOverlaps(view, leftStrip));
            left.Visible = false;
            Assert.IsFalse(StickyNoteWindowRules.AnyVisibleNoteOverlaps(view, leftStrip));
        }

        [TestMethod]
        public void ReminderCommands_DetachCollectionsAndPreserveValues()
        {
            DateTime deadline = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            ReminderItem original = new ReminderItem(deadline, "喝水", "n", 12.5F, true);
            ReminderItem[] input = { original };
            StickyNoteUiSnapshot snapshot = StickyNoteUiSnapshot.FromData(
                new StickyNoteData { Id = "n" });
            StickyUiCommand[] commands = {
                StickyUiCommand.Create(snapshot, false, input),
                StickyUiCommand.EnsureSession(snapshot, input),
                StickyUiCommand.UpdateReminders("n", input)
            };
            input[0] = new ReminderItem(deadline.AddDays(1), "其他", "other", 18F, false);
            foreach (StickyUiCommand command in commands)
            {
                Assert.HasCount(1, command.Reminders);
                ReminderItem item = command.Reminders[0];
                Assert.AreEqual(deadline, item.DeadlineUtc);
                Assert.AreEqual("喝水", item.Text);
                Assert.AreEqual("n", item.SourceNoteId);
                Assert.AreEqual(250, item.FontSizeTwips);
                Assert.IsTrue(item.PreAlertEnabled);
            }
            commands[0].Reminders[0] = null;
            Assert.IsNotNull(commands[1].Reminders[0]);
            Assert.IsNotNull(commands[2].Reminders[0]);
        }

        [TestMethod]
        public void ReminderCommands_FilterNullsKeepOrderAndLimitToFive()
        {
            Assert.HasCount(0, StickyUiCommand.UpdateReminders("n", null).Reminders);
            List<ReminderItem> input = new List<ReminderItem> { null };
            for (int index = 0; index < 7; index++)
                input.Add(new ReminderItem(DateTime.UtcNow.AddDays(1), "item" + index));
            StickyUiCommand command = StickyUiCommand.UpdateReminders("n", input);
            input.Clear();
            Assert.HasCount(5, command.Reminders);
            for (int index = 0; index < 5; index++)
                Assert.AreEqual("item" + index, command.Reminders[index].Text);
        }
    }
}
