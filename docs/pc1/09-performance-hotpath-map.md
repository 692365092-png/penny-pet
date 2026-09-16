# PERF-OBS-01 hot-path map — no optimization

Baseline `ab10b45`. Human observation from master plan: occasional reduced smoothness with SideTabs visible, nonblocking. This is a **static call/operation map**, not a profile and not a proven root cause. No timing claim or performance patch is made.

## All production PositionNoteTabs direct callers

| Caller / reference | Frequency | Trigger |
|---|---|---|
| PetForm.cs:443 LocationChanged lambda | HOT | ordinary Pet drag/reposition; suppressed during `_petDpiDragHandoffActive` |
| PetForm.cs:449 SizeChanged lambda | WARM; may burst | scale/art size/native DPI resize; same handoff suppression |
| PetForm.cs:612 OnDpiChanged tail | WARM | one explicit final reposition after handoff |
| PetForm.cs:689 DisplayTopologyChanged tail | COLD/burst | settled topology change after Pet and Sticky reconciliation |
| PetDisplayRuntime.cs:182 TryPlacePetAtPreferred | COLD | restore/return/programmatic preferred placement |
| PetDisplayRuntime.cs:229 TryPlacePetDefault | COLD | default/recovery placement |
| PetDisplayRuntime.cs:410,422,469 ReconcilePetDisplayPlacement | COLD/burst | current/temporary/preferred display branches |
| PetStickyWindowCoordinator.cs:2740 RefreshNoteTabs same signature | WARM | note projection refresh without control rebuild |
| PetStickyWindowCoordinator.cs:2773 RefreshNoteTabs changed signature | WARM | after structural repartition/control replacement |

Pet mouse move drives native/Form location; there is no separate per-animation-tick direct PositionNoteTabs call. Raw source-string test references are excluded from this caller list. Note changes/startup/menu/visibility lead through RefreshNoteTabs, not an invented second movement timer.

## Typical unchanged-DPI, unchanged-split Pet move

```
PetMouseMove -> Pet location effect -> LocationChanged
 -> PositionNoteTabs (reentrancy/handle/disposed checks)
 -> TryGetPetDerivedDisplayContext
 -> CurrentTopologySnapshot + CapturePetWindowFacts (actual HWND, sequence++)
 -> topology surface lookup + SideTabPhysicalMetrics.ForDpi
 -> calculate physical overlap + edge-aware desiredLeftCount
 -> count comparison (split unchanged)
 -> ApplyPhysicalMetrics on both strip forms (same DPI early return)
 -> ShowNear twice -> pure CalculateSideTabLocation -> Location assignment
 -> ApplyNoteTabZOrder -> visible-note intersection scan per strip
 -> covered bool unchanged: no TopMost setter / BringToFront
 -> RepositionCurrentBubble (separate follower work)
```

References: WindowCoordinator.cs:2642,2682,2777,2792,2821; StickyNoteTabs.cs:116,175,220. `new Rectangle`, Point and DockRect are value operations, not automatically heap allocations.

## Existing gates versus remaining synchronous work

| Condition | Already avoided | Still performed |
|---|---|---|
| left/right split unchanged | SetNotes/control rebuild | facts read, layout arithmetic, strip location assignments, intersection scans |
| refresh signature unchanged (ID/title/color/DPI/left count) | SetNotes | hidden projection list and StringBuilder were already built before comparison; PositionNoteTabs and z-order policy still called |
| DPI unchanged | strip form ApplyPhysicalMetrics early return, hence no per-tab font/layout pass | metrics value object creation + strip reposition |
| DPI changes | no cumulative scaling; font replaced from 8.5pt logical reference exactly once | owned pixel Font rebuild, tab bounds, OnResize Region replacement, invalidation/layout |
| coverage unchanged | TopMost / BringToFront writes | canonical visible-note enumeration and rectangle intersection |
| Pet DPI drag handoff active | intermediate LocationChanged/SizeChanged follower work | one explicit final PositionNoteTabs + Bubble call remains |
| no controls | ShowNear returns | outer facts/count work can still occur if form/handle exists |

Tab `ApplyPhysicalMetrics` itself invalidates/layouts whenever called, but **ordinary same-DPI moves do not reach it** because the strip form gate returns. Tab Region recreation occurs on control Resize, not every Pet LocationChanged. No evidence justifies a new font cache for unchanged DPI.

## Allocation/effect budget by frequency

| Frequency | Observed allocations/effects | Evidence |
|---|---|---|
| ordinary move | new WindowFacts from native capture; SideTabPhysicalMetrics; GetAll list copies **and ModifiedUtcTicks sorting** for each visible-strip intersection scan; value rectangles/points; synchronous native read/location updates | CapturePetWindowFacts; TryGetPetDerivedDisplayContext; IsStripCoveredByVisibleSticky; repository GetAll:459 |
| note refresh, even unchanged signature | hidden canonical list, SideTabSnapshot list/items, StringBuilder + signature string | RefreshNoteTabs |
| actual split/note signature change | left/right GetRange lists; old control list; dispose/create controls/tooltips; Font and Region construction | RefreshNoteTabs -> SetNotes |
| DPI change | per-tab replacement pixel Font from actual metrics; control resize -> GraphicsPath/Region; invalidate | StickyNoteTabs.cs:1184,1200,1216 |
| icon rendering/cache construction | Bitmap allocations and raster masks | StickyNoteTabs icon helpers around 1381–1485; not ordinary movement |
| coverage transition | TopMost/BringToFront plus log formatting/IO | ApplyNoteTabZOrder |
| trace enabled and logger initialized | string formatting, lock, capacity check, append | DisplayDiagnostics.Trace -> ApplicationDiagnostics.WriteWindowLayerEvent |

Diagnostics are **bounded**: ApplicationDiagnostics.EnsureLogCapacity rotates the current log at >1 MiB to one `.previous` file (see actual filename in implementation), not unbounded append. Trace calls still build details before the logger's early return, and active logging is synchronous under LogGate. DisplayDiagnostics is enabled unless PENNY_DISPLAY_TRACE=0; actual IO also requires ApplicationDiagnostics initialization. Do not claim every trace appends when initialization is absent (e.g. some probes).

## What to measure later (PC-10, not now)

Compare identical monitor/DPI/note counts with strips empty/visible, fixed versus changing split, trace on/off. Measure facts capture, GetAll/intersection scan, native reposition, paint, and callback fan-out separately. Record bounded counters/distributions, no infinite trace append. H-PERF-1 is plausible from this synchronous route, but compact dimensions alone are not an established cause. No new cache or repaint throttling was added.
