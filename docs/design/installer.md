# 安装器设计（E7）

> 定位：把「为什么这么装」写清楚——6 个决策、3 条不变量、失败回滚的实现要点、已知限制。
> 操作步骤与用户视角说明在 [ops/install.md](../ops/install.md)；规格来源是 [plan/19-windows-install-flow.md](../plan/19-windows-install-flow.md)。

## 1. 范围

安装器只做四件事：

1. 文件落位（`<InstallDir>\app\<版本>\${C}）
2. 快捷方式（开始菜单 + 桌面）
3. HKCU 里的卸载项（`Uninstall\CkanUI`）
4. App Paths（`App Paths\<exe 名>`）

明确不做：`ckan://` 协议与 `.ckan` 关联（应用运行期自己注册，19 篇 §6）、自动更新、每机器安装、服务/驱动、MSIX 打包。

## 2. 决策表

### D1 每用户安装，不做每机器安装

| | |
| --- | --- |
| 决策 | 默认装到 `%LocalAppData%\CKAN UI`，全部注册表写入在 HKCU，不请求管理员权限 |
| 备选 | 每机器：装到 `Program Files\CKAN UI` + 写 HKLM + 每次安装/升级弹 UAC |
| 为什么否决备选 | CKAN UI 是单用户桌面应用，数据本来就在 `%LocalAppData%\CKAN`；公司内普通员工没有管理员权限，弹 UAC 等于装不上；卸载也不该要管理员 |
| 代价 | 多用户机器上每人各装一份（磁盘不共享）；没有「所有用户」的开始菜单项 |
| 后续 | 「随机器共享一份」不在路线图内；真需要就用便携包放共享盘 |

### D2 原生 .NET 单文件安装程序（CkanUISetup.exe）为主，脚本为辅，Inno 为模板

| | |
| --- | --- |
| 决策 | `tools/installer` 构建的原生单文件安装向导 `CkanUISetup.exe` 是推荐的图形化/静默安装器；随包附带 `install.ps1` / `uninstall.ps1` 作为脚本备份；`CkanUISetup.iss` 作为 Inno Setup 6 模板同时保留 |
| 为什么能做原生 setup.exe | 基于 .NET 10 单文件自包含发布（内嵌 payload.zip），体积仅约 85 MB（压缩率高），完全不依赖外部 Inno Setup / ISCC 工具链，双击直接运行，带 GUI 提示与控制台附加双模 |
| 备选 A：纯 Inno `setup.exe` | 否决理由：CI/构建机要额外安装 Inno Setup + ISCC；容易受机器环境影响无法构建 |
| 备选 B：MSIX | 否决理由见 [ops/install.md](../ops/install.md) §7：Win10 1709 下限（我们支持 1607）、侧载策略、文件虚拟化与「用户可见真实路径」冲突 |
| 优势 | 双击直接提示安装与启动；支持 `-Quiet`、`-InstallDir` 等全部自动化静默参数；自带严格误删守卫、原子 staging 替换与自克隆卸载器；自动集成进 `release.ps1` 出包流水线并接入代码签名 |
| 代价 | 单文件首次启动需要短分解压自身运行时，比纯 C 编写的 Inno 安装器稍慢数百毫秒 |

### D3 目录布局：`<InstallDir>\app\<版本>\${C} + `current\version.txt` 文本指针

| | |
| --- | --- |
| 决策 | 每个版本一个独立目录；当前版本用 `current\version.txt` 里的版本号表示，快捷方式与 App Paths 都指向这个指针解析出来的 exe |
| 备选 A：目录联接 / 符号链接 | 否决理由：junction 要额外权限（symlink 更要）；杀软与备份工具对链接的处理不一致；卸载后容易留悬空链接；用户看不懂 |
| 备选 B：单目录原地覆盖 | 否决理由：升级到一半失败就是半损坏状态；无法回滚；运行中的文件被占用时只能硬删 |
| 为什么选文本指针 | 新旧版本并存让回滚变成「改一个文本文件」；复制先进 `.staging-<随机>` 再整体 `Directory.Move` 就位，避免半拷贝；文本文件谁都能看、能改、能备份 |
| 代价 | 升级不删旧版本，磁盘会累积（手工清理）；指针是普通文本，可能被手工改坏（下次安装会按 exe 的 FileVersion 重建） |

### D4 卸载默认保留用户数据

| | |
| --- | --- |
| 决策 | `-KeepUserData` 默认 `$true`；只有显式 `-RemoveUserData` 才删本应用私有数据 |
| 备选：默认全删 | 否决理由：卸载常常是「重装 / 换机」的前一步，删掉设置与仓库登记是数据事故；CKAN 实例动辄几十 GB，用户重下代价极高 |
| 代价 | 卸载后 `%LocalAppData%\CKAN` 仍在（文档里明说，见 ops/install.md §5） |
| 边界 | `-RemoveUserData` 也只删 `ckan-ui.json`、`ckan-ui.secrets`、`ckan-ui.json.bak`、`ckanui-*.log`；`config.json`、`repos\${C}、`CKAN.log` 属于 CKAN 侧数据，不动 |

### D5 只写 HKCU，不写 HKLM

| | |
| --- | --- |
| 决策 | 卸载项、App Paths、快捷方式全部落在当前用户范围内 |
| 为什么 | 与 D1 配套；安装/升级/卸载永不触发 UAC；公司策略常禁止写 HKLM |
| 代价 | 无法注册系统级文件关联；其他用户看不到这次安装 |
| 补充 | `ckan://` 协议按用户写在 `HKCU\Software\Classes\ckan`，由应用运行期注册，安装器不碰，卸载器也不删（应用没装时这个键指向一个不存在的 exe，Windows 会忽略） |

### D6 本期不签名，先交付签名通道

| | |
| --- | --- |
| 现状 | 没有代码签名证书；发布包未签名；SmartScreen 显示「未知发布者」；完整性靠 `SHA256SUMS.txt` |
| 已交付 | `Sign-Artifacts.ps1`（真实签名 + `-DryRun` 离线预演，且 pfx 口令在日志里打码）、`New-DevCertificate.ps1`（本机自签，把签名→验签链路跑通） |
| 后续路径 | v1.0 前评估：SignPath Foundation（开源免费）→ OV → EV。无论哪条，签名必须带 RFC3161 时间戳，且在打 zip **之前**签（签完再打包会破坏签名） |
| 为什么现在不做 | 19 篇 §11 的结论：本期不买证书；没有证书时把通道做好，等证书到位只换参数 |

## 3. 不变量

1. **安装目录名与数据目录名绝不同名**：安装目录 `CKAN UI`，数据目录 `CKAN`。同名会让「删安装目录」变成「删用户数据」（19 篇 R-65，极高风险）。
2. **所有注册表写入在 HKCU**，卸载只删自己写的键；`Uninstall\CkanUI` 只在它的 `InstallLocation` 指向本安装目录时才删（否则会抹掉另一个安装位置的登记）。
3. **卸载必须有误删守卫**：拒绝盘符根、系统/共享目录（Windows、Program Files、ProgramData、Users、Temp、桌面……），并要求目标目录里至少有一个安装标记（`app\${C}、`current\${C}、`uninstall.ps1`），否则退出码 3。

## 4. 幂等、升级与失败回滚

- **同版本重装**：先复制到 `app\.staging-<随机>`，再把已存在的同版本目录挪到 `.backup-<版本>-<时间戳>`，然后把 staging 整体改名为正式目录，最后删备份。任何一步失败都不会留下半个正式目录。
- **跨版本升级**：新版本目录与旧版本并存，只改 `current\version.txt`；旧目录保留，回滚 = 改回版本号。
- **失败回滚**：安装器维护一张回滚表（`$script:Rollback`，ArrayList），每个「产生副作用」的步骤登记一个逆操作（删新建的 InstallDir、删 staging、删 current、删快捷方式、删两个注册表键），失败时逆序执行，退出码 3。
- **进程占用**：检测同名进程时不强杀（用户可能正在编辑仓库、正在下载），退出码 2 让用户自己关；`-Force` 只是「警告后继续」，文件真被占用时仍然会失败并回滚。

## 5. 与 release.ps1 的边界

安装器不参与出包，也不读 manifest；它把 `release.ps1` 的 stage 目录（或解压出来的便携包）整份当作 `-SourceDir`。

需要 Lead 接线的地方（本次交付没有改 `release.ps1`）：

1. `release.ps1` 在 stage 组装完成、打 zip **之前**调用 `Sign-Artifacts.ps1`（有证书时）；签名失败即中止发布，不要产出「半个签名」的包。
2. 未签名时产物文件名带 `-unsigned`（19 篇 §11 的约定），并在 manifest 里记 `signing.status=unsigned`。
3. `docs/README.md` 索引补两行：`ops/install.md`、`design/installer.md`（本次按约束未改索引，`tools/verify-layout --all` 因此会报 H-05 警告）。

## 6. 已知限制

1. **Inno 模板未实测**：本机没有 Inno Setup，`CkanUISetup.iss` 只做过语法与结构审阅，没有真正 `ISCC` 构建过。
2. **发布物未签名**：本机没有证书，只能 `-DryRun` 预演；真实签名需要 signtool（Windows SDK）+ 证书。
3. **不结束进程**：检测到运行中的 CKAN UI 就退出（码 2），`-Force` 也不杀进程。
4. **不支持每机器安装**：没有 HKLM 分支，也没有「所有用户」快捷方式。
5. **不自动清理旧版本**：`app\${C} 下会一直累积历史版本目录，需要手工删。
6. **没有自动更新通道**：升级靠用户重新下载安装包；没有 feed、没有差分。
7. **只覆盖 win-x64**：与 `release.ps1 -Runtime` 的现状一致，未测 arm64。
8. **只在 Windows PowerShell 5.1 验证**：脚本用 UTF-8 with BOM 保存（5.1 会按 ANSI 936 读无 BOM 的 UTF-8，中文字符串会变乱码甚至假语法错）；PowerShell 7 没测。
9. **卸载后仍有残留**：`%LocalAppData%\CKAN`（用户数据，D4）与 `HKCU\Software\Classes\ckan`（应用注册的协议键）。
10. **`EstimatedSize` 只是快照**：安装时按目录大小估算成 KB 写一次 DWORD，之后文件变化不会更新。
11. **安装器不校验来源**：`-SourceDir` 里的文件是什么就装什么，没有签名校验、没有哈希校验；完整性要用户自己用 `SHA256SUMS.txt` 对。
12. **图标只有 exe 内嵌的那一个**：快捷方式用 `<exe>,0`，没有独立 `.ico`，也没有不同尺寸的图标资源。
13. **资源管理器的图标缓存可能滞后**：装完或升级后「设置 → 应用」里的图标偶尔要刷新资源管理器才更新。
14. **`current` 是文本指针不是目录**：任何把 `<InstallDir>\current` 当目录遍历的第三方工具都会失败（这是 D3 的代价，写在这里以免被当成 bug）。

## 7. 相关文件

| 文件 | 作用 |
| --- | --- |
| [ops/install.md](../ops/install.md) | 用户视角：装/升/卸、注册表项、SmartScreen、签名流程、GPO |
| [plan/19-windows-install-flow.md](../plan/19-windows-install-flow.md) | 规格：目标、目录、注册项表、卸载规则、签名策略 |
| [tools/release/install/install.ps1](../../tools/release/install/install.ps1) | 每用户安装器 |
| [tools/release/install/uninstall.ps1](../../tools/release/install/uninstall.ps1) | 卸载器 |
| [tools/release/install/CkanUISetup.iss](../../tools/release/install/CkanUISetup.iss) | Inno Setup 6 模板 |
| [tools/release/Sign-Artifacts.ps1](../../tools/release/Sign-Artifacts.ps1) | 代码签名 |
| [tools/release/New-DevCertificate.ps1](../../tools/release/New-DevCertificate.ps1) | 自签测试证书 |
