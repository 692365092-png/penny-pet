using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PennyPet;

const int iterations = 10000, warmups = 2, samples = 5;
var results = new List<object>();
foreach (int count in new[] { 10, 50, 100 })
{
    var random = new Random(417);
    var notes = Enumerable.Range(0, count).Select(i => new StickyNoteData {
        Id = "note-" + i, Visible = true, AlwaysOnTop = i % 2 == 0,
        DockGroupId = "group-" + i / 10, DockGroupOrder = i % 10,
        ModifiedUtcTicks = random.Next()
    }).ToList();
    var view = notes.AsReadOnly();
    var runtime = new StickyPlacementRuntime();
    foreach (var note in notes)
        runtime.TryUpdateEffective(note.Id, new WindowFacts(note.Id, "target", "display",
            new PhysicalRect(100, 100 + note.DockGroupOrder * 230, 300, 230), 96, 1, 1));

    List<StickyNoteData> GetAll()
    {
        var sorted = new List<StickyNoteData>(notes);
        sorted.Sort((left, right) => right.ModifiedUtcTicks.CompareTo(left.ModifiedUtcTicks));
        return sorted;
    }
    StickyNoteData Find(string id)
    {
        foreach (var note in notes)
            if (String.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase)) return note;
        return null;
    }
    Dictionary<string, DockWindowFacts> Before()
    {
        // Exact old GetAll -> note IDs -> repository Find -> effective facts path.
        var ids = new List<string>();
        foreach (var note in GetAll()) if (note != null) ids.Add(note.Id);
        var facts = new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            var note = Find(id);
            if (note == null) continue;
            var actual = DockWindowFacts.FromWindowFacts(runtime.GetEffective(note.Id), note.Visible, note.AlwaysOnTop);
            if (actual != null) facts[id] = actual;
        }
        return facts;
    }
    Dictionary<string, DockWindowFacts> After(IEnumerable<StickyNoteData> source)
    {
        // Same body as StickyDockController.CaptureDockFacts; no native HWND work.
        var facts = new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);
        if (source == null) return facts;
        foreach (var note in source)
        {
            var actual = note == null ? null : DockWindowFacts.FromWindowFacts(
                runtime.GetEffective(note.Id), note.Visible, note.AlwaysOnTop);
            if (actual != null) facts[note.Id] = actual;
        }
        return facts;
    }
    var before = Before();
    var after = After(view);
    if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var f) ||
        pair.Value.X != f.X || pair.Value.Y != f.Y || pair.Value.Width != f.Width ||
        pair.Value.Height != f.Height || pair.Value.Visible != f.Visible || pair.Value.TopMost != f.TopMost))
        throw new InvalidOperationException("Facts differ between implementations.");
    var beforeGroup = StickyDockGroups.GetVisibleGroup(GetAll(), notes[0]);
    var afterGroup = StickyDockGroups.GetVisibleGroup(view, notes[0]);
    if (!beforeGroup.Select(n => n.Id).SequenceEqual(afterGroup.Select(n => n.Id)))
        throw new InvalidOperationException("Group order differs between implementations.");

    Measure("capture-before-" + count, () => Before().Count);
    Measure("capture-after-" + count, () => After(view).Count);
    Measure("group-before-" + count, () => StickyDockGroups.GetVisibleGroup(GetAll(), notes[0]).Count);
    Measure("group-after-" + count, () => StickyDockGroups.GetVisibleGroup(view, notes[0]).Count);
}
string json = JsonSerializer.Serialize(new {
    sourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA"),
    baseline = "b0efb1375e3ab545197ec709c1057cd8fe4c2836",
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    iterations, warmups, samples, results,
    scope = "Managed hot-path microbenchmark; excludes dispatcher latency, native placement and rendering. Not an FPS measurement."
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[0], json);
Console.WriteLine(json);

void Measure(string name, Func<int> action)
{
    int Run() { int checksum = 0; for (int i = 0; i < iterations; i++) checksum += action(); return checksum; }
    for (int i = 0; i < warmups; i++) GC.KeepAlive(Run());
    var times = new List<double>(); var allocations = new List<long>();
    for (int i = 0; i < samples; i++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        int checksum = Run();
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(checksum);
        times.Add(elapsed); allocations.Add(allocated);
    }
    results.Add(new { name, medianMilliseconds = times.Order().ElementAt(samples / 2),
        medianAllocatedBytes = allocations.Order().ElementAt(samples / 2), milliseconds = times, allocatedBytes = allocations });
}
