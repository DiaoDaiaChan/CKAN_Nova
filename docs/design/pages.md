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
- **实现状态**：🚧 待落地（按规格设计并已有变更集预览 DTO 契约）
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
- **执行失败**：事务失败时接收 `InstallReport`，呈现失败模组及原因列表；提供「复制错误日志」与「重试失败项」按钮，Core 自动触发的事务回滚保证游戏目录无半装脏文件（08 篇 R-03）。

---

### 2.3 游戏实例管理页 (InstancesPageViewModel)
- **实现状态**：🚧 当前为占位符骨架，需按规范完整落地
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

---

## 4. 页面与对话框实现对照矩阵

| 视图/对话框名称 | 对应 VM 类 | 实现状态 | 规格条目 | 数据源服务接口 | 核心 DTO |
|---|---|---|---|---|---|
| 模组管理页 | `ModsPageViewModel` | ✅ 已实现 | 03 篇 §3 / UI-02, UI-03 | `IRepositoryService`, `IRegistryService`, `IInstanceService` | `ModRowData`, `RegistrySnapshot`, `RepositorySnapshot` |
| 变更集确认页 | `ChangesPageViewModel` | 🚧 待落地 | 03 篇 §4 / UI-04 | `IChangeSetService`, `IInstallerService`, `IOperationQueue` | `ChangeSetPreview`, `ChangeItemDto`, `OperationProgress` |
| 实例管理页 | `InstancesPageViewModel` | 🚧 待落地 (当前占位) | 03 篇 §5 / UI-05 | `IInstanceService`, `IDialogService` | `InstanceInfo` |
| 设置页 | `SettingsPageViewModel` | ✅ 已实现 | 03 篇 §7 / UI-07 | `ISettingsService`, `IUiSettingsStore` | `UiSettings`, `SettingItemViewModel` |
| 操作队列浮层 | `OperationQueueViewModel` | 🚧 待落地 | 03 篇 §1, §8 / UI-08 | `IOperationQueue` | `OperationProgress` |
| 确认对话框 | `ConfirmDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-01) / UI-09 | `IDialogService` (通过 `IUser.RaiseYesNoDialog`) | 字符串标量 |
| 单选对话框 | `SelectDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-02) / UI-09 | `IDialogService` (通过 `IUser.RaiseSelectionDialog`) | `IReadOnlyList<string>` |
| 错误对话框 | `ErrorDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-03) / UI-09 | `IErrorReporter` / `IUser.RaiseError` | `ErrorEntry` |
| 崩溃对话框 | `CrashDialogViewModel` | ✅ 已实现 | 03 篇 §8 (D-08) / ARC-04 | 全局异常捕获管道 | `Exception` |
| 模组详情视图 | `ModDetailsViewModel` | ✅ 已实现 | 03 篇 §3.1 / UI-03 | `IRepositoryService`, `IRegistryService` | `ModDetailsSnapshot`, `ModRelationItem` |
