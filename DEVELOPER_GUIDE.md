# Penny pet 1.0 开发交接说明

本文是当前 Windows 版本的日常维护地图。跨平台边界与 macOS 迁移结论见 `ARCHITECTURE.md`；软件与美术的许可边界分别见 `LICENSE` 和 `ASSET_LICENSE.md`。

## 1. 项目与构建

Penny pet 是 Windows 桌宠，包含透明角色动画、便利贴、三态待办、日程、提醒、侧边页签、Dock、缩放、开机启动和可选按键显示。

仓库使用 `PennyPet.sln`：

- `PennyPet.Core`：`netstandard2.0` 平台无关模型和纯规则。
- `PennyPet.Tests`：`net8.0` Core 测试和程序集边界门禁。
- Windows Core、App、Tools、SelfTests 与兼容单 EXE：.NET Framework 4.8。

在项目根目录运行：

```powershell
dotnet build '.\PennyPet.sln' --configuration Release
dotnet test '.\desktop-pet\PennyPet.Tests.csproj' --configuration Release
```

生成资源完整的单文件测试版：

```powershell
& '.\desktop-pet\build.ps1' -OutputFile '.\Penny pet-test.exe'
```

`PennyPet.Windows` 的 Release 构建使用 `PennyPet.Tools` 生成美术发布包和启动缓存并嵌入 EXE；`build.ps1` / `BuildOfficialRelease` 只复用该构建并复制产物。

## 2. 程序入口

- `desktop-pet/Program.cs`：兼容单 EXE 的命令路由与正常启动入口。
- `desktop-pet/PennyApplicationHost.cs`：单实例、loading 和异常兜底。
- `desktop-pet/PetForm.cs`：Windows 桌宠窗口构造、关闭和位置生命周期。
- `PetStartupCoordinator.cs`、`PetAnimationRuntime.cs`、`PetBubbleCoordinator.cs`、`PetMenuActions.cs`：`PetForm` 的职责 partial。
- `desktop-pet/PetContextMenu.cs`：右键菜单构造与命令绑定。
- `desktop-pet/StartupLoadingForm.cs`：启动 loading；等待 UI 与美术 readiness。

这些 partial 文件是代码定位边界，仍共享同一个窗口状态。不要把它们包装成大量单实现接口或仅为缩短文件继续切碎。

## 3. 模块地图

### 桌宠与美术

- `Core/Animation`：动画状态、优先级、概率、冷却和资源预加载纯规则。
- `Core/Art`：美术清单、状态别名、渲染参数和帧时长纯规则。
- `PetArt.cs`：发布资源包、启动缓存、位图解码和运行时缓存。
- `Features/Art`：GDI 动画帧生命周期、画布对齐和内描边。
- `LayeredSpriteRenderer.cs`：把 ARGB 位图提交到 Windows 透明分层窗口。
- `Infrastructure/Persistence/WindowsDataPaths.cs`：统一 `%LocalAppData%\PennyPet` 路径入口。

> **美术许可维护提示：** `art/` 和相关原创视觉资源是构建输入，不表示其许可与软件源代码相同。修改、复制、替换、导出或在其他产品中复用前必须阅读根目录 `ASSET_LICENSE.md`。GPL-3.0-or-later 的软件许可不能作为使用泥泥NINII美术资源的授权依据。

新增资源时必须记录来源和许可。第三方或单独标注的资源适用其自身声明，不得默认继承软件 GPL，也不得擅自归为泥泥NINII所有。

### 便利贴、待办、日程与 Dock

- `Core/StickyNotes/StickyNoteModels.cs`
  - 便利贴、三态 Todo、Schedule 和 Dock 持久化模型。
  - `StickyDockGroups`：组顺序、父子关系、规范化和快照恢复。
- `Core/StickyNotes/StickyNoteCodec.cs`：v1-v9 数据行编解码、内容限制和旧格式兼容。
- `Core/StickyNotes/StickyDockOperations.cs`：组插入、抽离、隐藏槽位、快照、统一置顶数据和长按拆分判定。
- `Core/StickyNotes/StickyTabDropSession.cs`：跨 OLE 嵌套消息循环的页签拖放事务。
- `Core/StickyNotes/DockGeometry.cs`：`DockPoint`、`DockSize`、`DockRect`，以及 Dock 统一布局、divider、header 可达性、恢复、新建、页签、弹窗和异常拖拽恢复的纯数值规则。
- `Core/StickyNotes/StartupRestorePlanner.cs`：每个可见 Dock 组件恢复一次的顺序和 UI/美术 readiness 纯门禁；不是完整启动状态机。
- `Features/StickyNotes/StickyNoteRepository.cs`：Windows 文件读取、迁移、备份、原子保存、dirty、重试和紧急导出。
- `Features/StickyNotes/StickyNoteWpf.cs`：WPF 窗口构造、总体生命周期、持久化和外观接线。
- `Features/StickyNotes/StickyEditorCoordinator.cs`：RichText、字体、焦点和 IME；最高风险。
- `Features/StickyNotes/StickyTodoCoordinator.cs` / `StickyScheduleCoordinator.cs`：待办和日程 UI。
- `Features/StickyNotes/StickyReminderCoordinator.cs` / `StickyAppearanceCoordinator.cs`：提醒条和外观 UI。
- `Features/StickyNotes/StickyNativeWindowBehavior.cs`：Win32 消息、拖拽、resize 和最大化拦截。
- `Features/StickyNotes/PetStickyDockCoordinator.cs`：Windows 屏幕/DPI/窗口事实采集、原生几何转换和真实窗口副作用。
- `Features/StickyNotes/StickyNoteTabs.cs`：侧边页签和隐藏/恢复 UI。

`DockGeometry` 持有平台无关几何，Windows Coordinator 负责把 `Point`、`Size`、`Rectangle` 转成 `DockPoint`、`DockSize`、`DockRect`，调用 Core 后再移动或缩放窗口。`DockCoordinateSafetyLimit = 30000` 是 Win32 限制，由 Windows 层传入，不能下沉成跨平台业务常量。

Dock 修改必须同时检查：组关系、组内顺序、持久化快照、统一宽度、相邻高度、隐藏/恢复、插入/抽离、置顶和屏幕安全边界。不要把 Dock 简化成“坐标相邻”。

### 链接边界

- `Core/Links/StickyNoteLinks.cs`：当前只识别 HTTP(S) 并保存平台中性的匹配数据；不得解释盘符、UNC 或 Windows 文件扩展名。
- `Features/StickyNotes/StickyLinkService.cs`：Windows 盘符/UNC、危险扩展名、确认文案、文件探测和 Shell 打开。
- `Features/StickyNotes/StickyLinkCoordinator.cs`：WPF 链接格式、点击和小手光标。

未来 macOS 文件目标必须使用自己的路径和安全语义，不能复用 Windows 盘符/UNC 策略。

### 提醒、设置与启动

- `Core/Reminders`：提醒模型、时间刷新和气泡替换规则。
- `PetReminderWindowsCoordinator.cs` / `ReminderUi.cs`：Windows 提醒 UI 和协调。
- `Core/Settings/PetSettingsData.cs` / `PetSettingsCodec.cs`：平台中性设置、`StartAtLogin` 语义和旧 INI 兼容。
- `PetSettings.cs`：Windows 数据目录、备份、原子保存、dirty 和失败通知。
- `StartupRegistration.cs`：Windows Registry 开机启动。
- `PetStartupCoordinator.cs`：Windows Timer、窗口创建、首帧等待、注册表和事件协调。

当前 Core 只有 `StartupRestorePlanner` 的恢复/readiness 小规则，不得将其描述为完整跨平台启动框架。

### 键盘显示与隐私

- `Features/KeyboardOverlay/GlobalKeyboardActivity.cs`：Windows 全局低级键盘 Hook。
- `KeyboardFocusSnapshot.cs` / `SensitiveInputDetector.cs`：焦点快照、UIA、Win32 密码样式和凭据窗口证据。
- `KeyboardInputFormatter.cs` / `KeyboardOverlayForm.cs`：Windows 虚拟键显示和透明覆盖窗口。
- `Core/Keyboard/KeyDisplayAccumulator.cs`：连按/长按计数状态机。
- `Core/Keyboard/PetKeyboardPrivacyPolicy.cs`：首次确认、偏好和检查失败时 fail-closed 的平台中性判定。

按键显示默认关闭；关闭必须卸载 Hook。第三方自绘控件、浏览器、跨权限窗口和远程桌面无法保证全部识别，产品文案只能说“尽力隐藏”。

## 4. IME 与富文本：修改前必读

以下 Windows 行为是兼容边界，不要为跨平台形式统一而重写：

- 工具栏取得焦点时保存 RichTextBox 选择区。
- 字体/字号使用 WPF 原生 `TextSelection` / `FlowDocument`。
- IME composition 期间不插入、替换、保存抓取或重写组合文本。
- 工具栏操作后排队恢复编辑器焦点。
- WinForms 主循环与 WPF modeless keyboard interop 桥接。

这些代码曾用于解决中文无法输入、候选框错位、格式回退、重复提交和组合拼音被提前固化。修改后必须进行真实中文输入法回归。

## 5. 用户数据与恢复

运行数据目录：`%LocalAppData%\PennyPet`。

- `settings.ini` / `.bak`：位置、大小、启动偏好、键盘显示和提醒。
- `sticky-notes.dat` / `.bak`：便利贴、待办、日程、显示状态和 Dock。
- `diagnostics.log`：本地异常诊断。

兼容逻辑仍会从旧品牌目录导入数据。读取失败时先尝试 `.bak`，无法读取的源文件会保留为损坏备份。写入失败时保持 dirty 并重试；退出前仍失败时允许重试、导出快照或取消退出。不要用默认值覆盖尚未安全保留的旧数据。

## 6. 测试与最低验证

`PennyPet.Tests` 负责纯 Core 规则和程序集引用门禁；`PennyPet.SelfTests` 负责必须加载 Windows 产品程序集、真实资源或平台适配器的场景。

文档或许可说明修改只需检查路径、链接和 `git diff --check`。代码修改按风险选择最小验证：

1. 先运行相关 Core 测试或 Release 编译。
2. 输入/IME 修改增加真实中文输入法手测。
3. Dock 修改手测单张、组、插入、抽离、隐藏/恢复、多屏和统一置顶。
4. 存储修改使用旧数据副本测试主文件损坏、备份恢复和重启一致性。
5. 动画修改以实际 `PetArtPackage` 检查帧数、时长、透明度和基线。

自动测试不能替代真实 IME、窗口消息、透明窗口、多屏 Dock、Keyboard Hook 和系统权限行为。

## 7. 后续维护原则

- 保持 WinForms + WPF 技术栈，除非有明确迁移任务。
- 只把平台无关、可用无窗口测试保护的规则移入 Core。
- “没有 Windows using”与“语义平台无关”分别检查。
- 不全面重写 Dock、IME、富文本或窗口事件时序。
- 不为未来业务创建空的 Application、Mac、Network、DI 或 Repository abstraction。
- 未来业务 Feature 的具体目录、类名、状态机和持久化方式由届时程序员根据真实产品需求决定。

未来新增业务功能应优先判断哪些规则可保持平台无关，并避免把网络、业务状态和持久化决策直接塞入 Windows 窗口类。具体架构不由本文预设。
