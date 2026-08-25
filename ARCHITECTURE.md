# Penny 架构与平台迁移地图

本文记录当前**已纳入 Git 的 Windows 版**架构、依赖边界和测试范围。它是一份维护地图，不是要求立即跨平台重写的设计稿。工作区中未纳入 Git 的实验目录不属于本文验收范围，也不会被 Windows 构建脚本编译或上传。

当前原则：保持 Windows 版行为、数据格式和构建结果不变；优先识别边界，不为“架构漂亮”引入接口、依赖注入或新的大型框架。

## 1. 架构验收结论

- `Program` 已主要负责进程入口、命令行模式、启动互斥和启动预加载；桌宠窗口实现位于 `PetForm`。
- `PetForm` 仍是 Windows 桌面窗口协调中心，但菜单、动画决策、提醒纯规则、提醒 Windows UI 协调和 Dock 协调已有明确文件边界。
- `StickyNoteForm` 仍是 WPF 便利贴窗口本体；Todo、Schedule、提醒横幅、外观和 Dock 已按职责定位到独立文件。它们使用 `partial` 共享同一个窗口状态，属于“维护边界”，不是假装已经完全解耦的服务层。
- 未发现平台无关核心中的循环依赖；未引入 DI container，也没有 interface / wrapper / service 数量膨胀。
- Windows 窗口协调层仍存在 `PetForm` 与 `StickyNoteForm` 的双向协作。这是 Dock、隐藏/恢复和窗口生命周期造成的 Windows-only 耦合，不能在没有完整窗口回归测试时强拆。
- `StickyDockController` 较长，但其中是历史上高风险的 Dock 算法与窗口协调。当前选择保留算法和顺序，不以行数为目标继续拆分。
- 本轮只修正了两个明确、低风险的问题：移除 `PetForm` 对纯控制器的无意义转发包装；把提醒规则与 Windows UI 协调、便利贴模型/持久化与 Windows UI 文件分开。没有改变数据格式或用户可见行为。

当前仍值得关注的五个架构问题：

1. `StickyNoteWpf.cs` 仍同时承载 WPF 窗口构建、RichText、IME/焦点、窗口消息和编辑器交互；这些代码风险最高，暂不为缩短文件而拆。
2. `PetForm.cs` 仍负责较多 Windows 启动、窗口生命周期、渲染时钟、键盘隐私和便利贴协调；它已是协调器，但还不是轻量壳。
3. `SelfTestRunner.Run` 是一个很长的测试编排方法；覆盖面充足但维护成本较高。本轮不改写断言和报告格式。
4. `PetArt.cs` 同时包含资源清单、动画包、解码/缓存和渲染前处理，并有一处对 `LayeredSpriteRenderer` 的依赖；资源规则可复用，但位图实现尚未完全平台化。
5. 设置和便利贴仓库的序列化/恢复规则可复用，但默认数据目录与诊断记录仍直接选择当前平台位置；未来共享项目需要把“路径选择”留给平台入口。

## 2. 主要模块与依赖方向

依赖方向以“纯规则向外、平台实现向内调用”为目标：

```text
Program
  -> PetForm (Windows 协调)
       -> PetAnimationController (纯动画规则)
       -> PetReminderCoordinator (纯提醒状态/时间规则)
       -> PetReminderWindowsCoordinator (WinForms/WPF 提醒 UI 协调)
       -> PetContextMenu / StickyDockController / Windows UI
       -> PetArt -> LayeredSpriteRenderer

StickyNoteForm (WPF 窗口)
  -> Sticky Todo / Schedule / Reminder / Appearance partial 模块
  -> StickyNoteModels
  -> StickyNoteRepository -> AtomicTextFile / ApplicationDiagnostics

Reminder UI / PetForm
  -> ReminderModels

SelfTest
  -> 纯规则、持久化模块和 Windows UI 探针
```

原则上，平台无关模块不应反向引用 `PetForm`、WPF、WinForms 或 Win32。当前主要例外是资源/设置外围：`PetArt` 的位图处理和 `PetSettings` 的默认路径/诊断仍需未来适配，而不是现在重写。

### 入口和桌宠

| 文件 | 责任 | 分类 |
|---|---|---|
| `Program.cs` | 入口、命令行模式、单实例、启动准备 | Windows 入口 |
| `PetForm.cs` | 桌宠窗口与各模块协调、渲染时钟、交互入口 | Windows-only |
| `PetContextMenu.cs` | 右键菜单构造与命令绑定 | Windows-only |
| `PetAnimationController.cs` | 动画状态、优先级、随机选择、冷却和恢复规则 | 可跨平台 |
| `PetArt.cs` | 动画清单、帧包、解码、缓存和资源校验 | 部分可复用，位图后端待适配 |
| `LayeredSpriteRenderer.cs` | GDI / layered window 像素渲染 | Windows-only |
| `GlobalKeyboardActivity.cs` | Windows 全局低级键盘 Hook | Windows-only |
| `KeyboardOverlay.cs` | 键盘显示 UI、Windows 密码框探测 | 规则部分可移植，窗口/隐私探测 Windows-only |

### 提醒

| 文件 | 责任 | 分类 |
|---|---|---|
| `ReminderModels.cs` | 提醒数据、时间安排、短文本规则 | 可跨平台 |
| `PetReminderCoordinator.cs` | 到点/预提醒状态、时间刷新、气泡替换规则 | 可跨平台 |
| `PetReminderWindowsCoordinator.cs` | 对话框、桌宠动画、气泡和便利贴 UI 协调 | Windows-only |
| `ReminderUi.cs` | WinForms 提醒编辑窗口 | Windows-only |
| `SpeechBubbleForm.cs` | 桌宠气泡窗口 | Windows-only |

### 便利贴

| 文件 | 责任 | 分类 |
|---|---|---|
| `StickyNoteModels.cs` | 便利贴、Todo、Schedule 数据模型和 Dock 关系快照规则 | 可跨平台；使用 `Point`/ARGB 值需薄适配 |
| `StickyNoteRepository.cs` | 文本格式、保存、备份、损坏恢复和默认路径 | 格式/恢复可复用；默认路径待平台适配 |
| `StickyNoteWpf.cs` | WPF 窗口、RichText、IME、焦点、编辑和原生消息 | Windows-only，高风险 |
| `StickyTodoController.cs` | Todo UI 与本窗口内的增删改显示 | Windows-only UI；模型在 `StickyNoteModels` |
| `StickyScheduleController.cs` | Schedule UI 与本窗口内的增删改显示 | Windows-only UI；模型在 `StickyNoteModels` |
| `StickyReminderController.cs` | 便利贴提醒横幅 UI | Windows-only |
| `StickyAppearanceController.cs` | 便利贴外观对话框协调 | Windows-only |
| `StickyDockController.cs` | 活窗口吸附、插入、拆分、拖动、隐藏/恢复和屏幕边界 | Windows-only，高风险 |
| `StickyNoteTabs.cs` | 收起后的侧边标签窗口 | Windows-only |
| `StickyNotes.cs` | WinForms 管理器、标题框、IME 友好输入控件 | Windows-only |

### 设置、诊断与启动

| 文件 | 责任 | 分类 |
|---|---|---|
| `PetSettings.cs` | 设置模型、INI 兼容格式、恢复 | 格式可复用；默认路径/诊断待适配 |
| `ApplicationDiagnostics.cs` | Windows 用户目录中的诊断与原子文本写入 | 原子写入规则可复用；路径/日志实现待适配 |
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

### 当前构建为什么不能直接换掉

`desktop-pet/build.ps1` 会：

1. 收集 `desktop-pet` 根目录下的 `.cs` 文件（当前**不递归**）。
2. 使用 .NET Framework C# 编译器和固定 WPF / WinForms / UIAutomation 引用生成中间 EXE。
3. 运行中间 EXE 生成 release art pack 与 startup art cache。
4. 把上述资源二次嵌入，生成当前单文件 Windows EXE。

`build-protected.ps1` 还会对成品进行保护、版本检查、完整 SelfTest、启动缓存探针和内嵌标记检查。GitHub Actions 也直接调用 `build.ps1`。因此仓促替换为 SDK-style 项目，最容易丢失的是二次资源生成、嵌入名称、单文件结果或保护版兼容性。

### 建议

现在**不建议直接迁移或删除 `build.ps1`**。建议后续独立任务分四步进行：

1. 先增加仅用于 IDE/静态分析的 Windows `.csproj`，明确目标框架、WPF/WinForms/UIAutomation 引用和所有源文件；发布仍由 `build.ps1` 完成。
2. 把“生成 art pack / startup cache”表示为明确的 MSBuild target，或提取成可重复执行的小型构建工具；对比资源名、文件版本、EXE 行为和 SelfTest。
3. CI 同时运行旧脚本与新项目构建，直到输出结构、探针和保护流程等价；旧脚本继续作为 fallback。
4. 平台边界稳定后，再为真正纯逻辑建立单独的共享项目。不要把 WPF/WinForms/Win32 文件放入共享项目。

目录重排也应等构建系统能递归或显式发现文件后再进行。当前先保持 `.cs` 平铺，避免“文件移动了但没有被编译”。

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
| 隐私 | 键盘功能默认关闭、按键累计、进程隔离、密码输入抑制规则 |
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

## 6. 当前是否适合开始 Mac 版

当前已经适合**开始独立的 Mac 技术验证和 UI 项目规划**，因为动画、提醒和持久化语义已有清晰边界，Windows-only 能力也已列出。但还不适合宣称“共享核心可以直接编译给 Mac”：当前没有标准共享项目，资源位图、默认路径和诊断仍需薄适配。

推荐路线是原生 **Swift + AppKit** 实现透明窗口、事件、Dock、输入权限、多显示器和 Retina；把本文件中的行为表、现有数据格式和纯规则测试作为兼容规范。若后续确实需要共享 C# 代码，可先把已验证的纯模型/规则建立为小型跨平台 .NET library，再单独评估互操作成本。不要为了共享少量规则而把整个 Windows UI 迁到 Avalonia、MAUI 或 Electron。

开始正式 Mac UI 前，最值得继续做的不是再拆 Windows 文件，而是：

1. 固化设置、提醒和便利贴格式的兼容样例（golden files）。
2. 为纯动画/提醒规则建立独立、无 Windows UI 的测试项目。
3. 明确 Mac 数据目录和 Windows 数据迁移方式。
4. 用一个最小 AppKit 原型验证透明窗口、Retina 帧显示、鼠标事件和多屏坐标；该原型应在独立任务/项目中完成，不改写当前 Windows 实现。
