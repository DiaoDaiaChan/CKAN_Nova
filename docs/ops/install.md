# 安装、升级与卸载（Windows 安装器）

> E7 落地说明：装什么、装到哪、写哪些 HKCU 键、为什么未签名、怎么签名、公司内怎么分发。
> 与 [design/installer.md](../design/installer.md) 分工：那篇讲决策与已知限制（D1–D6），本篇是可照抄的操作步骤。
> 规格依据：[plan/19-windows-install-flow.md](../plan/19-windows-install-flow.md) §4/§6/§8/§11；包本身怎么出见 [release.md](release.md)。
> 现状：安装器与签名通道已交付并实测；**发布物仍未签名**（本机没有代码签名证书，见 §8/§9）。

## 1. 便携包与安装器

同一份程序有两种交付形态，产物都来自 `tools/release/release.ps1`：

| 维度 | 原生单文件安装程序（`setup.exe`） | 便携包 zip | 安装脚本（`install.ps1`） |
| --- | --- | --- | --- |
| 使用方式 | 双击 setup.exe 直接弹出安装/静默安装 | 解压到任意目录，双击 CkanUI.exe | 解压便携包后，运行 PowerShell 脚本安装 |
| 写注册表 | 写 4 处 HKCU（见 §6） | 不写 | 写 4 处 HKCU（见 §6） |
| 出现在「设置 → 应用」 | 是（自带卸载向导） | 否 | 是（可正常卸载） |
| 快捷方式 | 开始菜单 + 桌面（可选） | 无 | 开始菜单 + 桌面（可选） |
| 升级 | 重跑 setup 自动原子替换 | 手工覆盖 | 重跑 install.ps1 原子切换指针 |
| 管理员权限 | 不需要（纯 HKCU，不弹 UAC） | 不需要 | 不需要（不写 HKLM，不弹 UAC） |
| 适合 | 普通最终用户、自动化分发安装 | 尝鲜、U 盘随身带、无安装权限机器 | 运维流水线定制脚本、源码构建者 |

两个 zip 的区别（`release.ps1` 一次出两个）：

| 包 | 体积 | 对机器的要求 |
| --- | --- | --- |
| `ckan-ui-<版本>-win-x64-selfcontained.zip` | 大（0.1.0 实测约 578 MB） | 无，运行时打在里面 |
| `ckan-ui-<版本>-win-x64-frameworkdependent.zip` | 小 | 需要 .NET 10 Desktop Runtime |

两个 zip 都是「解压到哪，哪就是应用目录」，里面没有多套一层包裹目录。同目录还有 `SHA256SUMS.txt` 与 `manifest.json`。

## 2. 便携包怎么用

```text
1) 解压 ckan-ui-<版本>-win-x64-selfcontained.zip 到任意可写目录（别放 Program Files，那里要提权）
2) 双击 CkanUI.exe
3) 需要校验就把 SHA256SUMS.txt 里的值与 Get-FileHash 的结果对一遍
```

主程序名正在从 `CkanUI.App.exe` 改成 `CkanUI.exe`：0.1.0 起是 `CkanUI.exe`，更早的包是 `CkanUI.App.exe`，两个名字都能用（安装器两个都认，见 §4）。

便携包不写注册表，所以不会出现在「设置 → 应用」里，也没有快捷方式；升级就是解压新包覆盖旧目录。

## 3. 安装

### 3.1 前置

- Windows 10 1607（10.0.14393）或更高，x64。
- Windows PowerShell 5.1（系统自带，不需要 PowerShell 7）。
- 先把 zip 解压出一个目录（下称 stage），安装器从这个目录复制。

### 3.2 方式一：原生安装向导（CkanUISetup.exe）

从发布页下载 `ckan-ui-<版本>-win-x64-setup.exe`（自包含内嵌版）：
1. **图形化安装**：直接双击 exe，确认目标目录后全自动安装、创建桌面与开始菜单快捷方式、登记 HKCU 卸载项，并提示是否立即启动。
2. **命令行静默安装（适合脚本与批量部署）**：
```cmd
:: 最简静默安装（默认路径 %LocalAppData%\CKAN UI）
ckan-ui-0.1.0-win-x64-setup.exe -Quiet

:: 自定义安装目录、静默且装完不自动启动
ckan-ui-0.1.0-win-x64-setup.exe -InstallDir "D:\Apps\CKAN UI" -Quiet -NoLaunch

:: 兼容 Inno Setup 风格的目录参数
ckan-ui-0.1.0-win-x64-setup.exe /DIR="D:\Apps\CKAN UI" /S
```

### 3.3 方式二：PowerShell 安装脚本（install.ps1）

适用于下载便携 zip 解压后手动执行安装：
```powershell
# 默认装到 %LocalAppData%\CKAN UI，建开始菜单 + 桌面快捷方式，装完启动
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install\install.ps1 -SourceDir .

# 换安装位置、不建快捷方式、装完不启动
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install\install.ps1 ^
    -SourceDir . -InstallDir "D:\Apps\CKAN UI" -NoShortcuts -NoLaunch
```

参数：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-SourceDir` | 必填 | 解压出来的应用目录，必须能在这里找到主程序 exe |
| `-InstallDir` | `%LocalAppData%\CKAN UI` | 安装根目录；不能是盘符根 |
| `-ExeName` | `CkanUI.exe` | 不显式给时两个名字都试（先 `CkanUI.exe`，再回退 `CkanUI.App.exe`）；显式给时只认指定名。日志会打印实际用的名字 |
| `-NoShortcuts` | 关 | 不建开始菜单与桌面快捷方式 |
| `-NoLaunch` | 关 | 装完不启动 |
| `-Force` | 关 | 检测到同名进程在运行时，从「中止」改成「警告后继续」。**不是强杀**：本安装器从不结束进程 |

退出码：

| 码 | 含义 | 怎么办 |
| --- | --- | --- |
| 0 | 安装成功 | — |
| 2 | 主程序正在运行 | 关掉 CKAN UI 再装；确实要在运行时装就加 `-Force`（文件被占用会失败并回滚） |
| 3 | 安装失败，已回滚 | 看日志最后一条错误；已复制的目录、已写的键、已建的快捷方式都撤销了 |

### 3.3 装完是什么样

```text
%LocalAppData%\CKAN UI\
├─ app\
│  └─ 0.1.0.0\                程序本体（-SourceDir 的整份拷贝）
├─ current\
│  ├─ version.txt             当前生效的版本号（一行，UTF-8 无 BOM）
│  └─ README.txt              指针文件怎么读、怎么回滚
├─ uninstall.ps1              卸载入口（UninstallString 指向它）
├─ LICENSE.md                 源目录有才复制
└─ THIRD-PARTY-NOTICES.txt    源目录有才复制
```

版本号取自主程序 exe 的 FileVersion（0.1.0 的 exe 读出来是 `0.1.0.0`，四段）；读不到就用 `0.0.0`。

`current\version.txt` 是唯一的「当前版本」指针，不是目录联接（junction）。理由见 [design/installer.md](../design/installer.md) D3：junction 要额外权限、杀软与备份工具处理不一致、卸载后容易留悬空链接；文本文件谁都能看、能改、能回滚。

### 3.4 为什么默认装在 %LocalAppData%

数据目录在 `%LocalAppData%\CKAN`（设置的 `ckan-ui.json`、仓库登记 `registry.json`、`repos\${C}、日志 `CKAN.log`）。安装目录与数据目录**必须不同名**：安装目录叫 `CKAN UI`，数据目录叫 `CKAN`，一旦同名，卸载时删安装目录会把用户数据一起带走（19 篇 R-65 把这条列为极高风险）。

装到 `%LocalAppData%` 还带来两个好处：整条路径当前用户可写，不需要管理员；卸载与升级不会碰到 UAC。

## 4. 升级

```powershell
# 解压新版本 → 用同样的命令装一遍
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\install\install.ps1 -SourceDir D:\tmp\ckan-ui-0.2.0
```

- **同版本重装**：目标版本目录整体替换（先复制到 `.staging-<随机>`，再整体改名就位），旧目录先挪到 `.backup-*` 再删掉，成功后台面不留备份。
- **新版本**：`app\0.2.0.0\${C} 与 `app\0.1.0.0\${C} 并存，`current\version.txt` 改指新版本，旧版本目录**保留**（回滚只需要改这个文本文件）。
- 安装器**不自动清理**旧版本目录：确认新版本没问题后，手工删 `<InstallDir>\app\<旧版本>`。
- 回滚：把 `current\version.txt` 改回旧版本号即可，快捷方式与 App Paths 指的都是 `current` 解析出来的 exe。

## 5. 卸载

三个入口，效果一样：

1. 「设置 → 应用 → 已安装的应用 → CKAN UI → 卸载」
2. 开始菜单里 `Programs\CKAN UI\卸载 CKAN UI.lnk`（存在时）
3. 命令行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<InstallDir>\uninstall.ps1" -Force

# 连用户设置/日志/缓存一起删（默认不删）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<InstallDir>\uninstall.ps1" -Force -RemoveUserData
```

参数：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-InstallDir` | 空 | 不给时先按「脚本自己所在的目录」推断（装到非默认位置也能卸对），推不出来才回退 `%LocalAppData%\CKAN UI` |
| `-KeepUserData` | `$true` | 保留 `%LocalAppData%\CKAN` 里的设置/日志/缓存 |
| `-RemoveUserData` | 关 | 删 `ckan-ui.json`、`ckan-ui.secrets`、`ckan-ui.json.bak`、`ckanui-*.log`；**不动** `config.json`、`repos\${C}、`CKAN.log`（那是 CKAN 侧的数据，不是本应用私有的） |
| `-Force` | 关 | 不询问直接结束运行中的 CKAN UI 进程；卸载项里的 QuietUninstallString 带的就是这个 |

退出码：0 成功；2 用户拒绝结束进程；3 误删守卫拒绝执行（目录不像安装目录）；1 删除失败（文件被占用）。

误删守卫：卸载器拒绝删盘符根、系统/共享目录（Windows、Program Files、ProgramData、Users、Temp、桌面……），并且要求目标目录里至少有一个安装标记（`app\${C}、`current\${C}、`uninstall.ps1`），否则退出码 3。

卸载后仍会留下的东西：

- `%LocalAppData%\CKAN`（用户数据，除非 `-RemoveUserData`）
- `HKCU\Software\Classes\ckan` —— `ckan://` 协议与 `.ckan` 关联由**应用自己在运行期**注册，卸载器不碰它们（见 §6）

## 6. 写了哪些注册表项

全部在 HKCU，不写 HKLM，不写系统级关联。

| 位置 | 名称 | 值（0.1.0 实测） |
| --- | --- | --- |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI` | `DisplayName` | `CKAN UI` |
| 同上 | `DisplayVersion` | `0.1.0.0` |
| 同上 | `Publisher` | `CKAN UI Project` |
| 同上 | `DisplayIcon` | `<InstallDir>\app\<版本>\CkanUI.exe,0` |
| 同上 | `InstallLocation` | `<InstallDir>` |
| 同上 | `UninstallString` | `"powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "<InstallDir>\uninstall.ps1"` |
| 同上 | `QuietUninstallString` | 同上 + ` -Force` |
| 同上 | `EstimatedSize` | REG_DWORD，单位 KB（0.1.0 实测 591995 ≈ 578 MB） |
| 同上 | `NoModify` / `NoRepair` | `1` / `1`（没有「修改」「修复」按钮） |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\CkanUI.exe` | (默认) | `<InstallDir>\app\<版本>\CkanUI.exe` |
| 同上 | `Path` | `<InstallDir>\app\<版本>` |

App Paths 的键名是**实际使用的 exe 名**：老包（`CkanUI.App.exe`）会写成 `App Paths\CkanUI.App.exe`，安装器还会顺手删掉指向本安装目录的旧名键。

核对命令：

```bash
MSYS_NO_PATHCONV=1 reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI" /s
MSYS_NO_PATHCONV=1 reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\CkanUI.exe" /s
```

（`MSYS_NO_PATHCONV=1` 只对 Git Bash 有意义：不加的话 `/s` 会被 MSYS 当成路径改写。）

快捷方式两条：

- `%AppData%\Microsoft\Windows\Start Menu\Programs\CKAN UI.lnk`
- `%UserProfile%\Desktop\CKAN UI.lnk`

指向的都是 `<InstallDir>\current\version.txt` 解析出来的 exe。

## 7. 为什么不用 MSIX

19 篇 §3.3 的结论，这里重复一遍，因为「为什么不打个 msix」是最常被问的问题：

1. **系统下限**：MSIX 需要 Windows 10 1709+，我们最低支持 10.0.14393（1607）。
2. **侧载策略**：未受管机器装 MSIX 需要开发者模式或侧载开关，1803 之前还要 sideloading key，企业策略经常直接禁掉。
3. **文件虚拟化**：MSIX 把程序装进受保护的 WindowsApps 并重定向写入，而 CKAN UI 的工作方式依赖「用户看得见的真实路径」——用户要能从资源管理器打开实例目录、外部工具要能按绝对路径读写 `repos\${C}，虚拟化会把这些路径搅乱。

代价是我们要自己维护安装器与卸载器（就是本篇这些脚本）；换来的是在 1607 起的任何机器上都不需要管理员、不需要改系统策略。

## 8. SmartScreen：为什么会提示「未知发布者」

Windows 对从网上下载的 exe 打一个标记（Mark-of-the-Web），SmartScreen 再按「发布者证书 + 这个文件/这个发布者攒下的下载信誉」决定拦不拦。两条都会触发提示：

- 包没签名 → 一律显示「未知发布者」，正文是「Windows 已保护你的电脑」。
- 包签了名但证书是新的、下载量还是 0 → 仍然可能提示，只是能显示发布者名字。

现状（v0.x）：**发布物未签名**。发布脚本不调用签名步骤，产物文件名带 `-unsigned`，请用 `SHA256SUMS.txt` 校验完整性（见 [release.md](release.md)）。

用户侧临时办法：

```powershell
# 先核对哈希，再确认来源可信
Get-FileHash .\ckan-ui-0.1.0-win-x64-selfcontained.zip -Algorithm SHA256

# 去掉下载标记（只去掉 MOTW，不改变 SmartScreen 对「未签名」的判断）
Unblock-File .\CkanUI.exe
```

弹窗里点「更多信息 → 仍要运行」也能过，但那等于让每个用户自己判断风险——这不是可长期接受的分发方式。

真正的消除路径（按成本从低到高）：

| 方案 | 效果 | 成本 |
| --- | --- | --- |
| 不签名 + SHA256 清单 | 用户每次都要点「仍要运行」 | 0 |
| SignPath Foundation（开源项目免费签名） | 有发布者名，信誉随下载量涨 | 申请审核；需开源资质 |
| OV 代码签名证书 | 有发布者名；信誉需累积（数周到数月，取决于下载量） | 每年数千元 + 硬件令牌/云 HSM |
| EV 代码签名证书 | 立即获得 SmartScreen 信誉 | 更贵；私钥必须在硬件里 |

无论走哪条，**都要加 RFC3161 时间戳**，否则证书一过期，之前签的包全部失效。

## 9. 签名流程（Sign-Artifacts.ps1）

脚本：`tools/release/Sign-Artifacts.ps1`。它只做一件事：找到 `signtool.exe`，对给定文件逐个签名，签完用 `Get-AuthenticodeSignature` 复核。

前置：`signtool.exe`。装 Windows SDK 时只勾「Windows SDK Signing Tools for Desktop Apps」即可，装完在 `%ProgramFiles(x86)%\Windows Kits\10\bin\<版本>\x64\signtool.exe`，脚本会自动找到（也会先看 PATH）。

参数：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-Path` | 必填 | 要签的文件，可以给多个（字符串数组） |
| `-Thumbprint` | 空 | 证书指纹，证书已在本机证书存储时用这条；与 `-PfxPath` 同时给以指纹为准 |
| `-PfxPath` | 空 | .pfx 路径，配合 `-PfxPassword` |
| `-PfxPassword` | 空 | pfx 口令；打印命令时一律显示为 `***`，真实执行才用明文 |
| `-TimestampUrl` | `http://timestamp.digicert.com` | RFC3161 时间戳服务；给空字符串则不时间戳（不可用于发布） |
| `-DryRun` | 关 | 只打印将要执行的完整命令行，不签名，退出码 0 |

先预演，再真签：

```powershell
# 1) 看命令行（不需要 signtool，也不需要证书）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\Sign-Artifacts.ps1 ^
    -Path .\artifacts\release\_work\stage-win-x64-selfcontained\CkanUI.exe ^
    -PfxPath .\artifacts\certs\dev-code-signing.pfx -PfxPassword ckanui-dev -DryRun

# 2) 真签（证书在证书存储里）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\Sign-Artifacts.ps1 ^
    -Path .\dist\CkanUI.exe, .\dist\CkanUI.dll -Thumbprint 0123456789ABCDEF0123456789ABCDEF01234567

# 3) 验签
Get-AuthenticodeSignature .\dist\CkanUI.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
```

预演输出长这样（本机没装 SDK 也照样能看）：

```text
[1] 定位 signtool
    注意：本机没有 signtool.exe：-DryRun 只预览命令行，所以继续（真实签名会失败）。

[2] 校验参数
    待签名 E:\...\stage\CkanUI.exe
    证书文件 E:\...\artifacts\certs\dev-code-signing.pfx

[3] 组装 signtool 命令
    时间戳 RFC3161 http://timestamp.digicert.com
    按 .pfx 选证书（口令在日志里打码）

[4] 预演（-DryRun，不执行签名）

signtool.exe sign /a /fd sha256 /tr http://timestamp.digicert.com /td sha256 /f E:\...\dev-code-signing.pfx /p *** E:\...\stage\CkanUI.exe

    口令在打印里显示为 ***：实际执行用 -PfxPassword 的值，别把明文口令写进日志或 CI 配置。

预演完成：1 个文件，未改动任何文件。
```

真实模式（不加 `-DryRun`）找不到 signtool 时会以非零退出，并打印三条可操作的路：装 SDK、先 `-DryRun`、或走 SignPath/云签名。签名中途任一文件失败即停止，不会「签一半继续」。

### 9.1 开发机自签：New-DevCertificate.ps1

`tools/release/New-DevCertificate.ps1` 生成一张自签代码签名证书（RSA 3072 / SHA256 / 默认 1 年），导出 `artifacts/certs/dev-code-signing.pfx` 与 `.cer`，用完默认从证书存储里删掉（`-Keep` 保留）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\New-DevCertificate.ps1
# 生成 + 顺手导入当前用户受信任根 + 保留证书
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\New-DevCertificate.ps1 -Trust -Keep
```

**自签只在本机有意义**：别人机器上没有这张证书，签出来的包在他们那里照样是「未知发布者」。它只用来把「签名 → 验签」链路跑通。

## 10. 公司内分发（GPO 受信任根）

如果只在公司域内分发，可以不买证书，改用「内部 CA + GPO 推受信任根」：

1. 用内部 CA（AD CS）签发一张代码签名证书，或直接把自签证书的 `.cer` 当作内部发布证书。
2. 组策略：**计算机配置 → 策略 → Windows 设置 → 安全设置 → 公钥策略 → 受信任的根证书颁发机构**，导入该 `.cer`。
3. （可选）再把同一张 `.cer` 加到**受信任的发布者**，让 UAC 与 SmartScreen 都直接放行。
4. 用这张证书签发布包（`Sign-Artifacts.ps1 -Thumbprint ...`），域内机器看到的就是「受信任的发布者」。

注意两点：这等于让域内所有机器无条件信任该证书/CA 签发的一切，安全边界要自己兜；且这个方案**只对域内机器有效**，员工拿回家装仍然提示未知发布者。

## 11. Inno Setup 模板（未实测构建）

`tools/release/install/CkanUISetup.iss` 是一份 Inno Setup 6 的 per-user 安装脚本模板，与 `install.ps1` 的注册表写入、目录布局保持一致（`PrivilegesRequired=lowest`、`DefaultDirName={localappdata}\CKAN UI`、`ArchitecturesAllowed=x64compatible`）。

**本机没有装 Inno Setup，这份 .iss 没有实测构建过**，只做模板交付。真要出 setup.exe：

```text
1) 装 Inno Setup 6（https://jrsoftware.org/isdl.php）
2) 先按 release.ps1 -KeepWork 出一个 stage（或先解压 zip 得到一个应用目录）
3) ISCC.exe tools\release\install\CkanUISetup.iss /DAppVersion=0.1.0 /DStageDir=<stage 绝对路径>
4) 产物在 artifacts\release\CkanUISetup-0.1.0-x64.exe
```

与 `install.ps1` 的差异：卸载项（`Uninstall\CkanUI`）由 Inno 自动生成而不是手写；`current\version.txt` 由 `[Code]` 段在安装完成后写入；`ckan://` 协议与 `.ckan` 关联两边都不写（应用运行期注册）。

## 12. 故障排查

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 安装器退出码 2，提示「请先关闭 CKAN UI」 | 目标 exe 正在运行 | 关掉应用再装；确实要带锁装就 `-Force`（不保证成功） |
| 安装失败，退出码 3 | 复制/写注册表出错，已回滚 | 看最后一条错误；常见是文件被占用（杀软扫描、资源管理器预览）或 InstallDir 无写权限 |
| 提示「源目录里找不到主程序」 | `-SourceDir` 指错了，或 exe 名两边都不匹配 | 确认目录里有 `CkanUI.exe`；老包用 `-ExeName CkanUI.App.exe` |
| 卸载器退出码 3 | 误删守卫：目录里没有 `app\${C}/`current\${C}/`uninstall.ps1` | 确认 `-InstallDir` 是不是装错地方了；确认要删就手工清空该目录 |
| 从开始菜单启动报缺 .NET | 装的是框架依赖包 | 换自包含包，或装 .NET 10 Desktop Runtime |
| 「设置 → 应用」里没有条目 | 用的是便携包 | 正常；便携包不写注册表 |
| 卸载后文件还在 | 文件被占用（应用没退干净、杀软） | 关掉相关进程再跑一次 `uninstall.ps1` |
| 双击 exe 弹 SmartScreen | 未签名 | 见 §8 |

## 13. 相关文件

| 文件 | 作用 |
| --- | --- |
| [tools/release/install/install.ps1](../../tools/release/install/install.ps1) | 每用户安装器 |
| [tools/release/install/uninstall.ps1](../../tools/release/install/uninstall.ps1) | 卸载器（安装时会被复制进安装目录） |
| [tools/release/install/CkanUISetup.iss](../../tools/release/install/CkanUISetup.iss) | Inno Setup 6 模板（未实测构建） |
| [tools/release/Sign-Artifacts.ps1](../../tools/release/Sign-Artifacts.ps1) | 代码签名（含 `-DryRun`） |
| [tools/release/New-DevCertificate.ps1](../../tools/release/New-DevCertificate.ps1) | 本机自签测试证书 |
| [design/installer.md](../design/installer.md) | 决策表 D1–D6 与已知限制 |
| [ops/release.md](release.md) | 怎么出便携包 |
