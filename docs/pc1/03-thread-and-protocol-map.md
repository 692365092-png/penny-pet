# Thread and protocol map

## Ownership, not object creation alone

| Object | Runs on / created by / stopped by | Window | Canonical / topology / sequence / effective / persistence |
|---|---|---|---|
| PetForm + 13 partials | Main Pet WinForms STA; PennyApplicationHost; Form exit | Pet, tabs, dialogs, indicators | integrates repository, display and hosted state; canonical mutations and save calls |
| StickyUiThreadHost | Own Sticky WPF STA; Host field constructed by Pet; Host BeginShutdown/Dispose | no note Window | thread/Dispatcher/scheduling only; no canonical/geometry |
| StickyUiHost | facade called by Pet, registry/commands executed on Sticky STA; Pet owns shutdown | registry of sessions, not Pet-visible Window refs | locked topology/epoch mirrors; routes batch results; no repository |
| StickyWindowSession | created/removed by Host on Sticky STA | one StickyNoteWindow/HWND | detached working copy; adopted topology; per-session sequence; capture facts; no repository |
| StickyNoteWindow | Sticky STA session | WPF controls/caret/IME | local mutable editing data, not repository instance |
| StickyHostedRuntime | created by PetForm, used Pet STA | none | membership/accepted sequence, IME/focus/delete/exit; no topology or save |
| StickyPlacementRuntime | Pet STA field lifetime; Remove/Clear entries | none | accepted effective WindowFacts + temporary rehome flags; no repository |
| DockInteractionSession | Pet STA | none | phase, source, epoch, bound generation; no HWND/save |
| DisplayTopologyRuntime | Pet STA timer lifecycle; Pet constructs/disposes | none | semantic topology generation; no window placement |
| StartupLoadingThreadHost | temporary WinForms STA; PennyApplicationHost owns close/disposal | bootstrap loading only | ready/close lifecycle; no PetArt/repository/Sticky |
| async workers | ThreadPool/network, IO worker, privacy/art preload | no UI authority | detached results returned through context; repository physical IO gate owns serialization |

Evidence: StickyUiThreadHost.cs; StickyUiHost.cs:23,64,82,106,173,915,938; StickyWindowSession.cs:26,780; PetForm.cs constructor/OnFormClosed; StartupLoadingThreadHost.cs. `StickyUiHost` is not “entirely Pet-thread” merely because Pet constructs it. Config setters use a lock; registry/window methods are scheduled to Sticky.

## Carriers

[protocol-message.csv](protocol-message.csv) records payload shapes. 16 CommandKind / 23 EventKind at golden. No kinds were added in PC-1. Latest/final Dock plan scheduling is an existing explicit Host API, not a new CommandKind.

Detached does **not** mean deeply immutable everywhere: command DockNoteIds/Reminders, snapshot TodoItems/ScheduleItems, result FinalSnapshots expose arrays. Constructors copy relevant inputs, but consumers could mutate exposed arrays. DockBatchResult clones and exposes read-only Members. This is protocol-tightening debt, not evidence of an observed race.

## Standalone show, actual methods

```text
Pet user/startup -> ShowHostedSticky
  -> [not member] StartHostedSticky -> StickyUiCommand.Create(snapshot,...)
     -> PostHostedStickyCommand -> StickyUiHost.PostCommand -> HandleCommand
     -> CreateSession -> new StickyWindowSession(snapshot.CreateWorkingCopy())
     -> Show / Reproject
  -> [member] PostHostedStickyShow -> Show or Reproject command
     -> HandleCommand -> session.AdoptTopology -> session.Show / Reproject
  -> ResolvePlacementPlan -> PlaceAtNativeBounds / placement executor -> HWND
  -> CurrentResult / EmitSnapshot -> PostEvent / reply SynchronizationContext
  -> ApplyHostedStickySnapshot / ApplyHostedStickyEvent / ApplyReprojectResult
  -> canonical content + current-generation facts mirror; placementRuntime update
  -> AdoptPreferredIfEmpty or explicit user commit, not arbitrary fallback overwrite
```

Create/Show ordinary result uses ApplyHostedStickySnapshot; current geometry arrives through facts-bearing events. Reproject result uses ApplyReprojectResult. Missing preferred display is selected on Pet through TryBuildTemporaryRehomeTarget. Focus after Reproject may post one extra FocusPrimaryInput. References: PetStickyWindowCoordinator.cs:979,1042,1365,1433,1740,1961; StickyUiHost.cs:173,256; Session.cs:57,178,421.

## Dock cross-STA paths

| Phase | Pet entry / outbound | Sticky executor / effects | Reply and acceptance |
|---|---|---|---|
| Begin | HeaderDragStarted -> BeginStickyDockDrag; RaiseDockGroupForDrag for group >1 | RaiseDockGroupForDrag -> ordered RaiseForDockDragWithoutActivation; native contiguous z band | status callback logs failure; geometry drag arms synchronously on Pet via TryEnterDragging, no start facts barrier |
| Move | HeaderDragMoved(snapshot+facts+capture topology) -> MoveStickyDockDrag -> PlanLiveDockPlan -> PlanDockPlan -> ApplyLiveDockPlan -> PostLatestDockPlan | TakeLatest -> ApplyDockPlan; source follows real drag; followers TryPrepareDockTargetDpi; native deferred batch, bounded fallback SetBounds | one DockBatchResult including all members; ApplyDockBatchResult -> canonical compatibility/effective/accepted sequence |
| DPI handoff, same topology | next actual source facts drive target surface/actual DPI and existing next live plan | follower DPI bootstrap inside same ApplyDockPlan, then batch/capture; events suppressed | same batch result; no extra per-follower cross-STA DPI command |
| Topology change | HandleStickyTopologyChanged -> InvalidateDockPlansForTopologyChange -> BeginRebase/RestartFinalizing; ResumeDockDragAfterTopologyChange -> CaptureDockFacts | CaptureDockFactsForCommit -> session CaptureDockMember against adopted topology | TryApplyDockFactsBarrier -> reset baseline, cancel split hold, TryEnterDragging -> live plan; other groups ReprojectDockGroup |
| Mouse-up | CompleteStickyDockDrag updates merge relation using Core; StartDockFinalization -> CaptureDockFacts | capture all current member HWNDs (plan sequence 0) | finalizing epoch + facts barrier -> PlanDockPlan -> ReplaceWithFinal -> PostFinalDockPlan -> ApplyFinalDockPlan/ApplyDockPlan -> CompleteDockDurableCommit |
| Split | MoveStickyDockDrag deliberate hold -> ExtractSingleDockMember, reconnect remainder, rebuild active group | existing SetBounds/role effects for remainder; source continues normal live pipeline | same final commit path, seed and remainder visible order committed |
| Cancel/failure | reset/exit/topology gates; ResetDockDragState invalidates epoch and optionally mailbox | old queued plans fail host generation/epoch checks | no standalone Cancel CommandKind; rejection behavior differs: live error logs, final barrier failure resets, rebase barrier failure stays Rebasing |

References: PetStickyDockCoordinator.cs:246,332,421,524,579,635,691,732; PetStickyWindowCoordinator.cs:462,499,1564; StickyUiHost.cs:514,524,541.

## Counted structural traffic: 3 existing members, root drag, no merge/split

Counts describe code dispatch opportunities, **not a measured mouse message count**. `m` accepted nonzero source moves, `k <= m` queued latest-wins drains; OS BoundsChanged/content/IME events are additional and not fixed.

| Phase | Pet -> Sticky work | Sticky -> Pet |
|---|---|---|
| Begin | 1 RaiseDockGroupForDrag, not 3 per-window commands | 1 HeaderDragStarted input event + 1 status result |
| Each accepted move | 0 if mailbox already queued, else 1 PostLatestDockPlan | 1 HeaderDragMoved input per callback; at most 1 result per queued drain |
| Each successful live drain | 2 follower native placements, capture 3 members inside STA | 1 batch result, not 3 result round trips |
| DPI handoff | **0 dedicated additional commands**; included in next live drain | same batch; native source may emit extra bounds events |
| Final capture + final plan | 1 CaptureDockFacts + 1 PostFinalDockPlan | 1 HeaderDragCompleted input + 2 results; each batch carries 3 members |
| Role refresh at CompleteStickyDockDrag | RefreshDockResizeRoles first resets all hosted notes, then assigns grouped roles | **6 commands/results** for exactly 3 visible grouped hosted notes: 3 reset + 3 grouped; global note population changes count |

Thus geometry/z-order core is `k + 3` dispatches and corresponding results (one begin, k live, two final), **plus 6** role commands/results for the stated three-note scene: `k + 9`, excluding unrelated events and optional feature effects. Role refresh dispatches are independent of the final capture/batch protocol. No falsely constant “one RTT total”. Topology rebase adds a CaptureDockFacts round trip and resumed live work; Restore is a separate N EnsureSession + group transaction, below.

## CloseAll (separate lifecycle)

Pet TryCloseAllHostedStickies -> one CloseAll; Host CloseAllSessions preflights **all** IME states before closing any; suppresses events; FlushAndCaptureFinal per available session; closes and clears registry; returns FinalSnapshots batch. Pet applies every snapshot without per-item save, prepares exit, begins host shutdown, then BeginExitSequence persistence. NotAccepted leaves exit requested and last IME end retries; failure cancels exit. See Host.cs:473; WindowCoordinator.cs:1846; Session.cs:730. Protocol tests and Windows probes are distinguished in 08.
