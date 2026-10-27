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
| F-05 | 启动游戏（多命令选择） | UI-28, INT-04 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:717`, `src/CkanUI.ViewModels/Pages/Instances/IInstanceShellLauncher.cs:15` | 部分 | 基础启动命令及错误提示已实现；多启动命令选择下拉与 E6 资源释放状态机待 UI-28 / E6 接入 |
| F-06 | 游戏命令行编辑（列表、重置、增、接受、取消） | UI-28 | — | 未开始 | 待 UI-28 实现命令行编辑器对话框，验证启动参数列表的增删改与持久化 |
| F-07 | 打开游戏目录（菜单 + 托盘） | UI-01 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:680`, `src/CkanUI.ViewModels/Pages/Instances/IInstanceShellLauncher.cs:14` | 已实现 | 验证通过系统外壳接口启动文件管理器定位游戏根目录 |
| F-08 | 实例锁定/注册表损坏的处置（删锁、回滚、提示升级 CKAN） | INT-04, UI-09 | `src/CkanUI.Services/Instances/InstanceService.cs:45`, `src/CkanUI.Services/Registry/RegistryService.cs:37`, `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:305` | 已实现 | 单测验证锁定实例拒绝切换并提示不可重试错误，损坏注册表隔离不阻断其它实例枚举 |
| F-09 | 切换实例后按需刷新（InstanceChanged → 重载列表） | INT-04, VM-04 | `src/CkanUI.Services/Instances/InstanceService.cs:25,91`, `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs:176` | 已实现 | 单测验证订阅 Core `InstanceChanged` 事件后异步派发并整体刷新实例行投影 |
| F-10 | 元数据仓库管理（增、删、上移、下移、优先级、名称/URL 编辑、官方仓库预设） | UI-06, INT-06 | `src/CkanUI.Services/Repositories/RepositoryService.cs:48,57`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:320` | 部分 | 已实现快照加载与联网刷新；元数据仓库增删、优先级拖动排序与预设弹窗待 UI-06 / INT-06 接入 |
| F-11 | 认证令牌（host + token，增删，脱敏显示） | UI-34 | `src/CkanUI.Services/Settings/SettingsService.cs:188`, `src/CkanUI.ViewModels/Pages/Settings/AuthTokenEditorViewModel.cs:13`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:341` | 已实现 | 单测 `SettingsServiceTests` 验证令牌增删改、读写脱敏掩码与敏感字符串不入日志 |
| F-12 | 仓库刷新（手动、启动时、定时、暂停、Ctrl 强制重下） | UI-06, INT-06 | `src/CkanUI.Services/Repositories/RepositoryService.cs:57`, `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:734`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:323` | 部分 | 手动刷新命令与自动刷新周期配置已实现；Ctrl 跳过 ETag 强制刷新与暂停刷新开关待 UI-06 补充 |
| F-13 | 稳定性容忍度（全局 + 单 mod 覆盖） | UI-07, UI-29 | `src/CkanUI.Services/Models/GUIMod.cs:171`, `src/CkanUI.Services/Models/ModIndexOperations.cs:266` | 部分 | 模型层已支持容忍度冲突判定；设置页全局容忍度与详情页单模组覆盖 UI 待 UI-07 / UI-29 接入 |
| F-14 | 兼容游戏版本（当前版本 + 追加版本列表、通配 1.12→1.12.x、风险警示） | UI-29 | `src/CkanUI.Services/Models/GUIMod.cs:260`, `src/CkanUI.ViewModels/Pages/Mods/ModsCompatibilityFilter.cs:8` | 部分 | 已支持基于主次版本的兼容性过滤计算；追加自定义兼容版本与通配符编辑弹窗待 UI-29 接入 |
| F-15 | 首选下载主机（可用按热度 / 首选按优先级，拖动排序） | UI-30 | `src/CkanUI.Services/Settings/SettingsService.cs:182`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:333` | 部分 | 已在设置页支持首选主机文本列表读写持久化；热度排序与可视化拖拽弹窗待 UI-30 接入 |
| F-16 | 安装过滤器（全局 + 实例级，仅影响后续安装） | UI-31 | `src/CkanUI.Services/Settings/SettingsService.cs:195`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:402,410` | 部分 | 已支持 KSP/KSP2 全局规则持久化与配置校验；实例级覆盖与高级过滤规则编辑器待 UI-31 接入 |
| F-17 | 插件系统（激活/停用/重载/卸载/新增，Active 与 Dormant 两栏） | UI-33 | — | 未开始 | 待 UI-33 实现 `ICKANUIPlugin` 接口、反射加载与插件管理视图后验收 |
| F-18 | Mod 列表 15 列（Installed、Auto-installed、Update、Replace、Name、Author 等） | UI-02, VM-01 | `src/CkanUI.ViewModels/Pages/Mods/ModsRowProjection.cs:16`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowView.cs:13`, `src/CkanUI.App/Views/ModsPageView.axaml:160` | 已实现 | 单测 `ModsRowProjectionTests` 验证 15 列数据投影与格式化，UI DataGrid 列头绑定 |
| F-19 | 11 种状态过滤器 + 排序（点击列头，含 Update 列自动排序选项） | UI-02, VM-01 | `src/CkanUI.Services/Models/GUIModFilter.cs:3`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowFilter.cs:10`, `src/CkanUI.ViewModels/Pages/Mods/ModsRowSorter.cs:10` | 已实现 | 单测 `ModListFilterTests` 与 `ModsRowSorterTests` 覆盖 11 种过滤枚举与列头多字段升降序 |
| F-20 | 组合搜索面板（Search / Or 两组，逐字段可选） | UI-14, MOD-01 | `src/CkanUI.Services/Models/ModSearch.cs:13`, `src/CkanUI.ViewModels/Pages/Mods/ModsQuery.cs:12` | 部分 | `ModSearch` 与 `ModsQuery` 支持 Search/Or 多条件语法解析；组合搜索独立抽屉/面板待 UI-14 落地 |
| F-21 | 搜索字段 18 项（Name、Author、Description、Depends on、Conflicts 等 18 项） | UI-14, MOD-01 | `src/CkanUI.ViewModels/Pages/Mods/ModsQuery.cs:22`, `src/CkanUI.Services/Models/ModSearch.cs:27` | 部分 | 单测 `ModSearchTests` 验证多字段联合过滤；18 项独立图形化勾选表单待 UI-14 落地 |
| F-22 | 标签（Tags）与标签徽章、点击显隐 | UI-02 | `src/CkanUI.ViewModels/Pages/Mods/ModsQuery.cs:28`, `src/CkanUI.App/Views/ModsPageView.axaml:115` | 部分 | 已支持 Tag 过滤词与行内标签文本展示；标签徽章点击即时显隐过滤待 UI-02 工具栏接入 |
| F-23 | Label 过滤与编辑入口 | UI-02, UI-24b | `src/CkanUI.Services/Models/ModSearch.cs:19` | 部分 | 模型层定义 Label 检索条件；Label 工具栏下拉与编辑弹窗入口待 UI-02 / UI-24b 接入 |
| F-24 | Label 属性（名称、背景色、隐藏于其它过滤器、更新/安装通知、不升级等 10 项） | UI-24b | — | 未开始 | 待 UI-24b 实现 `EditLabelsDialog`，验证标签 10 项属性编辑与落盘 |
| F-25 | 前进/后退导航（选中历史） | UI-02 | — | 未开始 | 待 UI-02 完善导航历史堆栈（选中模组历史的前进/后退动作） |
| F-26 | 详情四页：元数据 / 关系 / 内容 / 版本 | UI-03 | `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:14`, `src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml:1` | 部分 | 已实现元数据与关系两大部分详情投影与视图；内容预览树与独立版本页签待 UI-03 完善 |
| F-27 | 元数据页（Identifier、Replaced by、Max game ver.、Author、Downloads 等） | UI-03 | `src/CkanUI.ViewModels/Dialogs/ModDetailsSnapshot.cs:4`, `src/CkanUI.ViewModels/Dialogs/ModDetailsFormatter.cs:10` | 已实现 | 视图模型单测验证元数据各字段提取、格式化展示与空状态回退 |
| F-28 | 关系树（九色图例、反向关系、双击跳转） | UI-18 | `src/CkanUI.ViewModels/Dialogs/ModRelationKind.cs:4`, `src/CkanUI.ViewModels/Dialogs/ModRelationItem.cs:7`, `src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs:149` | 部分 | 已实现关系分类、多项依赖数据绑定与显示；九色图例树形控件与双击跳转待 UI-18 完善 |
| F-29 | 内容页（未缓存时提示、下载、打开 ZIP 预览文件树） | UI-03 | — | 未开始 | 待 UI-03 内容页完成未缓存提示、一键下载与 ZIP 压缩包内文件树预览 |
| F-30 | 版本页（全部版本、兼容/已装/不稳定标记、发布日期） | UI-03 | `src/CkanUI.Services/Models/GUIMod.cs:23`, `src/CkanUI.ViewModels/Pages/Mods/ModsFormatter.cs:15` | 部分 | 数据层具备版本枚举与兼容判定；详情面板独立版本页签与标记展示待 UI-03 完善 |
| F-31 | 右键动作（重装、下载到缓存、从缓存清除、Labels 子菜单） | UI-02, INT-07 | `src/CkanUI.Services/ChangeSets/ModChange.cs:24` | 部分 | 变更集底层具备重装/下载语义；模组列表右键上下文菜单（ContextMenu）待 UI-02 接入 |
| F-32 | 全量更新（Update all）、应用（Apply） | UI-02, UI-04 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs:846`, `src/CkanUI.App/Views/ModsPageView.axaml.cs:78` | 部分 | 已实现应用变更命令入口与状态栏提示；全量更新按钮与安装器完整调度待 UI-02 / UI-04 接入 |
| F-33 | 变更集页（Mod / Change / Reasons / Skip? 四列、清空、应用、游戏运行警告） | UI-04, VM-02 | `src/CkanUI.Services.Abstractions/Dtos/ChangeSetPreview.cs:5`, `src/CkanUI.Services/ChangeSets/ModChange.cs:9`, `src/CkanUI.ViewModels/Pages/Mods/ModsChangePreviewBuilder.cs:10` | 部分 | 已实现变更集预览构建器与变更类型定义；独立四列变更集确认页面待 UI-04 / VM-02 接入 |
| F-34 | 提供者选择（虚拟 mod 多候选单选） | UI-16 | — | 未开始 | 待 UI-16 实现虚拟 mod 多提供者单选对话框，验证候选解析与变更集合流 |
| F-35 | 推荐/建议/被支持选择（三组、Check All / Uncheck All / Always uncheck all 等） | UI-15, INT-07 | — | 未开始 | 待 UI-15 实现关联选择对话框，验证三组勾选逻辑与“总是取消全选”持久化 |
| F-36 | 推荐审计（遍历已装 mod 的推荐与建议） | UI-17 | — | 未开始 | 待 UI-17 实现推荐审计工具，验证批量扫描已安装模组推荐与一键采纳 |
| F-37 | 安装/卸载/升级执行（进度、取消、重试、状态日志） | UI-08, INT-08 | `src/CkanUI.Services/Installers/CkanInstallerBackend.cs:21`, `src/CkanUI.Services/Operations/OperationQueue.cs:10` | 部分 | 底层具备安装/卸载执行后端与异步操作队列；全屏任务等待日志页（WaitTabPage）待 UI-08 落地 |
| F-38 | 下载失败处置（逐项 Retry?/Skip?、错误文本、复制到剪贴板） | UI-35 | — | 未开始 | 待 UI-35 实现下载失败对话框，验证逐项重试、跳过与错误复制剪贴板 |
| F-39 | 从 .ckan 安装（多选文件） | UI-20 | — | 未开始 | 待 UI-20 实现多选 .ckan 元数据文件解析与批量安装管道 |
| F-40 | 导入已下载 mods（ZIP → 缓存） | UI-20 | — | 未开始 | 待 UI-20 实现本地模组 ZIP 导入下载缓存并建立索引 |
| F-41 | 去重（跨实例扫描并共享可共享文件） | UI-21 | — | 未开始 | 待 UI-21 / INT-09 实现跨实例文件哈希扫描与硬链接共享去重 |
| F-42 | 未管理文件（树、刷新、全展开/全折叠/重置、在文件夹中显示、删除） | UI-10 | `src/CkanUI.Services.Abstractions/IUnmanagedService.cs:8` | 未开始 | 仅有抽象服务契约定义；未管理文件树扫描、定位与删除页面待 UI-10 / INT-09 实现 |
| F-43 | 残留目录清理（目录树 + 内容预览、打开目录、删除勾选、全部保留） | UI-22 | `src/CkanUI.Services/Installers/CkanInstallerBackend.cs:179` | 部分 | 卸载逻辑中已记录配置残留目录（`possibleConfigOnlyDirs`）；清理对话框与文件树待 UI-22 接入 |
| F-44 | 安装历史（时间戳列表、已装/未装分组、从历史重装） | UI-23 | — | 未开始 | 待 UI-23 / INT-08 实现时间戳历史记录展示与旧状态一键回滚重装 |
| F-45 | 导出：已装列表（不可回导）／modpack（可回导） | UI-24 | — | 未开始 | 待 UI-24 / INT-10 实现已装列表与 modpack 双格式导出 |
| F-46 | modpack 编辑器（标识/名称/摘要/作者/版本/关系单选等） | UI-24 | — | 未开始 | 待 UI-24 实现 modpack 完整元数据编辑界面与关系分组单选 |
| F-47 | 下载统计（饼图 + 表格） | UI-25 | — | 未开始 | 待 SP-14 图表库验证并在 UI-25 实现下载量饼图与排序表格 |
| F-48 | 游戏时长（实例名 / 小时数，F2 就地编辑） | UI-10 | — | 未开始 | 待 UI-10 实现时长统计表格与 F2 就地编辑保存功能 |
| F-49 | 设置总表（仓库、令牌、缓存、自动更新、行为、外观、高级等分组） | UI-07 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:264`, `src/CkanUI.App/Views/SettingsPageView.axaml:1` | 已实现 | 设置页 7 组分类配置双向绑定已就绪，支持改一项写一项与读回校验 |
| F-50 | 下载缓存管理（文件数/容量、路径编辑、上限 MiB、Purge、Reset、打开） | UI-19 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:296`, `src/CkanUI.Services/Settings/SettingsService.cs:170` | 部分 | 设置页支持缓存路径与容量上限；缓存文件数统计、Purge 清理与 Reset 弹窗待 UI-19 接入 |
| F-51 | 托盘图标（启用/最小化到托盘；7 项右键菜单） | UI-26 | — | 未开始 | 待 UI-26 实现托盘图标托管、窗口隐藏/恢复与 7 项快捷菜单 |
| F-52 | 自动更新（启动时检查、dev builds、检查/安装、新版本对话框） | UI-27 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:270` | 部分 | 设置页支持开发版更新通道开关；更新检查器、新版本弹窗与二进制替换待 UI-27 / PKG 接入 |
| F-53 | 语言与本地化（语言选择需重启；多语 resx/json 支持） | UI-13 | `src/CkanUI.Localization/Localizer.cs:15`, `src/CkanUI.Localization/LanguagePackLoader.cs:11`, `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs:367` | 已实现 | 单测 `LocalizationTests` 覆盖本地化加载、多语词包回退链及设置页语言切换与持久化 |
| F-54 | 显示格式（隐藏 epoch、隐藏 "v"、自动排序） | UI-07 | — | 未开始 | 待 UI-07 在设置页通用/外观组中增加 Epoch、"v" 前缀过滤与更新自动排序项 |
| F-55 | 帮助与关于（链接、关于框、单实例互斥、ckan:// 协议、错误/崩溃对话框） | UI-36, ARC-04, ARC-05, UI-09 | `src/CkanUI.Platform/SingleInstance/SingleInstanceGuard.cs:20`, `src/CkanUI.Platform/SingleInstance/CkanUri.cs:16`, `src/CkanUI.ViewModels/Dialogs/ErrorDialogViewModel.cs:13` | 部分 | 单实例互斥、`ckan://` 协议唤醒与错误/崩溃/诊断对话框已就绪；关于框与外部支持链接待 UI-36 落地 |

---

## 2. 统计与分析

### 2.1 状态分类汇总

| 状态 | 数量 | 占比 | 包含特性的主要范围 |
|---|---|---|---|
| **已实现** | **13** | 23.6% | 实例基础管理（F-01..F-04, F-07..F-09）、认证令牌（F-11）、模组列表 15 列与 11 种过滤器（F-18, F-19）、元数据详情（F-27）、设置主页（F-49）、多语本地化（F-53） |
| **部分** | **22** | 40.0% | 游戏启动与刷新（F-05, F-10, F-12）、版本与容忍度（F-13..F-16）、组合搜索与关系（F-20..F-23, F-26, F-28, F-30）、变更集与安装底层（F-31..F-33, F-37, F-43）、缓存设置与平台（F-50, F-52, F-55） |
| **未开始** | **20** | 36.4% | 命令行编辑器（F-06）、插件系统（F-17）、Label 属性（F-24）、导航历史（F-25）、内容预览（F-29）、推荐与虚拟模组对话框（F-34..F-36）、下载失败（F-38）、导入导出与工具族（F-39..F-42, F-44..F-48）、托盘与格式（F-51, F-54） |
| **总计** | **55** | 100.0% | 覆盖全特性清单（A 9 + B 8 + C 17 + D 14 + E 7）全部条目 |

---

## 3. 下一步优先级建议（按 12 篇 §4 缺口清单）

基于 [12 全特性对齐清单](../plan/12-feature-parity.md) §4 缺口分析及当前源码实现底座，建议按以下四个波次推进剩余特性的闭环落地：

### 第一优先级：核心安装与决策闭环（M2 里程碑，阻断正常安装流）
1. **F-34（提供者选择对话框，UI-16）**：虚拟模组冲突或多实现时阻断变更集生成，属于基础安装核心链路。
2. **F-35（推荐/建议/支持选择对话框，UI-15）**：安装前模组推荐选择，含全选/取消及“总是取消”配置持久化。
3. **F-33（完整变更集确认视图，UI-04 / VM-02）**：将现有的估算预览构建器升级为可交互确认、可取消变更的独立确认页面。
4. **F-37（执行等待页，UI-08）**：接入 `WaitTabPage`，呈现安装日志流、进度条、取消与重试按钮。
5. **F-38（下载失败处置对话框，UI-35）**：提供网络异常断点时的重试、跳过与错误复制能力。

### 第二优先级：仓库、实例与设置周边补全（M3 里程碑）
1. **F-10（仓库增删与优先级拖拽，UI-06）**：补齐元数据仓库管理完整弹窗与官方预设列表。
2. **F-14、F-13（兼容游戏版本与容忍度，UI-29）**：提供通配符版本兼容列表编辑器与模组级覆盖。
3. **F-50（下载缓存管理面板，UI-19）**：在标量配置基础上，实现缓存容量监控、Purge to limit / Purge all 清理弹窗。
4. **F-15、F-16（首选主机拖拽排序与安装过滤器编辑，UI-30 / UI-31）**：将文本行配置升级为直观的可视化排序与规则编辑器。
5. **F-20、F-21（组合搜索面板，UI-14）**：将底层 `ModSearch` 表达式包装为 18 字段的图形化筛选抽屉。

### 第三优先级：数据分析、文件与工具族（M4 里程碑）
1. **F-28（关系树可视化，UI-18）**：在现有关系列表基础上实现 9 类着色图例树控件与双击联动。
2. **F-43、F-42（残留目录清理与未管理文件接管，UI-22 / UI-10）**：释放磁盘空间，清理卸载后残留配置。
3. **F-39、F-40、F-45、F-46（.ckan 导入、模组包导入与 modpack 编辑器，UI-20 / UI-24）**：支持模组配置分享与离线安装。
4. **F-44、F-47、F-48（安装历史重装、下载统计图表、游戏时长，UI-23 / UI-25 / UI-10）**：需先由 SP-14 完成图表库选型验证。
5. **F-41、F-24、F-51（跨实例去重、Label 属性定制、系统托盘，UI-21 / UI-24b / UI-26）**。

### 第四优先级：全特性收口与外部扩展（M5' 里程碑）
1. **F-52（自建自动更新，UI-27）**：配合 PKG-02 / PKG-08 实现更新包拉取与后台二进制静默替换。
2. **F-17（插件系统，UI-33）**：定义 `ICKANUIPlugin` 契约，支持外部加载与启用/停用。
3. **F-55、F-06、F-25、F-54（帮助外链、启动命令行编辑、选择历史前进后退、显示格式微调）**：完成 TST-05 验收矩阵 55 项全绿收口。
