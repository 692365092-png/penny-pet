using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal interface IReminderPresentation
    {
        void RemindersChanged();
        bool TryShowPreAlert(string text, bool updateCurrent);
        void ClosePreAlert();
        void CloseCurrentMessage();
        void RefreshHover();
        void ShowDue(ReminderItem item, StickyNoteData linkedNote);
        Task PrepareAttentionAsync();
        void BeginAttention();
    }

    // All mutable execution state belongs to Pet STA. The view handles windows;
    // the schedule owns reminder data, and Core contains only timing rules.
    internal sealed class ReminderRuntime : IDisposable
    {
        private readonly ReminderSchedule _schedule;
        private readonly PetSettings _settings;
        private readonly StickyFeature _notes;
        private readonly IReminderPresentation _view;
        private readonly Timer _clock;
        private ReminderItem _preAlertItem;
        private int _attentionGeneration;
        private bool _running;

        internal ReminderRuntime(ReminderSchedule schedule, PetSettings settings,
            StickyFeature notes, IReminderPresentation view)
        {
            _schedule = schedule;
            _settings = settings;
            _notes = notes;
            _view = view;
            _clock = new Timer { Interval = 500 };
            _clock.Tick += ClockTick;
        }

        internal void Restore(DateTime launchedUtc)
        {
            List<ReminderItem> future = new List<ReminderItem>();
            foreach (ReminderItem item in _settings.Reminders)
                if (ReminderRules.ShouldRestoreReminderAfterLaunch(item, launchedUtc))
                    future.Add(item);
            _schedule.Restore(future);
            bool removedExpired = future.Count != _settings.Reminders.Count;
            bool removedOrphans = ReconcileLinkedNotes();
            if (removedExpired || removedOrphans) SaveSettings();
        }

        internal void Start()
        {
            _running = true;
            _clock.Start();
        }

        internal void Stop()
        {
            _running = false;
            _clock.Stop();
            _preAlertItem = null;
            ++_attentionGeneration;
        }

        public void Dispose()
        {
            Stop();
            _clock.Dispose();
        }

        private void ClockTick(object sender, EventArgs e)
        {
            Tick(DateTime.UtcNow);
        }

        internal void Tick(DateTime nowUtc)
        {
            if (!_running) return;
            ReminderItem due = _schedule.FirstDue(nowUtc);
            if (due != null)
            {
                // Consume before callbacks: a nested message loop or repeated
                // tick must never deliver this reminder twice.
                _schedule.Remove(due);
                StickyNoteData linkedNote = UpdateLinkedNote(due, true);
                _view.CloseCurrentMessage();
                SaveChanges();
                int generation = ++_attentionGeneration;
                _view.ShowDue(due, linkedNote);
                RequestAttentionAnimation(generation);
                return;
            }
            RefreshPreAlert(nowUtc);
            _view.RefreshHover();
        }

        internal bool RefreshPreAlert(DateTime nowUtc)
        {
            if (!_running) return false;
            ReminderItem next = _schedule.NextPreAlert;
            TimeSpan remaining = next == null ? TimeSpan.Zero : next.DeadlineUtc - nowUtc;
            if (!ReminderRules.ShouldShowPreAlert(next, remaining))
            {
                _view.ClosePreAlert();
                return false;
            }
            int seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            string text = "提醒倒计时 " + seconds + " 秒\n" + next.Text;
            if (_view.TryShowPreAlert(text, ReferenceEquals(_preAlertItem, next)))
                _preAlertItem = next;
            return true;
        }

        internal void MessageClosed(PetMessageKind kind)
        {
            if (kind == PetMessageKind.ReminderPreAlert) _preAlertItem = null;
            if (kind == PetMessageKind.ReminderDue) ++_attentionGeneration;
        }

        private async void RequestAttentionAnimation(int generation)
        {
            if (!_running || generation != _attentionGeneration) return;
            try
            {
                // Called on Pet STA: await returns to that UI context. Resource
                // loading does not own or extend this notification's lifetime.
                await _view.PrepareAttentionAsync();
            }
            catch (Exception error)
            {
                if (_running && generation == _attentionGeneration)
                    ApplicationDiagnostics.ReportNonFatal("reminder-art-preload", error);
                return;
            }
            if (_running && generation == _attentionGeneration)
                _view.BeginAttention();
        }

        internal ReminderItem Add(DateTime deadlineUtc, string text,
            float fontSizePoints, bool preAlertEnabled)
        {
            ReminderItem item = _schedule.Add(deadlineUtc, text, null,
                fontSizePoints, preAlertEnabled);
            SaveChanges();
            return item;
        }

        internal ReminderItem Edit(ReminderItem existing, DateTime deadlineUtc,
            string text, float fontSizePoints, bool preAlertEnabled)
        {
            if (existing == null || !_schedule.GetItems().Contains(existing)) return null;
            ReminderItem replacement = _schedule.Replace(existing, deadlineUtc,
                text, fontSizePoints, preAlertEnabled);
            if (ReferenceEquals(_preAlertItem, existing)) _view.ClosePreAlert();
            UpdateLinkedNote(replacement, false);
            SaveChanges();
            return replacement;
        }

        internal bool Cancel(ReminderItem item)
        {
            if (!_schedule.Remove(item)) return false;
            UpdateLinkedNote(item, false);
            if (ReferenceEquals(_preAlertItem, item)) _view.ClosePreAlert();
            SaveChanges();
            return true;
        }

        internal int CancelForNote(StickyNoteData note)
        {
            if (note == null) return 0;
            int removed = _schedule.RemoveBySourceNoteId(note.Id);
            RefreshLinkedNote(note);
            if (_preAlertItem != null && String.Equals(
                _preAlertItem.SourceNoteId, note.Id, StringComparison.OrdinalIgnoreCase))
                _view.ClosePreAlert();
            _notes.Save();
            SaveChanges();
            return removed;
        }

        internal void CancelAll()
        {
            _schedule.Cancel();
            ReconcileLinkedNotes();
            _view.CloseCurrentMessage();
            ++_attentionGeneration;
            SaveChanges();
        }

        private void SaveSettings()
        {
            _settings.SetReminders(_schedule.GetItems());
            _settings.SaveAsync();
        }

        private void SaveChanges()
        {
            SaveSettings();
            _view.RemindersChanged();
        }

        internal void ReconcileNoteLinks()
        {
            if (ReconcileLinkedNotes()) SaveChanges();
        }

        private bool ReconcileLinkedNotes()
        {
            bool changed = false;
            HashSet<string> noteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.InStorageOrder)
            {
                noteIds.Add(note.Id);
                long previous = note.ReminderUtcTicks;
                RefreshLinkedNote(note);
                changed |= previous != note.ReminderUtcTicks;
            }
            int removed = _schedule.RemoveLinkedNotesNotIn(noteIds);
            if (changed) _notes.Save();
            return removed > 0;
        }

        private StickyNoteData UpdateLinkedNote(ReminderItem item, bool makeVisible)
        {
            if (String.IsNullOrEmpty(item.SourceNoteId)) return null;
            StickyNoteData note = _notes.Find(item.SourceNoteId);
            if (note == null) return null;
            RefreshLinkedNote(note);
            if (makeVisible) note.Visible = true;
            _notes.Save();
            return note;
        }

        private void RefreshLinkedNote(StickyNoteData note)
        {
            ReminderItem next = _schedule.FindBySourceNoteId(note.Id);
            note.ReminderUtcTicks = next == null ? 0 : next.DeadlineUtc.Ticks;
        }
    }
}
