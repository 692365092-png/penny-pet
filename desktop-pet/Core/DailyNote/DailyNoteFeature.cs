using System;

namespace PennyPet
{
    internal enum DailyNoteActionKind
    {
        Create,
        AlreadyIssued,
        MissedDay,
        ProgramComplete
    }

    internal sealed class DailyNoteEntry
    {
        internal DailyNoteEntry(string title, string body)
        {
            Title = title ?? String.Empty;
            Body = body ?? String.Empty;
        }

        internal string Title { get; private set; }
        internal string Body { get; private set; }
    }

    internal sealed class DailyNoteProgress
    {
        internal int IssuedDay;
        internal DateTime LastIssuedLocalDate;
        internal bool Completed;
    }

    internal sealed class DailyNoteAction
    {
        internal DailyNoteAction(DailyNoteActionKind kind, int dayNumber,
            DailyNoteEntry entry)
        {
            Kind = kind;
            DayNumber = dayNumber;
            Entry = entry;
        }

        internal DailyNoteActionKind Kind { get; private set; }
        internal int DayNumber { get; private set; }
        internal DailyNoteEntry Entry { get; private set; }
    }

    internal static class DailyNoteFeature
    {
        internal const int ProgramLengthDays = 30;

        internal static DailyNoteAction Decide(DateTime localDate,
            DailyNoteProgress progress, DailyNoteEntry entry)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (progress.Completed || progress.IssuedDay >= ProgramLengthDays)
                return new DailyNoteAction(DailyNoteActionKind.ProgramComplete,
                    progress.IssuedDay, null);

            DateTime today = localDate.Date;
            DateTime lastIssued = progress.LastIssuedLocalDate.Date;
            if (progress.IssuedDay > 0 && lastIssued == today)
                return new DailyNoteAction(DailyNoteActionKind.AlreadyIssued,
                    progress.IssuedDay, entry);

            int nextDay = progress.IssuedDay + 1;
            if (progress.IssuedDay > 0 &&
                (today - lastIssued).TotalDays > 1)
                return new DailyNoteAction(DailyNoteActionKind.MissedDay,
                    nextDay, entry);

            if (nextDay > ProgramLengthDays)
                return new DailyNoteAction(DailyNoteActionKind.ProgramComplete,
                    ProgramLengthDays, null);

            return new DailyNoteAction(DailyNoteActionKind.Create,
                nextDay, entry);
        }

        internal static DailyNoteProgress MarkIssued(
            DailyNoteProgress progress, DateTime localDate, int dayNumber)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            int issued = Math.Max(1, Math.Min(ProgramLengthDays, dayNumber));
            return new DailyNoteProgress
            {
                IssuedDay = issued,
                LastIssuedLocalDate = localDate.Date,
                Completed = issued >= ProgramLengthDays
            };
        }
    }
}
