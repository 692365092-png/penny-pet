# 整组恢复验证证据

2026-09-13，在 PR #4 的 `410dc4f` 基础上实施，经 `349af93` WIP 保全检查点后补齐本轮回归。

- `direct-results.json`：464 次实际 MSTest 方法 / DataRow 调用，0 失败，其中 119 项标记为源码边界检查。
- `direct-output.log`：辅助执行器汇总。
- `tests-build.log`：net8 测试项目 Release 构建，零警告、零错误。
- `net48-reference-compile.log`：全部产品 C# 与原生自测源码的官方 net48 引用程序集编译，零警告、零错误。
- `vstest-startup-failure.log`：标准宿主在执行前发生的内部异常。
- `source-sha256.json`：实际构建和辅助执行对应的全部 236 个 C# / 项目文件哈希，不包含生成物。

SDK 为 .NET 8.0.425。辅助执行器和临时 net48 编译项目源码沿用 [integration-evidence](../integration-evidence/README.md)。运行方法见 [上一轮说明](../horizontal-resize-evidence/README.md)：执行器输出到 desktop-pet/bin/AuditHarness，并复制 Tests/Fixtures，以定位真实源码和磁盘格式样例。

`DockRestoreOperationTests` 新增 23 个行为用例。Windows 宿主接线通过源码边界检查和完整编译确认；这里没有真实 WPF 鼠标、HWND、IME、热插拔、焦点、Z-order、打包或原生自测执行结果。

完整修改、取消补偿的限度和仍开放的旧格式分支见 [本轮审查](../2026-09-13-restore-operation-review.md)。
