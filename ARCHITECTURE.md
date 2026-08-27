# Penny 架构与平台迁移地图

本文记录当前实验分支的 Windows 架构、依赖边界和测试范围。它是一份维护地图，不是要求立即跨平台重写的设计稿。正式发布仍以兼容单 EXE 构建通过为前提。

当前原则：保持 Windows 版行为、数据格式和构建结果不变；优先识别边界，不为“架构漂亮”引入接口、依赖注入或新的大型框架。

## 1. 架构验收结论

- `Program` 只处理演示入口和正常启动；测试/预览与美术命令分别委托 `Infrastructure/SelfTestCommandRouter` 和 `Infrastructure/ArtCommandRouter`，独立 App、Tools、SelfTests 与兼容单 EXE 共用同一套路由语义。
- `PetForm.cs` 只保留桌宠窗口构造、关闭和位置生命周期。启动、动画运行时、键盘隐私、气泡、菜单动作和便利贴窗口协调位于职责明确的 partial 文件；它们仍共享同一个窗口状态，不是假装独立的服务层。
- `StickyNoteForm` 仍是 WPF 便利贴窗口本体；编辑器/IME、链接、原生窗口行为、Todo、Schedule、提醒横幅、外观和 Dock 已按职责定位到独立 partial 文件。
- 未发现平台无关核心中的循环依赖；未引入 DI container，也没有 interface / wrapper / service 数量膨胀。
- Windows 窗口协调层仍存在 `PetForm` 与 `StickyNoteForm` 的双向协作。这是 Dock、隐藏/恢复和窗口生命周期造成的 Windows-only 耦合，不能在没有完整窗口回归测试时强拆。
- `PetStickyDockCoordinator` 仍负责高风险窗口坐标和拖拽副作用；组插入、抽离、隐藏槽位和拆分判定已迁入 `StickyDockOperations`，不再由 `PetForm` 定义领域规则。
- 高风险修复阶段已通过完整 SelfTest、输入/消息循环/透明窗口/启动探针和兼容单 EXE 构建。后续行为不变的边界拆分采用一次最小构建验证，完整门禁留给 CI/发布阶段，IME 事件顺序仍未重写。

当前仍值得关注的五个架构问题：

1. `StickyNoteForm` 的 partial 文件仍共享大量窗口控件和状态；这是 WPF/IME 行为保持的取舍，不能误认为已经业务解耦。
2. `PetForm` 的 partial 文件也共享主窗口状态；边界已可查找，但未来新增功能仍不能直接把网络/缓存塞进这些文件。
3. `SelfTestRunner.Run` 仍是一个很长的 Windows 集成测试编排方法；纯 Core 重复项已删除，美术/启动资源、设置持久化、便利贴格式/恢复、Dock、富文本/IME、提醒横幅、Todo、日程、字体、独立对话框、侧边页签、通用窗口策略、提醒协调、键盘覆盖、动画、气泡及启动/缩放/桌宠壳层已分别收口到检查方法，备份清理和三组 Dock 检查也已有统一入口；持久化/历史兼容、便利贴交互及 Dock/窗口几何报告字段由独立 builder 组装。剩余对话框、侧边页签和运行时报告段应继续小步拆分。
4. `PetArt.cs` 仍同时承担发布包生成、位图解码和运行时缓存；清单模型、别名/时长/渲染参数规则已移入 `Core/Art`，画布适配和描边则隔离在 Windows-only 的 `Features/Art/PetArtFrameRenderer.cs`。后续若引入第二个位图后端，再继续拆包 codec 与运行时加载器。
5. 设置、便利贴、美术缓存和诊断已共用 `WindowsDataPaths` 选择 `%LocalAppData%\PennyPet`；具体文件名与读写仍留在各自适配器。未来平台只需提供自己的路径入口，不应把 Windows 目录规则移入 Core。

## 2. 主要模块与依赖方向

依赖方向以“纯规则向外、平台实现向内调用”为目标：

```text
PennyPet.App -> PennyApplicationHost
  -> PetForm (Windows 窗口壳)
       -> PetStartupController / PetAnimationRuntime
       -> PetKeyboardOverlayController / PetBubbleController
       -> Windows keyboard hook / focus inspector / overlay form
            -> PetKeyboardPrivacyPolicy / KeyDisplayAccumulator (Core)
       -> PetStickyWindowCoordinator / PetMenuActions
       -> PetAnimationController (纯动画规则)
       -> PetReminderCoordinator (纯提醒状态/时间规则)
       -> PetReminderWindowsCoordinator (WinForms/WPF 提醒 UI 协调)
        -> PetContextMenu / PetStickyDockCoordinator / Windows UI
       -> PetArtPackage
            -> PetArtRules (Core)
            -> PetArtFrameRenderer -> LayeredSpriteRenderer

StickyNoteForm (WPF 窗口)
  -> Sticky Todo / Schedule / Reminder / Appearance partial 模块
  -> StickyNoteModels
  -> StickyNoteRepository -> AtomicTextFile / ApplicationDiagnostics

Reminder UI / PetForm
  -> ReminderModels

PennyPet.SelfTests -> SelfTest
  -> 纯规则、持久化模块和 Windows UI 探针

PennyPet.Tools -> PetArt 资源包/启动缓存生成

兼容 Program + build.ps1 -> 保留原单 EXE 发布和 CI 命令面
```

原则上，平台无关模块不应反向引用 `PetForm`、WPF、WinForms 或 Win32。美术清单和别名/时长规则已经进入 Core；当前主要平台外围是 `PetArtPackage` 的位图解码/缓存与 `PetArtFrameRenderer` 的 GDI 画布处理，以及 `PetSettings` 的默认路径、备份和诊断。

### 入口和桌宠

| 文件 | 责任 | 分类 |
|---|---|---|
| `Program.cs` | 兼容单 EXE 的演示入口与正常启动 | Windows 兼容入口 |
| `Infrastructure/*CommandRouter.cs` | App、Tools、SelfTests 共用的参数解析、命令路由和失败退出语义 | 工程基础设施 |
| `PennyApplicationHost.cs` | 单实例、loading、异常兜底和正常应用运行 | Windows-only |
| `PetForm.cs` | 桌宠窗口构造、关闭和位置生命周期 | Windows-only |
| `PetAnimationRuntime.cs` | 计时器、资源预载、帧提交和鼠标互动桥 | Windows-only |
| `PetStartupController.cs` | 延迟启动阶段和恢复窗口队列 | Windows-only |
| `Features/KeyboardOverlay/PetKeyboardOverlayController.cs` | 键盘事件、隐私扫描和 overlay 协调 | Windows-only |
| `PetBubbleController.cs` | 气泡生命周期和展示协调 | Windows-only |
| `PetStickyWindowCoordinator.cs` | 便利贴窗口创建、恢复、集中布局和页签协调 | Windows-only |
| `PetContextMenu.cs` | 右键菜单构造与命令绑定 | Windows-only |
| `Core/Animation/PetAnimationController.cs` | 动画状态、优先级、随机选择、冷却和恢复规则 | `PennyPet.Core`，可跨平台 |
| `Core/Animation/ArtPreloadReservations.cs` | 动画资源预加载去重与失败退避 | `PennyPet.Core`，可跨平台 |
| `Core/Art/PetArtManifest.cs` / `PetArtRules.cs` | 美术清单模型、状态别名、渲染参数和帧时长规范化 | `PennyPet.Core`，不引用 Bitmap 或文件系统 |
| `PetArt.cs` | 发布帧包、位图解码、运行时缓存和资源校验 | Windows/GDI 资源适配层 |
| `Features/Art/AnimationClip.cs` / `PetArtFrameRenderer.cs` | GDI 帧生命周期、画布适配与可选内描边 | Windows-only；集中持有 Bitmap 与 `LayeredSpriteRenderer` 依赖 |
| `LayeredSpriteRenderer.cs` | GDI / layered window 像素渲染 | Windows-only |
| `Features/KeyboardOverlay/GlobalKeyboardActivity.cs` | Windows 全局低级键盘 Hook；同步捕获按键发生时的焦点身份 | Windows-only |
| `Features/KeyboardOverlay/KeyboardFocusSnapshot.cs` | 前台窗口、进程、线程、焦点 HWND 与 UIA RuntimeId 快照及一致性判断 | Windows-only |
| `Features/KeyboardOverlay/KeyboardInputEventArgs.cs` / `KeyboardInputFormatter.cs` | Hook 事件快照载体、Windows 虚拟键与修饰键格式化 | Windows-only |
| `Features/KeyboardOverlay/SensitiveInputDetector.cs` | UI Automation、Win32 密码样式和系统凭据进程检查 | Windows-only 证据采集器 |
| `Features/KeyboardOverlay/KeyboardOverlayForm.cs` | 连按结果的透明窗口绘制、定位和淡出 | Windows-only 显示适配器 |
| `Core/Keyboard/KeyDisplayAccumulator.cs` | 连按/长按显示计数状态机 | `PennyPet.Core`，调用者注入时间 |
| `Core/Keyboard/PetKeyboardPrivacyPolicy.cs` | 首次确认、旧偏好、Hook 启动门禁与检查失败 fail-closed 判定 | `PennyPet.Core`，可跨平台 |

### 提醒

| 文件 | 责任 | 分类 |
|---|---|---|
| `Core/Reminders/ReminderModels.cs` | 提醒数据、时间安排、短文本规则 | `PennyPet.Core`，可跨平台 |
| `Core/Reminders/PetReminderCoordinator.cs` | 到点/预提醒状态、时间刷新、气泡替换规则 | `PennyPet.Core`，可跨平台 |
| `PetReminderWindowsCoordinator.cs` | 对话框、桌宠动画、气泡和便利贴 UI 协调 | Windows-only |
| `ReminderUi.cs` | WinForms 提醒编辑窗口 | Windows-only |
| `SpeechBubbleForm.cs` | 桌宠气泡窗口 | Windows-only |

### 便利贴

| 文件 | 责任 | 分类 |
|---|---|---|
| `Core/StickyNotes/StickyNoteModels.cs` / `StickyNoteCodec.cs` | 便利贴、Todo、Schedule 数据模型、Dock 快照、v1-v9 编解码与修复规则 | `PennyPet.Core`；颜色只保存原始 ARGB 值，不引用 `System.Drawing` |
| `Core/StickyNotes/StickyDockOperations.cs` / `StickyTabDropSession.cs` | Dock 组插入、抽离、隐藏槽位、拆分判定和页签拖放提交会话 | `PennyPet.Core`；以数据和不透明来源标识工作，不引用 WinForms |
| `Features/StickyNotes/StickyNoteRepository.cs` | 文件保存、备份、损坏恢复、dirty 状态和紧急导出 | 文件系统适配；格式由 Core codec 负责 |
| `StickyNoteWpf.cs` | WPF 窗口构造、总体生命周期、外观和持久化接线 | Windows-only，高风险 |
| `StickyEditorController.cs` | RichText、格式、焦点和 IME 组合输入 | Windows-only，最高风险 |
| `Core/Links/StickyNoteLinks.cs` / `StickyLinkPolicy.cs` | HTTP(S)/Windows 路径识别、打开风险分类与确认文案 | `PennyPet.Core`，无文件系统或 Shell 副作用 |
| `StickyLinkController.cs` / `StickyLinkService.cs` | WPF 链接格式/点击、用户确认、存在性检查和 Shell 打开 | Windows-only 薄适配层 |
| `StickyNativeWindowBehavior.cs` | Win32 消息、拖拽、resize 和最大化拦截 | Windows-only，高风险 |
| `StickyTodoController.cs` | Todo UI 与本窗口内的增删改显示 | Windows-only UI；模型在 `StickyNoteModels` |
| `StickyScheduleController.cs` | Schedule UI 与本窗口内的增删改显示 | Windows-only UI；模型在 `StickyNoteModels` |
| `StickyReminderController.cs` | 便利贴提醒横幅 UI | Windows-only |
| `StickyAppearanceController.cs` | 便利贴外观对话框协调 | Windows-only |
| `PetStickyDockCoordinator.cs` | 活窗口拖动、坐标布局、窗口同步和屏幕边界；组关系变更委托给 Core | Windows-only，高风险适配层 |
| `StickyNoteTabs.cs` | 收起后的侧边标签窗口；拖放会话状态由 Core 对象持有 | Windows-only |
| `StickyNotes.cs` | WinForms 管理器、标题框、IME 友好输入控件 | Windows-only |

### 设置、诊断与启动

| 文件 | 责任 | 分类 |
|---|---|---|
| `Core/Settings/PetSettingsData.cs` / `PetSettingsCodec.cs` | 设置模型、默认值、INI 新旧格式和提醒字段 | `PennyPet.Core`，可跨平台 |
| `Infrastructure/Persistence/WindowsDataPaths.cs` | 统一选择 `%LocalAppData%\PennyPet`，供设置、便利贴、诊断和内嵌美术缓存使用 | Windows-only 路径入口，无读写副作用 |
| `PetSettings.cs` | 文件大小门禁、主/备份恢复、原子保存和失败事件 | 文件系统平台适配；默认目录由 `WindowsDataPaths` 提供 |
| `ApplicationDiagnostics.cs` | Windows 用户目录中的诊断与文本写入 | Windows 日志适配；默认目录由 `WindowsDataPaths` 提供 |
| `StartupRegistration.cs` | 注册表开机启动 | Windows-only |

## 3. Windows / macOS 迁移地图

“可复用”表示业务规则和数据语义可复用，不等于当前 `.cs` 文件已经能直接加入一个 Mac 工程编译。

| 能力 | 可复用部分 | macOS 必须实现/适配 |
|---|---|---|
| 桌宠透明窗口 | 帧尺寸与动画状态 | AppKit 无边框透明 `NSWindow`、透明合成 |
| 桌宠拖拽 | 拖拽阈值等规则可作为行为规范 | `NSWindow` 鼠标事件、屏幕坐标换算 |
| 桌宠置顶 | 设置语义 | `NSWindow.Level` 与 Space 行为 |
| 动画状态机 | `PetAnimationController` 的状态、概率、优先级、冷却 | 平台计时器和实际帧提交 |
| 动画资源 | manifest、状态名、帧顺序、时长语义 | `CGImage`/ImageIO 解码与缓存；替换 GDI 位图路径 |
| 鼠标 hover | 进入/离开后选择何种状态 | AppKit tracking area 与事件分发 |
| 点击互动 | 动画选择规则、拖拽判定语义 | AppKit click/drag 事件 |
| 键盘活动检测 | 按键显示格式和累计规则可移植 | macOS Event Tap、辅助功能权限、Secure Input 隐私处理 |
| Reminder | 模型、调度和气泡替换规则 | macOS 时钟/唤醒协调及 UI 展示 |
| Speech Bubble | 文案、持续/替换语义 | 新的 AppKit 气泡窗口与定位 |
| Sticky Note | 数据、格式、恢复规则 | 新的 AppKit 编辑窗口；不能复用 WPF/WinForms |
| Todo | `StickyTodoItem` 和持久化语义 | Mac 列表 UI 和编辑交互 |
| Schedule | `StickyScheduleItem` 和持久化语义 | Mac 日程 UI、日期控件和窗口交互 |
| Dock | 保存的关系语义和部分纯快照规则 | Mac 窗口吸附、拖拽、插入、拆分、屏幕安全边界应重新实现并对照不变量测试 |
| 开机启动 | 开关语义 | Login Item / ServiceManagement；不能复用注册表实现 |
| 设置存储 | INI 字段、默认值、兼容和恢复规则 | Application Support 路径与 Mac 原子保存 |
| 数据恢复 | 主文件/备份选择和失败保护规则 | Mac 路径、权限和文件替换实现 |
| 多显示器 | “当前桌宠所在屏幕”等产品规则 | `NSScreen` 坐标系、菜单栏/Dock 可用区域 |
| DPI / Retina | 逻辑尺寸和缩放设置语义 | points/pixels、backing scale factor、跨屏幕变化 |

Windows 最大的五个耦合点：

1. `UpdateLayeredWindow`、GDI 位图和 User32 原生窗口绘制。
2. WPF + WinForms 混合消息循环、`ElementHost`、RichTextBox、焦点和中文 IME interop。
3. 低级键盘 Hook、UI Automation 密码框探测和 Windows 隐私边界。
4. Windows Registry 开机启动。
5. `Screen`、DPI、窗口句柄、`SetWindowPos` 与多显示器 Dock 坐标操作。

## 4. 构建系统评估

### 构建链路已统一到 SDK/Roslyn

`PennyPet.Windows.csproj` 就是正式单文件 EXE 的构建入口，与 `dotnet build` 标准链共用同一套 MSBuild/Roslyn 编译器与 `LangVersion=latest`，不再通过旧 .NET Framework `csc.exe` 二次编译。构建流程：

1. 递归编译 `desktop-pet` 下的 `.cs` 文件，排除 `bin`、`obj`、`dist`、`Tests` 与独立入口。
2. `PennyPet.Tools`（通过 `ProjectReference` 先构建）生成 release art pack 与 startup art cache。
3. `GeneratePennyArtResources` target 把这些资源以及图标、联系作者图片、带版本 manifest 一并嵌入 EXE。

`desktop-pet/build.ps1` 与 `BuildOfficialRelease` target 都是薄封装：调用 `dotnet build` 并把产物复制到发布目录。`build-protected.ps1` 仍独立负责本地保护版。

### 建议

实验分支已经完成可回退的第二阶段工程化验证：

- 根目录 `PennyPet.sln` 可由 Visual Studio 和 `dotnet` 打开。
- `PennyPet.Core.csproj` 目标为 `netstandard2.0`，承载 Reminder、Sticky Note、Dock、动画、美术清单/时长、隐私和设置 codec 等纯规则；不引用 Form、Window、Registry、Screen 或 Bitmap。
- `PennyPet.Windows.Core.csproj` 通过递归 Compile glob 编译 Windows 产品代码；名称中的 `Windows` 很重要，它不是跨平台核心。
- `PennyPet.App.csproj` 只有正常桌宠入口，不携带 SelfTest/Tools 命令路由。
- `PennyPet.Tools.csproj` 独立生成美术发布包、启动缓存和校验报告。
- `PennyPet.SelfTests.csproj` 独立承载资源、文件系统、WinForms/WPF、Hook 等 Windows SelfTest、探针和预览入口；纯 Core 规则只放在 `PennyPet.Tests`。
- `PennyPet.Tests.csproj` 由 `dotnet test` 发现，直接引用纯 Core，不加载 Windows UI。
- `PennyPet.Windows.csproj` 与 `Program.cs` 继续保留为兼容单 EXE 编译入口。
- `Directory.Build.props` 为同目录的项目隔离 `obj`，避免 NuGet 还原结果互相覆盖。
- `ProductVersion.props` 是产品、程序集与发布文件名的唯一版本源；`Directory.Build.targets` 会从 `app.manifest.template` 生成带版本的 manifest。
- Windows Core 的 MSBuild target 调用 Tools 生成并嵌入资源；独立 SelfTests 的完整报告为 `ok=true`。
- `BuildOfficialRelease` 与 `build.ps1` 都复用 `PennyPet.Windows` 的 Release 构建，只把产物复制到发布目录；资源生成只有 `GeneratePennyArtResources` 这一套。

当前迁移门禁是：

1. CI 同时构建解决方案和 `build.ps1` 单 EXE。
2. 标准 Core tests 必须通过；独立 SelfTests 与兼容单 EXE 的完整报告都必须为 `ok=true`，专项探针也必须一致。
3. 签名发布可在现有链路上追加；`build-protected.ps1` 只保留给本地研究用途。
4. 新的纯 Core 只覆盖已验证的模型和规则；不得把 `PennyPet.Windows.Core` 或现有持久化/UI 实现错称为跨平台。

构建脚本和 SDK 工程现在使用一致的递归源码发现，键盘覆盖层与便利贴已经进入 `Features` 目录；后续目录调整不再需要复制源码清单，但仍应避免只按行数拆类。

## 5. 测试体系与行为规范

### 已有自动验证

| 范围 | 当前覆盖 |
|---|---|
| 动画 | 状态选择、权重、冷却、单周期恢复、notification/idle/typing/拖拽规则、资源帧和时长 |
| Reminder | 创建/取消、具体时间、20 秒预提醒、编辑期间时钟、气泡持续/替换和字号 |
| Todo / Schedule | 模型、增删改后的序列化与持久化兼容路径 |
| 设置 | 默认值、INI 保存/读取、静默模式、启动和键盘显示选项 |
| 数据恢复 | 原子写入、自动备份、损坏主文件恢复、加载失败不覆盖、旧数据继续创建 |
| Dock | 分组快照、父子/根关系、插入/拆分规则、隐藏恢复、屏幕安全位置和多种历史回归场景 |
| 隐私 | 键盘功能默认关闭、按键累计、进程隔离、按键时焦点快照、目标变化 fail-closed、密码输入抑制规则 |
| 发布资源 | 图标、动画包、启动缓存、联系人资源、逐像素透明和轮廓 |

主要入口是 `--self-test=<json>`。此外还有：

- `--startup-probe=<json>`：验证启动缓存和启动路径。
- `--sticky-input-probe=<json>`：验证便利贴输入路径。
- `--sticky-pump-probe=<json>`：验证 WinForms/WPF 消息泵协作。
- `--sticky-transparency-probe=<json>`：验证透明窗口重叠场景。
- 多个预览渲染入口：用于人工查看便利贴、日程、外观、气泡、提醒和整体功能。

### 自动测试不能代替的人工验证

- 真实中文输入法：composition、候选框位置、Enter、Selection、字体继承和焦点恢复。
- 实际窗口 Dock：左右组互换、中间插入、长按拆分、组拖动、隐藏/恢复、特别宽/长便利贴。
- 多显示器：不同排列、负坐标、断开外接屏、从不同 DPI 屏幕恢复窗口。
- 桌宠视觉：hover、点击、waiting/thinking、notification、waving 的视觉时序与基线。
- 真正到点的提醒：系统时间、声音、常亮气泡、后续气泡覆盖和用户点击消失。
- 键盘 Hook 与隐私：不同应用密码框、管理员权限差异、IME 与 Secure Input 类场景。
- 保护版：杀毒软件/SmartScreen 环境和签名发布链。

未来 Mac 实现应把现有纯规则 SelfTest 的输入、输出和概率/时间不变量当作行为规范；平台窗口、IME、Event Tap、Dock、多显示器和 Retina 必须建立 Mac 专属集成测试与人工回归清单，不能把 Windows 探针伪装成跨平台覆盖。

## 6. 未来在线功能的扩展边界

当前 Penny **没有在线内容功能，也不会主动请求远程服务**。本节是后续开发约束，不是在描述已经存在的产品行为。

第一个真实在线功能进入仓库时，应保持下面这条简单依赖链：

```text
Windows UI（PetForm / SpeechBubbleForm / 其他展示窗口）
  <- 只接收可展示的 Feature 结果
Feature（是否展示、每天一次、去重、默认行为等业务规则）
  -> 对应 Remote Service（例如 DailySongService）
       -> 统一 ApiClient（HTTP、超时、取消、错误分类、JSON）
       -> 对应 API model
  -> OnlineCacheRepository（独立在线缓存，不碰用户核心数据）

平台外围
  -> UI 线程调度、Windows 路径、诊断日志、实际窗口展示
```

这是概念边界，不要求现在创建同名目录、空接口或空类。当前没有真实 endpoint、响应模型和调用者，提前加入 `ApiClient` 或 `OnlineCacheRepository` 无法验证序列化、错误和缓存策略，只会产生未使用的抽象。因此它们应由**第一个真实 API Feature** 一次建立、测试并复用，不能由各个窗口各写一套网络代码。

### 各层职责

| 层 | 应负责 | 不应负责 |
|---|---|---|
| Windows UI / Presentation | 收到结果后选择气泡或窗口展示；切回正确 UI 线程；用户交互 | HTTP、JSON、重试、缓存过期、每日去重 |
| Feature | 业务触发条件、是否应该展示、内容去重、把服务结果转换为简单展示结果 | `Form`、`Window`、句柄、Registry、Dock、IME |
| Remote Service | 调用具体 endpoint、验证该功能的响应、映射为 Feature model | 直接操作桌宠动画或窗口控件 |
| ApiClient | HTTPS 请求、统一超时、取消、User-Agent、有限重试、状态码和 JSON 基础错误 | 每日推歌或节日的业务判断 |
| Online Cache | cache key、抓取时间、过期时间、原子保存、损坏恢复和离线 fallback | 写入 `settings.ini` 或 `sticky-notes.dat` |
| Platform | 文件路径、Windows UI dispatch、日志落盘和具体窗口实现 | 远程内容的业务含义 |

`Reminder`、每日内容和节日事件要保持概念独立：

- `Reminder` 是用户明确创建、必须持久化并按时触发的提醒。
- Daily Content 是应用自动获取、失败时可以静默跳过的增强内容。
- Holiday Event 来自日期规则或服务器数据，有自己的刷新周期。
- 三者可以共用纯时间工具，但不能塞进一个新的巨型 `ReminderManager`。

Feature 输出建议是一个很小的展示数据对象，例如内容类型、标题、正文、可选图片 URL、可选按钮文字和一个受控动作标识。Feature 不应知道 `SpeechBubbleForm` 的控件树，也不应传递任意服务器脚本或任意可执行命令给 UI。

## 7. 网络客户端原则

第一个在线 Feature 实现时，增加一个小型、可复用的 HTTP 边界即可，不引入 Polly、DI container 或大型网络框架：

- 复用长生命周期 `HttpClient`；不要每次请求都新建一个客户端。
- 只使用 HTTPS，基础地址集中配置；endpoint 路径由对应 Service 管理，不能散落在 `PetForm`。
- 设置明确的短超时，初始建议 10～15 秒，并接受 `CancellationToken`。
- 网络 I/O 使用 `async` / `await`，不得在 WinForms/WPF UI 线程调用 `.Result` 或 `.Wait()`。
- 只对幂等 GET 的瞬时失败考虑最多一次延迟重试；超时、HTTP 408、429 和部分 5xx 才可能重试，并尊重 `Retry-After`。JSON 错误、业务错误和普通 4xx 不重试。
- User-Agent 使用稳定、非个人化的形式，例如 `PennyPet/1.0 (Windows)`。
- 限制可接受响应大小；先检查状态码和内容类型，再反序列化。
- 未知 JSON 字段必须可忽略；非关键字段缺失时使用安全默认值；关键内容缺失或无效时把本次响应判为不可用。
- 网络失败不能使桌宠、便利贴、提醒或启动流程失败。没有可用缓存时，Feature 应静默跳过或显示产品定义的友好提示，不能弹技术异常框。

异步完成后，只有 Presentation 边界负责用 WinForms `BeginInvoke`、WPF `Dispatcher` 或创建时保存的 UI `SynchronizationContext` 回到正确线程。纯 Feature、Service 和 Cache 不引用这些 Windows 类型。

为测试保留一个**窄接缝**即可：第一个真实 `ApiClient` 可以接受一个很小的 transport 抽象或请求委托，使 SelfTest 能返回固定响应。没有真实实现前不创建空接口，也不引入 Mock 框架。

## 8. 后端 API 契约建议

Penny 自有后端建议从 `/v1/` 开始，以可增加字段的 JSON envelope 返回结果：

```json
{
  "version": 1,
  "success": true,
  "serverTimeUtc": "2026-08-26T01:23:45Z",
  "cacheTtlSeconds": 86400,
  "data": {}
}
```

失败示例：

```json
{
  "version": 1,
  "success": false,
  "serverTimeUtc": "2026-08-26T01:23:45Z",
  "error": {
    "code": "temporarily_unavailable",
    "message": "Content is temporarily unavailable.",
    "retryable": true
  }
}
```

约束：

- HTTP 状态码表达传输/权限/服务状态，`success` 表达业务结果；客户端不能只看其中一个。
- `error.code` 是稳定、可编程判断的短标识；`message` 用于诊断或受控展示，不能包含 secret。
- `serverTimeUtc` 用于排查时间偏差，不能直接替代用户本机的 Reminder 时钟。
- `cacheTtlSeconds` 可选，客户端仍要限制不合理的过短或过长期限。
- 服务器不能假设所有客户端都是最新版；增加字段必须向后兼容，旧客户端会忽略它们。
- 缺少非关键字段时客户端使用默认行为；协议发生真正破坏性变化时再新增 `/v2/`，不要为每次加字段升级版本。
- 图片和跳转地址要经过客户端允许列表、协议和长度校验；服务端文本不能变成任意本地文件操作或命令执行入口。

后端开发者无需理解 Win32、WPF、Dock 或 IME。以未来“每日推歌”为例，真实开发大致只需要：

1. 定义 `DailySongModels`，匹配 `/v1/daily-song` 的稳定响应。
2. 用共享 `ApiClient` 实现 `DailySongService`。
3. 在 `DailySongFeature` 中处理“今天是否已经展示”、空内容和展示结果转换。
4. 通过共享在线缓存保存当天结果。
5. 为成功、网络失败、坏 JSON、空内容、有效缓存和过期缓存增加不访问公网的测试。
6. 最后只在 Windows Presentation 边界增加一次事件/调用接线，让 UI 展示 Feature 结果。

不要把 HTTP、JSON、日期判断和气泡控件调用直接写进 `PetForm`。

## 9. API secret、安全和隐私边界

真正需要保密的第三方 API key 不能进入 EXE、GitHub、普通设置文件或客户端日志。编译、混淆、Base64 和所谓“加密常量”都不能保护客户端 secret。推荐路径是：

```text
Penny 客户端 -> Penny 自有后端 -> 第三方 API
                                  ^ secret 只保存在服务器
```

允许公开分发的客户端 token 必须单独评估，并且至少做到权限最小、可撤销、可轮换、限流和来源滥用监控；不能因为名称叫 token 就默认它可以公开。

未来在线 Feature 默认遵守 data minimization：不得上传便利贴、Todo、Schedule、Reminder 正文、键盘内容或无关设备信息。若某项功能确实需要用户数据，必须先有明确产品说明、最小字段设计和用户可理解的授权，再修改隐私文档。

现有 `ApplicationDiagnostics` 可以继续作为本地诊断落点，但网络日志在交给它以前必须净化。可以记录功能/endpoint 类型（不是完整带 query 的 URL）、HTTP status、错误类别、发生时间和客户端版本；不得记录请求/响应正文、Authorization、cookie、API key、完整 token 或用户内容。Exception message 可能包含 URL，也要先移除 query 和敏感 header。

## 10. 在线缓存边界

建议在第一个真实在线 Feature 中把缓存放到独立位置，例如：

```text
%LocalAppData%\PennyPet\Cache\Online\<feature>\<safe-key>.json
```

这是未来建议路径，当前版本不会创建它。缓存不能与 `settings.ini`、`sticky-notes.dat` 或 Reminder 数据混写。

每条缓存至少需要：schema version、cache key、`fetchedUtc`、`expiresUtc` 和 payload；可按服务能力增加 ETag。key 必须由受控字段组成或经过稳定哈希，不能把服务器/用户字符串直接拼成任意文件路径。

保存应复用 `AtomicTextFile` 已验证的“临时文件 + 替换 + 备份”经验。读取损坏时忽略或隔离该条缓存并重新获取，绝不能因为增强内容缓存损坏而阻止 Penny 启动。离线 fallback 由 Feature 决定：

- 未过期缓存：正常使用。
- 已过期缓存：只有产品规则明确允许时才作为 stale fallback，并标记来源。
- 没有缓存：静默跳过或显示友好提示。
- 每日内容：通常以本地日期 + 内容种类作为 key，当天最多主动拉取一次；手动刷新另行定义。

缓存是可丢弃的远程内容副本，不是用户核心数据，也不能成为保存用户私人内容的新位置。

## 11. 在线 Feature 测试规范

网络测试默认不得访问公网。第一个真实在线功能应使用简单 fake/stub 覆盖：

- 服务器成功且内容完整。
- 网络不可用、超时和可重试状态。
- HTTP 成功但 JSON 损坏。
- `success=false` 的业务错误。
- `data` 为空或缺少关键字段。
- 未过期缓存、过期缓存、缓存损坏和无缓存。
- 未知新增字段可以忽略。
- Feature 失败不影响桌宠启动、本地 Reminder 或便利贴。
- 后台完成后只由 Presentation 在正确 UI 线程更新窗口。

这些测试应加入现有 SelfTest 或未来独立的纯逻辑测试项目；不得为了跑测试要求真实 API key。真实 Windows 气泡展示仍属于 UI 集成/人工验证。

## 12. 当前是否适合开始 Mac 版

当前已经适合**开始独立的 Mac 技术验证和 UI 项目规划**，因为提醒、便利贴数据、Dock 与设置规范化规则已有可直接编译的纯 Core，Windows-only 能力也已列出。但仍不能宣称 Windows 应用可直接移植：资源位图、平台数据目录、持久化、诊断和全部窗口交互仍需平台适配；Windows 的目录选择现已集中在一个入口。

推荐路线是原生 **Swift + AppKit** 实现透明窗口、事件、Dock、输入权限、多显示器和 Retina；把本文件中的行为表、现有数据格式和纯规则测试作为兼容规范。若后续确实需要共享 C# 代码，可先把已验证的纯模型/规则建立为小型跨平台 .NET library，再单独评估互操作成本。不要为了共享少量规则而把整个 Windows UI 迁到 Avalonia、MAUI 或 Electron。

Mac 不是当前阶段的开发目标。若未来开始正式 Mac UI，最值得继续做的不是再拆 Windows 文件，而是：

1. 固化设置、提醒和便利贴格式的兼容样例（golden files）。
2. 以 `Core/Animation` 与 `Core/Art` 的现有行为作为 Mac 动画状态、别名和时长兼容规范；平台侧只替换图像解码、缓存和绘制。
3. 明确 Mac 数据目录和 Windows 数据迁移方式。
4. 用一个最小 AppKit 原型验证透明窗口、Retina 帧显示、鼠标事件和多屏坐标；该原型应在独立任务/项目中完成，不改写当前 Windows 实现。

就当前 Windows 仓库而言，架构已经适合开始第一个真实 API Feature，不需要再进行大规模重构。下一步应先确定后端契约、隐私需求和失败体验，再由该 Feature 建立并验证共享 `ApiClient` 与 `OnlineCacheRepository`；不要继续为了文件行数整理稳定的窗口代码。

## 13. 实验分支对外部建议的验证结果

下列改动均先在副本中分阶段完成，并以完整 SelfTest 和专项探针作为门禁：

| 建议 | 当前真实状态 | 决定 |
|---|---|---|
| 拆 `StickyNoteWpf.cs` | 原样迁移为窗口壳、编辑器/IME、链接和原生窗口行为；Todo/Schedule/Reminder/Appearance/Dock 保持既有 partial | 已完成；不重写 IME/Dock，完整 SelfTest 与三项 WPF 探针通过 |
| 拆 `PetForm.cs` | 启动、动画运行时、键盘、气泡、菜单和便利贴窗口协调按职责分文件 | 已完成；`PetForm.cs` 约 498 行，partial 共享状态而不是回调式伪服务 |
| 建立 `.csproj` / `.sln` | App、Windows Core、Tools、Tests、SelfTests 与兼容项目都有显式工程入口 | 已完成；解决方案启用 warnings-as-errors 且保持 0 警告，`build.ps1` 复用 SDK/Roslyn 生成单 EXE |
| 把 Tools/Tests 从主入口分离 | 独立 Tools 生成 art pack/cache，独立 SelfTests 运行完整报告和探针 | 已完成；兼容 `Program` 暂时保留旧命令面供 CI/回退 |
| 敏感输入 fail-closed 与首次提示 | `PetKeyboardPrivacyPolicy` 管理首次确认；无法检查时隐藏 | 已完成自动策略测试；真实第三方/跨权限输入仍需人工回归 |
| 危险本地路径二次确认 | `Core/Links/StickyLinkPolicy` 分类 executable/script/shortcut/UNC，`StickyLinkService` 仅执行 Windows 副作用 | 已完成纯规则与 WPF 确认接线；UNC 在用户确认前不会探测网络文件系统 |

实验结果尚未自动成为正式发布方案。合并回主线前仍应完成人工中文 IME、Dock 组合拖拽、多屏、全局键盘隐私和危险路径确认回归，并让 CI 同时保留模块化与兼容单 EXE 两条门禁。
