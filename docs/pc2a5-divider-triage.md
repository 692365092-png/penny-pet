# PC-2A.5 Human Gate — High-DPI Dock Divider Defect: Characterization / Triage

> Date: 2026-09-08
> Branch: `codex/project-system-experiment`
> Triage base: `ee3083bdd7e65168f8305a5b685f7c5ecf64ddd7` (PC-2A.5 closure)
> Type: **CHARACTERIZE / TRIAGE ONLY**. No production behavior change.

## 0. Summary

The high-DPI divider overlap/disappearance reported during Human Gate is
reproduced in the user's own `diagnostics.log` (2026-09-08 19:16:50–19:16:53,
DISPLAY2 @ 144 DPI / 150%). The evidence points to a **pre-existing latent
defect**, not a PC-2A.5 regression:

- The PC-2A.5 diff (`b9a6e79..ee3083b`) contains **zero changes** to any
  divider symbol (`ResizeHostedStickyDockDivider`,
  `CalculateDockMemberResizeTargets`, `ApplyDockTargets`,
  `CompleteHostedStickyDockDivider`, `DockDividerResize*`, `SetBounds`).
- Divider follower placement is computed from the resize-start snapshot with
  a **height-delta-only** formula and is never re-anchored to the source's
  current actual top; any top displacement during the gesture becomes a
  permanent overlap/gap.
- Divider follower `SetBounds` commands are posted per WM_SIZING frame on the
  ordinary `PostCommand` path (`DispatcherPriority.Normal`, FIFO, **no
  latest-wins coalescing**), and completion neither cancels pending live
  frames nor verifies the final stack seam.

## 1. Reproduction from the user's own log

Gesture on note `945ca87c…` (divider source), group order
`945ca87c → a5ae9f39 → b269f225`, all on `\\.\DISPLAY2` @ 144 DPI:

| time | note | physical rect | note |
|---|---|---|---|
| 19:16:50.813 | 945ca87c | (2133, **-170**, 480, 452) | gesture start: source top **off-screen** |
| 19:16:50.815 | b269f225 | (2133, 732, 480, 450) | seam was correct at start: -170+452=282, 282+450=732 |
| 19:16:50.855–51.161 | 945ca87c | WM_SIZING heights 452→952 | drag up |
| 19:16:51.297–51.402 | 945ca87c | heights 952→330 | drag down |
| 19:16:51.580 | 945ca87c | (2133, **0**, 480, 330) | final source: top moved +170, height 330 |
| 19:16:52.311 | a5ae9f39 | (2133, **161**, 480, 450) | follower landed 170 px too high |
| 19:16:52.312 | b269f225 | (2133, 610, 480, 450) | seam with a5ae9f39 (161+450=611) is fine |

Final invariant violation:

```text
source bottom = 0 + 330 = 330
follower top  = 161
overlap       = 330 - 161 = 169 px   (matches reported header/stack overlap)
```

The follower value is exactly the delta-only formula result:

```text
startSourceTop (-170) + finalSourceHeight (330) = 160   (+1 native rounding → 161)
```

The correct value is the source's final actual bottom (330). The +170 source
top displacement (off-screen correction / native top movement during the
gesture) is never propagated to followers.

## 2. A/B provenance vs PC-2A.5

- `git diff b9a6e79..ee3083b` touches 9 files; the divider handlers in
  `PetStickyWindowCoordinator` / `PetStickyDockCoordinator`,
  `StickyWindowSession.SetBounds`, `StickyNativeWindowBehavior`, and the Core
  divider geometry are **not among the changed regions**.
- PC-2A.5 added session invalidation / acceptance preflight to
  `StickyPlacementRuntime`, `EnsureSession` metadata, and five
  *geometry-result* consumers. The divider path consumes none of them.
- Conclusion: **expected pre-existing latent defect**. Human A/B
  (`b9a6e79` vs `ee3083b`, same 144-DPI divider drag) is still required to
  confirm; a diagnostics package with identical trace instrumentation is
  produced for both SHAs for that purpose.

## 3. Answers to the 8 audit questions

1. **Is WM_SIZING requested height always physical?**
   YES. `StickyNativeWindowBehavior.WindowHook` reads the WM_SIZING screen
   rect (physical), clamps with `DeviceScaleY()`-scaled min/max, and raises
   `DockDividerResizeEventArgs(requested)` in physical pixels. The Pet-side
   contract comment confirms "already-clamped physical HWND height".

2. **Started / Resizing / Completed height contract consistent?**
   YES, all physical. `Started/Completed` use `CurrentPhysicalHeight()`
   (real HWND rect); `Resizing` uses the clamped WM_SIZING height. Verified in
   the log: Started=452 physical, final Completed=330 physical.

3. **Same physical coordinate system for source actual rect and follower
   targets?**
   YES at capture time: `CaptureSnapshot()` derives canonical X/Y/W/H as the
   physical projection of the actual HWND rect, and `DockWindowFacts.FromData`
   / `FromSnapshot` both read those physical mirrors. `SetBounds` positions
   the HWND at physical pixels. No DIP/physical mixing found in this path.

4. **How many pending SetBounds per fast drag?**
   One `SetBounds` per *changed* follower per processed WM_SIZING frame
   (changed is diffed against canonical, which is updated at post time, so
   identical consecutive frames are skipped). No coalescing and no in-flight
   cap exist. A 100-frame drag can queue dozens of follower SetBounds at
   `DispatcherPriority.Normal`.

5. **Does ordinary PostCommand replay historical frames serially?**
   YES. `StickyUiThreadHost.Post` → `PostToDispatcher` →
   `dispatcher.BeginInvoke(DispatcherPriority.Normal, …)` per command, FIFO.
   There is no mailbox/coalescing for `SetBounds`.

6. **Can stale follower SetBounds still execute after mouse-up?**
   YES. `DockDividerResizeCompleted` posts one more follower batch and clears
   the Pet resize session, but the already-queued frames are not canceled;
   they run in FIFO order (final frame last, so FIFO normally orders it
   correctly). If any queued frame fails, its completion handler clears the
   resize session and reports; subsequent frames and the completed handler
   then find the session gone and stop moving followers — there is no
   post-completion seam verification.

7. **Observed symptoms (stale move after final frame / overlap / gap /
   off-screen / visibility / z-order)?**
   Overlap: CONFIRMED (169 px) and explained by the delta-only math. The
   off-screen source top (-170) is also observed directly at gesture start —
   its header is outside the screen, matching "header 被挡 / 消失" reports.
   Visibility / z-order changes: none observed in this trace set.

8. **Real HWND facts on disappearance:**
   Captured in the log at gesture boundaries (WindowFacts trace): DPI 144,
   gdi `\\.\DISPLAY2`, generation/target present. Gaps: the log did **not**
   previously record per-`SetBounds` requested-vs-actual facts, divider event
   sequences on the Pet side, or follower visibility — this triage adds those
   traces (`DockDividerGesture`, `DockDividerEvent`, `DockDividerFrame`,
   `DockTargetPosted/Completed`, `DockDividerCompleted`, `DockSetBoundsApplied`).

## 4. Root cause

Two independent defects in the divider follower pipeline:

### RC-1: follower targets are delta-only, anchored to the resize-start snapshot

`StickyDockGeometry.CalculateDockMemberResizeTargetsExact`:

```csharp
delta = finalSourceHeight - startBounds[source].Height;
follower.Top = startBounds[follower].Top + delta;
```

This preserves `follower.Top - source.Top = startSourceHeight` and assumes the
source top never moves. The user's log proves the source top moved -170 → 0
during the gesture, so the final stack is off by exactly that displacement
(170 px), even though every SetBounds succeeded. `ResizeHostedStickyDockDivider`
and `CompleteHostedStickyDockDivider` both compute from the frozen
`_activeHostedDockResizeFacts` and never re-anchor to the source's current
actual physical rect at frame time or at completion.

### RC-2: uncoalesced Normal-priority backlog with no final barrier

Per-frame `PostCommand(SetBounds)` has no latest-wins semantics (unlike the
Dock drag path, which uses `DockPlanMailbox` + `PostDockPlan`). Completion
posts a final batch from the same stale start facts, clears the session, and
never waits for or verifies follower HWND facts. A failed or superseded
intermediate frame strands followers mid-stack (gap/overlap/off-stack) with no
self-heal.

## 5. Proposed smallest correctness fix (not implemented in this triage)

Latest-wins divider follower effect + final exact barrier, mirroring the
existing Dock live-drag mailbox:

1. During WM_SIZING: coalesce follower frames — only the newest requested
   frame is retained; at most one deferred follower apply in flight.
2. On WM_EXITSIZEMOVE: capture the final **actual source physical rect**
   (top + height, not the stale start facts); drop/replace any pending live
   frame; apply one final follower layout re-anchored to the final source
   rect; then capture actual follower HWND facts and verify the seam
   invariant `next.Top == previous.Bottom` (≤2 px native tolerance).
3. A rejected/failed apply must not silently strand followers; the resize
   session must not be cleared until the final batch result is resolved, and
   the completed handler must verify rather than assume.

Forbidden shortcuts (per the runbook): Task.Delay/Sleep, ad-hoc debounce
constants, ignoring old results without a replace barrier, global sequence
changes, DPI formula edits, hide/show to mask overlap, dispatcher priority
hacks, or a post-mouse-up NormalizeAllDockGroups.

**Is a latest-wins divider mailbox / final barrier justified?** YES. RC-2 is
the same "many frames, one native effect queue" shape already solved by
`DockPlanMailbox`/`PostDockPlan` for drags; RC-1 additionally requires the
final apply to be re-anchored to the captured final actual source rect.

## 6. Diagnostics added by this triage (trace-only, no behavior change)

- `DockDividerGesture` (Started/Completed: source physical rect + scale)
- `DockDividerEvent` (session kind/seq/height)
- `DockResizePhysical` (added `top=` to the existing height trace)
- `DockDividerFrame` (Pet: source height + changed follower count)
- `DockTargetPosted` / `DockTargetCompleted` (divider follower SetBounds
  posted/completed, status + sequence)
- `DockDividerCompleted` (Pet final acceptance + height)
- `DockSetBoundsApplied` (every SetBounds: requested rect vs actual HWND
  facts, DPI, gdi, generation, sequence)

## 7. Next step

Human A/B reproduction on the two diagnostic packages
(`ee3083b`-based and `b9a6e79`-based) at 144/150% DPI. If `b9a6e79` also
reproduces, the defect is pre-existing and the RC-1/RC-2 closure can proceed
as a new narrow checkpoint. If `b9a6e79` does NOT reproduce, STOP and re-audit
PC-2A.5 for an indirect regression before any fix.
