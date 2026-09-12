using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockMembershipAuthorityTests
    {
        private static StickyNoteData[] Group(params string[] ids)
        {
            StickyNoteData[] notes = ids.Select(id => new StickyNoteData { Id = id }).ToArray();
            StickyDockGroups.ApplyOrderedGroup(notes);
            return notes;
        }

        [TestMethod]
        public void VisibleOrderIgnoresStaleParentsAndPhysicalPosition()
        {
            StickyNoteData[] notes = Group("a", "b", "c", "d");
            notes[1].Visible = false;
            foreach (StickyNoteData note in notes) { note.DockParentId = "d"; note.Y = -note.DockGroupOrder; }
            StickyNoteData[] shuffled = { notes[3], notes[1], notes[0], notes[2] };
            CollectionAssert.AreEqual(new[] { "a", "c", "d" },
                StickyDockGroups.GetVisibleGroup(shuffled, notes[2]).Select(n => n.Id).ToArray());
            Assert.AreSame(notes[0], StickyDockGroups.GetVisibleNeighbor(shuffled, notes[2], -1));
            Assert.AreEqual(1, notes[1].DockGroupOrder);
        }

        [TestMethod]
        public void LegacyLinksAreConsumedOnlyAtImport()
        {
            StickyNoteData a = new StickyNoteData { Id = "a" };
            StickyNoteData b = new StickyNoteData { Id = "b", DockParentId = "a" };
            StickyNoteData c = new StickyNoteData { Id = "c", DockParentId = "b", Visible = false };
            StickyNoteData[] notes = { c, b, a };
            Assert.AreEqual(1, StickyDockGroups.GetOrderedGroup(notes, a).Count);
            StickyDockGroups.NormalizeAll(notes);
            CollectionAssert.AreEqual(new[] { a, b, c }, StickyDockGroups.GetOrderedGroup(notes, b));
            Assert.IsTrue(notes.All(n => String.IsNullOrEmpty(n.DockParentId)));
            c.Visible = true;
            Assert.AreSame(b, StickyDockGroups.GetVisibleNeighbor(notes, c, -1));
        }

        [TestMethod]
        public void ConflictingLegacyEdgesCannotMergeExplicitGroups()
        {
            StickyNoteData[] first = Group("a", "b");
            StickyNoteData[] second = Group("c", "d");
            first[1].DockParentId = "c";
            second[0].DockParentId = "a";
            StickyNoteData[] notes = first.Concat(second).ToArray();
            StickyDockGroups.NormalizeAll(notes);
            CollectionAssert.AreEqual(first, StickyDockGroups.GetOrderedGroup(notes, first[1]));
            CollectionAssert.AreEqual(second, StickyDockGroups.GetOrderedGroup(notes, second[0]));
        }

        [TestMethod]
        public void HidingAndDeletingDifferentMembersPreservesRemainingHiddenSlots()
        {
            StickyNoteData[] notes = Group("a", "b", "c", "d");
            notes[1].Visible = false;
            notes[3].Visible = false;
            List<StickyNoteData> remaining = StickyDockOperations.ExtractSingleDockMember(notes, notes[2]);
            CollectionAssert.AreEqual(new[] { "a", "b", "d" }, remaining.Select(n => n.Id).ToArray());
            Assert.AreEqual(1, notes[1].DockGroupOrder);
            Assert.AreEqual(2, notes[3].DockGroupOrder);
            foreach (StickyNoteData note in remaining) note.Visible = true;
            CollectionAssert.AreEqual(remaining, StickyDockGroups.GetVisibleGroup(remaining, notes[3]));
        }

        [TestMethod]
        public void LegacyOutputIsDerivedWithoutMutatingLiveOrSnapshotState()
        {
            StickyNoteData[] notes = Group("a", "b", "c");
            notes[1].Visible = false;
            foreach (StickyNoteData note in notes) note.DockParentId = "poison";
            Dictionary<string, string> parents = StickyDockGroups.BuildLegacyParents(notes);
            Assert.AreEqual(String.Empty, parents["a"]);
            Assert.AreEqual(String.Empty, parents["b"]);
            Assert.AreEqual("a", parents["c"]);
            Assert.IsTrue(notes.All(n => n.DockParentId == "poison"));
            StickyNoteData serialized = StickyNoteCodec.ParseLine(StickyNoteCodec.SerializeLine(notes[2], parents["c"]));
            Assert.AreEqual("a", serialized.DockParentId);
            Assert.AreEqual(2, serialized.DockGroupOrder);
        }

        [TestMethod]
        public void DuplicateOrderInputIsRejectedBeforeAnyMutation()
        {
            StickyNoteData[] notes = Group("a", "b");
            string[] before = notes.Select(StickyNoteCodec.SerializeLine).ToArray();
            Assert.ThrowsExactly<ArgumentException>(() => StickyDockGroups.ApplyOrderedGroup(
                new[] { notes[1], notes[0], new StickyNoteData { Id = "A" } }));
            CollectionAssert.AreEqual(before, notes.Select(StickyNoteCodec.SerializeLine).ToArray());
        }

        [TestMethod]
        public void MergeIsStagedUntilSuccessfulCommitAndRetainsHiddenMembers()
        {
            StickyNoteData[] target = Group("a", "b");
            StickyNoteData[] source = Group("c", "d");
            target[1].Visible = source[1].Visible = false;
            StickyNoteData[] all = target.Concat(source).ToArray();
            string[] before = all.Select(StickyNoteCodec.SerializeLine).ToArray();
            DockMergePlan plan = StickyDockOperations.PrepareMergeAfterParent(target, target[0], source);
            List<StickyNoteData> proposed;
            Assert.IsTrue(plan.TryResolve(all, out proposed));
            CollectionAssert.AreEqual(new[] { "a", "c", "d", "b" }, proposed.Select(n => n.Id).ToArray());
            CollectionAssert.AreEqual(before, all.Select(StickyNoteCodec.SerializeLine).ToArray());
            Assert.IsTrue(plan.TryCommit(all));
            CollectionAssert.AreEqual(proposed, StickyDockGroups.GetOrderedGroup(all, source[0]));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void StagedMergeRejectsInterveningMembershipChangesWithoutMutation(bool appendMember)
        {
            StickyNoteData[] target = Group("a", "b");
            StickyNoteData c = new StickyNoteData { Id = "c" };
            DockMergePlan plan = StickyDockOperations.PrepareMergeAfterParent(target, target[0], new[] { c });
            List<StickyNoteData> all = new List<StickyNoteData>(target) { c };
            if (appendMember)
            {
                StickyNoteData d = new StickyNoteData { Id = "d" };
                all.Add(d);
                StickyDockGroups.ApplyOrderedGroup(new[] { target[0], target[1], d });
            }
            else target[1].Visible = false;
            string[] before = all.Select(StickyNoteCodec.SerializeLine).ToArray();
            Assert.IsFalse(plan.TryCommit(all));
            CollectionAssert.AreEqual(before, all.Select(StickyNoteCodec.SerializeLine).ToArray());
            Assert.AreEqual(String.Empty, c.DockGroupId);
        }

        [TestMethod]
        public void CorruptLegacyCycleRecoversDeterministically()
        {
            StickyNoteData a = new StickyNoteData { Id = "a", DockParentId = "b", Y = 20 };
            StickyNoteData b = new StickyNoteData { Id = "b", DockParentId = "a", Y = 10 };
            StickyNoteData[] notes = { a, b };
            StickyDockGroups.NormalizeAll(notes);
            CollectionAssert.AreEqual(new[] { b, a }, StickyDockGroups.GetOrderedGroup(notes, a));
            string[] before = notes.Select(StickyNoteCodec.SerializeLine).ToArray();
            StickyDockGroups.NormalizeAll(notes);
            CollectionAssert.AreEqual(before, notes.Select(StickyNoteCodec.SerializeLine).ToArray());
        }
    }
}
