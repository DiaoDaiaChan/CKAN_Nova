# 里程碑状态表（M0–M14）

> 本文是 06 篇 WBS 的**执行实况**对照：每一行给出该里程碑的验收门禁、当前状态、证据所在文件。
> 规格侧口径以 `docs/plan/06-wbs-milestones.md` 与 `docs/plan/00-index.md` §3 为准；状态一旦变化必须同步 `docs/design/feature-matrix.md`。
> 生成方式：人工核对 + `dotnet build ckan-ui.sln`、`dotnet run --project tests/CkanUI.TestRunner`、`dotnet run --project tools/verify-layout -- --all` 三条命令的实测结果。

## 1. 总览

| 里程碑 | 主题 | 状态 | 门禁实测 |
| --- | --- | --- | --- |
| M0 | 骨架与契约冻结（ENV-01..05、ARC-01..05） | ✅ 完成 | 构建 0 警告 0 错误；架构测试 9/9；CI 三平台 matrix 已落 `.github/workflows/ci.yml` |
| M1 | 五个主页面 + 服务层打通（UI-01..UI-09、INT-04..08） | ✅ 完成 | 页面 VM 与视图全部可解析（`ViewLocatorTests` 钉死）；测试全绿 |
| M2 | 安装/卸载真实执行（INT-08 收口、E2） | 🚧 进行中 | `IInstallerService` + `CkanInstallerBackend` 可用；`IDownloadService`（临时文件 + 大小/SHA256 校验 + 原子 Move + 取消清理 + 失败分类）已落地；仍缺「真实联网装一个模组」的端到端冒烟 |
| M3 | 仓库、实例与设置周边补全（UI-06、UI-07、UI-28..UI-34） | ✅ 基本完成 | 仓库页（F-10）、设置页 8 组与密度、实例重命名（`RenameAsync`）、启动命令编辑（F-05/F-06）、启动后分层资源释放（18 篇 E6）均已落地 |
| M4 | 模组详情、关系与搜索打磨（UI-03、UI-10、UI-25..UI-27） | 🚧 进行中 | 详情面板与关系树已实现；未管理文件树（F-42）已落地；高级搜索 18 个前缀已全部接线（F-20..F-23 仍缺图形化筛选面板） |
| M5 | 导入导出与工具族（F-39..F-48） | 🚧 进行中 | `.ckan` 导入、modpack 导出、实例比对、未管理文件树、ZIP 灌缓存、跨实例去重、安装历史与重装、游戏时长均已落地（F-39/F-41/F-42/F-44/F-48 已实现，F-40/F-45 部分）；缺 modpack 编辑器（F-46）与下载统计图表（F-47） |
| M6 | 插件系统与外观个性化（UI-33、THM-xx） | 🚧 基本完成 | 主题 token 与 Light/Dark/HighContrast 已生效、密度三档落盘；插件页（Active/Dormant 两栏 + 可回收 ALC）已落地，缺插件声明页签的渲染 |
| M7 | 存档工作台（E1，SFS 格式） | ✅ 基本完成 | SFS 内核（解析/格式化/校验 + CLI）93 例全绿；存档工作台页面已落地（枚举/诊断/安全格式化）；V-09 `.loadmeta` 一致性校验未做 |
| M8–M14 | 日志与 AI 调试、托盘、打包发布、性能与验收 | ⏳ 未开始 | 见 `docs/plan/06-wbs-milestones.md` |

## 2. M0 已交付证据

| 条目 | 证据 |
| --- | --- |
| ENV-01 仓库骨架 | `src/` 11 个项目 + `tests/` 8 个项目 + `tools/verify-layout`，全部进 `ckan-ui.sln` |
| ENV-02 接入 Core | `src/CkanUI.Services/CkanUI.Services.csproj` 对 `external/CKAN/Core/CKAN-core.csproj` 的 ProjectReference（本地 junction，CI 走浅克隆） |
| ENV-03 日志落位 | `src/CkanUI.App/log4net.xml` + `src/CkanUI.Services/Logging/LoggingSetup.cs`（`__CKAN_LOG_DIR__` 令牌替换） |
| ENV-04 CI | `.github/workflows/ci.yml`（三平台 matrix + `TestCategory!=RequiresTxF` 分流 + Windows publish） |
| ENV-05 架构测试 | `tests/CkanUI.Architecture.Tests/DependencyRulesTests.cs`（7 条）+ `NamespaceRulesTests.cs`（2 条） |
| ARC-01 DI 组装根 | `src/CkanUI.App/ServiceRegistration.cs` |
| ARC-02 IDispatcher | `src/CkanUI.Platform/Threading/AvaloniaDispatcher.cs` |
| ARC-03 ViewLocator | `src/CkanUI.App/ViewLocator.cs`（`ResolveViewType` 可测） |
| ARC-04 异常兜底 | `Program.InstallCrashHandlers` + `IErrorReporter` + `CrashDialogViewModel` |
| ARC-05 单实例与 `ckan://` | `SingleInstanceGuard` + `App.HandleExternalArguments` |

## 3. M1–M5 已落地模块清单

| 模块 | 入口文件 | 说明 |
| --- | --- | --- |
| 外壳与导航 | `src/CkanUI.ViewModels/ShellViewModel.cs` | 十六项导航：模组 / 内容预览 / 变更集 / 仓库 / 导入导出 / modpack 编辑器 / 实例 / 游戏时长 / 存档 / 未管理文件 / 下载缓存 / 下载统计 / 跨实例去重 / 安装历史 / 插件 / 设置 |
| 模组管理 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` | DataGrid 8 列、列宽持久化、80ms 防抖搜索、勾选→变更请求 |
| 变更集 | `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs` | 预览/应用状态机；有 `IInstallerService` 时执行真实安装 |
| 仓库管理 | `src/CkanUI.ViewModels/Pages/ReposPageViewModel.cs` | 增删/上下移/单仓库刷新/恢复默认（F-10） |
| 游戏实例 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs` | 列表/克隆/遗忘/重命名/打开目录/Steam 库导入/启动（多命令选择） |
| 启动命令编辑 | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs` | 增删改排序/重置默认/持久化到 `GUIConfig.json.CommandLines`（F-05/F-06） |
| 设置 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs` | 八组（含兼容性与诊断）、改一项写一项、读回校验、密度三档 |
| 操作浮层 | `src/CkanUI.ViewModels/Operations/OperationQueueViewModel.cs` | 100ms 节流、可取消 |
| 导入导出 | `src/CkanUI.ViewModels/Pages/ModPacksPageViewModel.cs` | `.ckan` 导入预览与执行、modpack 导出、实例差集比对（F-39/F-45） |
| 服务层 | `src/CkanUI.Services/**` | Instances / Registry / Repositories / Settings / ChangeSets / Installers / Downloads / ModPacks / Logging / Bootstrap |
| 存档工作台 | `src/CkanUI.ViewModels/Pages/SavesPageViewModel.cs` | 存档枚举 + 头部/loadmeta 元数据 + 诊断 + 安全格式化（`ISaveFileService`） |
| 未管理文件 | `src/CkanUI.ViewModels/Pages/UnmanagedPageViewModel.cs` | GameData 未管理文件树、预演与二次确认删除、资源管理器定位（F-42） |
| 下载缓存 | `src/CkanUI.ViewModels/Pages/CachePageViewModel.cs` | 缓存占用/条目表、按上限清理、全部清理、从 ZIP 灌缓存（F-50/F-40） |
| 跨实例去重 | `src/CkanUI.ViewModels/Pages/DedupPageViewModel.cs` | 大小分组 + SHA256 扫描、硬链接共享、组内回滚、默认预演（F-41） |
| 安装历史 | `src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs` | JSONL 历史、已装/未装分组、按快照重装（F-44） |
| 插件 | `src/CkanUI.ViewModels/Pages/PluginsPageViewModel.cs` | Active/Dormant 两栏、显式激活、可回收 ALC 加载（F-17） |
| 搜索语法解析 | `src/CkanUI.Services/Search/ModSearchParser.cs` | 页面不再有前缀白名单：18 个前缀 + 自由词 + 取反全部走模型层（F-20..F-23） |
| 存档内核 | `src/CkanUI.Sfs/**` + `src/CkanUI.Sfs.Cli/**` | SFS 解析 / 格式化 / 校验（含 `format` / `validate` 子命令） |
| 导航历史 | `src/CkanUI.ViewModels/Navigation/NavigationService.cs` | 页面级前进/后退（双栈、深度 64）+ 模组选中历史（深度 32），外壳与详情面板各两个按钮（F-25） |
| 关于框 | `src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs` | 版本矩阵四行 + 三条官方链接 + GPL-3.0；外链走 Avalonia `Launcher`，失败只在框内提示（F-55） |
| 内容预览 | `src/CkanUI.ViewModels/Pages/ContentPageViewModel.cs` | ZIP 中央目录建树、未缓存时下载入库、路径复制与目录定位（F-29） |
| modpack 编辑器 | `src/CkanUI.ViewModels/Pages/ModPackEditorPageViewModel.cs` | 元数据/作者/许可/关系行编辑、版本折叠、互斥校验（F-46） |
| 下载统计 | `src/CkanUI.ViewModels/Pages/DownloadStatsPageViewModel.cs` | 自绘环图 + 排序表格，三档维度、超 12 段合并「其他」（F-47） |
| 系统托盘 | `src/CkanUI.App/Tray/TrayHost.cs` | 七项原生菜单、图标现画、最小化/关闭到托盘两个开关（F-51） |
| 资源释放 | `src/CkanUI.Services/Lifecycle/ResourceReleaseService.cs` | 五层（视图/缓存/注册表锁+L2/GC/工作集）四档（关/保守/均衡/激进），可逆（`RestoreAsync`），退出即还原（18 篇 E6） |

## 4. 已知缺口（按影响排序）

1. **真实下载未端到端验证**：`CkanInstallerBackend` 的联网路径只有离线假后端用例；沙箱内无法访问 CDN，需要在有网络的机器上跑一次「装一个真实模组」的冒烟。
2. **实例重命名只改配置不搬目录**：`IInstanceService.RenameAsync` 已接 `GameInstanceManager.RenameInstance`（重名可重试拒绝、自启动名同步跟随），但不移动磁盘目录——与上游语义一致，界面已注明。
3. **搜索缺图形化筛选面板**：语法已全部接线（18 前缀 + 自由词 + 取反，且 `is:`/`not:` 未知值不再静默丢弃而是进 `UnsupportedTerms` 并在状态栏中文说明），但 F-20/F-21 要求的字段勾选抽屉尚未做。
4. **Steam 库扫描只读 `libraryfolders.vdf`**：`net10.0`（非 `-windows`）下访问注册表会触发 CA1416，故未实现注册表兜底。
5. **下载层未做断点续传与自动重试**：`DownloadTargetFile.size` 恒传 0（全新下载），`retryable` 只作为标志交给上层，重试策略待 UI-35 落地。
6. **未管理文件的「接管为受管安装」未实现**：`IUnmanagedService.AdoptAsync` 仍返回不可重试失败，等 INT-09。
7. **资源释放的 L0/L1 生产参与方为空**：`IViewResourceParticipant` / `IAppCacheParticipant` 接口已就位，但缓存与视图侧尚未实现——当前保守档实际只统计 0 项；L4（工作集归还）返回 `Unsupported` 记 Skipped，需要 psapi P/Invoke 与权限，沙箱内无法验证真实工作集变化。
8. **插件声明的页签未渲染**：`PluginPageDescriptor` 只带 Key/Title/VM 类型全名，`PageTypes` 是静态字典，需要支持动态 key 并按 ViewLocator 规则解析插件 VM 类型。
9. **托盘只到编译与 VM 单测**：`TrayHost` 的真实图标、菜单点击与两个开关联动需要桌面手测（沙箱无 GUI 会话）；图标固定 32×32 不随 DPI 重绘，菜单文案是 VM 内中文常量。
10. **打包发布、性能验收**（M8–M14 主体）尚未开工。

## 5. 如何复核本表

```bash
cd ckan-ui
dotnet build ckan-ui.sln -v q --nologo          # 期望 0 警告 0 错误
dotnet run --project tests/CkanUI.TestRunner    # 期望 0 失败
dotnet run --project tools/verify-layout -- --all   # 期望「结论: 绿」
```

> 注：本沙箱内 `dotnet test` 不可用（Testhost 被拒），故本地一律用 `tests/CkanUI.TestRunner`（NUnitLite 进程内跑）；CI 上仍用 `dotnet test`。
