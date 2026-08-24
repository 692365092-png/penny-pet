# Penny pet

Penny pet 是一款 Windows 桌面宠物，包含透明角色动画、普通便利贴、待办清单、日程、提醒、侧边页签以及可选的按键显示功能。

![Penny pet](art/loading.png)

## 下载与运行

GitHub Actions 会从公开源码自动生成一个完整的单文件 EXE。运行时不需要另外安装美术包，也不需要把 GIF 文件放在 EXE 旁边。

> 按键显示功能默认关闭。开启后只在本机显示按键名称，不保存或上传键盘内容；密码框和隐私输入会自动隐藏。

普通便利贴会把 `http://`、`https://`、`www.` 开头的网址，以及 Windows 本地文件/文件夹绝对路径显示为蓝色下划线；鼠标移上去会变成小手，单击即可用系统默认程序打开。待办清单和日程不会启用此识别。

## 从源码构建

要求：Windows 10/11、Windows PowerShell 5.1、.NET Framework 4.8。

```powershell
.\desktop-pet\build.ps1 -TargetPlatform anycpu -OutputFile ".\release\Penny pet-1.0.exe"
```

构建过程会把 `art/pet-art.json` 引用的完整分辨率动画和启动缓存嵌入 EXE。生成结果仍然是一个文件。

运行自动测试：

```powershell
.\release\Penny pet-1.0.exe --self-test="$env:TEMP\penny-selftest.json"
```

## 源码结构

- `desktop-pet/`：Windows 桌宠、便利贴、待办、日程、提醒和自动测试。
- `art/`：构建单文件 EXE 所需的角色动画与界面美术。
- `DEVELOPER_GUIDE.md`：维护说明、数据位置和高风险兼容逻辑。
- `.github/workflows/build.yml`：GitHub 自动构建与测试。

用户数据只保存在 `%LocalAppData%\PennyPet`，不会提交到仓库。

## 隐私

Penny pet 不包含广告、账户系统或遥测，不会上传便利贴、提醒或键盘内容。详情见 [PRIVACY.md](PRIVACY.md)。

## 开源许可

程序源码以 GNU GPL v3.0 或更高版本发布。再发布修改版时也必须公开相应源码并保留版权说明。Penny pet 的名称、角色形象与作者署名不因开源而自动成为他人的商标。

美术与程序由 NiNii 和 Codex 共同完成。
