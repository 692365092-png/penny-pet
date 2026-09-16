# PC-1 Architecture Inventory

Baseline: `codex/project-system-experiment @ ab10b45705195bc7564db74c2d1f03dc996917ba`.
Evidence/package reference: `ef9411019d1cf76c995e513884818b785b4b646c`.

**PC-1 reviewed and CLOSED. Production changes: NONE. PC-2A GO after docs amendment; PC-2B BLOCKED pending PC-2A PASS and review. Human runtime test: NOT REQUIRED for the inventory.**

## Ten questions answered

| Question | Evidence / answer |
|---|---|
| Q1 PetForm lifecycle/state count | 13 partial files, 12 responsibility domains A–L; 138 direct declarations including constants/event and 19 forwarding properties, not 138 independent owners. 02 + state-owner.csv |
| Q2 physical split vs ownership | all PetForm fragments share one object; animation/bubble/daily/reminder/context-menu/hosted/placement/topology objects have real independent responsibilities. 02/07/11 |
| Q3 three geometry families | all 14 model fields indexed; codec, compatibility transforms, working-copy vs canonical contexts distinguished. 04 + geometry-read-write.csv |
| Q4 placement precedence | session show preferred -> v10 -> bounded physical fallback; Pet temporary rehome and whole Dock restore are distinct paths. Live Dock still reads v10 height, so not all runtime follows preferred. 04 |
| Q5 stale dimensions | topology, gesture epoch, mailbox plan sequence, producer WindowSequence, consumer lease watermark, last-applied plan and pending restore gate each have separate failure modes. 05 |
| Q6 cross-STA counts | three-member root drag: k queued live drains + one begin z-order + two final operations; six final role refresh commands (reset + grouped). Core dispatch/result total k+9, plus unrelated events/optional effects; same-topology DPI adds no dedicated command. 03 |
| Q7 Session necessity vs leak | full method map separates native HWND/IME/events from preferred/v10/fallback policy and compatibility capture. 06 |
| Q8 runtime overlap | actual facts, planned caches, canonical mirrors, session lease and effective watermark distinguished; recreated lease/partial acceptance needs characterization, not speculative patch. 07 |
| Q9 test strength | 317 methods / 331 cases: 134 pure, 36 migration, 134 source guards, 13 protocol; 292 report checks per SelfTest mode, not 292 methods. 08 + test/cluster CSVs |
| Q10 synchronous hot path | actual Pet facts, metrics/split math, native reposition, repeated visible-note GetAll copy/sort/intersections; existing split/DPI/z-order gates avoid unconditional control rebuild. No measured root cause. 09 |

## Top five ownership findings

1. PetForm file separation is not state separation; Dock, display, restore and final canonical commit still converge on shared private fields.
2. Live Dock `PlanDockPlan` depends on v10 `LocalLogicalHeight`, while actual source facts determine DPI/unified width. v10 is not merely a migration reader yet.
3. Session owns necessary native transactions but also preferred/v10/physical placement selection; remove policy only after an explicit intent boundary exists.
4. Hosted and placement watermarks accept different domains. EnsureSession lease reset can lower one counter; effective facts have their own monotonic check. Test recreation plus retained effective facts before restructuring.
5. Some consumer acceptance paths mutate canonical before/without successful effective update. Host batch result alone does not prove atomic Pet-side canonical/effective acceptance. This is an audit risk, not a newly reproduced regression.

## Top five compatibility bridges

1. Physical recovery rectangle and its live preview/tab/layout readers.
2. v10 DisplayId/local geometry still serialized and used by normal planning.
3. v10-to-preferred migration plus incomplete-preferred Dock restore fallback.
4. Per-note session lease synchronization and independent topology/epoch validation.
5. Historical v1–v11 readers and future-primary + older-backup fail-closed protection (must remain).

## Top five test debts

1. Named behavior-looking tests sometimes only inspect source literals.
2. SelfTestRunner/PennySelfTests are partials of one large runner, mixing pure rules, resources, IO, HWND probes and reporting.
3. Need outcome/state-after-rejection characterization for facts barriers and whole-member final commits.
4. Need combined session recreation + effective-registry watermark characterization beyond standalone lease tests.
5. Repeated pure arithmetic can later consolidate, but modular/single-file resource and actual Windows coverage cannot be deleted to reduce counts.

## PERF-OBS-01

Likely investigation route is Pet movement -> SideTab facts/layout/reposition -> visible-note copy/sort/intersection and paint/diagnostics. Existing gates already avoid same-DPI font rebuild and unchanged-split controls rebuild. **Static evidence does not establish the source of perceived stutter.** No optimization or new cache was introduced; logging remains bounded/rotated.

## Delivered files (20)

- 00-baseline.md; 01-code-size-risk-map.md; 02-petform-partial-ownership-map.md
- 03-thread-and-protocol-map.md; 04-geometry-authority-matrix.md; 05-dock-version-state-machine-map.md
- 06-sticky-session-responsibility-map.md; 07-runtime-owner-overlap-map.md; 08-test-debt-map.md
- 09-performance-hotpath-map.md; 10-compatibility-retirement-register.md
- 11-target-ownership-proposal.md; 12-pc2-pc8-recommendation.md; PC1_SUMMARY.md
- code-size.csv; state-owner.csv; geometry-read-write.csv; protocol-message.csv; test-classification.csv; selftest-clusters.csv

All source locations refer to golden, not this docs commit. CSVs are bounded audit snapshots. Lexical field/geometry indexes are navigation aids, explicitly distinguished from a compiler alias graph or runtime measurement; semantic decisions and exceptions are documented in the maps.

## Automated baseline gate executed in this audit

| Gate | Result |
|---|---|
| `dotnet build PennyPet.sln -c Release --no-restore` | PASS, 0 warnings / 0 errors |
| `dotnet test desktop-pet/PennyPet.Tests.csproj -c Release --no-build` | PASS 331, failed 0, skipped 0 |
| modular `PennyPet.SelfTests.exe --self-test=.../modular.json` | 292 `_ok` fields, all true |
| single-file `Penny pet.exe --self-test=.../single-file.json` | 292 `_ok` fields, all true; output checked after GUI executable completed writing |
| diff whitespace and path scope | `git diff --cached --check`; staged paths must all begin docs/pc1/ |
| production/test/project/existing architecture docs | unchanged from golden |
| manual and new package | NOT REQUIRED / no new hand-test release package |

SelfTest output paths: `%TEMP%/Penny-PC1-baseline-gate/modular.json` and `single-file.json`, fixed filenames (overwrite), not committed. Existing build outputs were used; no release package copies. CI/human evidence in master plan is historical, not a new CI/manual claim.

## PC-1 acceptance checklist

PASS-01 exact SHA; PASS-02 all partials; PASS-03 geometry fields/authority; PASS-04 version dimensions; PASS-05 protocol graphs/counts; PASS-06 session methods; PASS-07 runtime overlaps; PASS-08 test classifications; PASS-09 hot path; PASS-10 retirement register; PASS-11 prerequisites; PASS-12 zero production diff.

Recommended next **after approval only**: PC-2 characterization and test-domain decomposition in the order specified in 12. PC-3 onward remains blocked. Local docs-only commit: `Document PC-1 architecture inventory`. No push, no main merge, no behavior change. **STOP for review.**

## Reviewed execution order and ownership correction

PC-2A characterization FIRST (A1–A6 production state-after-rejection), then STOP for review. PC-2B harness/domain decomposition only AFTER PC-2A PASS and approval. A reachable partial canonical/effective mutation is a correctness STOP, not a reason to move tests first.

Pending restore and topology gates are feature/reconcile lifecycles, not drag ownership. Only live/final drag mailbox belongs to the proposed interaction owner; PlanSequence allocation shared by topology and restore remains an independent PC-6 audit boundary. See amended state-owner.csv, 05/07/11/12. This amendment supersedes the original unreviewed next-phase recommendation above.
