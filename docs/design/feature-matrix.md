# 特性对齐状态矩阵（Feature Parity Matrix）

> 本文是 **55 项特性（F-01..F-55）** 在新 UI（ckan-ui）中的实现状态跟踪矩阵。
> 规格来源：[12 全特性对齐清单](../plan/12-feature-parity.md) §5、[06 WBS 与里程碑](../plan/06-wbs-milestones.md)。
> 代码口径：基于 `src/` 源码实测检索，状态分为 **已实现**（基础逻辑与界面骨架均落地）、**部分**（底层服务/模型/局部逻辑就绪，待专用对话框或页面接入）、**未开始**（尚未编写实现代码）。

---

## 1. 55 项特性实现状态总表

| F-ID | 特性 | 对应任务 | 实现位置（文件:行） | 状态 | 验收方式 |
|---|---|---|---|---|---|
| F-01 | 多实例列表（名称/游戏/版本/时长/路径，选中、设为默认、遗忘、重命名、新建） | UI-05, UI-32, INT-04 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:22`, `src/CkanUI.Services/Instances/InstanceService.cs:9` | 已实现 | 单测 `InstanceServiceTests` 与 `InstancesPageViewModel` 覆盖增删、重命名、切换默认与多实例投影断言 |
| F-02 | 克隆实例（源/目标路径、新名、设为默认、切换、共享 stock、可选路径复制） | UI-32, INT-04 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:420`, `src/CkanUI.Services/Instances/InstanceService.cs:68` | 已实现 | 单测 `InstanceServiceTests.CloneAsync` 验证克隆同级路径推导、新实例注册表加载与选中切换 |
| F-03 | Steam 库导入（扫描 Steam 库并识别 KSP 路径） | UI-32 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:765`, `src/CkanUI.ViewModels/Pages/Instances/SteamLibraryParser.cs:10` | 已实现 | 单测 `SteamLibraryParserTests` 验证 `libraryfolders.vdf` 两种语法解析与 KSP 目录自动探测 |
| F-04 | 实例右键：打开目录 / 加入 CKAN / 克隆 | UI-32 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:420,609,679`, `src/CkanUI.Services/Instances/InstanceService.cs:80` | 已实现 | 视图模型单测验证 `OpenFolderCommand`、`BeginCloneCommand`、`ForgetCommand` 动作分发 |
| F-05 | 启动游戏（多命令选择） | UI-28, INT-04 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:717`, `src/CkanUI.ViewModels/Pages/Instances/IInstanceShellLauncher.cs:15` | 已实现 | `LaunchSelectedAsync` 多命令走 `IDialogService.SelectAsync` 选择、取消即不启动；`ShellInstanceLauncher.LaunchGameAsync(gameDir, command)` 按命令解析 binary/args，Steam URI 直走系统外壳，缺文件给「未在该目录找到启动文件」。单测 `InstancesLaunchCommandTests` 覆盖 |
| F-06 | 游戏命令行编辑（列表、重置、增、接受、取消） | UI-28 | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs:1`, `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandFile.cs:1` | 已实现 | 面板支持增/删/改/上移/下移/重置默认/接受/取消，`HasChanges`/`CanResetToDefaults`/`CanRemove`（不允许删到 0 条）；持久化到 `<GameDir>/CKAN/GUIConfig.json` 的 `CommandLines`，只改该键、临时文件 + `File.Move(overwrite:true)` 原子写；默认命令按 `KerbalSpaceProgram.DefaultCommandLines` 语义生成 |
| F-07 | 打开游戏目录（菜单 + 托盘） | UI-01 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:680`, `src/CkanUI.ViewModels/Pages/Instances/IInstanceShellLauncher.cs:14` | 已实现 | 验证通过系统外壳接口启动文件管理器定位游戏根目录 |
| F-08 | 实例锁定/注册表损坏的处置（删锁、回滚、提示升级 CKAN） | INT-04, UI-09 | `src/CkanUI.Services/Instances/InstanceService.cs:45`, `src/CkanUI.Services/Registry/RegistryService.cs:37`, `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:305` | 已实现 | 单测验证锁定实例拒绝切换并提示不可重试错误，损坏注册表隔离不阻断其它实例枚举 |
| F-09 | 切换实例后按需刷新（InstanceChanged → 重载列表） | INT-04, VM-04 | `src/CkanUI.Services/Instances/InstanceService.cs:25,91`, `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:176` | 已实现 | 单测验证订阅 Core `InstanceChanged` 事件后异步派发并整体刷新实例行投影 |
| F-10 | 元数据仓库管理（增、删、上移、下移、优先级、名称/URL 编辑、官方仓库预设） | UI-06, INT-06 | `src/CkanUI.Services/Repositories/RepositoryService.cs:48,57`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:320` | 已实现 | 仓库页覆盖增删、上下移（优先级重编号）、单仓库刷新、恢复官方默认；写盘走 `RepositoriesSet` + `RegistryManager.Save()`，校验在纯函数 `RepositoryCatalog` 里可离线测。改地址 = 移除 + 新增（Core 无 rename）；无「启用/禁用」开关（`Repository` 只有 name/uri/priority） |
| F-11 | 认证令牌（host + token，增删，脱敏显示） | UI-34 | `src/CkanUI.Services/Settings/SettingsService.cs:188`, `src/CkanUI.ViewModels/Pages/Settings/AuthTokenEditorViewModel.cs:13`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:341` | 已实现 | 单测 `SettingsServiceTests` 验证令牌增删改、读写脱敏掩码与敏感字符串不入日志 |
| F-12 | 仓库刷新（手动、启动时、定时、暂停、Ctrl 强制重下） | UI-06, INT-06 | , ,  | 已实现 | 手动刷新命令、自动刷新周期配置与 RepositoryService.RefreshAsync(skipETags) 跳过 ETag 强制重下协议均已落地；2 例测试 (RepositorySkipETagsTests) |
| F-13 | 稳定性容忍度（全局 + 单 mod 覆盖） | UI-07, UI-29 | `src/CkanUI.Services/Models/GUIMod.cs:171`, `src/CkanUI.Services/Models/ModIndexOperations.cs:266` | 部分 | 模型层已支持容忍度冲突判定；设置页全局容忍度与详情页单模组覆盖 UI 待 UI-07 / UI-29 接入 |
| F-14 | 兼容游戏版本管理（UI-11） | UI-11 | `src/CkanUI.Services/Compatibility/CkanCompatibleVersionsService.cs:1`, `src/CkanUI.ViewModels/Dialogs/CompatibleVersionsDialogViewModel.cs:1` | 已实现 | 契约 `ICompatibleVersionsService` + 服务实现：对接 Core GameVersionCriteria 与 GameInstance.SetCompatibleVersions；对话框提供版本列表勾选、版本号排序、批量全选/全不选与保存。用例：`CompatibleVersionsServiceTests`（13）、`CompatibleVersionsDialogViewModelTests`（12） |
| F-15 | 首选下载主机设置（UI-30） | UI-30 | `src/CkanUI.Services/Settings/PreferredHostsService.cs:1`, `src/CkanUI.ViewModels/Dialogs/PreferredHostsDialogViewModel.cs:1` | 已实现 | 契约 `IPreferredHostsService` + 服务实现：内置 github.com, spacedock.info 等已知主机、配置持久化回写；对话框提供主机列表上下移动重排优先级、添加自定义主机、删除、URL 清洗与重复项校验。用例：`PreferredHostsServiceTests`（7）、`PreferredHostsDialogViewModelTests`（11） |
| F-16 | 安装过滤器（全局 + 实例级，仅影响后续安装） | UI-31 | `src/CkanUI.Services/Settings/InstallFiltersService.cs:1`, `src/CkanUI.ViewModels/Dialogs/InstallFiltersDialogViewModel.cs:1` | 已实现 | 契约 `IInstallFiltersService` + 服务实现：对接 Core 全局规则与默认预设、实例自适应识别；对话框支持添加规则、正则校验、删除单项、恢复默认预设。用例：`InstallFiltersServiceTests`（5）、`InstallFiltersDialogViewModelTests`（10） |
| F-17 | 插件系统（激活/停用/重载/卸载/新增，Active 与 Dormant 两栏） | UI-33 | `src/CkanUI.Services.Abstractions/IPluginService.cs:1`, `src/CkanUI.Services/Plugins/PluginService.cs:1`, `src/CkanUI.ViewModels/Pages/PluginsPageViewModel.cs:1` | 已实现 | 契约 `ICKanUiPlugin` + `IPluginService`（Discover/Activate/Deactivate/Reload/Uninstall/Add + `Failures` 留痕），实现用可回收 `AssemblyLoadContext`（契约与 System/Avalonia 前缀返回 null，防双份 Abstractions）、`LoadFromStream` 不占 dll 句柄；页面 Active/Dormant 两栏、显式「激活」才跑 `Initialize`、状态徽标与错误栏。用例：`PluginServiceTests`（23）、`PluginsPageViewModelTests`（15）。缺口：插件声明的页签尚未渲染成导航项；ALC 卸载尽力而为；不校验签名 |
| F-18 | Mod 列表 15 列（Installed、Auto-installed、Update、Replace、Name、Author 等） | UI-02, VM-01 | `src/CkanUI.ViewModels/Pages/Mods/ModsRowProjection.cs:16`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowView.cs:13`, `src/CkanUI.App/Views/ModsPageView.axaml:160` | 已实现 | 单测 `ModsRowProjectionTests` 验证 15 列数据投影与格式化，UI DataGrid 列头绑定 |
| F-19 | 11 种状态过滤器 + 排序（点击列头，含 Update 列自动排序选项） | UI-02, VM-01 | `src/CkanUI.Services/Models/GUIModFilter.cs:3`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowFilter.cs:10`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowSorter.cs:10` | 已实现 | 单测 `ModListFilterTests` 与 `ModsRowSorterTests` 覆盖 11 种过滤枚举与列头多字段升降序 |
| F-20 | 组合搜索面板（Search / Or 两组，逐字段可选） | UI-14, MOD-01 | `src/CkanUI.ViewModels/Pages/Mods/ModSearchDrawerViewModel.cs:1`, `src/CkanUI.App/Views/Mods/ModSearchDrawerView.axaml:1` | 已实现 | 组合搜索独立抽屉（ModSearchDrawerViewModel），支持多条件组合、正反向双向同步，单测覆盖（`ModSearchDrawerViewModelTests`） |
| F-21 | 搜索字段 18 项（Name、Author、Description、Depends on、Conflicts 等 18 项） | UI-14, MOD-01 | `src/CkanUI.ViewModels/Pages/Mods/ModSearchDrawerViewModel.cs:1`, `src/CkanUI.Services.Abstractions/Dtos/ModSearchSpec.cs:1` | 已实现 | 18 项独立图形化勾选表单（基本/关系/分类/状态三态开关）完整落地，支持双向解析与语法生成（13 例） |
| F-22 | 标签（Tags）与标签徽章、点击显隐 | UI-02 | `src/CkanUI.ViewModels/Pages/Mods/ModSearchDrawerViewModel.cs:65`, `src/CkanUI.App/Views/Mods/ModSearchDrawerView.axaml:1` | 已实现 | 标签芯片渲染，点击即时触发 AppendTag 追加 `tag:<name>` 过滤，双向过滤联动验证完备 |
| F-23 | Label 过滤与编辑入口 | UI-02, UI-24b | `src/CkanUI.ViewModels/Dialogs/LabelEditDialogViewModel.cs:1`, `src/CkanUI.App/Views/Dialogs/LabelEditDialogView.axaml:1` | 已实现 | 模组页 Label 筛选区下拉联动，提供 Label 编辑弹窗入口（UI-24b），支持新增、编辑、删除与持久化联动 |
| F-24 | Label 属性（名称、背景色、隐藏于其它过滤器、更新/安装通知、不升级等 10 项） | UI-24b | `src/CkanUI.Services/Labels/LabelService.cs:1`, `src/CkanUI.ViewModels/Dialogs/LabelEditDialogViewModel.cs:1` | 已实现 | 10 项属性完整编辑面板落地（名称、颜色、锁定版本、隐藏于其它过滤器等），校验与上游 ModuleLabelList 契约对齐，单测覆盖（`LabelEditDialogViewModelTests`） |
| F-25 | 前进/后退导航（选中历史） | UI-02 | `src/CkanUI.ViewModels/Navigation/NavigationService.cs:31`, `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:365` | 已实现 | 两套历史都做了：① 页面级前进/后退（`NavigationService` 双栈、深度上限 64、新导航清空前进栈、外壳左栏 `←`/`→` 按钮）；② 模组选中历史（`ModsPageViewModel` 的 `_selectionBack`/`_selectionForward`、深度上限 32、回退目标已不在当前列表时原地不动、详情面板 `←`/`→`）。单测 `NavigationHistoryTests`（10 例）+ `ModsSelectionHistoryTests`（8 例） |
| F-26 | 模组详情多页签架构（元数据/关系/版本/本地文件） | UI-08 | `src/CkanUI.ViewModels/ModDetailsViewModel.cs:1`, `src/CkanUI.App/Views/ModDetailsView.axaml:1` | 已实现 | 多页签导航架构：Tab 1 基础元数据与作者许可证、Tab 2 依赖与推荐关系树、Tab 3 历史与多版本发布列表（F-30）、Tab 4 本地受管文件与跳转内容预览（F-29），纯 Token 绑定与无障碍支持 |
| F-27 | 元数据页（Identifier、Replaced by、Max game ver.、Author、Downloads 等） | UI-03 | `src/CkanUI.ViewModels/Dialogs/ModDetailsSnapshot.cs:4`, `src/CkanUI.ViewModels/Dialogs/ModDetailsFormatter.cs:10` | 已实现 | 视图模型单测验证元数据各字段提取、格式化展示与空状态回退 |
| F-28 | 关系树（九色图例、反向关系、双击跳转） | UI-18 | `src/CkanUI.ViewModels/Relations/RelationLegendItem.cs:1`, `src/CkanUI.ViewModels/Relations/RelationTreeViewModel.cs:1`, `src/CkanUI.App/Views/Relations/RelationTreeView.axaml:1` | 已实现 | 9 色语义图例（Depends/Conflicts/Recommends 等）WrapPanel 展示、正向/反向依赖构建与单键切换（谁依赖了我）、循环依赖安全截断保护、双击节点跳转模组详情导航，单测覆盖（`RelationTreeViewModelTests`） |
| F-29 | 内容页（未缓存时提示、下载、打开 ZIP 预览文件树） | UI-03 | — | 已实现 | @BT@src/CkanUI.Services/Content/{ContentService,NetModuleCacheZipStore}.cs@BT@（中央目录建树 + 临时分卷下载后 @BT@cache.Store(move:true,validate:true)@BT@）、@BT@src/CkanUI.ViewModels/Pages/ContentPageViewModel.cs@BT@、@BT@src/CkanUI.App/Views/ContentPageView.axaml@BT@（TreeView）；测试 @BT@ContentServiceTests@BT@(11) + @BT@ContentPageViewModelTests@BT@(12) |
| F-30 | 版本列表与指定版本安装（UI-09） | UI-09 | `src/CkanUI.ViewModels/ModDetailsViewModel.cs:150`, `src/CkanUI.App/Views/ModDetailsView.axaml:120` | 已实现 | 详情页 Tab 3 展示模组可用历史版本（发布版本、发布时间、兼容游戏版本范围、下载体积），支持选择并直接交接待安装版本 |
| F-31 | 右键动作（重装、复制标识符、复制下载地址、打开主页） | UI-02, UI-18 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1630` | 已实现 | 模组列表 DataGrid.ContextMenu 接入，支持重装、复制标识符、复制下载地址与系统浏览器打开主页，单测覆盖（10 例） |
| F-32 | 全量更新（Update all）、应用（Apply） | UI-02, UI-04 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:1587`, `src/CkanUI.App/Views/ModsPageView.axaml:85` | 已实现 | 全量更新（`UpdateAllCommand`）识别并全选可更新模组，应用变更（`ApplyChangesCommand`）校验并交接 `ChangesPageViewModel` 导航（`ModsUpdateAllTests` 5 例） |
| F-33 | 变更集页（Mod / Change / Reasons / Skip? 四列、清空、应用、游戏运行警告） | UI-04, VM-02 | `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs:1`, `src/CkanUI.App/Views/ChangesPageView.axaml:1`, `src/CkanUI.Services/ChangeSets/CoreChangeSetPlanner.cs:1` | 已实现 | 变更集页：冲突/警告横幅、六列明细（名称/动作/版本/来源/体积/操作）、清空、单条剔除、应用与执行状态机（Idle→Computing→Ready→Applying→Done/Failed/Cancelled）、**游戏运行中横幅**（12 篇 §F-33：探测实例目录下的进程，拦下主按钮并提供「仍要执行」，见 `src/CkanUI.Services/Instances/InstanceService.cs:122` `IsGameRunning` 与 `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs` `HasGameRunningWarning`，用例 `tests/CkanUI.Services.Tests/GameProcessProbeTests.cs`、`tests/CkanUI.ViewModels.Tests/ChangesGameRunningTests.cs`）。「跳过」仍与原因合并进明细说明文本 |
| F-34 | 提供者选择（虚拟 mod 多候选单选） | UI-16 | `src/CkanUI.ViewModels/Dialogs/VirtualModuleDialogViewModel.cs:1`, `src/CkanUI.App/Views/Dialogs/VirtualModuleDialogView.axaml:1`, `src/CkanUI.Services.Abstractions/IModDialogService.cs:1` | 部分 | 对话框本身已完成：单选互斥、默认第一项、未选拒绝确认、双击即确认，取消返回 `null`（`ModDialogsTests` 25 例）。缺口：`CkanInstallerBackend` 抛 `TooManyModsProvideKraken` 处尚未调用（安装编排接入点未接） |
| F-35 | 推荐/建议/被支持选择（三组、Check All / Uncheck All / Always uncheck all 等） | UI-15, INT-07 | `src/CkanUI.ViewModels/Dialogs/RecommendationDialogViewModel.cs:1`, `src/CkanUI.App/Views/Dialogs/RecommendationDialogView.axaml:1` | 部分 | 对话框完成：三组勾选、全选/全不选（全选跳过 DLC 与冲突项）、零候选才禁用确认、`SuppressRecommendations` 写进 `RecommendationSelection` 并已并入 `UiSettings.SuppressRecommendations`。缺口：安装确认前尚未调用（`ModsPageViewModel` 安装流程仍由 `CoreChangeSetPlanner` 静默处理推荐项） |
| F-36 | 推荐审计（遍历已装 mod 的推荐与建议） | UI-17 | `src/CkanUI.ViewModels/Dialogs/AuditRecommendationsDialogViewModel.cs:1`, `src/CkanUI.App/Views/Dialogs/AuditRecommendationsDialogView.axaml:1` | 已实现 | 遍历已装模组 recommends/suggests、去重与来源归并、排除已安装与待安装队列、候选名称简介映射、全选/全不选、批量交接 IChangeRequestSink 入变更集；9 例测试 (AuditRecommendationsDialogTests) |
| F-37 | 安装/卸载/升级执行（进度、取消、重试、状态日志） | UI-08, INT-08 | `src/CkanUI.Services/Installers/InstallerService.cs:1`, `src/CkanUI.Services/Operations/OperationQueue.cs:10`, `src/CkanUI.ViewModels/Operations/OperationQueueViewModel.cs:1` | 部分 | 执行后端 + 异步操作队列 + 右下角操作浮层（进度、字节/速率文本、取消）已落地，取消在阶段边界生效。缺口：逐项重试与独立全屏等待日志页未做（浮层替代 WaitTabPage） |
| F-38 | 下载失败处置（逐项 Retry?/Skip?、错误文本、复制到剪贴板） | UI-35 | ,  | 已实现 | 失败分类与 DownloadErrorsDialogViewModel 对话框落地；支持逐项 Retry/Skip/全部重试/跳过继续与一键复制诊断文本；10 例测试 (DownloadErrorsDialogViewModelTests) |
| F-39 | 从 .ckan 安装（多选文件） | UI-20 | `src/CkanUI.Services/ModPacks/ModPackCatalog.cs:1`, `src/CkanUI.ViewModels/Pages/ModPacksPageViewModel.cs:1` | 已实现 | 多选 `.ckan`（多行/分号分隔）先预览（将安装/升级/降级/已安装/找不到/有冲突+原因），跨来源版本冲突只报不合并；执行映射成 `ChangeRequest` → `IChangeSetService.BuildAsync`（有冲突即中止）→ `IInstallerService.ApplyAsync`，无自有下载逻辑 |
| F-40 | 导入已下载 mods（ZIP → 缓存） | UI-20 | `src/CkanUI.Services/Cache/CkanCacheStore.cs:1`, `src/CkanUI.ViewModels/Pages/CachePageViewModel.cs:1` | 部分 | 缓存页已能「从 ZIP 导入」：`NetModuleCache.ZipValid` → `Registry.GetDownloadHashesIndex()`（SHA256→SHA1）反查 → 读 zip 内 `*.ckan` 补伪下载地址 → `Store(move:false, validate:false)`；坏 zip 不可重试并给中文原因。缺口：导入后不自动触发安装（只进缓存） |
| F-41 | 去重（跨实例扫描并共享可共享文件） | UI-21 | `src/CkanUI.Services.Abstractions/IInstanceDedupService.cs:1`, `src/CkanUI.Services/Dedup/DedupScanner.cs:1`, `src/CkanUI.Services/Dedup/DedupExecutor.cs:1`, `src/CkanUI.ViewModels/Pages/DedupPageViewModel.cs:1` | 已实现 | 两阶段扫描（先按大小分组，再 SHA256 流式哈希）→ 按 (哈希, 卷) 成组；执行走「临时名 → 建硬链接 → 校验文件标识 → `File.Move(overwrite)`」，失败按组逆序回滚。六条安全边界：仅在 GameData 内、跳过重解析点、默认跳过受管路径、同卷、≥2 副本、默认 dry-run；页面两次确认。用例：`DedupServiceTests`（13，1 例因沙箱无符号链接权限 Ignore）、`DedupPageViewModelTests`（12）。缺口：非 Windows 无 inode 探测；无共享存储目录模式 |
| F-42 | 未管理文件（树、刷新、全展开/全折叠/重置、在文件夹中显示、删除） | UI-10, INT-09 | `src/CkanUI.Services/Unmanaged/UnmanagedScanner.cs:1`, `src/CkanUI.ViewModels/Pages/UnmanagedPageViewModel.cs:1`, `src/CkanUI.App/Views/UnmanagedPageView.axaml:1` | 已实现 | 受管路径集合 = `registry.InstalledFileInfo()` 的键（去 `GameData/` 前缀）+ 纯 DLL 安装；仅展示"递归含未管理文件"的目录，目录尺寸为未管理文件合计；删除默认 dry-run，含受管内容的批次整批拒绝（不可重试），界面两次确认；「接管为受管安装」仍由 INT-09 承担 |
| F-43 | 残留目录清理（目录树 + 内容预览、打开目录、删除勾选、全部保留） | UI-22 | `src/CkanUI.Services/Cleanup/ResidualCleanupService.cs:1`, `src/CkanUI.ViewModels/Dialogs/ResidualCleanupDialogViewModel.cs:1`, `src/CkanUI.Services/Installers/CkanInstallerBackend.cs:368` | 已实现 | `possibleConfigOnlyDirs` 贯通到 `InstallReport.ResidualDirectories`；对话框三出口（打开目录/删除勾选/全部保留），逐行展示路径·文件数·字节·样例文件，被拒行不可勾选。安全边界：GameData 之外、重解析点自身、内含重解析点、含受管文件四类拒绝理由；删除逐个跳过而非整批拒绝，失败返回可重试中文原因（`ResidualCleanupServiceTests` 15 例 + `ResidualCleanupDialogTests` 12 例） |
| F-44 | 安装历史（时间戳列表、已装/未装分组、从历史重装） | UI-23 | `src/CkanUI.Services.Abstractions/IInstallHistoryService.cs:1`, `src/CkanUI.Services/History/InstallHistoryService.cs:1`, `src/CkanUI.Services/History/HistoryRecordingInstaller.cs:1`, `src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs:1` | 已实现 | 每实例一份 `<GameDir>/CKAN/install_history.jsonl`（一次操作 = entry + snapshot 两行同时间戳，坏行跳过，2000 行滚动截断，`.tmp` + `File.Move` 原子写）；安装器装饰器自动记账（`InstallHistoryScope` 抑制重装双记）；重装走 `IChangeSetService.BuildAsync` + `IInstallerService.ApplyAsync`，冲突即中止。页面按「当前是否装着」分已装/未装组。用例：`InstallHistoryServiceTests`（15）、`InstallHistoryPageViewModelTests`（17）。缺口：只按行数截断、不能删除/清空历史、`RemoveExtras` 无界面开关 |
| F-45 | 导出：已装列表（不可回导）／modpack（可回导） | UI-24 | `src/CkanUI.Services/ModPacks/CkanModPackStore.cs:1`, `src/CkanUI.ViewModels/Pages/ModPacks/InstalledListExporter.cs:1`, `src/CkanUI.ViewModels/Pages/ModPacks/ExportFormViewModel.cs:1` | 已实现 | modpack 导出已实现；非 ckan 多格式导出已闭环（PlainText, Markdown, BbCode, Csv, Tsv，纯函数转换 + 特殊字符转义与流式写出），上游 Exporter 与 CkanModPackStore 贯通；测试 `InstalledListExporterTests`（15 例）全过 |
| F-46 | modpack 编辑器（标识/名称/摘要/作者/版本/关系单选等） | UI-24 | — | 已实现 | @BT@src/CkanUI.Services/ModPacks/{ModPackEditorStore,ModPackEditorValidator}.cs@BT@ + @BT@ModPackService.Editor.cs@BT@（@BT@IModPackService@BT@ 三个 DIM）、@BT@src/CkanUI.ViewModels/Pages/ModPackEditorPageViewModel.cs@BT@、@BT@src/CkanUI.App/Views/ModPackEditorPageView.axaml@BT@；测试 @BT@ModPackEditorStoreTests@BT@(20) + @BT@ModPackEditorValidatorTests@BT@(26) + @BT@ModPackEditorPageViewModelTests@BT@(24)；ksp_version 与 min/max 互斥判 Error |
| F-47 | 下载统计（饼图 + 表格） | UI-25 | — | 已实现 | @BT@src/CkanUI.Services.Abstractions/Dtos/DownloadStats.cs@BT@（三档维度 Mods/Tags/Installed）、@BT@src/CkanUI.Services/Stats/{DownloadStatsAggregator,DownloadStatsService}.cs@BT@、@BT@src/CkanUI.ViewModels/Pages/DownloadStatsPageViewModel.cs@BT@、@BT@src/CkanUI.App/Controls/DonutChart.cs@BT@；按仓库维度因 @BT@RepositorySnapshot@BT@ 无 per-module 映射未做；测试 @BT@DownloadStatsServiceTests@BT@(16) + @BT@DownloadStatsPageViewModelTests@BT@(15) |
| F-48 | 游戏时长（实例名 / 小时数，F2 就地编辑） | UI-10 | `src/CkanUI.Services/PlayTime/PlayTimeService.cs:1`, `src/CkanUI.ViewModels/Pages/PlayTimePageViewModel.cs:1`, `src/CkanUI.App/Views/PlayTimePageView.axaml:1` | 已实现 | 时长页：实例名 + 小时数、F2 就地编辑、Enter 保存 / Esc 取消、空状态与错误栏；存储为 `%LocalAppData%/CKAN/play-time.json`（GUID 临时文件 + `File.Move(overwrite)`，坏 JSON 读空）。导航第 6 项「游戏时长」。缺口：启动/退出路径尚未自动累计时长（只能手工编辑） |
| F-49 | 设置总表（仓库、令牌、缓存、自动更新、行为、外观、高级等分组） | UI-07 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:264`, `src/CkanUI.App/Views/SettingsPageView.axaml:1` | 已实现 | 设置页 7 组分类配置双向绑定已就绪，支持改一项写一项与读回校验 |
| F-50 | 下载缓存管理（文件数/容量、路径编辑、上限 MiB、Purge、Reset、打开） | UI-19 | `src/CkanUI.Services/Cache/CacheService.cs:1`, `src/CkanUI.ViewModels/Pages/CachePageViewModel.cs:1`, `src/CkanUI.App/Views/CachePageView.axaml:1` | 已实现 | 缓存页条目/容量/上限展示、按上限清理、全部清理、单项删除、ZIP 导入、在文件管理器中打开缓存目录与重置上限（2048 MiB 或无限制）完整闭环；18 例测试 (CacheServiceTests, CachePageViewModelTests) |
| F-51 | 托盘图标（启用/最小化到托盘；7 项右键菜单） | UI-26 | — | 已实现 | @BT@src/CkanUI.ViewModels/Shell/{TrayMenuViewModel,TrayMenuItemViewModel,ITrayHostActions}.cs@BT@、@BT@src/CkanUI.App/Tray/{TrayHost,TrayHostActions}.cs@BT@（@BT@TrayIcon.GetIcons/SetIcons@BT@，Avalonia 12 无 @BT@Application.TrayIcons@BT@）、@BT@UiSettings.MinimizeToTray/CloseToTray@BT@；测试 @BT@TrayMenuTests@BT@(17) + @BT@SettingsTrayTests@BT@(8)；真实图标点击需桌面手测 |
| F-52 | 自动更新（启动时检查、dev builds、检查/安装、新版本对话框） | UI-27 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:270` | 部分 | 设置页支持开发版更新通道开关；更新检查器、新版本弹窗与二进制替换待 UI-27 / PKG 接入 |
| F-53 | 语言与本地化（语言选择需重启；多语 resx/json 支持） | UI-13 | `src/CkanUI.Localization/Localizer.cs:15`, `src/CkanUI.Localization/LanguagePackLoader.cs:11`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:367` | 已实现 | 单测 `LocalizationTests` 覆盖本地化加载、多语词包回退链及设置页语言切换与持久化 |
| F-54 | 显示格式（隐藏 epoch、隐藏 "v"、自动排序） | UI-07 | `src/CkanUI.ViewModels/Pages/Settings/VersionDisplayFormat.cs:1`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowView.cs:25`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:491` | 已实现 | 三个开关落在设置页「高级与诊断」组（`HideVersionEpoch` / `HideVersionVPrefix` / `AutoSortUpdates`，随 `ckan-ui.json` 落盘）；模组列版本号走 `VersionDisplayFormat.Format`，可升级项在按名称排序时整体置顶。用例：`ModsVersionDisplayTests`（8 例）、`VersionDisplayFormatTests` |
| F-55 | 帮助与关于（链接、关于框、单实例互斥、ckan:// 协议、错误/崩溃对话框） | UI-36, ARC-04, ARC-05, UI-09 | `src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs:30`, `src/CkanUI.App/AboutInfoFactory.cs:12`, `src/CkanUI.Platform/SingleInstance/SingleInstanceGuard.cs:20`, `src/CkanUI.ViewModels/Dialogs/ErrorDialogViewModel.cs:13` | 已实现 | ① 单实例互斥与 `ckan://` 协议唤醒（`SingleInstanceGuard` + `CkanUri`）；② 错误/崩溃/诊断对话框；③ 关于框（外壳左栏底部「关于」）：版本矩阵四行（界面/核心库/运行时/操作系统）、三条官方链接、GPL-3.0 声明，外链走 Avalonia `Launcher`，通道缺失或失败在框内提示不抛。单测 `AboutDialogTests`（9 例） |

---

## 2. 统计与分析

### 2.1 状态分类汇总

| 状态 | 数量 | 占比 | 包含特性的主要范围 |
|---|---|---|---|
| **已实现** | **49** | 89.1% | 基础与导航（F-01..F-04, F-07..F-12, F-25）、兼容版本与偏好主机及过滤器（F-14..F-16）、实例与启动命令（F-05, F-06）、高级搜索抽屉与 18 项字段表单（F-20..F-22）、Label 管理与 10 项属性编辑（F-23, F-24）、模组详情多页签与版本列表（F-26, F-30）、右键菜单与全量更新（F-31, F-32）、关系树增强与九色图例反向展开（F-28）、游戏运行探测（F-33）、推荐审计与内容预览（F-29, F-36）、下载错误处置与缓存完整管理（F-38, F-50）、Modpack 导入导出与多格式列表导出与编辑器（F-39, F-45, F-46）、高级去重/未管理/残留清理/安装历史/下载统计（F-41..F-44, F-47）、设置项与版本显示（F-48, F-54）、系统托盘/关于框/插件（F-51, F-55, F-17） |
| **部分** | **6** | 10.9% | 稳定版容忍度（F-13）、推荐与虚拟模组对话框流程接入（F-34, F-35）、安装执行等待与重试细节（F-37）、ZIP 灌缓存细化（F-40）、自建更新（F-52） |
| **未开始** | **0** | 0.0% | 无（F-01..F-55 全部进入已实现或部分） |
| **总计** | **55** | 100.0% | 覆盖全特性清单（A 9 + B 8 + C 17 + D 14 + E 7）全部条目 |

---

## 3. 下一步优先级建议（按 12 篇 §4 缺口清单）

基于 [12 全特性对齐清单](../plan/12-feature-parity.md) §4 缺口分析及当前源码实现底座，建议按以下四个波次推进剩余特性的闭环落地：

### 第一优先级：把「部分」项收口为真实闭环（已无未开始项）
1. **F-31 / F-37（安装执行链）**：全量更新与应用交接（F-32）已落地；真实下载与安装已通（`DownloadService` + `HistoryRecordingInstaller`），剩余逐项重试、全屏等待页与失败回滚细节。
2. **F-38（下载失败处置，UI-35）**：在 `DownloadFailureClassifier` 之上补逐项 Retry?/Skip? 对话框。
3. **F-34 / F-35 / F-36（模组对话框接线）**：三个对话框 VM 与视图已就绪，缺在模组页安装流程与 `CkanInstallerBackend` 抛 `TooManyModsProvideKraken` 处调用。
4. **F-12（仓库刷新）**：补强制重下（跳过 ETag）与「暂停自动刷新」开关。

### 第二优先级：设置与仓库可视化（M3 里程碑）
1. **F-13..F-16（容忍度、兼容版本、首选主机、安装过滤器）**：把文本行配置升级为可视化排序与规则编辑器。
2. **F-20..F-23（图形化筛选抽屉）**：语法解析器（`IModSearchParser`）已全量接线，缺 18 字段的图形化面板。
3. **F-50（缓存面板收尾）**：补「打开缓存目录」与「重置上限」两个入口。
4. **F-24（标签组编辑对话框）**：标签读写与筛选已落地，缺 UI-24b 的组编辑对话框。

### 第三优先级：工具族收口（M4 里程碑）
1. **F-29（内容页，UI-03）**：未缓存提示、一键下载与 ZIP 内文件树预览。
2. **F-46（modpack 编辑器）**：导入导出已通，缺完整元数据编辑界面。
3. **F-47（下载统计图表）**：需先做图表库选型（SP-14）。
4. **F-51（系统托盘）**：托盘托管、最小化到托盘与右键菜单。

### 第四优先级：全特性验收收口（M5' 里程碑）
1. **F-52（自建自动更新）**：更新检查器、新版本弹窗与二进制替换。
2. **F-45（已装列表导出）**：不可回导格式。
3. 全 55 条按 [07 测试策略](../plan/07-testing.md) TST-05 验收矩阵逐条复验。
