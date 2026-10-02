# Windows SelfTests

Windows/net48 probes live in domain files here. `SelfTestRunner.cs` only
orchestrates their execution and writes the existing report. Probe bodies,
execution order, report field names, and failure aggregation remain unchanged.

- `SelfTest` remains one partial owner; splitting files is not a new manager layer.
- Keep real HWND, DPI, IME, dispatcher, Z-order, shutdown and packaging probes here.
- Portable Core/domain behavior belongs in `Tests/` / Core tests instead.
- Do not place source-string architecture guards here.
- Do not add production policy solely for test convenience.
- `PennyPet.Windows.csproj` and `PennyPet.Windows.Core.csproj` exclude this directory.
- `PennyPet.SelfTests.csproj` includes them through `SelfTests\**\*.cs`.
- Art, settings, persistence/compatibility, Dock, editor/hosted windows,
  SideTabs, keyboard/privacy, animation, bubble/persona, weather, and display
  probes have separate files. `HarnessTransport.cs` contains shared UI-pump
  transport helpers. The existing `SelfTest` partial owner keeps private helper
  access; no new framework or manager is introduced.
