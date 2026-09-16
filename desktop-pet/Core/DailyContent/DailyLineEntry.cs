using System;

namespace PennyPet
{
    internal sealed class DailyLineEntry
    {
        internal DailyLineEntry(string id, string text)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A daily line needs an id.", "id");
            if (String.IsNullOrWhiteSpace(text))
                throw new ArgumentException("A daily line needs text.", "text");
            Id = id;
            Text = text;
        }

        internal string Id { get; private set; }
        internal string Text { get; private set; }
    }
}
