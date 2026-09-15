using System;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyFactsReceiverTests
    {
        private sealed class Scene
        {
            internal readonly StickyNoteRepository Notes = new StickyNoteRepository("unused", _ => PersistenceResult.Success());
            internal readonly StickyHostedRuntime Hosted = new StickyHostedRuntime();
            internal readonly StickyPlacementRuntime Placement = new StickyPlacementRuntime();
            internal readonly StickyFactsReceiver Receiver;
            internal readonly StickyNoteData Note;
            internal Scene()
            {
                Note = Notes.CreateDraft("before", new Point(9000, 8000));
                Note.Id = "note";
                Note.PreferredPlacement = new WindowPlacementPreference("mdp:missing",
                    new LogicalRect { X = 700, Y = 0, Width = 320, Height = 240 });
                Hosted.AddNote(Note.Id);
                Receiver = new StickyFactsReceiver(Notes, Hosted, Placement);
            }
            internal DockBatchMemberResult Member(string snapshotId = "note", long sequence = 10, bool created = false)
            {
                return new DockBatchMemberResult(Note.Id, sequence,
                    StickyGeometryAuthorityTests.Facts(sequence: sequence),
                    StickyNoteUiSnapshot.Capture(new StickyNoteData {
                        Id = snapshotId, Text = "after", Visible = true, AlwaysOnTop = true }), created);
            }
        }

        [TestMethod]
        public void PrepareDoesNotMutateAndCommitAdvancesOneCaptureFrame()
        {
            var s = new Scene();
            var topology = StickyGeometryAuthorityTests.Topology();
            var member = s.Member();
            StickyFactsReceiver.Update update;
            Assert.IsTrue(s.Receiver.TryPrepare(member, topology, out update));
            Assert.AreEqual("before", s.Note.Text);
            Assert.IsNull(s.Placement.GetEffective("note"));
            Assert.IsTrue(s.Hosted.CanApplySequence("note", 10));
            update.Commit();
            Assert.AreEqual("after", s.Note.Text);
            Assert.AreEqual(-1840, s.Note.X);
            Assert.AreEqual(40, s.Note.LocalLogicalX);
            Assert.AreEqual(320, s.Note.LocalLogicalWidth);
            Assert.AreSame(member.Facts, s.Placement.GetEffective("note"));
            Assert.IsFalse(s.Hosted.CanApplySequence("note", 10));
            Assert.AreEqual("mdp:missing", s.Note.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(700, s.Note.PreferredPlacement.LocalLogicalRect.X);
        }

        [TestMethod]
        [DataRow(120, 2, false)]
        [DataRow(120, 2, true)]
        [DataRow(144, 1, false)]
        [DataRow(144, 1, true)]
        [DataRow(192, 1, false)]
        [DataRow(192, 1, true)]
        public void MixedDpiFactsKeepExactPixelsAcrossBothCommitPaths(int dpi, int offset, bool batch)
        {
            var s = new Scene();
            var topology = StickyGeometryAuthorityTests.Topology();
            var bounds = new PhysicalRect(-1920 + offset, -1080 + offset, 643, 451);
            var facts = new WindowFacts("note", "mdp:one", "DISPLAY1", bounds, dpi,
                topology.Generation, 10);
            if (batch)
            {
                StickyFactsReceiver.Update update;
                Assert.IsTrue(s.Receiver.TryPrepare(new DockBatchMemberResult("note", 10,
                    facts, null), topology, out update));
                update.Commit();
            }
            else
            {
                bool tabsChanged;
                Assert.IsTrue(s.Receiver.TryApplySnapshot(StickyNoteUiSnapshot.Capture(s.Note),
                    10, facts, topology, topology, out tabsChanged));
            }
            Assert.AreEqual(bounds.Left, s.Note.X);
            Assert.AreEqual(bounds.Top, s.Note.Y);
            Assert.AreEqual(bounds.Width, s.Note.Width);
            Assert.AreEqual(bounds.Height, s.Note.Height);
            Assert.AreSame(facts, s.Placement.GetEffective("note"));
            Assert.AreEqual("mdp:missing", s.Note.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(700, s.Note.PreferredPlacement.LocalLogicalRect.X);
            Assert.AreEqual("DISPLAY1", s.Note.DisplayId);
            Assert.AreEqual((int)Math.Round(offset / facts.Scale,
                MidpointRounding.AwayFromZero), s.Note.LocalLogicalX);
            Assert.AreEqual((int)Math.Round(643 / facts.Scale,
                MidpointRounding.AwayFromZero), s.Note.LocalLogicalWidth);
        }

        [TestMethod]
        public void ForeignContentCannotRideOnValidWindowFacts()
        {
            var s = new Scene();
            StickyFactsReceiver.Update update;
            Assert.IsFalse(s.Receiver.TryPrepare(s.Member("other"), StickyGeometryAuthorityTests.Topology(), out update));
            Assert.IsNull(update);
            Assert.AreEqual("before", s.Note.Text);
            Assert.IsNull(s.Placement.GetEffective("note"));
            Assert.IsTrue(s.Hosted.CanApplySequence("note", 10));
        }

        [TestMethod]
        public void RejectedLastMemberLeavesPreparedEarlierMemberUntouched()
        {
            var s = new Scene();
            StickyFactsReceiver.Update first, last;
            Assert.IsTrue(s.Receiver.TryPrepare(s.Member(), StickyGeometryAuthorityTests.Topology(), out first));
            Assert.IsFalse(s.Receiver.TryPrepare(s.Member(sequence: 0), StickyGeometryAuthorityTests.Topology(), out last));
            Assert.AreEqual("before", s.Note.Text);
            Assert.AreEqual(9000, s.Note.X);
            Assert.IsTrue(s.Hosted.CanApplySequence("note", 10));
            Assert.IsNull(s.Placement.GetEffective("note"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void OnlyAcknowledgedNewSessionCanResetBothWatermarks(bool created)
        {
            var s = new Scene();
            var topology = StickyGeometryAuthorityTests.Topology();
            s.Hosted.RecordSequence("note", 90);
            s.Placement.TryUpdateEffective("note", StickyGeometryAuthorityTests.Facts(sequence: 90), topology);
            s.Placement.MarkTemporaryRehome("note", "display-missing");
            s.Hosted.SetImeComposition("note", true);
            s.Hosted.SetInputFocus("note", true);
            s.Hosted.TryBeginDelete("note");
            StickyFactsReceiver.Update update;
            Assert.AreEqual(created, s.Receiver.TryPrepare(s.Member(sequence: 2, created: created),
                topology, out update, allowSessionCreation: true));
            if (!created) return;
            update.Commit();
            Assert.AreEqual(2L, s.Placement.GetEffective("note").WindowSequence);
            Assert.IsTrue(s.Hosted.CanApplySequence("note", 3));
            Assert.IsTrue(s.Placement.IsTemporaryRehome("note"));
            Assert.IsTrue(s.Hosted.HasImeComposition);
            Assert.IsTrue(s.Hosted.HasInputFocus);
            Assert.IsFalse(s.Hosted.TryBeginDelete("note"));
        }

        [TestMethod]
        public void NewContentWithOldTopologyDoesNotOverwriteGeometryOrIntent()
        {
            var s = new Scene();
            var topology = StickyGeometryAuthorityTests.Topology();
            WindowFacts accepted = StickyGeometryAuthorityTests.Facts();
            s.Placement.TryUpdateEffective("note", accepted, topology);
            s.Placement.MarkTemporaryRehome("note", "unplugged");
            bool tabs;
            var member = s.Member(sequence: 11);
            Assert.IsTrue(s.Receiver.TryApplySnapshot(member.Snapshot, 11, member.Facts,
                topology, StickyGeometryAuthorityTests.Topology(8, 0, 0), out tabs));
            Assert.AreEqual("after", s.Note.Text);
            Assert.AreEqual(9000, s.Note.X);
            Assert.AreSame(accepted, s.Placement.GetEffective("note"));
            Assert.IsTrue(s.Placement.IsTemporaryRehome("note"));
            Assert.IsFalse(s.Hosted.CanApplySequence("note", 11));
            Assert.AreEqual("mdp:missing", s.Note.PreferredPlacement.PreferredTargetKey);
        }

        [TestMethod]
        public void GeometryOnlyUpdatePreservesContentAndWindowFlags()
        {
            var s = new Scene();
            s.Note.Visible = false; s.Note.AlwaysOnTop = false;
            StickyFactsReceiver.Update update;
            Assert.IsTrue(s.Receiver.TryPrepare(new DockBatchMemberResult("note", 10,
                StickyGeometryAuthorityTests.Facts(), null), StickyGeometryAuthorityTests.Topology(), out update));
            update.Commit();
            Assert.AreEqual("before", s.Note.Text);
            Assert.IsFalse(s.Note.Visible);
            Assert.IsFalse(s.Note.AlwaysOnTop);
            Assert.AreEqual(-1840, s.Note.X);
        }

        [TestMethod]
        public void ForeignWindowCannotEnterPlacementRuntime()
        {
            var runtime = new StickyPlacementRuntime();
            Assert.IsFalse(runtime.TryUpdateEffective("other", StickyGeometryAuthorityTests.Facts()));
            Assert.IsNull(runtime.GetEffective("other"));
        }
    }
}
