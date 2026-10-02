using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private static bool RunDisplayTopologyRuntimeCheck()
        {
            DisplayTargetIdentity target1 = FakeTarget("mdp:fake-1");
            DisplayTargetIdentity target2 = FakeTarget("mdp:fake-2");
            DisplayTargetIdentity target3 = FakeTarget("mdp:fake-3");
            DisplaySurfaceSnapshot surface1 = FakeSurface(1, 0, true,
                1080, 1032, target1);
            DisplaySurfaceSnapshot surface2 = FakeSurface(2, 1920, false,
                1080, 1032, target2);
            DisplaySurfaceSnapshot surface3 = FakeSurface(3, 3840, false,
                1080, 1032, target3);
            DisplayTopologySnapshot two = new DisplayTopologySnapshot(0,
                new[] { surface1, surface2 });
            DisplayTopologySnapshot three = new DisplayTopologySnapshot(0,
                new[] { surface1, surface2, surface3 });
            DisplayTopologySnapshot reordered = new DisplayTopologySnapshot(0,
                new[] { surface2, surface1 });
            DisplaySurfaceSnapshot shifted2 = FakeSurface(2, 1920, false,
                1080, 932, target2);
            DisplayTopologySnapshot workShifted = new DisplayTopologySnapshot(0,
                new[] { FakeSurface(1, 0, true, 1080, 932, target1) });
            DisplayTopologySnapshot one = new DisplayTopologySnapshot(0,
                new[] { surface1 });

            int captures = 0;
            DisplayTopologySnapshot current = two;
            DisplayTopologySnapshot lastEventSnapshot = null;
            long lastEventGeneration = -1;
            int changeEventCount = 0;
            bool initialGenerationZero = false;
            bool sameSnapshotUnchanged = false;
            bool addedSurface = false;
            bool removedMany = false;
            bool reorderIgnored = false;
            bool workAreaChanged = false;
            bool rapidHintsOneSettledCapture = false;
            using (DisplayTopologyRuntime runtime =
                new DisplayTopologyRuntime(delegate
                {
                    captures++;
                    return current;
                }))
            {
                runtime.TopologyChanged += delegate(string reason,
                    DisplayTopologySnapshot snapshot)
                {
                    lastEventSnapshot = snapshot;
                    lastEventGeneration = snapshot == null
                        ? -1 : snapshot.Generation;
                    changeEventCount++;
                };
                runtime.CaptureInitial();
                initialGenerationZero =
                    runtime.Generation == 0 && runtime.Current != null &&
                    runtime.Current.Generation == 0 &&
                    Object.ReferenceEquals(runtime.Current,
                        lastEventSnapshot) &&
                    captures == 1;

                runtime.NotifyPotentialChange("same");
                runtime.FlushPendingForTest();
                sameSnapshotUnchanged =
                    runtime.Generation == 0 && captures == 2;

                current = reordered;
                runtime.NotifyPotentialChange("reorder");
                runtime.FlushPendingForTest();
                reorderIgnored = runtime.Generation == 0 &&
                    captures == 3;

                current = three;
                runtime.NotifyPotentialChange("added");
                runtime.FlushPendingForTest();
                addedSurface = runtime.Generation == 1 &&
                    runtime.Current.Generation == 1 &&
                    Object.ReferenceEquals(runtime.Current,
                        lastEventSnapshot) &&
                    lastEventGeneration == 1 &&
                    captures == 4;

                current = one;
                runtime.NotifyPotentialChange("removed");
                runtime.FlushPendingForTest();
                removedMany = runtime.Generation == 2 &&
                    runtime.Current.Generation == 2 &&
                    lastEventGeneration == 2 &&
                    captures == 5;

                current = workShifted;
                runtime.NotifyPotentialChange("workarea");
                runtime.FlushPendingForTest();
                workAreaChanged = runtime.Generation == 3 &&
                    runtime.Current.Generation == 3 &&
                    lastEventGeneration == 3 &&
                    captures == 6;
            }
            // initial + added + removed + workarea = 4 published snapshots;
            // "same" and "reorder" publish nothing.
            bool eventSnapshotsMatchGeneration = changeEventCount == 4;

            int burstCaptures = 0;
            using (DisplayTopologyRuntime burst =
                new DisplayTopologyRuntime(delegate
                {
                    burstCaptures++;
                    return two;
                }))
            {
                burst.CaptureInitial();
                for (int index = 0; index < 20; index++)
                    burst.NotifyPotentialChange("hint-" + index);
                burst.FlushPendingForTest();
                rapidHintsOneSettledCapture =
                    burstCaptures == 2 && burst.Generation == 0;
            }
            bool retriesRecoverAndStop;
            int retryCaptures = 0;
            bool failCapture = true;
            using (DisplayTopologyRuntime retry = new DisplayTopologyRuntime(delegate
            {
                retryCaptures++;
                if (failCapture) throw new InvalidOperationException("capture unavailable");
                return two;
            }))
            {
                retry.CaptureInitial();
                failCapture = false;
                retry.FlushPendingForTest();
                retriesRecoverAndStop = retry.Current != null && retry.Generation == 0 &&
                    retryCaptures == 2;
                DisplayTopologySnapshot valid = retry.Current;
                failCapture = true;
                retry.NotifyPotentialChange("transient display failure");
                for (int index = 0; index < 10; index++) retry.FlushPendingForTest();
                retriesRecoverAndStop &= retryCaptures == 2 + 1 +
                    DisplayTopologyRuntime.CaptureRetryLimit &&
                    Object.ReferenceEquals(valid, retry.Current);
                failCapture = false;
                retry.NotifyPotentialChange("new external hint");
                retry.FlushPendingForTest();
                retriesRecoverAndStop &= retryCaptures == 4 +
                    DisplayTopologyRuntime.CaptureRetryLimit && retry.Generation == 0;
            }
            return initialGenerationZero && sameSnapshotUnchanged &&
                addedSurface && removedMany && reorderIgnored &&
                workAreaChanged && eventSnapshotsMatchGeneration &&
                rapidHintsOneSettledCapture && retriesRecoverAndStop;
        }

        private static DisplayTargetIdentity FakeTarget(string key)
        {
            return new DisplayTargetIdentity(key, true, String.Empty,
                "fake", 0, 0, 0);
        }

        private static DisplaySurfaceSnapshot FakeSurface(int index,
            int left, bool primary, int height, int workHeight,
            DisplayTargetIdentity target)
        {
            return new DisplaySurfaceSnapshot("surface-" + index,
                "\\\\.\\DISPLAY" + index,
                new PhysicalRect(left, 0, 1920, height),
                new PhysicalRect(left, 0, 1920, workHeight),
                primary, 0, new[] { target });
        }

        private static bool RunStickySnapshotSeparationCheck()
        {
            StickyNoteData source = new StickyNoteData();
            source.Id = "separation-source";
            source.Title = "新标题";
            source.Text = "新正文";
            source.X = 100;
            source.Y = 200;
            source.Width = 320;
            source.Height = 300;
            source.LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY1",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 300 });
            source.PreferredPlacement = new WindowPlacementPreference("mdp:preferred",
                new LogicalRect { X = 0, Y = 0, Width = 640, Height = 240 });
            source.Visible = false;
            source.AlwaysOnTop = false;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.Capture(source);

            StickyNoteData target = new StickyNoteData();
            target.X = 999;
            target.Y = 888;
            target.Width = 123;
            target.Height = 456;
            target.LegacyPlacement = new StickyLegacyPlacement("OLD-DISPLAY",
                new LogicalRect { X = 7, Y = 8, Width = 123, Height = 456 });
            snapshot.ApplyContentTo(target);
            bool contentOnly = target.Title == "新标题" &&
                target.Text == "新正文" &&
                target.Id != "separation-source" &&
                target.X == 999 && target.Y == 888 &&
                target.Width == 123 && target.Height == 456 &&
                target.LegacyPlacement.RuntimeGdiName == "OLD-DISPLAY" &&
                target.LegacyPlacement.Logical.X == 7 &&
                target.LegacyPlacement.Logical.Y == 8 &&
                target.LegacyPlacement.Logical.Width == 123 &&
                target.LegacyPlacement.Logical.Height == 456;

            StickyNoteData editor = snapshot.CreateWorkingCopy();
            StickyNoteData defaults = new StickyNoteData();
            bool editorCopyOnly = editor.Id == "separation-source" &&
                editor.Title == source.Title && editor.Text == source.Text &&
                !editor.Visible && !editor.AlwaysOnTop &&
                editor.X == defaults.X && editor.Y == defaults.Y &&
                editor.Width == defaults.Width && editor.Height == defaults.Height &&
                editor.LegacyPlacement == null &&
                editor.PreferredPlacement == null;

            WindowFacts facts = new WindowFacts("sep-note", "mdp:sep",
                "\\\\.\\DISPLAY2",
                new PhysicalRect(1920, 0, 640, 600), 144, 3, 5);
            return contentOnly && editorCopyOnly && facts.Scale == 1.5 &&
                facts.WindowId == "sep-note";
        }

        // DRT-5 pure placement contract: the logical rect is projected with a
        // long-arithmetic rounding policy, the tolerance gate drives the one
        // corrective placement, and a Schedule keeps its 320x360 logical size
        // from spawn time (never 300 first).
        private static bool RunNativePlacementCheck()
        {
            PhysicalRect projected = DisplayGeometry.ProjectLocalRect(
                new LogicalRect
                {
                    X = 100,
                    Y = 50,
                    Width = 320,
                    Height = 300
                }, 1920, 0, 1.5);
            bool projectionOk = projected.Left == 1920 + 150 &&
                projected.Top == 75 &&
                projected.Width == 480 && projected.Height == 450;

            PhysicalRect requested = new PhysicalRect(100, 200, 480, 450);
            bool toleranceOk =
                DisplayGeometry.IsWithinPlacementTolerance(requested,
                    new PhysicalRect(102, 202, 480, 450), 2) &&
                !DisplayGeometry.IsWithinPlacementTolerance(requested,
                    new PhysicalRect(103, 200, 480, 450), 2) &&
                !DisplayGeometry.IsWithinPlacementTolerance(requested,
                    new PhysicalRect(100, 200, 483, 450), 2);

            bool toleranceConstantOk =
                WindowsWindowPlacementExecutor.PlacementTolerancePixels == 2;

            // Centered spawn policy: a Schedule keeps its 320x360 logical
            // size and lands in the WorkArea center (never beside the pet).
            PhysicalRect schedule = StickySpawnPolicy.PlanCenteredSpawn(
                new PhysicalRect(0, 0, 1920, 1040), 1.0, 320, 360);
            bool scheduleCenteredOk = schedule.Width == 320 && schedule.Height == 360 &&
                schedule.Left == 800 && schedule.Top == 340;
            PhysicalRect scaled = StickySpawnPolicy.PlanCenteredSpawn(
                new PhysicalRect(0, 0, 1920, 1040), 2.0, 320, 300);
            bool scaledCenteredOk = scaled.Width == 640 && scaled.Height == 600 &&
                scaled.Left == 640 && scaled.Top == 220;

            return projectionOk && toleranceOk && toleranceConstantOk &&
                scheduleCenteredOk && scaledCenteredOk;
        }

        // DRT-6 v11 contract: codec round-trips the durable preferred fields,
        // only user-gesture reasons may commit a preference, and a v10 note
        // migrates its display-local rect into a durable target key without
        // overwriting an existing preference.
        private static bool RunV11PreferredCheck()
        {
            StickyNoteData source = new StickyNoteData
            {
                Id = "v11-check",
                PreferredPlacement = new WindowPlacementPreference("mdp:home",
                    new LogicalRect { X = -10, Y = 30, Width = 320, Height = 300 })
            };
            string line = StickyNoteCodec.SerializeLine(source);
            bool headerAndFields =
                line.StartsWith("11|", StringComparison.Ordinal) &&
                line.Split('|').Length ==
                    StickyNoteCodec.CurrentFieldCount;
            StickyNoteData parsed = StickyNoteCodec.ParseLine(line);
            bool roundTrip = parsed != null &&
                parsed.PreferredPlacement.PreferredTargetKey == "mdp:home" &&
                parsed.PreferredPlacement.LocalLogicalRect.X == -10 &&
                parsed.PreferredPlacement.LocalLogicalRect.Width == 320;

            bool reasonGuard =
                StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.UserMoveCommit) &&
                StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.UserResizeCommit) &&
                StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.Spawn) &&
                !StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.Restore) &&
                !StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.DockLiveFollower) &&
                !StickyPlacementRules.CanCommitPreferred(
                    PlacementReason.TemporaryRehome);

            StickyNoteData v10 = new StickyNoteData
            {
                Id = "v10-check",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY2",
                    new LogicalRect { X = 5, Y = 6, Width = 320, Height = 300 })
            };
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(0, new[]
                {
                    FakeSurface(2, 1920, false, 1080, 1032,
                        FakeTarget("mdp:fake-2"))
                });
            bool migrated = StickyPlacementRules.MigrateV10Preferred(
                v10, topology) &&
                v10.PreferredPlacement.PreferredTargetKey == "mdp:fake-2" &&
                v10.PreferredPlacement.LocalLogicalRect.X == 5 &&
                v10.PreferredPlacement.LocalLogicalRect.Width == 320;

            return headerAndFields && roundTrip && reasonGuard && migrated;
        }

        // DRT-7 state machine: a temporary rehome preserves Effective facts,
        // a user placement commit ends it and blocks a later pull-back, and a
        // fresh rehome resets the intent window.
        private static bool RunTemporaryRehomeCheck()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            runtime.TryUpdateEffective("rehome-note",
                new WindowFacts("rehome-note", "mdp:b", "\\\\.\\DISPLAY2",
                    new PhysicalRect(1920, 0, 640, 600), 192, 4, 5));
            runtime.MarkTemporaryRehome("rehome-note",
                "preferred-display-missing");
            bool marked = runtime.IsTemporaryRehome("rehome-note") &&
                !runtime.UserMovedSinceRehome("rehome-note") &&
                runtime.TemporaryReason("rehome-note") ==
                    "preferred-display-missing";

            runtime.TryUpdateEffective("rehome-note",
                new WindowFacts("rehome-note", "mdp:fallback",
                    "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 640, 600),
                    96, 4, 6));
            bool factsUpdatePreservesFlags =
                runtime.IsTemporaryRehome("rehome-note") &&
                runtime.GetEffective("rehome-note").Dpi == 96;

            runtime.MarkUserPlacementCommit("rehome-note");
            bool userMovedEndsRehome =
                !runtime.IsTemporaryRehome("rehome-note") &&
                runtime.UserMovedSinceRehome("rehome-note");

            runtime.MarkTemporaryRehome("rehome-note", "again");
            bool newRehomeResetsIntent =
                runtime.IsTemporaryRehome("rehome-note") &&
                !runtime.UserMovedSinceRehome("rehome-note");

            runtime.MarkReturnedToPreferred("rehome-note");
            bool returnedClearsFlags =
                !runtime.IsTemporaryRehome("rehome-note") &&
                !runtime.UserMovedSinceRehome("rehome-note");

            return marked && factsUpdatePreservesFlags &&
                userMovedEndsRehome && newRehomeResetsIntent &&
                returnedClearsFlags;
        }

        // Final results now cross the owner boundary once, in dependency
        // order. No live frame mailbox participates in the product path.
        private static bool RunDockCommitHandoffCheck()
        {
            var queue = new StickyDockCommitQueue();
            var first = new StickyDockGestureCommit(1, 0,
                StickyDockCommitIntent.Move, "a", null, 3, null, null);
            var second = new StickyDockGestureCommit(2, 1,
                StickyDockCommitIntent.DividerResize, "a", null, 3, null, null);
            bool ordered = queue.TryAdd(first) &&
                ReferenceEquals(queue.PeekReady(), first) &&
                queue.TryAdd(second) && queue.PeekReady() == null &&
                !queue.TryAdd(first);
            bool acknowledged = queue.Acknowledge(
                new StickyDockCommitAck(1, true)).Accepted &&
                ReferenceEquals(queue.PeekReady(), second) &&
                !queue.Acknowledge(new StickyDockCommitAck(1, true)).Matched &&
                queue.Acknowledge(new StickyDockCommitAck(2, true)).Accepted &&
                queue.PendingCount == 0;

            StickyNoteData snapshotSource = new StickyNoteData
            {
                Title = "content-only",
                Visible = true,
                AlwaysOnTop = true,
                X = 120,
                Y = 240,
                Width = 360,
                Height = 480,
                LegacyPlacement = new StickyLegacyPlacement("legacy-display",
                    new LogicalRect { X = 0, Y = 0, Width = 360, Height = 480 }),
                PreferredPlacement = new WindowPlacementPreference("preferred-target",
                    new LogicalRect { X = 0, Y = 0, Width = 360, Height = 480 })
            };
            StickyNoteUiSnapshot contentOnly =
                StickyNoteUiSnapshot.Capture(snapshotSource);
            StickyNoteData contentCopy = contentOnly.CreateWorkingCopy();
            StickyNoteData defaultCopy = new StickyNoteData();
            bool contentSnapshotIsNarrow =
                contentOnly.NoteId == snapshotSource.Id &&
                contentOnly.Title == "content-only" &&
                contentOnly.Visible && contentOnly.AlwaysOnTop &&
                contentCopy.X == defaultCopy.X && contentCopy.Y == defaultCopy.Y &&
                contentCopy.Width == defaultCopy.Width && contentCopy.Height == defaultCopy.Height &&
                contentCopy.LegacyPlacement == null &&
                contentCopy.PreferredPlacement == null;

            var targets = new List<DockWindowTarget> { new DockWindowTarget("a", new PhysicalRect(1, 2, 300, 230)) };
            var detachedPlan = new DockPlacementPlan(1, 1, "a", "surface-1", 96, targets);
            targets.Clear();
            var members = new List<DockBatchMemberResult> { new DockBatchMemberResult("a", 1, null, contentOnly) };
            var detachedBatch = new DockBatchResult(1, 1, members);
            members.Clear();
            return ordered && acknowledged && contentSnapshotIsNarrow &&
                detachedPlan.WindowTargets.Count == 1 && detachedBatch.Members.Count == 1;
        }

        // DISPLAYCONFIG_TARGET_DEVICE_NAME is a wire ABI passed directly to
        // DisplayConfigGetDeviceInfo.  Verify field widths and offsets so a
        // future harmless-looking managed refactor cannot corrupt monitor
        // identity reads on mixed-DPI topologies.
        private static bool RunNativeDisplayAbiCheck()
        {
            return Marshal.SizeOf(typeof(DisplayConfigTargetDeviceName)) == 420 &&
                Marshal.OffsetOf(typeof(DisplayConfigTargetDeviceName),
                    "EdidManufactureId").ToInt32() == 28 &&
                Marshal.OffsetOf(typeof(DisplayConfigTargetDeviceName),
                    "EdidProductCodeId").ToInt32() == 30 &&
                Marshal.OffsetOf(typeof(DisplayConfigTargetDeviceName),
                    "ConnectorInstance").ToInt32() == 32 &&
                Marshal.OffsetOf(typeof(DisplayConfigTargetDeviceName),
                    "MonitorFriendlyDeviceName").ToInt32() == 36 &&
                Marshal.OffsetOf(typeof(DisplayConfigTargetDeviceName),
                    "MonitorDevicePath").ToInt32() == 164;
        }

        private static bool RunDockTopologyReprojectCheck()
        {
            DisplaySurfaceSnapshot surface = new DisplaySurfaceSnapshot(
                "surface-hotplug", "\\\\.\\DISPLAY9",
                new PhysicalRect(-1920, 0, 1920, 1080),
                new PhysicalRect(-1920, 0, 1920, 1040), false, 0,
                new[]
                {
                    new DisplayTargetIdentity("mdp:hotplug", true,
                        "path", "Hotplug", 0, 0, 0)
                });
            DockGroupLogicalState group = new DockGroupLogicalState(
                new LogicalPoint { X = 10, Y = 20 }, new[]
                {
                    new DockLogicalMember("a", 320, 300),
                    new DockLogicalMember("b", 320, 400)
                });
            DockPlacementPlan plan = DockPlacementPlanner.PlanReproject(
                new DockGroupReprojectPlan(7, 11, "surface-hotplug",
                    group, true), surface, 192);

            // Legacy dimensions deliberately contradict accepted actual facts.
            StickyNoteData reprojA = new StickyNoteData();
            reprojA.Id = "reproj-a";
            reprojA.LegacyPlacement = new StickyLegacyPlacement("old",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 1 });
            reprojA.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { X = 900, Y = 800, Width = 500, Height = 600 });
            StickyNoteData reprojB = new StickyNoteData();
            reprojB.Id = "reproj-b";
            reprojB.LegacyPlacement = new StickyLegacyPlacement("old",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 999 });
            reprojB.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { X = 900, Y = 1400, Width = 500, Height = 700 });
            List<StickyNoteData> reprojGroup =
                new List<StickyNoteData> { reprojA, reprojB };

            DisplayTopologySnapshot captureTopology = new DisplayTopologySnapshot(7, new[] { surface });
            StickyPlacementRuntime captureRuntime = new StickyPlacementRuntime();
            captureRuntime.TryUpdateEffective(reprojA.Id,
                new WindowFacts(reprojA.Id, String.Empty, surface.RuntimeGdiName,
                    new PhysicalRect(surface.Bounds.Left + 20, surface.Bounds.Top + 40, 640, 600), 192, 7, 1),
                captureTopology);
            captureRuntime.TryUpdateEffective(reprojB.Id,
                new WindowFacts(reprojB.Id, String.Empty, surface.RuntimeGdiName,
                    new PhysicalRect(surface.Bounds.Left + 20, surface.Bounds.Top + 640, 640, 720), 192, 7, 1),
                captureTopology);
            DockGroupLogicalState runtimeState;
            bool runtimeOk = StickyDockController.TryBuildDockTopologyLogicalState(
                reprojGroup, DockTopologyReprojectReason.CurrentRuntimeRepair,
                out runtimeState, captureRuntime) &&
                runtimeState.RootAnchor.X == 10 &&
                runtimeState.RootAnchor.Y == 20 &&
                runtimeState.Members[0].Width == 320 &&
                runtimeState.Members[0].Height == 300 &&
                runtimeState.Members[1].Width == 320 &&
                runtimeState.Members[1].Height == 360;

            DockGroupLogicalState returnState;
            bool returnOk = StickyDockController.TryBuildDockTopologyLogicalState(
                reprojGroup, DockTopologyReprojectReason.PreferredReturn,
                out returnState) &&
                returnState.RootAnchor.X == 900 &&
                returnState.RootAnchor.Y == 800 &&
                returnState.Members[0].Width == 500 &&
                returnState.Members[0].Height == 600 &&
                returnState.Members[1].Width == 500 &&
                returnState.Members[1].Height == 700;

            DockGroupLogicalState rehomeState;
            bool rehomeOk = StickyDockController.TryBuildDockTopologyLogicalState(
                reprojGroup, DockTopologyReprojectReason.TemporaryRehome,
                out rehomeState) &&
                rehomeState.Members[0].Width == 500 &&
                rehomeState.Members[0].Height == 600 &&
                rehomeState.Members[1].Width == 500 &&
                rehomeState.Members[1].Height == 700;

            StickyNoteData badLocalA = new StickyNoteData();
            badLocalA.Id = "reproj-bad-local-a";
            badLocalA.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { Width = 500, Height = 600 });
            StickyNoteData badLocalB = new StickyNoteData();
            badLocalB.Id = "reproj-bad-local-b";
            badLocalB.PreferredPlacement = new WindowPlacementPreference("mdp:one",
                new LogicalRect { Width = 500, Height = 700 });
            DockGroupLogicalState rejectedLocal;
            bool localRejected =
                !StickyDockController.TryBuildDockTopologyLogicalState(
                    new List<StickyNoteData> { badLocalA, badLocalB },
                    DockTopologyReprojectReason.CurrentRuntimeRepair,
                    out rejectedLocal) && rejectedLocal == null;

            StickyNoteData badPrefA = new StickyNoteData();
            badPrefA.Id = "reproj-bad-pref-a";
            badPrefA.LegacyPlacement = new StickyLegacyPlacement("old",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 300 });
            StickyNoteData badPrefB = new StickyNoteData();
            badPrefB.Id = "reproj-bad-pref-b";
            badPrefB.LegacyPlacement = new StickyLegacyPlacement("old",
                new LogicalRect { X = 10, Y = 20, Width = 320, Height = 360 });
            DockGroupLogicalState rejectedPreferred;
            bool preferredRejected =
                !StickyDockController.TryBuildDockTopologyLogicalState(
                    new List<StickyNoteData> { badPrefA, badPrefB },
                    DockTopologyReprojectReason.PreferredReturn,
                    out rejectedPreferred) && rejectedPreferred == null;

            bool reasonOwnedGeometry = runtimeOk && returnOk && rehomeOk &&
                localRejected && preferredRejected;

            return plan.TopologyGeneration == 7 &&
                plan.PlanSequence == 11 &&
                plan.SourceNoteId == String.Empty &&
                plan.TargetDpi == 192 &&
                plan.WindowTargets.Count == 2 &&
                plan.WindowTargets[0].PhysicalBounds.Width == 640 &&
                plan.WindowTargets[0].PhysicalBounds.Bottom ==
                    plan.WindowTargets[1].PhysicalBounds.Top &&
                reasonOwnedGeometry;
        }

        // Z-order band contract: the pure raise sequence preserves membership
        // and puts the source last for root/middle/tail drags. The local
        // host rejects duplicates, missing source and short groups.
        private static bool RunDockZOrderCheck()
        {
            string[] order;

            bool rootSource =
                StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a", "b", "c" }, "a", out order) &&
                order.Length == 3 &&
                order[0] == "c" && order[1] == "b" && order[2] == "a";

            bool middleSource =
                StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a", "b", "c" }, "b", out order) &&
                order.Length == 3 &&
                order[0] == "c" && order[1] == "a" && order[2] == "b";

            bool tailSource =
                StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a", "b", "c" }, "c", out order) &&
                order.Length == 3 &&
                order[0] == "b" && order[1] == "a" && order[2] == "c";

            bool duplicateRejected =
                !StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a", "b", "b" }, "a", out order) &&
                order == null;

            bool missingSourceRejected =
                !StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a", "b" }, "x", out order) &&
                order == null;

            bool shortGroupRejected =
                !StickyUiHost.TryBuildDockDragRaiseOrder(
                    new[] { "a" }, "a", out order) &&
                order == null;

            return rootSource && middleSource && tailSource &&
                duplicateRejected && missingSourceRejected && shortGroupRejected;
        }

        // A broken DisplayId -> metrics resolver silently mis-places restored
        // stickies on the wrong monitor. Verify that resolving by device id
        // agrees with the point/rect resolver for the primary monitor so a
        // regression fails loudly instead of landing notes on the wrong screen.
        private static void RunDisplayResolverConsistencyCheck()
        {
            WindowsDisplayMetrics primary =
                WindowsDisplayResolver.ResolvePhysicalRect(0, 0, 1, 1);
            if (primary == null) return; // headless: nothing to validate
            WindowsDisplayMetrics byDisplay =
                WindowsDisplayResolver.ResolveDisplay(primary.DisplayId);
            if (byDisplay == null)
                throw new InvalidOperationException(
                    "ResolveDisplay could not resolve primary monitor '" +
                    primary.DisplayId + "'.");
            if (byDisplay.Scale != primary.Scale ||
                byDisplay.PhysicalLeft != primary.PhysicalLeft ||
                byDisplay.PhysicalTop != primary.PhysicalTop)
                throw new InvalidOperationException(
                    "ResolveDisplay metrics differ from ResolvePhysicalRect " +
                    "for monitor '" + primary.DisplayId + "'.");
        }
    }
}
