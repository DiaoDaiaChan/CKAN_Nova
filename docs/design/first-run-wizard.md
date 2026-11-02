# 首次启动指引（自动扫描 → 代理 → 版本 → 元数据 → 装机必备）

> 规格来源：[06 WBS 与里程碑](../plan/06-wbs-milestones.md) M1「实例向导」占位、用户需求「完整首次启动指引」。
> 姊妹文档：[兼容版本](compatible-versions.md)、[仓库与索引](repository.md)、[实例可靠性处置](instance-repair.md)。
> 相关代码：`src/CkanUI.ViewModels/Dialogs/FirstRunWizardViewModel.cs`、`src/CkanUI.App/Views/Dialogs/FirstRunWizardView.axaml`、`src/CkanUI.App/Onboarding/FirstRunWizardLauncher.cs`。

---

## §1. 定位与边界

新用户装完 CKAN UI 打开，面对的是一个空实例列表和一个不在本机的模组索引。上游 CKAN 把这段路留给文档：先去官网找 KSP 目录、自己判断要不要配代理、手动点「刷新」等两分钟、再去论坛抄一份「必装模组」清单。本特性把这四件事收进一个一次性的向导，让「装完 → 能装第一个模组」之间不再需要外部文档。

边界（刻意不做的事）：

- **不自动安装任何模组**。推荐清单默认全不勾，勾哪个装哪个（见 §4）。
- **不静默改全局设置**。代理写盘前必须先点「下一步」，退出向导不改任何配置。
- **不拦人**。除「登记实例」外每一步都能跳过：代理拉不通、索引下不来、版本读不出，都只提示不拦路。
- **不做常驻入口**。向导是对话框，走完即消失；重入路径在「设置 → 首次启动指引」。

分层与文件：

| 层 | 文件 | 职责 |
|---|---|---|
| 契约 | `src/CkanUI.Services.Abstractions/Onboarding/KspDiscovery.cs` | `IKspInstallScanner`、`KspCandidate`、`KspCandidateSource` |
| 契约 | `src/CkanUI.Services.Abstractions/Onboarding/ProxySettings.cs` | `ProxyMode`、`ProxySettings`、`IProxyConfigurator` |
| 服务 | `src/CkanUI.Services/Onboarding/KspInstallScanner.cs` | Steam 库 / 注册表 / 常见路径三路扫描 + 目录判定 + 版本嗅探 |
| 服务 | `src/CkanUI.Services/Onboarding/ProxyConfigurator.cs` | 进程级代理装配（`HttpClient.DefaultProxy` + `WebRequest.DefaultWebProxy`）与连通性测试 |
| 表现层 | `src/CkanUI.ViewModels/Dialogs/FirstRunWizardViewModel.cs` | 七步状态机、每步提交、错误与状态文案 |
| 表现层 | `src/CkanUI.App/Views/Dialogs/FirstRunWizardView.axaml` | 左进度轨 + 右单步舞台，纯 token 布局 |
| 宿主 | `src/CkanUI.App/Onboarding/FirstRunWizardLauncher.cs` | 装配 VM、取 owner、`ShowDialog` |
| 宿主 | `src/CkanUI.App/App.axaml.cs` | 启动早期应用代理；无实例且未完成时弹向导 |
| 入口 | `src/CkanUI.ViewModels/Pages/IFirstRunWizardLauncher.cs` + 设置页按钮 | 手动重入 |

红线：VM 不碰 `File.*`/`Directory.*`、不调 Avalonia；服务层不弹窗；窗口与 owner 只在 App 层出现。

## §2. 流程

七步，其中五步是「要做的事」，首尾是欢迎与总结：

| # | 步骤 | 做什么 | 可跳过 | 自动动作 |
|---|---|---|---|---|
| 1 | 欢迎 | 说明向导只做四件事、不改模组、不收集数据 | — | — |
| 2 | 找到游戏 | 扫描 → 选中 → `AddAsync` 或 `SwitchAsync` 登记实例 | 否（后面三步都要实例名） | 进入即扫描 |
| 3 | 下载代理 | 跟随系统 / 直连 / 手动三选一，可测连通性 | 是 | 回填上次保存的模式与地址 |
| 4 | 兼容版本 | 勾选额外兼容的游戏版本 | 是 | 进入即 `LoadAsync` |
| 5 | 元数据缓存 | `IRepositoryService.RefreshAsync()` 拉索引 | 是 | 进入即刷新 |
| 6 | 装机必备 | 六个基础模组的可选勾选 | 是 | 一个都不预勾 |
| 7 | 完成 | 四项总结 + `FirstRunCompleted=true` | — | 写完成标志后关窗 |

进入条件（`App.axaml.cs:MaybeShowStartupGuidesAsync`）：`Instances.Count == 0` **且** `UiSettings.FirstRunCompleted == false`。已有实例的老用户升级上来不会被拦。

### §2.1 启动时序（与启动动效、界面导览的先后）

三套全屏层的冲突不在「谁弹出来」，而在「同一秒里连着弹」。启动序列按下面的顺序放行，任何一层都不叠在另一层身上：

1. **等入场落位**：首屏导航先跑完，再等一个页面动效时长（`CkanMotionDurationPage`）加 260ms 卡片错峰尾巴。系统关闭动效时该等待为 0，不会平白多停。
2. **窗口不在前台就不弹**：托盘启动、启动即隐藏的场景下，向导的 owner 窗口不可见，弹窗会挂在一个看不见的窗口上；此时整段序列直接跳过。
3. **向导优先，且本次启动不再自动开导览**：向导弹出即记为「本次已展示」，关闭后也不接界面导览。刚走完一套引导再糊一层全屏遮罩，是打扰不是引导。
4. **导览只在真前台时开**：没有向导可弹时，界面导览要等主窗口 `IsVisible && IsActive` 且没有其他导览在播才启动；用户已经切走就放弃这次自动播放。

重看入口不依赖自动序列：向导完成页写明「想再看一遍界面？随时按 F1。」，F1、命令面板、「设置 → 首次启动指引」都可随时手动打开；连续按 F1 也不会把导览打回第一步重播。

## §3. 扫描与代理

### §3.1 目录扫描（`IKspInstallScanner`）

三路，按代价从低到高，命中即停的那一路不会阻塞其余：

1. **Steam 库**：复用 `ISteamLibraryLocator`（注册表 `HKCU\\Software\\Valve\\Steam\\SteamPath` → 默认路径 → `libraryfolders.vdf`），再拼 `steamapps/common/Kerbal Space Program`。
2. **注册表**：`HKLM\\...\\Uninstall\\Steam App 220200\\InstallLocation`、`HKLM\\...\\GOG.com\\Games\\1429867978\\path`。
3. **常见路径**：`ProgramFiles` / `ProgramFilesX86` / 各固定盘的 `KSP`、`Games\\KSP`、`SteamLibrary\\steamapps\\common\\KSP`。

判定「是 KSP 目录」需要同时满足：目录存在、内有 `GameData`、且有 `KSP_x64.exe` 或 `KSP.exe`。版本号从 `readme.txt` / `buildID.txt` / `KSP_x64_Data/app.info` 头部嗅探（`\b\d+\.\d+(\.\d+){0,2}\b`），读不出就是「版本未知」——不猜。

结果按不区分大小写去重；手动选择走同一条判定，不合格直接给「需要同时存在 GameData 与 KSP.exe」。

### §3.2 代理（`IProxyConfigurator`）

为什么必须做成进程级：Core 的 `NetAsyncDownloader` 走 `WebClient`，没有「按请求指定代理」的入口，能改的只有 `.NET` 的全局代理。因此：

- `Apply(settings)`：`Direct` → 两边都装空 `WebProxy`（显式绕过系统代理）；`System` → 恢复构造时抓到的系统默认值；`Manual` → 按地址构造（缺 scheme 自动补 `http://`，`bypass` 按 `,`/`;`/空格拆）。
- 生效时机有两处：启动时（`App.ApplySavedProxy`，早于任何索引刷新）与向导第 3 步「下一步」时。
- `TestAsync` 用独立 `HttpClientHandler`（`Direct` 时 `UseProxy=false`），15s 超时，返回「连接正常：… 返回 HTTP 200，耗时 312 ms」这种可核对的话。

落盘：`UiSettings.ProxyMode`（`direct`/`system`/`manual`）+ `ProxyServer` + `ProxyBypass`。写盘失败不拦流程——本次会话里代理已生效，只是下次要重填。

## §4. 装机必备：可选，不强制

清单是**社区基础设施层**，不是玩法推荐：`ModuleManager`（几乎所有模组的前置）、`KerbalEngineerRedux`、`MechJeb2`、`KerbalAlarmClock`、`Trajectories`、`Waterfall`。

- 全部条目 `IsChecked = false`，界面提供「全选推荐」「全部取消」两个显式动作，但默认不替用户选。
- 点了「安装并继续」才走 `IChangeSetService.BuildAsync` → `IInstallerService.ApplyAsync`；一个都没勾则直接跳到完成页，不发任何安装请求。
- 安装失败不拦路：摘要行写「已安装 N 个，失败 M 个」，用户可以进主界面后在「模组」页重试。

## §5. 失败处理

| 场景 | 界面行为 | 数据后果 |
|---|---|---|
| 扫不到任何 KSP | 空状态卡 + 「手动选择目录」引导 | 无 |
| 手动选的目录不是 KSP | 红条「需要同时存在 GameData 与 KSP.exe」 | 无 |
| 代理地址非法 | 红条「格式应为 http://主机:端口」，不写盘 | 无 |
| 代理测试失败 | 结果行写失败原因，不阻断「下一步」 | 无 |
| 索引拉取失败 | 摘要行写「拉取失败：…」，仍可继续 | 保留旧快照 |
| 版本列表读不出 | 状态行提示「这一步可以跳过」 | 不改兼容版本 |
| 推荐模组安装失败 | 摘要行写失败数 | 已装的成功项保留 |
| 完成标志写盘失败 | 状态行提示 | 下次启动仍会弹向导（可接受） |

所有失败都走 `SetError`/`StatusText`，不弹二级对话框——首启阶段再叠一层弹窗只会让人更想退出。

## §6. 验收

| 项 | 判据 |
|---|---|
| 自动弹出 | 干净配置（无 `ckan-ui.json` 或 `FirstRunCompleted=false`）且无实例 → 启动后出现向导 |
| 不打扰老用户 | 已有实例 或 `FirstRunCompleted=true` → 启动无向导 |
| 扫描可用 | 装过 Steam 版 KSP 的机器上，第 2 步直接列出该目录且预选中 |
| 代理生效 | 第 3 步填手动代理 → 「下一步」→ 第 5 步索引走代理拉取成功 |
| 代理持久化 | 重启后代理仍生效（`App.ApplySavedProxy` 早于索引刷新） |
| 索引可见 | 第 5 步完成行给出「索引就绪：N 个仓库，M 个模组」 |
| 推荐可选 | 第 6 步初始 0 项勾选；不勾直接「下一步」→ 完成页「已安装模组」显示 0 |
| 推荐可装 | 勾 1 项 → 「安装并继续」→ 实例 `GameData` 下出现该模组目录 |
| 可重入 | 设置页「首次启动指引」按钮能重新打开向导 |
| 不叠层 | 首屏入场动效跑完才出现向导；向导出现过的这次启动不再自动叠界面导览；连按 F1 不会重播 |
| 设计一致 | XAML 无颜色/字号/圆角/高度字面量，全部 `{DynamicResource}` token |
| 构建 | `dotnet build ckan-ui.sln` 零警告（`TreatWarningsAsErrors`） |

## §7. 已知取舍

- **不做「先装游戏」引导**：KSP 是玩家自己装的，向导只负责找到它。
- **不做静默网络探测**（如自动判断「你在墙内」）：猜错的代价比多问一句大。
- **进度轨不做可点击跳转**：跳过中间步骤会造成「实例还没登记就去拉索引」这类半成品状态，代价高于收益。
