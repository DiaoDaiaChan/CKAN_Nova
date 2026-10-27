# 页面与对话框实现设计说明（Pages & Dialogs Architecture）

> 真相源与对应规格：
> - 规格清单：`docs/plan/03-ui-spec.md`（§1 架构、§2 页面清单、§3 ModsView、§4 ChangesView、§5 InstancesView、§6 RepositoriesView、§7 SettingsView、§8 对话框、§10 空态与错误态、§13 全特性对照）
> - 工期与任务：`docs/plan/06-wbs-milestones.md`（UI-01 ~ UI-13，UI-14 ~ UI-26）
> - 服务抽象：`src/CkanUI.Services.Abstractions/` 与 `docs/design/service-contracts.md`
> - 代码现状：`src/CkanUI.ViewModels/Pages/**`、`src/CkanUI.ViewModels/Dialogs/**`

---

## 1. 架构总览与分层规则

新版 CKAN UI 基于 Avalonia 12 + CommunityToolkit.Mvvm 实现 MVVM 架构。各页面视图模型（PageViewModel）与对话框视图模型（DialogViewModelBase）遵循严格的工程与架构铁律：

1. **依赖单向流动**：`CkanUI.ViewModels` 只引用 `CkanUI.Services.Abstractions` 与 DTO，永不引用 `CkanUI.Services`，更不直接引用 Core (`CKAN.*`)。
2. **零活对象持有**：VM 永不持有 Core 的活对象（如 `CkanModule`、`GameInstance`），仅持不可变 DTO 或通过唯一标识符（如 `Identifier`、`Name`）按需查询快照。
3. **快照整体替换**：为规避 3000+ 项集合频繁触发 `CollectionChanged` 引发界面掉帧（04 篇 §2.1），数据更新一律采用整体替换，不做细粒度增量刷新。
4. **异常全收口**：VM 内所有异步操作与服务调用均做 `try/catch` 包裹，业务失败解析为 `OperationResult<T>` 或更新行内/状态栏文案，全局未处理异常统一转交 `IErrorReporter`，严禁异常穿透导致 UI 线程崩溃（05 篇）。
5. **事件已在 UI 线程**：服务层契约已通过 `IDispatcher` 封送所有事件，VM 订阅时无需二次调度。

---

## 2. 页面实现说明

### 2.1 模组管理页 (ModsPageViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs`)
- **规格对应**：03 篇 §3 (V-01)；06 篇 UI-02 (模组列表)、UI-03 (详情面板)、VM-01 (列表流水线)；PERF-01

#### 2.1.1 视图模型职责
- 承载模组列表主从布局（左列表 + 右详情 + 顶部搜索过滤 + 底部状态栏）。
- 管理搜索输入防抖（80ms）与复合条件过滤流水线（`ModsListPipeline`）。
- 维护模组勾选状态集合（`_selected`），与变更集计算联动。
- 驱动模组选中行变化并联动右侧详情面板刷新。

#### 2.1.2 数据来源与契约
| 依赖服务 | 接口方法 / 事件 | 输入/输出 DTO | 用途 |
|---|---|---|---|
| `IRepositoryService` | `Current` 快照、`Updated` 事件、`RefreshAsync()` | `RepositorySnapshot` | 获取远端可用模组池、触发仓库刷新 |
| `IRegistryService` | `Current` 快照、`Changed` 事件 | `RegistrySnapshot`、`InstalledModuleInfo` | 获取本机已装模组信息与状态 |
| `IInstanceService` | `Current` 快照、`CurrentChanged` 事件 | `InstanceInfo` | 监听当前生效实例变化，触发整页数据重载 |
| `IChangeSetService` | `BuildAsync()` (可选) | `ChangeRequest` → `ChangeSetPreview` | 实时预览勾选引发的依赖与变更集 |
| `IErrorReporter` | `Report()` | `ErrorEntry` | 记录并上报查询/操作中的异常 |

#### 2.1.3 命令与状态定义
| 属性/命令 | 类型 | 职责说明 |
|---|---|---|
| `SearchText` | `string` (Observable) | 搜索框输入文本，带 80ms 防抖调度 |
| `VisibleRows` | `IReadOnlyList<ModsRowView>` | 经过搜索、过滤、排序后的可见行（整体替换） |
| `SelectedRow` | `ModsRowView?` | 当前光标聚焦行，驱动右侧详情 |
| `InstallFilter` / `CompatibilityFilter` | 枚举 (Observable) | 安装状态与兼容性过滤开关 |
| `StatusText` | `string` | 状态栏汇总信息（总数、过滤后数、选中数、变更数） |
| `ToggleRowSelectionCommand` | `IRelayCommand<string>` | 切换指定标识模组的加入/移出变更集勾选 |
| `ApplyChangesCommand` | `IAsyncRelayCommand` | 导航至变更集页面或提交变更 |
| `RefreshRepositoriesCommand` | `IAsyncRelayCommand` | 重新拉取所有仓库并重建可见列表 |

#### 2.1.4 空状态与错误收口
- **无实例**：状态栏提示「当前未选择有效 KSP 实例」，列表禁用，引导至实例页。
- **仓库未加载/空索引**：可见列表为空，中央展示引导卡片「尚未加载模组索引，[立即刷新]」。
- **搜索无匹配**：列表区域居中展示「没有匹配的模组」，并展示当前生效的过滤 Chip 及一键重置按钮。
- **服务异常**：所有仓库刷新、变更集计算包裹在 `try/catch` 中，异常格式化为行内状态栏红字，严重异常调用 `_errors.Report()` 记录，绝不打崩界面。

---

### 2.2 变更集确认与执行页 (ChangesPageViewModel)
- **实现状态**：✅ 已实现（`src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs`；含冲突横幅、警告横幅、游戏运行中横幅与执行状态机）
- **规格对应**：03 篇 §4 (V-02)；06 篇 UI-04 (变更集页)、VM-02；02 篇 §4

#### 2.2.1 视图模型职责
- 展示用户选择、依赖解析、自动带出的最终变更明细列表（安装、升级、卸载、替换）。
- 识别并可视化不可协调的依赖冲突，明确警示并阻断执行。
- 驱动执行状态机（`Idle` → `Confirming` → `Downloading` → `Installing` → `Completed`），提供安装阶段可安全取消/回滚的控制能力。

#### 2.2.2 数据来源与契约
| 依赖服务 | 接口方法 / 事件 | 输入/输出 DTO | 用途 |
|---|---|---|---|
| `IChangeSetService` | `BuildAsync()` | `ChangeRequest[]` → `ChangeSetPreview` | 获取最终解析好的变更集项目集合与冲突项 |
| `IInstallerService` | `ApplyAsync()` | `ChangeSetPreview`、`IProgress<OperationProgress>` → `OperationResult<InstallReport>` | 调用安装引擎执行事务安装 |
| `IOperationQueue` | `Enqueue()`、`Cancel()` | `OperationProgress` | 将执行步骤纳入全局队列，供进度浮层监控 |
| `IDialogService` | `ConfirmAsync()` | 确认文案与选项 | 弹窗二次确认高危操作（如级联卸载） |

#### 2.2.3 命令与状态定义
| 属性/命令 | 类型 | 职责说明 |
|---|---|---|
| `Preview` | `ChangeSetPreview?` | 当前变更集只读投影（包含变更列表、冲突列表、下载总字节） |
| `SummaryText` | `string` | 摘要汇总文案（如「安装 3 个，升级 1 个，预计下载 45.2 MB」） |
| `HasConflicts` | `bool` | 是否存在冲突，为 true 时强制禁用执行按钮 |
| `IsExecuting` | `bool` | 是否处于下载/安装事务中 |
| `ExecutionProgress` | `double` (0-100) | 当前事务总体百分比 |
| `StartExecutionCommand` | `IAsyncRelayCommand` | 校验通过后，启动安装管线 |
| `CancelExecutionCommand` | `IRelayCommand` | 取消执行：下载中立即停止，安装中等待当前事务回滚 |
| `RemoveChangeItemCommand` | `IRelayCommand<string>` | 从变更集中移除某项并重算解析结果 |

#### 2.2.4 空状态与错误收口
- **空变更集**：当 `Preview.Items` 为空时，展示「暂无待应用的模组变更，请在模组管理页选择需要安装或移除的模组」，主按钮置灰。
- **存在冲突**：顶部以危险警示色展示冲突面板，列出冲突模组对与互斥原因，禁用 [开始] 按钮。
- **游戏正在运行（12 篇 F-33）**：页面激活时调 `IInstanceService.IsGameRunning()`（判据 = 有进程的可执行文件落在实例游戏目录之下，探测失败按没在跑处理）。命中时顶部给黄框横幅「游戏正在运行」+ 说明，主按钮 `CanApply` 变为 false，另给「仍要执行」（`ApplyAnywayCommand`，只在横幅可见时可点）。用户点过之后本次页面生命周期内不再拦。
- **执行失败**：事务失败时接收 `InstallReport`，呈现失败模组及原因列表；提供「复制错误日志」与「重试失败项」按钮，Core 自动触发的事务回滚保证游戏目录无半装脏文件（08 篇 R-03）。

---

### 2.3 游戏实例管理页 (InstancesPageViewModel)
- **实现状态**：✅ 已实现（`src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs`）；重命名走 `IInstanceService.RenameAsync`，启动命令编辑见 §2.8，启动后资源释放见 §2.3.5
- **规格对应**：03 篇 §5 (V-03)、§8 (D-06 实例编辑)、§8 (D-09 首次向导)；06 篇 UI-05；02 篇 §5 (实例切换生命周期)

#### 2.3.1 视图模型职责
- 展示所有已登记的 KSP 实例卡片列表，标明当前生效实例、默认实例、游戏版本与锁定状态。
- 驱动「切换实例」生命周期：释放旧注册表锁 → 设为新实例 → 独占锁定检测 → 重新加载注册表与依赖服务。
- 提供实例的新增（自动探测 / 手动选择目录）、克隆、编辑名称、删除（仅移出索引，不动文件）及在文件管理器中打开。

#### 2.3.2 数据来源与契约
| 依赖服务 | 接口方法 / 事件 | 输入/输出 DTO | 用途 |
|---|---|---|---|
| `IInstanceService` | `Instances`、`Current`、`RefreshAsync()`、`SwitchAsync()`、`AddAsync()`、`CloneAsync()`、`RemoveAsync()` | `InstanceInfo` | 实例查询与全生命周期修改 |
| `IDialogService` | `ConfirmAsync()`、`ShowErrorAsync()` | 标题与文案 | 实例删除前二次确认、被锁警告 |
| `IOperationQueue` | `Operations`、`Cancel()` | `OperationProgress` | 切换实例前检查并中止进行中的安装/写入任务 |

#### 2.3.3 命令与状态定义
| 属性/命令 | 类型 | 职责说明 |
|---|---|---|
| `Instances` | `ObservableCollection<InstanceInfo>` | 实例列表视图集合 |
| `CurrentInstance` | `InstanceInfo?` | 当前选中的活动实例 |
| `IsSwitching` | `bool` | 切换中遮罩状态，为 true 时界面阻断所有操作 |
| `SwitchInstanceCommand` | `IAsyncRelayCommand<string>` | 切换至指定实例，带完整事务回滚机制 |
| `AutoDetectInstancesCommand` | `IAsyncRelayCommand` | 扫描 Steam 库及默认路径发现新实例并批量添加 |
| `AddManualInstanceCommand` | `IAsyncRelayCommand` | 弹出目录选取器并验证 `buildID.txt` / KSP 标志结构 |
| `CloneInstanceCommand` | `IAsyncRelayCommand<string>` | 克隆指定实例副本 |
| `RemoveInstanceCommand` | `IAsyncRelayCommand<string>` | 移出实例登记（需确认对话框） |

#### 2.3.4 空状态与错误收口
- **无实例（首次启动/全删除）**：展示全屏向导卡片（D-09 首次向导）：「未检测到 Kerbal Space Program 游戏实例」，提供 [自动探测 Steam/常见目录] 与 [手动浏览路径] 两个主按钮。
- **实例锁定冲突**：切换时若目标实例被其他 CKAN 进程独占，捕捉锁定失败，弹窗阻断并提示「该实例正被另一个进程使用，无法切换」，回滚至原实例。
- **路径失效/损坏**：卡片上置灰标红「目录不可访问」，提供「重新定位」与「移除」选项。

#### 2.3.5 启动后的分层资源释放（18 篇 E6）
- **实现位置**：编排在 `src/CkanUI.Services/Lifecycle/ResourceReleaseService.cs`，时机在 `src/CkanUI.ViewModels/Pages/Instances/GameLaunchReleaseCoordinator.cs`。
- **为什么做**：KSP 与 CKAN 同机常驻会抢内存，而 Core 没有「清缓存」API——能真正放掉的只有「仓库快照 + `RegistryManager.DisposeAll()`」，所以把它做成**可逆**的释放：`RestoreAsync` 用 `IRegistryService.OpenAsync(当前实例)` + `IRepositoryService.LoadCachedAsync()` 重新打开。
- **五层**：L0 视图参与方（`IViewResourceParticipant`）/ L1 缓存参与方（`IAppCacheParticipant`）/ L2 注册表锁 + 仓库快照 / L3 压缩式 GC / L4 工作集归还（本版本返回 `Unsupported`，报告记 Skipped 不算失败）。
- **四档**（`UiSettings.ReleaseTier`，默认 `Balanced`）：`Off` 连计时器都不起；`Conservative` 只跑 L0+L1；`Balanced` 补 L2；`Aggressive` 补 L3/L4。
- **时机**：启动成功即排程，T3 = 0s（保守）、T4 = +10s（均衡）、T5 = +30s（激进）；服务按「已成功的层」幂等，每层只跑一次。
- **收口**：游戏退出（`IInstanceLaunchExitSignal.GameExited`，由 App 侧 `ShellInstanceLauncher` 保留句柄后广播）或离开页面 → 取消后续层并 `RestoreAsync`，失败走错误栏且可重试。每层独立成败，失败只记 Warn 不阻断后续层。

---

### 2.4 设置页 (SettingsPageViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs`)
- **规格对应**：03 篇 §7 (V-05)；06 篇 UI-07；20 篇 §1 ~ §10；02 篇 §3 (逐项保存与读回校验)

#### 2.4.1 视图模型职责
- 承载七大设置分组：通用、缓存与下载、兼容性、稳定版、安装过滤器、仓库、高级与诊断。
- 实现「改一项、写一项、读回校验」铁律：编辑单项立即触发异步写盘与读回校验，错误在行内标红。
- 维护整页与各分组脏标记（`IsDirty`），针对需重启生效的项显式添加角标提示。
- 区分持久化通道：Core 业务标量配置写入 `ISettingsService` (`config.json`)；界面外观个性化写入 `IUiSettingsStore` (`ckan-ui.json`)。

#### 2.4.2 数据来源与契约
| 依赖服务 | 接口方法 | 输入/输出 DTO 或键 | 用途 |
|---|---|---|---|
| `ISettingsService` | `Get<T>()`、`SetAndVerifyAsync<T>()` | 键名常量 (`SettingsKeyNames`) | Core 核心配置项读写与原子校验 |
| `IUiSettingsStore` | `Load()`、`Save()` | `UiSettings` 记录 | 主题、圆角、材质、密度等 UI 配置 |
| `IDialogService` | `ConfirmAsync()`、`ShowMessageAsync()` | 提示信息 | 重置默认值二次确认、重启提醒 |

#### 2.4.3 命令与状态定义
| 属性/命令 | 类型 | 职责说明 |
|---|---|---|
| `Groups` | `IReadOnlyList<SettingGroupViewModel>` | 7 个分组集合（内含设置项 VM 列表） |
| `TokenEditor` | `AuthTokenEditorViewModel` | GitHub / 认证令牌子编辑器 VM |
| `IsLoading` | `bool` | 是否正在从底层存储读取设置项 |
| `IsDirty` | `bool` | 是否存在未保存或已变动的配置 |
| `StatusText` | `string` | 状态栏提示（如「设置已保存」或失败详情） |
| `SaveItemCommand` | `IAsyncRelayCommand<SettingItemViewModel>` | 单项提交保存并读回验证 |
| `ResetGroupDefaultsCommand`| `IAsyncRelayCommand<SettingGroupKey>` | 重置指定分组为默认配置 |
| `OpenLogDirectoryCommand` | `IRelayCommand` | 在系统管理器中打开日志目录 |

#### 2.4.4 空状态与错误收口
- **读取失败**：单项无法解析或读空时，回退到定义好的安全预设值，并在状态栏提示「部分设置项读取失败，已使用默认值」。
- **校验失败**：如缓存路径非法、并发连接数超限（如负数或超出最大边界），行内标红 `ErrorMessage`，不发起底层写盘，阻止保存脏数据。

---

### 2.5 操作队列与进度浮层 (OperationQueueViewModel)
- **实现状态**：🚧 待落地（已有 `IOperationQueue` 契约与 `FakeOperationQueue` 测试支撑）
- **规格对应**：03 篇 §1 (TopBar 状态区)、§8 (D-04 进度)、§13.1 (Status Log 抽屉)；06 篇 UI-08；05 篇 §4 (100ms 进度节流)

#### 2.5.1 视图模型职责
- 聚合全局后台任务进度，在顶部导航条展示迷你状态环（当前进行数、总百分比、是否忙碌）。
- 提供可展开/抽屉式的任务管理列表，明细展示各任务阶段文案、子百分比与取消按钮。
- 对 Core 高频上报的进度数据执行 100ms UI 节流消费，杜绝高频刷新引发的卡顿。

#### 2.5.2 数据来源与契约
| 依赖服务 | 接口方法 / 事件 | 输入/输出 DTO | 用途 |
|---|---|---|---|
| `IOperationQueue` | `Operations`、`IsBusy`、`Updated` 事件、`Cancel()` | `OperationProgress` (`TaskId`, `Kind`, `Title`, `Percent`, `CanCancel`, `State`) | 监听全局任务状态变更与触发取消 |

#### 2.5.3 命令与状态定义
| 属性/命令 | 类型 | 职责说明 |
|---|---|---|
| `IsBusy` | `bool` | 是否有正在运行或挂起的任务 |
| `ActiveOperations` | `IReadOnlyList<OperationProgress>` | 进行中与最近完成的任务列表（已节流） |
| `OverallPercent` | `double` | 综合进度百分比 (0-100) |
| `CancelTaskCommand` | `IRelayCommand<string>` | 取消指定 TaskId 的后台任务 |
| `ClearCompletedCommand` | `IRelayCommand` | 清除已成功或已取消的已完结任务卡片 |

#### 2.5.4 空状态与错误收口
- **队列空闲**：状态栏与抽屉内显示「当前无正在执行的后台任务」，进度环隐藏。
- **任务取消/异常**：若任务因网络超时或 Core 抛错中断，状态标记为 `OperationState.Failed`，进度条变红，保留重试提示与错误详情展开。

---

### 2.6 仓库管理页 (ReposPageViewModel)

**对应规格**：03 篇 §6 / 06 篇 UI-06·VM-06 / 12 篇 F-10·F-12。

**职责**：展示当前实例的元数据仓库列表，并提供增删、上下移（优先级）、单仓库刷新与恢复官方默认。所有 Core 事务都封在 `IRepositoryStore` 后面（`RegistryRepositoryStore` 是唯一碰 `Registry.RepositoriesSet` 与网络的实现），页面 VM 只跟 `IRepositoryService` 的 8 个增量成员打交道。

**关键成员**：

| 成员 | 类型 | 说明 |
|---|---|---|
| `Repositories` | `IReadOnlyList<RepositoryRowViewModel>` | 按 priority 升序投影的行 |
| `LoadRepositoriesCommand` | `IAsyncRelayCommand` | 读快照（无实例时返回空列表且不报错） |
| `BeginAddCommand` / `BeginEditCommand` | `IRelayCommand` | 打开页内联表单（`RepositoryFormViewModel`） |
| `RemoveCommand` / `MoveUpCommand` / `MoveDownCommand` | `IRelayCommand<RepositoryRowViewModel>` | 增删与优先级调整；删除最后一个仓库会被拒绝 |
| `RefreshOneCommand` | `IAsyncRelayCommand<RepositoryRowViewModel>` | 只刷该仓库；失败只给该行打 `HasFailure` 标记 |
| `RestoreDefaultsCommand` | `IAsyncRelayCommand` | 用 `RepositoryList.DefaultRepositories` 整体替换 |

**语义约定**（与服务层一致，逐条有单测）：
- 名称空 / 地址空 / 非 http(s) / 重名（忽略大小写）/ 地址重复 / 找不到 / 删除最后一个 → 失败且**不写盘**，`Retryable=false`。
- 官方默认列表取不到 → `Retryable=true`（可换网络后重试）。
- 写注册表异常 → `Retryable=true`，文案「保存仓库列表失败，修改未生效。」。
- 上下移越界或 `delta==0` → 成功且不写盘（按钮已禁用，无副作用）。
- 变更后经 `IRepositoryService.RepositoriesChanged`（`IDispatcher.Post` 封送）通知，模组页据此重载索引。

**为什么没有「启用/禁用」开关**：CKAN 的 `Repository` 只有 `name`/`uri`/`priority`，没有 enabled 字段；启用状态由「是否在列表里 + 优先级」表达，不另造字段。

### 2.7 导入导出页 (ModPacksPageViewModel)

**对应规格**：03 篇 §6 / 06 篇 UI-20·UI-24 / 12 篇 F-39·F-40·F-45。

**职责**：导出当前实例的 modpack、预览并执行 `.ckan` 导入、比对两个实例的模组差集。三种操作都只经 `IModPackService`；Core 的 `RegistryManager`/`CkanModule`/`NetModuleCache` 全部收在 `IModPackStore` 后面（`CkanModPackStore` 是唯一实现），纯函数逻辑在 `ModPackCatalog`（可离线测）。

**关键成员**：

| 成员 | 类型 | 说明 |
|---|---|---|
| `ExportForm` | `ExportFormViewModel` | 目标路径、是否带版本、是否打包缓存 zip |
| `ExportCommand` | `IAsyncRelayCommand` | 调 `ExportAsync(ModPackExportRequest)`；成功后显示写出路径与模组数 |
| `PreviewImportCommand` / `ImportCommand` | `IAsyncRelayCommand` | 先 `PreviewImportAsync` 出可勾选行，再 `ImportAsync(preview, selectedIdentifiers)` |
| `ImportRows` | `IReadOnlyList<ImportEntryRowViewModel>` | 将安装 / 将升级 / 将降级 / 已安装 / 找不到 / 有冲突（带原因） |
| `CompareCommand` / `DiffRows` | `IAsyncRelayCommand` / `IReadOnlyList<DiffRowViewModel>` | 左右实例三分支差集：仅左侧有 / 仅右侧有 / 版本不同 |
| `StatusText` / `ErrorText` | `string` | 全中文文案；异常一律经 `IErrorReporter` 并落 `ModPacksErrorText` |

**语义约定**：
- 导出格式 = `RegistryManager.GenerateModpack(recommends, with_versions).ToJson()` 直出，`depends`/`recommends`/`metapackage`/`spec_version` 全由 Core 决定，不自己拼 JSON；文件名 `{前缀}-{instance.SanitizedName}.ckan`。
- 可选打包：`<名字>.ckan` 放归档根，`Downloads/<identifier>-<version>.zip` 放缓存里的下载包（**上游没有对应格式**，这是自定朴素归档；解压后 `ckan import <目录>` 可消费）。
- 导入执行：勾选行 → `ChangeRequest` → `IChangeSetService.BuildAsync`；`HasConflicts` 时中止且**不调用** `ApplyAsync`；无自有下载/解包逻辑。
- 同一模组被两个来源要求不同版本 → 标 Conflict 并丢弃，绝不静默安装。
- 比对要求两个不同实例；只有一个实例时按钮禁用且命令内二次拦截。
- 导出目标已存在同名目录 → `UnauthorizedAccessException` → 转可重试文案。

**已知缺口**：F-40（本地 ZIP 直接灌下载缓存）、F-41（跨实例去重）、F-42（未管理文件树）未实现。

### 2.8 启动命令编辑 (LaunchCommandsPanelViewModel)

**对应规格**：03 篇 §5.4 / 06 篇 UI-28 / 12 篇 F-05·F-06。

**职责**：编辑某个实例的启动命令列表（多条命令并存，启动时择一）。状态与持久化都在 `LaunchCommandFile`：读 `<GameDir>/CKAN/GUIConfig.json` 的 `CommandLines` 键（PascalCase 字符串数组，与老 GUI `GUIConfiguration.CommandLines` 共用同一文件），只改该键、其余字段原样保留，写 `path + ".tmp"` 再 `File.Move(overwrite:true)`。缺文件或空数组回落 `LaunchCommandDefaults.DefaultsFor`。

**关键成员**：

| 成员 | 说明 |
|---|---|
| `Rows` / `SelectedRow` | 命令列表与其编辑区（`LaunchCommandRowViewModel`：文本 + 名称） |
| `AddCommand` / `RemoveCommand` / `MoveUpCommand` / `MoveDownCommand` | 增删与排序；`CanRemove` 不允许删到 0 条（与老 GUI `e.Cancel = true` 一致） |
| `ResetToDefaultsCommand` | 用 `LaunchCommandDefaults` 重新生成（Windows/Unix 每个存在的锚点 → `"{anchor} -single-instance"`，Mac → `./KSP.app/Contents/MacOS/KSP`，末尾追加 `steam://rungameid/<id>`，向上 ≤8 层找 `steamapps/appmanifest_220200.acf` / `_1740390.acf`） |
| `AcceptCommand` / `CancelCommand` | 接受写盘、取消丢弃；`HasChanges` 控制接受按钮可用性 |

**命令解析**：`LaunchCommandLine.Split` 以首个 token 为 binary、其余 `Join` 为 args（与 Core `PlayGame` 同口径）；`Tokenize` 按 `CommandLineToArgvW` 规则切分，`Quote` 负责回写转义；`IsSteamCommand` 识别 `steam://` 走系统外壳。

**宿主能力**：文本输入走页面级 `IInstanceTextPrompt`（`IDialogService` 尚无通用文本输入）；启动走 `IInstanceShellLauncher.LaunchGameAsync(gameDir, command)`。

**已知缺口**：老 GUI 的「不兼容 mod 启动确认」（`Main.cs:985`）未复刻；E6 `DisposeAll` 与精简模式状态机仍未接。

---

### 2.9 未管理文件页 (UnmanagedPageViewModel)

**对应规格**：03 篇 §6 / 06 篇 UI-10 / 12 篇 F-42。

**职责**：把 `<GameDir>/GameData` 下不属于任何受管安装的文件列成树，支持定位、预演删除与真实删除。受管路径集合由 `CkanManagedPathProvider` 从 `registry.InstalledFileInfo()` 的键（去 `GameData/` 前缀）+ 纯 DLL 安装合成；扫描与删除是纯文件系统逻辑（`UnmanagedScanner` 不引用 CKAN），因此可无实例 headless 测试。

**关键成员**：

| 成员 | 说明 |
|---|---|
| `Nodes` / `SelectedNodes` | 树节点投影（`UnmanagedNodeViewModel`）；目录条目只在「递归含未管理文件」时出现，尺寸为未管理文件合计，另带 `ManagedChildCount` |
| `RefreshCommand` / `ExpandAllCommand` / `CollapseAllCommand` / `ResetFilterCommand` | 刷新与树视图操作 |
| `FilterText` | 按相对路径筛选，命中项保留祖先链（`UnmanagedTreeBuilder` 剪枝） |
| `PreviewDeleteCommand` / `DeleteCommand` | 前者走 `dryRun:true` 只列路径；后者连弹两次 `ConfirmAsync` 才落盘 |
| `RevealInExplorerCommand` | 经 `IUnmanagedShellLauncher`（视图 code-behind 用 `??=` 注入）打开资源管理器定位 |

**安全语义**：整批先做安全校验，任一路径受管或「包含受管内容」→ 整批拒绝且不可重试（提示去模组页卸载），dry-run 也不放行；目标已消失 → 可重试失败。「接管为受管安装」（`AdoptAsync`）留给 INT-09。

---

### 2.10 存档工作台 (SavesPageViewModel)

**对应规格**：13 篇（savegame-sfs）/ M7·E1。

**职责**：枚举 `<GameDir>/saves` 下的存档，展示头部元数据与校验诊断，并提供「按 SFS 规范重排格式」的格式化能力。格式化复用 SFS 内核（`SfsParser` / `SfsFormatter` / `SfsValidator`），磁盘语义全在服务层。

**关键成员**：

| 成员 | 说明 |
|---|---|
| `Saves` / `SelectedSave` | 存档行（`SaveRowViewModel`：标题、场景、游戏版本、资金、载具数、修改时间）；按 `LastWriteTime` 倒序 |
| `Diagnostics` | 选中存档的校验诊断（级别 / 消息 / 行列），来自 `SaveValidationReport` |
| `RefreshCommand` | 重新枚举；目录不存在按空列表处理，不报错 |
| `FormatCommand` | 默认只写 `<stem>.formatted.sfs`；`Overwrite` 模式先落 `.pre-edit-<yyyyMMdd-HHmmss>.sfs` 快照再原子写并还原 `LastWriteTime` |
| `RevealInExplorerCommand` | 经 `ISavesShellLauncher` 定位文件 |

**安全语义**：源文件有 Error 级诊断或自校验失败一律 `Refused` 且绝不落盘（不猜修复）；`*.sfs.bak` 永不覆盖；损坏存档返回 `Ok` + 诊断而不是抛异常。元数据来源 = 头部 `GAME` 直接子键 + 同级 `.loadmeta`（后者优先 gameMode/kspVersion/UT/funds/vesselCount）。

**已知缺口**：存档内嵌的 `.loadmeta` 一致性校验（V-09）未做；尚无「存档另存为」对话框。

---

### 2.11 跨实例去重页 (DedupPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/DedupPageView.axaml` |
| 依赖 | `IInstanceDedupService`、`IErrorReporter`、`IDialogService` |
| 关键成员 | `Groups` / `Results` / `DryRun`（默认 true）/ `MinimumFileSizeKiB`（128）/ `ScanCommand` / `ExecuteCommand` / `ReclaimableBytes` / `SkippedCount` / `IsSupported` |
| 语义 | 实例多选默认全选（只记住用户取消的项）；扫描后按「哈希 + 卷」成组列出副本；执行前两次确认（第一次讲要做什么，第二次讲「多个实例共用同一份数据，删任一份影响其它实例」）；真写盘成功后清空计划并要求重扫；平台不支持时命令不可执行并给出提示，而不是报错 |
| 缺口 | 非 Windows 无 inode 探测（已建链的路径会重复列出，执行阶段安全跳过）；无共享存储目录模式；组表格无分页（依赖 ListBox 虚拟化） |

### 2.12 安装历史页 (InstallHistoryPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/InstallHistoryPageView.axaml` |
| 依赖 | `IInstallHistoryService`、`IErrorReporter` |
| 关键成员 | `Entries` / `SelectedEntry` / `InstalledModules` / `MissingModules` / `RestorePreviewText` / `RestoreResultText` / `LoadCommand` / `RestoreCommand` |
| 存储 | 每实例一份 `<GameDir>/CKAN/install_history.jsonl`：一次操作落 entry + snapshot 两行，共用同一时间戳；坏行跳过不抛；超过 2000 行只留最新；写入走 `.tmp` + `File.Move(overwrite:true)` |
| 语义 | 按「当前是否装着」分已装/未装两组；版本不同的模组留在已装组并显示「版本不同（当前 x）」；重装走 `IChangeSetService.BuildAsync` + `IInstallerService.ApplyAsync`，有冲突即中止且不调用执行；已同版本不发变更请求；`HistoryRecordingInstaller` 装饰器自动记账，`InstallHistoryScope` 抑制重装双记 |
| 缺口 | 只按行数截断（无按时间/大小归档）；不支持删除或清空历史；`RemoveExtras` 在契约里但页面无开关；旧客户端 `CKAN/history/*.ckan` 不导入 |

### 2.13 插件页 (PluginsPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/PluginsPageView.axaml` |
| 依赖 | `IPluginService`、`IErrorReporter`；文件选择走视图注入的 `IPluginFilePicker` |
| 关键成员 | `ActivePlugins` / `DormantPlugins` / `PluginsDirectory` / `FailureTexts` / `RefreshCommand` / `AddNewCommand` / `ActivateCommand` / `DeactivateCommand` / `ReloadCommand` / `UnloadCommand` |
| 语义 | 插件目录默认 `%LocalAppData%/CKAN/plugins`；扫描一律以 `activate:false` 加载进 Dormant，**必须用户显式点「激活」才执行插件 `Initialize`**；加载用可回收 `AssemblyLoadContext`，对契约程序集与 System/Avalonia 前缀返回 null（否则插件会加载出第二份 Abstractions）；用 `LoadFromStream` 而非 `LoadFromAssemblyPath`，避免长期占用 dll 句柄；同进程、不沙箱，页面页头明示 |
| 缺口 | 插件声明的页签尚未渲染成导航项（`PageTypes` 是静态字典，需要动态 key + 按 ViewLocator 解析插件 VM 类型）；ALC 卸载尽力而为（插件持有静态引用/线程时不会真回收）；不校验签名；不递归子目录；页面拉取式刷新，不订阅 `IPluginService.Changed` |

### 2.14 内容预览页 (ContentPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/ContentPageView.axaml`（TreeView 折叠树） |
| 依赖 | `IContentService`、`IContentTargetProvider`（宿主注入当前选中模组）；剪贴板与「打开目录」走视图注入的 `IContentShellLauncher` |
| 关键成员 | `Roots` / `SelectedNode` / `StatusText` / `EntryCountText` / `LoadCommand` / `EnsureCachedCommand` / `CopyPathCommand` / `OpenDirectoryCommand` |
| 语义 | 只读 ZIP 中央目录建树（目录在前、同层按名排序、首层展开）；路径穿越条目一律丢弃；ZIP 未缓存时给「下载到缓存」——先下到 `<缓存目录>/<id>-<guid>.zip.part` 再 `cache.Store(move:true, validate:true)` 原子入库，残缺文件不会污染缓存 |
| 缺口 | 无真实实例的端到端下载集成测试；条目超过 20000 直接拒绝；只读不编辑 |

### 2.15 modpack 编辑器页 (ModPackEditorPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/ModPackEditorPageView.axaml` |
| 依赖 | `IModPackService`（编辑器能力是它上面的三个 DIM：`LoadEditorAsync` / `SaveEditorAsync` / `ValidateEditor`） |
| 关键成员 | `Document` / `Authors` / `Licenses` / `Relations` / `Issues` / `LoadCommand` / `SaveCommand` / `ValidateCommand` / `AddRelationCommand` |
| 语义 | 读写都走上游 `CkanModule`（`new CkanModule(json, StrictGameComparator)` / `ToJson()`），不发明磁盘字段；关系行 `multi` 模式保存时展开成 N 条 `name` 行（磁盘上没有「多选」概念），读回永不产生 `multi`；精确版本读成 min==max 再折叠回 `version`，避免「打开→保存」把精确版本降级成范围 |
| 上游硬约束 | `ksp_version` 与 `ksp_version_min/max` 互斥（`CkanModule.CheckHealth` 抛 `InvalidModuleAttributesKraken`，写得出去读不回来）→ 校验判 Error（`ksp_version.conflict`），`BuildModule` 有上下限时绝不写精确版本；`ModuleVersion` 对任意字符串都不抛异常，版本改用 Spec.md 字符集 `^([0-9]+:)?[0-9A-Za-z.+\-_]+$` 校验 |
| 缺口 | `resources` / `provides` / `localizations` / `install` / `download_hash` 打开再保存会丢（定位是 metapackage 编辑器）；手填 `spec_version` 保存时被 `SpecVersionAnalyzer.MinimumSpecVersion` 覆盖；无标识自动补全，「包不存在」这类问题校验查不出 |

### 2.16 下载统计页 (DownloadStatsPageViewModel)

| 项 | 值 |
| --- | --- |
| 视图 | `src/CkanUI.App/Views/DownloadStatsPageView.axaml` + `src/CkanUI.App/Controls/DonutChart.cs`（自绘环图，无第三方图表库） |
| 依赖 | `IDownloadStatsService`（实现内部用 `IRepositoryService` + `ICacheService` + `IRegistryService`） |
| 关键成员 | `Slices` / `ChartSlices` / `Rows` / `Dimension` / `Sort` / `Descending` / `RefreshCommand` / `SetDimensionCommand` / `SetSortCommand` |
| 语义 | 三档维度 `Mods`（对齐上游饼图）/ `Tags` / `Installed`；超过 12 段合并为「其他」；下载量为 0 的模组归入「下载量不可用」单一段而不是画 0% 扇区；同列再点翻转方向、新列重置为降序；`OnActivated` 自动刷新 |
| 缺口 | **按仓库维度未做**：`RepositorySnapshot` 只有 `(Available, RepositoryIds, UpdatedAt, IsFromCache)`，没有 per-module 仓库映射，强行统计会给出假数据；行数上限 200 且无导出 |

### 2.17 系统托盘（外壳级，F-51）

| 项 | 值 |
| --- | --- |
| 组成 | `src/CkanUI.ViewModels/Shell/TrayMenuViewModel.cs`（菜单与口径）+ `ITrayHostActions`（七个动作的窄接口）+ `src/CkanUI.App/Tray/TrayHost.cs`（图标与 `NativeMenu` 装配）+ `TrayHostActions.cs`（窗口/目录/刷新/退出实现） |
| 菜单 | 七项固定顺序：显示主窗口 / 隐藏到托盘 / 打开日志目录 / 打开下载缓存目录 / 打开实例目录 / 刷新仓库列表 / 退出 |
| 语义 | Avalonia 12 没有 `Application.TrayIcons`，用 `TrayIcon.GetIcons/SetIcons(Application)` 附加属性；图标零二进制资源，`RenderTargetBitmap` 现画 32×32，失败降级到平台默认图标并 `SetTrayAvailable(false)`；隐藏一律 `Hide()` 不用 `Close()`（`ShutdownMode=OnMainWindowClose` 会退进程）；两个开关 `MinimizeToTray` / `CloseToTray` 的真源是 `UiSettings`，钩子挂在 `App.axaml.cs` 的 `Closing` 与 `WindowState` 变化上，`MainWindow.axaml.cs` 一行未改 |
| 缺口 | 真实图标点击/开关联动只在 VM 层单测，需桌面手测；图标不随 DPI 重绘（固定 32×32）；菜单文案是 VM 内中文常量（原生菜单由 OS 绘制，拿不到语言切换重绘时机）；开机自启 + 启动即最小化未实现 |

## 3. 对话框实现说明

全部对话框继承自 `DialogViewModelBase`，并通过 `IDialogService` 在 UI 线程上弹窗呈现，解耦 Core 后台线程与 UI 窗口生命周期。

### 3.1 确认对话框 (ConfirmDialogViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/ConfirmDialogViewModel.cs`)
- **规格对应**：03 篇 §8 (D-01)；02 篇 §4 (`IUser.RaiseYesNoDialog`)
- **职责与数据来源**：作为 Core `IUser.RaiseYesNoDialog` 的唯一出口。接收标题、主体消息（Core 原生文案）、可选技术细节及确认/取消按钮文案。
- **状态与命令**：
  - `Message`, `Detail`: 提示文案与可选折叠细节。
  - `ShowDoNotAskAgain`, `DoNotAskAgain`: 是否展示「不再询问」复选框及用户勾选值（由调用方持久化）。
  - `ConfirmCommand`, `CancelCommand`: 关闭弹窗并返回布尔决策。

### 3.2 单选对话框 (SelectDialogViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/SelectDialogViewModel.cs`)
- **规格对应**：03 篇 §8 (D-02)；02 篇 §4 (`IUser.RaiseSelectionDialog`)
- **职责与数据来源**：作为 Core `IUser.RaiseSelectionDialog` 的唯一出口。处理虚拟模组提供者多选一等场景。
- **状态与命令**：
  - `Options`: 候选项文本列表（顺序敏感）。
  - `SelectedIndex`: 0 基当前选中索引；空列表或取消时收口返回 `-1`（SP-04 约定）。
  - `HasOptions`: 校验是否有有效项，无有效项时禁用确定按钮。

### 3.3 错误对话框与条 (ErrorDialogViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/ErrorDialogViewModel.cs`)
- **规格对应**：03 篇 §8 (D-03)；02 篇 §4 (`IUser.RaiseError`)
- **职责与数据来源**：承接 Core 运行时错误与异常。绑定 `ErrorEntry`（时间戳、错误级别、消息、详情、异常堆栈）。
- **特色功能**：提供 `CopyDiagnosticCommand`，通过注入的异步剪贴板委托拼装标准诊断文本（03 篇 §8），便于用户反馈。

### 3.4 崩溃报告对话框 (CrashDialogViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/CrashDialogViewModel.cs`)
- **规格对应**：03 篇 §8 (D-08)；01 篇 §7；ARC-04 (未捕获异常三级兜底)
- **职责与数据来源**：当触发 AppDomain 或 UI 线程未捕获异常时弹出。
- **状态与命令**：
  - `LogPath`: 当前已落盘的日志绝对路径（供用户定位）。
  - `Exception`: 原始异常对象，提供完整 StackTrace 格式化展示。
  - `PrimaryButtonText`（退出程序）与 `SecondaryButtonText`（尝试返回程序）。

### 3.5 模组详情面板/弹窗 (ModDetailsViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs`)
- **规格对应**：03 篇 §3.1；06 篇 UI-03、VM-04
- **职责与数据来源**：以 `Identifier` 索引 `IRepositoryService` 与 `IRegistryService`。通过可选的 `DetailsProvider` 投影富元数据（版本、作者、许可证、主页、九类依赖关系树 `RelationTreeView`）。未接线 Core 时降级显示快照基础数据，保证 headless 可测。

### 3.6 关于框 (AboutDialogViewModel)
- **实现状态**：✅ 已就位 (`src/CkanUI.ViewModels/Dialogs/AboutDialogViewModel.cs` + `src/CkanUI.App/Views/Dialogs/AboutDialogView.axaml`)
- **规格对应**：12 篇 F-55；03 篇 §8 (D-09 帮助与关于)
- **入口**：外壳左栏底部「关于」按钮（`MainWindow.axaml` 的 `OnAboutClick`），由 code-behind 建窗并指定 owner（05 篇 §2 铁律）。
- **职责与数据来源**：VM 只接收字符串，不引用 Avalonia 与 CKAN。版本矩阵四行由 `src/CkanUI.App/AboutInfoFactory.cs` 采集：界面版本（程序集版本 `v{Major}.{Minor}.{Build}`）、核心库版本（`CkanUI.Services.Diagnostics.CoreVersionInfo.ReleaseVersion` → `CKAN.Meta.ReleaseVersion`）、运行时（`.NET {Environment.Version}`）、操作系统（`RuntimeInformation.OSDescription`）。
- **状态与命令**：
  - `Fields`（4 行 `AboutField`）、`Links`（3 条官方地址）、`LicenseText`（GPL-3.0）。
  - `OpenLinkCommand`：外链交给注入的 `Func<string, Task<bool>>`（App 侧是 Avalonia `Launcher.LaunchUriAsync`）。通道缺失、返回 false 或抛异常都收口成框内提示，不弹二级错误框、不关闭关于框。
  - 只有「关闭」一个按钮，`PrimaryCommand` 关闭并置 `IsConfirmed`。
- **为什么把版本采集放在 App 层**：`CKAN` 命名空间只许出现在 Services/Localization（01 篇 §5），所以 `CoreVersionInfo` 是 Services 层薄壳，App 层负责把四个来源拼成 `AboutInfo`。

---

## 4. 页面与对话框实现对照矩阵

| 视图/对话框名称 | 对应 VM 类 | 实现状态 | 规格条目 | 数据源服务接口 | 核心 DTO |
|---|---|---|---|---|---|
| 模组管理页 | `ModsPageViewModel` | ✅ 已实现 | 03 篇 §3 / UI-02, UI-03 | `IRepositoryService`, `IRegistryService`, `IInstanceService` | `ModRowData`, `RegistrySnapshot`, `RepositorySnapshot` |
| 变更集确认页 | `ChangesPageViewModel` | ✅ 已实现 | 03 篇 §4 / UI-04 | `IChangeSetService`, `IInstallerService`, `IOperationQueue` | `ChangeSetPreview`, `ChangeItemDto`, `OperationProgress` |
| 仓库管理页 | `ReposPageViewModel` | ✅ 已实现 | 03 篇 §6 / UI-06 | `IRepositoryService`, `IDialogService` | `RepositoryEntryInfo`, `RepositoryEditRequest` |
| 导入导出页 | `ModPacksPageViewModel` | ✅ 已实现（F-40/F-41/F-42 除外） | 03 篇 §6 / UI-20, UI-24 | `IModPackService`, `IInstanceService` | `ModPackPreview`, `ModPackImportReport`, `InstanceComparison` |
| 存档工作台 | `SavesPageViewModel` | ✅ 已实现 | 13 篇 / M7·E1 | `ISaveFileService` | `SaveGameInfo`, `SaveValidationReport`, `SaveFormatOutcome` |
| 未管理文件页 | `UnmanagedPageViewModel` | ✅ 已实现 | 03 篇 §6 / UI-10 | `IUnmanagedService` | `UnmanagedFileInfo` |
| 跨实例去重页 | `DedupPageViewModel` | ✅ 已实现 | 03 篇 §6 / UI-21 | `IInstanceDedupService` | `DedupScanReport`, `DedupGroup`, `DedupExecuteReport` |
| 安装历史页 | `InstallHistoryPageViewModel` | ✅ 已实现 | 03 篇 §6 / UI-23 | `IInstallHistoryService` | `InstallHistoryEntry`, `InstallHistorySnapshot`, `InstallHistoryRestorePlan` |
| 插件页 | `PluginsPageViewModel` | ✅ 已实现 | 03 篇 §6 / UI-33 | `IPluginService` | `PluginInfo`, `PluginLoadFailure` |
| 内容预览页 | `ContentPageViewModel` | ✅ 已实现 | 12 篇 F-29 / UI-03 | `IContentService`, `IContentTargetProvider` | `ContentPreview`, `ContentEntry` |
| modpack 编辑器页 | `ModPackEditorPageViewModel` | ✅ 已实现 | 12 篇 F-46 / UI-24 | `IModPackService`（三个 DIM） | `ModPackEditorDocument`, `ModPackEditorIssue` |
| 下载统计页 | `DownloadStatsPageViewModel` | ✅ 已实现 | 12 篇 F-47 / UI-25 | `IDownloadStatsService` | `DownloadStatsReport`, `DownloadStatsSlice` |
| 系统托盘 | `TrayMenuViewModel` | ✅ 已实现（真实点击待手测） | 12 篇 F-51 / UI-26 | `ITrayHostActions`（App 层实现） | `TrayMenuItemViewModel` |
| 实例管理页 | `InstancesPageViewModel` | ✅ 已实现（重命名已接 `RenameAsync`；启动命令编辑见 §2.8） | 03 篇 §5 / UI-05 | `IInstanceService`, `IDialogService` | `InstanceInfo` |
| 设置页 | `SettingsPageViewModel` | ✅ 已实现 | 03 篇 §7 / UI-07 | `ISettingsService`, `IUiSettingsStore` | `UiSettings`, `SettingItemViewModel` |
| 操作队列浮层 | `OperationQueueViewModel` | ✅ 已实现 | 03 篇 §1, §8 / UI-08 | `IOperationQueue` | `OperationProgress` |
| 确认对话框 | `ConfirmDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-01) / UI-09 | `IDialogService` (通过 `IUser.RaiseYesNoDialog`) | 字符串标量 |
| 单选对话框 | `SelectDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-02) / UI-09 | `IDialogService` (通过 `IUser.RaiseSelectionDialog`) | `IReadOnlyList<string>` |
| 错误对话框 | `ErrorDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-03) / UI-09 | `IErrorReporter` / `IUser.RaiseError` | `ErrorEntry` |
| 关于框 | `AboutDialogViewModel` | ✅ 已实现 | 12 篇 F-55 / UI-36 | 无（App 侧 `AboutInfoFactory` 采集） | `AboutInfo`, `AboutField`, `AboutLink` |
| 崩溃对话框 | `CrashDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-08) / ARC-04 | 全局异常捕获管道 | `Exception` |
| 模组详情视图 | `ModDetailsViewModel` | ✅ 已实现 | 03 篇 §3.1 / UI-03 | `IRepositoryService`, `IRegistryService` | `ModDetailsSnapshot`, `ModRelationItem` |
