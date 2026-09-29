# R27 art-package experiment

The current PPAP0003 release pack already indexes independently compressed clips. The smallest single-format candidate is to retain that pack and remove the PCAF0003 startup cache. This isolated project compiles the **unchanged production reader/rendering code** twice: with both resources, and with only the release pack. It adds no product flags, decoder or alternate shipping format.

After a Release solution build on Windows:

```powershell
./docs/architecture-review/art-package-benchmark/run.ps1 -ReportPath "$env:TEMP/penny-art-package.json"
```

The script launches seven fresh processes per variant, alternating order. It measures elapsed/CPU time from entering `PetArtPackage.Load(192, 208)` through the synchronous idle-ready boundary, private-byte change and process peak working set. It reports both embedded resource and executable sizes. The OS file cache is not flushed; these are cold-process/JIT samples, **not cold-storage or complete UI first-paint measurements**. Peak working set is an absolute process high-water mark, not peak private bytes. All-state peak memory is recorded separately during verification, including hash work.

Both probes run outside the repository, without an external art directory. The idle source must identify the intended embedded path. Separate verification processes hash every state, frame dimension, frame duration and premultiplied BGRA byte (including transparency). Existing encoders regenerate both files and their SHA-256 values must match the solution build. No source artwork, timing or licensing files are changed. Results retain raw samples and hashes.

This screens one concrete candidate. If removing the pre-rendered idle cache materially worsens idle-ready time or memory, keep the two formats. It does not prove every future indexed format is worse: a new pre-rendered single pack would need a separate experiment covering dimensions, render settings, aliases, size, startup and maintenance cost. No format is adopted merely because it saves one file.

Result: [CI #171 decision](../R27-art-package-decision.md) retains both resources. Use workflow_dispatch with `compare_art_packages=true` to repeat the experiment; ordinary PR builds skip this observational benchmark.
