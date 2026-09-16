# HWND facts acceptance boundary

Base: `f5bd7859654b955b2cf21978d660ca4c21bfd114`.

The latest branch already has a primary-file single writer, independent emergency export, ordered Dock membership, scoped finalization, and transactional native group restore. Those are substantive improvements and are preserved here.

## Changes

`StickyFactsReceiver` is the Pet-thread commit boundary for actual HWND geometry. Dock capture barriers, live batches, final commits, topology repair, restore, standalone reproject, and resize prepare their fact updates through this implementation. Each caller first validates its own complete member set, gesture identity and topology lifetime. All updates are prepared before any canonical state changes; they are consumed synchronously, without UI effects or asynchronous work between prepare and commit.

One commit updates canonical compatibility geometry, capture-time topology, effective facts and the session sequence. It replaces the repeated validate/mutate/validate-again sequences. Restore can reset both watermarks only when session creation is acknowledged. Temporary-rehome intent, IME, focus and deletion state survive that boundary. Resize follower commits intentionally apply geometry only, preserving content and flags from independently edited windows.

Content from a fresh event can still be accepted when its geometry belongs to a stale topology. Preferred placement is never changed by this receiver; only explicit placement actions can commit it. Physical/v10 fields remain derived compatibility/recovery data, so PC-3 is not declared fully closed.

The common boundary also rejects a snapshot belonging to a different note even when the accompanying HWND facts are valid. Previously the batch paths checked the facts' identity but could apply another note's content.

## Self-test repair

The existing Windows PC2 fixture called a removed repository constructor shape, populated a deleted restore field, invoked a removed restore preparation/failure path, and omitted newly added reflection arguments. It now constructs the repository directly and exercises the actual `DockRestoreOperation` completion policy: rejection leaves canonical/session state unchanged, releases the operation and queues hides for originally hidden windows. The tests no longer assert the obsolete destructive rejection policy.

## Validation

- Unmodified base: 502 direct MSTest method/DataRow calls passed, 0 failed.
- This node: 510 calls passed, 0 failed, including 8 new receiver behavior cases. Existing source-boundary guards were updated to follow the new owner; they are not HWND behavior tests.
- All Windows product and self-test C# sources compiled against official .NET Framework 4.8 reference assemblies with warnings treated as errors.
- Standard VSTest 17.11.1 still fails during host initialization (`TestEngine.ShouldRunInProcess`, `NullReferenceException`). The fallback invokes the real compiled MSTest methods and assertions. It is not a successful VSTest run.
- This Linux environment cannot run the WinForms/WPF native self-tests or verify mixed-DPI dragging, IME and display hotplug visually. Those remain explicit validation gaps.

## Remaining architecture work

`PetForm` still owns the convenience UI, Dock lifecycle and application shell. The next node moves resource lifetimes and state to actual component instances. Sharing a dictionary, adding another wrapper or moving methods among `PetForm` partials would not resolve that ownership problem. The native session also deep-copies content on geometry frames, and display traces currently default to synchronous logging; both warrant hot-path review.
