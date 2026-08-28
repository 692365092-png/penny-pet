# Penny pet

Penny pet 是一款 Windows 桌面宠物，包含透明角色动画、普通便利贴、待办清单、日程、提醒、侧边页签以及可选的按键显示功能。

![Penny pet](art/loading.png)

## 下载与运行

GitHub Actions 会从公开源码自动生成一个完整的单文件 EXE。运行时不需要另外安装美术包，也不需要把 GIF 文件放在 EXE 旁边。

> 按键显示功能默认关闭。开启后只在本机显示按键名称，不保存或上传键盘内容；程序会尽力识别并隐藏密码框和敏感输入，但无法保证识别所有第三方或自绘输入框，处理高敏感信息时建议关闭此功能。

普通便利贴会把 `http://`、`https://`、`www.` 开头的网址，以及 Windows 本地文件/文件夹绝对路径显示为蓝色下划线；鼠标移上去会变成小手，单击即可用系统默认程序打开。待办清单和日程不会启用此识别。

右键菜单中的“将已展开的便利贴集中到此屏幕”会按当前显示器的 DPI 缩放，把普通便利贴、待办、日程和磁吸组合集中排列到小 Penny 所在屏幕。到点提醒气泡会保持显示，点击气泡才会关闭；后续功能提示或新提醒可以覆盖它。

## 从源码构建

要求：Windows 10/11、Windows PowerShell 5.1、.NET Framework 4.8。

使用 Visual Studio 或标准 .NET 工具进行源码编译检查：

```powershell
dotnet build ".\PennyPet.sln" --configuration Release
```

解决方案会构建跨平台 `PennyPet.Core`、Windows Core、App、Tools、标准 Tests 和 SelfTests；模块化 App 会通过 Tools 生成并嵌入美术资源。生成用于公开分发的兼容单文件 EXE 仍使用：

```powershell
.\desktop-pet\build.ps1 -TargetPlatform anycpu -OutputFile ".\release\Penny pet-release.exe"
```

构建过程会把 `art/pet-art.json` 引用的完整分辨率动画和启动缓存嵌入 EXE。生成结果仍然是一个文件。

运行自动测试：

```powershell
dotnet test ".\desktop-pet\PennyPet.Tests.csproj" --configuration Release
.\release\Penny pet-release.exe --self-test="$env:TEMP\penny-selftest.json"
```

## 源码结构

- `desktop-pet/`：Windows 桌宠、便利贴、待办、日程、提醒和自动测试。
- `PennyPet.sln`：跨平台 Core、Windows Core、App、Tools、Tests、SelfTests 和兼容单 EXE 项目；正式单文件发布由 `build.ps1` 生成。
- `art/`：构建单文件 EXE 所需的角色动画与界面美术；其许可边界见 `ASSET_LICENSE.md`。
- `ARCHITECTURE.md`：当前跨平台技术边界与 macOS 迁移地图。
- `DEVELOPER_GUIDE.md`：维护说明、数据位置、高风险兼容逻辑和平台边界。
- `.github/workflows/build.yml`：GitHub 自动构建与测试。

用户数据只保存在 `%LocalAppData%\PennyPet`，不会提交到仓库。

## 隐私

当前 Penny pet 不包含广告、账户系统、遥测或在线内容服务，不会上传便利贴、提醒或键盘内容。未来在线功能的开发也必须遵守最少数据原则。详情见 [PRIVACY.md](PRIVACY.md)。

## 许可与美术资源版权

除另有说明外，Penny pet 的软件源代码依据 **GNU General Public License v3.0 or later（GPL-3.0-or-later）** 发布。

**Penny pet 中由画师 泥泥NINII 创作的角色与视觉美术资源不属于 GPL-3.0-or-later 的授权范围。** 软件代码采用 GPL 并不意味着这些美术资源已经获得 GPL 授权。未经权利人另行明确授权，不得使用这些美术资源，也不得将其用于任何商业用途。

详细说明见 [`ASSET_LICENSE.md`](ASSET_LICENSE.md)。软件代码许可证见 [`LICENSE`](LICENSE)。
