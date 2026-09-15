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
            var initial = StickyNoteUiSnapshot.Capture(note);
            var content = StickyNoteUiSnapshot.Capture(note, initial);
            Assert.AreSame(initial, content);
            note.X = -1500; note.LocalLogicalWidth = 300;
            note.PreferredPlacement = new WindowPlacementPreference("disconnected",
                new LogicalRect { Width = 320, Height = 240 });
            var moved = StickyNoteUiSnapshot.Capture(note, content);
            Assert.AreSame(content, moved);
            var editor = moved.CreateWorkingCopy();
            Assert.AreEqual(new StickyNoteData().X, editor.X);
            Assert.AreEqual(0, editor.LocalLogicalWidth);
            Assert.IsNull(editor.PreferredPlacement);
        }

        [TestMethod]
        public void RestoreTopMostOverrideDoesNotChangeCanonicalOrRebuildContent()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.Capture(note);
            var restore = StickyNoteUiSnapshot.Capture(note, before, alwaysOnTop: true);
            Assert.IsFalse(note.AlwaysOnTop);
            Assert.IsFalse(before.AlwaysOnTop);
            Assert.IsTrue(restore.AlwaysOnTop);
            Assert.IsTrue(restore.CreateWorkingCopy().AlwaysOnTop);
            Assert.AreSame(before.TodoItems, restore.TodoItems);
            Assert.AreSame(before.ScheduleItems, restore.ScheduleItems);
            Assert.AreSame(restore, StickyNoteUiSnapshot.Capture(note, restore, alwaysOnTop: true));
            Assert.IsFalse(StickyNoteUiSnapshot.Capture(note, restore).AlwaysOnTop);
        }

        [TestMethod]
        public void WindowFlagsDoNotModifyAnAlreadyPostedSnapshot()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.Capture(note);
            note.Visible = false; note.AlwaysOnTop = true;
            var after = StickyNoteUiSnapshot.Capture(note, before);
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
            var before = StickyNoteUiSnapshot.Capture(note);
            note.Text = "中文输入中的内容";
            note.RichTextRtf = "{\\rtf1 pending}";
            var after = StickyNoteUiSnapshot.Capture(note, before);
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
            var before = StickyNoteUiSnapshot.Capture(note);
            note.TodoItems[0].Text = "edited";
            note.TodoItems[0].IsPinned = true;
            note.ScheduleItems[0].TargetDateTicks = new DateTime(2026, 9, 15).Ticks;
            var after = StickyNoteUiSnapshot.Capture(note, before);
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
            var before = StickyNoteUiSnapshot.Capture(note);
            note.BackgroundOpacityPercent = 25;
            note.FontSizeTwips = 500;
            note.ReminderUtcTicks = 123;
            var after = StickyNoteUiSnapshot.Capture(note, before);
            Assert.AreNotSame(before, after);
            Assert.AreEqual(25, after.BackgroundOpacityPercent);
            Assert.AreEqual(500, after.FontSizeTwips);
            Assert.AreEqual(123L, after.ReminderUtcTicks);
        }

        [TestMethod]
        public void SameContentCannotReuseAnotherNotesIdentity()
        {
            var note = Note();
            var before = StickyNoteUiSnapshot.Capture(note);
            note.Id = "other";
            var after = StickyNoteUiSnapshot.Capture(note, before);
            Assert.AreNotSame(before, after);
            Assert.AreEqual("other", after.NoteId);
        }

        [TestMethod]
        public void RepeatedGeometryFramesDoNotAllocateContentOrReplaceCanonicalRows()
        {
            var note = Note();
            for (int i = 1; i < 100; i++) note.TodoItems.Add(new StickyTodoItem("todo-" + i, false));
            var snapshot = StickyNoteUiSnapshot.Capture(note);
            var canonical = snapshot.CreateWorkingCopy();
            var firstRow = canonical.TodoItems[0];
            for (int i = 0; i < 100; i++)
            {
                snapshot = StickyNoteUiSnapshot.Capture(note, snapshot);
                snapshot.ApplyContentTo(canonical);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                note.X = i;
                snapshot = StickyNoteUiSnapshot.Capture(note, snapshot);
                snapshot.ApplyContentTo(canonical);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(allocated < 1024, "Unchanged content allocated " + allocated + " bytes.");
            Assert.AreSame(firstRow, canonical.TodoItems[0]);
        }
    }
}
