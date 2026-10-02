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
        public void SideTabs_BalancedSplitPolicySingleSourceOfTruth()
        {
            int[] totals = new int[] { 0, 1, 2, 3, 4, 5, 11, 20, 101 };
            foreach (int total in totals)
            {
                int left =
                    StickyDockGeometry.CalculateBalancedLeftSideTabCount(total);
                int right = total - left;
                Assert.AreEqual(total, left + right);
                Assert.IsTrue(Math.Abs(left - right) <= 1);
                Assert.AreEqual((total + 1) / 2, left);
                Assert.AreEqual(
                    StickyDockGeometry.CalculateBalancedLeftSideTabCount(total),
                    StickyDockGeometry.CalculateBalancedLeftSideTabCount(total));
            }
            Assert.IsNotNull(
                typeof(StickyDockGeometry).GetMethod(
                    "CalculateBalancedLeftSideTabCount",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null));
            Assert.IsNull(
                typeof(StickyDockGeometry).GetMethod(
                    "CalculateLeftSideTabCount"));
            Assert.IsNull(
                typeof(StickyDockGeometry).GetMethod(
                    "CalculatePreferredSideTabCount"));
            Assert.IsTrue(StickyDockGeometry.IsBalancedSideTabSplit(6, 5));
            Assert.IsFalse(StickyDockGeometry.IsBalancedSideTabSplit(4, 7));
        }

        [TestMethod]
        public void StickyTabDropSession_DefersCommitAndUsesOpaqueSourceIdentity()
        {
            StickyTabDropSession session = new StickyTabDropSession();
            object source = new object();
            int commits = 0;

            session.Begin("drag-note", source);
            Assert.IsTrue(session.IsActiveNote("DRAG-NOTE"));
            Assert.AreEqual("drag-note", session.ActiveNoteId);
            Assert.IsTrue(session.IsSource(source));
            Assert.IsFalse(session.IsSource(new object()));
            Assert.IsFalse(session.QueueCommit("other-note",
                delegate { commits++; }));
            Assert.IsTrue(session.QueueCommit("DRAG-NOTE",
                delegate { commits++; }));
            Assert.AreEqual(0, commits);
            Assert.IsFalse(session.Complete("other-note"));
            Assert.IsTrue(session.Complete("DRAG-NOTE"));
            Assert.AreEqual(1, commits);
            Assert.IsTrue(String.IsNullOrEmpty(session.ActiveNoteId));
            Assert.IsNull(session.Source);
            Assert.IsFalse(session.Complete("DRAG-NOTE"));
        }

        [TestMethod]
        public void SideTabSnapshot_DetachesDisplayFactsFromCanonicalNote()
        {
            StickyNoteData note = new StickyNoteData
            {
                Id = "side-note",
                Title = "初始标题",
                ColorArgb = unchecked((int)0xFF112233),
                IsTodoList = true,
                Visible = false
            };

            SideTabSnapshot snapshot = SideTabSnapshot.FromData(note);
            note.Title = "后续标题";
            note.ColorArgb = unchecked((int)0xFF445566);
            note.IsTodoList = false;
            note.Visible = true;

            Assert.AreEqual("side-note", snapshot.NoteId);
            Assert.AreEqual("初始标题", snapshot.DisplayTitle);
            Assert.AreEqual(unchecked((int)0xFF112233), snapshot.ColorArgb);
            Assert.IsTrue(snapshot.IsTodoList);
            Assert.IsFalse(snapshot.Visible);
        }
    }
}
