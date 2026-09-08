# PC-1 baseline — 2026-09-08

- Branch: `codex/project-system-experiment`.
- Audited production HEAD / golden: `ab10b45705195bc7564db74c2d1f03dc996917ba` — Make SideTabs more compact without changing DPI semantics.
- Evidence/package reference: `ef9411019d1cf76c995e513884818b785b4b646c`. Read-only diff from golden contains only three release package/metadata/hash files, not product changes.
- Initial tree clean. Working branch was an ancestor and was fast-forwarded to golden; no reset, rebase, merge commit or force push. Display-scale branch remains frozen.
- .NET SDK: `10.0.400`; OS: `Microsoft Windows NT 10.0.26200.0`.
- Inventory scope: tracked C# sources at golden. Generated bin/obj and release packages are not source inventory.
- Data class: finite audit snapshot, replaced on regeneration; not an append-only diagnostic stream.

## Authority and scope

Read both user-provided PC-1 execution guide and Post-DRT master plan v2 dated 2026-09-08. Only `docs/pc1/**` may change. No production/test code, project graph, schema, existing architecture docs, UI behavior or optimization changes. PC-2 through PC-11 BLOCKED until review.

## Recent history at golden

```text
ab10b45 Make SideTabs more compact without changing DPI semantics
8ea4c4a Add PC-0.5 Dock restore corrective test package
078ffb0 Close PC-0.5 Dock restore transaction gaps
b548091 PC-0.5 v3 C/D/E/F: dock restore transaction, SideTabs edge split, manager delete completion, pet tile-all (NOT hand-tested)
f3748cd Add PC-0.5 mixed-DPI stabilization test package
414f669 Rebase active Pet drag across per-monitor DPI handoff
56ee5bb Fix Dock resize physical-pixel contract across mixed DPI
ac2c934 Add DRT-13 N-display release candidate package
```

## Evidence conventions

All code references below are `repository-relative-path:line` at golden, not mutable remote HEAD. Method names identify overloaded/long scopes; CSVs supply discovery indexes, with semantic limitations explicitly labeled. Source guards are not runtime evidence. The supplied master plan reports human PASS and CI PASS for the evidence checkpoint; this audit does not relabel those as new runs.

New automated runs and final diff scope are recorded in PC1_SUMMARY.md. Human runtime test NOT REQUIRED for this docs-only checkpoint.
