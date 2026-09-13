# 旧格式 Dock 恢复统一验证证据

2026-09-13，基于 PR #4 的 `3ae78f6`。

- `direct-results.json`：485 次实际 MSTest 方法 / DataRow 调用，0 失败；其中 120 项标记为 ArchitectureSourceBoundary。
- `direct-output.log`：最终辅助执行器汇总。
- `tests-build.log`：net8 测试项目 Release 构建，零警告、零错误。
- `net48-reference-compile.log`：全部产品 C# 与原生自测源码的官方 net48 引用程序集编译，零警告、零错误。
- `vstest-startup-failure.log`：标准宿主执行前的内部 NullReferenceException。该启动尝试发生在最后一个测试输入修正及两处不可达 null 判断删除之前；修正后重新编译并执行完整辅助回归，未重复追查宿主故障。
- `source-sha256.json`：最终构建及辅助执行对应的 237 个 C# / 项目文件 SHA-256，不包含生成物及美术资源。

SDK 为 .NET 8.0.425。辅助执行器与临时 net48 编译项目源码沿用 [integration-evidence](../integration-evidence/README.md)，运行位置及 Fixtures 布置沿用 [horizontal-resize-evidence](../horizontal-resize-evidence/README.md)。开始时 SDK 首次使用/依赖缓存未就绪，初次构建失败；重新 restore 后上列两项最终构建通过。

本轮新增 20 个行为用例和 1 个源码边界检查。辅助反射调用实际测试方法，不等于标准 MSTest 宿主、Windows HWND/WPF 行为、原生自测或打包验收通过。

兼容语义、删除范围、生产代码规模和未结案事项见 [审查说明](../2026-09-13-legacy-dock-recovery-review.md)。
