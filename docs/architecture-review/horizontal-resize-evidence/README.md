# 横向 resize 验证证据

2026-09-13，在 PR #4 的 `00b7067` 基础上实施，经 `c0ad4c5` 保全检查点后完成本轮行为回归。

- `direct-results.json`：440 次实际 MSTest 方法 / DataRow 直接调用，0 失败；118 项标记为源码边界检查。
- `direct-output.log`：辅助执行汇总。
- `tests-build.log`：net8 测试项目 Release 构建，零警告、零错误。
- `net48-reference-compile.log`：全部产品源码与原生自测源码使用官方 net48 引用程序集编译，零警告、零错误。
- `vstest-startup-failure.log`：标准 VSTest 17.11.1 在执行前的内部异常，未通过标准宿主验收。
- `source-sha256.json`：本轮实际构建 / 执行的全部 234 个 C# 和项目文件哈希，不包含生成物。

SDK 为 .NET 8.0.425。辅助执行器和临时 net48 编译项目的源代码沿用 [integration-evidence](../integration-evidence/README.md)。前轮复现说明见 [resize-ownership-evidence](../resize-ownership-evidence/README.md)。执行器必须输出到仓库 desktop-pet/bin/AuditHarness，并复制 Tests/Fixtures，供架构边界检查和磁盘格式用例定位源码与样例。

复现主要命令（将 `dotnet` 换成相应 SDK 路径）：

```sh
cd desktop-pet
dotnet build PennyPet.Tests.csproj -c Release
dotnet bin/AuditHarness/DirectRunner.dll bin/Release/net8.0/PennyPet.Tests.dll direct-results.json
dotnet test PennyPet.Tests.csproj -c Release --no-build --no-restore
```

新增 30 个行为用例集中于 `DockHorizontalResizeTests` 与 `StickyResizePreferencesTests`，包括实际 repository / writer / codec 的混合 DPI 尺寸保存往返。两个集中源码检查验证原生批次入口和延后操作接线；它们不能代替真实 Windows 输入测试。

本证据不证明 Windows HWND / WPF / IME、焦点、Z-order、跨屏 / 热插拔、打包或原生自测实际通过；完整限制和待验收操作见 [本轮审查](../2026-09-13-horizontal-resize-review.md)。
