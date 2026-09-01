# Penny 架构与平台迁移地图

本文记录当前已经落地的技术架构、Windows/通用边界和 macOS 迁移地图。它不是未来业务功能设计稿，也不要求立即创建 macOS UI、Application 层或网络基础设施。

本文对应当前发布版本 `1.0.2`。

当前原则：保持 Windows 版行为、数据格式和构建结果；只把已能用平台无关数据表达和验证的规则放入 Core，不为架构形式引入空接口、依赖注入或大型框架。

## 1. 当前架构结论

- `PennyPet.Core` 目标为 `netstandard2.0`，保存平台无关模型与纯规则。
- `PennyPet.Windows.Core`、`PennyPet.App`、`PennyPet.Windows` 和 `PennyPet.SelfTests` 仍是 Windows 实现或宿主。
- `PetForm` 和 `StickyNoteWindow` 已按职责拆成 partial 文件，但仍共享窗口状态；这是一条代码定位边界，不是假装独立的服务层。
- 高风险 IME、WPF/WinForms 消息桥、Window Message、Keyboard Hook、UI Automation、GDI、Shell、Registry 和真实窗口副作用继续留在 Windows 实现。
- Dock 组关系、统一置顶数据、页签拖放会话和纯数值几何已进入 Core；启动 Core 当前只包含 loading readiness 纯判定。
- HTTP(S) 与 Windows 路径/UNC 链接识别、危险扩展名、确认文案、文件探测和 Shell 打开位于 `Features/StickyNotes`。
- `Core/DailyNote/DailyNoteFeature.cs` 已包含三十日每日便利贴的纯进度判定；当前文档只记录已落地规则，不预设后续 UI、内容来源或持久化结构。
- 当前没有 `PennyPet.Mac`、完整跨平台 Application 层、网络服务或完整平台无关启动状态机。

`CoreArchitectureTests` 会检查编译后 Core 的程序集引用，阻止 Drawing、WinForms、WPF、Registry 和 UI Automation 进入共享层。这个门禁只证明代码依赖；盘符、UNC、Win32 坐标限制、键码和权限模型等语义上的平台假设仍需人工审查。

## 2. 依赖方向

```text
PennyPet.App / PennyPet.Windows
  -> PennyApplicationHost
  -> PetForm（Windows 窗口与协调）
       -> Core 动画、提醒、设置、键盘隐私规则
       -> Windows Hook、UIA、GDI、Registry、Screen 与窗口副作用

StickyNoteWindow（WPF）
  -> Sticky Editor / Todo / Schedule / Reminder / Link / Dock partial
  -> Core StickyNote 模型、codec、Dock 关系与几何规则
  -> Windows 文件系统、路径、Shell 与原生窗口适配

PennyPet.Tests -> PennyPet.Core
PennyPet.SelfTests -> Windows 产品程序集与真实资源/平台探针
PennyPet.Tools -> 美术发布包和启动缓存生成
```

平台层可以调用 Core；Core 不得反向引用 `PetForm`、WPF、WinForms、Win32、Registry、Screen、Bitmap 或平台路径规则。

## 3. 主要模块

### 桌宠、动画与美术

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/Animation` | 动画状态、优先级、概率、冷却和资源预加载规则 | 平台无关 |
| `Core/Art` | 美术清单模型、状态别名、渲染参数和帧时长规则 | 平台无关代码；不包含美术授权 |
| `PetArt.cs` | 发布资源包、位图解码、运行时缓存和资源校验 | Windows/GDI 资源适配 |
| `Features/Art` | GDI 帧生命周期、画布适配和内描边 | Windows-only |
| `LayeredSpriteRenderer.cs` | 透明分层窗口像素提交 | Windows-only |
| `PetAnimationRuntime.cs` | 计时器、资源预载和实际帧提交 | Windows-only |

`Core/Art` 的“跨平台”只表示代码和数据模型可复用，不代表 `art/` 中的美术资源采用 GPL 授权。泥泥NINII原创美术资源的版权与使用限制见根目录 [`ASSET_LICENSE.md`](ASSET_LICENSE.md)。

### 消息与桌宠互动

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/Messaging/PetMessageKind.cs` | Bubble 产品消息身份 | 平台无关 |
| `Core/Messaging/PetMessagePolicy.cs` | replacement、silent suppression 和 readability interrupt 规则 | 平台无关 |
| `Core/Messaging/BubbleReadingDurationRules.cs` | 文本动态阅读时间和 minimum readable 纯规则 | 平台无关 |
| `Core/Interaction/PetPokeBurstTracker.cs` | 快速连续 Poke 计数及单次 50 连戳触发 | 平台无关、仅进程内状态 |
| `Core/Interaction/PetSmallTalkPolicy.cs` | SmallTalk 概率、cooldown 和相邻文案不重复规则 | 平台无关 |
| `PetSmallTalkCoordinator.cs` | SmallTalk eligibility、文案选择、cooldown 状态及显示接受结果 | 平台无关产品协调 |
| `PetBubbleCoordinator.cs` | Bubble 窗口生命周期、pending、minimum readability 和 reposition | Windows-only |
| `PetAnimationRuntime.cs` | Poke 输入、产品顺序与实际 Windows 动画接线 | Windows-only |

`PetBubbleCoordinator` 只在 pending request 真正显示成功后将其移出队列；因 minimum readable 或 replacement policy 暂时失败的 request 会保留到现有 MouseUp/当前 Bubble 关闭重试点。没有额外轮询 Timer 或第二套消息管道。

`PetSmallTalkCoordinator` 只持有最小的进程内运行状态，并通过 `Func<string, bool>` 请求显示；`PetSmallTalkPolicy` 继续保持无状态纯规则。

### Daily Content

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/DailyContent/DailyContentRules.cs` | 日期键、每日一次和 DayPart 纯规则 | 平台无关 |
| `Core/DailyContent/DailyBriefingComposer.cs` | DayPart 与结构化每日事实组合为短文案 | 平台无关 |
| `Core/DailyContent/ZodiacSign.cs` | 用户星座偏好的稳定业务身份 | 平台无关 |
| `Core/DailyContent/ZodiacDailyCatalog.cs` | 随应用发布的原创 Zodiac daily copy | 平台无关、有限静态内容 |
| `Core/DailyContent/ZodiacDailySelector.cs` | 按当地日期与星座确定性选择每日文案 | 平台无关纯规则 |
| `Core/Calendar/SolarTerm*` | 二十四节气天文事实 | 平台无关 |
| `PetDailyContentCoordinator.cs` | 首次有效 Poke eligibility、compose、Bubble accepted 后消费日期 | Windows 产品协调 |
| `DailyContentSettingsForm.cs` | Daily Content、节气和星座偏好设置 UI | Windows-only |

`DailyBriefingComposer` 按 greeting、可选 solar term、可选 zodiac 的顺序合成一次 `DailyGreeting`；选择结果不缓存、不持久化。

`PennyPet.Core` 直接使用 `CosineKitty.AstronomyEngine 2.1.19`，当前用途仅为 `SolarTermCalculator` 的平台无关天文计算。兼容单文件 Windows 项目把固定的 `astronomy.dll` 嵌入 EXE，并由窄范围 `EmbeddedAssemblyResolver` 只解析该程序集；第三方许可见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

### 便利贴、Dock 与链接

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/StickyNotes/StickyNoteModels.cs` | 便利贴、三态 Todo、Schedule 和 Dock 持久化模型 | 平台无关 |
| `Core/StickyNotes/StickyNoteCodec.cs` | v1-v9 数据行编解码、兼容和内容限制 | 平台无关 |
| `Core/StickyNotes/StickyDockOperations.cs` | Dock 组插入、抽离、隐藏槽位、快照和统一置顶数据 | 平台无关 |
| `Core/StickyNotes/StickyTabDropSession.cs` | 页签拖放事务 | 平台无关；窗口来源是不透明身份 |
| `Core/StickyNotes/StickyDockGeometry.cs` | `DockPoint`、`DockSize`、`DockRect` 及 Dock、divider、header 可达性、恢复、新建、页签、弹窗和异常拖拽恢复几何 | 平台无关纯数值规则 |
| `Core/Startup/PetStartupRules.cs` | UI/美术 readiness 门禁 | 平台无关的小范围启动判定，不是完整启动框架 |
| `Core/DailyNote/DailyNoteFeature.cs` | 三十日进度、同日幂等、断签与完成判定 | 平台无关纯规则；不预设 UI 和存储 |
| `Features/StickyNotes/StickyNoteLinks.cs` / `StickyLinkPolicy.cs` | HTTP(S)、Windows 路径和危险目标策略 | Windows-only 链接策略 |
| `Features/StickyNotes/StickyNoteRepository.cs` | Windows 文件保存、备份、损坏恢复、dirty 和重试 | Windows 文件系统适配 |
| `Features/StickyNotes/StickyLinkService.cs` | 盘符/UNC、扩展名风险、确认、文件探测和 Shell 打开 | Windows-only 路径策略 |
| `Features/StickyNotes/StickyLinkCoordinator.cs` | WPF 链接格式、点击和光标 | Windows-only UI |
| `Features/StickyNotes/PetStickyDockCoordinator.cs` | 屏幕/DPI/原生几何转换、真实窗口移动和同步 | Windows-only 副作用适配 |
| `Features/StickyNotes/StickyEditorCoordinator.cs` | RichText、焦点和 IME | Windows-only，高风险 |
| `Features/StickyNotes/StickyNativeWindowBehavior.cs` | Win32 消息、拖拽、resize 和最大化拦截 | Windows-only，高风险 |

Windows Coordinator 把 `Point`、`Size`、`Rectangle` 等平台对象转换为 Core 的 `DockPoint`、`DockSize`、`DockRect`，调用纯规则后再执行 WPF/Win32 窗口副作用。`DockCoordinateSafetyLimit = 30000` 是 Win32 安全范围，由 Windows 层作为参数提供给 Core 计算；它不是 Penny 业务规则，也不能成为 macOS 常量。

当前 Dock participant eligibility 与窗口 executor 解耦：Ordinary、Todo、Schedule 可以任意 mixed Dock；Reminder 是所有便利贴共享的 capability/UI，不是独立 Sticky subtype 或第四种 Dock participant。eligibility 不依赖 `IsTodoList / IsSchedule / IsHostedSticky / ReminderUtcTicks`。hosted 与 legacy executor 可以进入同一 Dock component。两条输入路径共用 `DockWindowFacts`、同一 Dock session/Core rules 和 `DockLayoutTarget`，只在最终 owned effect edge 分别落到 legacy Window 或 `StickyUiHost`。Hosted/mixed preview、merge pulse 和 split guide 已接入这条共享流程。

### 提醒、设置、启动与键盘隐私

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/Reminders` | 提醒模型、时间刷新和气泡替换规则 | 平台无关 |
| `PetReminderWindowsCoordinator.cs` / `ReminderUi.cs` | 提醒窗口、动画和气泡协调 | Windows-only |
| `Core/Settings` | 设置数据、`StartAtLogin` 语义和旧 INI codec | 平台无关 |
| `PetSettings.cs` / `WindowsDataPaths.cs` | Windows 路径、备份、原子保存和诊断 | Windows-only |
| `Core/Keyboard` | 首次确认、偏好和 fail-closed 隐私判定 | 平台无关标准化输入 |
| `Features/KeyboardOverlay` | Hook、虚拟键、UIA/Win32 敏感输入证据和覆盖窗口 | Windows-only |
| `PetStartupCoordinator.cs` | Timer、Registry、窗口创建、首帧等待和事件触发 | Windows-only 启动协调 |
| `StartupLoadingForm.cs` | 直接读取 embedded loading asset、按 Pet canvas 等比贴底呈现 | Windows-only bootstrap visual；不依赖 `PetArtPackage` 或 Sticky runtime |
| `StartupLoadingThreadHost.cs` | 临时 WinForms STA、独立 message loop、异步置前/关闭和线程退出 | Windows-only bootstrap host；不创建 `PetForm`、Art 或 Sticky state |

`PennyApplicationHost` 先启动临时 loading STA，确认 loading 已呈现后才在主 Pet STA 构造 `PetForm`。同步 bootstrap 工作不会阻塞 loading message loop；既有 UI + art readiness 满足并触发 `StartupReady` 后，loading host 异步关闭窗口并退出。启动方面目前只有 `PetStartupRules` 中的小范围 readiness 纯门禁可复用；它不是完整的跨平台 startup framework 或状态机。

## 4. Windows / macOS 迁移地图

| 能力 | 可复用规则/数据 | macOS 必须重新实现 |
|---|---|---|
| 动画 | manifest、状态、概率、时长和冷却 | AppKit 窗口、ImageIO/CGImage 解码和帧提交 |
| 桌宠交互 | 动画选择和部分拖拽语义 | 鼠标事件、透明窗口、Space 和 Z-order |
| 便利贴 | 数据、codec、Todo/Schedule、Dock 关系和纯几何 | AppKit 编辑器、窗口、文件路径和恢复副作用 |
| Dock | `StickyDockGeometry` 与组不变量 | `NSScreen` 坐标、DPI/Retina、窗口移动和吸附反馈 |
| 每日便利贴 | 三十日进度和日期判定 | 产品 UI、内容来源、平台通知与持久化接线 |
| 提醒 | 模型和时间规则 | macOS 调度、唤醒和 UI |
| 键盘隐私 | 标准化 fail-closed 判定 | Event Tap、Accessibility、Secure Input 和权限引导 |
| 登录启动 | `StartAtLogin` 设置语义 | Login Item / ServiceManagement |
| 设置与恢复 | 字段、兼容和失败保护原则 | Application Support 路径和文件替换 |

“可复用”不等于现有 Windows `.cs` 文件可直接加入 Mac 工程，也不表示 macOS UI 已经完成。

## 5. 构建与验证

- `PennyPet.Core.csproj`：`netstandard2.0` 共享规则。
- `PennyPet.Tests.csproj`：`net8.0` Core 单元测试和程序集边界门禁。
- `PennyPet.Windows.Core.csproj`：Windows 产品程序集。
- `PennyPet.App.csproj`：正常桌宠入口。
- `PennyPet.Tools.csproj`：美术发布资源生成。
- `PennyPet.SelfTests.csproj`：Windows 资源、文件系统、WinForms/WPF、Hook 与探针。
- `PennyPet.Windows.csproj`：兼容单文件 EXE 入口。

自动测试可以验证纯规则、codec 和程序集依赖，不能替代真实中文 IME、WPF/WinForms 消息循环、透明窗口、Dock 拖拽、多屏、键盘隐私和危险路径确认的人工回归。

## 6. 后续架构原则

未来新增业务功能应优先判断哪些规则可保持平台无关，并避免把网络、业务状态和持久化决策直接塞入 Windows 窗口类。具体 Feature/Application 架构由当时负责的程序员根据真实需求设计，本文件不预设类名、目录、API、缓存、状态机、DI、Repository interface 或空工程。

只有出现真实需求和调用者时才建立相应边界，并用最小可运行测试保护。跨平台拆分定义的是技术边界，不替未来接手程序员决定产品流程。

UI ownership 按 framework / message-loop 划分，而不是要求同一 feature 的所有窗口必须位于同一个线程。WPF `StickyNoteWindow` 可以继续由 `StickyUiHost` 的 WPF Dispatcher STA 承载；WinForms Side Tabs 可以留在 Pet/WinForms UI thread，只要它们只消费 typed snapshot，并只产生 typed user-action，不直接访问 hosted WPF 窗口。

Side Tabs 是附着于 Pet chrome 的 no-activate TopMost UI；存在 tab controls 时不因普通或 hosted note 可见而降出 TopMost band。Pet monitor、working area 或 scale 改变时会重新验证 desired left/right split；分配不变时只 reposition，分配改变时才 rebuild controls。

## 7. 当前 Windows UI ownership

```text
Pet / WinForms STA
├─ PetForm
├─ canonical StickyNoteData
├─ StickyHostedRuntime
└─ Side Tabs

        typed commands/events/snapshots
                   ↓ ↑

Sticky WPF STA
├─ StickyUiThreadHost
│   └─ Thread / Dispatcher / async Post / Shutdown
│
├─ StickyUiHost
│   └─ session registry / routing / CloseAll
│
└─ StickyWindowSession
    └─ one StickyNoteWindow
       sequence
       LastSnapshot
       IME deferred close
       ApplyingBounds
       event wiring
```

数据 ownership：

- `StickyNoteData`：canonical persistent truth。
- `StickyNoteUiSnapshot`：detached hosted editing/window snapshot。
- `StickyHostedRuntime`：Pet-thread-only transient hosted protocol state。
- `DockWindowFacts`：detached geometry/runtime facts。
- `DockLayoutTarget`：pure desired effect target。
- `SideTabSnapshot`：read-only side-tab projection。

当前 hosted/legacy 双路径：

- Ordinary、Todo、Schedule 可任意 mixed Dock；设置或未设置提醒的 ordinary / Todo / Schedule 均可正常参与 mixed Dock，eligibility 不依赖 Reminder 状态。
- hosted 与 legacy executor 可混合进入同一 Dock group，并共享 Dock session、Core merge/split/layout rules、typed targets 和 visual feedback。
- hosted/mixed Dock 已覆盖 merge、group move、TopMost、horizontal resize、vertical divider、collapse-reopen、split、3-note insertion、preview、merge pulse 和 split guide。
- persisted docked notes 在重启恢复时仍可能走 legacy path，因为当前 hosted eligibility 排除已有 `DockGroupId / DockParentId` 的 note；这不是数据丢失。
- “展开全部并平铺到此屏幕”会展开所有 note、清除 canonical Dock membership，并通过各自 owned effect path 将 hosted/legacy 窗口平铺到 Pet 当前屏幕。
- Side Tabs 始终保持 no-activate TopMost chrome，并在 monitor/work-area/scale 改变时按需重新验证左右布局。
- Side Tabs 仍在 WinForms Pet STA；`SideTabSnapshot.ToDisplayData()` compatibility adapter 当前仍存在，direct snapshot consumption 是已知债务。

启动 loading ownership：

- `StartupLoadingForm` 只负责 embedded bootstrap visual、Pet scale 和保存位置/fallback；不依赖 `PetArtPackage`、Sticky repository 或 hosted runtime。
- `StartupLoadingThreadHost` 是短生命周期 WinForms STA host，拥有独立 message loop、loading form、ready/exit signal，以及异步 `BringToFront` / `Close`。
- `PetForm` 仍由主 Pet STA 创建；Art decode、Sticky restore 和 `StickyUiThreadHost.Start` 没有迁到 loading thread。
- `_startupUiReady + _startupArtReady` 仍通过 `PetStartupRules` 纯门禁释放 normal Pet frame，并由 `StartupReady` 关闭 loading。
