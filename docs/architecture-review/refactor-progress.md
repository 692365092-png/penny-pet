# Refactor progress

Implementation follows the approved R01–R27 roadmap on `codex/simplify-dock-pipeline`. Each item is pushed separately. This file records scope changes and verification limits rather than treating every checklist entry as automatically finished.

| Item | Implemented change | Verification |
| --- | --- | --- |
| R01 | Replace the removed release self-test entry with an external executable smoke test; retain modular self-tests. | [932df16](https://github.com/692365092-png/penny-pet/commit/932df16d36e3d6e5b8a69262c6efd1c8d4cc5562), [Windows CI passed](https://github.com/692365092-png/penny-pet/actions/runs/35800599831). |
| R02 | Exclude tool argument parsing and self-test source directories from product builds; inspect the actual release for test/tool entry points and fixtures. | [16d9e84](https://github.com/692365092-png/penny-pet/commit/16d9e845c5ae764f34178f809c17a85b4979d55b), [Windows CI passed](https://github.com/692365092-png/penny-pet/actions/runs/35800808231). Window interaction exercise methods remain for the R15 view/test-driver extraction. |
| R03 | Inline the readiness conjunction; remove the unused flag parser and product arguments. | [89b480d](https://github.com/692365092-png/penny-pet/commit/89b480d868c6d613781fdb89ca8ad83dc3942807). One truth-table unit test and the duplicate self-test JSON field were removed with the wrapper. The actual startup boundary guard and release smoke remain. |
| R04 | Record executable startup/shutdown, memory and GUI handles; run the existing 10/50/100-note managed microbenchmark in Windows CI. | Reports are uploaded as `Penny-test-reports`. These are automated baseline observations; native drag latency, visible follower lag and real IME interaction still require their dedicated Windows scenarios. |
| R05 | Send one shared reminder update per model change; refresh countdown text with one Sticky STA timer; remove countdown polling from Pet STA and avoid geometry snapshots for reminder-only updates. | A native self-test checks shared-list parity, independent ticking while the caller is blocked, selection/body preservation, hidden/empty banners and batch close. The existing `reminder_banner_tick_throttled_ok` report field now checks the actual host clock. |

R03 passed [Windows CI #57](https://github.com/692365092-png/penny-pet/actions/runs/35801035266). R04 passed [Windows CI #58](https://github.com/692365092-png/penny-pet/actions/runs/35801276643), including baseline capture and upload.

## Reading the baseline

- `penny-release-smoke.json` records the tested revision, OS, processor count, time to the first responsive pet HWND, working/peak memory, CPU time, GUI handles and normal shutdown time. It does not claim that all notes have restored or that the first pixels have appeared. One run is not a percentile distribution.
- `penny-dock-baseline.json` records warmups, raw repeated timings and allocations for 10, 50 and 100 notes. It is the existing managed comparison of captured facts/group lookup paths, not the native Dock gesture executor. The `baseline` field identifies the historical comparison; `sourceRevision` identifies this run.
- Compare repeated samples on the same Windows setup and dataset. Hosted runner timing is observational; no arbitrary performance pass/fail threshold is introduced.
- The existing structural/core tests and modular native self-tests remain the functional gates. A green smoke test does not substitute for mixed-DPI dragging, focus/IME, hotplug or slow-disk scenarios.

## R05 correction discovered in the implementation

Every sticky currently displays the same global list of up to five reminders. `SourceNoteId` links reminder lifetime to a note; it is not a display filter. Filtering banners by `SourceNoteId`, as suggested in the roadmap, would change current functionality.

R05 preserves the shared list: send it once to StickyHost when the schedule changes, fan out locally on Sticky STA, and update countdown labels locally. The global banner remains available on every note.
