# Penny pet 1.0 开发交接说明

这份文档写给第一次接手 Penny pet 源码的开发者。目标不是解释每一行代码，而是让人先找到正确的模块，并知道哪些看似多余的兼容逻辑不能随手删除。

## 1. 项目是什么

Penny pet 是一个 Windows 桌面宠物。桌宠主体使用 WinForms 透明分层窗口；便利贴、待办和日程使用 WPF 顶层窗口；提醒、键盘显示、侧边页签和设置保存与两套 UI 共存。

当前产品版本固定为 `1.0`。程序集版本在 `desktop-pet/Program.cs` 顶部声明为 `1.0.0.0`。

项目没有 `.csproj`。`desktop-pet/build.ps1` 会收集 `desktop-pet` 根目录中的全部 `*.cs`，调用系统自带的 .NET Framework C# 编译器，并把美术发布包、首屏缓存、图标和联系作者图片嵌入单个 EXE。

## 2. 如何构建测试版

在项目根目录打开 PowerShell：

```powershell
& '.\desktop-pet\build.ps1' -OutputFile '.\Penny pet-1.0-test.exe'
```

美术源位于 `art`，入口是 `art/pet-art.json`。构建时会校验清单引用的文件，生成无损发布资源包和启动缓存。不要为了加快编译而跳过这两个步骤，否则测试版与实际发布版的动画加载路径会不同。

## 3. 程序入口与运行主线

- `desktop-pet/Program.cs`
  - `Program.Main`：命令行测试入口、单实例、启动 loading、异常兜底。
  - `PetForm`：桌宠主窗口、动画状态机、菜单、提醒触发、便利贴窗口协调、键盘活动响应。
  - `ArtPreloadReservations`：动画懒加载请求和失败后的有限重试。
- `desktop-pet/StartupLoadingForm.cs`：启动 loading 窗口；收到 `PetForm.StartupReady` 后关闭。
- `desktop-pet/LayeredSpriteRenderer.cs`：Windows 分层透明窗口绘制。

`Program.cs` 很大，但它连接了多个模块。不要一次性重写。以后若拆分，应一次只搬一个职责，每搬一次就编译并跑完整 SelfTest。

## 4. 代码模块地图

### 桌宠与美术

- `PetArt.cs`：美术清单、动画帧、发布资源包、启动缓存、行级懒加载和画布对齐。
- `LayeredSpriteRenderer.cs`：把 ARGB 位图送到透明桌宠窗口。
- `StartupLoadingForm.cs`：首屏 loading 美术和尺寸定位。
- `ScaleDialog.cs`：桌宠缩放设置。

动画状态的真正来源是 `art/pet-art.json` 和 `PetArtPackage`。新增或修改动画测试时，优先读取实际 `PetArtPackage` 数据，不要再维护一份独立的帧数和时长真相。

### 便利贴、待办、日程与吸附

- `StickyNotes.cs`
  - `StickyNoteData`、待办和日程数据模型。
  - `StickyDockGroups`：组顺序、父子关系、规范化。
  - `StickyNoteRepository`：读取、旧版迁移、备份、原子保存。
  - 少量仍被测试/管理界面使用的 WinForms 辅助控件。
- `StickyNoteWpf.cs`
  - `StickyNoteForm`：便利贴、富文本编辑、待办、日程、提醒条和组合拖拽的主要 WPF 窗口。
  - 这是高风险文件；字体、焦点、IME、窗口消息和 Dock 几条调用链在这里交汇。
- `StickyNoteTabs.cs`：侧边页签、隐藏/恢复、类型图标。
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
- `PetSettings.cs`：位置、缩放、键盘显示和提醒设置持久化，包括损坏文件保留与 `.bak` 恢复。
- `ReminderModels.cs`
  - `ReminderSchedule` / `ReminderItem`：提醒列表和到点判断。
  - `ShortItemText`：提醒与待办共用的短文本计量和规范化。
- `SpeechBubbleForm.cs`：桌宠气泡的绘制、定位和自动关闭。
- `StartupRegistration.cs`：当前用户开机启动注册表项。

提醒触发主线是：`ReminderSchedule` 到点 → `PetForm` 确保 Notification 动画已按需准备 → 播放提醒动画和气泡 → 同步对应便利贴提醒条。Notification 仍然是懒加载，不能改成启动时无条件加载全部动画。

### 键盘显示与隐私保护

- `GlobalKeyboardActivity.cs`：全局低级键盘钩子，只发布非本进程的按键活动。
- `KeyboardOverlay.cs`：按键文字格式化、连按常显、敏感输入检测、屏幕覆盖层。
- `PennySelfTests.cs`：自动测试和测试用渲染/探针；不再与正式键盘钩子放在同一文件。

按键显示对新用户默认关闭。关闭时不能安装全局键盘钩子；只有用户从菜单主动开启后才启动，重新关闭时立即卸载。不要把“关闭”简化成只隐藏文字覆盖层。

敏感输入检测是异步的，并用事件代次把检测结果与对应按键绑定。旧检测结果不能覆盖新按键内容。修改此处时要保留代次判断，否则密码框状态可能和显示文字错配。

### 其他

- `ApplicationDiagnostics.cs`：异常日志和带备份的原子文本写入。
- `ContactAuthorForm.cs`：联系作者窗口及小红书跳转。
- `ReverseEngineeringEasterEgg.cs`：不参与产品逻辑的彩蛋常量。

## 5. 为什么桌宠用 WinForms、便利贴用 WPF

桌宠依赖 WinForms/Win32 分层窗口来稳定显示逐帧透明美术。便利贴需要“背景半透明但文字和控件不透明”，WPF 可以在单个顶层窗口的一棵视觉树里完成，避免 TransparencyKey 空洞和跨窗口转发鼠标消息。

主消息循环由 WinForms 持有，所以 WPF 便利贴必须启用受支持的 modeless keyboard interop。`Program.EnsureWpfApplicationForStickyNotes` 与 `StickyNoteForm.EnableWinFormsKeyboardInterop` 一带看似绕，但负责让 WPF 收到正常的键盘、字符和输入法消息。

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

## 8. SelfTest 与探针

完整自检：

```powershell
$report = Join-Path $env:TEMP 'penny-selftest.json'
$process = Start-Process -FilePath '.\Penny pet-1.0-test.exe' `
    -ArgumentList ('--self-test=' + $report) -PassThru -Wait
Get-Content -LiteralPath $report -Raw
```

报告最外层 `ok` 必须为 `true`。以下三个布尔值按产品设计为 `false`，不是测试失败：

- `typing_moves_pet`
- `look_follow_registered`
- `keyboard_content_recorded`

常用专项入口：

- `--sticky-input-probe=<json>`：富文本/IME 输入探针。
- `--sticky-pump-probe=<json>`：WinForms 主循环下的 WPF 键盘桥探针。
- `--sticky-transparency-probe=<json>`：半透明窗口叠加与交互探针。
- `--startup-probe=<json>`：首屏缓存和启动准备探针。
- `--render-*-preview=<png>`：各 UI 视觉预览入口，具体名称见 `Program.Main`。

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
- 不全面重写 `Program.cs`、Dock、IME 或富文本编辑器。
- 不为缩短代码而合并不同职责。
- 原 `ReminderUi.cs` 已只保留提醒对话框及其日期控件；设置、模型、气泡与开机启动均已按职责纯搬家拆出。
- `StickyNoteWpf.cs` 仍然较大。它可以在未来按富文本、Todo、Schedule、Dock 窗口协调逐步拆分，但不能一次性重构。
- 建议先用本文件的“模块地图”定位，再沿事件订阅和调用者完整阅读调用链。

最重要的判断标准不是“代码是否更短”，而是：用户功能和旧数据保持不变，同时下一位开发者能明确知道该去哪里修改、哪里不能冒进。
