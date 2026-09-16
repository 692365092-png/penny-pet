# Dock 操作顺序验证证据

2026-09-14，基于 `04887f7`，经 `0951287` WIP 保全后完成。

- `direct-results.json`：502 次实际 MSTest 方法 / DataRow 调用通过，0 失败；124 项标记为源码边界检查。
- `direct-output.log`：最终辅助执行汇总。
- `tests-build.log`：net8 测试项目 Release 编译，零警告、零错误。
- `net48-reference-compile.log`：全部产品 C# 和原生自测源码的官方 net48 引用程序集编译，零警告、零错误。
- `vstest-startup-failure.log`：标准宿主在测试执行前发生的内部 NullReferenceException。
- `source-sha256.json`：最终构建和辅助执行对应的 240 个 C# / 项目文件哈希，排除生成物和美术资源。

SDK 为 .NET 8.0.425，辅助执行器和临时 net48 编译项目源码沿用 [integration-evidence](../integration-evidence/README.md)，运行位置与 Fixtures 布置沿用 [horizontal-resize-evidence](../horizontal-resize-evidence/README.md)。最初一次后台编译在会话继续后失去进程，重新运行 restore/build 得到上述最终结果。

本轮新增 13 个行为用例及 4 个源码边界检查。辅助反射调用不能替代标准 MSTest 宿主，也不证明真实 Windows 鼠标时序、HWND/WPF、IME、焦点、Z-order、DPI、热插拔、退出、原生自测和打包验收通过。

变更理由、代码规模及仍未处理的新原生手势入口见 [审查](../2026-09-14-dock-mutation-ordering-review.md)。
