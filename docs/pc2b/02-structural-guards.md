# PC-2B.1 structural guard isolation

PC-2A / PC-2A.5 CLOSED. Behavior golden remains `8286c5629b82c51f55e41a6f987c6217e87dfcbb`. PC-2B.0 PASS / REVIEWED per owner approval. PC-3+ BLOCKED.

Only source-structure guards were relocated in this checkpoint. No `SelfTests/` directory was created. `PennySelfTests.cs`, `SelfTestRunner.cs`, `CoreBehaviorTests.cs`, all production files and project files are unchanged.

The 102 methods formerly in `InputAnimationBoundaryTests.cs` now occupy eight domain files under `Tests/StructuralGuards/`. Partial declarations preserve the original class and fully qualified test identities, including the class-level `ArchitectureSourceBoundary` category. Mixed files retain behavior methods in their original files; only their guard methods move to partial declarations in StructuralGuards. The one guard inside `CoreBehaviorTests.cs` remains there under the explicit instruction not to split that file in this checkpoint.

`SourceGuardText` contains the original normalized `ReadSource`, `Between`, root lookup and Dock commit source helper. Its `RawSource` nested helper preserves the topology guards' different raw-text reader, lookup and exception behavior, together with the original brace slicer. Existing topology helper entry points delegate there so qualified callers and imports remain valid. No assertion strings were changed.

Validation:

- All 336 method names, method attributes and complete assertion bodies compared equal to `cb2c7be` after newline normalization.
- Release: 0 warnings / 0 errors.
- MSTest: 336 methods, 350 expanded cases, 350 PASS, 0 skipped.
- Modular and single-file SelfTests: exit 0, root true, all 306 baseline `_ok` paths true; identical sets and values; 0 deleted / 0 renamed.
- Production diff = 0; fixture and protocol shapes unchanged.
- `git diff --check` PASS.

Stop after committing this checkpoint for review. PC-2B.2 and PC-3+ have not started.
