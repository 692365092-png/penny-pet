using System;
using System.Collections.Generic;
using System.Drawing;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        // Test-side platform adapters call the surviving production rules.
        // They do not revive the deleted Pet live planner or transport.
        private static bool CanDockBelowForCheck(Rectangle moving,
            Rectangle target, int threshold)
        {
            return StickyDockOperations.CanDockBelow(moving.Left, moving.Top,
                moving.Width, moving.Height, target.Left, target.Top,
                target.Width, target.Height, threshold);
        }

        private static List<DockLayoutTarget> CalculateDividerTargetsForCheck(
            DockWindowFacts upper, DockWindowFacts lower)
        {
            var targets = new List<DockLayoutTarget>();
            if (upper == null || lower == null) return targets;
            int height = StickyDockController.CalculateDockDividerHeight(upper.Height);
            List<DockRect> followers = StickyDockGeometry.CalculateDockDividerFinalTargets(
                new[] { new DockRect(upper.X, upper.Y, upper.Width, upper.Height),
                    new DockRect(lower.X, lower.Y, lower.Width, lower.Height) },
                0, upper.Y, height);
            targets.Add(new DockLayoutTarget(upper.NoteId, upper.X, upper.Y,
                upper.Width, height, upper.Visible, upper.TopMost));
            DockRect follower = followers[0];
            targets.Add(new DockLayoutTarget(lower.NoteId, follower.Left,
                follower.Top, follower.Width, follower.Height,
                lower.Visible, lower.TopMost));
            return targets;
        }

        private static bool RunLocalDividerCycleCheck()
        {
            DisplaySurfaceSnapshot surface = FakeSurface(1, 0, true,
                1080, 1040, FakeTarget("mdp:divider"));
            var facts = new Dictionary<string, WindowFacts>();
            var members = new List<StickyDockSceneMember>();
            string[] ids = { "a", "b", "c", "d" };
            for (int index = 0; index < ids.Length; index++)
            {
                facts.Add(ids[index], new WindowFacts(ids[index], "mdp:divider",
                    surface.RuntimeGdiName,
                    new PhysicalRect(100, 100 + 300 * index, 420, 300), 96, 1, 1));
                members.Add(new StickyDockSceneMember(ids[index], "group", index, true));
            }
            IReadOnlyList<DockWindowTarget> applied = null;
            int applyCount = 0;
            var runtime = new StickyDockLocalGestureRuntime(
                id => facts.ContainsKey(id) ? facts[id] : null,
                (targets, source) =>
                {
                    if (source != "b") return false;
                    applied = targets;
                    applyCount++;
                    return true;
                });
            runtime.SetTopology(new DisplayTopologySnapshot(1, new[] { surface }));
            runtime.SetScene(new StickyDockSceneProjection(members, 1));
            if (runtime.TryBegin(StickyDockLocalGestureKind.DividerResize, "b") == 0)
                return false;
            int[] cycle = { 450, 250, 600, 300 };
            for (int repeat = 0; repeat < 50; repeat++)
                foreach (int requested in cycle)
                    if (!runtime.ResizeDivider(requested) || applied == null ||
                        applied.Count != 2 || applied[0].NoteId != "c" ||
                        applied[1].NoteId != "d" ||
                        applied[0].PhysicalBounds.Top != 400 + requested ||
                        applied[1].PhysicalBounds.Top != 700 + requested ||
                        applied[0].PhysicalBounds.Height != 300 ||
                        applied[1].PhysicalBounds.Height != 300 ||
                        applied[0].PhysicalBounds.Width != 420 ||
                        applied[1].PhysicalBounds.Width != 420)
                        return false;
            return applyCount == 200 && runtime.Complete() != null;
        }
        private static void RunDockCommitMembershipChecks(string root, List<string> evidence)
        {
            foreach (StickyDockCommitIntent intent in new[] { StickyDockCommitIntent.Move,
                StickyDockCommitIntent.HorizontalResize, StickyDockCommitIntent.DividerResize,
                StickyDockCommitIntent.Detach, StickyDockCommitIntent.MergeAfter })
            using (var scene = new Pc2Scene(root, "commit-members-" + intent))
            {
                var versions = new Dictionary<string, long>();
                foreach (var note in scene.Notes) versions.Add(note.Id, StickyDockCommitVersion.Compute(note));
                string before = String.Join("\n", scene.Notes.ConvertAll(StickyNoteCodec.SerializeLine));
                long saves = scene.Saves;
                var partial = new StickyDockGestureCommit(1, 0, intent,
                    scene.Ids[1], scene.Ids[0], scene.Topology.Generation, versions,
                    new[] { scene.Member(1) });
                bool accepted = (bool)Pc2Call(scene.Workspace.Dock, "TryApplyLocalDockGestureCommit", partial);
                Pc2Assert(!accepted && saves == scene.Saves && before ==
                    String.Join("\n", scene.Notes.ConvertAll(StickyNoteCodec.SerializeLine)),
                    "partial " + intent + " commit rejects before any relation, geometry or content mutation");
            }
            evidence.Add("all five Dock intents reject partial member sets atomically");
        }

        private static void RunPc2FinalDockFailure(string root,
            List<string> evidence)
        {
            using (var scene = new Pc2Scene(root, "R24-final-failure", true))
            {
                scene.Start();
                var facts = new Dictionary<string, WindowFacts>();
                var members = new List<StickyDockSceneMember>();
                for (int index = 0; index < scene.Notes.Count; index++)
                {
                    StickyNoteData note = scene.Notes[index];
                    Pc2Assert(scene.Send(StickyUiCommand.EnsureSession(
                        StickyNoteUiSnapshot.Capture(note), null,
                        scene.Topology)).Status == StickyUiCommandStatus.Handled,
                        "final failure fixture creates real session");
                    Pc2Assert(scene.Send(StickyUiCommand.Show(note.Id, false,
                        scene.Topology, StickyPlacementRecovery.SelectForShow(
                            note, scene.Topology))).Status == StickyUiCommandStatus.Handled,
                        "final failure fixture shows real window");
                    facts.Add(note.Id, scene.Send(StickyUiCommand.CaptureWindowFacts(
                        note.Id, scene.Topology)).Facts);
                    members.Add(new StickyDockSceneMember(note.Id, "group", index, true));
                }
                int attempts = 0;
                var runtime = new StickyDockLocalGestureRuntime(
                    id => facts.ContainsKey(id) ? facts[id] : null,
                    (targets, source) => { attempts++; return false; });
                // Dispatch the probe on the real Sticky STA. Only the failing
                // native batch result is injected; rollback uses actual HWNDs.
                scene.Host.SetCommandHandler(command =>
                {
                    runtime.SetTopology(scene.Topology);
                    runtime.SetScene(new StickyDockSceneProjection(members, 1));
                    Pc2Set(scene.Host, "_localDockGestures", runtime);
                    string source = scene.Ids[0];
                    Pc2Assert(runtime.TryBegin(
                        StickyDockLocalGestureKind.DividerResize, source) != 0,
                        "final failure gesture starts");
                    PhysicalRect before = facts[source].PhysicalBounds;
                    Pc2Call(scene.Host, "HandleCommand", new StickyUiCommand(
                        StickyUiCommandKind.SetBounds, source, false, null,
                        new StickyUiBounds(before.Left, before.Top,
                            before.Width, before.Height + 100)));
                    Pc2Assert((bool)Pc2Call(scene.Host, "TryHandleLocalDockEvent",
                        null, StickyUiEvent.DockGeometry(
                            StickyUiEventKind.DockDividerResizeCompleted,
                            source, 1, before.Left, before.Width,
                            before.Height + 100, facts[source], scene.Topology)),
                        "final failure event consumed locally");
                    Pc2Assert(attempts == 1 && !runtime.IsActive &&
                        ((StickyDockCommitQueue)Pc2Get(scene.Host,
                            "_dockCommitQueue")).PendingCount == 0,
                        "failed final batch cannot enqueue a commit or retry forever");
                    foreach (string id in scene.Ids)
                    {
                        var captured = (StickyUiCommandResult)Pc2Call(scene.Host,
                            "HandleCommand", StickyUiCommand.CaptureWindowFacts(
                                id, scene.Topology));
                        Pc2Assert(captured.Facts != null &&
                            captured.Facts.PhysicalBounds.Equals(facts[id].PhysicalBounds),
                            "failed final batch restores actual window " + id);
                    }
                    Pc2Assert(runtime.TryBegin(
                        StickyDockLocalGestureKind.HeaderDrag, source) != 0,
                        "next gesture can start after final failure");
                    Pc2Call(scene.Host, "HandleCommand", new StickyUiCommand(
                        StickyUiCommandKind.SetBounds, source, false, null,
                        new StickyUiBounds(before.Left + 40, before.Top + 40,
                            before.Width, before.Height)));
                    Pc2Call(scene.Host, "HandleCommand",
                        StickyUiCommand.PrepareDockStructure(new[] { "unrelated" }));
                    Pc2Assert(runtime.IsActive,
                        "unrelated structure command preserves active gesture");
                    Pc2Call(scene.Host, "HandleCommand",
                        StickyUiCommand.PrepareDockStructure(new[] { scene.Ids[1] }));
                    var restored = (StickyUiCommandResult)Pc2Call(scene.Host,
                        "HandleCommand", StickyUiCommand.CaptureWindowFacts(
                            source, scene.Topology));
                    Pc2Assert(!runtime.IsActive && restored.Facts != null &&
                        restored.Facts.PhysicalBounds.Equals(before),
                        "affected structure command retires gesture and restores source HWND");
                    foreach (bool captureFailure in new[] { true, false })
                    {
                        string child = scene.Ids[1];
                        runtime.TryBegin(StickyDockLocalGestureKind.HeaderDrag, child);
                        var completed = runtime.Complete();
                        var affected = new List<string>(completed.AffectedMemberIds);
                        if (captureFailure) affected.Add("missing-session");
                        var versions = new Dictionary<string, long>();
                        foreach (var pair in completed.BaselineVersions) versions.Add(pair.Key, pair.Value);
                        var detached = new StickyDockLocalGestureCompletion(completed.GestureId,
                            completed.Kind, StickyDockCommitIntent.Detach, child, null,
                            completed.SceneRevision, completed.TopologyGeneration,
                            completed.MemberIds, affected.AsReadOnly(), versions, completed.RollbackTargets);
                        PhysicalRect childBefore = facts[child].PhysicalBounds;
                        Pc2Call(scene.Host, "HandleCommand", new StickyUiCommand(
                            StickyUiCommandKind.SetBounds, child, false, null,
                            new StickyUiBounds(childBefore.Left + 60, childBefore.Top,
                                childBefore.Width, childBefore.Height)));
                        // An unknown dependency makes TryAdd reject even a complete capture.
                        Pc2Set(scene.Host, "_activeLocalDockDependency", captureFailure ? 0L : 999L);
                        Pc2Call(scene.Host, "PublishLocalDockCompletion", detached);
                        var captured = (StickyUiCommandResult)Pc2Call(scene.Host,
                            "HandleCommand", StickyUiCommand.CaptureWindowFacts(child, scene.Topology));
                        Pc2Assert(captured.Facts.PhysicalBounds.Equals(childBefore),
                            "unpublished commit restores physical position");
                        Pc2Assert(((StickyDockSceneProjection)Pc2Get(runtime, "_scene"))
                            .VisibleGroup(child).Count == 3 &&
                            ((StickyDockCommitQueue)Pc2Get(scene.Host, "_dockCommitQueue")).PendingCount == 0,
                            "failed capture/enqueue cannot leave a provisional split or pending ACK");
                    }
                    var full = (StickyDockCommitQueue)Pc2Get(scene.Host, "_dockCommitQueue");
                    for (int i = 1; i <= 8; i++)
                        full.TryAdd(new StickyDockGestureCommit(100 + i, 0, StickyDockCommitIntent.Move,
                            source, null, scene.Topology.Generation, new Dictionary<string, long> { [source] = 1 },
                            new DockBatchMemberResult[0]));
                    var deniedSession = DockSession(scene, 0);
                    var deniedWindow = (StickyNoteWindow)Pc2Get(deniedSession, "_window");
                    Pc2Call(deniedSession, "HeaderDragStarted", deniedWindow, EventArgs.Empty);
                    Pc2Assert((bool)Pc2Get(deniedWindow, "_dockGestureRejected") && !runtime.IsActive,
                        "queue refusal synchronously denies the native header drag");
                    Pc2Call(deniedSession, "HeaderDragCompleted", deniedWindow, EventArgs.Empty);
                    full.Clear();
                    return StickyUiCommandResult.Handled();
                });
                StickyUiCommandResult checkedResult = scene.Send(
                    StickyUiCommand.PrepareDockStructure(scene.Ids));
                scene.Host.SetCommandHandler(command =>
                    (StickyUiCommandResult)Pc2Call(scene.Host, "HandleCommand", command));
                Pc2Assert(checkedResult.Status == StickyUiCommandStatus.Handled,
                    "final native failure probe: " + checkedResult.Error);
                evidence.Add("R24 final native batch failure: one attempt, no commit, actual HWND bounds restored.");
            }
        }
    }
}
