# Divider resize 验证证据

2026-09-13，在 PR #4 的 `1931662` 基础上实施。源码 SHA-256 清单对应本轮最终构建与直接测试执行的 C# / 项目文件。

- `direct-results.json`：410 次真实 MSTest 方法 / DataRow 调用通过，0 失败；其中 117 项标记为源码边界检查。
- `direct-output.log`：辅助执行器的汇总输出。
- `tests-build.log`：net8 测试项目 Release 构建，零警告、零错误。
- `net48-reference-compile.log`：完整产品源码与原生自测源码在官方 net48 引用程序集下编译，零警告、零错误。
- `vstest-startup-failure.log`：标准 `dotnet test` 在执行前的 TestEngine.ShouldRunInProcess 内部 NullReferenceException，不能视为标准测试宿主通过。
- `source-sha256.json`：源码内容清单，不包含生成物。

复现方式沿用 [前轮证据](../dock-ownership-evidence/README.md)；辅助执行器及临时 net48 项目源码在 [integration-evidence](../integration-evidence/README.md)。SDK 为 .NET 8.0.425。测试程序集构建后，把辅助执行器输出及 Tests/Fixtures 放在仓库 desktop-pet/bin/AuditHarness 下，向 DirectRunner 传入实际测试程序集与输出 JSON 路径。

新增覆盖取消前后排队批次、旧 final 对更正批次的影响、跨 topology / 过期 / 异来源事件、200 次原生自测中的 live 帧合并路径、独立高度、混合 DPI 物理尺寸、最终源窗口 top 漂移、整批成员次序及实际尺寸验证。200 次 live 帧循环位于原生 SelfTestRunner，本轮只编译；MSTest 会话与邮箱行为检查实际执行。

此目录不证明 Windows 消息循环、WPF / IME、焦点、Z-order、混合 DPI 热插拔、打包或 306-key 原生自测验收通过。
