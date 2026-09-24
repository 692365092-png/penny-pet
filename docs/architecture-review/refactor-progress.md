# Refactor progress

Implementation follows the approved R01–R27 roadmap on `codex/simplify-dock-pipeline`. Each item is pushed separately. This file records scope changes and verification limits rather than treating every checklist entry as automatically finished.

| Item | Implemented change | Verification |
| --- | --- | --- |
| R01 | Replace the removed release self-test entry with an external executable smoke test; retain modular self-tests. | [932df16](https://github.com/692365092-png/penny-pet/commit/932df16d36e3d6e5b8a69262c6efd1c8d4cc5562), [Windows CI passed](https://github.com/692365092-png/penny-pet/actions/runs/35800599831). |
| R02 | Exclude tool argument parsing and self-test source directories from product builds; inspect the actual release for test/tool entry points and fixtures. | [16d9e84](https://github.com/692365092-png/penny-pet/commit/16d9e845c5ae764f34178f809c17a85b4979d55b), [Windows CI passed](https://github.com/692365092-png/penny-pet/actions/runs/35800808231). Window interaction exercise methods remain for the R15 view/test-driver extraction. |
| R03 | Inline the readiness conjunction; remove the unused flag parser and product arguments. | [89b480d](https://github.com/692365092-png/penny-pet/commit/89b480d868c6d613781fdb89ca8ad83dc3942807). One truth-table unit test and the duplicate self-test JSON field were removed with the wrapper. The actual startup boundary guard and release smoke remain. |
| R04 | Record executable startup/shutdown, memory and GUI handles; run the existing 10/50/100-note managed microbenchmark in Windows CI. | Reports are uploaded as `Penny-test-reports`. These are automated baseline observations; native drag latency, visible follower lag and real IME interaction still require their dedicated Windows scenarios. |
| R05 | Send one shared reminder update per model change; refresh countdown text with one Sticky STA timer; remove countdown polling from Pet STA and avoid geometry snapshots for reminder-only updates. | A native self-test checks shared-list parity, independent ticking while the caller is blocked, selection/body preservation, hidden/empty banners and batch close. The existing `reminder_banner_tick_throttled_ok` report field now checks the actual host clock. |
| R06 | Cache the ordered side-tab projection separately from layout; remove string signatures and recursive repository refresh on edge changes; preserve unchanged strip controls and compare type/icon fields too; scan visible note bounds once for both strips. | Extend native projection checks to cover unchanged refresh during drag preview, global drop-index updates, type-only changes, reorder and empty strips. Windows CI validates the actual forms; visible mixed-DPI dragging remains a manual scenario. |
| R07 | Move resource-pack/cache encoding and validation reports into `Tools/Art/PetArtWriter.cs`, compiled only by PennyPet.Tools. Remove the unreachable packaged-generator cache reset and manifest materialization fallback. Runtime retains decoding, external art loading and playback. | Existing builds regenerate both resource formats; CI additionally exercises the validation command and inspects the formal EXE for writer types/methods. Palette, delta and compression algorithms are unchanged. |
| R08 | Introduce one Pet-STA ReminderRuntime owning the timer, due consumption, accepted pre-alert identity, schedule changes/link reconciliation and attention-request lifetime. PetForm retains dialogs and implements a narrow presentation interface; Core ReminderRules has no mutable runtime state. Remove catch-all restore-and-overwrite recovery. | New native self-test exercises restoration, suppressed/accepted pre-alerts, stale editing, linked cancellation, repeated/reentrant ticks, real timer delivery, quiet-mode reminders, out-of-order art completion, dismissal/cancel-all and Stop. Windows CI is the execution gate. |
| R09 | ConversationRuntime owns daily/daypart/small-talk scheduling and ledger persistence; PetForm presents conversation messages and animation results. Daily attempts take one preference snapshot and use owner-context invalidation instead of a lock. | New integration self-test checks in-flight coalescing without premature ledger consumption, original-daypart commit, accepted/rejected presentation, date/daypart rollover, quiet mode, shutdown and old/new completion order. Existing catalogue, weather-cache and speaking-quota cases remain. |
| R10 | InteractionRuntime owns playback, hover hysteresis, pointer/drag state, typing, poke intent and reminder/exit transitions. PetForm supplies native input, one clock and read-only frame presentation. Ready art reads bypass the decoder gate. | Ten deterministic runtime tests cover fallback/full cycles, drag/DPI rebase, capture loss, hover, typing/editor focus, priority, reminders, exit and Stop. Native self-tests exercise the real runtime and ready-clip reads while another thread holds the decoder gate. Windows CI is the execution gate. |

R10 passed [Windows CI #65](https://github.com/692365092-png/penny-pet/actions/runs/35857017185), including the ten interaction tests, native ready-art check and formal EXE smoke.

## R11 — implementation complete, Windows validation pending

First checkpoint introduced the memory model and snapshot writer. The second checkpoint removes the transitional repository: `StickyModel` owns the canonical collection, note creation/removal, tab ordering, Dock member removal and detached snapshot capture. It has no store, file path, writer, window, dispatcher or Drawing dependency. `StickyStore` owns loading/recovery, the serial writer and independent emergency export. It returns a loaded model to the owner and retains no live model field. Background writes receive detached requests only.

`StickyFeature` owns the model, Store and attached Windows workspace. It accepts owner-side commands, captures save snapshots, and publishes an import/restore replacement only after the existing backup/primary commit succeeds. Startup still explicitly attaches and starts the UI runtime. Existing disk format, future-schema blocking, salvage/backups, hidden Dock slots and placement preferences are unchanged.

Workspace no longer has a PetForm reference. Its dependencies are read-only Pet surface facts, concrete presentation operations and reminder projection/actions; startup, typing and exit are explicit events. Modal management and backup dialogs remain in Pet presentation, not behind writable shell fields. Owner-context posts replace direct Form.BeginInvoke calls and ignore work after workspace disposal. StickyFactsReceiver accepts only StickyModel, without a file or writer dependency. The three redundant codec-forwarding methods were removed; UI callers use Core codec functions directly.

Five new tests exercise memory-only commands, hidden-tab ordering without changing Dock slots, hidden Dock-member removal, deep snapshot isolation and a blocked store while the live model changes. Existing real-file recovery, future-schema, import/restore and serial-writer tests remain the compatibility gates.

The new native `sticky_feature_boundary_ok` check constructs the actual feature/workspace without PetForm or real I/O. It checks canonical model identity, accepted content, reminder actions, lifecycle notifications, manager command wiring, deferred execution and suppression after disposal. Existing PC2 native scenarios now attach through the composition entry and inspect the writer through Store.

CI #67 compiled successfully but correctly rejected System.Drawing in the new Core model. Its creation API now takes plain x/y coordinates; the original platform boundary guard remains intact. Synchronous import/exit persistence barriers are preserved in this item; R17/R25 own their asynchronous redesign. SideTabs and live Dock execution ownership are still R21–R24, not claimed complete here.

R09 passed [Windows CI #64](https://github.com/692365092-png/penny-pet/actions/runs/35853802433), including runtime integration, release smoke and artifact checks.

R08 passed [Windows CI #63](https://github.com/692365092-png/penny-pet/actions/runs/35827162500) after the full-restore call-site fix, including the new runtime self-test and release smoke.

R07 passed [Windows CI #61](https://github.com/692365092-png/penny-pet/actions/runs/35825872147), including tool output validation and release generator exclusion.

R06 passed [Windows CI #60](https://github.com/692365092-png/penny-pet/actions/runs/35825579063), including native side-tab checks, release smoke and artifacts.

R05 passed [Windows CI #59](https://github.com/692365092-png/penny-pet/actions/runs/35801921499).

R03 passed [Windows CI #57](https://github.com/692365092-png/penny-pet/actions/runs/35801035266). R04 passed [Windows CI #58](https://github.com/692365092-png/penny-pet/actions/runs/35801276643), including baseline capture and upload.

## Reading the baseline

- `penny-release-smoke.json` records the tested revision, OS, processor count, time to the first responsive pet HWND, working/peak memory, CPU time, GUI handles and normal shutdown time. It does not claim that all notes have restored or that the first pixels have appeared. One run is not a percentile distribution.
- `penny-dock-baseline.json` records warmups, raw repeated timings and allocations for 10, 50 and 100 notes. It is the existing managed comparison of captured facts/group lookup paths, not the native Dock gesture executor. The `baseline` field identifies the historical comparison; `sourceRevision` identifies this run.
- Compare repeated samples on the same Windows setup and dataset. Hosted runner timing is observational; no arbitrary performance pass/fail threshold is introduced.
- The existing structural/core tests and modular native self-tests remain the functional gates. A green smoke test does not substitute for mixed-DPI dragging, focus/IME, hotplug or slow-disk scenarios.

## R05 correction discovered in the implementation

Every sticky currently displays the same global list of up to five reminders. `SourceNoteId` links reminder lifetime to a note; it is not a display filter. Filtering banners by `SourceNoteId`, as suggested in the roadmap, would change current functionality.

R05 preserves the shared list: send it once to StickyHost when the schedule changes, fan out locally on Sticky STA, and update countdown labels locally. The global banner remains available on every note.

## R08 behavior corrections

- A due reminder is removed before invoking presentation, so repeated ticks or a nested message loop cannot deliver it twice.
- Closing a due bubble, cancelling all reminders or stopping the runtime invalidates pending notification animation. Resource completion alone cannot revive a dismissed notification; the newest active intent still starts on Pet STA.
- Editing the currently displayed pre-alert closes the obsolete presentation before replacing it. The model remains authoritative if a reminder expires while its edit dialog is open.
- Reminder restoration no longer catches every exception and saves an empty schedule. Settings parsing/recovery remains at its existing persistence boundary; expired and orphaned reminders still follow the existing cleanup policy.
- Shared art-load locking is not fixed by this extraction; R12 owns resource task sharing and lock scope. Settings/sticky saves still use their existing writers; R17/R25 own asynchronous persistence and shutdown changes.

R08's first Windows run caught a remaining call to the removed reconciliation method in full backup restore. The follow-up routes it through `ReminderRuntime.ReconcileNoteLinks` and extends the runtime self-test with actual note-model replacement and orphan cleanup. The initial failed run is [CI #62](https://github.com/692365092-png/penny-pet/actions/runs/35826916959).

## R09 scope and behavior corrections

ConversationRuntime owns the ledger and the three existing content components; these components retain useful selection/composition logic rather than being replaced by another queue. PetForm no longer captures groups of preference delegates or directly consumes/persists dayparts. Only the accepted opening callback records the original request's date and daypart. An already-pending request is handled without consuming anything.

Settings changes, quiet-mode changes, reminder takeover, the poke Easter egg and shutdown invalidate pending daily attempts. Completion checks also compare the captured preferences and current local date/daypart before presentation. A prior completion cannot release a newer attempt's in-flight state. Weather HTTP/cache ownership and request reuse remain in PetWeatherSource; forecast errors use the existing no-weather content fallback. Existing bubble priority and accepted-presentation semantics remain the publication boundary.

## R10 ownership and behavior corrections

PetForm no longer forwards writable row/frame/typing/attention properties into a controller, and there is only one pointer/drag state. Core retains pure animation selection rules. InteractionRuntime exposes semantic input commands and read-only state; Shell retains native capture, window movement, DPI fact collection and bitmap presentation. Stable hover advances on the existing animation tick, so the separate hover timer is removed. DPI handoff rebases the runtime's pointer origins explicitly. Capture loss ends the gesture without synthesizing a poke.

Unavailable optional art now plays the ready Idle loop while preserving the current intent. Completion is observed by the current runtime state, so an abandoned poke is not revived by a later asset load. A reminder can preempt protected small talk; protection still applies to incidental dragging. Animated dragging no longer momentarily picks a random idle clip at a loop boundary. Async conversation results schedule animation from their acceptance time and cannot restart it after drag cancellation, reminder takeover or shutdown.

Ready clip publication uses release/acquire reads and writes. UI readiness, timing and bitmap reads do not acquire the decoder gate or call a decoding getter. The mandatory startup Idle load is explicit before background work starts. Resource-task sharing, background decode/disposal ownership and memory policy remain R12; scaled bitmap construction remains R19. Goodbye is warmed in the background and has a two-second missing-art wait budget, after which shutdown can finish instead of waiting indefinitely for damaged optional art. A ready goodbye still plays its full cycle.

No native drag-latency, mixed-DPI visual or IME performance improvement is claimed from source checks alone. Windows CI remains required for this change; the local environment has no .NET SDK.

R11.1 initial [CI #66](https://github.com/692365092-png/penny-pet/actions/runs/35890263969) compiled product code but rejected two new test fixtures that assumed parameterless todo/schedule constructors. The follow-up uses the actual text/state/date constructors.
