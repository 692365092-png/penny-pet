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
