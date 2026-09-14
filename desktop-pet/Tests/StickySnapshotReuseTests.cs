using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickySnapshotReuseTests
    {
        private static StickyNoteData Note()
        {
            var note = new StickyNoteData { Id = "note", Text = "before", Visible = true, AlwaysOnTop = false };
            note.TodoItems.Add(new StickyTodoItem("todo", false));
            note.ScheduleItems.Add(new StickyScheduleItem("schedule", new DateTime(2026, 9, 14)));
            return note;
        }

        [TestMethod]
        public void MovingAndResizingReuseContentWithoutRetainingPlacement()
        {
            var note = Note();
            var initial = StickyNoteUiSnapshot.FromData(note);
            var content = StickyNoteUiSnapshot.FromContentData(note, initial);
            Assert.AreNotSame(initial, content);
            note.X = -1500; note.LocalLogicalWidth = 300;
            note.PreferredDisplayTargetKey = "disconnected";
            var moved = StickyNoteUiSnapshot.FromContentData(note, content);
            Assert.AreSame(content, moved);
            Assert.AreEqual(0, moved.X);
            Assert.AreEqual(0, moved.LocalLogicalWidth);
            Assert.AreEqual(String.Empty, moved.PreferredDisplayTargetKey);
        }

        [TestMethod]
        public void WindowFlagsDoNotModifyAnAlreadyPostedSnapshot()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.FromContentData(note);
            note.Visible = false; note.AlwaysOnTop = true;
            var after = StickyNoteUiSnapshot.FromContentData(note, before);
            Assert.AreNotSame(before, after);
            Assert.IsTrue(before.Visible);
            Assert.IsFalse(before.AlwaysOnTop);
            Assert.IsFalse(after.Visible);
            Assert.IsTrue(after.AlwaysOnTop);
            Assert.AreSame(before.TodoItems, after.TodoItems);
            Assert.AreSame(before.ScheduleItems, after.ScheduleItems);
        }

        [TestMethod]
        public void PendingTextAndRichTextChangesDoNotNeedASaveRevision()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.FromContentData(note);
            note.Text = "中文输入中的内容";
            note.RichTextRtf = "{\\rtf1 pending}";
            var after = StickyNoteUiSnapshot.FromContentData(note, before);
            Assert.AreEqual(before.ModifiedUtcTicks, after.ModifiedUtcTicks);
            Assert.AreNotSame(before, after);
            Assert.AreEqual("before", before.Text);
            Assert.AreEqual(note.Text, after.Text);
            Assert.AreEqual(note.RichTextRtf, after.RichTextRtf);
        }

        [TestMethod]
        public void ListEditsWithUnchangedCountAndTimestampAreCaptured()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.FromContentData(note);
            note.TodoItems[0].Text = "edited";
            note.TodoItems[0].IsPinned = true;
            note.ScheduleItems[0].TargetDateTicks = new DateTime(2026, 9, 15).Ticks;
            var after = StickyNoteUiSnapshot.FromContentData(note, before);
            Assert.AreNotSame(before, after);
            Assert.AreEqual("todo", before.TodoItems[0].Text);
            Assert.AreEqual("edited", after.TodoItems[0].Text);
            Assert.IsTrue(after.TodoItems[0].IsPinned);
            Assert.AreNotEqual(before.ScheduleItems[0].TargetDateTicks, after.ScheduleItems[0].TargetDateTicks);
        }

        [TestMethod]
        public void AppearancePreviewAndReminderChangesAreCapturedBeforeSave()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.FromContentData(note);
            note.BackgroundOpacityPercent = 25;
            note.FontSizeTwips = 500;
            note.ReminderUtcTicks = 123;
            var after = StickyNoteUiSnapshot.FromContentData(note, before);
            Assert.AreNotSame(before, after);
            Assert.AreEqual(25, after.BackgroundOpacityPercent);
            Assert.AreEqual(500, after.FontSizeTwips);
            Assert.AreEqual(123L, after.ReminderUtcTicks);
        }

        [TestMethod]
        public void SameContentCannotReuseAnotherNotesIdentity()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.FromContentData(note);
            note.Id = "other";
            var after = StickyNoteUiSnapshot.FromContentData(note, before);
            Assert.AreNotSame(before, after);
            Assert.AreEqual("other", after.NoteId);
        }

        [TestMethod]
        public void RepeatedGeometryFramesDoNotAllocateContentOrReplaceCanonicalRows()
        {
            var note = Note();
            for (int i = 1; i < 100; i++) note.TodoItems.Add(new StickyTodoItem("todo-" + i, false));
            var snapshot = StickyNoteUiSnapshot.FromContentData(note);
            var canonical = snapshot.CreateWorkingCopy();
            var firstRow = canonical.TodoItems[0];
            for (int i = 0; i < 100; i++)
            {
                snapshot = StickyNoteUiSnapshot.FromContentData(note, snapshot);
                snapshot.ApplyContentTo(canonical);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                note.X = i;
                snapshot = StickyNoteUiSnapshot.FromContentData(note, snapshot);
                snapshot.ApplyContentTo(canonical);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(allocated < 1024, "Unchanged content allocated " + allocated + " bytes.");
            Assert.AreSame(firstRow, canonical.TodoItems[0]);
        }
    }
}
