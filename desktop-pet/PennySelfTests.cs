using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        // Test-only construction skips PetForm's product startup (real user
        // files, hooks, art, tray). We invoke the unchanged production methods
        // on isolated collaborators, not a copied acceptance implementation.
        private sealed class Pc2Scene : IDisposable
        {
            internal readonly PetForm Pet;
            internal readonly StickyNoteRepository Repository;
            internal readonly StickyHostedRuntime Hosted = new StickyHostedRuntime();
            internal readonly StickyPlacementRuntime Placement = new StickyPlacementRuntime();
            internal readonly DockInteractionSession Interaction = new DockInteractionSession();
            internal readonly StickyUiHost Host = new StickyUiHost();
            internal readonly Pc2Context Context = new Pc2Context();
            internal readonly DisplayTopologyRuntime Display;
            internal readonly List<StickyNoteData> Notes = new List<StickyNoteData>();
            internal IReadOnlyDictionary<string, DockWindowFacts> Active
                { get { return Interaction.PreviewFacts; } }
            internal IReadOnlyDictionary<string, DockWindowFacts> Original
                { get { return Interaction.BaselineFacts; } }
            internal readonly string PathName;
            private readonly PetContextMenu menu;
            private readonly PetBubbleCoordinator bubble;
            private bool started;

            internal Pc2Scene(string root, string name, bool native = false)
            {
                string directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                PathName = Path.Combine(directory, "sticky-notes.dat");
                Repository = (StickyNoteRepository)Activator.CreateInstance(
                    typeof(StickyNoteRepository), BindingFlags.Instance |
                    BindingFlags.NonPublic, null, new object[] { PathName }, null);
                Pet = (PetForm)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(PetForm));
                GC.SuppressFinalize(Pet); // no native Pet resource was created
                DisplayTopologySnapshot supplied = native
                    ? new WindowsDisplayTopologyProvider().Capture()
                    : new DisplayTopologySnapshot(0, new[] {
                        new DisplaySurfaceSnapshot("pc2-surface", "pc2-display",
                            new PhysicalRect(0, 0, 1920, 1080),
                            new PhysicalRect(0, 0, 1920, 1040), true, 0,
                            new[] { new DisplayTargetIdentity("mdp:pc2", true,
                                "pc2-path", "pc2", 0, 0, 0) }) });
                Display = new DisplayTopologyRuntime(delegate { return supplied; });
                Display.CaptureInitial();
                Pc2Assert(Display.Current != null, "topology available");
                menu = new PetContextMenu("PC2 test", false, false, true,
                    new PetContextMenuCommands());
                // Feedback is queued rather than displayed; only test UI sink
                // behavior is selected. Rejection decisions are not mocked.
                bubble = new PetBubbleCoordinator(Pet, () => true, () => false,
                    null, null);
                Pc2Set(Pet, "_notes", Repository);
                Pc2Set(Pet, "_hostedRuntime", Hosted);
                Pc2Set(Pet, "_placementRuntime", Placement);
                Pc2Set(Pet, "_displayTopologyRuntime", Display);
                Pc2Set(Pet, "_dockInteraction", Interaction);
                Pc2Set(Pet, "_dockPlanMailbox", new DockPlanMailbox());
                Pc2Set(Pet, "_lastAppliedDockPlanSequence", -1L);
                Pc2Set(Pet, "_stickyUiHost", Host);
                Pc2Set(Pet, "_petUiContext", Context);
                Pc2Set(Pet, "_settings", new PetSettings());
                Pc2Set(Pet, "_reminders", new ReminderSchedule());
                Pc2Set(Pet, "_petContextMenu", menu);
                Pc2Set(Pet, "_bubbleCoordinator", bubble);
                Pc2Set(Pet, "_pendingHostedDockRestoreGroups", new HashSet<string>());
                Pc2Set(Pet, "_expectedFirstRenderNoteIds", new HashSet<string>());
                Pc2Set(Pet, "_renderedFirstRenderNoteIds", new HashSet<string>());
                for (int i = 0; i < 3; i++)
                {
                    StickyNoteData note = Repository.CreateDraft("before-" + i,
                        new Point(100, 100 + 230 * i));
                    note.Id = "pc2-" + i;
                    note.Width = 300; note.Height = 230;
                    note.DisplayId = Surface.RuntimeGdiName;
                    note.LocalLogicalX = 100; note.LocalLogicalY = 100 + 230 * i;
                    note.LocalLogicalWidth = 300; note.LocalLogicalHeight = 230;
                    note.PreferredDisplayTargetKey = DisplayTopologyRules
                        .SelectPreferredTargetKey(Surface, null);
                    note.PreferredLocalLogicalX = 100;
                    note.PreferredLocalLogicalY = 100 + 230 * i;
                    note.PreferredLocalLogicalWidth = 300;
                    note.PreferredLocalLogicalHeight = 230;
                    note.AlwaysOnTop = false; note.Visible = true;
                    Notes.Add(note); Hosted.AddNote(note.Id);
                    Hosted.RecordSequence(note.Id, 1);
                    Placement.TryUpdateEffective(note.Id, Facts(i, 1, 100));
                }
                StickyDockGroups.ApplyOrderedGroup(Notes);
                Dictionary<string, DockWindowFacts> baseline = new Dictionary<string, DockWindowFacts>();
                foreach (StickyNoteData note in Notes) baseline[note.Id] = DockWindowFacts.FromData(note);
                Interaction.BeginGesture(baseline[Notes[0].Id], Ids, baseline, Topology.Generation, DateTime.UtcNow);
                Interaction.TryEnterDragging(Interaction.Epoch, Topology.Generation);
                Host.SetCurrentTopology(Topology);
                Host.SetCurrentDockInteractionEpoch(Interaction.Epoch);
                Pc2Assert(Repository.Save().Succeeded, "isolated baseline saved");
            }

            internal DisplayTopologySnapshot Topology { get { return Display.Current; } }
            internal DisplaySurfaceSnapshot Surface { get { return Topology.PrimaryOrFirst(); } }
            internal string[] Ids { get { return Notes.ConvertAll(n => n.Id).ToArray(); } }
            internal long Saves { get { return (long)Pc2Get(Pc2Get(Repository, "_writer"), "_requestedRevision"); } }
            internal long Sequence(int i)
            { return ((Dictionary<string, long>)Pc2Get(Hosted, "_appliedSequences"))[Notes[i].Id]; }
            internal WindowFacts Facts(int i, long sequence, int x)
            {
                return new WindowFacts(Notes[i].Id,
                    DisplayTopologyRules.SelectPreferredTargetKey(Surface, null),
                    Surface.RuntimeGdiName, new PhysicalRect(x, 100 + i * 230, 300, 230),
                    96, Topology.Generation, sequence);
            }
            internal DockBatchMemberResult Member(int i, long seq = 2, bool nullFacts = false)
            {
                StickyNoteData copy = StickyNoteUiSnapshot.FromData(Notes[i]).CreateWorkingCopy();
                copy.Text = "after-" + i;
                return new DockBatchMemberResult(copy.Id, seq,
                    nullFacts ? null : Facts(i, seq, 400), StickyNoteUiSnapshot.FromData(copy));
            }
            internal StickyUiCommandResult Batch(long plan, params DockBatchMemberResult[] members)
            { return StickyUiCommandResult.Handled(new DockBatchResult(plan,
                Topology.Generation, Surface.RuntimeSurfaceId, 96, members, Interaction.Epoch)); }
            internal void Start(Func<StickyUiCommand, StickyUiCommandResult> handler = null)
            {
                if (started) return;
                started = true;
                Host.Configure(delegate { }, Context);
                if (handler != null) Host.SetCommandHandler(handler);
                Host.Start();
            }
            internal StickyUiCommandResult Send(StickyUiCommand command)
            {
                StickyUiCommandResult result = null;
                Host.PostCommand(command, r => result = r, Context);
                Context.PumpUntil(() => result != null);
                return result;
            }
            public void Dispose()
            {
                Pc2Assert(Repository.WaitForPendingSaves().Succeeded, "isolated IO flush");
                Host.BeginShutdown();
                if (started)
                {
                    object threadHost = Pc2Get(Host, "_threadHost");
                    System.Threading.Thread thread = (System.Threading.Thread)Pc2Get(threadHost, "_thread");
                    Context.PumpUntil(() => thread == null || !thread.IsAlive);
                }
                Display.Dispose(); bubble.Dispose(); menu.Dispose();
            }
        }

        private sealed class Pc2Context : System.Threading.SynchronizationContext
        {
            private readonly Queue<Action> queue = new Queue<Action>();
            internal int Executed;
            public override void Post(System.Threading.SendOrPostCallback callback, object state)
            { lock (queue) queue.Enqueue(() => callback(state)); }
            internal void PumpUntil(Func<bool> done)
            {
                Stopwatch deadline = Stopwatch.StartNew();
                while (!done())
                {
                    Action next = null;
                    lock (queue) if (queue.Count > 0) next = queue.Dequeue();
                    if (next != null) { next(); Executed++; }
                    Application.DoEvents(); // test-driver pump, never product startup
                    if (deadline.ElapsedMilliseconds > 15000)
                        throw new InvalidOperationException("PC2 callback timeout");
                    System.Threading.Thread.Sleep(1);
                }
            }
        }

        private static void Pc2Set(object owner, string name, object value)
        { owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value); }
        private static object Pc2Get(object owner, string name)
        { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
        private static object Pc2Call(object owner, string method, params object[] args)
        {
            try { return owner.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        private static void Pc2Assert(bool value, string message)
        { if (!value) throw new InvalidOperationException("PC2 characterization: " + message); }

        private static void RunPc2CharacterizationChecks(string outputPath)
        {
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "pc2a");
            List<string> evidence = new List<string>();
            // A1: whole-set preflight rejects before any map/canonical/lease write.
            using (Pc2Scene s = new Pc2Scene(root, "A1"))
            {
                WindowFacts old = s.Facts(1, 100, 100);
                s.Placement.TryUpdateEffective(s.Notes[1].Id, old);
                long saves = s.Saves;
                object[] args = { s.Batch(0, s.Member(0), s.Member(1), s.Member(2)),
                    s.Ids, s.Topology, s.Interaction.Epoch, s.Notes[0].Id, true, null };
                bool accepted = (bool)Pc2Call(s.Pet, "TryApplyDockFactsBarrier", args);
                Pc2Assert(!accepted && args[6] == null &&
                    s.Notes.TrueForAll(n => n.X == 100) &&
                    s.Placement.GetEffective(s.Notes[0].Id).WindowSequence == 1 &&
                    ReferenceEquals(s.Placement.GetEffective(s.Notes[1].Id), old) &&
                    s.Placement.GetEffective(s.Notes[2].Id).WindowSequence == 1 &&
                    s.Sequence(0) == 1 && s.Sequence(1) == 1 && s.Sequence(2) == 1 &&
                    s.Active.Count == 3 && s.Original.Count == 3 && s.Saves == saves,
                    "A1 whole-set zero-mutation reject with null sourceFacts");
                evidence.Add("A1: false; zero canonical/effective/lease/map/save mutation; sourceFacts null.");
            }
            // A3: live batch rejects atomically; lastApplied does not advance.
            using (Pc2Scene s = new Pc2Scene(root, "A3"))
            {
                WindowFacts old = s.Facts(1, 100, 100);
                s.Placement.TryUpdateEffective(s.Notes[1].Id, old);
                long saves = s.Saves;
                Pc2Call(s.Pet, "ApplyDockBatchResult", s.Batch(10,
                    s.Member(0), s.Member(1), s.Member(2)).DockBatchResult);
                Pc2Assert(s.Notes.TrueForAll(n => n.X == 100) &&
                    s.Sequence(0) == 1 && s.Sequence(1) == 1 && s.Sequence(2) == 1 &&
                    ReferenceEquals(s.Placement.GetEffective(s.Notes[1].Id), old) &&
                    (long)Pc2Get(s.Pet, "_lastAppliedDockPlanSequence") == -1L &&
                    s.Active.Count == 3 && s.Saves == saves,
                    "A3 whole live batch zero mutation, lastApplied unchanged");
                evidence.Add("A3: void; whole batch rejected; zero canonical/effective/lease/lastApplied mutation; no Save.");
            }
            using (Pc2Scene s = new Pc2Scene(root, "A4"))
            {
                WindowFacts old = s.Facts(0, 100, 100);
                s.Placement.TryUpdateEffective(s.Notes[0].Id, old);
                s.Placement.MarkTemporaryRehome(s.Notes[0].Id, "test-rehome");
                long saves = s.Saves;
                string beforeText = s.Notes[0].Text;
                DockBatchMemberResult m = s.Member(0);
                bool accepted = (bool)Pc2Call(s.Pet, "ApplyReprojectResult",
                    StickyUiCommandResult.Handled(m.Snapshot, m.WindowSequence, m.Facts, s.Topology),
                    m.NoteId, s.Topology);
                s.Repository.WaitForPendingSaves();
                Pc2Assert(!accepted && s.Notes[0].X == 100 &&
                    s.Notes[0].Text == beforeText &&
                    s.Notes[0].Visible && !s.Notes[0].AlwaysOnTop &&
                    s.Sequence(0) == 1 &&
                    ReferenceEquals(s.Placement.GetEffective(m.NoteId), old) &&
                    s.Notes[0].PreferredLocalLogicalX == 100 &&
                    s.Placement.IsTemporaryRehome(m.NoteId) && s.Saves == saves,
                    "A4 rejected reproject leaves canonical/content/lease/Save/preferred/temp untouched");
                evidence.Add("A4: false; zero canonical/content/lease/Save mutation; preferred/temp/effective unchanged.");
            }
            // A5: every rejection class is now whole-set atomic.
            for (int scenario = 0; scenario < 3; scenario++)
            using (Pc2Scene s = new Pc2Scene(root, "A5-" + scenario))
            {
                long saves = s.Saves;
                string before = String.Join("\n", s.Notes.ConvertAll(StickyNoteCodec.SerializeLine));
                WindowFacts old = s.Facts(2, 100, 100);
                if (scenario == 2) s.Placement.TryUpdateEffective(s.Notes[2].Id, old);
                Pc2Call(s.Pet, "CompleteDockDurableCommit",
                    s.Batch(10, s.Member(0), s.Member(1),
                        s.Member(2, scenario == 1 ? 1 : 2, scenario == 0)),
                    s.Topology, s.Interaction.Epoch, s.Notes[0], null, s.Ids, 10L);
                Pc2Assert(before == String.Join("\n", s.Notes.ConvertAll(StickyNoteCodec.SerializeLine)) &&
                    s.Saves == saves && s.Sequence(0) == 1 &&
                    s.Placement.GetEffective(s.Notes[0].Id).WindowSequence == 1 &&
                    (scenario != 2 || (ReferenceEquals(s.Placement.GetEffective(s.Notes[2].Id), old) &&
                        s.Notes[2].PreferredLocalLogicalX == 100)),
                    "A5 whole-set zero mutation for structural/hosted/effective rejection");
                evidence.Add("A5-" + scenario + ": " + (scenario < 2
                    ? "invalid final member: zero canonical/preferred/lease/effective/order/Save mutation."
                    : "effective-only reject: whole-set zero durable mutation and zero Save."));
            }
            // A7: topology/restore consumer also rejects atomically.
            using (Pc2Scene s = new Pc2Scene(root, "A7"))
            {
                WindowFacts old = s.Facts(1, 100, 100);
                s.Placement.TryUpdateEffective(s.Notes[1].Id, old);
                long saves = s.Saves;
                string before = String.Join("\n", s.Notes.ConvertAll(StickyNoteCodec.SerializeLine));
                object[] args = { s.Batch(7, s.Member(0), s.Member(1), s.Member(2)),
                    s.Topology, s.Surface, s.Ids, 7L, true };
                bool accepted = (bool)Pc2Call(s.Pet, "TryApplyDockTopologyResult", args);
                Pc2Assert(!accepted &&
                    before == String.Join("\n", s.Notes.ConvertAll(StickyNoteCodec.SerializeLine)) &&
                    s.Saves == saves && s.Sequence(0) == 1 && s.Sequence(1) == 1 && s.Sequence(2) == 1 &&
                    ReferenceEquals(s.Placement.GetEffective(s.Notes[1].Id), old) &&
                    s.Notes.TrueForAll(n => n.Visible && !n.AlwaysOnTop),
                    "A7 topology/restore zero-mutation reject preserves visibility/topmost/save");
                evidence.Add("A7: false; zero canonical/effective/lease/visibility/topmost/Save mutation.");
            }
            RunPc2FailurePolicies(root, evidence);
            // Last: establish a real session recreation path, not just seeded counters.
            RunPc2Recreation(root, evidence);
            RunPc2EnsureSessionRuntime(root, evidence);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "characterization.json"),
                new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                    sourceGolden = "ab10b45705195bc7564db74c2d1f03dc996917ba",
                    characterizationPassed = true,
                    confirmedLatentCorrectnessDefect = false,
                    pc2b = "PENDING_HUMAN", observations = evidence.ToArray()
                }), new UTF8Encoding(false));
            string closureRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "pc2a5");
            Directory.CreateDirectory(closureRoot);
            File.WriteAllText(Path.Combine(closureRoot, "closure.json"),
                new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                    sourceDefectCharacterization = "b9a6e79a31cfb5f8af8794e4bb3ec23985d67516",
                    sessionRecreationClosed = true,
                    rejectionAtomicityClosed = true,
                    pc2b = "PENDING_HUMAN", observations = evidence.ToArray()
                }), new UTF8Encoding(false));
        }

        private static void RunPc2FailurePolicies(string root, List<string> evidence)
        {
            using (Pc2Scene s = new Pc2Scene(root, "A6-rebase"))
            {
                s.Start(c => StickyUiCommandResult.NotHandled());
                s.Interaction.BeginRebase(s.Topology.Generation);
                int before = s.Context.Executed;
                Pc2Call(s.Pet, "ResumeDockDragAfterTopologyChange", s.Topology);
                s.Context.PumpUntil(() => s.Context.Executed > before);
                Pc2Assert(s.Interaction.Phase == DockInteractionPhase.Rebasing && s.Active.Count == 3,
                    "A6 rebase rejection stays Rebasing");
            }
            using (Pc2Scene s = new Pc2Scene(root, "A6-live"))
            {
                s.Start(); // real ApplyDockPlan rejects missing native sessions
                int before = s.Context.Executed;
                DockPlacementPlan plan = new DockPlacementPlan(s.Topology.Generation, 10,
                    s.Notes[0].Id, s.Surface.RuntimeSurfaceId, 96,
                    s.Notes.ConvertAll(n => new DockWindowTarget(n.Id, new PhysicalRect(400, 100, 300, 230))),
                    s.Interaction.Epoch);
                Pc2Call(s.Pet, "ApplyLiveDockPlan", plan);
                s.Context.PumpUntil(() => s.Context.Executed > before);
                Pc2Assert(s.Interaction.Phase == DockInteractionPhase.Dragging &&
                    s.Notes[0].X == 100, "A6 live failure keeps drag/canonical");
            }
            using (Pc2Scene s = new Pc2Scene(root, "A6-final"))
            {
                s.Start(c => StickyUiCommandResult.NotHandled());
                int before = s.Context.Executed;
                Pc2Call(s.Pet, "StartDockFinalization", s.Notes[0], null);
                s.Context.PumpUntil(() => s.Context.Executed > before);
                Pc2Assert(s.Interaction.Phase == DockInteractionPhase.Idle && s.Active.Count == 0 &&
                    String.IsNullOrEmpty(s.Interaction.SourceNoteId), "A6 final failure resets");
            }
            using (Pc2Scene s = new Pc2Scene(root, "A6-restore"))
            {
                s.Start(c => StickyUiCommandResult.Handled());
                HashSet<string> gates = (HashSet<string>)Pc2Get(s.Pet, "_pendingHostedDockRestoreGroups");
                gates.Add("test-group");
                Type stateType = typeof(PetForm).GetNestedType("HostedDockRestorePreparation", BindingFlags.NonPublic);
                object state = Activator.CreateInstance(stateType, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { s.Notes, s.Notes[0], false, true, "test-group" }, null);
                long saves = s.Saves;
                Pc2Call(s.Pet, "FailHostedDockRestore", state, "test-rejection", StickyUiCommandResult.NotHandled());
                Pc2Assert(gates.Count == 0 && s.Hosted.NoteCount == 0 &&
                    s.Notes.TrueForAll(n => !n.Visible) && s.Repository.Count == 3 && s.Saves == saves + 1,
                    "A6 restore keeps data, hides canonical, removes membership, saves, releases gate");
            }
            evidence.Add("A6: rebase remains Rebasing; live failure remains Dragging; final rejection resets Idle/maps; restore hides/retains/saves data and releases gate.");
        }

        private static void RunPc2Recreation(string root, List<string> evidence)
        {
            using (Pc2Scene s = new Pc2Scene(root, "A2-recreation", true))
            {
                s.Start();
                StickyNoteData note = s.Notes[0];
                StickyUiCommand ensure = StickyUiCommand.EnsureSession(
                    StickyNoteUiSnapshot.FromData(note), null, s.Topology);
                StickyUiCommandResult ensured = s.Send(ensure);
                Pc2Assert(ensured.Status == StickyUiCommandStatus.Handled &&
                    ensured.SessionCreated, "first real EnsureSession reports created");
                StickyUiCommandResult reused = s.Send(ensure);
                Pc2Assert(reused.Status == StickyUiCommandStatus.Handled &&
                    !reused.SessionCreated, "second real EnsureSession reports reused");
                s.Hosted.SynchronizeSessionLease(note.Id, ensured.Sequence);
                Pc2Assert(s.Send(StickyUiCommand.Show(note.Id, false, s.Topology,
                    StickyPlacementRecovery.SelectForShow(note, s.Topology))).Status ==
                    StickyUiCommandStatus.Handled, "old native window show");
                StickyUiCommandResult old = null;
                for (int i = 0; i < 30; i++)
                    old = s.Send(StickyUiCommand.CaptureWindowFacts(note.Id, s.Topology));
                Pc2Assert(old.Facts != null, "old actual HWND facts");
                s.Hosted.RecordSequence(note.Id, old.Sequence);
                s.Placement.TryUpdateEffective(note.Id, old.Facts);
                StickyUiCommandResult closed = null;
                Pc2Call(s.Pet, "CloseHostedStickyRuntimeForReload",
                    (Action<StickyUiCommandResult>)(r => closed = r));
                s.Context.PumpUntil(() => closed != null);
                Pc2Assert(closed.Status == StickyUiCommandStatus.Handled &&
                    !s.Hosted.ContainsNote(note.Id) &&
                    s.Placement.GetEffective(note.Id) == null,
                    "actual reload close invalidates old Effective watermark");
                ensured = s.Send(ensure);
                Pc2Assert(ensured.Status == StickyUiCommandStatus.Handled &&
                    ensured.SessionCreated && ensured.Sequence < old.Sequence,
                    "recreated session reports created with lower sequence");
                s.Hosted.SynchronizeSessionLease(note.Id, ensured.Sequence);
                StickyUiCommandResult moved = s.Send(StickyUiCommand.Reproject(note.Id,
                    new StickyUiReprojectTarget(s.Surface.RuntimeGdiName, 180, 140, 300, 230, false, true),
                    s.Topology));
                Pc2Assert(moved.Status == StickyUiCommandStatus.Handled && moved.Facts != null &&
                    moved.Sequence < old.Sequence && s.Hosted.CanApplySequence(note.Id, moved.Sequence),
                    "new actual facts pass hosted lease");
                long saves = s.Saves;
                Pc2Assert((bool)Pc2Call(s.Pet, "ApplyReprojectResult", moved, note.Id, s.Topology),
                    "real new-session reproject accepted");
                s.Repository.WaitForPendingSaves();
                Pc2Assert(ReferenceEquals(s.Placement.GetEffective(note.Id), moved.Facts) &&
                    note.LocalLogicalX == 180 && note.LocalLogicalY == 140 &&
                    note.X == moved.Facts.PhysicalBounds.Left &&
                    note.Y == moved.Facts.PhysicalBounds.Top &&
                    s.Sequence(0) == moved.Sequence && s.Saves == saves + 1 &&
                    StickyNoteRepository.LoadFromFile(s.PathName).Find(note.Id).X == note.X,
                    "recreated HWND: canonical and Effective both equal new actual facts and save once");
                evidence.Add("A2 real EnsureSession/CloseAll/recreation/Reproject: old HWND sequence=" + old.Sequence +
                    "; recreated lease=" + ensured.Sequence + "; new HWND sequence=" + moved.Sequence +
                    "; canonical and Effective both moved to the new actual HWND rect.");
            }
        }

        private static void RunPc2EnsureSessionRuntime(string root, List<string> evidence)
        {
            using (Pc2Scene s = new Pc2Scene(root, "EnsureSessionRuntime", true))
            {
                s.Start();
                StickyNoteData note = s.Notes[0];
                StickyUiCommand ensure = StickyUiCommand.EnsureSession(
                    StickyNoteUiSnapshot.FromData(note), null, s.Topology);
                StickyUiCommandResult first = s.Send(ensure);
                StickyUiCommandResult second = s.Send(ensure);
                Pc2Assert(first.Status == StickyUiCommandStatus.Handled && first.SessionCreated &&
                    second.Status == StickyUiCommandStatus.Handled && !second.SessionCreated,
                    "EnsureSession first created, same-session reuse reported");
                StickyUiCommandResult closed = null;
                Pc2Call(s.Pet, "CloseHostedStickyRuntimeForReload",
                    (Action<StickyUiCommandResult>)(r => closed = r));
                s.Context.PumpUntil(() => closed != null);
                StickyUiCommandResult third = s.Send(ensure);
                Pc2Assert(closed.Status == StickyUiCommandStatus.Handled &&
                    third.Status == StickyUiCommandStatus.Handled && third.SessionCreated,
                    "EnsureSession after CloseAll reports created");
                evidence.Add("EnsureSession metadata: first created=true, reused=false, after CloseAll created=true.");
            }
        }

        private static string BuildPc2CharacterizationReportFields()
        {
            return "  \"pc2a_facts_barrier_rejection_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a_session_recreation_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a_live_batch_rejection_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a_reproject_rejection_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a_durable_commit_rejection_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a_failure_policies_characterized_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_session_created_metadata_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_session_effective_invalidation_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_dock_facts_barrier_atomic_reject_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_live_dock_batch_atomic_reject_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_reproject_atomic_reject_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_dock_commit_atomic_reject_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_dock_topology_atomic_reject_ok\": " + Bool(true) + ",\n" +
                "  \"pc2a5_real_hwnd_recreation_ok\": " + Bool(true) + ",\n";
        }

        public static void RunWeatherApiProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            WeatherLocation location;
            WeatherLocation.TryCreate("武汉", "湖北", "中国", 30.5928,
                114.3055, "Asia/Shanghai", out location);
            WeatherForecastWindow forecast = null;
            int requestCount = 0;
            string failure = null;
            try
            {
                using (PetWeatherSource source = new PetWeatherSource())
                {
                    forecast = source.GetForecastAsync(location)
                        .GetAwaiter().GetResult();
                    requestCount = source.ForecastRequestCountForTest;
                }
                if (forecast == null) failure = "Forecast unavailable.";
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }
            timer.Stop();
            WeatherMeaning? meaning = WeatherMeaningRules.Select(forecast);
            WeatherDailySelection selection = meaning.HasValue
                ? WeatherWordingCatalog.Select(meaning.Value,
                    DateTime.Now.Date, location.StableKey) : null;
            string json = "{\n" +
                "  \"ok\": " + Bool(failure == null &&
                    requestCount == 1) + ",\n" +
                "  \"endpoint\": \"" + JsonText(
                    OpenMeteoForecastClient.Endpoint) + "\",\n" +
                "  \"location\": \"" + JsonText(location.DisplayName) +
                    "\",\n" +
                "  \"timezone\": \"" + JsonText(location.Timezone) +
                    "\",\n" +
                "  \"hourly_variables\": [\"" + String.Join("\", \"",
                    OpenMeteoForecastClient.HourlyVariables) + "\"],\n" +
                "  \"forecast_request_count\": " + requestCount + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"yesterday\": " + WeatherDayJson(
                    forecast == null ? null : forecast.Yesterday) + ",\n" +
                "  \"today\": " + WeatherDayJson(
                    forecast == null ? null : forecast.Today) + ",\n" +
                "  \"tomorrow\": " + WeatherDayJson(
                    forecast == null ? null : forecast.Tomorrow) + ",\n" +
                "  \"meaning\": " + (meaning.HasValue ? "\"" +
                    meaning.Value + "\"" : "null") + ",\n" +
                "  \"wording\": " + (selection == null ? "null" :
                    "\"" + JsonText(selection.Text) + "\"") + ",\n" +
                "  \"failure\": " + (failure == null ? "null" :
                    "\"" + JsonText(failure) + "\"") + "\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        private static string WeatherDayJson(WeatherDaySummary day)
        {
            if (day == null) return "null";
            return "{\"date\":\"" + day.Date.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture) + "\"," +
                "\"temperature_min_c\":" + Number(day.MinimumTemperatureC) +
                ",\"temperature_max_c\":" + Number(day.MaximumTemperatureC) +
                ",\"apparent_min_c\":" +
                    Number(day.MinimumApparentTemperatureC) +
                ",\"apparent_max_c\":" +
                    Number(day.MaximumApparentTemperatureC) +
                ",\"precipitation_probability_max\":" +
                    Number(day.MaximumPrecipitationProbability) +
                ",\"precipitation_total_mm\":" +
                    Number(day.TotalPrecipitationMm) +
                ",\"snowfall_total_cm\":" + Number(day.TotalSnowfallCm) +
                ",\"wind_speed_max_kmh\":" +
                    Number(day.MaximumWindSpeedKmh) +
                ",\"wind_gust_max_kmh\":" +
                    Number(day.MaximumWindGustKmh) +
                ",\"likely_precipitation_hours\":" +
                    day.LikelyPrecipitationHours +
                ",\"has_snow_code\":" + Bool(day.HasSnowCode) + "}";
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string JsonText(string value)
        {
            return (value ?? String.Empty).Replace("\\", "\\\\")
                .Replace("\"", "\\\"").Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        public static void RunStickyInputProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            int caseCount = 0;
            int interactionCount = 0;
            bool singleWindowArchitecture = true;
            bool closeOk = true;
            bool editorInteractionOk = true;
            bool appearanceCloseStressOk = true;
            bool winFormsKeyboardInteropOk = true;
            string failure = null;
            try
            {
                int[] opacities = new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
                foreach (bool todoMode in new bool[] { false, true })
                {
                    foreach (int opacity in opacities)
                    {
                        StickyNoteData data = new StickyNoteData();
                        data.Title = todoMode ? "透明待办输入压力测试" :
                            "透明正文输入压力测试";
                        data.Text = "半透明便签输入回归测试 abcdefghijklmnopqrstuvwxyz";
                        data.IsTodoList = todoMode;
                        data.BackgroundOpacityPercent = opacity;
                        data.ColorArgb = StickyNoteWindow.PaletteColorForTest(
                            caseCount % 33).ToArgb();
                        data.TextColorArgb = caseCount % 2 == 0
                            ? Color.Black.ToArgb() : Color.White.ToArgb();
                        data.Width = 420;
                        data.Height = 320;
                        if (todoMode)
                            data.TodoItems.Add(new StickyTodoItem("现有待办项目", false));
                        using (StickyNoteWindow note = new StickyNoteWindow(data))
                        {
                            note.CreateControl();
                            singleWindowArchitecture &=
                                note.LegacyInputProxyHandleForTest == IntPtr.Zero &&
                                !note.UsesLegacyInputProxyForTest &&
                                note.UsesImeCompatibleEditor;
                            if (caseCount == 0)
                            {
                                note.EnableWinFormsKeyboardInterop();
                                winFormsKeyboardInteropOk &=
                                    note.UsesWinFormsKeyboardInteropForTest;
                            }
                            if (caseCount == 0)
                                appearanceCloseStressOk &=
                                    note.ExerciseAppearanceCloseStressForTest(20);
                            editorInteractionOk &= todoMode
                                ? note.ExerciseTodoWrapAndInlineEditForTest()
                                : note.ExerciseSmoothFormatInteractionForTest();
                            interactionCount += 5;
                            note.HideNote();
                            closeOk &= !note.Visible;
                        }
                        caseCount++;
                    }
                }
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }
            timer.Stop();
            bool ok = failure == null && singleWindowArchitecture && closeOk &&
                editorInteractionOk && appearanceCloseStressOk &&
                winFormsKeyboardInteropOk &&
                caseCount == 18 && interactionCount == 90;
            string escapedFailure = failure == null ? "" : failure.Replace(
                "\\", "\\\\").Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"single_window_input\": " +
                    Bool(singleWindowArchitecture) + ",\n" +
                "  \"close_ok\": " + Bool(closeOk) + ",\n" +
                "  \"editor_interaction_ok\": " +
                    Bool(editorInteractionOk) + ",\n" +
                "  \"appearance_x_close_stress_ok\": " +
                    Bool(appearanceCloseStressOk) + ",\n" +
                "  \"winforms_keyboard_interop_ok\": " +
                    Bool(winFormsKeyboardInteropOk) + ",\n" +
                "  \"normal_and_todo_cases\": " + caseCount + ",\n" +
                "  \"interaction_checks\": " + interactionCount + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        public static void RunStickyWinFormsPumpProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            bool visible = false;
            bool handleCreated = false;
            bool editorOk = false;
            bool dragPositionOk = false;
            bool hideShowOk = false;
            string failure = null;
            StickyNoteData data = new StickyNoteData();
            data.Title = "WinForms 消息循环集成测试";
            data.Text = "60% 单窗口编辑器";
            data.BackgroundOpacityPercent = 60;
            data.X = -2400;
            data.Y = -2400;
            data.Width = 360;
            data.Height = 260;
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            using (System.Windows.Forms.Timer probeTimer =
                new System.Windows.Forms.Timer())
            {
                probeTimer.Interval = 250;
                probeTimer.Tick += delegate
                {
                    probeTimer.Stop();
                    try
                    {
                        visible = note.Visible;
                        handleCreated = note.Handle != IntPtr.Zero &&
                            note.LegacyInputProxyHandleForTest == IntPtr.Zero;
                        editorOk = note.ExerciseSmoothFormatInteractionForTest();
                        Point moved = new Point(note.Left + 17, note.Top + 13);
                        note.Location = moved;
                        dragPositionOk = note.Location == moved;
                        note.HideNote();
                        bool hidden = !note.Visible;
                        note.ShowRestored();
                        hideShowOk = hidden && note.Visible;
                    }
                    catch (Exception error)
                    {
                        failure = error.GetType().Name + ": " + error.Message;
                    }
                    finally
                    {
                        note.CloseForApplicationExit();
                        Application.ExitThread();
                    }
                };
                note.ShowRestored();
                probeTimer.Start();
                Application.Run();
            }
            timer.Stop();
            bool ok = failure == null && visible && handleCreated && editorOk &&
                dragPositionOk && hideShowOk;
            string escapedFailure = failure == null ? "" : failure.Replace(
                "\\", "\\\\").Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"winforms_message_pump_visible\": " + Bool(visible) + ",\n" +
                "  \"single_wpf_window_handle\": " + Bool(handleCreated) + ",\n" +
                "  \"editor_dispatch_ok\": " + Bool(editorOk) + ",\n" +
                "  \"position_update_ok\": " + Bool(dragPositionOk) + ",\n" +
                "  \"hide_show_ok\": " + Bool(hideShowOk) + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        public static void RunStickyTransparencyOverlapProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string fullOutputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(fullOutputPath);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string screenshotPath = Path.ChangeExtension(fullOutputPath, ".png");

            int requestedOpacity = 60;
            int rawBodyAlpha = -1;
            int maximumRenderedAlpha = -1;
            bool transparentWindowMode = false;
            bool perPixelAlphaOk = false;
            bool opaqueTextOk = false;
            bool overlapCompositionOk = false;
            Color noteOneActual = Color.Empty;
            Color noteTwoActual = Color.Empty;
            Color overlapActual = Color.Empty;
            Color noteOneExpected = Color.Empty;
            Color noteTwoExpected = Color.Empty;
            Color overlapExpected = Color.Empty;
            int noteOneDistance = Int32.MaxValue;
            int noteTwoDistance = Int32.MaxValue;
            int overlapDistance = Int32.MaxValue;
            string failure = null;

            Color stageColor = Color.FromArgb(42, 55, 70);
            Color noteOneColor = Color.FromArgb(255, 96, 96);
            Color noteTwoColor = Color.FromArgb(80, 160, 255);

            try
            {
                StickyNoteData alphaData = CreateTransparencyProbeNote(
                    "Alpha 原始层检查", "文字必须保持完全不透明", noteOneColor,
                    requestedOpacity, 420, 320);
                using (StickyNoteWindow alphaNote = new StickyNoteWindow(alphaData))
                {
                    transparentWindowMode = alphaNote.AllowsTransparency &&
                        alphaNote.Background == System.Windows.Media.Brushes.Transparent;
                    rawBodyAlpha = alphaNote.BackgroundAlphaForTest;
                    maximumRenderedAlpha = alphaNote.TextAlphaForTest;
                }
                perPixelAlphaOk = rawBodyAlpha >= 150 && rawBodyAlpha <= 156;
                opaqueTextOk = maximumRenderedAlpha == 255;

                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                int stageWidth = Math.Min(760, Math.Max(720, work.Width - 120));
                int stageHeight = Math.Min(540, Math.Max(520, work.Height - 120));
                Rectangle stageBounds = new Rectangle(
                    work.Left + Math.Max(20, (work.Width - stageWidth) / 2),
                    work.Top + Math.Max(20, (work.Height - stageHeight) / 2),
                    stageWidth, stageHeight);

                StickyNoteData noteOneData = CreateTransparencyProbeNote(
                    "透明便签 A", String.Empty, noteOneColor, requestedOpacity,
                    420, 320);
                StickyNoteData noteTwoData = CreateTransparencyProbeNote(
                    "透明便签 B", String.Empty, noteTwoColor, requestedOpacity,
                    420, 320);
                using (Form stage = new Form())
                using (StickyNoteWindow noteOne = new StickyNoteWindow(noteOneData))
                using (StickyNoteWindow noteTwo = new StickyNoteWindow(noteTwoData))
                using (Bitmap screenshot = new Bitmap(stageBounds.Width,
                    stageBounds.Height, PixelFormat.Format32bppArgb))
                {
                    stage.Text = "Penny 双透明便签合成验收背景";
                    stage.FormBorderStyle = FormBorderStyle.None;
                    stage.StartPosition = FormStartPosition.Manual;
                    stage.ShowInTaskbar = false;
                    stage.TopMost = true;
                    stage.BackColor = stageColor;
                    stage.Bounds = stageBounds;

                    noteOne.Location = new Point(stage.Left + 40, stage.Top + 80);
                    noteTwo.Location = new Point(stage.Left + 260, stage.Top + 180);
                    noteOne.TopMost = true;
                    noteTwo.TopMost = true;
                    stage.Show();
                    noteOne.Show();
                    noteTwo.Show();
                    noteTwo.BringToFront();
                    PumpUi(700);

                    using (Graphics capture = Graphics.FromImage(screenshot))
                        capture.CopyFromScreen(stage.Left, stage.Top, 0, 0,
                            stage.Size, CopyPixelOperation.SourceCopy);
                    screenshot.Save(screenshotPath, ImageFormat.Png);

                    noteOneActual = screenshot.GetPixel(120, 300);
                    noteTwoActual = screenshot.GetPixel(600, 350);
                    overlapActual = screenshot.GetPixel(360, 300);

                    noteOneExpected = BlendForExpected(noteOneColor, stageColor,
                        requestedOpacity);
                    noteTwoExpected = BlendForExpected(noteTwoColor, stageColor,
                        requestedOpacity);
                    overlapExpected = BlendForExpected(noteTwoColor,
                        noteOneExpected, requestedOpacity);
                    noteOneDistance = ColorDistance(noteOneActual, noteOneExpected);
                    noteTwoDistance = ColorDistance(noteTwoActual, noteTwoExpected);
                    overlapDistance = ColorDistance(overlapActual, overlapExpected);
                    overlapCompositionOk = noteOneDistance <= 12 &&
                        noteTwoDistance <= 12 && overlapDistance <= 16;

                    noteTwo.CloseForApplicationExit();
                    noteOne.CloseForApplicationExit();
                    stage.Close();
                    PumpUi(50);
                }
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }

            timer.Stop();
            bool ok = failure == null && transparentWindowMode && perPixelAlphaOk &&
                opaqueTextOk && overlapCompositionOk;
            string escapedFailure = failure == null ? String.Empty :
                failure.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string escapedScreenshot = screenshotPath.Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"true_transparent_wpf_window\": " +
                    Bool(transparentWindowMode) + ",\n" +
                "  \"requested_background_opacity_percent\": " +
                    requestedOpacity + ",\n" +
                "  \"raw_body_alpha\": " + rawBodyAlpha + ",\n" +
                "  \"per_pixel_alpha_ok\": " + Bool(perPixelAlphaOk) + ",\n" +
                "  \"maximum_rendered_alpha\": " + maximumRenderedAlpha + ",\n" +
                "  \"opaque_text_ok\": " + Bool(opaqueTextOk) + ",\n" +
                "  \"two_window_overlap_composition_ok\": " +
                    Bool(overlapCompositionOk) + ",\n" +
                "  \"note_one_only_actual\": \"" + ColorText(noteOneActual) + "\",\n" +
                "  \"note_one_only_expected\": \"" + ColorText(noteOneExpected) + "\",\n" +
                "  \"note_one_color_distance\": " + noteOneDistance + ",\n" +
                "  \"note_two_only_actual\": \"" + ColorText(noteTwoActual) + "\",\n" +
                "  \"note_two_only_expected\": \"" + ColorText(noteTwoExpected) + "\",\n" +
                "  \"note_two_color_distance\": " + noteTwoDistance + ",\n" +
                "  \"overlap_actual\": \"" + ColorText(overlapActual) + "\",\n" +
                "  \"overlap_expected\": \"" + ColorText(overlapExpected) + "\",\n" +
                "  \"overlap_color_distance\": " + overlapDistance + ",\n" +
                "  \"overlap_screenshot\": \"" + escapedScreenshot + "\",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            File.WriteAllText(fullOutputPath, json, new UTF8Encoding(false));
        }

        private static StickyNoteData CreateTransparencyProbeNote(string title,
            string text, Color color, int opacity, int width, int height)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = title;
            data.Text = text;
            data.ColorArgb = color.ToArgb();
            data.TextColorArgb = Color.Black.ToArgb();
            data.BackgroundOpacityPercent = opacity;
            data.Width = width;
            data.Height = height;
            data.AlwaysOnTop = true;
            return data;
        }

        private static Color BlendForExpected(Color foreground, Color background,
            int opacityPercent)
        {
            int alpha = (int)Math.Round(Math.Max(0, Math.Min(100,
                opacityPercent)) * 2.55);
            int inverse = 255 - alpha;
            return Color.FromArgb(255,
                (foreground.R * alpha + background.R * inverse + 127) / 255,
                (foreground.G * alpha + background.G * inverse + 127) / 255,
                (foreground.B * alpha + background.B * inverse + 127) / 255);
        }

        private static int ColorDistance(Color actual, Color expected)
        {
            return Math.Abs(actual.R - expected.R) +
                Math.Abs(actual.G - expected.G) +
                Math.Abs(actual.B - expected.B);
        }

        private static string ColorText(Color value)
        {
            return value.IsEmpty ? "unavailable" : String.Format("#{0:X2}{1:X2}{2:X2}",
                value.R, value.G, value.B);
        }

        private static void PumpUi(int milliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(1);
            }
            while (timer.ElapsedMilliseconds < milliseconds);
            Application.DoEvents();
        }

        public static void RenderStickyPreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "明天下午改卷子！";
            data.Text = "明天下午改卷子！\r\n\r\n这行文字用于检查字体、字号和样式。";
            data.Width = 480;
            data.Height = 400;
            using (RichTextBox source = new RichTextBox())
            using (Font body = new Font("Microsoft YaHei UI", 14F))
            {
                source.Text = data.Text;
                source.SelectAll();
                source.SelectionFont = body;
                source.Select(0, 8);
                using (Font heading = new Font("Microsoft YaHei UI", 18F,
                    FontStyle.Bold | FontStyle.Underline))
                    source.SelectionFont = heading;
                data.RichTextRtf = source.Rtf;
            }
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                note.StartPosition = FormStartPosition.Manual;
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                note.Location = new Point(work.Left + 24, work.Top + 24);
                note.TopMost = true;
                note.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();
                using (Bitmap canvas = new Bitmap(note.Width + 40,
                    note.Height + 40, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(canvas))
                using (Bitmap noteBitmap = new Bitmap(note.Width, note.Height,
                    PixelFormat.Format32bppArgb))
                {
                    try
                    {
                        using (Graphics screenCapture = Graphics.FromImage(noteBitmap))
                            screenCapture.CopyFromScreen(note.Left, note.Top, 0, 0,
                                note.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        note.DrawToBitmap(noteBitmap,
                            new Rectangle(Point.Empty, note.Size));
                    }
                    graphics.Clear(Color.FromArgb(235, 238, 244));
                    graphics.DrawImageUnscaled(noteBitmap, 20, 20);
                    string parent = Path.GetDirectoryName(
                        Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }
                note.Hide();
            }
        }

        public static void RenderSchedulePreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "日程";
            data.IsSchedule = true;
            data.IsTodoList = false;
            data.FontSizeTwips = 320;
            data.Width = 390;
            data.Height = 430;
            data.ScheduleItems.Add(new StickyScheduleItem("参加画展",
                DateTime.Today.AddDays(6), true));
            data.ScheduleItems.Add(new StickyScheduleItem("五一放假",
                DateTime.Today.AddDays(22)));
            data.ScheduleItems.Add(new StickyScheduleItem("朋友生日",
                DateTime.Today.AddDays(58)));
            data.ScheduleItems.Add(new StickyScheduleItem("国庆节",
                DateTime.Today.AddDays(175)));
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                note.StartPosition = FormStartPosition.Manual;
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                note.Location = new Point(work.Left + 24, work.Top + 24);
                note.TopMost = true;
                note.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();
                using (Bitmap canvas = new Bitmap(note.Width + 40,
                    note.Height + 40, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(canvas))
                using (Bitmap noteBitmap = new Bitmap(note.Width, note.Height,
                    PixelFormat.Format32bppArgb))
                {
                    try
                    {
                        using (Graphics screenCapture = Graphics.FromImage(noteBitmap))
                            screenCapture.CopyFromScreen(note.Left, note.Top, 0, 0,
                                note.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        note.DrawToBitmap(noteBitmap,
                            new Rectangle(Point.Empty, note.Size));
                    }
                    graphics.Clear(Color.FromArgb(235, 238, 244));
                    graphics.DrawImageUnscaled(noteBitmap, 20, 20);
                    string parent = Path.GetDirectoryName(
                        Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }
                note.Hide();
            }
        }

        public static void RenderStickyAppearancePreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "颜色与透明度预览";
            data.Text = "这段文字始终保持完全不透明。\r\n可以点击正文继续输入。";
            data.Width = 420;
            data.Height = 300;
            data.ColorArgb = StickyNoteWindow.PaletteColorForTest(24).ToArgb();
            data.BackgroundOpacityPercent = 60;
            data.TextColorArgb = Color.Black.ToArgb();
            data.FontFamilyName = "Noto Sans SC DemiLight";
            data.FontSizeTwips = 240;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Rectangle stageBounds = new Rectangle(work.Left + 40, work.Top + 40,
                Math.Min(1160, work.Width - 80), Math.Min(520, work.Height - 80));
            using (Form stage = new Form())
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                stage.Text = "Penny 便签外观开发预览背景";
                stage.FormBorderStyle = FormBorderStyle.None;
                stage.StartPosition = FormStartPosition.Manual;
                stage.ShowInTaskbar = false;
                stage.TopMost = true;
                stage.BackColor = Color.FromArgb(245, 245, 240);
                stage.Bounds = stageBounds;
                stage.Show();

                note.StartPosition = FormStartPosition.Manual;
                note.Location = new Point(stage.Left + 28, stage.Top + 95);
                note.TopMost = true;
                note.Show();
                note.OpenAppearanceDialogForTest();
                Application.DoEvents();
                System.Threading.Thread.Sleep(650);
                Application.DoEvents();

                Form appearance = null;
                foreach (Form open in Application.OpenForms)
                {
                    if (open is StickyAppearanceDialog) appearance = open;
                }

                using (Bitmap canvas = new Bitmap(stage.Width, stage.Height,
                    PixelFormat.Format32bppArgb))
                using (Graphics capture = Graphics.FromImage(canvas))
                {
                    try
                    {
                        capture.CopyFromScreen(stage.Left, stage.Top, 0, 0,
                            stage.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        capture.Clear(stage.BackColor);
                        using (Bitmap noteBitmap = new Bitmap(note.Width,
                            note.Height, PixelFormat.Format32bppArgb))
                        {
                            note.DrawToBitmap(noteBitmap,
                                new Rectangle(Point.Empty, note.Size));
                            capture.DrawImageUnscaled(noteBitmap,
                                note.Left - stage.Left, note.Top - stage.Top);
                        }
                        if (appearance != null)
                        {
                            using (Bitmap dialogBitmap = new Bitmap(
                                appearance.Width, appearance.Height,
                                PixelFormat.Format32bppArgb))
                            {
                                appearance.DrawToBitmap(dialogBitmap,
                                    new Rectangle(Point.Empty,
                                        appearance.Size));
                                capture.DrawImageUnscaled(dialogBitmap,
                                    appearance.Left - stage.Left,
                                    appearance.Top - stage.Top);
                            }
                        }
                    }
                    string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }

                if (appearance != null) appearance.Close();
                note.Hide();
                stage.Hide();
            }
        }

        public static void RenderHoverBubblePreview(string outputPath)
        {
            using (SpeechBubbleForm empty = new SpeechBubbleForm("今天想要做些什么呢？", 0))
            using (SpeechBubbleForm countdown = new SpeechBubbleForm(
                "距离最近提醒还有1小时20分钟。\n当前共有 3 条提醒。", 0))
            using (Bitmap preview = new Bitmap(empty.Width + countdown.Width +
                30, Math.Max(empty.Height, countdown.Height) + 20,
                PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(preview))
            using (Bitmap emptyBitmap = new Bitmap(empty.Width, empty.Height,
                PixelFormat.Format32bppArgb))
            using (Bitmap countdownBitmap = new Bitmap(countdown.Width, countdown.Height,
                PixelFormat.Format32bppArgb))
            {
                empty.CreateControl();
                countdown.CreateControl();
                empty.DrawToBitmap(emptyBitmap, empty.ClientRectangle);
                countdown.DrawToBitmap(countdownBitmap, countdown.ClientRectangle);
                emptyBitmap.MakeTransparent(empty.TransparencyKey);
                countdownBitmap.MakeTransparent(countdown.TransparencyKey);
                graphics.Clear(Color.FromArgb(225, 229, 236));
                graphics.DrawImageUnscaled(emptyBitmap, 5, 10);
                graphics.DrawImageUnscaled(countdownBitmap,
                    empty.Width + 20, 10);
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                preview.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderReminderPreview(string outputPath)
        {
            using (ReminderDialog dialog = new ReminderDialog(
                "下午三点提交修改后的方案", 18F, true))
            using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height,
                PixelFormat.Format32bppArgb))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-2400, -2400);
                dialog.Show();
                Application.DoEvents();
                dialog.DrawToBitmap(bitmap,
                    new Rectangle(Point.Empty, dialog.Size));
                dialog.Hide();
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderContactAuthorPreview(string outputPath)
        {
            using (ContactAuthorForm dialog = new ContactAuthorForm())
            using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height,
                PixelFormat.Format32bppArgb))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-2400, -2400);
                dialog.Show();
                Application.DoEvents();
                dialog.DrawToBitmap(bitmap,
                    new Rectangle(Point.Empty, dialog.Size));
                dialog.Hide();
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderPreview(string outputPath)
        {
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            using (Bitmap preview = new Bitmap(960, 208, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                graphics.Clear(Color.FromArgb(225, 229, 236));
                graphics.DrawImageUnscaled(art.GetFrame(0, 0), 0, 0);
                graphics.DrawImageUnscaled(art.GetFrame(8, 0), 192, 0);
                graphics.DrawImageUnscaled(art.GetFrame(6, 4), 384, 0);
                graphics.DrawImageUnscaled(art.GetFrame(7, 0), 576, 0);
                graphics.DrawImageUnscaled(art.GetFrame(4, 0), 768, 0);
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                preview.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RunStartupProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            int loadedStates;
            int materializedGifFiles;
            int width;
            int height;
            bool startupCacheUsed;
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            {
                Bitmap firstFrame = art.GetFrame(0, 0);
                width = firstFrame.Width;
                height = firstFrame.Height;
                loadedStates = art.LoadedRuntimeStateCount;
                startupCacheUsed = art.LoadedStartupCache;
                materializedGifFiles = Directory.Exists(art.ArtRoot)
                    ? Directory.GetFiles(art.ArtRoot, "*.gif",
                        SearchOption.AllDirectories).Length : 0;
            }
            timer.Stop();
            string fullOutputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(fullOutputPath);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string json = "{\n" +
                "  \"ok\": " + Bool(width == 192 && height == 208 &&
                    loadedStates == 1) + ",\n" +
                "  \"elapsed_milliseconds\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"loaded_runtime_states\": " + loadedStates + ",\n" +
                "  \"startup_cache_used\": " + Bool(startupCacheUsed) + ",\n" +
                "  \"materialized_gif_files\": " + materializedGifFiles + "\n" +
                "}\n";
            File.WriteAllText(fullOutputPath, json, new UTF8Encoding(false));
        }

        public static void RenderFeaturePreview(string outputPath)
        {
            StickyNoteData yellowData = new StickyNoteData();
            yellowData.Title = "今日计划";
            yellowData.Text = "支持中文输入：整理方案、记录灵感。";
            yellowData.X = 0;
            yellowData.Y = 0;
            yellowData.Width = 320;
            yellowData.Height = 300;
            yellowData.ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks;
            StickyNoteData blueData = new StickyNoteData();
            blueData.Title = "本周待办";
            blueData.IsTodoList = true;
            blueData.TodoItems.Add(new StickyTodoItem("完成便利贴优化", true));
            blueData.TodoItems.Add(new StickyTodoItem("检查提醒倒计时", false));
            blueData.TodoItems.Add(new StickyTodoItem("整理下周计划", false));
            blueData.ColorArgb = Color.FromArgb(255, 211, 239, 255).ToArgb();
            blueData.X = 0;
            blueData.Y = 0;
            blueData.Width = 320;
            blueData.Height = 300;

            List<ReminderItem> previewReminders = new List<ReminderItem>();
            previewReminders.Add(new ReminderItem(DateTime.UtcNow.AddMinutes(18),
                "提交今日方案"));
            previewReminders.Add(new ReminderItem(DateTime.UtcNow.AddHours(2),
                "休息并喝水"));

            using (Bitmap canvas = new Bitmap(1080, 760, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (StickyNoteWindow yellow = new StickyNoteWindow(yellowData))
            using (StickyNoteWindow blue = new StickyNoteWindow(blueData))
            using (ScaleDialog scale = new ScaleDialog(100, 100))
            using (Bitmap yellowBitmap = new Bitmap(320, 300, PixelFormat.Format32bppArgb))
            using (Bitmap blueBitmap = new Bitmap(320, 300, PixelFormat.Format32bppArgb))
            using (Bitmap scaleBitmap = new Bitmap(scale.Width, scale.Height,
                PixelFormat.Format32bppArgb))
            using (Bitmap blackKeys = KeyboardOverlayForm.RenderTextPreview(
                "CTRL+W", Color.Black, 255, 60))
            using (Bitmap whiteKeys = KeyboardOverlayForm.RenderTextPreview(
                "W*3", Color.White, 255, 150))
            using (Font heading = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold))
            using (SolidBrush headingBrush = new SolidBrush(Color.FromArgb(45, 51, 60)))
            using (SolidBrush darkBackground = new SolidBrush(Color.FromArgb(35, 39, 48)))
            {
                graphics.Clear(Color.FromArgb(235, 238, 244));
                yellow.UpdateReminderBanner(previewReminders);
                blue.UpdateReminderBanner(previewReminders);
                yellow.StartPosition = FormStartPosition.Manual;
                blue.StartPosition = FormStartPosition.Manual;
                scale.StartPosition = FormStartPosition.Manual;
                yellow.Location = new Point(-2400, -2400);
                blue.Location = new Point(-2400, -2400);
                scale.Location = new Point(-2400, -2400);
                yellow.Show();
                blue.Show();
                scale.Show();
                Application.DoEvents();
                yellow.DrawToBitmap(yellowBitmap, new Rectangle(0, 0, 320, 300));
                blue.DrawToBitmap(blueBitmap, new Rectangle(0, 0, 320, 300));
                scale.DrawToBitmap(scaleBitmap, new Rectangle(Point.Empty, scale.Size));
                yellow.Hide();
                blue.Hide();
                scale.Hide();
                graphics.DrawString("便利贴顶部固定提醒 / 正文与待办清单",
                    heading, headingBrush, new PointF(24, 18));
                graphics.DrawImageUnscaled(yellowBitmap, 24, 52);
                graphics.DrawImageUnscaled(blueBitmap, 366, 52);
                graphics.DrawString("按键显示：小 60% / 大 150%",
                    heading, headingBrush, new PointF(724, 18));
                graphics.FillRectangle(Brushes.White, 724, 52, 330, 110);
                graphics.DrawImageUnscaled(blackKeys,
                    889 - blackKeys.Width / 2, 84);
                graphics.FillRectangle(darkBackground, 724, 174, 330, 110);
                graphics.DrawImageUnscaled(whiteKeys,
                    889 - whiteKeys.Width / 2, 203);
                graphics.DrawString("桌宠缩放与按键文字大小",
                    heading, headingBrush, new PointF(24, 386));
                graphics.DrawImage(scaleBitmap, new Rectangle(24, 420, 500, 290),
                    new Rectangle(0, 0, scaleBitmap.Width, scaleBitmap.Height),
                    GraphicsUnit.Pixel);
                graphics.DrawString("左右侧页签：长按拖拽排序 / 右键删除",
                    heading, headingBrush, new PointF(570, 386));
                List<StickyNoteData> tabNotes = new List<StickyNoteData>();
                string[] tabTitles = { "待办清单", "日程", "便利贴", "待办",
                    "日程", "灵感", "购物清单", "日程", "阅读记录" };
                Color[] tabColors = { Color.FromArgb(255, 239, 156),
                    Color.FromArgb(214, 246, 215), Color.FromArgb(211, 239, 255),
                    Color.FromArgb(230, 226, 239), Color.FromArgb(255, 221, 181),
                    Color.FromArgb(244, 221, 222), Color.FromArgb(255, 239, 156),
                    Color.FromArgb(211, 239, 255), Color.FromArgb(214, 246, 215) };
                for (int i = 0; i < tabTitles.Length; i++)
                {
                    StickyNoteData tabNote = new StickyNoteData();
                    tabNote.Title = tabTitles[i];
                    tabNote.ColorArgb = tabColors[i].ToArgb();
                    tabNote.Visible = false;
                    tabNote.IsTodoList = i % 3 == 0;
                    tabNote.IsSchedule = i % 3 == 1;
                    tabNotes.Add(tabNote);
                }
                int previewLeftCount = StickyNoteTabsForm.CalculateLeftCount(
                    tabNotes.Count);
                List<SideTabSnapshot> tabSnapshots = new List<SideTabSnapshot>();
                foreach (StickyNoteData tabNote in tabNotes)
                    tabSnapshots.Add(SideTabSnapshot.FromData(tabNote));
                List<SideTabSnapshot> previewLeft = tabSnapshots.GetRange(0,
                    previewLeftCount);
                List<SideTabSnapshot> previewRight = tabSnapshots.GetRange(
                    previewLeftCount, tabSnapshots.Count - previewLeftCount);
                using (StickyNoteTabsForm leftTabs = new StickyNoteTabsForm(
                    StickyTabSide.Left, delegate(string noteId) { }))
                using (StickyNoteTabsForm rightTabs = new StickyNoteTabsForm(
                    StickyTabSide.Right, delegate(string noteId) { }))
                using (PetArtPackage petArt = PetArtPackage.Load(192, 208))
                {
                    Bitmap petFrame = petArt.GetFrame(0, 0);
                    leftTabs.Location = new Point(-2600, -2600);
                    rightTabs.Location = new Point(-2600, -2600);
                    leftTabs.SetNotes(previewLeft);
                    rightTabs.SetNotes(previewRight);
                    StickyNoteData crossSideSource = previewLeft.Count >= 3 &&
                        previewRight.Count >= 2 ? tabNotes[1] : null;
                    if (crossSideSource != null)
                    {
                        StickyNoteTabsForm.BeginDragSession(crossSideSource.Id);
                        rightTabs.ShowDropPreviewForTest(crossSideSource.Id, 2);
                    }
                    Application.DoEvents();
                    using (Bitmap leftTabsBitmap = new Bitmap(leftTabs.Width,
                        leftTabs.Height, PixelFormat.Format32bppArgb))
                    using (Bitmap rightTabsBitmap = new Bitmap(rightTabs.Width,
                        rightTabs.Height, PixelFormat.Format32bppArgb))
                    {
                        leftTabs.DrawToBitmap(leftTabsBitmap,
                            new Rectangle(Point.Empty, leftTabs.Size));
                        rightTabs.DrawToBitmap(rightTabsBitmap,
                            new Rectangle(Point.Empty, rightTabs.Size));
                        leftTabsBitmap.MakeTransparent(Color.Fuchsia);
                        rightTabsBitmap.MakeTransparent(Color.Fuchsia);
                        int petX = 760;
                        int petY = 454;
                    graphics.DrawImageUnscaled(leftTabsBitmap,
                            petX - leftTabsBitmap.Width - StickyNoteTabsForm.PetGap,
                            petY + (petFrame.Height - leftTabsBitmap.Height) / 2);
                        graphics.DrawImageUnscaled(petFrame, petX, petY);
                        graphics.DrawImageUnscaled(rightTabsBitmap,
                            petX + petFrame.Width + StickyNoteTabsForm.PetGap,
                            petY + (petFrame.Height - rightTabsBitmap.Height) / 2);
                    }
                    if (crossSideSource != null)
                        StickyNoteTabsForm.EndDragSession(crossSideSource.Id);
                    leftTabs.Hide();
                    rightTabs.Hide();
                }
                using (Font noteFont = new Font("Microsoft YaHei UI", 9.5F))
                {
                    graphics.DrawString(
                        "提醒微调：↑ / 滚轮向上 = 往前，↓ / 滚轮向下 = 往后",
                        noteFont, headingBrush, new PointF(24, 724));
                    graphics.DrawString(
                        "拖拽排序：上下页签会让开并显示蓝色插入槽",
                        noteFont, headingBrush, new PointF(570, 690));
                }
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                canvas.Save(outputPath, ImageFormat.Png);
            }
        }


        [ThreadStatic]
        private static List<bool> _reportedChecks;

        private static void BeginCheckCollection()
        {
            _reportedChecks = new List<bool>();
        }

        private static bool EndCheckCollection()
        {
            List<bool> checks = _reportedChecks;
            _reportedChecks = null;
            if (checks == null || checks.Count == 0) return false;
            foreach (bool passed in checks)
                if (!passed) return false;
            return true;
        }

        private static void CancelCheckCollection()
        {
            _reportedChecks = null;
        }

        private sealed class SolarTermProbeCase
        {
            internal readonly DateTimeOffset LocalDate;
            internal readonly SolarTerm ExpectedTerm;
            internal readonly string ExpectedName;
            internal readonly int ExpectedLongitude;

            internal SolarTermProbeCase(int year, int month, int day,
                TimeSpan offset, SolarTerm expectedTerm,
                string expectedName, int expectedLongitude)
            {
                LocalDate = new DateTimeOffset(year, month, day, 12, 0, 0,
                    offset);
                ExpectedTerm = expectedTerm;
                ExpectedName = expectedName;
                ExpectedLongitude = expectedLongitude;
            }
        }

        public static void RunDailyBriefingProbe(string outputPath)
        {
            DateTimeOffset localDate = new DateTimeOffset(2026, 9, 3,
                12, 0, 0,
                TimeSpan.FromHours(8));
            const ZodiacSign sign = ZodiacSign.Scorpio;
            DayPart dayPart = DailyContentRules.ResolveDayPart(localDate);
            SolarTermInfo? solar = SolarTermCalculator.FindForLocalDate(
                localDate);
            DailyLineEntry curated = CuratedDailyLineSelector.Select(
                localDate);
            DailyLineEntry zodiac = ZodiacDailySelector.Select(sign,
                localDate);
            AlmanacDayInfo almanacDay = AlmanacCalculator.Calculate(localDate);
            AlmanacDailySelection almanac = almanacDay == null ? null :
                AlmanacDailySelector.Select(almanacDay, localDate);
            DailyBriefingContent content = new DailyBriefingContent(solar,
                null, almanac, curated, zodiac);
            DailyBriefingSentence[] selected =
                DailyBriefingComposer.SelectSupplementary(content);
            string finalText = DailyBriefingComposer.Compose(dayPart,
                localDate.Date, content);
            StringBuilder selectedJson = new StringBuilder();
            for (int i = 0; i < selected.Length; i++)
            {
                if (i > 0) selectedJson.Append(", ");
                selectedJson.Append(JsonString(selected[i].Body));
            }
            bool deterministic = curated.Id == CuratedDailyLineSelector
                .Select(localDate).Id && ((zodiac == null &&
                    ZodiacDailySelector.Select(sign, localDate) == null) ||
                    (zodiac != null && zodiac.Id == ZodiacDailySelector
                        .Select(sign, localDate).Id)) &&
                ((almanac == null && (almanacDay == null ||
                    AlmanacDailySelector.Select(almanacDay, localDate) ==
                        null)) || (almanac != null && almanac.VariantId ==
                    AlmanacDailySelector.Select(almanacDay,
                        localDate).VariantId));
            bool ok = deterministic && curated != null && almanacDay != null &&
                selected.Length <= 2;
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"deterministic\": " + Bool(deterministic) + ",\n" +
                "  \"date\": \"2026-09-03\",\n" +
                "  \"dayPart\": " + JsonString(dayPart.ToString()) +
                    ",\n" +
                "  \"solarCandidate\": " + JsonString(solar.HasValue
                    ? solar.Value.ChineseName : null) + ",\n" +
                "  \"almanacCandidate\": " +
                    AlmanacSelectionJson(almanac) + ",\n" +
                "  \"curatedId\": " + JsonString(curated.Id) + ",\n" +
                "  \"curatedText\": " + JsonString(curated.Text) + ",\n" +
                "  \"zodiacEligible\": " + Bool(zodiac != null) + ",\n" +
                "  \"zodiacId\": " + JsonString(zodiac == null ? null :
                    zodiac.Id) + ",\n" +
                "  \"zodiacText\": " + JsonString(zodiac == null ? null :
                    zodiac.Text) + ",\n" +
                "  \"selectedSupplementary\": [" + selectedJson +
                    "],\n" +
                "  \"finalText\": " + JsonString(finalText) + "\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        private static string JsonString(string value)
        {
            if (value == null) return "null";
            return "\"" + value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"").Replace("\r", "\\r")
                .Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }

        private static string AlmanacSelectionJson(
            AlmanacDailySelection selection)
        {
            if (selection == null) return "null";
            return "{ \"topic\": " + JsonString(selection.Topic.ToString()) +
                ", \"sourceTerm\": " + JsonString(selection.SourceTerm) +
                ", \"polarity\": " + JsonString(selection.IsYi ? "Yi" :
                    "Ji") +
                ", \"variantId\": " + JsonString(selection.VariantId) +
                ", \"framingId\": " + JsonString(selection.FramingId) +
                ", \"wordingId\": " + JsonString(selection.WordingId) +
                ", \"text\": " + JsonString(selection.Text) + " }";
        }

        public static void RunAlmanacProbe(string outputPath)
        {
            DateTimeOffset sampleDate = new DateTimeOffset(2026, 9, 3,
                12, 0, 0, TimeSpan.FromHours(8));
            AlmanacDayInfo sample = AlmanacCalculator.Calculate(sampleDate);
            AlmanacDailySelection sampleSelection = sample == null ? null :
                AlmanacDailySelector.Select(sample, sampleDate);
            string[] recognized;
            string[] suppressed;
            AlmanacDailySelector.DescribeTopics(sample, out recognized,
                out suppressed);

            Dictionary<string, int> unmapped =
                new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> prefixes =
                new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<AlmanacTopic, HashSet<string>> variants =
                new Dictionary<AlmanacTopic, HashSet<string>>();
            List<AlmanacCoverageProbeYear> years =
                new List<AlmanacCoverageProbeYear>();
            Dictionary<string, string> recommended =
                new Dictionary<string, string>(StringComparer.Ordinal);
            bool calculatorOk = sample != null;
            int selectedTextCount = 0;
            int legacyAlmanacTermCount = 0;
            int startsWithToday = 0;
            int yiJiTermCount = 0;
            int traditionalCalendarTermCount = 0;
            int folkTermCount = 0;
            int lifeFirstCount = 0;
            int sourceLateCount = 0;
            for (int year = 2026; year <= 2028; year++)
            {
                AlmanacCoverageProbeYear stats =
                    new AlmanacCoverageProbeYear(year);
                DateTimeOffset date = new DateTimeOffset(year, 1, 1,
                    12, 0, 0, TimeSpan.FromHours(8));
                DateTimeOffset end = date.AddYears(1);
                while (date < end)
                {
                    stats.Days++;
                    AlmanacDayInfo day = AlmanacCalculator.Calculate(date);
                    calculatorOk &= day != null;
                    AlmanacDailySelection selection = day == null ? null :
                        AlmanacDailySelector.Select(day, date);
                    if (selection == null)
                    {
                        stats.NoSelection++;
                        RememberDate(recommended, "noSelection", date);
                    }
                    else
                    {
                        stats.Selected++;
                        if (selection.Topic == AlmanacTopic.ConservativeDay)
                        {
                            stats.Conservative++;
                            RememberDate(recommended, "conservative", date);
                        }
                        else if (AlmanacSemanticCatalog.IsEverydayYi(
                            selection.Topic) && selection.IsYi)
                        {
                            stats.Everyday++;
                            RememberDate(recommended, "everyday", date);
                        }
                        else
                        {
                            stats.Cultural++;
                        }
                        if (selection.Topic == AlmanacTopic.Outing)
                            RememberDate(recommended, selection.IsYi
                                ? "outingYi" : "outingJi", date);
                        if (SolarTermCalculator.FindForLocalDate(date)
                            .HasValue)
                            RememberDate(recommended, "solarAlmanac", date);
                        HashSet<string> topicVariants;
                        if (!variants.TryGetValue(selection.Topic,
                            out topicVariants))
                        {
                            topicVariants = new HashSet<string>(
                                StringComparer.Ordinal);
                            variants.Add(selection.Topic, topicVariants);
                        }
                        topicVariants.Add(selection.VariantId);
                        string compact = selection.Text.Replace("\n", "");
                        string prefix = compact.Substring(0,
                            Math.Min(6, compact.Length));
                        Increment(prefixes, prefix);
                        selectedTextCount++;
                        if (compact.Contains("老黄历"))
                            legacyAlmanacTermCount++;
                        if (compact.StartsWith("今天",
                            StringComparison.Ordinal)) startsWithToday++;
                        if (compact.Contains("宜忌")) yiJiTermCount++;
                        if (compact.Contains("传统日历"))
                            traditionalCalendarTermCount++;
                        if (compact.Contains("民俗")) folkTermCount++;
                        if (selection.FramingId == "F06-LIFE-FIRST")
                            lifeFirstCount++;
                        if (selection.FramingId == "F07-SOURCE-LATE")
                            sourceLateCount++;
                    }
                    if (day != null)
                    {
                        HashSet<string> dailyUnmapped =
                            new HashSet<string>(StringComparer.Ordinal);
                        CollectUnmapped(day.Yi, dailyUnmapped);
                        CollectUnmapped(day.Ji, dailyUnmapped);
                        foreach (string term in dailyUnmapped)
                            Increment(unmapped, term);
                        if (ContainsRestricted(day))
                            RememberDate(recommended, "restrictedRaw", date);
                    }
                    date = date.AddDays(1);
                }
                years.Add(stats);
            }

            int totalDays = 0;
            int totalSelected = 0;
            int totalEveryday = 0;
            int totalCultural = 0;
            int totalConservative = 0;
            int totalNone = 0;
            StringBuilder coverageJson = new StringBuilder();
            for (int i = 0; i < years.Count; i++)
            {
                AlmanacCoverageProbeYear item = years[i];
                totalDays += item.Days;
                totalSelected += item.Selected;
                totalEveryday += item.Everyday;
                totalCultural += item.Cultural;
                totalConservative += item.Conservative;
                totalNone += item.NoSelection;
                coverageJson.Append("    ");
                coverageJson.Append(CoverageJson(item));
                if (i < years.Count - 1) coverageJson.Append(",");
                coverageJson.Append("\n");
            }

            StringBuilder variantJson = new StringBuilder();
            Array topicValues = Enum.GetValues(typeof(AlmanacTopic));
            for (int i = 0; i < topicValues.Length; i++)
            {
                AlmanacTopic topic = (AlmanacTopic)topicValues.GetValue(i);
                HashSet<string> topicVariants;
                int count = variants.TryGetValue(topic, out topicVariants)
                    ? topicVariants.Count : 0;
                variantJson.Append("    { \"topic\": ");
                variantJson.Append(JsonString(topic.ToString()));
                variantJson.Append(", \"variantCount\": ");
                variantJson.Append(count);
                variantJson.Append(" }");
                if (i < topicValues.Length - 1) variantJson.Append(",");
                variantJson.Append("\n");
            }
            string json = "{\n" +
                "  \"ok\": " + Bool(calculatorOk && sample != null) +
                    ",\n" +
                "  \"packageVersion\": \"1.6.8\",\n" +
                "  \"assemblyName\": \"lunar\",\n" +
                "  \"date\": \"2026-09-03\",\n" +
                "  \"sect\": 1,\n" +
                "  \"rawYi\": " + JsonArray(sample == null ? null :
                    sample.Yi) + ",\n" +
                "  \"rawJi\": " + JsonArray(sample == null ? null :
                    sample.Ji) + ",\n" +
                "  \"recognizedTopics\": " + JsonArray(recognized) +
                    ",\n" +
                "  \"suppressedTopics\": " + JsonArray(suppressed) +
                    ",\n" +
                "  \"selection\": " +
                    AlmanacSelectionJson(sampleSelection) + ",\n" +
                "  \"coverage\": [\n" + coverageJson + "  ],\n" +
                "  \"aggregate\": { \"days\": " + totalDays +
                    ", \"selected\": " + totalSelected +
                    ", \"selectedPercent\": " + Percent(totalSelected,
                        totalDays) +
                    ", \"everyday\": " + totalEveryday +
                    ", \"everydayPercent\": " + Percent(totalEveryday,
                        totalDays) +
                    ", \"cultural\": " + totalCultural +
                    ", \"culturalPercent\": " + Percent(totalCultural,
                        totalDays) +
                    ", \"conservative\": " + totalConservative +
                    ", \"conservativePercent\": " + Percent(
                        totalConservative, totalDays) +
                    ", \"noSelection\": " + totalNone +
                    ", \"noSelectionPercent\": " + Percent(totalNone,
                        totalDays) + " },\n" +
                "  \"wordingCoverage\": [\n" + variantJson + "  ],\n" +
                "  \"topPrefixes\": " + CountListJson(prefixes, 10) +
                    ",\n" +
                "  \"legacyAlmanacTermPercent\": " + Percent(
                    legacyAlmanacTermCount, selectedTextCount) + ",\n" +
                "  \"todayPrefixPercent\": " + Percent(startsWithToday,
                    selectedTextCount) + ",\n" +
                "  \"yiJiTermPercent\": " + Percent(yiJiTermCount,
                    selectedTextCount) + ",\n" +
                "  \"traditionalCalendarTermPercent\": " + Percent(
                    traditionalCalendarTermCount, selectedTextCount) +
                    ",\n" +
                "  \"folkTermPercent\": " + Percent(folkTermCount,
                    selectedTextCount) + ",\n" +
                "  \"lifeFirstPercent\": " + Percent(lifeFirstCount,
                    selectedTextCount) + ",\n" +
                "  \"sourceLatePercent\": " + Percent(sourceLateCount,
                    selectedTextCount) + ",\n" +
                "  \"topUnmapped\": " + CountListJson(unmapped, 20) +
                    ",\n" +
                "  \"recommendedDates\": " +
                    StringDictionaryJson(recommended) + "\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        private static string CoverageJson(AlmanacCoverageProbeYear item)
        {
            return "{ \"year\": " + item.Year + ", \"days\": " +
                item.Days + ", \"selected\": " + item.Selected +
                ", \"selectedPercent\": " + Percent(item.Selected,
                    item.Days) + ", \"everyday\": " + item.Everyday +
                ", \"cultural\": " + item.Cultural +
                ", \"conservative\": " + item.Conservative +
                ", \"noSelection\": " + item.NoSelection + " }";
        }

        private static string Percent(int count, int total)
        {
            double percent = total == 0 ? 0D : count * 100D / total;
            return percent.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string JsonArray(IEnumerable<string> values)
        {
            if (values == null) return "[]";
            StringBuilder json = new StringBuilder("[");
            bool first = true;
            foreach (string value in values)
            {
                if (!first) json.Append(", ");
                json.Append(JsonString(value));
                first = false;
            }
            json.Append("]");
            return json.ToString();
        }

        private static string CountListJson(Dictionary<string, int> counts,
            int limit)
        {
            List<KeyValuePair<string, int>> ordered =
                new List<KeyValuePair<string, int>>(counts);
            ordered.Sort(delegate(KeyValuePair<string, int> left,
                KeyValuePair<string, int> right)
            {
                int byCount = right.Value.CompareTo(left.Value);
                return byCount != 0 ? byCount :
                    StringComparer.Ordinal.Compare(left.Key, right.Key);
            });
            StringBuilder json = new StringBuilder("[");
            int take = Math.Min(limit, ordered.Count);
            for (int i = 0; i < take; i++)
            {
                if (i > 0) json.Append(", ");
                json.Append("{ \"value\": ");
                json.Append(JsonString(ordered[i].Key));
                json.Append(", \"days\": ");
                json.Append(ordered[i].Value);
                json.Append(" }");
            }
            json.Append("]");
            return json.ToString();
        }

        private static string StringDictionaryJson(
            Dictionary<string, string> values)
        {
            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            StringBuilder json = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) json.Append(", ");
                json.Append(JsonString(keys[i]));
                json.Append(": ");
                json.Append(JsonString(values[keys[i]]));
            }
            json.Append("}");
            return json.ToString();
        }

        private static void CollectUnmapped(IReadOnlyList<string> terms,
            HashSet<string> destination)
        {
            foreach (string term in terms)
            {
                AlmanacTopic ignored;
                if (!AlmanacSemanticCatalog.TryMap(term, out ignored))
                    destination.Add(term);
            }
        }

        private static bool ContainsRestricted(AlmanacDayInfo day)
        {
            string[] restricted = { "求医", "治病", "针灸", "纳财",
                "求财", "置产", "词讼", "立券", "交易", "安葬",
                "入殓", "祭祀", "祈福", "动土", "修造" };
            foreach (string expected in restricted)
                foreach (string term in day.Yi)
                    if (term == expected) return true;
            foreach (string expected in restricted)
                foreach (string term in day.Ji)
                    if (term == expected) return true;
            return false;
        }

        private static void Increment(Dictionary<string, int> counts,
            string key)
        {
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }

        private static void RememberDate(Dictionary<string, string> dates,
            string key, DateTimeOffset date)
        {
            if (!dates.ContainsKey(key))
                dates.Add(key, date.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private sealed class AlmanacCoverageProbeYear
        {
            internal AlmanacCoverageProbeYear(int year)
            {
                Year = year;
            }

            internal int Year;
            internal int Days;
            internal int Selected;
            internal int Everyday;
            internal int Cultural;
            internal int Conservative;
            internal int NoSelection;
        }

        public static void RunSolarTermProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            SolarTermProbeCase[] cases = new SolarTermProbeCase[]
            {
                new SolarTermProbeCase(2016, 2, 4, TimeSpan.FromHours(8),
                    SolarTerm.StartOfSpring, "立春", 315),
                new SolarTermProbeCase(2016, 3, 20, TimeSpan.FromHours(8),
                    SolarTerm.VernalEquinox, "春分", 0),
                new SolarTermProbeCase(2016, 6, 21, TimeSpan.FromHours(8),
                    SolarTerm.SummerSolstice, "夏至", 90),
                new SolarTermProbeCase(2016, 9, 7, TimeSpan.FromHours(8),
                    SolarTerm.WhiteDew, "白露", 165),
                new SolarTermProbeCase(2016, 12, 21, TimeSpan.FromHours(8),
                    SolarTerm.WinterSolstice, "冬至", 270),
                new SolarTermProbeCase(2026, 2, 4, TimeSpan.FromHours(8),
                    SolarTerm.StartOfSpring, "立春", 315),
                new SolarTermProbeCase(2026, 2, 18, TimeSpan.FromHours(8),
                    SolarTerm.RainWater, "雨水", 330),
                new SolarTermProbeCase(2026, 9, 7, TimeSpan.FromHours(8),
                    SolarTerm.WhiteDew, "白露", 165),
                new SolarTermProbeCase(2026, 9, 23, TimeSpan.FromHours(8),
                    SolarTerm.AutumnalEquinox, "秋分", 180),
                new SolarTermProbeCase(2026, 12, 7, TimeSpan.FromHours(8),
                    SolarTerm.MajorSnow, "大雪", 255),
                new SolarTermProbeCase(2026, 12, 22, TimeSpan.FromHours(8),
                    SolarTerm.WinterSolstice, "冬至", 270)
            };
            DateTimeOffset[] nonTermDates = new DateTimeOffset[]
            {
                new DateTimeOffset(2026, 9, 6, 12, 0, 0,
                    TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 9, 8, 12, 0, 0,
                    TimeSpan.FromHours(8))
            };

            bool oracleOk = true;
            bool nonTermOk = true;
            string failure = null;
            StringBuilder json = new StringBuilder();
            try
            {
                json.Append("  \"oracle\": [\n");
                for (int i = 0; i < cases.Length; i++)
                {
                    SolarTermInfo? info =
                        SolarTermCalculator.FindForLocalDate(
                            cases[i].LocalDate);
                    bool match = info.HasValue &&
                        info.Value.Term == cases[i].ExpectedTerm &&
                        info.Value.ChineseName == cases[i].ExpectedName &&
                        info.Value.LongitudeDegrees ==
                            cases[i].ExpectedLongitude;
                    oracleOk &= match;
                    json.Append(SolarTermProbeEntry("oracle-" + (i + 1),
                        cases[i].LocalDate, info, match));
                    if (i < cases.Length - 1) json.Append(",");
                    json.Append("\n");
                }
                json.Append("  ],\n  \"non_term\": [\n");
                for (int i = 0; i < nonTermDates.Length; i++)
                {
                    SolarTermInfo? info =
                        SolarTermCalculator.FindForLocalDate(nonTermDates[i]);
                    bool match = !info.HasValue;
                    nonTermOk &= match;
                    json.Append(SolarTermProbeEntry("non-term-" + (i + 1),
                        nonTermDates[i], info, match));
                    if (i < nonTermDates.Length - 1) json.Append(",");
                    json.Append("\n");
                }
                json.Append("  ],\n");
                SolarTermInfo? current =
                    SolarTermCalculator.FindForLocalDate(DateTimeOffset.Now);
                json.Append("  \"current\": ");
                json.Append(SolarTermProbeEntry("current",
                    DateTimeOffset.Now, current, true));
                json.Append("\n");
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }
            timer.Stop();
            bool ok = failure == null && oracleOk && nonTermOk;
            string escapedFailure = failure == null ? "" : failure
                .Replace("\\", "\\\\").Replace("\"", "\\\"");
            string prefix = "{\n  \"ok\": " + Bool(ok) + ",\n" +
                "  \"oracle_ok\": " + Bool(oracleOk) + ",\n" +
                "  \"non_term_ok\": " + Bool(nonTermOk) + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\",\n";
            string body = json.Length == 0
                ? "  \"oracle\": []\n" : json.ToString().Substring(
                    json.ToString().IndexOf("  \"oracle\":",
                    StringComparison.Ordinal));
            string parent = Path.GetDirectoryName(
                Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, prefix + body + "}\n",
                new UTF8Encoding(false));
        }

        public static void RunDisplayTopologyProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string failure = null;
            DisplayTopologySnapshot snapshot = null;
            WindowsDisplayTopologyProvider provider =
                new WindowsDisplayTopologyProvider();
            try
            {
                snapshot = provider.Capture();
                if (snapshot == null && failure == null)
                    failure = provider.LastCaptureError;
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }
            timer.Stop();
            bool ok = failure == null && snapshot != null;
            StringBuilder json = new StringBuilder();
            json.Append("  \"surfaces\": [\n");
            if (snapshot != null)
            {
                IReadOnlyList<DisplaySurfaceSnapshot> surfaces =
                    snapshot.Surfaces;
                for (int index = 0; index < surfaces.Count; index++)
                {
                    DisplaySurfaceSnapshot surface = surfaces[index];
                    json.Append("    { ");
                    json.Append("\"surface_id\": " +
                        JsonString(surface.RuntimeSurfaceId) + ", ");
                    json.Append("\"gdi\": " +
                        JsonString(surface.RuntimeGdiName) + ", ");
                    json.Append("\"bounds\": { \"left\": " +
                        surface.Bounds.Left + ", \"top\": " +
                        surface.Bounds.Top + ", \"width\": " +
                        surface.Bounds.Width + ", \"height\": " +
                        surface.Bounds.Height + " }, ");
                    json.Append("\"work_area\": { \"left\": " +
                        surface.WorkArea.Left + ", \"top\": " +
                        surface.WorkArea.Top + ", \"width\": " +
                        surface.WorkArea.Width + ", \"height\": " +
                        surface.WorkArea.Height + " }, ");
                    json.Append("\"primary\": " +
                        Bool(surface.IsPrimary) + ", ");
                    json.Append("\"rotation_degrees\": " +
                        surface.RotationDegrees + ", ");
                    json.Append("\"targets\": [");
                    for (int targetIndex = 0;
                        targetIndex < surface.Targets.Count; targetIndex++)
                    {
                        DisplayTargetIdentity target =
                            surface.Targets[targetIndex];
                        if (targetIndex > 0) json.Append(", ");
                        json.Append("{ \"key_prefix\": " + JsonString(
                            target.StableKey.StartsWith("mdp:",
                                StringComparison.OrdinalIgnoreCase)
                                ? "mdp:" : "ephemeral:") +
                            ", \"durable\": " + Bool(target.IsDurable) +
                            ", \"friendly\": " +
                            JsonString(target.FriendlyName) + " }");
                    }
                    json.Append("] }");
                    if (index < surfaces.Count - 1) json.Append(",");
                    json.Append("\n");
                }
            }
            json.Append("  ]\n");
            string escapedFailure = failure == null ? String.Empty :
                failure.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string prefix = "{\n  \"ok\": " + Bool(ok) + ",\n" +
                "  \"surface_count\": " +
                (snapshot == null ? 0 : snapshot.Surfaces.Count) + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\",\n";
            string parent = Path.GetDirectoryName(
                Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, prefix + json + "}\n",
                new UTF8Encoding(false));
        }

        private static string SolarTermProbeEntry(string label,
            DateTimeOffset local, SolarTermInfo? info, bool matched)
        {
            string term = info.HasValue ? "\"" + info.Value.Term + "\"" :
                "null";
            string chineseName = info.HasValue
                ? "\"" + info.Value.ChineseName + "\"" : "null";
            string longitude = info.HasValue
                ? info.Value.LongitudeDegrees.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) :
                "null";
            string instantUtc = info.HasValue
                ? "\"" + info.Value.InstantUtc.ToString(
                    "yyyy-MM-ddTHH:mm:sszzz",
                    System.Globalization.CultureInfo.InvariantCulture) + "\"" :
                "null";
            string localDate = local.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
            string offset = local.Offset.Hours >= 0 ? "+" : "-";
            offset += Math.Abs(local.Offset.Hours).ToString("00",
                System.Globalization.CultureInfo.InvariantCulture) + ":" +
                Math.Abs(local.Offset.Minutes).ToString("00",
                    System.Globalization.CultureInfo.InvariantCulture);
            return "    { \"label\": \"" + label + "\", " +
                "\"local_date\": \"" + localDate + "\", " +
                "\"offset\": \"" + offset + "\", " +
                "\"matched\": " + (matched ? "true" : "false") + ", " +
                "\"term\": " + term + ", " +
                "\"chinese_name\": " + chineseName + ", " +
                "\"longitude\": " + longitude + ", " +
                "\"instant_utc\": " + instantUtc + " }";
        }

        private static string Bool(bool value)
        {
            if (_reportedChecks != null) _reportedChecks.Add(value);
            return value ? "true" : "false";
        }

        private static bool TouchesTransparency(Bitmap bitmap, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= 192 || ny < 0 || ny >= 208 ||
                        bitmap.GetPixel(nx, ny).A == 0)
                        return true;
                }
            }
            return false;
        }

    }
}
