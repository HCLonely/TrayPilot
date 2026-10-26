# 开发与技术说明

[返回使用说明](../README_CN.md)

## 从源码构建与诊断

### 本地构建

在 Windows 上安装 Git、Visual Studio 2022 C++ x64 构建工具和 Windows SDK，以及 .NET 10 SDK 或支持 `net10.0-windows` 的更新 SDK。双击 `scripts` 目录中的 `build.bat` 即可构建；窗口会保留构建结果，按任意键关闭。首次构建需要联网还原 NuGet 包。

也可以在 PowerShell 中执行：

```powershell
.\scripts\build.ps1

# 精简版（需要 .NET 10 Desktop Runtime x64）
.\scripts\build.ps1 -Package lite -OutputDirectory output\lite

# 自定义构建配置和输出目录
.\scripts\build.ps1 -Configuration Debug -OutputDirectory output\debug
```

默认使用 Release 配置构建 Windows x64 完整版，输出到 `app/TrayPilot.exe`，自动构建并嵌入 C++ 原生模块。相对输出路径以仓库根目录为基准；脚本失败时返回非零退出码。若 PowerShell 执行策略阻止运行，可使用 `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1`。

发布输出为单个 `app/TrayPilot.exe`，包含内置语言包与原生模块。完整版包含 .NET（原生运行时组件在启动时自动释放）；精简版需要 .NET 10 Desktop Runtime x64。源码位于 `source/`，使用 C#、WebView2 与 Win32 API；Windows Forms 仅承担窗口和托盘宿主。所有页面资源内嵌，不访问网络，需要 WebView2 Evergreen Runtime。

### 识别诊断

```powershell
.\app\TrayPilot.exe --system-icons-probe .\probe.txt
.\app\TrayPilot.exe --system-icons-probe-legacy .\probe-legacy.txt
.\app\TrayPilot.exe --symbol-cache-test .\symbol-cache-test.txt
.\app\TrayPilot.exe --explorer-images-test .\image-test.txt
.\app\TrayPilot.exe --explorer-images-probe .\image-probe.json
.\app\TrayPilot.exe --application-images-test .\application-image-test.txt
.\app\TrayPilot.exe --application-images-probe .\application-image-probe.json
.\app\TrayPilot.exe --application-model-test .\application-model-test.txt
.\app\TrayPilot.exe --application-model-probe .\application-model-probe.json
.\app\TrayPilot.exe --application-model-live-test .\application-model-live.json
```

前两项分别测试新版方案和旧方案，报告包含识别结果与方案名称；失败详情写入对应的 `.error.txt` 文件。第三项检查本机符号缓存及损坏、错版缓存的拒绝行为。

`--explorer-images-test` 检查共享内存图像的尺寸、序列号、时间戳、透明度及预乘 BGRA 颜色。`--explorer-images-probe` 只读连接 Explorer，输出各控件是否取得内部图像、尺寸及 HRESULT，并在报告目录保存成功取得的 PNG；无有效图像时返回非零退出码。两项均不截取屏幕。`--web-system-bridge-test` 同时验证内部图像优先、失败回退及重试恢复。

`--application-images-test` 使用合成回调验证普通软件图标的 HICON 复制、颜色解码、UID/GUID 分离、进程身份匹配、图像更新、无效图像失效和资源清理；不注册或修改用户图标。`--application-images-probe` 探测普通软件的 `ITrayNotify` 图像通知，并用独立原生 C++ 回调做对照。该命令只创建、更新、隐藏和删除自己的测试图标；对用户图标只读，不截图、不改变偏好。JSON 报告区分 `connected`（注册成功）与 `available`（实际得到匹配图像），没有匹配图像时返回 1，不能把接口成功当作图像功能可用。

### 普通软件的内部图像读取

`ApplicationIconSource` 由 `ScanSession` 持有，后台准备与当前 DLL 精确匹配的 taskbar.dll、SystemTray.dll 符号，再建立只读取图的 Explorer 会话，不阻塞首次列表显示。失败后退避 60 秒，Explorer 身份变化后重新连接，退出时清理当前和仍在建立的连接。Taskbar.pdb 下载正文超时为 45 秒，较大的 SystemTray.pdb 为 120 秒；有效缓存不重新下载。所有 DbgHelp 操作共用原生互斥锁。

`native/ApplicationModelImages.h` 在主任务栏 XAML 线程遍历 `NotifyIconView`，通过精确匹配的接口 vtable 与 getter 读取 `IconViewModel.ContentVM`。当前支持的 ImageIconContentViewModel 构造/访问指令经过字节验证后，取得其持有的通知图标数据模型，再通过 `UDKObject` 读取 `WindowsUdk.UI.Shell.NotificationAreaIcon`。身份字段同样由已验证的 Identity/访问器指令布局读取。未知版本或布局直接回退，不扫描猜测字段，不调用未验证接口槽位。

图像来自图标对象的 `SoftwareBitmap`：只读锁定 BGRA 缓冲，检查平面布局、长度和透明度后复制像素，立即关闭引用与锁，不修改或关闭 Explorer 持有的原始图片。必要的像素格式转换生成独立副本。这个流程既不截取屏幕，也不渲染控件。当前还保留有界图像流读取作为资源类型的兼容路径。

独立伴随映射使用协议 3：头部 200 字节，128 个 66088 字节记录，总大小 8459464 字节。每条最多 65536 字节，原始图像最大 128×128，保存 HWND、UID、GUID、格式尺寸与诊断类型。序列号保证一致读取；超过 3 秒的数据、错误和非法图像回退。GUID 优先匹配，UID 使用实际 HWND 和 ID，并检查 PID、进程启动时间和路径；身份冲突时不任选图片。

`TrayEntry.LiveIconSnapshot` 不写入配置或恢复记录。`TrayImages` 按实时图像 → 原有 IconSnapshot → EXE → 通用图标加载；Web 图像缓存签名包含图像来源与内容，因此取图失败、图像变化和重试恢复都会更新主列表及托盘快捷面板。只读取图的原生会话不执行系统控件隐藏/恢复，也不接管保存的系统控件原始属性。

`--application-model-test` 检查像素、透明度、身份匹配、共享内存和实际 Web 图片输出的回退/恢复。`--application-model-probe` 导出成功匹配的图片及逐条诊断；`--application-model-live-test` 验证真实扫描集成。这两项不改变任何图标的可见性或偏好。本机 Windows 11 25H2 实测微信、QQ、PowerToys PowerDisplay、EarTrumpet 共 4 项成功；其余当时未取得对象的条目回退。当前不主动创建折叠区视图或显示隐藏图标，未实例化的折叠区及隐藏图标可能持续回退；其他 Windows 布局仍需验证。

早期 COM 路径仅保留作诊断：`TrayNotifyReader` 的 ITrayNotify 创建和注册在本机成功，但托管和独立原生回调均为 0，因此生产路径不使用它。注销参数按 ABI 使用 32 位 cookie 的指针。

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

程序快捷键保存在 `ProgramHotkeys`（规范化程序路径 → Windows Forms Keys 整数）中，旧配置缺少该字段时使用空集合。`ProgramHotkeys.cs` 管理独立 Win32 注册、保存失败回滚和串行图标切换；全局设置保存也检查与程序绑定的重复。窗口句柄销毁或退出时注销。`programhotkeys.js` 复用快捷键编辑器并实现规则右键菜单。`--web-rules-live-test` 同时验证程序绑定的持久化、占用回滚、批量切换与规则例外、菜单定位、键盘入口和清除。

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

- 普通软件优先读取 Explorer 内部实际图像；不可用时依次回退到 Windows 缓存、EXE 图标和通用图标。缓存图标及提示可能不是实时内容；名称主要来自 EXE 文件描述，脚本可能显示宿主名称。
- 音量、网络、电池、时钟等 Explorer 内建控件通过**系统图标** 页面直接控制，兼容性及生效方式见上文。
- 无托盘记录、路径匹配失败、受保护进程或特殊实现可能无法识别或控制。

## 系统图标实现

语言栏及附加图标（掩码 256、512）在 Explorer 的 XAML 线程注册 `VisibilityProperty` 变化回调：已选择隐藏的控件被系统显示时同步重新折叠，已选择强制显示的控件被系统隐藏时同步恢复 `Visible`。任务栏根控件的 `LayoutUpdated` 回调执行不读取图像的语言栏扫描，以发现新建或替换的控件。原有 250 毫秒定时扫描仍负责状态发布、失效控件清理、根控件重连与异常恢复。RDP 实测发现系统会从数据集合中移除语言图标，仅保护 XAML 可见性无法阻止此情况。


`native/LanguageSource.h` 使用匹配 `SystemTray.dll` 的 PDB、时间戳和映像大小验证语言/IME 模型接口及 `get_CurrentData`，并验证 `IIconData.ContentMetadata.RequestingDisplay` 的实际 ABI 方法。缓存按模型弱引用保存最近一次请求显示的真实 `IconData`，最多 16 项；仅在对应强制显示位有效且原始数据不请求显示时返回缓存，未知布局和失败 HRESULT 原样返回。本机有效更新会替换缓存，停止或所属进程终止后立即停止保留，最后会话清理时恢复函数表并释放数据。启动扫描通过当前图标的 `IconViewModel.Configuration.DataModel` 初始化缓存，接口和 getter 均由匹配 PDB 验证，不读取猜测的对象字段、不合成图标、不改变输入法配置。现场已确认 RDP 前台时附加图标仍附着于任务栏，用户确认进入 RDP 后保持显示；启动初始化另确认首次扫描读取了两种语言模型。其他系统版本仍需验证。

回调只持有 `Context` 弱引用，读取当前隐藏/强制显示掩码并检查会话和所属进程是否仍有效；图标类型变化时重新注册。恢复属性前先注销属性回调，停止会话（包括 `stop=2` 不恢复退出）时同时注销布局与属性回调；不恢复退出只保留弱控件身份和原始属性值。只读取图的会话不安装这些监听，不修改输入法或 Windows 语言栏配置。

强制显示通过 `GuardLanguageContent` 保护实际内容路径，通过 `GuardLanguageAncestors` 保护到 `NonActivatableStack` 为止的父级；不覆盖任务栏根和系统自动隐藏。共享父级的回调按任一仍启用的强制显示位生效，只允许强制显示，不将隐藏请求传播到父级。已确认身份的同一个 `SystemTrayIcon` 换用未收录但非空的字形时可沿用原身份；内容变空时继续保护原容器，但不计入 `Found`；明确识别成其他类别的内容不会沿用旧身份。

`RecordLanguageState` 每轮扫描比较结构状态，仅在变化时记录最近 8 次事件，每次最多 1800 个字符、24 个语言栏区域节点，包含原容器是否存活/仍附着于当前根、当前识别类别、可见性、子节点数及文字是否非空。只记录前台窗口类名，不记录标题、输入文字或图像。读取 `DebugInfo()` 可在返回 RDP 后检查之前的移除/内容清空事件；诊断失败不影响显示控制。

`ForcedShownSystemIcons` 默认 0，仅允许掩码 256、512，保存时与 `HiddenSystemIcons` 互斥。旧配置缺少字段时保持关闭；读取配置时清除不支持和与隐藏重叠的强制显示位。`Controller.SetSystemIconVisibility` 一次保存两种选择，失败时回退两者。Web 桥接 `systemForceShow` 只接受这两个单独的位和布尔 `enabled`，启用隐藏或强制显示前都验证互斥；详情开关、卡片和隐藏按钮同步禁用冲突操作。撤销同时记录两种选择，恢复全部/显示所有只清除隐藏选择。后台连接释放、启动与重连均检查两种偏好，避免仅启用强制显示时释放会话。

共享内存偏移 8 的 `requested` 用低 12 位保存隐藏掩码，高 16 位保存强制显示掩码（`hidden | forcedShown << 16`），在一个 32 位写入中发布互斥状态；图像区偏移不变，协议 4 在图像区后保存有界诊断与语言源符号。`SystemIconSession.Requested` 和 `ForcedShown` 分别解码，确认原生显示状态后才保存配置；失败时重新发送原有两种选择。关闭强制显示恢复接管前的原始属性，退出和异常恢复遵循已有设置。

本地隔离 XAML 回归用例位于 `tests/native/LanguageVisibility.cpp`，可执行 `powershell -NoProfile -ExecutionPolicy Bypass -File tests/native/build.ps1` 编译运行；仅操作测试进程自己的离屏控件，不连接 Explorer。用例覆盖真实数据保留/更新、关闭强制显示、未知数据布局、失败 HRESULT、原生引用释放，以及同步重新隐藏/强制显示、关闭强制显示后恢复原始隐藏属性、两种语言控件独立设置、类型变化、布局发现替换控件、恢复、进程终止及回调清理。按项目约定，`tests/`（包括源码）已被 Git 忽略，不随默认提交分发。`--web-system-bridge-test` 同时覆盖设置/API 互斥、旧配置默认值、持久化、撤销、保存失败回退、重连与实际 Web 开关状态，并导出 `.force-show.png` 供视觉检查。

图像与显示状态控制相互独立。`native/IconImages.h` 在 Explorer 的 XAML 线程通过 `RenderTargetBitmap.RenderAsync` / `GetPixelsAsync` 渲染已经识别的控件；不读取桌面或窗口屏幕像素，不临时显示隐藏控件。每个控件最多一个异步任务，每秒最多启动一次，只在系统页面启用图像读取时工作。删除透明布局边缘后，保留实际图形、文字及原始颜色。

共享内存协议版本为 4，图像区从偏移 2024 开始，12 个槽位，每槽 49176 字节（24 字节头部及最多 192×64×4 字节预乘 BGRA）。偏移 592136 为语言栏诊断序列号，后接 16384 个 UTF-16 字符，偏移 624912 为 14 个语言源符号值，625024 为源接入 HRESULT，末尾对齐后总大小 625032 字节。图像与诊断各自的独立序列号防止读写混合；托管侧拒绝错误 HRESULT、空图、无效尺寸、全透明、未来时间戳及超过 3 秒的图像。协程持有原会话直到结束，停止后不再发布。图像错误不会写入显示控制的会话错误字段。

`ExplorerIconImage.cs` 验证并转换像素；`WebSystemIcons.cs` 根据像素哈希复用 PNG，失败或数据失效时回退到 `SystemIconImages.Create`，成功重试后自动恢复内部图像。原生图像保留颜色和透明背景，列表及详情均不添加灰色底框；仅回退图像继续按页面主题着色。已折叠或缺失的控件、“显示桌面”等没有可见图形的控件可能始终回退。普通软件使用上文独立的实际图像读取与回退链。

本次只读实测在 Windows 11 25H2 上取得网络、时钟及语言栏附加图标；音量和语言栏当时已隐藏，其他部分控件未出现，不能据此声称全部类别均已验证。

系统图标窗口提供两种 **识别方案**，选择后立即重新连接并保存，下次启动继续使用：

- **新版兼容方案（推荐）**：默认启用。读取当前 `taskbar.dll` 的符号标识，通过 HTTPS 直接获取微软对应的 PDB，校验 GUID 与 DBI age 后缓存，再进行原有的控件识别。无需额外安装 `symsrv.dll`，解决旧方案无法下载符号时的“找不到元素”（`0x80070490`）问题；不会修改普通软件托盘图标的扫描方式。
- **旧方案**：保留原有 DbgHelp 符号服务器解析路径，可随时切回。此路径仍受本机符号下载组件和网络环境影响。

新版方案首次连接或 Windows 更新后可能需要联网下载符号（下载超时为 45 秒）；有效缓存可离线复用，损坏或版本不匹配的缓存会重新下载。微软未发布对应符号、网络不可用或系统内部布局改变时会显示连接失败，不会猜测内存偏移。切换方案时恢复旧连接控制的图标，成功连接后重新应用已保存的隐藏选择。

原生模块已内嵌到 EXE，使用时自动释放到 `%LOCALAPPDATA%/TrayPilot/native/<SHA256>/`，无需随 EXE 附带 DLL。这是单文件分发，并非纯托管实现：XAML 诊断接口需要把进程内 COM DLL 加载到 Explorer。源码编译仍需 C++ 工具链。当前实现针对 Windows 11 x64 主任务栏；副屏时钟和 ARM64 尚未验证。新增方案已在 **Windows 11 25H2，26200.9457** 实测音量、网络、电池、时钟、语言栏附加图标及“显示桌面”的单独隐藏、恢复、组合隐藏和异常退出恢复；其他当时未出现的控件仍待实机验证。
