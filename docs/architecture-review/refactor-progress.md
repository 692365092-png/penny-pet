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
| R09 | ConversationRuntime owns daily/daypart/small-talk scheduling and ledger persistence; PetForm presents conversation messages and animation results. Daily attempts take one preference snapshot and use owner-context invalidation instead of a lock. | New integration self-test checks in-flight coalescing without premature ledger consumption, original-daypart commit, accepted/rejected presentation, date/daypart rollover, quiet mode, shutdown and old/new completion order. Existing catalogue, weather-cache and speaking-quota cases remain. |
| R10 | InteractionRuntime owns playback, hover hysteresis, pointer/drag state, typing, poke intent and reminder/exit transitions. PetForm supplies native input, one clock and read-only frame presentation. Ready art reads bypass the decoder gate. | Ten deterministic runtime tests cover fallback/full cycles, drag/DPI rebase, capture loss, hover, typing/editor focus, priority, reminders, exit and Stop. Native self-tests exercise the real runtime and ready-clip reads while another thread holds the decoder gate. Windows CI is the execution gate. |

R10 passed [Windows CI #65](https://github.com/692365092-png/penny-pet/actions/runs/35857017185), including the ten interaction tests, native ready-art check and formal EXE smoke.

## R11 — implementation complete, Windows functional gates passed

First checkpoint introduced the memory model and snapshot writer. The second checkpoint removes the transitional repository: `StickyModel` owns the canonical collection, note creation/removal, tab ordering, Dock member removal and detached snapshot capture. It has no store, file path, writer, window, dispatcher or Drawing dependency. `StickyStore` owns loading/recovery, the serial writer and independent emergency export. It returns a loaded model to the owner and retains no live model field. Background writes receive detached requests only.

`StickyFeature` owns the model, Store and attached Windows workspace. It accepts owner-side commands, captures save snapshots, and publishes an import/restore replacement only after the existing backup/primary commit succeeds. Startup still explicitly attaches and starts the UI runtime. Existing disk format, future-schema blocking, salvage/backups, hidden Dock slots and placement preferences are unchanged.

Workspace no longer has a PetForm reference. Its dependencies are read-only Pet surface facts, concrete presentation operations and reminder projection/actions; startup, typing and exit are explicit events. Modal management and backup dialogs remain in Pet presentation, not behind writable shell fields. Owner-context posts replace direct Form.BeginInvoke calls and ignore work after workspace disposal. StickyFactsReceiver accepts only StickyModel, without a file or writer dependency. The three redundant codec-forwarding methods were removed; UI callers use Core codec functions directly.

Five new tests exercise memory-only commands, hidden-tab ordering without changing Dock slots, hidden Dock-member removal, deep snapshot isolation and a blocked store while the live model changes. Existing real-file recovery, future-schema, import/restore and serial-writer tests remain the compatibility gates.

The new native `sticky_feature_boundary_ok` check constructs the actual feature/workspace without PetForm or real I/O. It checks canonical model identity, accepted content, reminder actions, lifecycle notifications, manager command wiring, deferred execution and suppression after disposal. Existing PC2 native scenarios now attach through the composition entry and inspect the writer through Store.

CI #67 compiled successfully but correctly rejected System.Drawing in the new Core model. Its creation API now takes plain x/y coordinates; the original platform boundary guard remains intact. Synchronous import/exit persistence barriers are preserved in this item; R17/R25 own their asynchronous redesign. SideTabs and live Dock execution ownership are still R21–R24, not claimed complete here.

R09 passed [Windows CI #64](https://github.com/692365092-png/penny-pet/actions/runs/35853802433), including runtime integration, release smoke and artifact checks.

R08 passed [Windows CI #63](https://github.com/692365092-png/penny-pet/actions/runs/35827162500) after the full-restore call-site fix, including the new runtime self-test and release smoke.

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

R08's first Windows run caught a remaining call to the removed reconciliation method in full backup restore. The follow-up routes it through `ReminderRuntime.ReconcileNoteLinks` and extends the runtime self-test with actual note-model replacement and orphan cleanup. The initial failed run is [CI #62](https://github.com/692365092-png/penny-pet/actions/runs/35826916959).

## R09 scope and behavior corrections

ConversationRuntime owns the ledger and the three existing content components; these components retain useful selection/composition logic rather than being replaced by another queue. PetForm no longer captures groups of preference delegates or directly consumes/persists dayparts. Only the accepted opening callback records the original request's date and daypart. An already-pending request is handled without consuming anything.

Settings changes, quiet-mode changes, reminder takeover, the poke Easter egg and shutdown invalidate pending daily attempts. Completion checks also compare the captured preferences and current local date/daypart before presentation. A prior completion cannot release a newer attempt's in-flight state. Weather HTTP/cache ownership and request reuse remain in PetWeatherSource; forecast errors use the existing no-weather content fallback. Existing bubble priority and accepted-presentation semantics remain the publication boundary.

## R10 ownership and behavior corrections

PetForm no longer forwards writable row/frame/typing/attention properties into a controller, and there is only one pointer/drag state. Core retains pure animation selection rules. InteractionRuntime exposes semantic input commands and read-only state; Shell retains native capture, window movement, DPI fact collection and bitmap presentation. Stable hover advances on the existing animation tick, so the separate hover timer is removed. DPI handoff rebases the runtime's pointer origins explicitly. Capture loss ends the gesture without synthesizing a poke.

Unavailable optional art now plays the ready Idle loop while preserving the current intent. Completion is observed by the current runtime state, so an abandoned poke is not revived by a later asset load. A reminder can preempt protected small talk; protection still applies to incidental dragging. Animated dragging no longer momentarily picks a random idle clip at a loop boundary. Async conversation results schedule animation from their acceptance time and cannot restart it after drag cancellation, reminder takeover or shutdown.

Ready clip publication uses release/acquire reads and writes. UI readiness, timing and bitmap reads do not acquire the decoder gate or call a decoding getter. The mandatory startup Idle load is explicit before background work starts. Resource-task sharing, background decode/disposal ownership and memory policy remain R12; scaled bitmap construction remains R19. Goodbye is warmed in the background and has a two-second missing-art wait budget, after which shutdown can finish instead of waiting indefinitely for damaged optional art. A ready goodbye still plays its full cycle.

No native drag-latency, mixed-DPI visual or IME performance improvement is claimed from source checks alone. Windows CI remains required for this change; the local environment has no .NET SDK.

R11.1 initial [CI #66](https://github.com/692365092-png/penny-pet/actions/runs/35890263969) compiled product code but rejected two new test fixtures that assumed parameterless todo/schedule constructors. The follow-up uses the actual text/state/date constructors.


R11 completed on `27746ac`: [Windows CI #69](https://github.com/692365092-png/penny-pet/actions/runs/36010301854) passed build, 609 discoverable tests, modular native self-tests and the formal single-file EXE smoke. CI #68 exposed two source guards still expecting direct PetForm calls; the follow-up follows the actual presentation port and typing event subscription. The native feature-boundary test continues to exercise typing and lifecycle delivery.

## R12 — shared clip tasks and resource lifetime

Each terminal manifest state / release-pack clip index has one `ArtClipAsset`, shared by its row aliases. Startup warmup, interactive requests and reminder attention all use its task. Ready frame reads and loaded-row counts do not acquire a decoder lock. Pack metadata and alias mapping are established once during package construction; bitmap decoding runs outside short task/publication gates.

Each asset permits one initial decode plus one retry, with a one-second backoff. A failed task is reused during backoff and after the retry budget is exhausted. Idle remains the interaction fallback. The row reservation component and separate startup worker thread are removed. Reminder intent invalidation stays with ReminderRuntime; abandoning an intent does not cancel another consumer's shared task.

Disposal cancels pending consumers without waiting for decoding. A late worker disposes its unpublished result; published aliases share one disposal owner. Failed GIF/folder decode attempts release partial frames. Embedded fallback extraction uses unique temporary paths so different definitions cannot overwrite the same staging file. Startup Idle and offline art tools retain an explicit synchronous loading entry; optional UI playback never waits there.

Native regression checks cover 20 simultaneous consumers, ready reads during blocked decode, bounded failure/backoff, cancellation before decode completes, late bitmap release, actual packaged row identity, external alias chains/fallback and invalid optional aliases. The existing lazy-load check now counts all three ready hover aliases. Windows CI is required; this Linux workspace has no .NET SDK or interactive Windows desktop. No rendered latency or memory-performance improvement is claimed without measurement.

R12 passed [Windows CI #70](https://github.com/692365092-png/penny-pet/actions/runs/36022686869) on `90a67d8`: solution/tool builds, 608 discoverable tests, modular native regressions, formal EXE smoke and artifacts. The obsolete reservation unit test was replaced by native shared-asset concurrency, bounded-retry, alias and disposal checks.

## R13 — separate Dock rules from application protocol

`DockLayout` now produces plain window targets from logical geometry; projection and centering have no gesture identity, sequence or execution state. `DockPlacementPlanner` wraps those results with the existing application protocol in Features. `DockInput`, the old Pet-side interaction session and the deferred mutation queue also move to Features. They remain in use until R22–R24 replace the old execution path; this step does not claim zero live cross-thread traffic. Legacy physical recovery returns detached geometry to the application, which adds its restore transaction identity.

Horizontal follower layout and final correction are now pure geometry operations, alongside the existing divider and clamping rules. Snap candidate ranking consumes captured eligible rectangles, retains stable tie ordering and uses wide intermediates for distant screen coordinates. Both preview and final target selection call it through the same existing controller path. Detached id-order transforms are shared by staged merges, committed merges and member extraction, preserving hidden slots.

New regression cases check the compiled Core assembly boundary, protocol-free result types, seam rounding at 96/120/144/192 DPI, mixed-size physical followers, final divider correction, snap ties/extreme coordinates, hidden-member preview/commit parity and overlapping merge ids. Existing gesture/mailbox, topology, restore and native tests remain the compatibility gates. Windows CI pending.


R13 CI #71 built successfully and passed all 619 discoverable tests, including the 11 new pure-rule cases. Native validation exposed an existing weather test race: an immediately completed fixture response could populate cache before the second call, while the test still required in-flight Task identity. The follow-up holds that fixture response until both requests exist, preserving the task-sharing, cached-value and single-request assertions. The report's `typing_moves_pet`, `look_follow_registered` and `keyboard_content_recorded` false fields are fixed capability declarations, not failed checks. Production weather behavior is unchanged.


R13 passed [Windows CI #72](https://github.com/692365092-png/penny-pet/actions/runs/36024112230) on `29e69c9`: build, all 619 discoverable tests, modular native self-tests, formal single-file EXE smoke and artifacts.

## R14 — independent Pet display runtime

`Features/Display/PetDisplayRuntime` now owns preferred-placement commits, temporary rehome/user-move state, programmatic placement scopes and the effective WindowFacts/topology pair. PetForm implements a narrow native window port on Pet STA: actual HWND DPI/facts, movement, size application and follower presentation. The existing DisplayTopologyRuntime remains the single runtime snapshot source; its settling and StickyHost snapshot delivery are unchanged.

Captures publish only against the exact current snapshot and a newer window sequence. Native reentry that changes topology or produces newer facts cannot relabel old pixels or overwrite the newer pair. Programmatic placement uses nested scopes; a nested DPI handoff cannot prematurely enable user commits. Placement checks snapshot currency after native capture and scaling before applying projected coordinates.

Fourteen new deterministic runtime cases cover 96/120/144/192 DPI at a negative origin, unplug/return without preference loss, user placement on durable and ephemeral surfaces, topology changes during drag/capture/scaling, nested capture and programmatic movement, invalid facts, work-area clamping and compatibility-only save. Architecture guards enforce the native adapter boundary. The existing native scene fixture now constructs the runtime explicitly. Windows CI is required; local .NET SDK and a physical mixed-DPI Windows desktop are unavailable. These checks do not claim measured drag latency or manual hotplug/visual validation.

R14 [CI #73](https://github.com/692365092-png/penny-pet/actions/runs/36076537966) compiled successfully. Two new multi-display fixtures incorrectly marked both surfaces primary and were rejected by topology validation. The follow-up explicitly marks the secondary surface; production code is unchanged.


R14 passed [Windows CI #74](https://github.com/692365092-png/penny-pet/actions/runs/36076763441) on `c95caac`: all 634 discoverable tests, native self-tests, single-file EXE smoke and artifact upload.

## R15 — one content view per sticky window

The shell creates one fixed-type content object: ordinary text, todo or schedule. RichTextBox, font-family choices, selection/typing-format state, composition handlers and link refresh belong only to the text view. Todo owns its row editors, completion/pin/order actions and selected row; schedule owns its list, date dialogs and minute refresh timer. List views share only their compact size selector and creation buttons. The unattached hidden todo input and inactive editor trees are removed. Shell keeps title, native geometry, persistence scheduling and common chrome.

Reminder controls are created on the first nonempty projection and updated independently of the body. The shared reminder semantics remain unchanged. Font preview/countdown updates retain the existing current view and keyboard target. Closing disposes the selected view and stops its timer; queued input callbacks retain their existing disposed-window checks.

All `Exercise...ForTest` scripts move from the product window into the Windows integration-test assembly. Tests inspect and operate the real current controls through a test-side driver. Scenarios that formerly changed one window's type now use correctly typed windows; production does not construct hidden controls for tests. New native checks cover control-tree ownership, lazy banners, focus/body stability during reminders, document undo, routed composition signals and timer shutdown. Architecture checks guard allocation and ownership boundaries.

The extraction preserves the existing WPF selection and IME event order. Native synthetic composition checks and multilingual text round trips do not substitute for manual Chinese/Japanese IME candidate-window verification. Windows CI is required; this workspace has no .NET SDK or interactive Windows desktop. No measured startup or input latency improvement is claimed.


R15 passed [Windows CI #75](https://github.com/692365092-png/penny-pet/actions/runs/36077981822) on `93052fe`: all 637 discoverable tests, native view/editor regressions, formal EXE smoke and artifacts.

## R16 — bind keyboard display to verifiable event-time input

The hook still captures native metadata only. It now distinguishes a recognized native Edit/RichEdit HWND from a shared browser/WPF host; unknown/password targets produce no formatted key text. A missing event-time UIA RuntimeId is no longer a wildcard for a later identity. Native control identity, foreground/process/thread, a focus/state-event revision and capture timestamp travel with each candidate. Focus events invalidate queued results and hide the overlay; repeat counts do not span unknown targets or focus revisions.

SensitiveInputDetector accepts only an explicit supported `IsPassword=false` on the focused UIA Edit whose NativeWindowHandle/process match the event-time native control. It verifies RuntimeId before/after inspection and rechecks native identity. Missing properties, provider failures, credential-process/name signals and ambiguous virtual descendants suppress display. Native metadata inspection alone never substitutes for missing UIA password evidence.

One dedicated background MTA thread performs inspection without windows. There is one replaceable pending input and one coalesced UI delivery slot. Capture age is limited to 750 ms at inspection and publication. UI publication performs only cheap native checks; settings disable/re-enable, focus changes and shutdown invalidate prior generations. A blocked provider cannot block either UI STA, spawn replacement inspectors or accumulate a queue. Shutdown does not join the provider and releases delivery delegates; the background call itself cannot be forcibly cancelled.

This intentionally narrows compatibility: browser/WPF/self-drawn inputs sharing an HWND are not sufficiently attributable at hook time, so they retain typing animation but suppress key labels. Adding an asynchronous focus cache would not prove which virtual control received an earlier key. The existing first-use explanation and menu text reflect suppression when uncertain.

Deterministic worker/policy tests cover unsupported password properties, same-host identity ambiguity, focus changes after dispatch, disable/re-enable, exit, deadlines, provider errors and 1,000 replacements while inspection is blocked. Native tests exercise actual native Win32 Edit password/plain HWND transitions and WPF TextBox/PasswordBox sharing an HWND. Windows CI is required; no real third-party browser/provider penetration test or measured hook latency is claimed.

Platform references: [Microsoft UIA threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading) requires non-UI MTA use; [EM_SETPASSWORDCHAR](https://learn.microsoft.com/en-us/windows/win32/controls/em-setpasswordchar) documents native password style behavior. Native classification is used for control correlation, never as the sole privacy verdict.

R16 CI #76 rejected the linked .NET 8 test runtime because SetApartmentState is Windows-only (CA1416). The follow-up adds an OS guard for the cross-platform test target; the net48 Windows product continues to set MTA explicitly.

R16 CI #77 compiled and passed 657 of 658 tests, including all new worker/privacy cases. One existing source guard still expected the removed worker-local `focusSnapshot` variable; it now checks the validated delivery input while retaining the own-process/modal policy assertions.

R16 CI #78 passed all 658 tests; native QA rejected the WinForms wrapper fixture at the event-time identity assertion. The follow-up creates explicit Win32 Edit child windows, records non-content HWND diagnostics on assertion failure, and uses GetClassName exact registered classes in production. Superclass aliases remain unknown instead of relying on an underlying-type query inside the hook.


## R16 — completed

R16 closed on `2bd1d27`. The hook still performs only cheap native correlation. UI Automation stays on one background MTA worker, unknown/same-HWND virtual controls never publish key labels, focus-version changes invalidate queued results, and exact native Edit controls are the only cheap event-time identity. Hosted CI cannot reliably steal the desktop foreground, so native QA now exercises the same capture primitive against its own top-level GUI thread while production `CaptureCheap` still begins with `GetForegroundWindow()`.

[Windows CI #82](https://github.com/692365092-png/penny-pet/actions/runs/36132673981) passed build, all discoverable tests, modular native self-tests, formal single-file EXE smoke and the managed Dock baseline.

## R17 — persistence retry ownership

The existing `PersistenceWriter<T>` queue, revisions, adjacent autosave coalescing and explicit barriers remain unchanged. `StickyFeature` and `PetSettings` expose only a narrow retry-target contract to one application `PetPersistenceRuntime`. The runtime owns the one-shot retry cadence and unresolved failure episode; retries execute on the captured owner context so each target captures its newest detached snapshot. A pending writer is never duplicated, and explicit save/import barriers remain in the writer queue.

PetForm no longer owns a persistence retry timer, polls writer dirty/pending flags, or subscribes directly to raw failure events. It consumes one warning/recovered notice stream. Repeated failures in the same unresolved episode produce one warning rather than another bubble every timer interval. Existing synchronous exit/import barriers are intentionally retained for R25.

Deterministic runtime tests cover repeated failure coalescing, pending-writer suppression, recovery notification and startup dirty state. Windows CI remains the execution gate for this item.


## R18 — budget startup restore on the execution STA

Startup restore no longer measures a 6 ms stopwatch on Pet STA while merely posting work to another thread. Pet now dequeues one immutable restore request per deferred tick and hands it to a dedicated startup-restore transport. StickyUiThreadHost owns the restore queue and measures the 6 ms slice around the actual WPF Create/Show execution on Sticky STA, yielding through DispatcherPriority.Background when more work remains.

Normal interactive commands, Dock frame transport and restore semantics are unchanged. The dedicated path is startup-only: it prevents a fast producer from disguising an arbitrarily expensive WPF burst behind cheap cross-thread posts, without introducing a general scheduler. First-render acknowledgements still gate startup readiness.

R18 passed Windows CI #92 on `1bc550c`: build, all 663 discoverable tests, modular native self-tests, formal single-file EXE smoke, managed Dock baseline and release artifacts.

## R19 — render-cost experiment, measurement first

R19 is conditional by design. Production rendering is unchanged in this checkpoint: the global WPF SoftwareOnly setting remains, and LayeredSpriteRenderer still uses its existing per-call HBITMAP/DC lifetime. A standalone Windows benchmark links the production layered renderer and records same-machine default-vs-software transparent WPF work plus repeated UpdateLayeredWindow wall/CPU time, memory and GUI-handle deltas.

The CI report is observational and has no arbitrary performance threshold. Hosted Windows results cannot establish mixed-DPI, GPU/driver or Remote Desktop visual correctness, so they are evidence for whether a deeper experiment is justified, not permission to replace the renderer by themselves.

Windows CI #96 on `174ff1a` completed the first same-run comparison at 96 DPI, non-RDP. For 240 transparent WPF updates, Default recorded 28.83 ms wall / 46.88 ms CPU / +1,257,472 private bytes, while SoftwareOnly recorded 21.21 ms wall / 62.50 ms CPU / +1,003,520 private bytes. The wall and CPU directions disagree, so this single hosted sample is not evidence for changing the global mode. For 240 production LayeredSpriteRenderer updates, Default recorded 62.31 ms wall / 62.50 ms CPU and SoftwareOnly 60.09 ms / 62.50 ms; both had +65,536 private bytes, zero GDI/USER handle delta, a transparent corner, translucent center and the layered style present. This does not establish LayeredSpriteRenderer as a dominant cost or justify a reusable DIB/DC layer.

Windows CI #97 on `a63d28f` repeated the probe independently. For WPF, Default recorded 11.12 ms wall / 15.63 ms CPU / +835,584 private bytes and SoftwareOnly 9.37 ms wall / 0 ms sampled CPU / +1,290,240 private bytes. The coarse CPU counter and reversed memory direction reinforce that hosted-runner wall time alone is not a reliable mode-selection signal. For LayeredSpriteRenderer, Default recorded 42.72 ms wall / 46.88 ms CPU and SoftwareOnly 44.36 ms / 46.88 ms, with the same +65,536 private bytes, zero GDI/USER handle delta and correct alpha/style checks in both cases. The tiny wall-time ordering reversed from CI #96.

R19 therefore closes with no production rendering change: SoftwareOnly remains, and the per-call LayeredSpriteRenderer HBITMAP/DC lifetime remains. Two independent hosted runs show no repeatable material renderer win, no GUI-handle leak and no alpha-source regression. A future renderer rewrite requires evidence from the physical GPU/mixed-DPI/RDP scenarios named by the roadmap rather than extrapolation from this hosted experiment.


## R20 — shell-first application composition

PennyApplicationHost no longer creates or waits for a third startup-loading STA. PetForm first publishes its mandatory idle frame and raises ShellReady; only then does the application host prepare StickyStore/StickyModel on the thread pool. Publication returns to Pet STA, constructs StickyFeature there so its SynchronizationContext remains the real owner context, and only then attaches persistence, Sticky UI and reminder runtimes.

The PetForm constructor no longer scans or repairs sticky-note files. It still decodes only the mandatory idle clip; optional animation rows remain asynchronous, and weather remains demand-driven. Menu actions that depend on Sticky/reminders report that background restoration is still in progress rather than dereferencing an absent runtime.

Deferred startup now has an explicit WaitForStickyRuntime phase. ShellReady is independent of note first-render acknowledgements; StartupBackgroundReady is raised only after the R18 Sticky-STA restore queue is drained and expected visible notes have acknowledged first render. Closing during disk preparation saves settings directly and the late completion path checks disposed/exiting state before any runtime publication, so background work cannot resurrect windows. Future-schema detection remains fail-closed: prepared data is never published or overwritten and the existing compatibility message closes the shell.

## R21 — Sticky STA owns SideTabs and Dock feedback chrome

SideTab and Dock-feedback HWND ownership now follows the interaction owner rather than the historical WinForms/WPF split. `StickyUiHost` creates and destroys both SideTab forms, owns the Dock preview/split-guide/pulse forms, and applies the modal z-order floor on Sticky STA. The controls remain WinForms deliberately: moving the HWND owner removes the cross-STA correctness problem without paying for a cosmetic WPF rewrite.

Pet publishes one immutable `StickySideTabsProjection` containing hidden-note snapshots, Pet physical bounds, work area, DPI metrics and topology generation. That transport is latest-wins, so Pet motion cannot create an unbounded placement backlog. Sticky derives edge allocation and live overlap locally. Sticky-window geometry events and Dock batch completion update SideTab coverage directly on Sticky STA; the overlap path uses lightweight native window facts rather than forcing full WPF content snapshots.

Dock controller no longer constructs feedback forms. It computes semantic target/seam data and routes it to the Sticky host, which owns the corresponding HWND lifetime. Modal layering crosses the boundary only as the current modal HWND, not as a foreign Form object. Existing note-open/delete/reorder actions cross back to the Pet owner context as semantic callbacks.

Structural guards assert that Pet-side workspace no longer owns SideTab HWNDs, Dock controller no longer owns `DockPulseIndicatorForm`, and Sticky host owns creation, overlap and modal layering. Windows CI #118 on `5bdb8a1` passed the full pipeline: build, discoverable tests, modular native self-tests, single-file EXE smoke, managed Dock baseline, render-cost observation and release artifacts.
