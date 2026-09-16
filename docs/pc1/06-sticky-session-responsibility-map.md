# StickyWindowSession responsibility map

Baseline `ab10b45`; references below are lines in `desktop-pet/StickyWindowSession.cs`. All methods execute on Sticky STA unless explicitly noted. No WPF Window reference is exposed to Pet; `PlacementHwnd` is an STA-local executor edge.

Each method has one **primary** label. Policy labels are extraction candidates, not instructions to move them in PC-1. Adjacent event handlers share a row but are named individually.

| Method / line | Primary label | Responsibility / future disposition |
|---|---|---|
| constructor 26; IsAvailable 44 | WINDOW LIFECYCLE | detached snapshot -> working copy, one Window, executor and events; keep |
| IsImeCompositionActive 48 | IME SAFETY | real WPF composition status; keep native side |
| Show 57 | WINDOW LIFECYCLE | edit/show coordination and result; policy delegation candidate only after placement contract exists |
| TryShowAtPhysicalBounds 78 | DISPLAY POLICY | ResolvePlacementPlan plus native attempt; split decision from execution later |
| PlaceAtNativeBounds 113 | NATIVE EFFECT | bootstrap, HWND DPI, projection, exact placement, show, bounded correction; keep native effect |
| TracePlacementMismatch 161 | ACTUAL FACTS | compare requested vs actual, diagnostic only |
| ResolvePlacementPlan 178 | DISPLAY POLICY | preferred/v10/physical selection + topology lookup and work-area fallback; EXTRACTION CANDIDATE |
| FindSurfaceWithLargestIntersection 246 | DISPLAY POLICY | immutable rect/topology math; EXTRACTION CANDIDATE |
| Hide 270 | WINDOW LIFECYCLE | uses window hide behavior, returns local snapshot |
| FocusPrimaryInput 277 | NATIVE EFFECT | explicit focus on owning STA |
| SetTopMost 283 | NATIVE EFFECT | local window pin state, not group business decision |
| RaiseForDockDragWithoutActivation 292 | NATIVE EFFECT | no-activate z-order primitive; group ordering stays Host/Pet |
| SetDockResizeRole 306 | NATIVE EFFECT | applies typed grouped/divider role, not divider geometry engine |
| UpdateReminders 316 | WPF EVENT BRIDGE | refresh local reminder projection; no repository |
| SetBounds 325 | NATIVE EFFECT | physical placement under ApplyingBounds and result capture; preserve feedback suppression |
| AdoptTopology 366 | PROTOCOL SEQUENCE | accepts supplied snapshot and traces generation change; does not itself capture topology or reject older generations |
| CaptureCurrentFacts 380; PlacementHwnd 399 | ACTUAL FACTS | actual HWND facts bound to local sequence and adopted topology |
| Reproject 421 | NATIVE EFFECT | IME preflight, hide/bootstrap/GetDpiForWindow/exact rect/capture/rollback; centering projection is a policy seam |
| CorrectReprojectionOnce 500 | NATIVE EFFECT | one corrective native attempt, not unbounded retry |
| TryPrepareDockTargetDpi 527 | NATIVE EFFECT | verify/bootstrap target DPI while preserving rollback state |
| TryPrepareDockTargetSurface 566 | NATIVE EFFECT | temporarily hidden surface preparation; actual DPI only after placement |
| TryShowCurrentPlacement 601 | NATIVE EFFECT | narrow no-second-placement show used by restore transaction |
| CommitRestoredVisibleState 612 | WINDOW LIFECYCLE | update detached working visibility after all show effects accepted |
| CompleteDockTargetDpi 619 | NATIVE EFFECT | rollback/restore visibility and suppression state; transaction success decision belongs to Host |
| RollbackReproject 647 | NATIVE EFFECT | restore prior physical rect/visibility, not durable preference rollback |
| CaptureDockMember 670 | ACTUAL FACTS | per-note snapshot/facts/sequence result for batch |
| CaptureContentSnapshotForNativeResult 685 | WPF EVENT BRIDGE | detached content, deliberately no legacy placement recapture |
| CaptureFactsWith 690 | ACTUAL FACTS | explicit topology capture wrapper |
| Close 699 | IME SAFETY | NotAccepted while composing; local flush/close when accepted |
| DockDpiTransition constructor 714 | NATIVE EFFECT | STA-local previous bounds/visibility value; not cross-thread owner |
| FlushAndCaptureFinal 730 | WINDOW LIFECYCLE | flush editor, increment/capture final per-note snapshot |
| ReportImeCompositionActive 738 | IME SAFETY | informs Pet exit preflight |
| SetEventsSuppressed 745 | WPF EVENT BRIDGE | batch suppression flag, separate from ApplyingBounds |
| CloseForBatch 750; CloseAfterFailure 756; CloseForHostShutdown 766 | WINDOW LIFECYCLE | batch/error/shutdown local close paths; preserve unwiring/final capture ordering |
| CurrentResult 774 | PROTOCOL SEQUENCE | advances local sequence, snapshot result |
| WireEvents 780; UnwireEvents 808 | WPF EVENT BRIDGE | symmetric subscriptions owned by one session |
| NoteChanged 836; TypingActivity 841; InputFocusChanged 847 | WPF EVENT BRIDGE | detached editing/activity/focus events, not canonical mutations |
| ImeCompositionChanged 853 | IME SAFETY | deferred hide + composition signal; must remain STA-local |
| Shown 867 | WPF EVENT BRIDGE | FirstRendered signal; Pet readiness owner elsewhere |
| BoundsChanged 873 | WPF EVENT BRIDGE | suppress programmatic feedback before emitting snapshot/facts |
| HeaderDragStarted 880; HeaderDragMoved 885; HeaderDragCompleted 894 | WPF EVENT BRIDGE | capture-time facts carried with gesture, not Window reference |
| UserResizeCompleted 899; DockHorizontalResizing 912 | WPF EVENT BRIDGE | typed actual resize input |
| DockDividerResizeStarted 922; DockDividerResizing 929; DockDividerResizeCompleted 935; EmitDockDividerResize 942 | WPF EVENT BRIDGE | internal-divider input only; ApplyingBounds suppresses native-effect feedback |
| CancelReminderRequested 953; ModifyReminderRequested 958; DeleteReminderRequested 965 | WPF EVENT BRIDGE | copied request to Pet business owner |
| CloseRequested 972; DeleteRequested 978; NewNoteRequested 983; NewTodoRequested 988; NewScheduleRequested 993 | WPF EVENT BRIDGE | request forwarding, no creation/deletion of canonical business objects |
| WindowClosed 998 | WINDOW LIFECYCLE | final signal / unwire on owning STA |
| RaiseRequest 1012; RaiseReminderRequest 1017 | WPF EVENT BRIDGE | payload-shape creation |
| EmitSnapshot 1024 | PROTOCOL SEQUENCE | snapshot, sequence increment, facts at that sequence, event |
| CaptureWindowFacts 1036; TraceWindowFacts 1053 | ACTUAL FACTS | native HWND reader and bounded diagnostic channel |
| CaptureSnapshot 1078 | WPF EVENT BRIDGE | calls compatibility capture then detached DTO |
| CaptureCanonicalPlacement 1090 | PERSISTENCE POLICY | physical/v10 mirror in **working copy**, with legacy DIP fallback only if no valid placement; EXTRACTION CANDIDATE, no disk IO here |
| IsCanonicalValid 1129 | PERSISTENCE POLICY | knows legacy field validity; EXTRACTION CANDIDATE |
| Raise 1136 | WPF EVENT BRIDGE | local handler, Host marshals to Pet |
| NativePlacementPlan constructor 1147; Preferred 1164; Physical 1173/1181; Resolve 1188 | DISPLAY POLICY | local immutable intent variants/projection; EXTRACTION CANDIDATE |

## Required boundary distinction

`ResolvePlacementPlan` needs immutable topology and durable/compatibility data, **not HWND**. Selecting preferred target, legacy fallback or intersection surface can be decided before dispatch. `PlaceAtNativeBounds`, `Reproject`, DPI preparation and correction require actual HWND DPI and remain native-side. Pure projection may be calculated there using actual DPI without making session the durable-intent owner.

Do not move the HWND bootstrap into Pet just to remove a method. PC-5 must first give Session an explicit already-selected placement intent, keep rollback/IME/event ordering, and retain one coarse native result. No second display capture or new manager is justified. No method here owns repository Save; the persistence-policy label refers to field semantics, not disk ownership.

H-session conclusion: narrow the **policy**, not the indispensable Windows transaction. Native bulk may remain large after a correct extraction.
