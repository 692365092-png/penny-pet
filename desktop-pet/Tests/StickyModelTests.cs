using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyModelTests
    {
        [TestMethod]
        public void MemoryCommands_NeedNeitherStoreNorWindows()
        {
            var model = new StickyModel();
            var a = model.CreateDraft("A", 10, 20);
            var b = model.CreateDraft("B", 0, 0);
            var c = model.CreateDraft("C", 0, 0);
            a.Id = "mixed-Case";
            Assert.AreSame(a, model.Find("MIXED-case"));
            Assert.AreEqual(10, a.X); Assert.AreEqual(20, a.Y);
            Assert.AreEqual(3, model.Count);
            Assert.IsTrue(model.Remove(b));
            Assert.IsFalse(model.Remove(b));
            Assert.AreEqual(2, model.Count);
            Assert.AreSame(c, model.InStorageOrder[1]);
        }

        [TestMethod]
        public void HiddenReorder_PreservesVisibleSlotsAndDockOrder()
        {
            var model = new StickyModel();
            var a = model.CreateDraft("A", 0, 0);
            var b = model.CreateDraft("B", 0, 0);
            var c = model.CreateDraft("C", 0, 0);
            var d = model.CreateDraft("D", 0, 0);
            StickyDockGroups.ApplyOrderedGroup(new[] { a, b, c, d });
            a.Visible = c.Visible = d.Visible = false; b.Visible = true;
            Assert.IsTrue(model.ReorderHidden(d, 0));
            CollectionAssert.AreEqual(new[] { d, a, c }, model.GetHiddenInTabOrder());
            Assert.AreEqual(1, b.TabOrder);
            Assert.AreEqual(0, a.DockGroupOrder); Assert.AreEqual(3, d.DockGroupOrder);
            Assert.IsFalse(model.ReorderHidden(b, 0));
            Assert.IsFalse(model.ReorderHidden(new StickyNoteData { Visible = false }, 0));
        }

        [TestMethod]
        public void RemovingHiddenDockMember_KeepsRemainingOrderAndPreferredPlacement()
        {
            var model = new StickyModel();
            var a = model.CreateDraft("A", 0, 0);
            var b = model.CreateDraft("B", 0, 0);
            var c = model.CreateDraft("C", 0, 0);
            StickyDockGroups.ApplyOrderedGroup(new[] { a, b, c });
            b.Visible = false;
            c.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 390 });
            var preference = c.PreferredPlacement;
            Assert.IsTrue(model.Remove(b));
            CollectionAssert.AreEqual(new[] { a, c },
                StickyDockGroups.GetOrderedGroup(model.InStorageOrder, a));
            Assert.AreSame(preference, c.PreferredPlacement);
            Assert.AreEqual(1, c.DockGroupOrder);
        }

        [TestMethod]
        public void CapturedSnapshot_IsDetachedFromNestedEditsAndDatasetReplacement()
        {
            var model = new StickyModel();
            var note = model.CreateDraft("before", 0, 0);
            note.TodoItems.Add(new StickyTodoItem("todo-before", false));
            note.ScheduleItems.Add(new StickyScheduleItem("schedule-before", new DateTime(2035, 1, 1)));
            var captured = model.CaptureSnapshot();
            note.Text = "after"; note.TodoItems[0].Text = "todo-after";
            note.ScheduleItems.Clear();
            model.ReplaceWith(new[] { new StickyNoteData { Id = "replacement" } });
            Assert.AreEqual("before", captured[0].Text);
            Assert.AreEqual("todo-before", captured[0].TodoItems[0].Text);
            Assert.AreEqual("schedule-before", captured[0].ScheduleItems[0].Text);
            Assert.IsNull(model.Find(note.Id));
            Assert.AreEqual("replacement", model.InStorageOrder[0].Id);
            model.ReplaceWith(model.InStorageOrder);
            Assert.AreEqual(1, model.Count);
        }

        [TestMethod]
        public void Store_ProcessesCapturedSnapshotsWhileLiveModelKeepsChanging()
        {
            var model = new StickyModel();
            var note = model.CreateDraft("before", 0, 0);
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                StickyWriteRequest accepted = null;
                var store = new StickyStore(Path.Combine(Path.GetTempPath(), "unused-sticky.dat"), request => {
                    accepted = request;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                    return PersistenceResult.Success();
                });
                var pending = store.Save(new StickyWriteRequest(model.CaptureSnapshot()));
                try
                {
                    Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
                    note.Text = "after";
                    model.Remove(note);
                    Assert.AreEqual("before", accepted.Snapshot[0].Text);
                    Assert.AreEqual(0, model.Count);
                    Assert.IsTrue(store.HasPendingSaves);
                }
                finally { release.Set(); }
                Assert.IsTrue(pending.GetAwaiter().GetResult().Succeeded);
                Assert.IsFalse(store.HasUnsavedChanges);
            }
        }
    }
}
