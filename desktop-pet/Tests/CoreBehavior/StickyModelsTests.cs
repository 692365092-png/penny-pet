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
        public void StickyNoteWindowRules_KeepTabsTopMostWithOrWithoutVisibleNotes()
        {
            Assert.IsTrue(
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(false));
            Assert.IsFalse(
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true));
        }

        [TestMethod]
        public void StickyNoteData_CloneForPersistenceCopiesNestedState()
        {
            StickyNoteData source = new StickyNoteData();
            source.Title = "source";
            source.Text = "body";
            source.IsTodoList = true;
            source.TodoItems.Add(new StickyTodoItem("todo",
                StickyTodoState.InProgress, true));
            source.IsSchedule = true;
            source.ScheduleItems.Add(new StickyScheduleItem("schedule",
                new DateTime(2026, 8, 28), true));

            StickyNoteData copy = source.CloneForPersistence();

            Assert.AreEqual(source.Id, copy.Id);
            Assert.AreEqual(source.Title, copy.Title);
            Assert.AreEqual(source.Text, copy.Text);
            Assert.AreEqual(1, copy.TodoItems.Count);
            Assert.AreEqual(StickyTodoState.InProgress,
                copy.TodoItems[0].State);
            Assert.IsTrue(copy.TodoItems[0].IsPinned);
            Assert.AreEqual(1, copy.ScheduleItems.Count);
            Assert.AreEqual(new DateTime(2026, 8, 28),
                copy.ScheduleItems[0].TargetDate);

            copy.TodoItems[0].Text = "changed";
            copy.ScheduleItems[0].Text = "changed";
            Assert.AreEqual("todo", source.TodoItems[0].Text);
            Assert.AreEqual("schedule", source.ScheduleItems[0].Text);
        }

        [TestMethod]
        public void ShortItemText_UsesOneSharedDisplayBudget()
        {
            Assert.IsTrue(ShortItemText.Fits(new string('中', 50)));
            Assert.IsFalse(ShortItemText.Fits(new string('中', 51)));
            Assert.IsTrue(ShortItemText.Fits(new string('W', 100)));
            Assert.AreEqual(50,
                ShortItemText.NormalizeAndTruncate(new string('中', 51)).Length);
            Assert.AreEqual("one two",
                ShortItemText.Normalize("  one\r\n\t two  "));
        }
    }
}
