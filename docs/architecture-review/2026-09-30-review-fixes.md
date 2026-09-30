# 2026-09-30 thirteen-item review

Baseline: `115daad8ef5518ba8069c62d5bb9d113976d0763` on
`codex/simplify-dock-pipeline`. All thirteen findings were present in this
baseline. Changes preserve the existing settings/notes formats and ownership
boundaries. Each item was committed and pushed separately.

| # | Confirmed failure | Correction | Remote commit |
|---|---|---|---|
| 1 | A missing settings primary bypassed an existing `.bak`. | Read the backup when the primary is absent; retain unreadable-file preservation. | `5b420b5` |
| 2 | Runtime attachment could start reminders and writes while exit awaited persistence. | Defer composition through the persistence operation; resume after cancellation, discard on exit. | `b0b4361` |
| 3 | New snapshots could advance the Pet watermark ahead of pending Dock A/B captures. | Hold affected snapshots until the dependent commits settle, then capture fresh content and geometry. | `fce2f86` |
| 4 | Every clock tick consumed another due reminder and replaced the previous bubble. | Keep remaining due reminders in the durable schedule until the active due message closes; block reentrant delivery. | `1d8d6c4` |
| 5 | Late runtime attachment treated its attachment time as application launch. | Capture launch time before settings/shell loading and pass it to reminder restore. | `21276d8` |
| 6 | Rejected Dock begin still entered native drag/resize. | Deny header DragMove synchronously and constrain rejected WM_SIZING to its baseline. | `ea5c926` |
| 7 | Hold-to-detach discarded follower placement failure. | Return failure to the host so completion cannot commit the partial layout. | `5d4c8b8` |
| 8 | Failed rollback was cleared or reported as successful preparation/ACK. | Retain recovery targets, propagate NotAccepted, block persistence preparation/commit pumping while unresolved. | `368ddbb` |
| 9 | Startup Dock preparation rejection left expected first-render IDs pending. | Route preparation rejection through startup group recovery and settle the entire group. | `819d58a` |
| 10 | Failed preferred Pet placement never retried without another display event. | Retry at 250 ms intervals, at most three times; yield to dragging and cancel after user placement or success. | `ae7db13` |
| 11 | A failed topology capture consumed the final display hint. | Retry at most three times; retain the last valid snapshot/generation and reset the budget on a new external hint. | `3252b6f` |
| 12 | Keyboard availability ignored failure to register all focus hooks. | Require both focus and keyboard hooks; unwind partial registration and allow a later retry. | `d44a410` |
| 13 | Protected build tests only ran the unprotected self-test host. | Smoke-test the actual protected EXE in isolation from adjacent DLLs, checking resources, responsive Pet HWND and normal shutdown; gate output copying and Windows CI on success. | `613d4bd` |

## Follow-up checks

- Updated two exact-source guards for the launch constructor and initial topology
  retry generation. These initially failed CI despite successful compilation;
  the runtime behavior tests were not weakened.
- Removed seven Dock guards that only searched for retired private method names,
  mailbox names, or exact local call spelling. The corresponding ownership,
  follower-failure, topology-rebase, sequence, and live-input behavior remains
  covered by standard runtime tests and native self-tests. The remaining source
  guards are limited to platform boundaries, protocol shape, and gaps without
  a useful runtime probe.
- `3afdc9f` extends snapshot holding through failed rollback and releases fresh
  snapshots/ready commits after successful recovery, including when no gesture
  remains active. A native HWND probe covers this boundary.
- Regression coverage includes orphan backup load/save, composition deferral,
  dependent A/B queue membership, multiple due reminders and stale attention art,
  late launch restore, native begin rejection, detach follower failure, failed
  rollback retry, startup prepare rejection, preferred placement retry limits,
  topology capture recovery/exhaustion, and each partial focus-hook failure.
- Protected smoke uses separate PowerShell processes for raw and protected input;
  reflection-only loading cannot accidentally reuse the raw assembly identity.
  Name-based structural checks run on the raw input; resource and real process
  checks run on both artifacts. The report includes the tested EXE SHA-256.

## Validation

[Windows CI #203](https://github.com/692365092-png/penny-pet/actions/runs/36657135214)
validated code revision `9b8b30b35e412b1358f74d058e729829804b5545` after the
guard cleanup. The later `efa0840` documentation-only closeout does not change
the code under test:

- Full Release solution build and embedded art validation.
- 604 discoverable tests: 0 failures, 0 skipped; seven implementation-coupled
  Dock source guards were intentionally removed.
- Modular self-tests, including the native rollback/snapshot recovery probe.
- Ordinary single-file EXE build, responsive-window and normal-shutdown smoke.
- ConfuserEx protected build plus **actual protected EXE** resource, responsive
  window and normal-shutdown smoke. This is distinct from the unprotected
  semantic self-tests also executed by the build script.
- Dock baseline, render-cost experiment, SBOM and SHA-256 checksums.
- Local `git diff --check` passed; local and remote code trees matched after fetch.
  No .NET SDK is installed in this Linux workspace; executable results came from
  the Windows runner.

The optional R27 art comparison was skipped; no product release was published.
Synthetic HWND/failure injection covers these regressions. Physical monitor
hot-plug, actual driver failure, and third-party hook denial still need real
Windows environment acceptance; automated results do not claim that coverage.
