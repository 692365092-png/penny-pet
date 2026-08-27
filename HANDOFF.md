# PennyPet 第一天接手指南

本文写给第一次打开 PennyPet 仓库、没有参与过历史开发的成熟开发者。它不是完整架构说明，也不复述用户功能；它的作用是让你在第一天建立正确心智模型，知道先读什么、代码应放在哪里、哪些“看起来复杂”的地方不能凭直觉简化，以及改动后最低要证明什么。

本文以当前源码、项目文件和已验证构建链为事实源。若其他文档中的历史施工措辞与当前代码冲突，以代码和可重复的 build/test 结果为准。

## 1. 先建立一个准确模型

PennyPet 是 Windows 桌面宠物，当前没有账户、遥测或在线内容服务。产品同时使用三类边界：

- **WinForms / Win32**：桌宠主体、透明分层窗口、右键菜单、系统托盘式生命周期、低级键盘 Hook、屏幕与 DPI 协调等 Windows 宿主能力。
- **WPF**：便利贴、Todo、Schedule 及其富文本编辑器。WPF 用于实现“背景半透明、文字和控件保持不透明”的顶层窗口，并提供 RichTextBox、字体和中文 IME 交互。
- **PennyPet.Core**：目标为 `netstandard2.0` 的平台中立规则和数据模型。它包含 Reminder、Sticky Note/Dock、设置 codec、动画规则、美术清单规则、链接风险分类和键盘隐私决策，但不拥有窗口、文件路径、注册表、Bitmap、Hook 或 UI Automation。

依赖方向应当是 Windows 宿主调用 Core，而不是 Core 认识 Windows。这样做不是为了追求抽象层数：Core 是当前最容易稳定自动测试、未来可被其他宿主复用的行为规范。如果 Windows 类型渗入 Core，标准测试会被迫加载桌面框架，macOS 或其他宿主也无法复用规则。`CoreArchitectureTests` 因此会扫描 `Core/**`，发现 WinForms、WPF、Registry、System.Drawing.Bitmap 或 UIAutomation 引用时直接失败。

WinForms 和 WPF 并存是产品行为约束，不是等待“统一技术栈”的临时状态。WinForms 持有主消息循环；WPF 便利贴通过 modeless keyboard interop 接入。不要把“技术栈统一”当作默认重构目标。

## 2. 第一天按这个顺序阅读

1. **`README.md`**：先了解用户看到的产品、最短构建命令、隐私承诺和仓库顶层结构。
2. **本文 `HANDOFF.md`**：建立维护边界、风险清单和验证矩阵。
3. **`DEVELOPER_GUIDE.md`**：按具体任务查模块地图、数据位置、SelfTest/探针、IME 细节和新增 Feature 流程。这是日常开发的主要索引。
4. **`ARCHITECTURE.md`**：需要判断跨平台边界、未来在线功能、缓存/API/secret 约束或 macOS 迁移语义时再读。不要把其中的未来建议理解成当前必须创建的空架构。
5. **`PRIVACY.md`、`SECURITY.md`**：涉及键盘、链接、网络、日志、用户数据或发布时必读。
6. **`desktop-pet/README.md`**：涉及美术包、动画资源和命令入口时阅读。

阅读代码时从入口和事件订阅开始，再沿调用链进入对应 partial / Coordinator；不要只按文件名或行数挑一个文件局部修改。

### 当前事实基线

`ARCHITECTURE.md` 描述的是当前仓库已经落地的工程状态，不是等待合并的实验方案；项目拆分、Core tests、SelfTests、共享美术构建和兼容单 EXE 均属于当前构建链。根 `README.md` 记录当前命令行构建环境：需要 **.NET 8 SDK**（Tests 和 SDK/MSBuild）以及 **.NET Framework 4.8 Developer Pack/Targeting Pack**（net48 引用程序集），仅安装 4.8 Runtime 不足以构建。

## 3. 仓库和工程地图

### 顶层目录

- `art/`：角色 GIF、`pet-art.json`、loading 和界面图片。文件较大，构建时会生成发布 pack 与启动 cache。
- `desktop-pet/`：全部 C# 源码、项目文件、构建脚本和测试。
- `.github/workflows/build.yml`：Windows CI；构建、标准 Tests、模块化 SelfTest、正式单 EXE、版本检查、正式 SelfTest、SPDX SBOM、`SHA256SUMS.txt` 和 artifact 上传。
- `README.md` / `DEVELOPER_GUIDE.md` / `ARCHITECTURE.md`：分别面向产品入口、日常维护和架构/未来边界。

### 七个工程

- `PennyPet.Core.csproj` (`netstandard2.0`)：只编译 `Core/**`，承载平台中立模型、codec 和纯规则。
- `PennyPet.Tests.csproj` (`net8.0`)：标准可发现测试；直接引用 Core，不加载 Windows UI。包含 Core 架构护栏和便利贴 v1-v9 golden fixtures。
- `PennyPet.Windows.Core.csproj` (`net48`)：模块化 Windows 产品程序集；排除 `Core/**` 并引用已构建的 `PennyPet.Core`。
- `PennyPet.App.csproj` (`net48`)：模块化正常应用入口，引用 Windows Core。
- `PennyPet.Tools.csproj` (`net48`)：生成美术 release pack、startup cache 和相关报告。共享资源只在这里生成一次。
- `PennyPet.SelfTests.csproj` (`net48`)：Windows 资源、持久化、WinForms/WPF、Hook、探针和预览宿主；纯规则不要在这里重复测试。
- `PennyPet.Windows.csproj` (`net48`)：正式兼容单 EXE 入口。它递归编译产品源码以保持单文件分发，并通过 `PennyPet.ArtResources.targets` 嵌入 Tools 已生成的共享资源。

`PennyPet.sln` 同时构建以上工程。`Directory.Build.props` 隔离各工程的 `obj` 并启用 warnings-as-errors；`ProductVersion.props` 是版本唯一来源；`Directory.Build.targets` 生成 manifest。

## 4. Core 的准入规则

### 可以放入 Core

- 不依赖平台的状态模型、输入/输出值对象和确定性规则。
- 数据格式的解析、序列化、规范化、兼容和安全限制。
- 由调用方注入时间、随机数或外部结果后可纯测试的决策。
- 不执行副作用的风险分类、隐私门禁和错误分类。

判断问题不是“这个类以后可能复用吗”，而是：给定普通数据，它是否能在没有 Windows、文件系统和真实 UI 的测试进程中完成工作。

### 绝对不能放入 Core

- WinForms、WPF、Win32 HWND、Screen、DPI 或 Dispatcher。
- Registry、启动项、产品数据目录、文件替换、Shell 打开。
- `System.Drawing.Bitmap`、GDI 资源和真实图片解码。
- UI Automation、全局 Hook 或具体输入控件探测。
- MessageBox、用户提示、日志文件或重试 Timer。

例如，Core 可以判断一个路径属于“可执行文件风险”，但文件是否存在、何时弹确认框、如何调用 Shell 必须留在 Windows 层；Core 可以决定“隐私检查失败时隐藏按键”，但不能执行 UIA 检查。

## 5. PetForm、StickyNoteWindow 与 Coordinator 到底是什么

### `PetForm`

`PetForm` 是桌宠的 WinForms 窗口和 Windows 生命周期聚合点。它拥有窗口状态、消息循环相关资源和最终 UI 接线，但不应重新吸收可独立表达的业务规则。

`PetStartupCoordinator.cs`、`PetAnimationRuntime.cs`、`PetBubbleCoordinator.cs`、`PetMenuActions.cs`、`PetReminderWindowsCoordinator.cs`，以及 `Features/KeyboardOverlay` / `Features/StickyNotes` 中多个 `Pet*Coordinator.cs`，在代码层面仍可能声明为 `partial class PetForm`。这里的 “Coordinator” 表示按职责组织窗口行为和调用链，不承诺它是可独立注入的 service。

保留 partial 是有意折中：这些行为需要共享窗口句柄、计时器、当前宠物/便利贴集合和 UI 线程生命周期。强行改成大量 interface、callback 或 DI service 会隐藏真实所有权，增加事件解绑和线程切换成本，却不自动增加可测试性。可纯测规则应下沉 Core；必须共享窗口生命周期的协调逻辑可以继续作为窄职责 partial。

### `StickyNoteWindow`

`StickyNoteWindow` 是单张 WPF 便利贴的真实顶层窗口。`StickyNoteWpf.cs` 是窗口壳和生命周期；`StickyEditorCoordinator`、`StickyTodoCoordinator`、`StickyScheduleCoordinator`、`StickyReminderCoordinator`、`StickyAppearanceCoordinator`、`StickyLinkCoordinator`、`StickyNativeWindowBehavior` 等 partial 按交互领域组织同一窗口状态。

不继续强拆它的原因是：编辑器焦点、IME composition、FlowDocument、工具栏选择区、WinForms/WPF interop 和窗口原生消息存在严格时序。把共享状态包装成接口不会消除耦合，只会让时序跨更多对象。除非有明确行为需求和完整回归手段，不要为了缩短文件或追求“纯架构”继续搬动事件顺序。

## 6. 高风险区域：修改前先证明你懂约束

### 中文 IME 与富文本

高风险文件包括 `StickyNoteWpf.cs`、`StickyEditorCoordinator.cs` 和 `StickyNotes.cs`。工具栏取得焦点时保存选择区、composition 期间不重写文本、排队恢复焦点、旧 END 消息不能取消新 composition、WinForms 主循环接入 WPF 键盘消息，都是实际兼容路径。

看似“多余”的 BeginInvoke、代次判断、焦点保存或事件顺序可能是在防止中文重复提交、候选框错位、Enter 后字体回退或直接无法输入。自动探针不能替代真实微软/第三方中文输入法手测。

### Keyboard Privacy / fail-closed

按键显示默认关闭，关闭时不能安装全局 Hook。启用前有隐私确认；按键发生时捕获目标窗口/进程/线程/焦点/UIA 身份，异步检测前后任何身份变化或检查不可用都必须隐藏内容。这里的 fail-closed 是产品安全边界，不是“偶尔漏显示”的可用性 Bug。

`SensitiveInputDetector` 负责 Windows 证据采集，最终隐藏决策在 Core 的 `PetKeyboardPrivacyPolicy`。不要把检查失败改成默认显示，也不要宣称可以识别所有第三方、自绘、跨权限或远程桌面密码控件。

### Dock 组关系

Dock 不是“窗口坐标刚好相邻”。持久化模型同时包含 `DockParentId`、`DockGroupId` 和 `DockGroupOrder`；隐藏、恢复、插入中间、抽出成员、关闭成员、统一置顶和跨组拖动都会影响关系。

纯关系变化优先由 `StickyDockOperations` / `StickyDockGroups` 表达；`PetStickyDockCoordinator` 负责命中、真实窗口移动、DPI/屏幕安全边界和接线。任何 Dock 修改都应搜索关系字段的全部读写点，并手测组结构而不只是视觉位置。

### dirty / retry 与用户数据

设置和便利贴保存都不是“没抛异常就成功”。调用方必须检查 `PersistenceResult`；写入失败保留 dirty/`HasUnsavedChanges` 和错误信息，主窗口的持久化协调每五秒重试。退出前强制刷新，仍失败时允许重试、导出快照或取消退出。

读取失败也不能立刻用默认值覆盖：先尝试 `.bak`，损坏内容保留为 `.unreadable-*` 或 `.corrupt-*`。旧品牌数据导入路径仍是兼容功能。简化失败分支可能直接造成静默数据丢失。

### v1-v9 数据兼容

`StickyNoteCodec` 当前写 v9，但必须读取 v1-v9。`Tests/Fixtures/sticky-v1.txt` 至 `sticky-v9.txt` 是历史格式证据，不是随当前 serializer 更新的快照。新增版本时应新增 fixture 并保持旧文件不变；不要用“重新生成全部 golden files”掩盖兼容破坏。

### 正式单 EXE 与美术资源

模块化工程用于清晰边界和测试；公开发布仍以 `PennyPet.Windows.csproj` / `build.ps1` 生成的兼容单 EXE 为准。正式项目重新编译部分 Core 源码以维持 .NET Framework 单文件分发，这意味着“测试的 Core DLL”和“单 EXE 内编译的 Core”严格说不是同一个二进制。这是已知折中，不是当前 Bug，也不是立即引入 assembly merging/embedding 重构的理由。

`PennyPet.Tools` 生成一份共享 release pack/startup cache，Windows Core 和正式 EXE 经 `PennyPet.ArtResources.targets` 消费。不要把重复生成 target 重新复制回两个项目。

## 7. 改动后的最低验证矩阵

### 修改 Core

最低要求：

```powershell
dotnet test ".\desktop-pet\PennyPet.Tests.csproj" --configuration Release
dotnet build ".\PennyPet.sln" --configuration Release
```

同时为新增规则补纯测试。涉及 codec 时使用固定 fixture 验证旧版本，并确认架构护栏通过。

### 修改 Windows UI / 协调逻辑

最低要求：Release solution build、标准 Core tests、模块化或正式 EXE 的完整 `--self-test`（报告 `ok=true`）。再按范围追加：

- IME/编辑器：`--sticky-input-probe`、`--sticky-pump-probe`，并使用真实中文输入法手测 composition、候选框、Enter、字体/字号和焦点恢复。
- 透明便利贴：`--sticky-transparency-probe`，人工检查多窗口重叠和点击。
- Dock：人工覆盖单张↔组、组↔组、中间插入/抽出、隐藏/恢复、关闭成员、不同尺寸、屏幕边缘、多屏/DPI 和统一置顶。
- Keyboard：默认关闭、Hook opt-in/卸载、目标切换 fail-closed、真实密码框/浏览器/管理员窗口。
- 存储：在数据副本上覆盖写失败、dirty 保留、自动重试、`.bak` 恢复、损坏文件保留和退出失败选择。

### 修改构建、美术或发布

最低要求：

1. `dotnet build .\PennyPet.sln --configuration Release`
2. `desktop-pet\build.ps1` 生成正式单 EXE。
3. 对正式 EXE 做版本检查并运行完整 `--self-test`。
4. 美术改动额外运行 startup probe，并检查真实 `PetArtPackage` 的帧、时长、透明轮廓和首次 Notification 加载。
5. CI 改动确认 artifact 同时包含 EXE、SPDX JSON 和 `SHA256SUMS.txt`；checksum 应覆盖实际上传文件。

不要把“能编译模块化 App”当作正式发布验证，也不要把自动 SelfTest 当作 IME、Dock、多屏或隐私人工回归的替代品。

## 8. 新增 Feature 应该放在哪里

先按副作用和所有权分类：

- 纯状态、codec、时间/风险/隐私规则：放 Core，并由调用者注入外部输入。
- 具体 Windows 控件、Dispatcher、窗口、Hook、Registry、Shell、文件路径：放对应 Windows Feature/Infrastructure。
- 新的独立产品能力：建立以该能力命名的 Feature 边界；model、规则和外部适配按真实需要创建，不要先造空层。
- 只有最终展示或桌宠生命周期接线才进入 `PetForm` partial；HTTP、JSON、缓存、每日展示规则或可纯测状态不能塞回 `PetForm`。

当前没有在线 Feature。第一个真实 API Feature 应在后端契约、隐私需求和失败体验明确后，再建立最小共享 HTTP 边界。不要预先加入 DI container、Polly、通用 repository/interface 或没有调用者的 service。网络失败必须与桌宠启动、提醒和本地数据隔离；不得上传便利贴、Todo、Schedule、Reminder 正文或键盘内容。

## 9. 当前有意保留、但不属于 Bug 的折中

- WinForms 主宿主 + WPF 便利贴，而不是统一 UI 框架。
- Coordinator 可能仍是窗口 partial，而不是可注入 service。
- `PetForm` / `StickyNoteWindow` 仍共享一部分窗口状态，不继续按行数强拆。
- 正式单 EXE 会重新编译源码，而模块化 Core DLL 用于边界和测试；暂不引入程序集合并。
- `Program.cs` 和兼容命令面继续存在，供正式单 EXE、CI 和回退使用。
- `build-protected.ps1` 只用于本地研究；公开流水线生成可审查的未混淆版本。
- Keyboard 敏感输入检测只能尽力而为，因此产品保证是 fail-closed 和清晰文案，不是“100% 检出密码框”。

## 10. 真正未完成的未来事项

这些是产品/发布方向，不是要求立即重构现有代码：

- **代码签名**：CI 已有 SBOM 和 SHA-256，但尚无 Authenticode 证书、secret 管理和签名发布链。
- **Git LFS / 大资源治理**：`art/` 中 GIF 很大；迁移 LFS 需要评估已有历史、发布和贡献者工作流，不能只改 `.gitattributes` 后宣称完成。
- **第一个真实 API Feature**：当前没有 endpoint 或远程内容。应由真实需求建立最小 Client/Feature/cache，而不是预建空框架。
- **macOS 宿主**：可复用的是 Core 行为和数据语义；透明窗口、AppKit UI、Event Tap/Secure Input、数据目录、Dock、多屏和 Retina 都需要独立原生宿主与测试。当前 Windows C# UI 不能直接移植。
- **人工回归持续化**：中文 IME、复杂 Dock、多屏/DPI、跨权限 Keyboard Privacy、SmartScreen/签名体验仍需要真实环境验证。

## 11. 第一天 Checklist

- [ ] 安装 .NET 8 SDK 和 .NET Framework 4.8 Developer Pack。
- [ ] 按本文第 2 节顺序阅读文档，特别阅读隐私与安全边界。
- [ ] 运行 `dotnet test .\desktop-pet\PennyPet.Tests.csproj --configuration Release`。
- [ ] 运行 `dotnet build .\PennyPet.sln --configuration Release`，要求 0 警告、0 错误。
- [ ] 用 `desktop-pet\build.ps1` 生成一次正式单 EXE，理解它与模块化 App 的区别。
- [ ] 运行一次完整 `--self-test`，确认最外层 `ok=true`；记住三个按产品设计为 false 的字段不是失败。
- [ ] 从 `Program` / `PennyApplicationHost` 跟一次正常启动调用链。
- [ ] 从 `PetForm` 跟一次菜单或提醒到对应 Coordinator/Core 规则的调用链。
- [ ] 从 `StickyNoteWindow` 跟一次文本编辑和保存调用链，阅读 IME 高风险注释。
- [ ] 阅读 Dock 三个持久化字段及 `StickyDockOperations`、`PetStickyDockCoordinator` 的职责分界。
- [ ] 阅读一次保存失败后的 dirty → timer retry → 退出处理路径。
- [ ] 查看 v1-v9 fixtures，理解 golden file 不能随当前输出重写。
- [ ] 在第一个改动前写下它属于 Core、Windows UI、Infrastructure、Feature 还是构建发布，并选定对应验证项。
- [ ] 不以“代码更短”“类更多”或“全部 DI 化”作为第一周的成功标准；先保持用户行为、旧数据和发布链不变。

第一天的目标不是立即改善架构，而是能准确回答三个问题：这段状态由谁拥有、它为什么不能放到另一层、改动后用什么证据证明旧行为没有被破坏。
