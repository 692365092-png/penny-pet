using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    // Read-only Pet surface facts. No writable Form or application model escapes.
    internal interface IStickyPetSurface
    {
        Rectangle Bounds { get; }
        bool IsDisposed { get; }
        bool IsExiting { get; }
        bool HasHandle { get; }
        DisplayTopologySnapshot CurrentTopologySnapshot();
        WindowFacts CaptureWindowFacts(DisplayTopologySnapshot topology);
    }

    internal interface IStickyPresentation
    {
        void RefreshMenu();
        void CloseMenu();
        void ShowBubble(string text);
        void KeepBelowModal(Form window);
        bool Confirm(string text, string title);
        void ShowError(string text, string title);
        void ShowManager(Func<List<StickyNoteData>> notes, StickyNotesManagerCommands commands,
            Action create, Action<StickyNoteData> show);
    }

    internal interface IStickyReminderActions
    {
        List<ReminderItem> GetItems();
        void CancelForNote(StickyNoteData note, bool feedback);
        void Edit(ReminderItem reminder);
        void Cancel(ReminderItem reminder, bool feedback);
    }
}
