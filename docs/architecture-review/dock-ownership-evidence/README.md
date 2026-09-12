# Dock ownership 验证证据

本目录记录 PR #4 在 `1ed21da` 工作检查点之后完成的验证。源码 SHA-256 清单包含本次所有 C# / 项目文件；不是原生 Windows 验收证明。

- `direct-results.json`：393 个具名 MSTest 方法/DataRow 结果，含 116 项源码边界检查。
- `direct-output.log`：辅助执行器输出。
- `tests-build.log`：net8 测试程序集 Release 编译。
- `net48-reference-compile.log`：官方 net48 引用程序集下完整产品源码编译。
- `vstest-startup-failure.log`：标准宿主在测试执行之前的内部错误。
- `source-sha256.json`：本轮源文件内容标识。

辅助执行器与 net48 编译项目沿用 [上一轮证据](../integration-evidence/README.md) 的 `direct-runner.cs.txt`、`direct-runner.csproj.txt` 和 `net48-reference-compile.csproj.txt`。SDK 为 .NET 8.0.425。

复现时，将辅助执行器输出放在仓库 `desktop-pet/bin/AuditHarness`，并将 `desktop-pet/Tests/Fixtures` 复制到该输出目录下的 `Tests/Fixtures`。源码边界检查通过向上查找定位源码；fixture 检查从执行器基目录读取文件。然后向 DirectRunner 传入测试程序集绝对路径与结果 JSON 路径。

先完成测试程序集构建；执行器使用测试程序集目录解析真实 MSTest 依赖，只调用受支持的 TestMethod/DataRow/Task。它拒绝生命周期 fixture 或动态数据方法，没有模拟这些能力。当前套件无此类方法。

Windows 消息循环、IME、DPI、显示拓扑切换、Z-order 与发布资源不由这个辅助运行器验证。
