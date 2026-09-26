using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class PetDisplayRuntimeTests
    {
        private static DisplaySurfaceSnapshot Surface(string id, int left = 0,
            bool durable = true, int workHeight = 1040, bool primary = true)
        {
            return new DisplaySurfaceSnapshot(id, id,
                new PhysicalRect(left, -200, 1920, 1080),
                new PhysicalRect(left, -200, 1920, workHeight), primary, 0,
                new[] { new DisplayTargetIdentity("mdp:" + id, durable, "", "", 0, 0, 0) });
        }

        private sealed class Scene : IDisposable
        {
            internal DisplayTopologySnapshot Topology;
            internal readonly FakeWindow Window = new FakeWindow();
            internal readonly PetSettings Settings;
            internal readonly PetDisplayRuntime Runtime;
            internal int Writes;
            internal Scene(params DisplaySurfaceSnapshot[] surfaces)
            {
                Topology = new DisplayTopologySnapshot(1, surfaces);
                Settings = new PetSettings(request =>
                {
                    Interlocked.Increment(ref Writes);
                    return PersistenceResult.Success();
                });
                Settings.PetPreferredTargetKey = "mdp:a";
                Settings.PetPreferredLocalLogicalX = 100;
                Settings.PetPreferredLocalLogicalY = 80;
                Runtime = new PetDisplayRuntime(Window, Settings, () => Topology);
            }
            internal void SetTopology(params DisplaySurfaceSnapshot[] surfaces)
            { Topology = new DisplayTopologySnapshot(Topology.Generation + 1, surfaces); }
            public void Dispose()
            { Assert.IsTrue(Settings.WaitForPendingSaves().Succeeded); }
        }

        private sealed class FakeWindow : IPetDisplayWindow
        {
            public bool IsAvailable { get; set; } = true;
            public bool IsUserDragging { get; set; }
            public PhysicalRect Bounds { get; set; } = new PhysicalRect(0, 0, 192, 208);
            public int ScalePercent { get { return 100; } }
            internal int Dpi = 96;
            internal int Moves;
            internal Action OnMove;
            internal Action OnScale;
            internal Action OnCapture;
            internal bool WrongGeneration;
            public int GetDpi(int fallbackDpi) { return Dpi; }
            public bool MoveTopLeft(int x, int y)
            {
                Moves++;
                Bounds = new PhysicalRect(x, y, Bounds.Width, Bounds.Height);
                OnMove?.Invoke();
                return true;
            }
            public void ApplyScale(int dpi)
            {
                Bounds = new PhysicalRect(Bounds.Left, Bounds.Top, 192 * dpi / 96, 208 * dpi / 96);
                OnScale?.Invoke();
            }
            public WindowFacts CaptureFacts(DisplayTopologySnapshot topology, long sequence)
            {
                string gdi = "missing";
                foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
                    if (Bounds.Left >= surface.Bounds.Left && Bounds.Left < surface.Bounds.Right)
                        gdi = surface.RuntimeGdiName;
                var facts = new WindowFacts("pet", "", gdi, Bounds, Dpi,
                    topology.Generation + (WrongGeneration ? 1 : 0), sequence);
                OnCapture?.Invoke();
                return facts;
            }
            public void PlacementChanged() { }
        }

        [TestMethod]
        [DataRow(96)]
        [DataRow(120)]
        [DataRow(144)]
        [DataRow(192)]
        public void PreferredStartup_UsesActualDpiAtNegativeOrigin(int dpi)
        {
            using (var s = new Scene(Surface("a", -1920)))
            {
                s.Window.Dpi = dpi;
                s.Runtime.Initialize();
                Assert.AreEqual(-1920 + 100 * dpi / 96, s.Window.Bounds.Left);
                Assert.AreEqual(-200 + 80 * dpi / 96, s.Window.Bounds.Top);
                Assert.AreEqual(192 * dpi / 96, s.Window.Bounds.Width);
                Assert.AreEqual(dpi, s.Runtime.EffectiveFacts.Dpi);
                Assert.AreSame(s.Topology, s.Runtime.EffectiveTopology);
                Assert.AreEqual(0, s.Writes);
            }
        }

        [TestMethod]
        public void UnplugAndReturn_PreservesPreferredPointThroughTemporaryRehome()
        {
            using (var s = new Scene(Surface("a", -1920, primary: false), Surface("b")))
            {
                s.Runtime.Initialize();
                s.SetTopology(Surface("b"));
                s.Runtime.Reconcile(s.Topology, "unplug");
                Assert.IsTrue(s.Runtime.IsTemporarilyRehomed);
                Assert.AreEqual("b", s.Runtime.EffectiveFacts.RuntimeGdiName);
                Assert.AreEqual("mdp:a", s.Settings.PetPreferredTargetKey);
                Assert.AreEqual(100, s.Settings.PetPreferredLocalLogicalX);
                s.SetTopology(Surface("a", -1920, primary: false), Surface("b"));
                s.Runtime.Reconcile(s.Topology, "return");
                Assert.AreEqual(-1820, s.Window.Bounds.Left);
                Assert.IsFalse(s.Runtime.IsTemporarilyRehomed);
                Assert.AreEqual(0, s.Writes);
            }
        }

        [TestMethod]
        public void UserMoveOnEphemeralSurface_PreventsAutomaticReturn()
        {
            using (var s = new Scene(Surface("b", durable: false)))
            {
                s.Runtime.Initialize();
                Assert.IsTrue(s.Runtime.IsTemporarilyRehomed);
                s.Window.MoveTopLeft(240, 150);
                Assert.IsFalse(s.Runtime.CommitUserPlacement());
                s.SetTopology(Surface("a", -1920, primary: false), Surface("b", durable: false));
                s.Runtime.Reconcile(s.Topology, "return");
                Assert.AreEqual(240, s.Window.Bounds.Left);
                Assert.AreEqual("mdp:a", s.Settings.PetPreferredTargetKey);
                Assert.IsTrue(s.Runtime.IsTemporarilyRehomed);
            }
        }

        [TestMethod]
        public void ExplicitUserMove_CommitsNewDurablePreferenceOnce()
        {
            using (var s = new Scene(Surface("b")))
            {
                s.Runtime.Initialize();
                s.Window.Dpi = 144;
                s.Window.MoveTopLeft(150, -50);
                Assert.IsTrue(s.Runtime.CommitUserPlacement());
                Assert.AreEqual("mdp:b", s.Settings.PetPreferredTargetKey);
                Assert.AreEqual(100, s.Settings.PetPreferredLocalLogicalX);
                Assert.AreEqual(100, s.Settings.PetPreferredLocalLogicalY);
                Assert.IsFalse(s.Runtime.IsTemporarilyRehomed);
                Assert.IsTrue(s.Settings.WaitForPendingSaves().Succeeded);
                Assert.AreEqual(1, s.Writes);
            }
        }

        [TestMethod]
        public void Dragging_TopologyRepairDoesNotMoveWindowOrSave()
        {
            using (var s = new Scene(Surface("a")))
            {
                s.Runtime.Initialize();
                s.Window.IsUserDragging = true;
                int moves = s.Window.Moves;
                s.SetTopology(Surface("b"));
                s.Runtime.Reconcile(s.Topology, "drag");
                Assert.AreEqual(moves, s.Window.Moves);
                Assert.AreSame(s.Topology, s.Runtime.EffectiveTopology);
                Assert.AreEqual("mdp:a", s.Settings.PetPreferredTargetKey);
                Assert.AreEqual(0, s.Writes);
            }
        }

        [TestMethod]
        public void TopologyChangesDuringCapture_CannotPublishOrCommitStaleFacts()
        {
            using (var s = new Scene(Surface("a")))
            {
                s.Runtime.Initialize();
                var previousFacts = s.Runtime.EffectiveFacts;
                var previousTopology = s.Runtime.EffectiveTopology;
                s.Window.OnCapture = () => s.SetTopology(Surface("a", -1920));
                Assert.IsFalse(s.Runtime.CommitUserPlacement());
                Assert.AreSame(previousFacts, s.Runtime.EffectiveFacts);
                Assert.AreSame(previousTopology, s.Runtime.EffectiveTopology);
                Assert.AreEqual(100, s.Settings.PetPreferredLocalLogicalX);
                Assert.AreEqual(0, s.Writes);
            }
        }

        [TestMethod]
        public void NestedCapture_OlderCompletionCannotReplaceNewerFacts()
        {
            using (var s = new Scene(Surface("a")))
            {
                WindowFacts newer = null;
                s.Window.OnCapture = () =>
                {
                    s.Window.OnCapture = null;
                    s.Window.MoveTopLeft(300, 100);
                    newer = s.Runtime.Capture(s.Topology);
                };
                Assert.IsNull(s.Runtime.Capture(s.Topology));
                Assert.AreSame(newer, s.Runtime.EffectiveFacts);
                Assert.AreEqual(300, s.Runtime.EffectiveFacts.PhysicalBounds.Left);
            }
        }

        [TestMethod]
        public void InvalidCapture_DoesNotOverwriteEffectivePair()
        {
            using (var s = new Scene(Surface("a")))
            {
                var facts = s.Runtime.Capture(s.Topology);
                s.Window.WrongGeneration = true;
                Assert.IsNull(s.Runtime.Capture(s.Topology));
                Assert.AreSame(facts, s.Runtime.EffectiveFacts);
                s.Window.IsAvailable = false;
                Assert.IsNull(s.Runtime.Capture(s.Topology));
                Assert.AreSame(facts, s.Runtime.EffectiveFacts);
            }
        }

        [TestMethod]
        public void NestedProgrammaticMoves_CannotCommitUserPreference()
        {
            using (var s = new Scene(Surface("a")))
            {
                s.Window.OnMove = () =>
                {
                    s.Window.OnMove = null;
                    Assert.IsFalse(s.Runtime.CommitUserPlacement());
                    s.Runtime.MoveForDpiHandoff(200, 100);
                    Assert.IsFalse(s.Runtime.CommitUserPlacement());
                };
                s.Runtime.MoveForDpiHandoff(100, 100);
                Assert.AreEqual(0, s.Writes);
                Assert.IsTrue(s.Runtime.CommitUserPlacement());
                Assert.AreEqual(200, s.Settings.PetPreferredLocalLogicalX);
            }
        }

        [TestMethod]
        public void TopologyChangesDuringScale_PreventsOldProjection()
        {
            using (var s = new Scene(Surface("a", -1920)))
            {
                s.Window.OnScale = () => s.SetTopology(Surface("a", 1920));
                s.Runtime.Initialize();
                Assert.AreEqual(1, s.Window.Moves); // Only the bootstrap, no stale projection.
                Assert.AreNotSame(s.Topology, s.Runtime.EffectiveTopology);
                Assert.AreEqual("mdp:a", s.Settings.PetPreferredTargetKey);
                Assert.AreEqual(0, s.Writes);
                s.Window.OnScale = null;
                s.Runtime.Reconcile(s.Topology, "settled");
                Assert.AreEqual(2020, s.Window.Bounds.Left);
            }
        }

        [TestMethod]
        public void WorkAreaRepairAndSave_PreserveUnclampedLogicalPreference()
        {
            using (var s = new Scene(Surface("a")))
            {
                s.Settings.PetPreferredLocalLogicalY = 900;
                s.Runtime.Initialize();
                s.SetTopology(Surface("a", workHeight: 600));
                s.Runtime.Reconcile(s.Topology, "work-area");
                Assert.AreEqual(192, s.Window.Bounds.Top);
                s.Window.MoveTopLeft(1900, 1000);
                s.Runtime.KeepFullyVisible();
                s.Runtime.CaptureForSave();
                Assert.AreEqual(1728, s.Settings.X);
                Assert.AreEqual(192, s.Settings.Y);
                Assert.AreEqual(900, s.Settings.PetPreferredLocalLogicalY);
                Assert.AreEqual(100, s.Settings.PetPreferredLocalLogicalX);
                Assert.AreEqual(0, s.Writes);
            }
        }
    }
}
