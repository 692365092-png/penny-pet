# PC-2B.0 baseline

```text
branch=codex/project-system-experiment
behavior_golden=8286c5629b82c51f55e41a6f987c6217e87dfcbb
working_head=c34e057f7c8ef0c7133e1be7409be5952cd4bfbb
human=PASS (reported by the supplied next-work guide; not re-performed here)
Release=PASS (0 warnings / 0 errors)
MSTest=350/350
MSTest_methods=336
skipped=0
modular_SelfTests=PASS
single_file_SelfTests=PASS
git_diff_check=PASS
PC2B=GO
PC3=BLOCKED
```

Fresh local verification on 2026-09-08, after fast-forwarding the existing local branch to the exact working HEAD above:

```powershell
dotnet build PennyPet.sln -c Release --no-restore
dotnet test desktop-pet/PennyPet.Tests.csproj -c Release --no-build
git diff --check
```

The Release build produced both `desktop-pet/bin/Release/net48/PennyPet.SelfTests.exe` and `desktop-pet/bin/Release/net48/Penny pet.exe`. Both were run with `--self-test=<fresh-report-path>`; the single-file process was explicitly awaited. Both exit codes were 0. These are fresh reports, not values copied from prior package metadata.

| Report | root `ok` | `_ok` paths | false `_ok` paths |
|---|---|---|---|
| baseline-modular.json | true | 306 | none |
| baseline-single-file.json | true | 306 | none |

The sorted complete paths are recorded in `baseline-selftest-ok-keys.txt`. Modular and single-file key sets and corresponding boolean values are identical. Future checkpoints must compare against this frozen set: zero deleted keys and zero renamed keys. Root `ok` is checked separately, not counted as an `_ok` path.

`capture-baseline.ps1` records the current test inventory and compares the two supplied reports. It must only regenerate the baseline at the original working HEAD; later checkpoints must compare their reports with these frozen artifacts instead of overwriting them.

PC-2B.0 only adds documentation and capture tooling under `docs/pc2b`. Production files, test assertions, command prefixes, fixtures, schema, and project compile boundaries are unchanged. No main merge was performed. Stop after this checkpoint commit for diff review.
