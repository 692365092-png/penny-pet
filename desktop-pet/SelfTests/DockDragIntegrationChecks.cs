using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        public static void RunDockDragProbe(string outputPath)
        {
            string root = Path.Combine(Path.GetDirectoryName(
                Path.GetFullPath(outputPath)), "dock-drag-" + Guid.NewGuid().ToString("N"));
            var evidence = new List<string>();
            RunNativeDockDragChecks(root, evidence);
            File.WriteAllText(outputPath, new System.Web.Script.Serialization
                .JavaScriptSerializer().Serialize(new { ok = true, observations = evidence }),
                new UTF8Encoding(false));
        }

        private static void DockOnThread(Pc2Scene scene, Action action)
        {
            StickyUiCommandResult result = null;
            var thread = (StickyUiThreadHost)Pc2Get(scene.Host, "_threadHost");
            thread.PostToDispatcher(() =>
            {
                action();
                return StickyUiCommandResult.Handled();
            }, value => result = value, scene.Context);
            scene.Context.PumpUntil(() => result != null);
            Pc2Assert(result.Status == StickyUiCommandStatus.Handled,
                "native Dock step: " + result.Error);
        }

        private static StickyWindowSession DockSession(Pc2Scene scene, int index)
        {
            return ((Dictionary<string, StickyWindowSession>)Pc2Get(
                scene.Host, "_sessions"))[scene.Ids[index]];
        }

        private static void DockNativeLoop(StickyNoteWindow window, int message)
        {
            Pc2Call(window, "WindowHook", window.Handle, message,
                IntPtr.Zero, IntPtr.Zero, false);
        }

        // Pc2Scene deliberately has no real Pet HWND. Supply that boundary
        // through the existing port; Sticky windows and restore remain real.
        private sealed class StartupPetSurface : IStickyPetSurface
        {
            internal DisplayTopologySnapshot Topology;
            public System.Drawing.Rectangle Bounds { get { return System.Drawing.Rectangle.Empty; } }
            public bool IsDisposed { get { return false; } }
            public bool IsExiting { get { return false; } }
            public bool HasHandle { get { return false; } }
            public DisplayTopologySnapshot CurrentTopologySnapshot() { return Topology; }
            public WindowFacts CaptureWindowFacts(DisplayTopologySnapshot topology) { return null; }
        }

        private static void RunStartupDockRestoreChecks(string root, List<string> evidence)
        {
            foreach (int failureStage in new[] { 0, 1, 2 })
            using (var scene = new Pc2Scene(root, "startup-dock-" + failureStage, true))
            {
                bool fail = failureStage != 0;
                Pc2Set(scene.Workspace, "_surface", new StartupPetSurface { Topology = scene.Topology });
                foreach (StickyNoteData note in scene.Notes) scene.Hosted.RemoveNote(note.Id);
                // A visible child must restore its whole group, including the hidden root.
                scene.Notes[0].Visible = false;
                scene.Start();
                scene.Host.Configure(scene.Workspace.HostedStickyEventReceived, scene.Context);
                if (fail)
                    scene.Host.SetCommandHandler(command => command.Kind == (failureStage == 1
                        ? StickyUiCommandKind.RestoreDockGroup : StickyUiCommandKind.PrepareDockStructure)
                        ? StickyUiCommandResult.Failed(new InvalidOperationException("injected restore failure"))
                        : StickyUiCommandResult.Handled());
                var queue = (Queue<StickyNoteData>)Pc2Call(Pc2Get(scene.Pet, "_startup"), "BuildStartupRestoreQueue");
                Pc2Assert(queue.Count == 1, "startup queues one transaction for a Dock group");
                scene.Workspace.QueueStartupStickyRestore(queue.Dequeue());
                scene.Context.PumpUntil(() => fail
                    ? scene.Notes.TrueForAll(note => !note.Visible)
                    : scene.Notes.TrueForAll(note => scene.Hosted.ContainsNote(note.Id)));
                scene.Context.PumpUntil(() => (bool)Pc2Call(Pc2Get(scene.Pet, "_startup"), "AllExpectedNotesHaveFirstRendered"));
                if (!fail)
                    Pc2Assert(scene.Notes.TrueForAll(note => note.Visible),
                        "startup restores hidden members through the group transaction");
            }
            evidence.Add("startup Dock groups restore all members; failure releases first-render wait");
        }

        private static void RunNativeDockDragChecks(string root, List<string> evidence)
        {
            using (var scene = new Pc2Scene(root, "native-header-" + Guid.NewGuid().ToString("N"), true))
            {
                scene.Start();
                scene.Host.Configure(scene.Workspace.HostedStickyEventReceived, scene.Context);
                foreach (StickyNoteData note in scene.Notes)
                {
                    StickyDockGroups.ClearMembership(note);
                    Pc2Assert(scene.Send(StickyUiCommand.EnsureSession(
                        StickyNoteUiSnapshot.Capture(note), null, scene.Topology)).Status ==
                        StickyUiCommandStatus.Handled, "create real Dock window");
                    scene.Send(StickyUiCommand.Show(note.Id, false, scene.Topology,
                        StickyPlacementRecovery.SelectForShow(note, scene.Topology)));
                }
                // Publish after initial native Show snapshots have reached the model.
                scene.Workspace.Dock.RefreshDockResizeRoles();
                DockOnThread(scene, () => { });

                var observed = new List<StickyUiEventKind>();
                scene.Host.Configure(value =>
                {
                    observed.Add(value.Kind);
                    scene.Workspace.HostedStickyEventReceived(value);
                }, scene.Context);
                // Put note 1 just below note 0, then perform the actual native
                // move-loop notifications, including WM_EXITSIZEMOVE before
                // HeaderDragCompleted (the order used by WPF DragMove).
                DockOnThread(scene, () =>
                {
                    StickyWindowSession session = DockSession(scene, 1);
                    var window = (StickyNoteWindow)Pc2Get(session, "_window");
                    var parent = DockSession(scene, 0).CaptureVisibleFactsForChrome().PhysicalBounds;
                    var before = session.CaptureVisibleFactsForChrome().PhysicalBounds;
                    Pc2Set(window, "_headerDragInProgress", true);
                    Pc2Call(session, "HeaderDragStarted", window, EventArgs.Empty);
                    DockNativeLoop(window, 0x0231);
                    NativeDisplayConfig.SetWindowPos(window.Handle, IntPtr.Zero,
                        parent.Left, parent.Bottom + 8, before.Width, before.Height,
                        NativeDisplayConfig.SWP_NOZORDER | NativeDisplayConfig.SWP_NOACTIVATE);
                    DockNativeLoop(window, 0x0232);
                    Pc2Set(window, "_headerDragInProgress", false);
                    Pc2Call(session, "HeaderDragCompleted", window, EventArgs.Empty);
                });
                DockOnThread(scene, () => { }); // drain commit and acknowledgement
                Pc2Assert(!observed.Contains(StickyUiEventKind.UserResizeCompleted),
                    "a native header move must not publish a resize commit before its Dock commit");
                Pc2Assert(!String.IsNullOrEmpty(scene.Notes[0].DockGroupId) &&
                    scene.Notes[0].DockGroupId == scene.Notes[1].DockGroupId,
                    "native snap persists membership rather than rolling back");
                evidence.Add("Native header mouse-up merges two real HWNDs without a competing resize commit.");

                foreach (int sourceIndex in new[] { 0, 1 })
                {
                    PhysicalRect sourceStart = new PhysicalRect();
                    PhysicalRect otherStart = new PhysicalRect();
                    DockOnThread(scene, () =>
                    {
                        StickyWindowSession session = DockSession(scene, sourceIndex);
                        var window = (StickyNoteWindow)Pc2Get(session, "_window");
                        sourceStart = session.CaptureVisibleFactsForChrome().PhysicalBounds;
                        otherStart = DockSession(scene, 1 - sourceIndex)
                            .CaptureVisibleFactsForChrome().PhysicalBounds;
                        Pc2Set(window, "_headerDragInProgress", true);
                        Pc2Call(session, "HeaderDragStarted", window, EventArgs.Empty);
                        DockNativeLoop(window, 0x0231);
                        NativeDisplayConfig.SetWindowPos(window.Handle, IntPtr.Zero,
                            sourceStart.Left + 53, sourceStart.Top + 31,
                            sourceStart.Width, sourceStart.Height,
                            NativeDisplayConfig.SWP_NOZORDER | NativeDisplayConfig.SWP_NOACTIVATE);
                        // Fire the same save timer callback that runs when a user
                        // pauses during a drag; it must not invalidate Dock state.
                        typeof(StickyNoteWindow).GetMethod("PersistNow",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                            null, new[] { typeof(bool), typeof(bool) }, null)
                            .Invoke(window, new object[] { false, true });
                        var follower = (StickyNoteWindow)Pc2Get(
                            DockSession(scene, 1 - sourceIndex), "_window");
                        typeof(StickyNoteWindow).GetMethod("PersistNow",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                            null, new[] { typeof(bool), typeof(bool) }, null)
                            .Invoke(follower, new object[] { false, true });
                        // An unrelated programmatic LocationChanged must not
                        // masquerade as input from the active drag source.
                        var other = DockSession(scene, 2);
                        var otherBounds = other.CaptureVisibleFactsForChrome().PhysicalBounds;
                        NativeDisplayConfig.SetWindowPos(other.PlacementHwnd, IntPtr.Zero,
                            otherBounds.Left + 1, otherBounds.Top, otherBounds.Width, otherBounds.Height,
                            NativeDisplayConfig.SWP_NOZORDER | NativeDisplayConfig.SWP_NOACTIVATE);
                    });
                    DockOnThread(scene, () =>
                    {
                        StickyWindowSession session = DockSession(scene, sourceIndex);
                        var window = (StickyNoteWindow)Pc2Get(session, "_window");
                        // WPF can defer follower SizeChanged beyond the
                        // synchronous native placement/suppression scope.
                        var follower = DockSession(scene, 1 - sourceIndex);
                        Pc2Call(follower, "BoundsChanged",
                            Pc2Get(follower, "_window"), EventArgs.Empty);
                    });
                    DockOnThread(scene, () =>
                    {
                        StickyWindowSession session = DockSession(scene, sourceIndex);
                        var window = (StickyNoteWindow)Pc2Get(session, "_window");
                        DockNativeLoop(window, 0x0232);
                        Pc2Set(window, "_headerDragInProgress", false);
                        Pc2Call(session, "HeaderDragCompleted", window, EventArgs.Empty);
                    });
                    DockOnThread(scene, () => { });
                    Pc2Assert(scene.Notes[sourceIndex].X == sourceStart.Left + 53 &&
                        scene.Notes[sourceIndex].Y == sourceStart.Top + 31 &&
                        scene.Notes[1 - sourceIndex].X == otherStart.Left + 53 &&
                        scene.Notes[1 - sourceIndex].Y == otherStart.Top + 31 &&
                        scene.Notes[0].DockGroupId == scene.Notes[1].DockGroupId,
                        "root and child drag move both members and retain membership: " + sourceIndex +
                        " source=" + scene.Notes[sourceIndex].X + "," + scene.Notes[sourceIndex].Y +
                        " expected=" + (sourceStart.Left + 53) + "," + (sourceStart.Top + 31) +
                        " follower=" + scene.Notes[1 - sourceIndex].X + "," + scene.Notes[1 - sourceIndex].Y +
                        " expected=" + (otherStart.Left + 53) + "," + (otherStart.Top + 31));
                }
                evidence.Add("Dragging either root or child persists the whole group's translation, including an intervening save callback.");
                DockOnThread(scene, () =>
                {
                    var session = DockSession(scene, 2);
                    var window = (StickyNoteWindow)Pc2Get(session, "_window");
                    var before = window.PhysicalBounds;
                    session.SetEventsSuppressed(true);
                    try
                    {
                        // DPI handoff hides a live HWND. Visibility is not a
                        // prerequisite for verifying its native placement.
                        window.Hide();
                        var mismatch = (List<int>)typeof(StickyUiHost).GetMethod(
                            "LocalDockPlacementMismatches", System.Reflection.BindingFlags.Static |
                            System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] {
                                new List<StickyWindowSession> { session },
                                new List<PhysicalRect> { new PhysicalRect(before.Left, before.Top, before.Width, before.Height) },
                                scene.Topology.Generation });
                        Pc2Assert(mismatch.Count == 0, "temporarily hidden follower remains a valid placement participant");
                        window.Show();
                        Pc2Set(window, "_headerDragStartBounds", before);
                        NativeDisplayConfig.SetWindowPos(window.Handle, IntPtr.Zero,
                            before.Left + 17, before.Top + 19, before.Width + 40, before.Height + 40,
                            NativeDisplayConfig.SWP_NOZORDER | NativeDisplayConfig.SWP_NOACTIVATE);
                        var changed = window.PhysicalBounds;
                        Pc2Call(window, "RecoverFromSystemGeometryChange");
                        Pc2Assert(window.PhysicalBounds == changed,
                            "a normal window's DPI size change must not trigger maximize recovery");
                        evidence.Add("Native HWND checks passed at DPI " + session.CapturePlacementFacts().Dpi +
                            "; hidden placement and normal size-change recovery verified.");
                    }
                    finally { session.SetEventsSuppressed(false); }
                });
            }
        }
    }
}
