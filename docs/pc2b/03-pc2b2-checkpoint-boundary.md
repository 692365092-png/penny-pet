# PC-2B.2: read-only comparison, compile boundary and platform inventory

## Identity and scope

```text
start_head=0f5cd49bb2c8348c5071c59644e9c0c37417e1fa
behavior_golden=8286c5629b82c51f55e41a6f987c6217e87dfcbb
baseline_methods=336
baseline_cases=350
baseline_ok_keys=306
capture_baseline=FROZEN
compare_checkpoint=READ_ONLY
selftests_compile_boundary=ADDED
production_behavior_changed=NO
test_implementation_changed=NO
schema_protocol_ownership_changed=NO
PC2B3=BLOCKED_PENDING_REVIEW
PC3=BLOCKED
```

The only changed paths are this document, `compare-checkpoint.ps1`,
`test-classification-working.csv`, `desktop-pet/SelfTests/README.md`, and
`desktop-pet/PennyPet.SelfTests.csproj`. No characterization implementation,
report keys, production code, or frozen artifacts moved or changed.

Local commit sequence (push only after the final gate):

1. `a991ab2` — `test: add read-only PC2B checkpoint verifier`
2. `6496795` — `test: add SelfTests domain compile boundary`
3. `docs: classify PC2B test platform scope` — this document and working CSV.

## Read-only verifier

Run from the repository root:

```powershell
& ./docs/pc2b/compare-checkpoint.ps1 -ReportDirectory `
  ./desktop-pet/bin/Release/net48/pc2b2-checkpoint
```

The directory must contain fresh `modular.json` and `single-file.json`.
The verifier has no baseline override or update switch. It checks each frozen
artifact's `HEAD:path` blob identity and unstaged/staged cleanliness before
reading it. The frozen JSON reports, 306-key list and original classification
remain immutable inputs; the historical capture script is not invoked.

It validates baseline self-consistency, boolean-true roots, all 306 frozen keys,
and complete current path/value parity, including additional keys. Additional
keys are allowed by the reusable tool and listed explicitly; PC-2B.2 itself
requires zero additional keys. Non-boolean `_ok` values are rejected rather
than accepted through PowerShell coercion. Paths are case-sensitive; nested
objects and array elements are traversed. The script only reads, compares,
prints and throws; it does not write reports or mutate Git.

Six isolated report probes passed: missing key, false baseline key, mismatched
host key sets, root false and non-boolean value were rejected with the expected
diagnostic; matching additional keys were accepted and listed. Probe fixtures
were created under ignored build output, never over the frozen artifacts.
Windows PowerShell 5.1 probe processes required a process-local
`-ExecutionPolicy Bypass`; the default policy otherwise refused to load the
script before comparison. No persisted execution-policy setting was changed.

## Compile ownership

Only the modular SelfTests project adds `SelfTests\**\*.cs`.
The single-file Windows project already recursively discovers this directory;
it is unchanged. The new directory contains only a README, not a marker class.
`SelfTest` remains the existing owner; no manager or new runtime state exists.

**Review prerequisite before PC-2B.3:** `PennyPet.Windows.Core.csproj` also has a
broad recursive compile glob that currently does not exclude `SelfTests/**`.
There is no effect in PC-2B.2 because the directory contains no C# implementation.
The first future partial-file move must review and explicitly resolve that
overlap within approved scope, so harness code is not compiled into the
production library. This checkpoint does not change that project.

## Working classification

`test-classification-current.csv` remains frozen historical evidence.
`test-classification-working.csv` preserves all original case identities,
categories, evidence kinds, flags, fixtures and notes, adds `platform_scope`
plus an explanatory note, and resolves each method to its current source file.
134 case rows needed path updates after PC-2B.1. Each new path was checked
against the current method declaration; there remain 336 distinct methods.

```text
PORTABLE_CORE=192
PLATFORM_CONTRACT=49
WINDOWS_ONLY=109
UNKNOWN=0
TOTAL=350

PURE_BEHAVIOR=148
PROTOCOL_BEHAVIOR=21
MIGRATION=46
SOURCE_STRING_GUARD=135
WINDOWS_INTEGRATION=0
PACKAGING_RESOURCE=0
```

Scope describes what the case protects, not its folder or evidence strength:

- Pure geometry, placement math, codecs, import planning, content policy and
  deterministic domain state are portable. The daily-interaction ledger is
  portable domain state despite its historical `PROTOCOL_BEHAVIOR` label.
- Detached sequence/epoch acceptance, session replacement, mailbox final-frame
  ordering, effect-result/canonical ordering and Core dependency guards are
  platform contracts. Some are currently protected only by source guards in
  Windows owners; this does not claim executable Mac coverage or move ownership.
- WPF/WinForms/STA, actual native placement, IME, Z-order, packaging and current
  Windows feature wiring remain Windows-only. In particular, a pure SideTab DPI
  metric test does not prove physical glyph rendering, and a Windows Z-order
  source guard is not a portable ordering implementation.

The two zero evidence counts apply only to this net8 MSTest inventory. They do
not imply that the separate Windows SelfTest hosts lack native or packaging
coverage. Classification is an architecture map, not a claim that Windows
runtime dependencies were removed in this checkpoint.

## Automated evidence

The verifier was checked against fresh modular/single-file reports before its
first local commit. After the compile-boundary change, the complete gate passed:

```text
Release solution build (--no-restore)=0 warnings / 0 errors
MSTest (--no-build)=336 methods / 350 expanded cases
passed=350
failed=0
skipped=0
modular SelfTests exit=0 / root=true / _ok=306
single-file SelfTests exit=0 / root=true / _ok=306
baseline_missing=0
baseline_false=0
extra_current_keys=0
current_modular_single_parity=PASS
working_classification_identity_and_paths=PASS
frozen_working_tree_and_index_diff=NONE
compare_script_write_primitive_guard=PASS
git_diff_check=PASS
```

The final gate repeats Release, MSTest, both executable hosts and the comparison
on the final documentation commit before push. A failure blocks push and is
reported separately; no behavior repair belongs in these commits.

## Data lifetime and review boundary

Fresh reports are derived diagnostics, not user-created content. The fixed
`bin/Release/net48/pc2b2-checkpoint` filenames are overwritten on each run;
the six verifier-probe directories are fixed and bounded. Nothing appends to
an unbounded log or writes to user note data.

Human product gate: **NOT REQUIRED** (no product behavior, test implementation,
or SelfTest command/report semantics changed). Automated Windows SelfTests are
still required for both host/packaging paths.

Known debt: the future compile-glob overlap described above; source-only guards
remain source evidence; PowerShell comparison remains Windows closeout tooling,
with cross-platform tooling deferred to PC-10. No state/token, framework,
protocol kind, schema or production-owner change was introduced.

**STOP after the final push. PC-2B.3 requires explicit review approval;
PC-3+ remains blocked.**
