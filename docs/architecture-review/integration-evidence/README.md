# 整合验证证据（2026-09-12）

| 验证 | 实际结果 | 边界 |
| --- | --- | --- |
| `PennyPet.Tests.csproj` Release 编译 | 0 警告、0 错误 | .NET SDK 8.0.425 |
| 完整产品源码 + 官方 net48 引用程序集 | 0 警告、0 错误 | 临时单程序集编译；不是 Windows 资源打包和运行验收 |
| 辅助执行器直接调用 MSTest 方法及 DataRow | 375 通过、0 失败；其中 118 项带 ArchitectureSourceBoundary 标签 | 含逻辑、源码断言和真实文件 I/O；不能称为 375 项 Windows UI 测试 |
| 标准 VSTest 17.11.1 | 在执行测试前内部 NullReferenceException | 详见 `vstest-startup-failure.log`，没有报成 PASS |
| Windows HWND / IME / 焦点 / 多屏 DPI / 整包验收 | 未执行 | PR 保持 draft |

`direct-results.json` 逐项列出执行结果；`source-sha256.json` 标识当前代码/项目输入。产品代码在 net48 检查后未再改变，随后只补充了两组保存行为测试。所有日志均保留原始输出中的本机路径。

相对实验分支原有案例，新增：12 个几何案例、5 个队列案例、8 个 Repository/文件案例（其中隐藏成员保存通过两条入口执行）。此前 11 个过时几何断言已迁移；重定位状态检查改成实际运行状态转换。原生 SelfTest 的结果键保留，但未以编译结果冒充这些键已通过。

Repository 测试只替换 Windows 诊断输出端，使用实际 Repository、codec、writer 和 AtomicTextFile。阻塞测试在文件内容枚举阶段暂停主文件写入，验证独立导出在释放它之前完成，能够检测原来的 AtomicTextFile 全局锁。

## 复现辅助检查

先从仓库根目录运行：

```sh
dotnet build desktop-pet/PennyPet.Tests.csproj -c Release
```

把本目录的 `direct-runner.cs.txt` 和 `direct-runner.csproj.txt` 分别复制到独立临时目录的 `Program.cs`、`DirectRunner.csproj`。执行器仅支持当前测试使用的 TestMethod/DataRow；出现生命周期或动态数据测试时会拒绝继续，不能拿它替换正式宿主。

```sh
dotnet build /absolute/temp/DirectRunner.csproj -c Release -o desktop-pet/bin/AuditHarness
cp -R desktop-pet/bin/Release/net8.0/Tests desktop-pet/bin/AuditHarness/
dotnet desktop-pet/bin/AuditHarness/DirectRunner.dll \
  desktop-pet/bin/Release/net8.0/PennyPet.Tests.dll \
  /absolute/temp/direct-results.json
```

执行器放在 `desktop-pet/bin` 下，是因为现有源码断言从执行目录向上定位仓库。`net48-reference-compile.csproj.txt` 记录引用程序集检查的配置，复用时需把 Compile 的相对路径指向当前源码。这项编译不运行 HWND、WPF Dispatcher、IME 或 Win32 DPI API。
