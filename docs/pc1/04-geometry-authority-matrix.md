# Geometry authority matrix

The model has **14 fields in three representations**, not three interchangeable authorities. Pet repository instances are canonical product data; session uses a detached working copy. Actual HWND geometry is `WindowFacts`; planned targets are not facts. Physical/model read locations still exist in preview, recovery, layout, SideTabs and snapshots, so “all runtime reads WindowFacts only” would be false.

## Every field / serialized index (zero-based)

| Field | Persisted since / current index | Read roles | Writer roles |
|---|---|---|---|
| X | v1 / 5 | startup physical recovery; standalone fallback; Dock restore fallback; drag preview/layout; overlap/tab projection | parse; spawn/tile/recovery; facts mirror; legacy target mirror; working-copy capture |
| Y | v1 / 6 | same, plus chain anchor | same |
| Width | v1 / 7 | fallback size; Dock chain width; preview/overlap | same + clamp/resize |
| Height | v1 / 8 | fallback size; Dock chain height/seams | same + clamp/divider |
| DisplayId | v10 / 27 | show second fallback; migration; old snapshot/restore | parse; actual facts -> v10 placement; target compatibility mirror; working copy |
| LocalLogicalX | v10 / 28 | same v10 placement; migrate preferred X | parse; same placement conversion |
| LocalLogicalY | v10 / 29 | same v10 placement; migrate preferred Y | parse; same placement conversion |
| LocalLogicalWidth | v10 / 30 | same; validation/plan logical size fallback | parse; same conversion; RepairForDisplay |
| LocalLogicalHeight | v10 / 31 | same; validation/chain height fallback | parse; same conversion; RepairForDisplay |
| PreferredDisplayTargetKey | v11 / 32 | show/restore/hotplug preferred surface; user commit retains durable target choice | parse/repair; MigrateV10Preferred; CommitHostedStickyPreferred; initial adoption/spawn/tile |
| PreferredLocalLogicalX | v11 / 33 | standalone show/reproject; Dock logical restore/user intent | parse/repair; migration; allowed durable commit |
| PreferredLocalLogicalY | v11 / 34 | same | same |
| PreferredLocalLogicalWidth | v11 / 35 | standalone/chain projection independent of actual DPI | same |
| PreferredLocalLogicalHeight | v11 / 36 | independent per-note chain height | same |

All 14 are always written by current SerializeLine v11; v10 fields are **not decoder-only** yet. Physical and v10 updates do not intrinsically authorize overwriting preferred. CSV [geometry-read-write.csv](geometry-read-write.csv) is a line-level receiver-qualified index; it explicitly distinguishes canonical, working-copy and detached DTO contexts. Physical property names on Pet/art/other DTOs are filtered/identified, not counted as Sticky writes just because they are named X. Read lists include copying a field, which is not a new source of authority.

## Path matrix / actual precedence

| Path | Read authority / fallback order | Writes / save / preferred |
|---|---|---|
| Codec startup | historical fields, no topology capture | ParseLine then RepairForDisplay; runtime migration later; does not invent monitor intent |
| Standalone normal Show | Session ResolvePlacementPlan: valid preferred key+positive size resolving in topology; else v10 runtime GDI placement; else positive physical fallback | actual HWND capture mirrors physical/v10 in working copy; Pet accepted facts mirror; initial missing-preference adoption only |
| Missing preferred display | Pet TryBuildTemporaryRehomeTarget uses saved preferred and fallback surface policy with physical/Pet context before session Show | Reproject result updates actual mirrors + SaveAsync, temp-rehome marker; **does not overwrite existing preferred** |
| Dock hidden restore | MigrateDockRestorePreferredIfNeeded; complete preferred group -> common preferred surface, else bounded temporary surface; incomplete preferred -> TryRestoreHostedDockComponentLegacyFallback | group native result validates all, mirror/canonical visibility/save; preserve preferred except missing migration |
| Dock hotplug | preferred/effective topology policy chooses group target; one ReprojectDockGroup | actual batch -> compatibility/effective update; temp rehome flags; preferred remains saved intent |
| Standalone hotplug / return | ReconcileStandaloneSticky + current topology, preferred vs effective/temp flags | Reproject + ApplyReprojectResult; SaveAsync mirror, no arbitrary preferred replacement |
| Live drag / DPI | source capture-time WindowFacts selects surface/DPI; PlanDockPlan directly requires positive v10 LocalLogicalWidth/Height for every member, derives unified width from source actual facts and uses v10 LocalLogicalHeight for chain height/root offset | ApplyDockBatchResult mirrors actual facts; plan does not persist intent; active preview caches may hold targets |
| User move/resize completion | accepted current-generation facts + same topology -> TryBuildPreference | CommitHostedStickyPreferred only allowed reason; mark user placement + save |
| Dock final | CaptureDockFacts -> final native batch -> TryPrepareDockCommit builds all valid preferences | actual mirrors/effective/lease + preferred + order + synchronous Save in CompleteDockDurableCommit |
| Expand/tile | Pet actual surface + pure targets; clear membership | CommitExpandedPreferred and owned effects; Save; deliberate new user placement |
| SideTabs visibility/overlap | canonical Visible and physical X/Y/W/H | read-only projection; no preferred authority |

## Conversion registry (PetSticky* paths under Features/StickyNotes)

| Helper / reference | Classification | Input -> output | Caller / save |
|---|---|---|---|
| StickyPlacementMath.FromPhysicalRect / StickyCanonicalPlacement.ApplyTo | COMPATIBILITY MIRROR | physical origin+scale -> physical and v10 local fields | facts handlers, session capture, target adapter; no IO itself |
| PetStickyWindowCoordinator.cs:1408 ApplyHostedStickyFactsGeometry | EFFECTIVE UPDATE (compat mirror output) | actual facts+capture topology -> physical/v10 canonical | ApplyHostedStickyEvent, ApplyReprojectResult, barriers/live/final/topology commit; save by caller |
| PetStickyDockCoordinator.cs:658 ApplyDockCanonicalFromPhysical | COMPATIBILITY MIRROR | DockLayoutTarget + WindowsDisplayResolver -> physical/v10 model | ApplyDockTarget; not preferred |
| WindowCoordinator.cs:1497 TryBuildPreference | DERIVED PROJECTION | facts+same topology+existing key -> detached preference | user/end/initial adoption; no IO |
| WindowCoordinator.cs:1511 CommitHostedStickyPreferred | DURABLE COMMIT | allowed PlacementReason and positive local rect -> five preferred fields | explicit user actions; no IO itself |
| Core/StickyNotes/StickyPlacementRules.cs:46 MigrateV10Preferred | MIGRATION | missing preferred + valid v10 runtime GDI resolved in topology -> five preferred fields | AdoptPreferredIfEmpty, MigrateDockRestorePreferredIfNeeded; save by caller |
| WindowCoordinator.cs:1531 AdoptPreferredIfEmpty | MIGRATION / initial DURABLE COMMIT | try v10 intent first; then actual shown facts if still missing | first rendered path; existing preference never replaced |
| StickyWindowSession.cs:178 ResolvePlacementPlan | RECOVERY / DERIVED PROJECTION | preferred -> v10 -> physical intersecting/primary work-area clamp | Show; no repository |
| Session.cs:1090 CaptureCanonicalPlacement | COMPATIBILITY MIRROR | HWND PhysicalBounds + WindowsDisplayResolver -> working-copy physical/v10; invalid/no canonical only: legacy DIP fallback | CaptureSnapshot; no direct save, never passed as independent actual facts |
| WindowCoordinator.cs:1433 ApplyReprojectResult | EFFECTIVE UPDATE | version-checked result -> canonical mirrors + effective registry + accepted sequence | rehome/return/reproject; SaveAsync |
| WindowCoordinator.cs:142 CommitExpandedPreferred | DURABLE COMMIT | already-applied v10 DisplayId/local fields + current topology -> preferred | ExpandAndTileAllStickyNotesToPetScreen; save by orchestrator |
| WindowCoordinator.cs:402 CapturePetWindowFacts | DERIVED PROJECTION of actual native read | Pet HWND + generation + ++pet sequence -> immutable facts | display and fallback context; not StickyNoteData; no IO |
| WindowCoordinator.cs:1564 CompleteDockDurableCommit | DURABLE COMMIT; MULTI-AUTHORITY RISK | validated entire final batch -> content, visible, physical/v10, effective, sequence, preferred, group order | finalization; Save and MarkUserPlacementCommit |
| Core/StickyNotes/StickyNoteCodec.cs:209 RepairForDisplay | RECOVERY | persisted invalid data -> bounded legal fields | parser/repository repair; no topology or IO |
| StickyUiCommand.cs FromData/CreateWorkingCopy/ApplyTo vs ApplyContentTo | COMPATIBILITY MIRROR | detached record copying vs content-only application | boundary; content-only path intentionally leaves placement to facts |

MULTI-AUTHORITY RISK means several updates in one orchestration, **not permission to split the transaction blindly**. ApplyReprojectResult also updates canonical/effective/lease and saves; barrier and live batch can write canonical before TryUpdateEffective rejection (details 05/07). Future owner design needs a prevalidated acceptance unit, not another geometry mirror.

## Codec exact compatibility

StickyNoteCodec.cs:13–18: v10=32 fields; v11=37, current v11. ParseLine recognizes v1..v11 minimum field counts; SerializeLine always emits current 37. StickyImportBackupValidator performs stricter version/field-count validation for raw backup input.

RepairForDisplay clamps physical width/height to note limits; it does not move X/Y onto a monitor. Missing v10 DisplayId clears its local values; present ID clamps local dimensions to 1..20000. Preferred key is trimmed and cleared when over the key limit; missing key clears preferred local values; a present key with nonpositive dimensions clears the entire preferred placement (not fake 1x1); otherwise it bounds dimensions to 20000. It repairs content/font/opacity as well. It is not a migration-to-current-display chooser.

Tracked fixtures: Tests/Fixtures/sticky-v1.txt through sticky-v11.txt, plus sticky-vFuture.txt. Actual future-schema protection is in repository LoadFromFile/BlockFutureSchema **before** falling back to older backup; codec returning null for unknown alone would be insufficient. SelfTestRunner RunStickyFutureSchemaChecks explicitly tests old-reader/current+1 primary + older backup, unchanged primary/backup hashes, no salvage/recovery artifacts, blocked sync/async saves. This safety bridge remains mandatory.

## H-GEO-1 disposition

Supported as a target, **not true today**: preferred + effective could cover normal runtime, but physical fallback still provides old/missing-display recovery, and live layout/preview/tab overlap still read compatibility fields. PC-3 must migrate readers before PC-8 considers schema cleanup. No field can retire in PC-1.
