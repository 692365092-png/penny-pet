using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using PennyPet;
const int iterations = 10000;
var facts = Enumerable.Range(1, iterations).Select(i => new WindowFacts("n", "target", "display",
    new PhysicalRect(100, 100, 300, 230), 96, 1, i)).ToArray();
var reminders = Enumerable.Range(0, 5).Select(i => new ReminderItem(
    new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), "item" + i, "n", 12.5F, true)).ToArray();
var results = new List<object>();
Measure("effective-facts-10000", () => {
    var runtime = new StickyPlacementRuntime();
    foreach (var fact in facts) runtime.TryUpdateEffective("n", fact);
    return runtime.Count;
});
Measure("five-reminders-10000", () => {
    int count = 0;
    for (int i = 0; i < iterations; i++) count += StickyUiCommand.UpdateReminders("n", reminders).Reminders.Length;
    return count;
});
foreach (int count in new[] { 10, 50, 100 }) {
    var random = new Random(417);
    var notes = Enumerable.Range(0, count).Select(i => new StickyNoteData {
        Id = "n" + i, Visible = true, X = i * 300, Y = 200, Width = 280, Height = 220,
        ModifiedUtcTicks = random.Next()
    }).ToList();
    var view = notes.AsReadOnly();
    Measure("side-tabs-" + count + "-notes-20000-checks", () => {
        int hits = 0;
        for (int i = 0; i < iterations * 2; i++) {
#if BASELINE
            // Exact previous GetAll + coverage path: detached list, date sort,
            // then Rectangle.IntersectsWith. No cached order or data changes.
            var ordered = new List<StickyNoteData>(notes);
            ordered.Sort((left, right) => right.ModifiedUtcTicks.CompareTo(left.ModifiedUtcTicks));
            var bounds = new Rectangle(-150, -200, 128, 100);
            foreach (var note in ordered) {
                if (note == null || !note.Visible || note.Width <= 0 || note.Height <= 0) continue;
                if (bounds.IntersectsWith(new Rectangle(note.X, note.Y, note.Width, note.Height))) { hits++; break; }
            }
#else
            if (StickyNoteWindowRules.AnyVisibleNoteOverlaps(view, new DockRect(-150, -200, 128, 100))) hits++;
#endif
        }
        return hits;
    });
}
var largeNote = new StickyNoteData {
    Id = "large1", Text = new string('x', 1000000), RichTextRtf = "{\\rtf1 " + new string('x', 14999992) + "}"
};
long noteBytes = Encoding.UTF8.GetByteCount(StickyNoteCodec.SerializeLine(largeNote)) + 2L;
var report = new {
#if BASELINE
    variant = "baseline",
#else
    variant = "after",
#endif
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    warmups = 3, samples = 7, results,
    codecBudget = new { bodyCharacters = largeNote.Text.Length, rtfCharacters = largeNote.RichTextRtf.Length,
        oneNoteBytesIncludingCRLF = noteBytes, twoNoteBytesIncludingCRLF = noteBytes * 2,
        readerLimitBytes = StickyNoteLimits.MaximumDataFileBytes }
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[0], json);
Console.WriteLine(json);
void Measure(string name, Func<int> action) {
    for (int i = 0; i < 3; i++) GC.KeepAlive(action());
    var times = new List<double>(); var allocations = new List<long>();
    for (int i = 0; i < 7; i++) {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        int result = action();
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);
        times.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(allocated);
    }
    results.Add(new { name, medianMilliseconds = times.Order().ElementAt(3),
        medianAllocatedBytes = allocations.Order().ElementAt(3), milliseconds = times, allocatedBytes = allocations });
}
