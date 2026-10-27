# 19 篇 · Windows 标准安装 / 卸载 / 注册流程（E7）

> 用户指令（m01392）：「使用windows标准软件安装/卸载/注册流程」。
> 本文只写计划，不含实现代码。目标是把"解压即用的 zip + 手工放 ckan.exe"升级成 Windows 用户预期的那套东西：**安装程序 → 控制面板/设置里看得见 → 双击 `ckan://` 与 `.ckan` 能用 → 卸载干净 → 可静默部署**。
> 上游：`11 篇`（技术栈定稿）、`09 篇`（迁移与打包）、`16 篇`（本地化）、`18 篇`（启动后资源释放）。本文的 WBS 与里程碑回填到 `06 篇` §1.16 / M13。
> 版本：v1.0（2026-10-27）

---

## 1. 目标与非目标

| ID | 目标 | 验收方式 |
| --- | --- | --- |
| I-01 | 提供图形化安装程序，**默认零提权**（per-user），双击即装，装完能直接从开始菜单启动 | 干净 Win10 1607 虚拟机上，普通账户双击 `CkanUISetup.exe`，全程无 UAC |
| I-02 | 在「设置 → 应用 → 已安装的应用」（旧称"控制面板 → 程序和功能"）里有条目，**显示名称 / 版本 / 发布者 / 图标 / 大小**正确 | 装机后 `Get-Package`/控制面板可视核对；版本等于 `FileVersionInfo` |
| I-03 | 卸载是标准流程：既能从应用列表卸载，也能静默卸载；卸载后**不留可执行文件与注册项** | 卸载后 `%LocalAppData%\CkanUI` 不存在、HKCU 下协议/关联键不存在 |
| I-04 | `ckan://` 协议与 `.ckan` 文件关联由安装程序注册，**不需要管理员**，且**不改写 HKLM** | 新装机器上 `reg query HKCU\Software\Classes\ckan`；HKLM 无新增 |
| I-05 | 用 **EXE 安装器**支持静默安装与企业分发：`CkanUISetup.exe -s`（隐藏所有对话框、对提示答 yes）、`-t <DIR>`（自定义安装目录）；**不提供 MSI** | 静默装、静默卸各跑一次；退出码 0；`-t "D:\Apps\CkanUI"` 生效 |
| I-06 | 升级是标准流程：新版本安装包覆盖旧版本，用户数据（实例列表、`registry.json`、仓库缓存）**零迁移** | 装 v1 → 装 v2 → 实例与已装 mod 仍可见 |
| I-07 | 保留免安装 zip 包（便携模式），供 CI、测试和多实例玩家使用 | zip 解压即跑；启动时提示"检测到已安装版本" |
| I-08 | 所有对外发布的二进制都被 Authenticode 签名并带时间戳 | `Get-AuthenticodeSignature` 为 `Valid`，`TimeStamperCertificate` 非空 |

非目标：

- **不发布到 Microsoft Store**（打包模型与 CKAN 的"用户目录里可写数据"语义冲突，见 §3.3）。
- **不做 MSIX**（理由见 §3.3，硬约束是 Win10 1709 下限 + 侧载策略 + 文件虚拟化）。
- **不改 Core 一行**（D5 不变）。安装/注册全部发生在 App 层与打包脚本。
- 不做后台服务、不做内核驱动、不注册防火墙规则。
- 不做自动静默升级（升级由用户点，或包管理器发起）。

---

## 2. 现状取证（老 ckan.exe 今天是怎么"装"的）

| 事实 | 证据 |
| --- | --- |
| 没有任何安装程序。发布物是 zip，用户自己解压、自己找地方放、自己建快捷方式 | `09 篇` §2；`build/BuildPaths.cs` + `build/Program.cs` 只做 ILRepack 单文件合并 |
| 产物是**单文件 `ckan.exe`**：Cmdline + Core + ConsoleUI + GUI 被 ILRepack 合到一个 exe | `build/Program.cs`（ILRepack 2.0.44） |
| 协议注册**写 HKLM**，因此需要管理员；失败时降级为"询问用户 → 提权重启自己" | `GUI/URLHandlers.cs:109-142`：`Registry.ClassesRoot.CreateSubKey("ckan")`、`SetValue("", "URL: ckan Protocol")`、`SetValue("URL Protocol", "")`、`shell\open\command` = `{PathToRunningExe()} gui %1` |
| 提权回退路径 | `GUI/URLHandlers.cs:59-82`：捕获 `UnauthorizedAccessException` → `RaiseYesNoDialog(URLHandlersPrompt)` → `Process.Start(new ProcessStartInfo(...){ Verb = "runas", UseShellExecute = true, Arguments = "gui --asroot " + UrlRegistrationArgument })`；随后写 `config.URLHandlerNoNag = true` |
| 注册动作发生在**每次启动** | `GUI/Main/Main.cs:209` `URLHandlers.RegisterURLHandler(configuration, CurrentInstance, currentUser)` |
| 也有独立的注册参数（用于提权重启那条路径） | `GUI/Program.cs:55-60`（`args.Contains(URLHandlers.UrlRegistrationArgument)` → `RegisterURLHandler(null, null, null)`） |
| 结论：**没有**已安装应用条目、**没有**卸载器、**没有** `.ckan` 文件关联、**没有**静默安装、**没有**签名流程 | 全仓库无 `Uninstall`/`AddRemovePrograms`/`App Paths` 相关代码；`grep -i uninstall --include=*.cs` 仅命中文本资源 |
| 自动更新是"替换运行中的 exe"，与新程序语义冲突 | `Core/AutoUpdate/AutoUpdate.cs:51` `CanUpdate`、`:62` `StartUpdateProcess`（`08 篇` R-21） |

→ 也就是说：**这四项"标准"目前一项都不具备**，不是补丁，是从零补。

---

## 3. 三个候选的硬事实

### 3.1 Velopack（Setup.exe 一键装；本项目**只用 EXE，不出 MSI**）

| 事实 | 来源 |
| --- | --- |
| Windows 安装程序是"一键式"（无向导），装到 `%LocalAppData%\{packId}` | docs.velopack.io /packaging/installer |
| `--packTitle` 决定**快捷方式名、Apps & Features 里显示的名字**、便携版 exe 名 | 同上 |
| 默认建开始菜单与桌面快捷方式 | 同上 |
| **Setup.exe 命令行**（Velopack Setup 1.2.161）：`-s/--silent`、`-v/--verbose`、`-l/--log <FILE>`、`-t/--installto <DIR>`、`-- [EXE_ARGS]` | docs.velopack.io/reference/cli/content/setup-windows |
| 默认安装位置 `%LocalAppData%\{packId}`（**HKCU、免提权**）；改目录用 `-t <DIR>`，装到 `Program Files` 时安装进程需提权（走 UAC） | 同上 |
| 不产 MSI → `--instLocation`/`VELOPACK_INSTALLDIR` 这类 MSI 专有开关**一律不用** | 同上 |
| 自带 `Update.exe` 与 delta 更新，卸载由它处理；`current` 目录在更新时整体替换 | 同上 |
| 支持 `--splashImage`、`--icon`、`--instLicense`、`--instWelcome` 等 | 同上 |

### 3.2 纯 WiX MSI（自己写 WiX 工程）—— **不采用**（决策：不出 MSI）

标准、可控（能精确写每一个注册项、EveryPerUser 组件、ARP 属性），代价是要自己维护 Installer 工程 + UI 序列 + 升级逻辑，且**更新机制仍需自建**（回到 R-21 的 20h 坑）。

### 3.3 MSIX（否决）

| 事实 | 后果 |
| --- | --- |
| 「MSIX is currently supported on these versions of Windows: **Windows 10, version 1709, and later**」 | 与本项目 **Win10 1607 下限**（`11 篇` §7）直接冲突 |
| 「Prior to Windows 10 2004, sideloading of Windows apps must be enabled… Home and Professional SKUs had sideloading enabled by default」（Enterprise/Education/LTSC 需先开侧载策略） | 企业/教育/LTSC 用户装不上，或要先改策略 |
| Win10 1709/1803/1809 需 App Installer 才能双击安装；更早版本要用 MSIX Core | 又一个前置依赖 |
| 打包应用写文件走虚拟化/重定向，`<GameDir>\CKAN\registry.json` 这类"用户可见真路径"会被重定向到包容器 | 与"必须与 ckan.exe / CLI 共享同一批文件"（`09 篇` §4）根本冲突 |
| 需要证书信任链；未信任证书的 MSIX 不能装 | 分发门槛更高 |

→ **MSIX 不做**。理由不是"麻烦"，是"文件语义冲突 + 下限不达标"。

### 3.4 定稿

**单产物：`CkanUISetup.exe`（一键、per-user、免提权；`-s` 静默、`-t` 指目录）**，另发便携 zip；**不出 MSI**（2026-10-27 决策）。理由：

1. 一个 EXE 覆盖"双击即装 / 静默装 / 指定目录 / 企业派发"四种场景，不再维护第二条产物线（`08 篇` R-21 的"自建替换器"直接作废，改由 `Update.exe` 承担）；
2. per-user 安装**不需要管理员**——这正好修掉 `GUI/URLHandlers.cs:59-82` 那段"提权重启自己"的丑流程；
3. 它有稳定的**已安装应用条目 + 卸载器**，即 I-02 / I-03；
4. 纯 WiX 只在"必须逐字节控制注册表"时才更优，而这里要写的注册项很少（§6），Velopack + 我们自己的一段注册逻辑足够。

---

## 4. 安装布局与两个必须钉死的标识

### 4.1 目录布局

```
%LocalAppData%\CkanUI\                  ← packId = "CkanUI"（见 §4.2）
├─ Update.exe                           ← Velopack 引导器（勿改名、勿移动）
├─ sq.version
├─ current\                             ← 真正跑的程序，升级时整体替换
│  ├─ CkanUI.App.exe
│  ├─ CkanUI.Sfs.Cli.exe               （13 篇 SFS 命令行）
│  ├─ CkanUI.App.dll / Avalonia / SkiaSharp …
│  ├─ adapters\                         （15 篇：目录即插件）
│  ├─ i18n\                             （16 篇 L2 官方文案快照 + 17 篇元数据译文包）
│  ├─ themes\log-rules.json             （14 篇染色规则）
│  └─ log4net.xml                       （Core 的 Logging 会从 CWD 找它，见 02 篇 §2）
└─ packages\                            ← 更新下载物（卸装时随目录删除）
```

用户数据**不在**安装目录内（这是与老 ckan.exe 共存的前提）：

| 数据 | 位置 | 归属 |
| --- | --- | --- |
| 配置 | `%LocalAppData%\CKAN\config.json`（`Core/Configuration/JsonConfiguration.cs:87`） | 共享，不迁 |
| UI 自有状态 | `%LocalAppData%\CKAN\ckan-ui.json` | 本程序 |
| 注册表（实例的已装 mod） | `<GameDir>\CKAN\registry.json` | 共享，不迁 |
| 仓库缓存 | `%LocalAppData%\CKAN\repos` | 共享，不迁 |
| 密钥（AI key） | `%LocalAppData%\CKAN\ckan-ui.secrets`（DPAPI） | 本程序 |

### 4.2 两个必须钉死的标识

**① `packId` 不能是 `CKAN`。** Velopack 装到 `%LocalAppData%\{packId}`，而 `%LocalAppData%\CKAN` **已经是 CKAN 的数据目录**（`Core/IO/CKANPathUtils.cs:15` AppDataPath + `JsonConfiguration.cs:87`）。若 `packId = CKAN`，安装程序会往数据目录里塞 `Update.exe`/`current`，与 `config.json`、`repos/` 混在一起，卸载时甚至可能误删。→ **`packId = "CkanUI"`**，安装目录 `%LocalAppData%\CkanUI`，数据目录 `%LocalAppData%\CKAN` 保持不动。这是本文最容易踩的坑（R-65）。

**② 版本号只需 SemVer 三段。** 不出 MSI，也就不再受 `ProductVersion` 每段 ≤ 65535 的限制；CKAN 版本来自 `CHANGELOG.md` 的正则提取（`Core/CKAN-core.csproj`，形如 `1.36.5`），预发布后缀（`-alpha`）只影响显示名与 `sq.version` 的更新通道判断（R-66 的触发面因此收窄）。

---

## 5. 安装流程（标准 Windows 序列）

```
用户双击 CkanUISetup.exe（静默：CkanUISetup.exe -s；指定目录：-t "D:\Apps\CkanUI"）
  ├─ ① 前置检查：OS ≥ Win10 1607（低于则明确报错退出，别装完再崩）
  ├─ ② 前置检查：VC++ 2015-2022 x64 运行库（缺失 → 引导安装，见 §7）
  ├─ ③ 检测已有安装（HKCU Uninstall 键 / %LocalAppData%\CkanUI）
  │     ├─ 同版本 → 询问"修复 / 取消"
  │     ├─ 旧版本 → 静默升级（保留用户数据）
  │     └─ 新版本 → 明确拒绝降级（R-67）
  ├─ ④ 解包到 %LocalAppData%\CkanUI\current\
  ├─ ⑤ 注册项写入（全部 HKCU，见 §6）
  ├─ ⑥ 写 ARP 条目（已安装应用可见）
  ├─ ⑦ 首启向导（可选，安装器不弹；首次运行 App 时弹，见 §12）
  └─ ⑧ 完成页：勾"立即启动"
```

关键：**第 ⑤ 步失败不能让安装失败**。注册协议/关联失败时安装继续，App 内"设置 → 集成"页可以手动重试（标准软件的容错姿势）。

---

## 6. 需要写的注册项（全部在 HKCU，一个 HKLM 都不写）

| # | 键 | 值 | 作用 | 卸载时 |
| --- | --- | --- | --- | --- |
| 1 | `HKCU\Software\Classes\ckan` | `(默认)` = `URL: ckan Protocol`；`URL Protocol` = `""`；`FriendlyTypeName` = `CKAN Mod 链接` | 协议根 | 删整棵 |
| 2 | `HKCU\Software\Classes\ckan\shell\open\command` | `(默认)` = `"%LocalAppData%\CkanUI\current\CkanUI.App.exe" --url "%1"` | 双击链接 | 同上 |
| 3 | `HKCU\Software\Classes\ckan\DefaultIcon` | `"…\CkanUI.App.exe",0` | 链接图标 | 同上 |
| 4 | `HKCU\Software\Classes\.ckan` | `(默认)` = `CKAN.ModPackage`；`Content Type` = `application/x-ckan` | 扩展名映射 | 删 |
| 5 | `HKCU\Software\Classes\CKAN.ModPackage` | `FriendlyTypeName`、`DefaultIcon`、`shell\open\command` = `"…\CkanUI.App.exe" --install-file "%1"` | ProgId 本体 | 删整棵 |
| 6 | `HKCU\Software\CKAN\CkanUI\Capabilities` | `ApplicationName/ApplicationDescription/FileAssociations\ .ckan = CKAN.ModPackage / URLAssociations\ ckan = CKAN.UrlHandler` | 让"默认应用"界面认识我们 | 删 |
| 7 | `HKCU\Software\RegisteredApplications` | `CkanUI` = `Software\CKAN\CkanUI\Capabilities` | 注册到默认应用列表 | 删该值 |
| 8 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI` | `DisplayName`/`DisplayVersion`/`Publisher`/`DisplayIcon`/`InstallLocation`/`EstimatedSize`/`NoModify=1`/`NoRepair=1`/`UninstallString`/`QuietUninstallString` | ARP 条目（Velopack 会写大部分，我们补 `Publisher`、`URLInfoAbout`、`EstimatedSize`） | 删整棵 |
| 9 | `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\CkanUI.App.exe` | `(默认)` = 完整路径；`Path` = 所在目录 | Win+R / `start` 能直接找到 | 删整棵 |
| 10 | 开始菜单快捷方式 | `%AppData%\Microsoft\Windows\Start Menu\Programs\CKAN UI.lnk` | Velopack 自带 | 随卸载删 |
| 11 | 桌面快捷方式（可选） | 安装时勾选 | Velopack 默认建 | 随卸载删 |

**与老 ckan.exe 的冲突处理**：老版本往 `HKLM\Software\Classes\ckan` 写过协议（`GUI/URLHandlers.cs:113` 用的是 `Registry.ClassesRoot`，默认写 HKLM，需管理员）。Windows 解析 `ClassesRoot` 时 **HKCU 优先于 HKLM**，所以我们写 HKCU 后，`ckan://` 会指向新程序——但老 ckan.exe 下次启动仍会尝试改写 HKLM（会因无权限而弹 UAC 提示，或成功把 HKLM 改回自己）。

处置（写进首启向导，见 §12）：

- 若发现 `HKLM\Software\Classes\ckan` 存在且指向 `ckan.exe` → 提示"检测到旧版注册信息，是否清掉？"，**需要提权**时才做，用户拒绝就什么都不做（HKCU 仍然生效）；
- 若 `URLHandlerNoNag`（`GUI/URLHandlers.cs:79` 写在 `GUIConfig.json`）为 true，说明用户以前点了"不再提示"，不再打扰。

---

## 7. 依赖：VC++ 运行库（.NET 自包含不等于"零依赖"）

`.NET 10` 自包含（`09 篇` §11）把运行时打进来了，但 **Avalonia 的渲染后端 SkiaSharp 是原生 DLL（`libSkiaSharp.dll`），它链接 MSVC 运行时**（`msvcp140.dll` / `vcruntime140.dll` / `vcruntime140_1.dll`）。干净系统上可能直接 `DllNotFoundException: libSkiaSharp`。

官方给的两种做法与取舍（learn.microsoft.com，Redistribute Visual C++ Files）：

- **推荐：装 Redistributable 包**（`vc_redist.x64.exe /install /quiet /norestart /log <file>`）——"central deployment … makes it possible for Microsoft to service runtime library files independently"，卸载我们的应用也不会影响别人；
- 可行的替代：把 DLL 放进应用本地目录（app-local）——"It's also possible to directly install the Redistributable DLLs in the application local folder… **For servicing reasons, we don't recommend** that you use this installation location."

检测表（避免 vc_redist 因"已装更新版本"而报错退出，这是它的已知行为）：

```
HKLM\SOFTWARE\Wow6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64
    Version (REG_SZ) + Major/Minor/Bld/Rbld (REG_DWORD)
```

逻辑：读 `Version` → 与内置要求版本比较 → **已更新则跳过安装**（否则 vc_redist 返回非 0，安装器会误判失败）。

> ⚠ 这一节里的"必须装 VC++"是**推断**（SkiaSharp 的原生依赖），必须在干净 Win10 1607 镜像上实测（SP-41）。测出来不需要，就整段删除并把 PKG-04 的 10h 退回去。

---

## 8. 卸载流程

```
应用列表点"卸载" → UninstallString（Velopack 的 Update.exe --uninstall）
  ├─ ① 检测正在运行：CkanUI.App.exe 在跑 → 提示先关闭（或温和等待 3 s）
  ├─ ② 检测 KSP 是否在跑；若在跑且有未完成事务（registry.locked 存在）→ 提示
  ├─ ③ 删除程序目录 %LocalAppData%\CkanUI\（含 current、packages）
  ├─ ④ 删除 §6 的第 1–9 项注册值（只删我们自己写的；HKLM 不动）
  ├─ ⑤ 删除快捷方式
  └─ ⑥ 完成页：明确列出"已保留"的位置，给一个"打开该文件夹"按钮
```

**绝不删除**（写进完成页文案，也写进 `09 篇` §10）：

| 保留项 | 位置 | 理由 |
| --- | --- | --- |
| 配置 | `%LocalAppData%\CKAN\config.json` | 与 ckan.exe / CLI 共享 |
| 实例注册表 | `<GameDir>\CKAN\registry.json` | 删了等于"卸载 mod 管理数据"，是最不可接受的行为 |
| 仓库缓存 | `%LocalAppData%\CKAN\repos` | 重装省一次 38 MB 下载 |
| UI 偏好 | `%LocalAppData%\CKAN\ckan-ui.json` | 重装后无缝 |
| 日志 | `%LocalAppData%\CKAN\logs` 或 CWD 下的 `CKAN.log` | 排障需要 |

提供可选勾选「同时删除本程序的缓存与偏好（保留实例与 mod 记录）」，默认**不勾**。

退出码：`0` 成功、`1602` 用户取消、`3010` 需重启（本场景不应出现）、`1603` 致命错误。

---

## 9. 升级、降级与从老 ckan.exe 迁移

| 场景 | 行为 |
| --- | --- |
| 同版本 | 询问"修复/取消" |
| 旧版本 → 新版本 | 覆盖安装，保留用户数据；`current` 整体替换；`ckan-ui.json` 里记一次 `lastUpgradeUtc` |
| 新版本 → 旧版本 | **拒绝**并提示"请先卸载新版本"（防止 schema 回退） |
| 自动更新 | 由 Velopack `Update.exe` 执行（**取代** `08 篇` R-21 的自建替换器与 `12 篇` F-52 中的替换部分）；UI 只负责"检查/提示/频道（稳定 vs dev builds）" |
| 从老 ckan.exe 迁入 | 不搬文件、不改注册表。首次启动扫描 `config.json` 的 `GameInstances` 直接列出实例（零迁移，`09 篇` §4）；可选提示"是否把 `ckan.exe` 移到 `%LocalAppData%\CkanUI\legacy\` 备份"（默认否） |
| 与老 GUI 并存 | 允许并存，但**同一实例不可同时操作**（`registry.locked`，`08 篇` R-04）。启动时检测锁的存在并在状态栏提示 |
| 与 `11 篇` 的 1607 下限关系 | 安装器第一屏就判 `Environment.OSVersion`/`RtlGetVersion`，低于 1607 明确拒绝 |

---

## 10. 便携模式（zip）与包管理器

- 保留 `ckan-ui-<ver>-win-x64-portable.zip`：解压即用，**不写任何注册表**，配置与 UI 偏好落在解压目录旁的 `CkanUI.data\`（用 `--portable` 或检测 `portable.marker` 文件）。
- 便携版启动时若检测到已安装版本（HKCU Uninstall 键存在）→ 状态栏一行提示"检测到已安装版本，两者数据共享但 UI 偏好独立"。
- 包管理器清单：Chocolatey（`chocolateyinstall.ps1` 里跑 `CkanUISetup.exe -s`）、winget（`InstallerType: exe` + `SilentSwitches: -s`）。**这一步可以放到发布之后再补**，不阻塞 M13。
- 企业部署：给 IT 一页说明——`CkanUISetup.exe -s -t "C:\Program Files\CkanUI" -l C:\temp\ckanui-install.log`（提权由 UAC 承担），随后用 GPO/SCCM/Intune 派发该命令行即可。

---

## 11. 签名与"看起来像个正经软件"

| 项 | 要求 |
| --- | --- |
| 签名对象 | `CkanUISetup.exe`、`CkanUI.App.exe`、`CkanUI.Sfs.Cli.exe`、`Update.exe`（**本期不签**，见 §17 Q2） |
| 证书 | **0 成本策略**：本期不买证书（OV 约 ¥1–3k/年）；v1.0 前可申请 SignPath Foundation（OSS 免费、证书签给项目、私钥在其 HSM、按其 CI 流程签名）；不采用付费 Azure Artifact Signing，不采用自签 |
| 时间戳 | 必须（RFC 3161），否则证书过期后旧安装包签名失效 |
| CI 保管 | 证书不进仓库；用 GitHub Actions secrets + `signtool` 或云签名服务；本地构建允许未签名（打 `-dev` 后缀） |
| 未签名分支 | 公开发布的包一律文件名带 `-unsigned`，README 与安装器首页写明"Windows 会提示未知发布者，点『更多信息 → 仍要运行』"，并在发布页给出 SHA256 清单供核对 |
| SmartScreen | 新证书无信誉，前几千次下载会弹"仍要运行"。缓解：先小范围、稳定 `Publisher` 与文件名、不频繁换证书 |

---

## 12. 首启向导（第一次运行 App，不在安装器里）

三步，可"以后再说"跳过：

1. **实例**：扫描已有实例（`GameInstanceManager.GetPreferredInstance()` / `AddInstance`，`02 篇` §5）；扫不到 → 引导选目录或下载官方 KSP 说明。
2. **语言**：复用 `16 篇` §6 的实际资源并集，默认跟系统，写回 `Config.Language`（与 ckan.exe 互通）。
3. **系统集成**：勾选"注册 `ckan://` 链接与 `.ckan` 文件"（§6 第 1–7 项）+ 若检测到旧版 HKLM 注册，给一次清理机会。

若安装时注册已由安装器完成，第 3 步显示为"已完成 ✓"，只留"修复/移除"两个按钮。

---

## 13. WBS（138h，净增 126h）

| 任务 | 内容 | 工时 | 依赖 | 验收 |
| --- | --- | --- | --- | --- |
| PKG-01 | 发布物与目录布局：打包脚本产出 `current\` 结构、`adapters\`/`i18n\`/`themes\` 随包、`log4net.xml` 就位、版本资源与 `CkanUI.data` 概念 | 10 | ARC-02 | 解压后能跑；目录树与 §4.1 一致 |
| PKG-02 | Velopack 接入：`packId=CkanUI`、`packTitle`、图标、splash、`vpk pack` 参数、`Update.exe` 生命周期 | 12 | PKG-01 | `Setup.exe` 一键装到 `%LocalAppData%\CkanUI` |
| PKG-04 | VC++ 依赖：检测表读取、比较、引导安装、跳过逻辑（§7） | 10 | PKG-01, SP-41 | 干净 1607 镜像装完能启动 |
| PKG-05 | 协议与关联注册：§6 第 1–7 项，HKCU only；失败不阻塞安装 | 10 | PKG-02 | `reg query` 全中；双击 `ckan://` 拉起 |
| PKG-06 | 应用列表元数据：`Publisher`/`EstimatedSize`/`URLInfoAbout`/`DisplayIcon` 补全 | 6 | PKG-02 | 控制面板显示正确 |
| PKG-07 | 卸载流程：运行检测、锁检测、删注册项、保留清单与完成页 | 10 | PKG-05 | 卸载后无残留键与文件 |
| PKG-08 | 升级/降级/迁移：同版本修复、升级保留数据、拒降级、旧 ckan.exe 检测 | 12 | PKG-02 | 装 v1→v2 实例仍在 |
| PKG-09 | 未签名发布流水线（决策 Q2）：`-unsigned` 命名、SHA256 清单生成、发布页文案、可选的 SignPath Foundation 接口预留 | 2 | PKG-02 | 发布页有清单；文件名含 `-unsigned` |
| PKG-10 | 便携模式：`--portable`、`portable.marker`、已装版本提示 | 6 | PKG-01 | 便携版不写注册表 |
| PKG-11 | 静默/企业/包管理器：`-s`/`-t`/`-l` 说明页、GPO/SCCM 命令行、Chocolatey、winget（`InstallerType: exe`） | 8 | PKG-02 | 三个渠道各跑一次 |
| PKG-12 | Program Files / UAC 路径：`-t` 到受保护目录时的提权与失败回退、多人机组说明（决策 Q1b） | 4 | PKG-02 | 普通账户装 Program Files 触发 UAC 并可成功 |
| UI-INS-01 | 首启向导（§12） | 14 | PKG-05, UI-01 | 三步可跳过，重进不重复 |
| UI-INS-02 | 「关于/版本/检查更新/卸载入口」页 | 8 | PKG-02 | 版本与安装信息一致 |
| VM-INS | 上述 ViewModel | 6 | UI-INS-01 | — |
| TST-INS | 安装矩阵测试（§14） | 14 | PKG-07 | 矩阵全绿 |
| DOC-INS | 用户文档 + IT 部署说明 + 签名说明 | 6 | — | 三份文档 |
| **合计** | | **138** | | |

**抵扣**：`06 篇` §1.9 里的 `UI-27`（自建自动更新，20h）中"下载 + 替换"部分由 PKG-02/PKG-08 承担，保留 8h（检查/提示/频道），故 **§1.16 净增 126h**（138h − 12h），全量工时 **1864h → 1990h**（≈249 人日 ≈ 49.8 周净），排期仍为 **55 周**（M13 = W52–W55，4 周）。

---

## 14. 测试（T-INS-01..12 + 手工组 H-13）

自动化（能在虚拟机里脚本化的）：

| ID | 用例 | 判据 |
| --- | --- | --- |
| T-INS-01 | 普通账户双击 `Setup.exe` | 无 UAC 弹窗；装到 `%LocalAppData%\CkanUI` |
| T-INS-02 | `CkanUISetup.exe -s` 静默装 | 退出码 0；ARP 条目出现 |
| T-INS-03 | `Update.exe --uninstall -s` 静默卸 | 退出码 0；目录与注册项消失 |
| T-INS-04 | 注册项断言 | §6 第 1–9 项逐项存在且值正确 |
| T-INS-05 | 卸载残留断言 | `%LocalAppData%\CkanUI` 不存在；`%LocalAppData%\CKAN` **仍存在** |
| T-INS-06 | 升级保留数据 | 预置实例 + 已装 mod → 升级后仍可见 |
| T-INS-07 | 拒降级 | 新版本上跑旧 `Setup.exe` → 提示并退出非 0 |
| T-INS-08 | 协议拉起 | `start ckan://...` 打开新程序而非老 ckan.exe |
| T-INS-09 | `.ckan` 双击 | 打开"从文件安装"流程 |
| T-INS-10 | 断网安装 | 不因下载依赖失败而中断（VC++ 包内嵌或明确提示） |
| T-INS-11 | 便携版零注册 | 便携版启动后 HKCU 无新增键 |
| T-INS-12 | 签名校验 | 四个二进制均 `Valid` 且有时间戳 |

手工组 H-13（每次发版走一遍，附截图）：

1. Win10 1607 干净镜像（**VC++ 未装**）→ 装 → 启动 → 能列出实例；
2. Win10 1607 上故意用低于 1607 的兼容模式/假版本号 → 安装器明确拒绝；
3. 非管理员账户 + `%ProgramFiles%` 下的 KSP 实例 → 装完能读列表，改盘操作给出明确提示（不静默失败）；
4. 已装 ckan.exe 的机器 → 首启向导检测到 HKLM 注册并给清理选项；
5. 中文用户名 + 中文路径的实例 → 安装与卸载全程无乱码；
6. 卸载后重装 → 实例、仓库缓存、UI 偏好仍在。

---

## 15. 风险（接 `08 篇` §13）

| ID | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| R-65 | **`packId` 与数据目录同名**：`%LocalAppData%\CKAN` 既是数据目录又可能被当作安装目录，卸载会误删用户数据 | 中 | **极高** | 固定 `packId=CkanUI`；卸载断言 `%LocalAppData%\CKAN` 仍在（T-INS-05）；Code Review 检查点 |
| R-66 | 版本号不合法（`1.36.5-alpha`）导致升级判断失败 | 中 | 中 | 版本号统一 SemVer 三段（EXE-only 后无 MSI 约束）；CI 校验 |
| R-67 | 降级/回滚把新 schema 的数据写坏 | 中 | 高 | 明确拒绝降级；数据侧 schema 版本字段 + 只读模式 |
| R-68 | Velopack 的 `current` 整体替换与"用户改了 `adapters\` 目录里的文件"冲突 | 中 | 中 | 用户自定义 adapter 放 `%LocalAppData%\CKAN\adapters\`（数据侧），不放进安装目录 |
| R-69 | 协议注册被老 ckan.exe 反复改回 HKLM | 高 | 低 | HKCU 优先；只提示不强改；提供"我不用 ckan://"开关 |
| R-70 | 缺少代码签名证书导致 SmartScreen 拦截，玩家以为中毒 | 高（初期） | 中 | 未签名分支明确命名；发布说明写清；尽早申请证书 |
| R-71 | VC++ 运行库判断错误 → 干净机器上 `DllNotFoundException` | 中 | 高 | SP-41 实测；检测 + 引导 + 启动失败时给可复制的诊断提示（接 `14 篇` 诊断包） |
| R-72 | per-user 安装与"KSP 在 Program Files"权限模型冲突，用户误以为装错了 | 高 | 中 | 首启向导明确提示"游戏目录不可写，将以只读模式启动或需要以管理员运行" |

---

## 16. 待验证（SP-41..SP-45，接 `10 篇`）

| ID | 问题 | 时间盒 | Go/No-Go |
| --- | --- | --- | --- |
| SP-41 | **干净 Win10 1607 镜像上，不装 VC++ 时 Avalonia 能否启动？** | 2h | **Go/No-Go**：不能 → PKG-04 必做且必须在安装器内完成，允许内嵌 `vc_redist.x64.exe`（约 25 MB） |
| SP-42 | `CkanUISetup.exe -s -t <DIR>` 能否在 1607 上静默装/卸与自定义目录？ | 2h | 不能 → 只保留默认路径一键装，自定义目录改为手动解压 zip（发布说明注明） |
| SP-43 | `ckan://` 的 HKCU 注册能否压过老 ckan.exe 写的 HKLM？ | 1h | 不能 → 首启向导加"清理 HKLM（需提权）"为必经步骤 |
| SP-44 | 单文件打包（`PublishSingleFile`）与 Velopack 的 `current` 目录是否冲突 | 1h | 冲突 → 保持非单文件（`09 篇` §11 已定非单文件） |
| SP-45 | 签名后的 `Setup.exe` 在 SmartScreen 上的实际表现 | 1h | 记录，不阻塞 |

---

## 17. 决策（2026-10-27 已拍板）

> **重大变更：只发 EXE，不出 MSI**（用户指令「使用EXE，不要使用msi，重新评估」）。`Setup.exe` 自带的 `-s/--silent`（隐藏所有对话框并对提示答 yes）与 `-t/--installto <DIR>`（自定义目录）已完全覆盖静默安装与企业分发，MSI 及其"版本号必须 `x.x.x.x`"约束随之删除。签名策略改为 **0 成本**：本期不买证书，v1.0 前可申请 SignPath Foundation 的免费 OSS 签名。

| # | 问题 | 决策 |
| --- | --- | --- |
| Q1 | 默认安装范围 | **per-user 免提权**（默认，`%LocalAppData%\CkanUI`） |
| Q1b | 是否给 Program Files 提权路径 | **允许**：`CkanUISetup.exe -t "C:\Program Files\CkanUI"`，需 UAC，仅企业/多人机组使用 |
| Q2 | 代码签名（0 成本约束） | **本期不买证书**：v0.x 发未签名包（文件名带 `-unsigned` + 发布页给 SHA256 清单）；v1.0 前可申请 **SignPath Foundation**（OSS 免费、证书签给项目、私钥在其 HSM、需按其 CI 流程签名）；不采用付费 Azure Artifact Signing，不采用自签（对用户无价值且要装根证书） |
| Q3 | 便携 zip 是否继续发 | **保留**（CI 与排障都靠它） |
| Q4 | 是否内嵌 `vc_redist.x64.exe` | **内嵌**（+25 MB 换断网可装；SP-41 若证明不需要则整条删除） |

**工时影响**：156h → **138h**（删 `PKG-03` MSI 产物 14h、`PKG-09` 签名 10h→2h、新增 `PKG-12` 的 Program Files/UAC 路径 4h），净增 126h。

---

## 18. 修订记录

- **v1.1（E8 落盘后）**：本文 156h / M13（W52–W55）不变；全量口径随 E8 变为 **2196h / 60 周**（[20 篇](20-appearance-personalization.md) 206h / M14 = W56–W60）。E8 新增一条与本文相关的约束：**内置主题随程序目录整体替换、用户主题必须放数据侧**（20 篇 §12.1 / 本文 §12），否则升级会覆盖用户主题（R-68 同源）。

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-27 | v1.0 | 首版。定 Velopack 双产物、per-user 免提权、HKCU-only 注册；钉死 `packId=CkanUI` 与 MSI 版本格式；补 WBS 156h / M13（W52–W55），全量 1990h / 55 周 |
| 2026-10-27 | **v1.2** | **改为 EXE-only（删除 MSI 产物线）**：用户指令「使用EXE，不要使用msi，重新评估」→ `Setup.exe -s`/`-t` 覆盖静默与自定义目录；删 `PKG-03`(14h)、`PKG-09` 10h→2h（未签名发布流水线）、新增 `PKG-12`(4h，Program Files/UAC)；WBS **138h / 净增 126h**。签名改为 0 成本策略（本期不签，v1.0 前可申请 SignPath Foundation 免费 OSS 签名）。§17 全部 31 项决策回填完毕 |
