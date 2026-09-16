# Runtime owner overlap

Baseline `ab10b45`. Producer/consumer mirrors are necessary; a mirror becomes risky when it is accepted as an independent authority without the same transaction/lease validity.

| Runtime fact | Canonical authority | Mirror / cache / overlap | Retirement / open question |
|---|---|---|---|
| hosted membership | StickyUiHost session registry is actual existence on Sticky STA | StickyHostedRuntime membership is Pet routing intent/ack mirror, added before async create | preserve distinction between requested and actual; don't treat AddNote as proof of HWND |
| latest sequence | Session `_sequence` producer counter | HostedRuntime `_appliedSequences` accepted consumer watermark; PlacementRuntime Effective.WindowSequence separate geometry watermark | same number but different acceptance scope; session lease reset must cover both |
| effective geometry | actual HWND read, accepted in StickyPlacementRuntime | canonical physical/v10 mirrors; session LastSnapshot working-copy mirrors; activeDockCurrentFacts may contain **planned** targets | caches cannot replace HWND facts; PC-3 reader collapse prerequisite |
| current topology | DisplayTopologyRuntime.Current/Generation on Pet | Host locked `_currentTopology`, session `_topology`, detached event/plan capture topology | mirrors are not duplicate topology detectors; verify adoption and consumer revalidation |
| current Dock epoch | DockInteractionSession | StickyUiHost locked epoch mirror; capture/final closures store expected epoch | keep producer execution gate + consumer acceptance gate |
| pending plan | DockPlanMailbox under Gate | Pet active current facts and last-applied sequence are different derived/accepted views | don't merge pending target with accepted facts |
| current group | canonical DockGroupId/order/parent fields | active group ID snapshot; plan membership; Host expected session list | mutable canonical + gesture snapshot intentionally differ after split/merge; one lifecycle owner needed |
| drag source | DockInteractionSession.SourceNoteId | Pet `_activeNoteDragId`, plan.SourceNoteId and callback captures | strongest duplicate-state candidate; remove Pet duplicate only after all reset/rebase sites migrate |
| restore in progress | Pet pending group set + HostedDockRestorePreparation | per-member EnsureSession acknowledgements; Host native transaction temporary state | two stages, not the drag phase; one coarse result but separate preparation lifecycle |
| visibility | canonical Visible = product intended/saved state; HWND.IsVisible = actual effect | snapshot.Visible, temporary hidden bootstrap/rollback flags, restore preparation | cannot collapse temporary hidden state into durable Visible; success commits together |
| IME/focus/delete/exit | local WPF IME/focus actual; Pet owns pending-delete and exit decision | HostedRuntime bool/set projections; Session hide-after-composition and Host batch suppression | different state domains, not one generic shared boolean |

References: Features/StickyNotes/StickyHostedRuntime.cs:27–89; StickyPlacementRuntime.cs:24–41,84–132; DockWindowFacts.cs:94; Core/Display/DockInteractionState.cs; StickyUiHost.cs:106,111,289,541; PetStickyWindowCoordinator.cs:1365,1433,1740,2015; PetStickyDockCoordinator.cs:115,635,732.

## Precise characterization risks (not reproduced user failures)

1. `SynchronizeSessionLease` can lower the accepted hosted sequence after EnsureSession. `StickyPlacementRuntime.TryUpdateEffective` still rejects lower/equal sequence within the same topology. No session lease identity is embedded in WindowFacts. Before treating recreated-session handling as fully unified, test **old effective seq + acknowledged fresh session lower seq + same topology**. Verify all Remove/Clear paths, not just the hosted dictionary test.
2. `TryApplyDockFactsBarrier` mutates canonical data before per-member effective acceptance. `ApplyReprojectResult`, `ApplyDockBatchResult`, final commit and topology acceptance have different preflight/skip/update behavior. They can report content/sequence accepted while effective geometry acceptance differs. PC-2 should capture existing behavior; PC-3/6 must define a single commit boundary without silently changing golden semantics.
3. `RecordSequence` itself is assignment, not max/check. Safety resides in callers; EnsureSession is the deliberate lease-reset exception. It cannot simply be replaced by a global max without breaking recreation.
4. `AdoptTopology` assigns a provided snapshot; Host and Pet own stale checks. Do not credit the session method with rejecting old generations when it does not.
5. Failure policy is distributed: live logs, rebase stays suspended, final resets, restore hides and retains canonical data. No generic rollback manager is warranted by this inventory; transaction-specific policy must be explicit before ownership moves.

No confirmed correctness regression is claimed. Current automated gate and supplied human golden remain PASS. These are bounded next-stage test questions, not authorization to patch PC-0.5 or alter protocol in PC-1.

## PC-1 review amendment

Restore and topology reconciliation are not drag lifecycle. Pending restore gate belongs to a StickyFeatureController / DockRestore lifecycle candidate; standalone topology gate to DisplayRuntime + Sticky feature reconcile boundary; group topology gate to Display/Sticky topology reconcile boundary. _dockPlanMailbox live/final ownership is separable from its cross-lifecycle PlanSequence allocation used by topology reproject and Dock restore (WindowCoordinator.cs:686,2318). Keep that shared allocation boundary unresolved until PC-6; do not make hidden restore depend on current drag runtime.
