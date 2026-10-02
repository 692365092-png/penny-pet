# 2026-09-29 review follow-up

Baseline: `0763b2f` on `codex/simplify-dock-pipeline`.
The remote branch had advanced after the original seven-item review. Items 3 and 6
already had production fixes in that baseline; the other five required changes.

| # | Finding | Resolution | Commit |
|---|---|---|---|
| 1 | Optional art can permanently retain an interaction intent | Publish terminal failure after the second failed decode; release pending poke/reminder animation ownership without blocking or decoding on the UI thread. Transient loading still waits for the requested full cycle. | `9d6fe22` |
| 2 | Startup restores one note while waiting for every Dock member | Route startup groups through `DockRestoreOperation`, including hidden members, whole-batch fact acceptance and topology restart. Execute through the Sticky STA startup queue. Failed groups return to side tabs and leave the first-render wait set. | `41f8915` |
| 3 | Placement callback dereferences Sticky before attachment | Already guarded in `0763b2f`; add a pre-attachment callback regression probe. | `0763b2f` |
| 4 | Failed final capture/enqueue leaves provisional Dock state | Capture and enqueue before applying the provisional relation. Preserve gesture baseline rectangles in the completion; on capture/enqueue failure restore surviving HWNDs locally, without waiting for an ACK that will never arrive. | `7bd8a9a` |
| 5 | Conversation starts Notification before presentation acceptance | Remove the speculative animation. Carry acceptance per daily request, so a repeated in-flight request or rejected/stale result cannot claim Notification. Start the accepted animation after awaiting the conversation. | `01a7fcb` |
| 6 | Bubble close/ambient restore assumes Reminder runtime exists | Already guarded in `0763b2f`; add pre-attachment close and ambient callback probes. | `0763b2f` |
| 7 | Pet accepts a partial final Dock member set | Require `actual.SetEquals(expected)` before committing any content, relation, geometry or preferred placement. | `4900ba1` |

## Regression coverage

- Discoverable interaction tests: pending loading keeps its intent; terminal poke
  failure releases it; terminal Notification failure releases reminder attention.
- Native art self-test: the first failure remains retryable, the second is terminal.
- Native startup test: a visible child restores the hidden root and the entire group;
  an injected restore failure releases all expected first-render IDs.
- Native Dock test: missing capture session and rejected dependency queue restore
  real HWND bounds and leave no provisional split or pending commit.
- Native commit tests: partial Move, HorizontalResize, DividerResize, Detach and
  MergeAfter payloads leave serialized state and persistence revision unchanged.
- Conversation tests: blocked weather does not start an animation; only accepted
  presentation starts Notification; in-flight repeats return no animation.
- Shell callback probes execute placement, bubble close and ambient restore before
  Sticky and Reminder runtime attachment.

The native test fixture uses the existing Pet surface port to represent an absent
Pet HWND; Sticky windows and their placement/capture operations remain real.
Test failures now preserve full exception stack traces.

## Validation

[Windows CI #181](https://github.com/692365092-png/penny-pet/actions/runs/36571009858)
passed on `9ab0752e61a6dfd54e84cd9e85c20fa55b0f1040`:

- Full solution Release build and art validation.
- 603 discoverable tests passed; 0 failed; 0 skipped.
- Modular self-tests, including the new native regression probes.
- Single EXE build and release smoke tests.
- Dock baseline, render-cost experiment, SBOM, checksums and artifact upload.
- `git diff --check` passed locally. No .NET SDK is installed in this Linux workspace;
  execution results above come from the Windows runner.

The optional R27 art comparison was intentionally skipped. No product release was
published. Physical mixed-DPI, hotplug and IME interaction remain manual acceptance
boundaries.
