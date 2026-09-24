using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm : IStickyPetSurface, IStickyPresentation, IStickyReminderActions
    {
        internal StickyWorkspace AttachStickyWorkspace(SynchronizationContext context)
        {
            StickyWorkspace workspace = _notes.AttachWorkspace(this, this, this, context);
            workspace.FirstRendered += MarkFirstRendered;
            workspace.WindowRemoved += delegate(string noteId, bool forgetExpected)
            {
                _renderedFirstRenderNoteIds.Remove(noteId);
                if (forgetExpected) _expectedFirstRenderNoteIds.Remove(noteId);
            };
            workspace.TypingActivity += TriggerTypingAnimation;
            workspace.ExitReady += BeginExitSequence;
            return workspace;
        }

        Rectangle IStickyPetSurface.Bounds { get { return Bounds; } }
        bool IStickyPetSurface.IsDisposed { get { return IsDisposed || Disposing; } }
        bool IStickyPetSurface.IsExiting { get { return _exiting; } }
        bool IStickyPetSurface.HasHandle { get { return IsHandleCreated && Handle != IntPtr.Zero; } }
        DisplayTopologySnapshot IStickyPetSurface.CurrentTopologySnapshot() { return CurrentTopologySnapshot(); }
        WindowFacts IStickyPetSurface.CaptureWindowFacts(DisplayTopologySnapshot topology)
        { return CapturePetWindowFacts(topology); }

        void IStickyPresentation.RefreshMenu() { RefreshMenuText(); }
        void IStickyPresentation.CloseMenu() { if (_menu != null && _menu.Visible) _menu.Close(); }
        void IStickyPresentation.ShowBubble(string text) { ShowBubble(text); }
        void IStickyPresentation.KeepBelowModal(Form window) { _windowLayers.KeepTransientBelowModal(window); }
        bool IStickyPresentation.Confirm(string text, string title)
        { return MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
        void IStickyPresentation.ShowError(string text, string title)
        { MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }

        void IStickyPresentation.ShowManager(Func<List<StickyNoteData>> notes,
            StickyNotesManagerCommands commands, Action create, Action<StickyNoteData> show)
        {
            commands.ExportBackup = ExportStickyNotesBackup;
            commands.PrepareImport = PrepareStickyNotesImport;
            commands.ConfirmImport = CommitStickyNotesImport;
            commands.FullRestore = RestoreStickyNotesBackup;
            bool createRequested, fullRestoreRequested;
            StickyNoteData showRequested;
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(notes, commands))
            {
                _windowLayers.ShowModal(this, manager);
                createRequested = manager.CreateRequested;
                showRequested = manager.ShowRequested;
                fullRestoreRequested = manager.FullRestoreRequested;
            }
            if (fullRestoreRequested) RestoreStickyNotesBackup();
            else if (createRequested) create();
            else if (showRequested != null) show(showRequested);
        }

        List<ReminderItem> IStickyReminderActions.GetItems() { return _reminders.GetItems(); }
        void IStickyReminderActions.CancelForNote(StickyNoteData note, bool feedback)
        { CancelReminderForNote(note, feedback); }
        void IStickyReminderActions.Edit(ReminderItem reminder) { EditReminder(reminder); }
        void IStickyReminderActions.Cancel(ReminderItem reminder, bool feedback)
        { CancelReminder(reminder, feedback); }
    }
}
