# Target ownership proposal — not an implementation plan approved to execute

Derived from maps 02–10. A target is justified by state, lifecycle, invariant or native effect ownership, not a reduced line count. Keep existing real owners; no generic framework/DI/router stack.

| Current owner | Candidate target | State / methods to move together | Must NOT move | Thread | Expected deletion / risk / prerequisites |
|---|---|---|---|---|---|
| PetDisplayRuntime partial + PetForm + topology glue in Sticky coordinator | DisplayRuntime (name tentative) | topology subscription/generation access, Pet effective facts and placement flags; CapturePetWindowFacts, Pet rehome/return lifecycle | repository, Dock membership, Sticky Window, SideTab controls | Pet STA; native Pet edge callable only there | remove duplicate scattered display bookkeeping, not existing DisplayTopologyRuntime; PC-3 before PC-4; risk source DPI/rebase ordering |
| Pet Dock fields + DockInteractionSession + mailbox + acceptance callbacks | DockInteractionRuntime | source identity, active IDs/facts/hold, epoch binding, pending/final plan, expected batch acceptance, reset/rebase/final lifecycle | WPF Window, repository IO implementation, editor/SideTab UI; keep canonical changes at explicit product boundary | Pet STA, detached plans to Sticky | delete duplicate `_activeNoteDragId` vs Session.SourceNoteId and externally mutable gesture fields only after one API owns transitions; PC-2/3/5 before PC-6 |
| PetStickyWindowCoordinator partial | StickyFeatureController (only if genuine lifecycle boundary remains) | create/show/hide/delete/import/restore orchestration and runtime membership lifecycle; one completion contract | Dock algorithm, display topology generation, repository physical IO, Window refs | Pet STA | remove duplicated lifecycle callbacks from PetForm, not forward every private method through a wrapper; PC-6 before PC-7; restore/exit failure ordering is risk |
| Pet tabs fields and Refresh/Position/ZOrder | SideTabController | projection/signature/layout/coverage caches and form lifecycle; refresh/repartition/reposition | canonical mutable notes, hosted WPF, Dock core rules, Pet placement decisions | Pet WinForms STA | remove tab bookkeeping from shell; after core ownership, PC-9; performance should be measured, not assumed improved |
| StickyUiHost | retain StickyUiHost | registry, whole native batch orchestration, routing and typed forwarding | durable target choice, repository/canonical ownership | Sticky STA; locked config mirror setters | narrower payload plumbing after PC-5, no new controller above every command |
| StickyWindowSession | retain narrow Session | one HWND, native placement/correction/rollback, facts, local seq, IME/events | preferred/v10/physical fallback selection, durable policy | Sticky STA | policy methods move only after replacement intent exists; no disposal/event regressions; PC-3/4 before PC-5 |
| StickyPlacementRuntime + geometry acceptance scattered in Pet | retain/strengthen placement runtime invariant | accepted effective facts, lease-aware validation boundary, temporary flags | persistence service, Window, current topology detector | Pet STA | consolidate duplicated acceptance logic after behavior tests; don't merge planned targets into effective; PC-3/5 |
| PetStartupCoordinator partial | StartupRuntime only if queue/readiness lifecycle warrants | startup work timer/phase/expected-rendered sets and ready transition | Loading Window thread, PetArt resource ownership, Sticky windows | Pet STA, existing temporary loading STA separate | optional PC-7 candidate; keep PetStartupRules pure gate; no new startup framework |

## Non-moves

- PetForm retains composition root, WinForms callbacks, shell HWND and high-level product integration until an owner with a real invariant exists.
- PetAnimationController, PetBubbleCoordinator, PetDailyContentCoordinator, PetReminderCoordinator, PetContextMenu already own real state/APIs. Do not recreate them merely to match target names.
- StickyUiThreadHost remains thread/Dispatcher/post/shutdown only. Do not put note state there.
- Core owns pure planning/rules/models/migration, never HWND/Screen/Dispatcher/repository IO.
- Legacy decoder/fallback fields remain until their live readers have migrated and historical/future-schema fixtures pass.

## Hypothesis disposition

H-PET-1 supported for the 13 partials, with real object owners nested behind many fields. H-DOCK-1 supported: duplicate Pet-side lifecycle state is the extraction target, not abolition of cross-STA gates. H-GEO-1 is a destination, not current truth; v10 height is still an active planner input. H-TEST-1 supports decomposition of evidence domains, not coverage deletion. H-PERF-1 remains unmeasured; no ownership extraction can claim a speedup without before/after measurements.
