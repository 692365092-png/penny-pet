using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static bool RunStickyFeatureBoundaryChecks()
        {
            var feature = new StickyFeature("unused-feature-test", _ => PersistenceResult.Success());
            var ports = new StickyPortProbe();
            var context = new Pc2Context();
            StickyNoteData note = feature.CreateDraft("before", Point.Empty);
            using (StickyWorkspace workspace = feature.AttachWorkspace(ports, ports, ports, context))
            {
                Pc2Assert(Object.ReferenceEquals(feature.Workspace, workspace) &&
                    Object.ReferenceEquals(workspace.Notes.Model, feature.Model),
                    "one feature owns one canonical model and its attached UI runtime");
                // No PetForm, HWND, host thread, real files or dialogs are needed.
                workspace.Hosted.AddNote(note.Id);
                int rendered = 0, typing = 0;
                workspace.FirstRendered += id => { if (id == note.Id) rendered++; };
                workspace.TypingActivity += () => typing++;
                workspace.HostedStickyEventReceived(StickyUiEvent.Signal(
                    StickyUiEventKind.FirstRendered, "unknown-note", false, 1));
                workspace.HostedStickyEventReceived(StickyUiEvent.Signal(
                    StickyUiEventKind.FirstRendered, note.Id, false, 1));
                workspace.HostedStickyEventReceived(StickyUiEvent.Signal(
                    StickyUiEventKind.TypingActivity, note.Id, false, 2));
                ports.Exiting = true;
                workspace.HostedStickyEventReceived(StickyUiEvent.Signal(
                    StickyUiEventKind.TypingActivity, note.Id, false, 3));
                ports.Exiting = false;
                Pc2Assert(rendered == 1 && typing == 1,
                    "lifecycle output respects hosted membership and exit state");

                var reminder = new ReminderItem(DateTime.UtcNow.AddMinutes(5), "test", note.Id);
                ports.Items.Add(reminder);
                Pc2Assert(Object.ReferenceEquals(workspace.ReminderItems[0], reminder),
                    "shared reminder projection retains edit identity");
                workspace.HostedStickyEventReceived(new StickyUiEvent(
                    StickyUiEventKind.ModifyReminderRequested, note.Id, null, false, 4, reminder));
                workspace.HostedStickyEventReceived(new StickyUiEvent(
                    StickyUiEventKind.DeleteReminderRequested, note.Id, null, false, 5, reminder));
                workspace.HostedStickyEventReceived(StickyUiEvent.Signal(
                    StickyUiEventKind.CancelReminderRequested, note.Id, false, 6));
                Pc2Assert(ports.Edits == 1 && ports.Cancels == 1 && ports.NoteCancels == 1,
                    "reminder actions use the explicit port");

                bool tabsChanged;
                var changed = note.CloneForPersistence(); changed.Text = "accepted";
                Pc2Assert(workspace.Facts.TryApplySnapshot(StickyNoteUiSnapshot.Capture(changed),
                    7, null, null, null, out tabsChanged) && feature.Model.Find(note.Id).Text == "accepted",
                    "accepted editor content updates the shared model without saving in the receiver");
                Pc2Assert(!feature.HasUnsavedChanges, "facts receiver is memory-only");
                workspace.ShowStickyNotesManager();
                Pc2Assert(ports.Managers == 1, "management view receives typed feature commands");

                int commands = 0;
                workspace.QueueStickyWindowAction(() => commands++, "probe");
                Pc2Assert(commands == 0 && ports.MenuCloses == 1, "window action is deferred on owner context");
                context.PumpUntil(() => commands == 1);
                workspace.QueueStickyWindowAction(() => commands++, "disposed-probe");
                int before = context.Executed;
                workspace.Dispose();
                context.PumpUntil(() => context.Executed > before);
                Pc2Assert(commands == 1, "queued UI work cannot outlive its workspace");
            }
            return true;
        }

        private sealed class StickyPortProbe : IStickyPetSurface, IStickyPresentation, IStickyReminderActions
        {
            internal bool Exiting;
            internal int Edits, Cancels, NoteCancels, Managers, MenuCloses;
            internal readonly List<ReminderItem> Items = new List<ReminderItem>();
            public Rectangle Bounds { get { return new Rectangle(20, 30, 192, 208); } }
            public bool IsDisposed { get { return false; } }
            public bool IsExiting { get { return Exiting; } }
            public bool HasHandle { get { return false; } }
            public DisplayTopologySnapshot CurrentTopologySnapshot() { return null; }
            public WindowFacts CaptureWindowFacts(DisplayTopologySnapshot topology) { return null; }
            public void RefreshMenu() { }
            public void CloseMenu() { MenuCloses++; }
            public void ShowBubble(string text) { }
            public void KeepBelowModal(Form window) { }
            public bool Confirm(string text, string title) { return false; }
            public void ShowError(string text, string title) { throw new InvalidOperationException(text); }
            public void ShowManager(Func<List<StickyNoteData>> notes, StickyNotesManagerCommands commands,
                Action create, Action<StickyNoteData> show)
            {
                Pc2Assert(notes().Count == 1 && commands.HideNote != null && commands.DeleteNote != null &&
                    commands.CollapseAll != null && commands.ExpandAll != null && commands.TileAll != null,
                    "management actions belong to the feature");
                Managers++;
            }
            public List<ReminderItem> GetItems() { return new List<ReminderItem>(Items); }
            public void CancelForNote(StickyNoteData note, bool feedback) { NoteCancels++; }
            public void Edit(ReminderItem reminder) { Edits++; }
            public void Cancel(ReminderItem reminder, bool feedback) { Cancels++; }
        }
    }
}
