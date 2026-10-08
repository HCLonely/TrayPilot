# 开发与技术说明

[返回使用说明](../README_CN.md)

## 从源码构建与诊断

### 本地构建

在 Windows 上安装 Visual Studio 2022 C++ x64 构建工具和 Windows SDK，以及 .NET 10 SDK 或支持 `net10.0-windows` 的更新 SDK，在仓库根目录执行：

```powershell
dotnet publish .\source\TrayPilot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\app
```

发布输出为单个 `app/TrayPilot.exe`，包含内置语言包与原生模块。完整版包含 .NET（原生运行时组件在启动时自动释放）；精简版需要 .NET 10 Desktop Runtime x64。源码位于 `source/`，使用 C#、WebView2 与 Win32 API；Windows Forms 仅承担窗口和托盘宿主。所有页面资源内嵌，不访问网络，需要 WebView2 Evergreen Runtime。

### 识别诊断

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
```

前两项分别测试新版方案和旧方案，报告包含识别结果与方案名称；失败详情写入对应的 `.error.txt` 文件。第三项检查本机符号缓存及损坏、错版缓存的拒绝行为。

### 刷新与启动

Web 页面增量更新现有行，保留图片、选择、焦点和滚动；相同数据零 DOM 更新。主窗口自动发现每 2.5 秒执行，后台规则与恢复记录复用已有扫描会话。启动时在内存中合并内嵌 HTML、CSS 和脚本，提前准备并复用快捷面板的 WebView2。加载失败提供错误与重试。

```powershell
.\app\TrayPilot.exe --web-startup-test .\startup.json
.\app\TrayPilot.exe --web-startup-live-test .\startup-live.json
.\app\TrayPilot.exe --web-feedback-preview .\interface.png
.\app\TrayPilot.exe --web-feedback-bridge-test .\feedback.json
.\app\TrayPilot.exe --web-preferences-bridge-test .\preferences.json
.\app\TrayPilot.exe --web-system-bridge-test .\system.json
.\app\TrayPilot.exe --web-rules-live-test .\rules.json
.\app\TrayPilot.exe --self-test .\self-test.txt
.\app\TrayPilot.exe --resilience-test .\resilience.txt
```

启动耗时来自本机诊断会话；比较时应使用相同条件并重复测量。实际启动诊断使用隔离设置进行正常只读扫描。隐藏、恢复、结束任务桥接仅操作测试进程，启动项使用专属测试注册项；系统桥接使用模拟状态。

## 添加语言包

内置语言包已嵌入 EXE，源文件位于 `source/languages/`。可选的 EXE 同级 `languages/` 目录支持添加或覆盖语言包，使用 UTF-8 JSON：

```json
{
  "Name": "English",
  "Strings": {
    "mainWindowTitle": "TrayPilot · Tray Icon Manager",
    "settings": "Settings"
  }
}
```

键使用简短、稳定的语义化英文标识，采用 `camelCase` 命名（如 `mainWindowTitle`、`trayIconDetails`），不包含显示文案中的标点、换行或格式占位符。复制现有完整语言包，只翻译 `Strings` 的值，保留键和 `{0}`、`{1}` 等格式占位符。文件名（不含扩展名）作为语言标识，`Name` 为设置中的显示名称。重新打开设置即可发现新增文件。缺失翻译回退为内置英文；缺失或无效的语言包不会阻止正常图标管理。

## 配置与异常恢复

配置、隐藏规则和恢复记录保存在：

```text
%LOCALAPPDATA%\TrayPilot\settings.json
```

隐藏前先保存恢复记录，启动时尝试恢复上次记录，再按当前规则运行。`RestoreIconsOnExit` 默认为 `false`，旧设置中缺少此字段时也默认关闭：退出不恢复图标，保留恢复记录。勾选“退出后恢复图标”后，普通退出先恢复；若恢复失败则保留记录并取消退出，便于重试。关机/注销查询不触发恢复或关闭；系统确认结束会话时遵循相同设置，但不弹窗阻止退出。

系统图标会话的正常清理仍恢复原状态。仅在明确选择不恢复的退出流程中，原生停止命令使用 `stop=2`，停止计时器并保留图标当前状态；弱引用及原始属性值留在 Explorer 内，供后续会话恢复，无后台巡检或旧会话句柄。意外崩溃仍走原有恢复路径。Windows 重建任务栏或图标所属软件更新状态时，退出后保留的显示状态可能改变。

若程序被强制结束，可重新打开后点击 **全部恢复**，或重新启动目标软件，让它重新创建图标。仍有图标隐藏时不要删除恢复记录。

程序不修改其他软件配置，也不写入 Windows 托盘的 `NotifyIconSettings` 设置。开机启动使用当前用户的计划任务，切换时仅清理本程序遗留的 Run 启动项与 StartupApproved 状态。

配置格式包含版本号，兼容缺少版本号的旧配置；不支持的未来版本不会被自动降级覆盖。保存时先写临时文件并刷入磁盘，再替换主文件，将上一版保存为 `settings.json.bak`。配置损坏或规则、恢复记录结构无效时，尝试读取有效备份，并保留原文件为 `settings.json.corrupt-<唯一标识>`。备份可能缺少最近一次更改，恢复后会提示；主文件和备份均无效时仍停止启动，不会丢弃恢复记录并创建空配置。

单实例激活在读取配置之前执行。启动恢复在窗口显示后通过 UI 事件循环异步串行执行，失败时保留记录并允许在窗口中重试。刷新复用本轮扫描的状态，只复查自动隐藏涉及的图标，实际修改前仍检查进程身份。程序名称缓存最多 256 项，5 分钟后重新读取，支持同路径软件更新。

## 兼容范围与实现

本项目是面向 **Windows 11 25H2** 的原型。其他系统版本、未来 Windows 补丁及特殊软件的兼容性需要实际验证。

- 优先使用 Windows 缓存图标，依次回退到 EXE 图标、通用图标。缓存图标及提示可能不是实时内容；名称主要来自 EXE 文件描述，脚本可能显示宿主名称。
- 音量、网络、电池、时钟等 Explorer 内建控件通过**系统图标** 页面直接控制，兼容性及生效方式见上文。
- 无托盘记录、路径匹配失败、受保护进程或特殊实现可能无法识别或控制。

## 系统图标实现

系统图标窗口提供两种 **识别方案**，选择后立即重新连接并保存，下次启动继续使用：

- **新版兼容方案（推荐）**：默认启用。读取当前 `taskbar.dll` 的符号标识，通过 HTTPS 直接获取微软对应的 PDB，校验 GUID 与 DBI age 后缓存，再进行原有的控件识别。无需额外安装 `symsrv.dll`，解决旧方案无法下载符号时的“找不到元素”（`0x80070490`）问题；不会修改普通软件托盘图标的扫描方式。
- **旧方案**：保留原有 DbgHelp 符号服务器解析路径，可随时切回。此路径仍受本机符号下载组件和网络环境影响。

新版方案首次连接或 Windows 更新后可能需要联网下载符号（下载超时为 45 秒）；有效缓存可离线复用，损坏或版本不匹配的缓存会重新下载。微软未发布对应符号、网络不可用或系统内部布局改变时会显示连接失败，不会猜测内存偏移。切换方案时恢复旧连接控制的图标，成功连接后重新应用已保存的隐藏选择。

原生模块已内嵌到 EXE，使用时自动释放到 `%LOCALAPPDATA%/TrayPilot/native/<SHA256>/`，无需随 EXE 附带 DLL。这是单文件分发，并非纯托管实现：XAML 诊断接口需要把进程内 COM DLL 加载到 Explorer。源码编译仍需 C++ 工具链。当前实现针对 Windows 11 x64 主任务栏；副屏时钟和 ARM64 尚未验证。新增方案已在 **Windows 11 25H2，26200.9457** 实测音量、网络、电池、时钟、语言栏附加图标及“显示桌面”的单独隐藏、恢复、组合隐藏和异常退出恢复；其他当时未出现的控件仍待实机验证。
