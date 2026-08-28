# Penny 架构与平台迁移地图

本文记录当前已经落地的技术架构、Windows/通用边界和 macOS 迁移地图。它不是未来业务功能设计稿，也不要求立即创建 macOS UI、Application 层或网络基础设施。

本文对应当前发布版本 `1.0.1`。

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

启动方面目前只有 `PetStartupRules` 中的小范围 readiness 判定可复用。文档不得把它描述成完整的跨平台启动状态机。

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
