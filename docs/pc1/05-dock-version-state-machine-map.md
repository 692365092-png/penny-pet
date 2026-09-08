# Dock version and state-machine map

## Independent version dimensions

| Concept | Creator / increment | Comparer | Concrete rejected failure | Lifetime / merge decision |
|---|---|---|---|---|
| TopologyGeneration | DisplayTopologyRuntime CaptureInitial=0; TryCapture increments on semantic change only | Host ApplyDockPlan, Session AdoptTopology, WindowFactsVersionRules, Pet IsTopologyCurrent/barriers | result/plan projected against removed/changed monitor topology | app topology lifetime; cannot merge with gesture version |
| InteractionEpoch | DockInteractionSession NextEpoch: begin, rebase, finalizing/restart, reset | Host config epoch mirror + DockExecutionRules; Pet Matches/CanPlan | previous drag or pre-mouse-up live frame acting in new gesture/finalization | session object monotonic counter, wraps max to1; epoch0 explicitly used by topology plans; preserve |
| PlanSequence | DockPlanMailbox.NextSequence on Pet | TakeFinal/CompleteFinal, Pet _lastAppliedDockPlanSequence, final expected exact sequence | superseded live plan / wrong final target batch | mailbox lifetime, not reset per drag; unify bookkeeping only after characterization |
| WindowSequence | StickyWindowSession _sequence increment at emits/captures/results | result vs Facts equality and Pet accepted-sequence checks | snapshot and actual facts from different session mutation/capture | per window session; recreated session starts new lease, not global durable version |
| StickyUi result.Sequence | same session sequence on single-window result | ApplyHostedStickySnapshot/Reproject and runtime | older single result regressing canonical content | not a second counter; batch outer sequence is not per-note truth |
| StickyHostedRuntime _appliedSequences | AddNote=0; RecordSequence; SynchronizeSessionLease resets to acknowledged seq even lower; RemoveNote removes | CanApplySequence requires membership and strictly greater | late/duplicate hosted event overwriting newer accepted content | Pet mirror of producer sequence, **lease-scoped**; cannot be replaced by topology alone |
| _lastAppliedDockPlanSequence | Pet field -1; live assign; final Math.Max | ApplyDockBatchResult uses less-than rejection | older live batch after newer one accepted | PetForm lifetime; not cleared by ResetDockDragState |
| Restore pending group key | Pet _pendingHostedDockRestoreGroups.Add; group ID else sorted legacy IDs | TryRestoreHostedDockComponent before EnsureSession fan-out | double reopen interleaving same hidden-group restore | one restore transaction; released success/failure; not sequence |
| Pending topology keys | Pet _pendingDockTopologyGroups / _pendingStandaloneTopologyNotes | Add gate; removal in completion/deferred path | duplicate reconcile dispatch | bounded in-flight scope; keep separate from gesture while ownership unresolved |

References: Core/Display/DockInteractionState.cs; Infrastructure/Display/DisplayTopologyRuntime.cs; Features/StickyNotes/DockWindowFacts.cs:94; StickyWindowSession.cs:670,730,774,1024; StickyHostedRuntime.cs:27–57; PetStickyDockCoordinator.cs:635,732; PetStickyWindowCoordinator.cs:2041.

## Actual phase flow

```text
Idle --BeginPreparing(new epoch)--> Preparing
Preparing --TryEnterDragging in BeginStickyDockDrag--> Dragging
Dragging --same topology DPI handoff--> Dragging (next actual-facts live plan)
Dragging/Preparing --topology change + BeginRebase(new epoch)--> Rebasing
Rebasing --CaptureDockFacts barrier accepted--> Dragging
Dragging --deliberate split--> Dragging (canonical membership changes, no new phase)
active --BeginFinalizing(new epoch)--> Finalizing
Finalizing --topology change--> RestartFinalizing(new epoch) + new final capture
Finalizing --capture -> final batch -> commit, finally Reset--> Idle
failure/reset/exit --Reset(new invalidating epoch)--> Idle
```

No invented “DpiHandoff” phase or generic Cancel command. Begin does not wait for a facts-barrier round trip. Rebase rejection returns while phase remains Rebasing; live effect failure logs without resetting; final capture rejection resets. This is asymmetric failure handling to characterize, not automatically a bug. Reset clears active IDs/maps/detach/split/remainder, optionally mailbox, but not start/last/time facts or last-applied sequence; stale values are guarded by active identity/phase.

## TryApplyDockFactsBarrier — every check, source owner and decision

PetStickyDockCoordinator.cs:115–183. All retained. “Producer” can validate payload shape, but only Pet knows current canonical membership/accepted watermark.

| Check | Failure prevented / source owner | Move closer? / transaction token? / action |
|---|---|---|
| result non-null, Handled, DockBatchResult non-null | null/failed execution treated as success; Host result | producer shape factory possible; keep |
| expectedIds non-null/nonempty | capture without authoritative expected membership; Pet | Pet-only; keep |
| topology non-null, epoch positive | unbound transaction; Pet Dock session | can package expected token; keep dimensions |
| current generation equals captured topology | obsolete monitor result; DisplayTopologyRuntime | must recheck consumer; keep |
| batch epoch equals expected | prior gesture/finalizing capture; Pet epoch, Host echo | token candidate, keep |
| batch PlanSequence==0 | live/final layout result mistaken for baseline facts | typed operation identity candidate; do not remove |
| batch topology equals expected | inconsistent frame metadata | producer+consumer; keep |
| member count equals expectedIds.Count | missing/extra member | expected set owned Pet; keep |
| expected HashSet and actual HashSet | id-based, case-insensitive membership | precomputed expected descriptor possible; keep |
| member nonnull; Snapshot and Facts nonnull; canonical exists | incomplete/unowned member | shape producer; canonical existence consumer; keep |
| expected.Contains and actual.Add succeeds | foreign ID or duplicate | expected descriptor could travel; keep consumer check |
| member.WindowSequence == Facts.WindowSequence | mixed snapshot/facts capture | producer invariant, consumer defense; keep |
| Facts.TopologyGeneration == expected | old per-member monitor mapping hidden by current batch | producer + consumer; keep |
| NoteId == Facts.WindowId | another HWND's geometry assigned to this note | producer + consumer; keep |
| HostedRuntime.CanApplySequence | duplicate/old lease event | Pet watermark; cannot producer-only validate |
| DockWindowFacts conversion nonnull | unusable runtime record | producer shape possible; keep |
| actual.Count==expected.Count and sourceFacts found | missing authoritative source despite follower capture | Pet source identity; keep |
| optional clear original/current maps | rebase baseline replaces old frame | gesture owner; transaction boundary candidate |
| apply content/visibility/TopMost + actual geometry | canonical mutation after full member preflight | Pet canonical owner must remain |
| TryUpdateEffective succeeds | effective monotonic check against prior facts | effective owner check; **currently in mutation loop** |
| RecordSequence; current facts; optional original facts | advance mirrors consistently | consolidate acceptance ownership later, not delete |

**Audit risk, not reproduced regression:** member preflight finishes before writes, but effective monotonic rejection is inside the mutation loop, after canonical mutation. A false return may follow some writes. ApplyDockBatchResult and ApplyReprojectResult likewise do not uniformly honor TryUpdateEffective's bool. Characterize equal-sequence/new-lease/topology cases in PC-2 before moving this code. Do not call current behavior a fully atomic Pet transaction merely because Host returns one batch.

## Pet Dock field lifecycle inventory

All below owned by Pet STA except the shared mailbox lock; exact mutators/callers in state-owner.csv.

| State | Begin/write | Clear/end | Topology / exception handling |
|---|---|---|---|
| _activeNoteDragId | BeginStickyDockDrag source | Reset null | retained while rebase; final errors reset; stale callbacks use phase/epoch |
| _activeDockGroupIds | SetActiveDockGroup from canonical component; changes after split | Reset Clear | used as expected capture IDs; retained/rebased |
| _activeDockOriginalFacts | begin CaptureDockInteractionBaseline, accepted resetOriginal barrier | begin/reset Clear | rebase rebuilds; split remainder restoration reads baseline |
| _activeDockCurrentFacts | begin, RememberActiveDockFacts(target projection), barrier | reset Clear | contains preview/planned cache as well as accepted baseline; not HWND truth |
| _activeNoteDragStartFacts | begin once | next begin overwrites, reset does not clear | original gesture provenance retained through rebase |
| _activeNoteDragLastFacts | begin, Move, accepted rebase | next begin overwrites | rebase replaces current delta origin; guarded by source |
| _activeNoteDragStartedUtc | begin | next begin overwrites | rebase cancels split eligibility instead of restarting timer |
| _dockPreviewParentNoteId / _dockPreviewChildNoteId | UpdateDockPreview | ClearDockPreview | begin/end/preview invalidation; visual cleanup separate from Reset |
| _splitRemainderNoteId | split extraction | begin/reset null | finalizes remaining order; source/remainder canonical owners still Pet |
| _movingDockGroup | Move sets true | finally false | local recursion/effect guard, not cross-STA lock |
| _activeNoteDetached | begin false, deliberate extraction true | reset false | one extraction per gesture |
| _activeNoteSplitEligible | Core split rule at begin; move cancels; rebase false | reset false | mouse movement/time and topology cancel hold |
| _synchronizingDockLayout | LayoutDockChain / resize layout scope | finally false | prevents reentrant local layout; no async transaction guarantee |
| _dockPlanMailbox | NextSequence, Current replace, final replace | TakeLatest/final completion/reset/invalidate | lock shared; topology invalidation clears pending; old native result still must be rejected |
| _dockInteraction | begin/rebase/final/reset methods | reset phase | epoch published to Host at each invalidation |
| _lastAppliedDockPlanSequence | accepted live / durable commit | not cleared per gesture | monotonic mailbox lifetime |
| _pendingDockTopologyGroups | topology group dispatch Add | completion/remove path | old results rejected; caller retries latest topology; separate from restore gate |
| _pendingStandaloneTopologyNotes | ScheduleLatestStandaloneReconcile Add | deferred BeginInvoke removes before reconcile | skips disposed/active source; one queued latest reconcile |
| _activeHostedDockResizeFacts / _activeHostedDockResizeSourceId | BeginHostedStickyDockDivider captures baseline | ClearHostedDockResizeSession / IfMember, completion, exit | MatchesHostedDockResizeSession and generation facts gate; not drag phase |
| _pendingHostedDockRestoreGroups | restore key Add after complete preferred check | immediate failure/catch; ReleaseHostedDockRestoreGate in success/failure finally | rejects duplicate restore; async callbacks converge completion; no timeout token yet |

## Restore is a separate transaction, not drag

```text
ShowHostedSticky(hidden group)
 -> TryRestoreHostedDockComponent
 -> MigrateDockRestorePreferredIfNeeded
 -> [incomplete] TryRestoreHostedDockComponentLegacyFallback
 -> [complete] group gate Add
 -> HostedDockRestorePreparation (Ordered, focus flags, Pending, failure, PlanSequence)
 -> EnsureSession per note (no show/place)
 -> each ack SynchronizeSessionLease; wait for Pending==0
 -> CompleteHostedDockRestorePlacement
 -> TryResolveDockRestoreTarget / PostDockGroupRestoreReproject
 -> ReprojectDockGroup(RestorePreferred, show-after-placement)
 -> Host ApplyDockGroupReproject:
      prepare all -> one surface/actual DPI -> native group batch
      -> capture all -> TryShowCurrentPlacement all
      -> CommitRestoredVisibleState all -> one result
      -> finally CompleteDockTargetDpi; rollback if not applied
 -> CompleteHostedDockRestoreReproject / TryApplyDockTopologyResult
 -> CompleteHostedDockRestoreSuccess: canonical/save/focus, release gate finally
 failure -> FailHostedDockRestore, release gate finally
```

EnsureSession can acknowledge an already-present session or create a new one. SynchronizeSessionLease may lower the Pet watermark to that real session; it preserves IME/focus/delete flags. This is not an exemption permitting arbitrary late events to reset the lease. References: WindowCoordinator.cs:2015–2482; Host.cs:289,715; Session.cs:601–670.

## H-DOCK-1

Supported: the thread boundary is necessary, but one gesture's state is split across PetForm fields, DockInteractionSession, mailbox and Host current-token mirrors. PC-6 should centralize Pet lifecycle ownership after PC-2 characterization and PC-3/5 authority work, preserving independent producer/consumer validity checks.

## PC-1 review amendment

Restore and topology reconciliation are not drag lifecycle. Pending restore gate belongs to a StickyFeatureController / DockRestore lifecycle candidate; standalone topology gate to DisplayRuntime + Sticky feature reconcile boundary; group topology gate to Display/Sticky topology reconcile boundary. _dockPlanMailbox live/final ownership is separable from its cross-lifecycle PlanSequence allocation used by topology reproject and Dock restore (WindowCoordinator.cs:686,2318). Keep that shared allocation boundary unresolved until PC-6; do not make hidden restore depend on current drag runtime.
