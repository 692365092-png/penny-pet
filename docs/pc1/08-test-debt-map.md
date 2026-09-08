# Test debt map

Baseline `ab10b45`. New audit gate: **331/331 MSTest cases**, 0 skipped/failed; modular and single-file SelfTests each **292 true `_ok` fields**, no false fields. The 292 fields are report checks, not 292 independent test methods. Dedicated manual/interactive probes were not rerun.

## Inventory and evidence kinds

[test-classification.csv](test-classification.csv) classifies every **317 test method**; DataRow expansion gives 331 cases. Primary categories: PURE BEHAVIOR 134; MIGRATION 36; SOURCE-STRING GUARD 134; PROTOCOL BEHAVIOR 13. Classification follows source-reading helper calls and tested operations, not filenames: e.g. `DockCommitRejectsMissingMember/NullFacts/GenerationMismatch` and `Drt10_DurableCommitRejectsMixedTargetFacts` inspect `DockCommitValidationSource`, so are **SOURCE-STRING GUARD**, not simulated rejected native results. CoreBehaviorTests also contains the source guard StickyPlacementMath_NoGlobalLogicalShortcutInCoreGeometry. The two RestoreCommand tests execute DTO construction; WindowFactsVersionRules tests execute protocol classification; PetSettingsPlacementCodec tests are migration/serialization behavior.

PennyPet.Tests.csproj targets net8.0, compiles Tests/**/*.cs plus actual StickyHostedRuntime and StickyUiCommand implementations, references Core. Thus lease and command DTO tests execute production detached implementations. CoreArchitectureTests scans source for forbidden API strings; **it is not an assembly-reference test**. Core project build is a separate ARCHITECTURE COMPILE GUARD (netstandard2.0 dependency boundary), not counted as another MSTest case.

PennyPet.SelfTests.csproj targets net48 WPF+WinForms, references Windows.Core and embeds test fixtures. SelfTestRunner and PennySelfTests are two partial fragments of the same SelfTest owner, not independent test domains. Windows.csproj compiles the same runner into the single-file EXE. Passing both catches packaging/resource differences; it intentionally duplicates some behavior rather than replacing one mode.

## Cluster map

Full per-Run method index: [selftest-clusters.csv](selftest-clusters.csv). Primary classification is **not** a claim that every assertion inside a mixed cluster has the same strength.

| Domain | Representative real methods | Extraction boundary / prerequisite |
|---|---|---|
| Display | RunDisplayTopologyRuntimeCheck, RunNativeDisplayAbiCheck, RunDisplayResolverConsistencyCheck | move detached semantic equality/version/math to MSTest; native ABI/HWND topology capture stays Windows |
| Sticky Persistence | RunStickyPersistenceChecks, RunStickyCompatibilityChecks, RunStickyFutureSchemaChecks, RunStickyBackupCleanupCheck | codec/migration fixtures pure; actual IO/generation/primary+backup protection uses repository/product assembly |
| Sticky Placement | RunNativePlacementCheck, RunV11PreferredCheck, RunTemporaryRehomeCheck | pure policy portable; native SetWindowPos/GetDpiForWindow probes stay Windows |
| Sticky Dock | RunDockPersistenceChecks, RunDockLifecycleChecks, RunDockGeometryChecks, RunDockPlanMailboxCheck, RunDockTopologyReprojectCheck, RunDockZOrderCheck | split planner/rule behavior from HWND batch/z-order; preserve final commit/barrier characterization |
| Sticky Host | RunStickySnapshotSeparationCheck, RunStickyHostedLifecycleCheck | copy/lease transitions portable; IME/final batch/dispatcher/window shutdown need Windows |
| SideTabs | RunStickySideTabChecks, RunSideTabPhysicalProjectionChecks | Core split/layout/typography arithmetic portable; actual Font/Region/control lifetime retained Windows |
| Input | RunKeyboardOverlayChecks; RunStickyInputProbe/RunStickyWinFormsPumpProbe | pure eligibility portable; focus, hook, caret, UIA and real IME not replaced by source assertions |
| Art | RunArtResourceChecks, RunAnimationChecks | resource decode/bitmap/embedded packaging need product; timing rules pure |
| Startup | RunWindowShellChecks, RunStartupProbe | pure readiness gate portable; temporary STA/message-loop/frame suppression Windows |
| Daily/Weather | RunWeatherChecks, RunBubbleChecks, RunDailyBriefingProbe, RunAlmanacProbe, RunWeatherApiProbe | fixture and deterministic selection portable; API probe opt-in network, not deterministic gate; Bubble UI stays Windows |

## Debt and characterization order

1. **PC-2A FIRST:** characterize A1–A6 state-after-rejection through actual production logic; STOP for review and for any confirmed reachable correctness defect. **Only after PC-2A PASS + review, PC-2B:** separate reporting/orchestration from probe domains without changing `_ok` keys, command routes or resource logical names. Preserve modular + single-file gates.
2. Isolate source guards into a named structural suite; keep until equivalent behavior characterization exists. Do not delete tests just because refactoring breaks a literal name.
3. Required PC-2A safety net (before steps 1–2 decomposition): characterize barrier rejection **state after failure**, new-session lease vs existing effective geometry, and final commit whole-set validation. Source guards named like behavior tests currently overstate evidence.
4. Preserve fixture v1..v11 and future-primary+older-backup incident matrix; old readers must not recover an older backup then overwrite future primary.
5. DUPLICATE COVERAGE candidates: Core pure divider/placement arithmetic repeated in SelfTests; consolidate later only if Windows integration and embedded packaging checks remain.
6. OBSOLETE CONTRACT CANDIDATE: source tests that require current partial filenames/helper placement or v10 live-mirror strings. Not obsolete product safety; retire only after new owner invariant tests pass (PC-2/10).

H-TEST-1 supported: strong breadth, high navigation/review cost in giant files and mixed evidence kinds. No tests were changed/deleted in PC-1; no new test project or harness was created.
