# Code size and risk map

Baseline `ab10b45`. Scope: 192 **tracked** C# files from `git ls-files`, excluding generated bin/obj and untracked releases. Lines count newline-separated records (includes trailing empty record); bytes are UTF-8 file bytes. Full data: [code-size.csv](code-size.csv).

| File | Lines | Bytes | Risk, not verdict |
|---|---:|---:|---|
| desktop-pet/SelfTestRunner.cs | 6939 | 377347 | Size; test localization / contract coupling |
| desktop-pet/Tests/CoreBehaviorTests.cs | 3186 | 148048 | Size; test localization / contract coupling |
| desktop-pet/Features/StickyNotes/PetStickyWindowCoordinator.cs | 2938 | 132336 | Ownership; Protocol; geometry authority |
| desktop-pet/Tests/InputAnimationBoundaryTests.cs | 2608 | 138554 | Size; test localization / contract coupling |
| desktop-pet/Features/StickyNotes/StickyNoteWpf.cs | 2365 | 104570 | UI-only bulk plus input/event lifecycle |
| desktop-pet/Features/StickyNotes/StickyNoteTabs.cs | 1750 | 71582 | UI-only bulk plus input/event lifecycle |
| desktop-pet/Features/StickyNotes/PetStickyDockCoordinator.cs | 1707 | 79332 | Ownership; Protocol; geometry authority |
| desktop-pet/PennySelfTests.cs | 1699 | 84999 | Size; test localization / contract coupling |
| desktop-pet/PetArt.cs | 1399 | 63465 | Size; resource lifetime |
| desktop-pet/StickyWindowSession.cs | 1198 | 51287 | Protocol; ownership / detached boundary |
| desktop-pet/Features/StickyNotes/StickyNotes.cs | 1094 | 42544 | Persistence; compatibility authority |
| desktop-pet/Features/StickyNotes/StickyNoteRepository.cs | 977 | 39192 | Persistence; compatibility authority |
| desktop-pet/StickyUiHost.cs | 962 | 44144 | Protocol; ownership / detached boundary |
| desktop-pet/Features/StickyNotes/StickyUiCommand.cs | 867 | 35617 | Protocol; ownership / detached boundary |
| desktop-pet/PetForm.cs | 813 | 36462 | Ownership; Protocol; geometry authority |
| desktop-pet/Tests/DisplayTopologyTests.cs | 764 | 34611 | Size; test localization / contract coupling |
| desktop-pet/Features/StickyNotes/StickyEditorCoordinator.cs | 589 | 24685 | UI-only bulk plus input/event lifecycle |
| desktop-pet/PetAnimationRuntime.cs | 584 | 22665 | Feature/lifecycle review; not size alone |
| desktop-pet/PetDisplayRuntime.cs | 577 | 20710 | Ownership; Protocol; geometry authority |
| desktop-pet/Core/StickyNotes/StickyNoteModels.cs | 571 | 23732 | Persistence; compatibility authority |
| desktop-pet/Core/StickyNotes/StickyNoteCodec.cs | 544 | 25362 | Persistence; compatibility authority |
| desktop-pet/Features/StickyNotes/StickyNativeWindowBehavior.cs | 513 | 22266 | Feature/lifecycle review; not size alone |
| desktop-pet/Features/StickyNotes/StickyTodoCoordinator.cs | 468 | 20110 | UI-only bulk plus input/event lifecycle |
| desktop-pet/DailyContentSettingsForm.cs | 464 | 18126 | UI-only bulk plus input/event lifecycle |
| desktop-pet/PetBubbleCoordinator.cs | 416 | 15613 | Feature/lifecycle review; not size alone |
| desktop-pet/Core/StickyNotes/StickyDockGeometry.cs | 404 | 16216 | Pure rule / model size; not an owner by length |
| desktop-pet/Core/Settings/PetSettingsCodec.cs | 400 | 17647 | Persistence; compatibility authority |
| desktop-pet/ReminderUi.cs | 365 | 14188 | UI-only bulk plus input/event lifecycle |
| desktop-pet/Tests/DynamicNDisplayPropertyTests.cs | 362 | 15512 | Size; test localization / contract coupling |
| desktop-pet/Core/StickyNotes/StickyImportMergePlanner.cs | 361 | 15026 | Pure rule / model size; not an owner by length |
| desktop-pet/PetReminderWindowsCoordinator.cs | 343 | 14536 | Feature/lifecycle review; not size alone |
| desktop-pet/Features/StickyNotes/PetPersistenceCoordinator.cs | 333 | 15475 | Feature/lifecycle review; not size alone |
| desktop-pet/Core/Reminders/ReminderModels.cs | 332 | 11704 | Persistence; compatibility authority |
| desktop-pet/Features/StickyNotes/StickyScheduleCoordinator.cs | 312 | 13006 | UI-only bulk plus input/event lifecycle |
| desktop-pet/Features/KeyboardOverlay/KeyboardOverlayForm.cs | 309 | 12213 | Feature/lifecycle review; not size alone |
| desktop-pet/Core/StickyNotes/StickyImportBackupValidator.cs | 308 | 12911 | Pure rule / model size; not an owner by length |
| desktop-pet/Core/Display/DockPlacementPlanner.cs | 297 | 13573 | Pure rule / model size; not an owner by length |
| desktop-pet/PetMenuActions.cs | 277 | 12150 | Feature/lifecycle review; not size alone |
| desktop-pet/Core/Persona/PetPersonaEntry.cs | 274 | 13392 | Pure rule / model size; not an owner by length |
| desktop-pet/Core/StickyNotes/StickyDockOperations.cs | 268 | 11198 | Pure rule / model size; not an owner by length |

A large WPF editor/catalog is not automatically a decomposition candidate. The two PetSticky partials are high-risk because the same PetForm owns canonical edits, gesture state, topology interruption and async acceptance. Session combines native necessity with placement policy. Repository combines recovery/schema protection and serialized IO; its size does not authorize PC-1 changes.

Small real owners also audited: StickyHostedRuntime (membership/lease), StickyPlacementRuntime (effective registry), DockInteractionSession (phase/epoch), DockPlanMailbox (pending frame), DisplayTopologyRuntime (semantic generation). None should be replaced with a forwarding wrapper merely to shorten a file.
