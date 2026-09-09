# Windows SelfTests

Reserved for Windows/net48 SelfTest harness partials; PC-2B.2 moves no implementation here.

- `SelfTest` remains one partial owner; splitting files is not a new manager layer.
- Keep real HWND, DPI, IME, dispatcher, Z-order, shutdown and packaging probes here.
- Portable Core/domain behavior belongs in `Tests/` / Core tests instead.
- Do not place source-string architecture guards here.
- Do not add production policy solely for test convenience.
- `PennyPet.Windows.csproj` discovers these `.cs` files recursively.
- `PennyPet.SelfTests.csproj` includes them through `SelfTests\**\*.cs`.
- Before moving the first partial, review `PennyPet.Windows.Core.csproj`: its broad
  compile glob currently also discovers this directory. PC-2B.2 adds no C# file
  and does not change that project; the first code move must resolve this overlap
  within its approved scope, not accidentally compile harness code into the library.
