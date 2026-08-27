# Penny pet 1.0 开发交接说明

这份文档写给第一次接手 Penny pet 源码的开发者。目标不是解释每一行代码，而是让人先找到正确的模块，并知道哪些看似多余的兼容逻辑不能随手删除。

## 1. 项目是什么

Penny pet 是一个 Windows 桌面宠物。桌宠主体使用 WinForms 透明分层窗口；便利贴、待办和日程使用 WPF 顶层窗口；提醒、键盘显示、侧边页签和设置保存与两套 UI 共存。

产品版本唯一来源是 `desktop-pet/ProductVersion.props`。SDK 工程、单文件与保护构建、CI 文件名都读取该文件，Windows manifest 也从无版本含义的模板生成；升级版本时不要分别修改脚本。

仓库提供 `PennyPet.sln`。`PennyPet.Core` 目标为 `netstandard2.0`，包含提醒、便利贴数据、Dock、设置模型和兼容 codec 等纯规则；`PennyPet.Tests` 目标为 `net8.0` 并由 `dotnet test` 发现。Windows Core、App、Tools、SelfTests 和兼容单 EXE 项目继续目标为 .NET Framework 4.8。`PennyPet.Windows.Core` 仍是 Windows 产品程序集，不是跨平台核心。

正式单文件发布由 `PennyPet.Windows` 的 Release 构建统一完成：`PennyPet.Windows.csproj` 使用与标准构建相同的 MSBuild/Roslyn 工具链，通过 `PennyPet.Tools` 生成美术发布包和首屏缓存并内嵌到 EXE。`desktop-pet/build.ps1` 与 `BuildOfficialRelease` target 都只是调用这条构建并把产物复制到发布目录，不再维护独立的旧版编译器路径。

## 2. 如何构建测试版

在项目根目录打开 PowerShell：

仅验证标准项目能够被 IDE / 编译器理解：

```powershell
dotnet build '.\PennyPet.sln' --configuration Release
```

生成真正可运行、资源完整的单文件测试版：

```powershell
& '.\desktop-pet\build.ps1' -OutputFile '.\Penny pet-test.exe'
```

也可以从标准项目调用同一发布脚本：

```powershell
dotnet msbuild '.\desktop-pet\PennyPet.Windows.csproj' `
  -target:BuildOfficialRelease `
  '-property:OfficialOutputFile=G:\输出目录\Penny pet-release.exe'
```

美术源位于 `art`，入口是 `art/pet-art.json`。正式构建时会校验清单引用的文件，生成无损发布资源包和启动缓存；`PennyPet.Windows` 的 Release 输出本身已经是资源完整、可直接分发的单文件 EXE。

## 3. 程序入口与运行主线

- `desktop-pet/Program.cs`
  - `Program.Main`：兼容单 EXE 的命令路由，保留旧 CI/发布入口。
- `desktop-pet/PennyApplicationHost.cs`：正常应用的单实例、loading 和异常兜底。
- `desktop-pet/Core/Animation/ArtPreloadReservations.cs`：动画懒加载请求和失败后的有限重试。
- `desktop-pet/PetForm.cs`：桌宠 Windows 窗口构造、关闭和位置生命周期。
- `PetStartupController.cs` / `PetAnimationRuntime.cs` / `PetBubbleController.cs` / `PetMenuActions.cs` 以及 `Features` 下的键盘、便利贴协调文件：PetForm 的职责 partial 文件。
- `desktop-pet/PetContextMenu.cs`：桌宠右键菜单构造与命令绑定。
- `desktop-pet/Core/Animation/PetAnimationController.cs`：不依赖 WinForms 的动画状态、优先级、随机选择和冷却规则。
- `desktop-pet/Core/Reminders/PetReminderCoordinator.cs`：不依赖 Windows UI 的提醒刷新和气泡替换规则。
- `desktop-pet/PetReminderWindowsCoordinator.cs`：提醒对话框、桌宠动画、气泡和便利贴提醒条的 Windows 协调。
- `desktop-pet/StartupLoadingForm.cs`：启动 loading 窗口；收到 `PetForm.StartupReady` 后关闭。
- `desktop-pet/LayeredSpriteRenderer.cs`：Windows 分层透明窗口绘制。

这些 partial 文件是代码定位边界，仍共享同一个 `PetForm` 状态。不要把它们包装成大量 callback/interface，也不要继续为了行数切碎。

## 4. 代码模块地图

### 桌宠与美术

- `Core/Art`：美术清单、状态别名、渲染参数和帧时长纯规则。
- `PetArt.cs`：发布资源包、启动缓存、位图解码和行级懒加载。
- `Features/Art`：GDI 动画帧生命周期、画布对齐和内描边适配。
- `LayeredSpriteRenderer.cs`：把 ARGB 位图送到透明桌宠窗口。
- `Infrastructure/Persistence/WindowsDataPaths.cs`：统一 `%LocalAppData%\PennyPet`；不要在新功能里重复拼接产品数据目录。
- `StartupLoadingForm.cs`：首屏 loading 美术和尺寸定位。
- `ScaleDialog.cs`：桌宠缩放设置。

动画状态的真正来源是 `art/pet-art.json` 和 `PetArtPackage`。新增或修改动画测试时，优先读取实际 `PetArtPackage` 数据，不要再维护一份独立的帧数和时长真相。

### 便利贴、待办、日程与吸附

- `Core/StickyNotes/StickyNoteModels.cs`
  - `StickyNoteData`、待办和日程数据模型。
  - `StickyDockGroups`：组顺序、父子关系、规范化与快照恢复规则。
- `Core/StickyNotes/StickyDockOperations.cs`：Dock 组插入、单成员抽离、隐藏槽位、快照合并和长按拆分判定；窗口层不得复制这些规则。
- `Core/StickyNotes/StickyTabDropSession.cs`：跨 OLE 嵌套消息循环延迟提交页签拖放；来源窗口只作为不透明身份标识传入。
- `Core/StickyNotes/StickyNoteCodec.cs`：v1-v9 数据行编解码、内容限制、旧颜色兼容和显示修复；不接触文件路径或桌面 UI。
- `Features/StickyNotes/StickyNoteRepository.cs`：读取、旧版迁移、备份、原子保存、dirty 状态、失败重试和紧急导出。
- `StickyNotes.cs`：WinForms 管理器、标题输入框及 IME 友好输入辅助控件。
- `StickyNoteWpf.cs`：WPF 窗口构造、总体生命周期、持久化和外观接线。
- `StickyEditorController.cs`：RichText、字体、焦点和 IME；这是最高风险文件，不得擅自简化事件顺序。
- `StickyNativeWindowBehavior.cs`：Win32 消息、拖拽、resize 和最大化拦截。
- `Core/Links/StickyNoteLinks.cs` / `StickyLinkPolicy.cs`：HTTP(S)/Windows 路径识别、风险分类和确认文案；保持纯逻辑，不得访问文件系统或启动进程。
- `StickyLinkController.cs` / `StickyLinkService.cs`：普通便利贴的 WPF 格式与点击，以及确认后的文件存在检查和 Windows Shell 打开。
- `StickyTodoController.cs`：待办列表 UI、自身增删改和字号逻辑。
- `StickyScheduleController.cs`：日程 UI、自身增删改和刷新逻辑。
- `StickyReminderController.cs`：便利贴内提醒列表、倒计时和提醒操作。
- `StickyAppearanceController.cs`：便利贴外观对话框协调。
- `PetStickyDockCoordinator.cs`：吸附命中、整组拖动、窗口坐标同步和屏幕安全边界；它是 `PetForm` 的 Windows-only partial 适配模块，组关系变更调用 `StickyDockOperations`。
- `StickyNoteTabs.cs`：侧边页签、隐藏/恢复、类型图标；不再拥有拖放事务规则。
- `StickyAppearanceDialog.cs`：颜色、透明度和文字颜色设置。
- `ScheduleItemDialog.cs`：日程新增/编辑弹窗。
- `DockPulseIndicator.cs`：吸附、插入和拆分时的视觉引导。

Dock 的关键不变量：

1. 每个组只有一个最上层锚点；组内顺序由持久化的父子/顺序信息共同决定。
2. 拖最上层标题栏移动整组；长按中间或下层才拆出单张。
3. 组内宽度、相邻高度和屏幕安全边界必须一起更新。
4. 隐藏、恢复、关闭中间成员、插入中间、统一置顶都可能改变关系，修改任何一个入口前必须搜索全部调用链。

不要把 Dock 简化为“几个窗口坐标相邻”。历史 Bug 大多来自只更新了坐标却没有同步组关系，或反过来。

### 提醒与设置

- `ReminderUi.cs`
  - `ReminderDialog`：提醒编辑界面。
- `Core/Reminders/PetReminderCoordinator.cs`：提醒时间刷新、到点状态和气泡替换的纯规则。
- `PetReminderWindowsCoordinator.cs`：创建/编辑/触发提醒及 Windows UI 协调。桌宠菜单添加的提醒不会自动创建便利贴；用户后来打开或新建的便利贴会显示当前提醒列表。
- `Core/Settings/PetSettingsData.cs` / `PetSettingsCodec.cs`：位置、缩放、键盘显示、提醒字段以及新旧 INI 的纯内存编解码。
- `PetSettings.cs`：Windows 数据目录、文件大小门禁、损坏文件保留、`.bak` 恢复、原子写入、dirty 状态和失败通知；自动重试由 `PetPersistenceCoordinator` 协调。
- `Core/Reminders/ReminderModels.cs`
  - `ReminderSchedule` / `ReminderItem`：提醒列表和到点判断。
  - `ShortItemText`：提醒与待办共用的短文本计量和规范化。
- `SpeechBubbleForm.cs`：桌宠气泡的绘制、定位和自动关闭。
- `StartupRegistration.cs`：当前用户开机启动注册表项。

提醒触发主线是：`ReminderSchedule` 到点 → `PetForm` 确保 Notification 动画已按需准备 → 播放提醒动画和气泡 → 同步对应便利贴提醒条。Notification 仍然是懒加载，不能改成启动时无条件加载全部动画。

### 键盘显示与隐私保护

- `Features/KeyboardOverlay/GlobalKeyboardActivity.cs`：全局低级键盘钩子，只发布非本进程的按键活动，并同步捕获事件目标。
- `Features/KeyboardOverlay/KeyboardFocusSnapshot.cs`：保存按键发生时的窗口、进程、线程、焦点 HWND 与 UIA 身份；任一变化即 fail-closed。
- `Features/KeyboardOverlay/KeyboardInputEventArgs.cs`：Hook 发布的虚拟键、显示文字、重复次数和按键时焦点快照。
- `Features/KeyboardOverlay/KeyboardInputFormatter.cs`：Windows 虚拟键、修饰键状态和显示名称格式化。
- `Features/KeyboardOverlay/SensitiveInputDetector.cs`：只采集 UI Automation、Win32 密码样式和系统凭据进程证据；最终 fail-closed 规则由 Core 决定。
- `Features/KeyboardOverlay/KeyboardOverlayForm.cs`：透明覆盖层绘制、定位、长按保持和淡出。
- `Core/Keyboard/KeyDisplayAccumulator.cs`：不依赖系统时钟的连按/长按计数状态机。
- `Core/Keyboard/PetKeyboardPrivacyPolicy.cs`：首次确认、旧偏好处理、Hook 启动门禁和检查不可用时的隐藏规则；不引用 Windows 类型。
- `PennyPet.SelfTests.csproj`：独立 SelfTest / 探针 / 预览宿主。
- `PennyPet.Tests.csproj`：不加载 WinForms/WPF 的标准 Core 单元测试，可直接运行 `dotnet test`。
- `SelfTestRunner.cs`：文件系统、WinForms/WPF 与 Hook 的 Windows SelfTest 编排和 JSON 报告；各功能由窄检查方法负责；`RunStickyBackupCleanupCheck` 统一清理便利贴测试文件，`RunDockChecks` 汇总三组 Dock 检查，`BuildPersistenceReportFields`、`BuildStickyInteractionReportFields` 与 `BuildDockReportFields` 保持对应字段的既有顺序并通过 `Bool` 纳入总结果，不要在 `Run` 中重复这些细节或纯 Core 断言。
- `PennySelfTests.cs`：Windows 探针和测试用预览渲染。

按键显示对新用户默认关闭。关闭时不能安装全局键盘钩子；只有用户从菜单主动开启后才启动，重新关闭时立即卸载。不要把“关闭”简化成只隐藏文字覆盖层。

敏感输入检测是异步的，并用事件代次把检测结果与对应按键绑定。旧检测结果不能覆盖新按键内容。修改此处时要保留代次判断，否则密码框状态可能和显示文字错配。

### 其他

- `ApplicationDiagnostics.cs`：异常日志和带备份的原子文本写入。
- `ContactAuthorForm.cs`：联系作者窗口及小红书跳转。
- `ReverseEngineeringEasterEgg.cs`：不参与产品逻辑的彩蛋常量。

## 5. 为什么桌宠用 WinForms、便利贴用 WPF

桌宠依赖 WinForms/Win32 分层窗口来稳定显示逐帧透明美术。便利贴需要“背景半透明但文字和控件不透明”，WPF 可以在单个顶层窗口的一棵视觉树里完成，避免 TransparencyKey 空洞和跨窗口转发鼠标消息。

主消息循环由 WinForms 持有，所以 WPF 便利贴必须启用受支持的 modeless keyboard interop。`WpfApplicationHost.Ensure` 与 `StickyNoteForm.EnableWinFormsKeyboardInterop` 一带看似绕，但负责让 WPF 收到正常的键盘、字符和输入法消息。

## 6. IME 与富文本：修改前必读

`StickyNoteWpf.cs` 中以下做法是为中文及第三方输入法兼容保留的，不要为了“简化”删除：

- 工具栏取得焦点时保存 RichTextBox 选择区。
- 字体/字号通过 WPF 原生 `TextSelection` / `FlowDocument` 应用。
- 输入法正在 composition 时不插入、替换或重写 composition 文本。
- 工具栏操作后排队恢复编辑器焦点，而不是在组合文本中途强抢焦点。
- WinForms 主循环与 WPF modeless keyboard interop 的桥接。
- `StickyNotes.cs` 中 IME composition 开始/结束的代次保护。

这些代码曾用于解决：切换字体后中文无法输入、候选框出现在别处、回车后字体回退、中文重复提交。没有回归测试和真实输入法测试时，不要重写它们。

富文本持久化同时保存普通文本和 RTF。界面允许输入的内容不能在保存时静默截断；调整限制时必须同时检查 `StickyNoteLimits`、WPF 编辑器、RTF 读取和保存恢复路径。

## 7. 用户数据在哪里

运行数据目录：

```text
%LocalAppData%\PennyPet
```

主要文件：

- `settings.ini`：桌宠位置、大小、开机启动偏好、键盘显示和提醒。
- `settings.ini.bak`：设置的上一份原子保存备份。
- `sticky-notes.dat`：便利贴、待办、日程、显示状态和 Dock 信息。
- `sticky-notes.dat.bak`：便利贴数据备份。
- `diagnostics.log`：异常诊断；超过约 1 MB 后轮换为 `.previous`。

兼容逻辑会尝试从 `%LocalAppData%\FishPet\sticky-notes.dat` 和 `%LocalAppData%\ShanYingPet\sticky-notes.dat` 导入旧数据。不要仅因旧品牌名“看起来没用”就删除这两个路径。

读取失败时会先尝试 `.bak`。无法读取的便利贴文件会保留成 `.unreadable-时间.bak`；无法读取的设置会在下一次安全保存前复制为 `.corrupt-时间`。不要把失败读取直接等同于“可以用默认值覆盖原文件”。

便利贴写入失败时仓库保持 `HasUnsavedChanges=true`，主窗口每五秒自动重试并做非打扰提示。退出前会强制刷新所有编辑窗口；仍无法写入时，用户可以重试、导出当前快照或取消退出。调用保存接口时不要再假设“没有抛异常就等于成功”，应检查 `PersistenceResult` 或依赖统一协调层。

## 8. SelfTest 与探针

完整自检：

```powershell
$report = Join-Path $env:TEMP 'penny-selftest.json'
$process = Start-Process -FilePath '.\Penny pet-test.exe' `
    -ArgumentList ('--self-test=' + $report) -PassThru -Wait
Get-Content -LiteralPath $report -Raw
```

报告最外层 `ok` 必须为 `true`。以下三个布尔值按产品设计为 `false`，不是测试失败：

- `typing_moves_pet`
- `look_follow_registered`
- `keyboard_content_recorded`

纯规则回归由 `PennyPet.Tests` 承担；完整 SelfTest 只保留必须加载 Windows 产品程序集或真实资源/文件适配器的场景，避免两套测试重复维护同一规则。

常用专项入口：

- `--sticky-input-probe=<json>`：富文本/IME 输入探针。
- `--sticky-pump-probe=<json>`：WinForms 主循环下的 WPF 键盘桥探针。
- `--sticky-transparency-probe=<json>`：半透明窗口叠加与交互探针。
- `--startup-probe=<json>`：首屏缓存和启动准备探针。
- `--render-*-preview=<png>`：各 UI 视觉预览入口；模块化工程由 `PennyPet.SelfTests` 承载，兼容单 EXE 仍接受旧参数。

自动测试不能替代真实输入法、拖拽和多窗口操作。涉及 IME、Dock、半透明窗口或系统边缘行为时，构建后仍要人工测试。

## 9. 修改后的最低验证顺序

1. 编译未保护测试 EXE。
2. 运行完整 `--self-test`，确认 `ok=true`。
3. 如果改了输入：运行输入/消息循环探针，再用中文输入法手测字体、字号、回车和候选框。
4. 如果改了 Dock：手测单张↔组、组↔组、插入中间、抽出中间、关闭/恢复、不同宽度、屏幕边缘和统一置顶。
5. 如果改了存储：复制一份旧数据，在副本上测主文件损坏、`.bak` 恢复和重启后内容一致。
6. 如果改了动画：以实际 `PetArtPackage` 检查帧数、时长、脚底基线和 Notification 首次触发。

## 10. 当前维护原则与暂缓事项

- 保持 WinForms + WPF 技术栈。
- 不全面重写 Dock、IME 或富文本编辑器。
- 不为缩短代码而合并不同职责。
- 原 `ReminderUi.cs` 已只保留提醒对话框及其日期控件；设置、模型、气泡与开机启动均已按职责纯搬家拆出。
- `StickyNoteWpf.cs`、编辑器、链接和原生窗口行为已按职责原样迁移；它们仍共享窗口状态。没有真实回归需求时不要继续按行数强拆。
- 建议先用本文件的“模块地图”定位，再沿事件订阅和调用者完整阅读调用链。

最重要的判断标准不是“代码是否更短”，而是：用户功能和旧数据保持不变，同时下一位开发者能明确知道该去哪里修改、哪里不能冒进。

## 11. Adding a New Feature / 新增在线功能

当前版本没有远程内容请求。下面是以后增加真实 API Feature 时的开发顺序，不要为还不存在的功能提前创建空类、空目录或假 endpoint。

### 先判断代码应该放在哪里

- 只改变按钮、气泡或窗口外观：修改对应 Windows UI 文件。
- 增加“什么时候展示、每天是否只展示一次、如何处理空结果”等规则：新增 Feature。
- 调用一个具体远程 endpoint 并把 JSON 转成模型：新增该功能的 Service 和 model。
- 多个在线功能都需要 HTTP、超时、取消和错误分类：由第一个真实功能建立一个共享的小型 `ApiClient`。
- 需要离线副本和过期判断：通过独立 online cache/repository，不要写进设置或便利贴仓库。
- 只有需要把最终结果接到现有桌宠窗口时才修改 `PetForm`；修改应当是很薄的事件/调用接线，不能把 HTTP、JSON、缓存和每日规则搬进去。

### 虚构 Daily Content 示例

1. 后端先确定 `/v1/daily-content` 的请求、成功/失败响应和缓存 TTL。
2. 新建 Feature model，只包含业务需要的数据；不要引用 `Form`、WPF、Win32 或 Registry。
3. 新建 `DailyContentService`，只通过共享 `ApiClient` 请求并验证响应。
4. 新建 `DailyContentFeature`，决定今天是否应该获取/展示，并把结果转换为简单 Presentation result。
5. 如需缓存，通过共享 Online Cache 保存抓取时间、过期时间和 payload；缓存与用户核心数据分开。
6. Windows UI 接收 Presentation result 后决定显示气泡或窗口，并在正确 UI 线程更新。
7. 使用 fake/stub 测试成功、网络失败、坏 JSON、空内容、有效缓存和过期缓存，不访问真实公网。
8. 验证断网时 Penny 主体、提醒、便利贴和启动仍然正常。

推荐依赖方向：

```text
Pet UI <- Feature result <- Feature -> Remote Service -> ApiClient
                              |              |
                              +-> Cache      +-> API model
```

Feature 不知道 `SpeechBubbleForm` 的控件结构；Remote Service 不操作桌宠动画；ApiClient 不判断“今天是否已经推送”。参见 `ARCHITECTURE.md` 的在线功能、缓存、API 契约和安全章节。

### 网络与线程最低要求

- 使用长生命周期 `HttpClient`、HTTPS、明确超时和 `CancellationToken`。
- 网络请求异步执行；WinForms/WPF UI 线程不得同步 `.Result` / `.Wait()`。
- 只有 Presentation 边界使用 `BeginInvoke`、WPF `Dispatcher` 或 UI `SynchronizationContext`。
- 最多只对幂等 GET 的瞬时失败做一次有限重试；不要引入 Polly 或大型网络框架。
- 失败应返回可判断的结果，不应让异常穿透到桌宠主循环，也不要向用户弹技术错误堆栈。

### Secret 与日志

第三方 secret 不进入客户端。需要保密 key 时使用：Penny 客户端 -> Penny 自有后端 -> 第三方 API。secret 只放后端的安全配置/secret store，不放 EXE、GitHub、`settings.ini` 或日志。

网络日志只能记录功能类别、净化后的 endpoint 标识、HTTP status、错误类型、时间和版本。不得记录 Authorization、cookie、token、请求/响应正文、便利贴、Todo、Schedule、Reminder 或键盘内容。

## 12. 交给 API 开发者前的检查清单

- 后端契约已定义，且允许增加未知字段而不破坏旧客户端。
- 已明确该功能是否真的需要用户数据；默认不上传任何本地私人内容。
- 已定义网络失败、无缓存、旧缓存和空内容时的产品行为。
- Feature/Service/model 不引用 WinForms、WPF、Win32、Dock 或 IME。
- `PetForm` 只增加最终展示接线，没有直接 HTTP/JSON/缓存代码。
- 测试使用 fake/stub，不依赖公网和真实 secret。
- 完整 build、SelfTest 以及该功能对应的 Windows 人工展示验证均通过。

## 13. 已知安全边界与独立加固任务

### 按键显示不是绝对的密码检测器

`SensitiveInputDetector` 会检查 UI Automation 的 password 属性、标准 Win32 密码样式、控件名称和已知系统凭据进程；这些保护有价值，但第三方自绘控件、浏览器内部实现、跨权限窗口、远程桌面和拒绝 UI Automation 的窗口不能保证全部识别。当前实现无法完成 UIA/原生检查时默认隐藏，并在首次开启全局按键显示前要求用户确认隐私说明；产品文案仍只能说“尽力隐藏”，不能承诺 100%。

相关策略集中在 `Features/KeyboardOverlay`。按键发生时会捕获目标快照，后台检查前后任一窗口/进程/线程/焦点/UIA 身份变化都会隐藏该按键；同时保留首次确认、检查失败 fail-closed 和 Hook opt-in 自测。跨浏览器、自绘控件、管理员窗口与真实密码输入仍必须人工验证。

### 本地路径打开需要风险分级

`Core/Links/StickyNoteLinks.cs` 把 URL 限制为 HTTP/HTTPS，并用与运行宿主无关的规则识别盘符和 UNC 路径；`Core/Links/StickyLinkPolicy.cs` 负责打开风险分类与确认文案。`Features/StickyNotes/StickyLinkService.cs` 只在确认后检查文件是否存在并调用 Windows Shell。普通文档和文件夹直接打开；可执行文件、脚本、快捷方式以及 UNC 网络路径必须二次确认，默认按钮为“不打开”。UNC 路径必须先取得用户确认，再探测网络文件系统。WPF 点击和小手光标仍在 `StickyLinkController.cs`。

风险分类与确认文案已有纯逻辑测试。继续修改时仍要回归普通 URL、文件夹、常用文档、中文路径、长路径、无效路径和 IME/链接格式。
