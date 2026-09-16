# Current test evidence inventory

Inventory base: `c34e057f7c8ef0c7133e1be7409be5952cd4bfbb`.

`test-classification-current.csv` contains one row per discovered case: 350 rows for 336 methods. Parameterized cases retain the original method name, with the DataRow arguments and case index in `notes`. This reconciles the inventory with the fresh MSTest result; counting methods alone would undercount by 14.

| Evidence kind | Cases |
|---|---:|
| PURE_BEHAVIOR | 148 |
| PROTOCOL_BEHAVIOR | 21 |
| MIGRATION | 46 |
| SOURCE_STRING_GUARD | 135 |
| WINDOWS_INTEGRATION | 0 |
| PACKAGING_RESOURCE | 0 |

The last two zero counts apply only to the net8 MSTest inventory. They do not mean Windows and package coverage is absent: it is supplied by the separate net48 SelfTest hosts and must remain there.

Classification uses the PC-1 inventory as a starting point and examines current method bodies and reachable file-local helpers. The added divider mailbox cases execute `DockDividerFollowerMailbox`; settlement cases execute `StickyDockGeometry`; startup placement cases reach `PetPlacementPolicy.ResolveStartupPetPlacement` through `Resolve`; effective watermark cases execute `StickyPlacementRuntime`. The added resize completion check reads production source and remains a structural guard. Test names are identifiers, not the basis for evidence classification.

The CSV `notes` lists observed call identifiers. `domain` is a primary routing label based on the inspected body/helper closure; cross-domain tests retain their full assertion bodies and require destination review during migration. `category` records existing attributes, including class-level attributes, rather than inventing categories. `uses_source_reader` includes indirect source helper calls and directory-based source scanning. Fixture dependencies include reachable fixture helper references.

Important mixed-file decision:

- `DockDragLifecycleContractTests` contains three source guards, one pure behavior case calling `StickyDockOperations`, and one protocol case executing `DockInteractionSession`. Do not move the whole file into StructuralGuards without splitting the evidence types.
- All five `DockZOrderContractTests` cases inspect source wiring. They may move to StructuralGuards, but they do not prove actual native Z-order behavior.
- `InputAnimationBoundaryTests` already carries the class-level `ArchitectureSourceBoundary` marker. Its existing assertions and helper error behavior must be retained when physically split.

Windows ownership remains unchanged at this checkpoint. `SelfTestRunner.cs` still owns `Run`, domain checks, report assembly, Windows probes and previews. `PennySelfTests.cs` still contains `Pc2Scene`, `Pc2Context`, reflection helpers and the characterization checks. The current harness includes A7 in addition to A1–A6; preserve that existing coverage too. `RunPc2CharacterizationChecks` is still called by the real SelfTest entry point, and its evidence remains in both fresh reports.

Future domain moves must preserve native HWND/DPI/IME/dispatcher/Z-order/shutdown checks, real repository IO and all historical/future fixtures. Math/protocol MSTest cases do not replace these integration checks. No guard or duplicate test is retired by this inventory.

Next checkpoint: PC-2B.1, physically isolate structural guards by domain, retain method names and assertion bodies, then verify exactly 350 cases and unchanged SelfTest report keys before committing and stopping for review.
