# Penny-Pet 技术交接

本文对应当前发布版本 `1.0.1`。

## 第一天阅读顺序

1. `README.md`：产品、构建入口和仓库结构。
2. `LICENSE`：软件源代码的 GPL-3.0-or-later 许可。
3. `ASSET_LICENSE.md`：泥泥NINII原创美术资源的独立版权与使用限制。
4. `ARCHITECTURE.md`：当前技术边界与 macOS 迁移地图。
5. `DEVELOPER_GUIDE.md`：日常维护入口、高风险区域和最低验证方式。

## macOS 移植边界

- `PennyPet.Core` 是共享规则层；不得加入 `System.Drawing`、WinForms、WPF、Win32、Registry、UI Automation 或 Windows 路径语义。
- `Core/StickyNotes/StickyDockGeometry.cs` 中的 `DockPoint`、`DockSize`、`DockRect` 是平台无关几何值对象。该文件保存 Dock 统一布局、divider、header 可达性、恢复、新窗口级联、侧边页签、弹窗和异常拖拽恢复的纯数值规则。
- Windows Coordinator 负责读取 `Screen`、DPI、光标和窗口状态，把 `Point`、`Size`、`Rectangle` 转成 Core 几何值，再执行真实窗口移动、缩放和 Z-order 副作用。
- `DockCoordinateSafetyLimit = 30000` 是 Win32 平台约束，由 Windows 层作为输入提供给 Core 计算，不是 Penny 业务规则或跨平台常量。
- `Core/Startup/PetStartupRules.cs` 只保存 UI/美术均完成后释放 Loading 的纯门禁。Windows 的 `PetStartupCoordinator` 保留 Timer、注册表、窗口创建、首帧等待和事件触发；这不是完整的跨平台启动状态机。
- HTTP(S)、盘符、UNC、危险扩展名、确认文案、文件探测和 Shell 打开属于 Windows `Features/StickyNotes/StickyNoteLinks.cs`、`StickyLinkPolicy.cs` 与 `StickyLinkService.cs`；macOS 文件目标必须按自身路径和安全语义实现。
- `Core/DailyNote/DailyNoteFeature.cs` 已保存三十日进度的纯日期判定；后续 UI、内容来源和持久化仍由真实产品需求决定。
- `StickyNoteWindow`、IME、Win32 窗口消息、全局键盘 Hook、GDI 渲染、Shell 与 Registry 都是 Windows-only；未来 macOS 应重写平台实现，不要为形式统一而抽象高风险事件时序。
- 键盘隐私 Core 只接收 `sensitiveTargetDetected` 与 `inspectionAvailable`。UIA、Win32 密码样式、凭据窗口或未来 macOS Accessibility / Secure Input 的证据合并都属于平台检测器。
- “代码没有 Windows using”只证明编译依赖边界；盘符、UNC、窗口坐标限制和权限模型等语义上的 Windows 特有规则仍需人工检查。

## 美术与构建输入

`art/` 包含受独立美术版权声明保护的资源。它们作为构建输入被转换、缓存或打包进 EXE，不会因此变成 GPL 授权的美术资源。复制、修改、替换、导出或在其他产品中复用前必须阅读 `ASSET_LICENSE.md`。

新增资源时必须记录来源和许可。第三方或单独标注的资源适用其自身声明，不得默认继承软件 GPL，也不得擅自归为泥泥NINII所有。

## 未解决的高风险点

- Dock 的拖拽、隐藏/恢复与多窗口同步仍依赖 Windows 窗口生命周期；只迁移已能用纯数值和数据表达的规则。
- IME、WPF/WinForms 消息桥和键盘焦点采证据的时序不能为抽象而重写。
- `PennyPet.Tests` 的 Core 程序集引用门禁可以阻止平台程序集进入 Core，但不能证明规则在语义上跨平台。
- 当前没有 macOS UI 工程、完整 Application 层、网络服务或完整平台无关启动状态机，不应在文档或新代码中声称已经存在。

## 后续原则

未来新增业务功能应先判断哪些规则能保持平台无关，避免把网络、业务状态和持久化决策直接塞入 Windows 窗口类。具体 Feature/Application 结构由届时负责的程序员依据真实需求设计，本仓库不预设类名、目录、DI、Repository interface 或空工程。

每次只迁移一组可在无窗口环境验证的规则；Windows Coordinator 继续负责平台事实、类型转换和副作用。完整 Windows SelfTest 与发布验证留给 CI/发布阶段。

## 当前 hosted / legacy 双路径

- `PetForm` 在 WinForms STA 持有 canonical `StickyNoteData`、`StickyHostedRuntime` 和 Side Tabs。
- `StickyUiThreadHost` 只管理 STA Thread / Dispatcher / async Post / Shutdown，不拥有 WPF Window。
- `StickyUiHost` 是 hosted session facade，管理 session registry、命令路由和 CloseAll。
- `StickyWindowSession` 是唯一持有 `StickyNoteWindow` 的 hosted 会话对象。
- Ordinary、Todo、Schedule 可以任意 mixed Dock；Reminder 不参与 Dock participant eligibility。
- hosted 与 legacy executor 可以混合进入同一 Dock group；两者共用 detached facts、Dock session/Core rules、`DockLayoutTarget` 和 visual feedback，只在最终 effect edge 执行各自窗口副作用。
- hosted/mixed preview、merge pulse、split guide，以及 group move、TopMost、horizontal/divider resize、collapse-reopen、middle split 和多成员 insertion 已完成。
- persisted docked notes 重启后仍可能由 legacy executor 恢复；canonical Dock relation/order/geometry 不因此改变。
- “展开全部并平铺到此屏幕”会展开全部 note、真正清除 Dock relation，并通过 hosted/legacy owned effect path 平铺。
- Side Tabs 是不激活的 TopMost Pet chrome；monitor、work area 或 Pet scale 改变时会重新验证左右 split，仅在分配变化时 rebuild。
- Side Tabs 继续由 WinForms Pet STA 承载；`SideTabSnapshot.ToDisplayData()` 保留为 compatibility adapter。

## Startup loading ownership

- `StartupLoadingForm` 直接读取 embedded `PennyPet.Startup.Loading`，在 Pet-size transparent canvas 内等比、水平居中、底部对齐；它不依赖 `PetArtPackage` 或 Sticky runtime。
- `StartupLoadingThreadHost` 是临时 WinForms STA，独立运行 loading message loop；`BringToFront` / `Close` marshal 回该线程，关闭后线程退出。
- `PennyApplicationHost` 确认 loading 已呈现后，仍在主 Pet STA 构造 `PetForm`。Art decode、Sticky 初始化与恢复不属于 loading thread。
- `_startupUiReady + _startupArtReady`、normal frame suppression 和 `StartupReady` 语义保持不变。`PetStartupRules` 只是这两个 readiness 输入的纯 gate，不是完整 startup framework。
