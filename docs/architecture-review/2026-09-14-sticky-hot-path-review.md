# Sticky interaction hot path

Follows workspace checkpoint `e5737c0`.

## Changes

`StickyNoteUiSnapshot.FromContentData` can reuse a previous immutable content snapshot. It compares actual scalar/list values, so an editor change, blank/edited list row or appearance preview is seen even before the 900 ms save timer updates ModifiedUtcTicks. It never reuses an initial placement-bearing snapshot. Visibility/topmost changes create a shallow immutable projection; content collections are read-only and can be shared safely. Geometry fields still do not cross back from WPF snapshots.

Applying identical content to the canonical model preserves the existing todo/schedule rows instead of allocating replacement objects on each geometry frame. No additional dirty flag, generation counter or invalidation protocol was introduced. The tradeoff is an O(content length) comparison; the model is still mutable, so revision-only caching would miss existing edit paths.

Detailed display/window-layer trace logging is opt-in with `PENNY_DISPLAY_TRACE=1` before launch. Previously the default appended log files synchronously on UI activity. Fatal/nonfatal error reporting is unchanged. The frequent WindowFacts formatters now return before constructing strings when tracing is disabled.

Eight discarded synchronous saves in sticky creation, visibility/collapse and group layout were switched to the existing autosave writer. Snapshots are still captured on the model thread before native effects are posted. Explicit import, export, exit and persistence recovery barriers remain in their existing shell/repository paths. The three creation modes now share one implementation while retaining their size and error messages.

## Evidence and limits

- 517 direct MSTest method/DataRow invocations passed, 0 failed, including seven new snapshot behavior/allocation cases.
- Windows core and self-test sources compiled as separate assemblies against official net48 references with warnings treated as errors.
- Isolated .NET 8 snapshot factory + canonical content benchmark: 100 todo rows, 10,000 geometry frames. Unconditional snapshot construction allocated 64,560,000 bytes; reuse allocated 0 bytes. One run took approximately 104.7 ms and 100.3 ms respectively. The baseline here is the same current factory without reuse, not a full executable benchmark of the old branch. Raw result: `2026-09-14-snapshot-allocation.json`.
- This is allocation evidence. It does not measure native HWND rendering, WPF layout, mixed-DPI frame pacing or disk latency on a user's Windows machine. No native FPS improvement is claimed.
- StickyWindowSession remains a large native adapter. This node changes its payload construction, not its IME or native event ordering. Those integration paths still require Windows validation.
