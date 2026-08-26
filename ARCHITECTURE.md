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
- 上一轮只修正了两个明确、低风险的问题：移除 `PetForm` 对纯控制器的无意义转发包装；把提醒规则与 Windows UI 协调、便利贴模型/持久化与 Windows UI 文件分开。本轮再次验收后，没有发现必须通过生产代码重构才能解决的新问题，因此不继续移动 Windows 窗口、Dock 或 IME 代码。

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

第一阶段标准项目入口已经完成：

- 根目录 `PennyPet.sln` 可由 Visual Studio 和 `dotnet` 打开。
- `desktop-pet/PennyPet.Windows.csproj` 使用 SDK-style `net48`，启用 WinForms/WPF，并显式列出源码和 UIAutomation 等引用。
- 普通项目 `Build` 只做 IDE/编译器验证，不冒充资源完整的发布 EXE。
- `BuildOfficialRelease` 明确委托现有 `build.ps1`，因此标准工具可以进入正式发布链，而不复制第二套资源算法。

仍然**不建议删除或绕过 `build.ps1`**。后续独立任务按三步推进：

1. CI 增加普通 `.csproj` 编译检查，同时继续把 `build.ps1` + SelfTest 作为发布门禁。
2. 在独立实验副本中，把“生成 art pack / startup cache”提取成可重复执行的小型构建工具或明确 MSBuild target；对比资源名、文件版本、EXE 行为和 SelfTest。旧脚本继续作为 fallback。
3. 平台边界稳定后，再为真正纯逻辑建立单独的共享项目。不要把 WPF/WinForms/Win32 文件放入共享项目。

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

当前已经适合**开始独立的 Mac 技术验证和 UI 项目规划**，因为动画、提醒和持久化语义已有清晰边界，Windows-only 能力也已列出。但还不适合宣称“共享核心可以直接编译给 Mac”：当前没有标准共享项目，资源位图、默认路径和诊断仍需薄适配。

推荐路线是原生 **Swift + AppKit** 实现透明窗口、事件、Dock、输入权限、多显示器和 Retina；把本文件中的行为表、现有数据格式和纯规则测试作为兼容规范。若后续确实需要共享 C# 代码，可先把已验证的纯模型/规则建立为小型跨平台 .NET library，再单独评估互操作成本。不要为了共享少量规则而把整个 Windows UI 迁到 Avalonia、MAUI 或 Electron。

Mac 不是当前阶段的开发目标。若未来开始正式 Mac UI，最值得继续做的不是再拆 Windows 文件，而是：

1. 固化设置、提醒和便利贴格式的兼容样例（golden files）。
2. 为纯动画/提醒规则建立独立、无 Windows UI 的测试项目。
3. 明确 Mac 数据目录和 Windows 数据迁移方式。
4. 用一个最小 AppKit 原型验证透明窗口、Retina 帧显示、鼠标事件和多屏坐标；该原型应在独立任务/项目中完成，不改写当前 Windows 实现。

就当前 Windows 仓库而言，架构已经适合开始第一个真实 API Feature，不需要再进行大规模重构。下一步应先确定后端契约、隐私需求和失败体验，再由该 Feature 建立并验证共享 `ApiClient` 与 `OnlineCacheRepository`；不要继续为了文件行数整理稳定的窗口代码。

## 13. 已评估但本轮不执行的建议

下面几项方向本身有价值，但不能脱离当前真实代码和风险执行：

| 建议 | 当前真实状态 | 决定 |
|---|---|---|
| 继续拆 `StickyNoteWpf.cs` | Todo、Schedule、Reminder、Appearance、Dock 和纯链接识别已经分别位于独立文件；剩余主要是窗口壳、RichText、IME/焦点、链接格式/点击和 Win32 消息，它们共享同一 WPF 输入状态 | 不在行为保持阶段继续拆。未来只能按“原样移动一个边界 + 真实 IME/链接/窗口回归”的独立任务进行 |
| 继续拆 `PetForm.cs` | 菜单、动画规则、提醒纯规则/Windows 协调、Dock 和开机注册已经独立；剩余启动恢复、键盘隐私、气泡和便利贴窗口生命周期共享 UI 线程及状态 | 暂不新建 Tray/Startup/Keyboard/Bubble/Sticky/Runtime 六个 Controller，避免回调和双向依赖膨胀；第一个 API Feature 只允许加薄的 Presentation 接线 |
| 建立 `.csproj` / `.sln` | 第一阶段已经完成：标准项目可编译并显式列出源码，`BuildOfficialRelease` 委托原脚本；正式构建仍依赖两次编译、运行中间 EXE 生成美术包/启动缓存、资源二次嵌入和保护版验证 | 保留当前双入口；下一阶段先让 CI 同时验证项目编译和原发布链，再在独立副本研究资源工具化，不能删除或绕过 `build.ps1` |
| 把 Tools/Tests 从主 EXE 分离 | 能减少发布命令面，但当前 build 正是通过中间 EXE 生成 release pack/startup cache，SelfTest 也作为 CI 发布门禁 | 等标准项目和等价资源生成工具可验证后再做；现在拆会同时改变构建链和测试链 |
| 敏感输入检测失败时默认隐藏、首次启用提示 | 安全方向合理；当前检测覆盖 UI Automation、标准密码样式和已知凭据进程，但检查失败时不能保证 fail closed | 这是用户可见行为变更，单独做产品设计与跨浏览器/自绘/跨权限回归；公开文档不再承诺 100% 识别 |
| 本地可执行/脚本/快捷方式/UNC 路径二次确认 | URL 已限制为 HTTP/HTTPS，纯检测已在 `StickyNoteLinks.cs`；本地绝对路径当前仍由 Shell 打开 | 作为独立安全加固任务，实现路径风险分类、确认 UI 和测试后再上线；本轮不改变既有点击行为 |

这里的“不执行”不是忽略问题，而是避免把架构整理、构建迁移和产品安全交互混成一次无法归因的大改。前四项按独立阶段推进；后两项优先级高于继续缩短文件，但必须有明确产品文案和真实 Windows 回归。
