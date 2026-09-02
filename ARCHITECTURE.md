# Penny 架构与平台迁移地图

本文记录当前已经落地的技术架构、Windows/通用边界和 macOS 迁移地图。它不是未来业务功能设计稿，也不要求立即创建 macOS UI、Application 层或网络基础设施。

本文对应当前发布版本 `1.0.2`。

当前原则：保持 Windows 版行为、数据格式和构建结果；只把已能用平台无关数据表达和验证的规则放入 Core，不为架构形式引入空接口、依赖注入或大型框架。

## 1. 当前架构结论

- `PennyPet.Core` 目标为 `netstandard2.0`，保存平台无关模型与纯规则。
- `PennyPet.Windows.Core`、`PennyPet.App`、`PennyPet.Windows` 和 `PennyPet.SelfTests` 仍是 Windows 实现或宿主。
- `PetForm` 与 `StickyNoteWindow` 各自按职责拆成 partial 文件；partial 只是同一类型内部的代码定位边界，不是假装独立的服务层。
- 高风险 IME、WPF/WinForms 消息桥、Window Message、Keyboard Hook、UI Automation、GDI、Shell、Registry 和真实窗口副作用继续留在 Windows 实现。
- Dock 组关系、统一置顶数据、页签拖放会话和纯数值几何已进入 Core；启动 Core 当前只包含 loading readiness 纯判定。
- HTTP(S) 与 Windows 路径/UNC 链接识别、危险扩展名、确认文案、文件探测和 Shell 打开位于 `Features/StickyNotes`。
- 当前没有 `PennyPet.Mac`、完整跨平台 Application 层、网络服务或完整平台无关启动状态机。

`CoreArchitectureTests` 会检查编译后 Core 的程序集引用，阻止 Drawing、WinForms、WPF、Registry 和 UI Automation 进入共享层。这个门禁只证明代码依赖；盘符、UNC、Win32 坐标限制、键码和权限模型等语义上的平台假设仍需人工审查。

## 2. 依赖方向

```text
PennyPet.App / PennyPet.Windows
  -> PennyApplicationHost
  -> PetForm（Windows 窗口与协调）
       -> Core 动画、提醒、设置、键盘隐私规则
       -> Windows Hook、UIA、GDI、Registry、Screen 与窗口副作用

Pet Sticky coordination
  -> canonical StickyNoteData / Core Dock rules
  -> StickyUiCommand / StickyUiEvent / detached snapshots
  -> StickyUiHost session registry（Sticky WPF STA）
       -> StickyWindowSession
            -> StickyNoteWindow（Editor / Todo / Schedule / Reminder / Link）

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
| `Core/DailyContent/DailyContentRules.cs` | 日期键、每日一次、DayPart 与 semantic greeting 纯规则 | 平台无关 |
| `Core/DailyContent/DailyLineEntry.cs` | 有稳定 ID 的内置每日短句值 | 平台无关 |
| `Core/DailyContent/CuratedDailyLineCatalog.cs` / `CuratedDailyLineSelector.cs` | 96 条有限精选目录及当地日期确定性选择 | 平台无关纯规则 |
| `Core/DailyContent/ZodiacSign.cs` | 用户星座偏好的稳定业务身份 | 平台无关 |
| `Core/DailyContent/ZodiacDailyCatalog.cs` / `ZodiacDailySelector.cs` | 72 条有限 Zodiac 目录及当地日期约 15% 的确定性补位资格 | 平台无关纯规则 |
| `Core/Calendar/Almanac/AlmanacCalculator.cs` | 当地民用日期经 `lunar-csharp 1.6.8`、显式 sect 1 转成 detached Yi/Ji | 平台无关第三方适配边界；失败返回 null |
| `Core/DailyContent/Almanac/AlmanacSemanticCatalog.cs` | 原始传统术语的精确保守白名单与现代 Topic | 平台无关纯规则 |
| `Core/DailyContent/Almanac/AlmanacDailySelector.cs` / `AlmanacWordingCatalog.cs` | 稳定 Topic 排序、冲突抑制、确定性候选与非机械 wording | 平台无关纯规则；无历史和缓存 |
| `Core/DailyContent/Weather` | detached 地点/三日摘要、显式天气语义优先级和确定性 wording | 平台无关纯规则；无 HTTP、URL 或网络 DTO |
| `Infrastructure/Weather/OpenMeteo*` | 手动城市搜索、固定 8 变量预报请求与 JSON → Core 摘要转换 | Windows 网络适配边界 |
| `Infrastructure/Weather/PetWeatherSource.cs` | 单一 `HttpClient`、同地点日 in-flight、最多 3 键进程 Cache 与 15 分钟失败冷却 | Windows runtime；不持久化预报 |
| `Core/DailyContent/DailyBriefingContent.cs` | Solar、Weather、Almanac、Curated、Zodiac 候选值及短生命周期 sentence DTO | 平台无关；不持久化 |
| `Core/DailyContent/DailyBriefingComposer.cs` | 候选优先级、最多三条 semantic sentence、Role 分配与最终 join | 平台无关纯规则 |
| `Core/Messaging/PetSentenceEndingPolicy.cs` | Role、显式 Intent、ContentKind、稳定内容 ID 与当地日期驱动的句末标点/语气 | 平台无关确定性纯规则；无 NLP、历史或设置 |
| `Core/Calendar/SolarTerm*` | 二十四节气天文事实 | 平台无关 |
| `PetDailyContentCoordinator.cs` | 首次有效 Poke eligibility、异步天气收集、compose、Bubble accepted 后消费日期 | Windows 产品协调 |
| `DailyContentSettingsForm.cs` / `WeatherLocationDialog.cs` | Daily、节气、天气城市和星座偏好；搜索只由明确按钮触发，Enter 已归还输入法 | Windows-only |

`DailyBriefing` 按用户实际读到的 semantic sentence 计数：Greeting 也占一条，总数最多三条，每个 supplementary 最多贡献一条。事实优先级为 `SolarTerm → Weather → Almanac → filler`；Solar 与 Weather 同时存在时输出两者，没有 Solar 时 Weather 可与 Almanac 同时输出。任一高新鲜度事实存在后不机械补 Curated/Zodiac，三者都不存在才进入 filler layer。Weather 固定为一个直说事实加至多一个行动提醒，Almanac 在同一句内完成信息与必要降权。两个 filler 目录分别固定上限为 96 和 72 条。

Daily 文案先以 `Body + ContentKind + StableContentId + Intent` 进入 Composer；Composer 根据一句/首句/中句/末句分配 `Single / Opening / Middle / Closing`，再由 `PetSentenceEndingPolicy` 统一添加终止标点或轻量语气。Middle 默认使用普通标点，Closing 才是主要柔化点，Question 由显式 Intent 决定，严肃天气不会获得 cheerful ending。同日同内容使用 FNV-1a 稳定选择，不保存 Ending history，也不进入 Bubble UI、Reminder 或 `settings.ini`。

Almanac 的边界固定为 `lunar-csharp → raw traditional Yi/Ji → Penny exact semantic whitelist → modern conversational copy`。原始宜忌绝不直接成为 DailyBriefing 输出；现代化只改变表达，不改变传统术语含义。医疗、法律、财务、丧葬、宗教、施工及其他不适合的传统术语不进入 v1 建议；Yi/Ji 对同一 Topic 冲突时该 Topic 当天直接抑制。Penny v1 采用日粒度、sect 1 的黄历语义以保证同一当地民用日内稳定，这不代表其比其他民俗流派更权威。

每日新鲜度主要来自变化中的事实语境，而不是无限增加固定文案。Weather 仅在用户 opt-in 后由首次 eligible Poke 异步请求；普通动画先启动，网络失败静默回退，启动零请求且没有轮询。Today/Yesterday/Tomorrow 按所选城市本地日期解析（API `utc_offset_seconds` + UTC now），不得用电脑本地日期切日。成功预报只存在于最多 3 个 location 进程 Cache 中，城市本地日期跨日后旧 Today 不再复用；同键并发共享一个 Task，改变城市清空 Cache，失败键 15 分钟内不重试。城市偏好是覆盖保存的用户偏好，预报是有界 Cache，均不产生长期历史。

Open-Meteo Forecast 请求固定为昨天、今天、明天和 8 个小时变量，3 秒超时、零自动重试；Geocoding 只在用户明确搜索时请求最多 5 条中文结果，必须由用户确认，不按键搜索，也不在重启后重新解析已保存城市。实现时（2026-09-01）免费服务页面列出的限制为 600 次/分钟、5,000 次/小时、10,000 次/日、300,000 次/月；这些数字只记录实现时依据，不是运行时契约，发布或商业使用前必须重新核对 [官方定价/限制](https://open-meteo.com/en/pricing) 并评估 customer endpoint。

`PennyPet.Core` 直接使用 `CosineKitty.AstronomyEngine 2.1.19` 计算既有 SolarTerm，并使用 `lunar-csharp 1.6.8` 读取 Almanac Yi/Ji；两条链互不替代。兼容单文件 Windows 项目把固定的 `astronomy.dll` 与 `lunar.dll` 嵌入 EXE，并由窄范围 `EmbeddedAssemblyResolver` 只按这两个确切程序集名解析；第三方许可见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

### 便利贴、Dock 与链接

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/StickyNotes/StickyNoteModels.cs` | 便利贴、三态 Todo、Schedule 和 Dock 持久化模型 | 平台无关 |
| `Core/StickyNotes/StickyNoteCodec.cs` | v1-v9 数据行编解码、兼容和内容限制 | 平台无关 |
| `Core/StickyNotes/StickyImportBackupValidator.cs` / `StickyImportMergePlanner.cs` | 完整备份校验、稳定 NoteId 合并、冲突副本、Dock 保守降级 | 平台无关纯规则；不读文件、不修改 live repository |
| `Core/StickyNotes/StickyDockOperations.cs` | Dock 组插入、抽离、隐藏槽位、快照和统一置顶数据 | 平台无关 |
| `Core/StickyNotes/SideTabSnapshot.cs` | Side Tabs 所需的 detached 轻量显示投影 | 平台无关；不是 canonical/persistence owner |
| `Core/StickyNotes/StickyTabDropSession.cs` | 页签拖放事务 | 平台无关；窗口来源是不透明身份 |
| `Core/StickyNotes/StickyDockGeometry.cs` | `DockPoint`、`DockSize`、`DockRect` 及 Dock、divider、header 可达性、恢复、新建、页签、弹窗和异常拖拽恢复几何 | 平台无关纯数值规则 |
| `Core/Startup/PetStartupRules.cs` | UI/美术 readiness 门禁 | 平台无关的小范围启动判定，不是完整启动框架 |
| `Features/StickyNotes/StickyNoteLinks.cs` / `StickyLinkPolicy.cs` | HTTP(S)、Windows 路径和危险目标策略 | Windows-only 链接策略 |
| `Features/StickyNotes/StickyNoteRepository.cs` | Windows 文件保存、备份、损坏恢复、dirty 和重试 | Windows 文件系统适配 |
| `Features/StickyNotes/StickyNotes.cs` / `PetPersistenceCoordinator.cs` / `StickyBackupFileReader.cs` | Manager、导入预览、文件选择、pre-import backup、原子 commit 和 hosted reconcile | Windows-only UI/文件副作用 |
| `Features/StickyNotes/StickyLinkService.cs` | 盘符/UNC、扩展名风险、确认、文件探测和 Shell 打开 | Windows-only 路径策略 |
| `Features/StickyNotes/StickyLinkCoordinator.cs` | WPF 链接格式、点击和光标 | Windows-only UI |
| `Features/StickyNotes/PetStickyDockCoordinator.cs` | 屏幕/DPI/原生几何转换、canonical Dock 协调和 typed hosted effects | Windows-only 副作用适配 |
| `Features/StickyNotes/StickyEditorCoordinator.cs` | RichText、焦点和 IME | Windows-only，高风险 |
| `Features/StickyNotes/StickyNativeWindowBehavior.cs` | Win32 消息、拖拽、resize 和最大化拦截 | Windows-only，高风险 |

Windows Coordinator 把 `Point`、`Size`、`Rectangle` 等平台对象转换为 Core 的 `DockPoint`、`DockSize`、`DockRect`，调用纯规则后再执行 WPF/Win32 窗口副作用。`DockCoordinateSafetyLimit = 30000` 是 Win32 安全范围，由 Windows 层作为参数提供给 Core 计算；它不是 Penny 业务规则，也不能成为 macOS 常量。

Ordinary、Todo、Schedule 属于同一个 Sticky window system，内容模式不同但 Dock grouping 完全 type-agnostic，任意组合都可进入同一 Dock group。Reminder 是所有便利贴共享的 capability/UI，不是独立 Sticky subtype 或第四种 Dock participant；eligibility 不依赖 `IsTodoList / IsSchedule / ReminderUtcTicks`。`StickyUiHost` 是唯一 production window executor：Pet 端以 `DockWindowFacts` 进入同一 Dock session/Core rules，得到 `DockLayoutTarget` 后只通过 typed command effect 落到 Sticky STA。Preview、merge pulse 和 split guide 同样使用 detached geometry。

`StickyNotesManagerForm` 是现有 Sticky repository 的 Windows 管理视图，不是 persistence owner。表头排序只改变当前表格顺序，不修改 canonical、SideTab 或 Dock order。Import & Merge 固定走 `Read → Parse → Validate → Plan → Preview → Confirm → Commit`；Preview 只保留在当前 Form 生命周期内，取消或关闭即丢弃。确认时 Pet 端重新计算计划，再由 `StickyNoteRepository` 使用单个轮转 pre-import backup 和原子写入提交；既有 NoteId 的空间/可见状态保留，新 note 与 conflict copy 默认 `Visible=false` 进入 Side Tabs。

Sticky Backup v1 的 portable dataset 是 `sticky-notes.dat` 中的 Sticky 模型。`StickyNoteData.ReminderUtcTicks` 只是便于 Sticky UI 显示的下一次提醒投影；真正的 reminder records（文本、时间、预提醒和 `SourceNoteId`）由 `settings.ini` / `ReminderSchedule` 持有，当前 `.pennysticky` 不包含它们。因此 v1 不宣称 linked reminder 可跨电脑迁移，standalone reminder 也明确不属于 Sticky Backup；若以后支持，必须同时迁移并在 conflict copy 时重映射 `SourceNoteId`，不能只复制时间戳。

### 提醒、设置、启动与键盘隐私

| 文件/目录 | 当前职责 | 边界 |
|---|---|---|
| `Core/Reminders` | 提醒模型、时间刷新和气泡替换规则 | 平台无关 |
| `PetReminderWindowsCoordinator.cs` / `ReminderUi.cs` | 提醒窗口、动画和气泡协调 | Windows-only |
| `Core/Settings` | 设置数据、`StartAtLogin` 语义和旧 INI codec | 平台无关 |
| `PetSettings.cs` / `WindowsDataPaths.cs` | Windows 路径、备份、原子保存和诊断 | Windows-only |
| `Core/Keyboard` | 首次确认、偏好和 fail-closed 隐私判定 | 平台无关标准化输入 |
| `Features/KeyboardOverlay` | Hook、虚拟键、UIA/Win32 敏感输入证据和覆盖窗口 | Windows-only |
| `PetWindowLayerCoordinator.cs` | Pet-owned Form modal stack 和 no-activate transient z-order | Windows-only 必要运行状态；不持久化 |
| `PetStartupCoordinator.cs` | Timer、Registry、窗口创建、首帧等待和事件触发 | Windows-only 启动协调 |
| `StartupLoadingForm.cs` | 直接读取 embedded loading asset、按 Pet canvas 等比贴底呈现 | Windows-only bootstrap visual；不依赖 `PetArtPackage` 或 Sticky runtime |
| `StartupLoadingThreadHost.cs` | 临时 WinForms STA、独立 message loop、异步置前/关闭和线程退出 | Windows-only bootstrap host；不创建 `PetForm`、Art 或 Sticky state |

`PennyApplicationHost` 先启动临时 loading STA，确认 loading 已呈现后才在主 Pet STA 构造 `PetForm`。同步 bootstrap 工作不会阻塞 loading message loop；既有 UI + art readiness 满足并触发 `StartupReady` 后，loading host 异步关闭窗口并退出。启动方面目前只有 `PetStartupRules` 中的小范围 readiness 纯门禁可复用；它不是完整的跨平台 startup framework 或状态机。

## 4. Windows / macOS 迁移地图

| 能力 | 可复用规则/数据 | macOS 必须重新实现 |
|---|---|---|
| 动画 | manifest、状态、概率、时长和冷却 | AppKit 窗口、ImageIO/CGImage 解码和帧提交 |
| 桌宠交互 | 动画选择和部分拖拽语义 | 鼠标事件、透明窗口、Space 和 Z-order |
| 便利贴 | 数据、codec、Todo/Schedule、Dock 关系、纯几何、backup validation 和 merge planning | AppKit 编辑器、窗口、Manager、文件选择/路径、原子写入和 runtime reconcile |
| Dock | `StickyDockGeometry` 与组不变量 | `NSScreen` 坐标、DPI/Retina、窗口移动和吸附反馈 |
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

Side Tabs 是附着于 Pet chrome 的 no-activate UI；左右 strip 各自按几何 overlap 决定是否 TopMost，被可见 Sticky 覆盖的 strip 临时降层，移开后恢复。TopMost/BringToFront 与 window-layer 诊断只在 overlap 状态变化时执行。Pet monitor、working area 或 scale 改变时会重新验证 desired left/right split；分配不变时只 reposition，分配改变时才 rebuild controls。

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
- `StickyNotesImportPreview`：当前 Manager 生命周期内的有界 plan state；取消、关闭或提交后清除，不持久化、不追加历史。

当前单一 hosted executor：

- Ordinary、Todo、Schedule 可任意 mixed Dock；设置或未设置提醒均不改变 participant eligibility、group order 或 geometry。
- 新建、独立恢复、persisted Dock component 恢复、SideTab 展开及 Dock/TopMost/resize/hide/close effects 全部由 `StickyUiHost` 执行；`PetForm` 不再持有 `StickyNoteWindow` registry 或 legacy fallback。
- Hosted Dock 覆盖 merge、group move、TopMost、horizontal resize、vertical divider、collapse-reopen、split、多成员 insertion、preview、merge pulse 和 split guide。
- “展开全部并平铺到此屏幕”会展开所有 note、清除 canonical Dock membership，并通过唯一 hosted effect path 平铺到 Pet 当前屏幕。
- v1-v9 Sticky persistence codec 继续保留；旧数据先转换为 canonical `StickyNoteData`，运行时 executor 信息不写入用户数据。
- Side Tabs 保持 no-activate chrome，并只在真实被可见 Sticky 覆盖时按 strip 降层；monitor/work-area/scale 改变时按需重新验证左右布局。
- Side Tabs 仍在 WinForms Pet STA，直接消费 detached `SideTabSnapshot`；便利贴业务身份使用稳定 `NoteId`，拖拽来源 UI identity 保持平台本地 opaque object。OLE nested-loop、TransparencyKey canvas、BringToFront timing 等 workaround 是 Windows-only，不是未来 macOS UI 的复用契约。

启动 loading ownership：

- `StartupLoadingForm` 只负责 embedded bootstrap visual、Pet scale 和保存位置/fallback；不依赖 `PetArtPackage`、Sticky repository 或 hosted runtime。
- `StartupLoadingThreadHost` 是短生命周期 WinForms STA host，拥有独立 message loop、loading form、ready/exit signal，以及异步 `BringToFront` / `Close`。
- `PetForm` 仍由主 Pet STA 创建；Art decode、Sticky restore 和 `StickyUiThreadHost.Start` 没有迁到 loading thread。
- `_startupUiReady + _startupArtReady` 仍通过 `PetStartupRules` 纯门禁释放 normal Pet frame，并由 `StartupReady` 关闭 loading。
