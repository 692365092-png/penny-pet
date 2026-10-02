using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static bool RunStickyContentViewChecks()
        {
            for (int kind = 0; kind < 3; kind++)
            {
                var data = new StickyNoteData
                {
                    IsTodoList = kind == 1, IsSchedule = kind == 2,
                    Text = "中文・日本語 body", Width = 360, Height = 300
                };
                data.TodoItems.Add(new StickyTodoItem("待办", false));
                data.ScheduleItems.Add(new StickyScheduleItem("日程", DateTime.Today.AddDays(1)));
                var note = new StickyNoteWindow(data);
                var view = (StickyNoteWindow.StickyContentView)Pc2Get(note, "_contentView");
                DispatcherTimer timer = kind == 0
                    ? (DispatcherTimer)Pc2Get(view, "_linkRefreshTimer")
                    : kind == 2 ? (DispatcherTimer)Pc2Get(view, "_scheduleRefreshTimer") : null;
                try
                {
                    Pc2Assert(CountContentControls<RichTextBox>(note) == (kind == 0 ? 1 : 0),
                        "only ordinary notes instantiate RichTextBox");
                    Pc2Assert(CountContentControls<ComboBox>(note) == (kind == 0 ? 2 : 1),
                        "list notes create only their size selector");
                    Pc2Assert(Pc2Get(note, "_reminderList") == null && !note.HasReminderBanner,
                        "empty reminder projection allocates no banner controls");
                    note.UpdateReminderBanner(new ReminderItem[0]);
                    Pc2Assert(Pc2Get(note, "_reminderList") == null,
                        "empty refresh remains allocation-free");
                    note.ShowAndEdit();
                    WF.Application.DoEvents();
                    var focused = Keyboard.FocusedElement;
                    var body = view.Body;
                    var reminder = new ReminderItem(DateTime.UtcNow.AddMinutes(5), "提醒");
                    note.UpdateReminderBanner(new[] { reminder });
                    note.PreviewReminderFontSize(reminder, 22F);
                    note.RefreshReminderCountdown(DateTime.UtcNow.AddSeconds(2));
                    Pc2Assert(Object.ReferenceEquals(view.Body, body) &&
                        Object.ReferenceEquals(focused, Keyboard.FocusedElement) &&
                        Math.Abs(note.ReminderBannerFirstFontSize - 22F) < 0.2F,
                        "reminder update/preview preserves current body and keyboard focus");
                    note.UpdateReminderBanner(new ReminderItem[0]);
                    Pc2Assert(!note.HasReminderBanner, "empty banner is hidden again");
                    if (kind == 0)
                    {
                        var editor = (RichTextBox)body;
                        string before = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
                        editor.SelectAll();
                        editor.BeginChange();
                        editor.Selection.Text = "编辑后・日本語";
                        editor.EndChange();
                        editor.Undo();
                        Pc2Assert(new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text == before,
                            "ordinary text undo restores the original document");
                        bool composing = false;
                        note.ImeCompositionChanged += (sender, e) => composing = e.Active;
                        var composition = new TextComposition(InputManager.Current, editor, "中");
                        editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                            { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
                        Pc2Assert(composing && note.IsImeCompositionActiveForHost,
                            "composition start reaches the host through the active text view");
                        editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
                        Pc2Assert(!composing && !note.IsImeCompositionActiveForHost,
                            "composition completion clears the host guard");
                    }
                }
                finally { note.CloseForApplicationExit(); note.Dispose(); }
                WF.Application.DoEvents();
                Pc2Assert(note.IsDisposed && (timer == null || !timer.IsEnabled),
                    "closing stops the current view timer including queued refresh work");
            }
            return true;
        }

        private static int CountContentControls<T>(DependencyObject root) where T : DependencyObject
        {
            int result = root is T ? 1 : 0;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var dependency = child as DependencyObject;
                if (dependency != null) result += CountContentControls<T>(dependency);
            }
            return result;
        }
    }
}
