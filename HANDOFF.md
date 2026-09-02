# Penny-Pet 技术交接

本文对应当前发布版本 `1.0.2`。

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
- 当前没有 macOS UI 工程、完整 Application 层或完整平台无关启动状态机。已有网络边界仅限 Windows 的 opt-in Open-Meteo 天气适配，不应把它描述成通用网络服务框架。

## 后续原则

未来新增业务功能应先判断哪些规则能保持平台无关，避免把网络、业务状态和持久化决策直接塞入 Windows 窗口类。具体 Feature/Application 结构由届时负责的程序员依据真实需求设计，本仓库不预设类名、目录、DI、Repository interface 或空工程。

每次只迁移一组可在无窗口环境验证的规则；Windows Coordinator 继续负责平台事实、类型转换和副作用。完整 Windows SelfTest 与发布验证留给 CI/发布阶段。

## Daily Briefing copy ownership

- Daily Briefing 的硬上限是三条用户实际读到的 semantic sentence；Greeting 计入预算，每个 supplementary 最多一条。Solar/Weather/Almanac 任一存在后不为填满位置而补 Curated/Zodiac。
- Weather catalog 只保留一个直说事实和至多一个行动；Almanac 一句内完成内容与必要现实降权。Bubble 不负责靠加宽、缩字或换行掩盖内容冗余。
- `DailyBriefingSentence` 只携带 Body、ContentKind、显式 Intent 与稳定内容 ID。Composer 分配 `Single / Opening / Middle / Closing`，`PetSentenceEndingPolicy` 最后统一决定标点和语气词。
- Ending 是确定性派生展示结果：同日同内容稳定，不使用 `GetHashCode`，不写设置、不留历史、不建立 Cache；Reminder 与现有 SmallTalk 暂不接入。

## Daily Weather ownership

- `Core/DailyContent/Weather` 只接收 detached 三日摘要并做语义与确定性 wording；不得引用 HTTP、Open-Meteo URL 或网络 DTO。
- `Infrastructure/Weather/OpenMeteo*` 负责手动城市搜索、固定 forecast request 和 JSON 转换；`PetWeatherSource` 是唯一 `HttpClient`、成功 Cache、同键 in-flight 与失败冷却 owner。
- 城市是用户偏好：只长期保存当前选中值并覆盖更新。预报是 Cache：只在进程内最多保留 3 个地点日键。请求失败和 in-flight 是最小运行状态，不写入磁盘。
- Weather 默认关闭，启动零请求，不轮询，不读取系统/IP 定位；Daily Poke 网络失败必须静默回退到 Solar/Almanac/filler。
- 普通 SelfTests 只能读取 embedded fixture。真实调用只允许通过显式 `--weather-api-probe=<path>`，且网络失败不得使 gate 或本地功能失败。

## 当前 Sticky hosted 单执行器

- `PetForm` 在 WinForms STA 持有 canonical `StickyNoteData`、`StickyHostedRuntime` 和 Side Tabs。
- `StickyUiThreadHost` 只管理 STA Thread / Dispatcher / async Post / Shutdown，不拥有 WPF Window。
- `StickyUiHost` 是唯一 production Sticky window executor，管理 session registry、命令路由和 CloseAll；`PetForm` 不再直接持有窗口表或 silent legacy fallback。
- `StickyWindowSession` 是唯一持有 `StickyNoteWindow` 的运行时会话对象；窗口内数据是 detached working copy，canonical ownership 仍在 Pet thread。
- Reminder 是所有便利贴共享的 capability/UI，不是独立 Sticky subtype 或第四种 Dock participant；设置或未设置提醒的 ordinary / Todo / Schedule 均可正常参与 mixed Dock。
- Ordinary、Todo、Schedule 是同一个 Sticky window system 的三种 content mode；Dock grouping type-agnostic，任意 mixed-type group 共用 detached facts、Core rules、`DockLayoutTarget` 和 hosted effect boundary。
- Preview、merge pulse、split guide，以及 group move、TopMost、horizontal/divider resize、collapse-reopen、middle split 和多成员 insertion 已完成。
- persisted standalone 与 Dock component 都通过 hosted session 恢复；v1-v9 codec 和旧文件迁移继续保留，persisted data 不记录 executor 类型。
- “展开全部并平铺到此屏幕”会展开全部 note、真正清除 Dock relation，并通过唯一 hosted effect path 平铺。
- Side Tabs 是不激活的 TopMost Pet chrome；monitor、work area 或 Pet scale 改变时会重新验证左右 split，仅在分配变化时 rebuild。
- Side Tabs 继续由 WinForms Pet STA 承载，直接消费 Core 中 detached `SideTabSnapshot`；业务 note identity 使用稳定 `NoteId`，平台 UI source identity 保持本地 opaque object。OLE nested-loop、透明 canvas 和 WinForms z-order workaround 属于 Windows 实现，不要求 macOS 复制。
- Pet-owned WinForms Form modal 统一经过 `PetWindowLayerCoordinator` 的内存栈；Keyboard Overlay、Bubble 和 Side Tabs 保持 no-activate，并位于嵌套 modal chain 之后。键盘提示始终跟随 Pet，不因 modal 改变位置。密码/凭据检测仍由原隐私链独立 fail closed。

## Sticky 管理与备份

- `StickyNotesManagerForm` 是唯一管理入口，保留表格 UI；桌面整理、导出和 Import & Merge 都只调用现有 owner 提供的具体命令。
- 四列表头排序、搜索和 Import Preview 都是 view state，不得改动 repository、SideTab order 或 Dock order。
- Import & Merge 在同一 Manager 中执行完整 read/validate/plan 后进入 Preview；取消/关闭零修改，确认时重新规划，再经 rolling pre-import backup、atomic commit 和 hosted/SideTab refresh。
- 新增和 conflict copy 默认隐藏进 Side Tabs；已有 NoteId 保留 current geometry、visibility 和 Dock state。完整新 mixed Dock 可保留，碰到 current member 的 partial group 保守解除 imported member 的旧关系。
- `.pennysticky` v1 只携带 Sticky dataset。完整 reminder records 仍在 `settings.ini`，linked/standalone reminder 都不属于当前 portable contract；未来若扩展必须重映射 conflict copy 的 `SourceNoteId`。
- macOS 可复用 `StickyNoteData`、codec、validator、merge planner、Dock pure rules 和 `SideTabSnapshot`；WinForms Manager、Open/Save dialogs、Windows 文件路径/原子替换及 Hosted runtime reconcile 必须由平台侧实现。

## Startup loading ownership

- `StartupLoadingForm` 直接读取 embedded `PennyPet.Startup.Loading`，在 Pet-size transparent canvas 内等比、水平居中、底部对齐；它不依赖 `PetArtPackage` 或 Sticky runtime。
- `StartupLoadingThreadHost` 是临时 WinForms STA，独立运行 loading message loop；`BringToFront` / `Close` marshal 回该线程，关闭后线程退出。
- `PennyApplicationHost` 确认 loading 已呈现后，仍在主 Pet STA 构造 `PetForm`。Art decode、Sticky 初始化与恢复不属于 loading thread。
- `_startupUiReady + _startupArtReady`、normal frame suppression 和 `StartupReady` 语义保持不变。`PetStartupRules` 只是这两个 readiness 输入的纯 gate，不是完整 startup framework。
