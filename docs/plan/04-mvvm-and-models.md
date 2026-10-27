# 04 MVVM 结构与 Model 迁移

## 1. MVVM 约定

| 约定 | 规则 |
|---|---|
| 基类 | `ObservableObject`（CommunityToolkit.Mvvm）；异步命令用 `AsyncRelayCommand` |
| 属性 | 源生成字段 `[ObservableProperty] private string _searchText = "";` |
| 依赖 | ViewModel 只通过构造函数接收**接口**（`IRegistryService` 等），不 `new` 服务 |
| 生命周期 | 所有 VM 实现 `IActivatable`（`Activate()` / `Deactivate()`），由 View 在 `OnAttachedToVisualTree` 调用；订阅在 `Activate` 建立、`Deactivate` 解除 |
| 线程 | VM 假定自己在 UI 线程被调用；服务层负责封送（05 篇） |
| 校验 | `ObservableValidator` + `DataAnnotations`，错误直接绑到控件 |
| 视图定位 | `ViewLocator`：`XxxViewModel` → `XxxView`（约定式，无手动映射表） |

**禁止**：

- VM 里出现 `Avalonia.Controls.*` / `Window` / `MessageBox` / `Dispatcher`；
- VM 里出现 `File.*`、`Directory.*` 写操作；
- VM 之间直接互相引用（用 `IMessenger` 或经服务层事件）；
- 在 VM 构造函数里发起 IO（拖慢启动、难测试）。

## 2. ViewModel 清单

| VM | 对应视图 | 关键状态 | 依赖服务 | MVP |
|---|---|---|---|---|
| `MainWindowViewModel` | MainWindow | 当前页、实例、操作队列摘要、状态栏文本 | IInstanceService, IOperationQueue | ✅ |
| `ModsViewModel` | ModsView | 搜索文本、过滤器集合、排序、列表代理、选中项、变更集摘要 | IRepositoryService, IRegistryService, IChangeSetService | ✅ |
| `ModRowViewModel` | 列表行 | 该行的所有列值 + 状态枚举 + 勾选三态 | —（纯 DTO 投影） | ✅ |
| `ModDetailsViewModel` | 详情面板 | 当前选中模组的元数据、依赖树、主页链接 | IRepositoryService, IRegistryService | ✅ |
| `ChangesViewModel` | ChangesView | 变更集明细、冲突、预计体积、执行状态 | IChangeSetService, IInstallerService | ✅ |
| `InstancesViewModel` | InstancesView | 实例卡片列表、锁定状态 | IInstanceService | ✅ |
| `RepositoriesViewModel` | RepositoriesView | 仓库列表、优先级排序、刷新状态 | IRepositoryService | ✅ |
| `SettingsViewModel` | SettingsView | 各组设置值、脏标记 | ISettingsService | ✅ |
| `InstanceWizardViewModel` | 向导 | 探测结果、选中路径、校验错误 | IInstanceService | ✅ |
| `UnmanagedViewModel` | Unmanaged | 文件树、勾选、接管预览 | IUnmanagedService | M4 |
| `HistoryViewModel` | History | 历史条目列表、按时间分组 | IRegistryService | M4 |
| `StatsViewModel` | Stats | 统计表 | ISettingsService | M4 |
| `ModPackViewModel` | 导入导出 | 解析结果、将装/将卸清单 | IChangeSetService | M4 |
| `OperationQueueViewModel` | 浮层 | 任务列表、每个任务的进度/状态/取消 | IOperationQueue | ✅ |
| `DialogViewModelBase` + 各对话框 VM | 对话框 | 标题、按钮、校验 | IDialogService | ✅ |

### 2.1 列表投影（性能关键）

`ModsViewModel` 不持有 `GUIMod`/`CkanModule` 的包装对象数组，而是：

```
IRepositoryService.AvailableSnapshot   →  一次性拉取（启动时后台）
    ↓ 变换（纯函数，可测试）
IReadOnlyList<ModRowData>              →  不可变记录 struct/record
    ↓ 过滤 + 排序（在后台线程，100ms 防抖）
IReadOnlyList<ModRowData> filtered     →  DataGrid 的 ItemsSource
    ↓ 按需
ModRowViewModel                        →  仅为可见区域创建（虚拟化）
```

- `ModRowData` 用 `readonly record struct`（8 字段以内）或 `sealed record`，避免 3,000 个 `ObservableObject` 的分配开销；
- 行的可观察性放在 `ModRowViewModel`，只为可视区域实例化（Avalonia `DataGrid` 虚拟化 / 自绘列表）；
- 排序与过滤在**后台线程**执行，结果整体替换 `ItemsSource`（一次 `SetProperty`），不做增量 CollectionChanged（3,000 项的 CollectionChanged 是卡顿主因）。

## 3. 服务层接口清单

| 接口 | 实现要点 | 是否封送 |
|---|---|---|
| `IDispatcher` | `Invoke`/`Post`/`CheckAccess` | — |
| `IUser` 实现 | 见 02 篇 §4 | 是 |
| `IInstanceService` | 包装 `GameInstanceManager`；含切换序列、锁定检测 | 是 |
| `IRepositoryService` | 包装 `RepositoryDataManager`；暴露 `AvailableSnapshot`、`RefreshAsync`、`Updated` 事件 | 是 |
| `IRegistryService` | 包装 `RegistryManager`；暴露 `InstalledSnapshot`、`Changed` 事件、`Open(inst)` | 是 |
| `IChangeSetService` | 用 `RelationshipResolver` 计算；输入=用户选择+当前注册表；输出=`ChangeSet` | 是 |
| `IInstallerService` | 封装 `NetAsyncModulesDownloader` + `ModuleInstaller.InstallList`；输出进度流与结果 | 是 |
| `ISettingsService` | 包装 `IConfiguration`，串行化写 | 否 |
| `IUiSettingsStore` | 读写 `ckan-ui.json`（窗口几何、列宽、上次页） | 否 |
| `IOperationQueue` | 应用级任务队列，聚合所有进行中操作 | 内部封送 |
| `IDialogService` | 由 VM 发起的模态交互（返回 Task<bool>/Task<int>） | 是 |
| `IErrorReporter` | 统一异常上报 → 错误条 + 日志 | 是 |
| `IUnmanagedService` | 扫描 GameData 与注册表差异（M4） | 是 |

## 4. Model 复用决策（逐文件）

| 现有文件 | 行数 | 决策 | 说明 |
|---|---|---|---|
| `GUI/Model/ModSearch.cs` | 716 | **直接搬** | 仅 `System.*`，搜索语法解析，纯函数 |
| `GUI/Model/ModChange.cs` | 192 | **直接搬**（`:50 class ModChange`） | 变更集模型；命名空间改为 `CkanUI.Services.ChangeSets` |
| `GUI/Model/GUIMod.cs` | 399 | 近似直接搬 | `using System.Windows.Forms` 实际未用符号；需去掉 Designer 相关属性 |
| `GUI/Model/ModList.cs` | 707 | **解耦重写** | `:67` 以 `Dictionary<string, DataGridViewRow>` 为数据容器、`:144` `MakeRow` 产出 WinForms 行；把其中的"过滤/排序/标签判定/更新检测"抽成纯函数搬到 `ChangeSets` 与 `ModIndex` |
| `GUI/Model/GUIConfiguration.cs` | 274 | 重写 | 窗口几何/列宽改走 `IUiSettingsStore`；其余映射到 `IConfiguration` |
| `GUI/Model/ModTag.cs`、`ModuleLabel` 相关 | 少量 | 直接搬 | 注意 `Core/Types/Labels/ModuleLabel.cs:31` 的 `Color?` |
| `GUI/Model/ModUpdateSummary.cs` 等 | 少量 | 直接搬 | 纯数据 |

**搬运规则**：搬过来的文件必须①改命名空间②补 `nullable` 注解③加单元测试（原文件在 GUI 里几乎没有测试覆盖）。搬运后 **不要**同时保留两处实现——老 GUI 那边不动，但新仓库里的副本就是唯一事实。

## 5. RFC：变更集模型下沉到 Core（可选，D6 之后）

### 现状重复

| 位置 | 行数 | 内容 |
|---|---|---|
| `GUI/Model/ModChange.cs` | 192 | `ModChange`（`:50` 类定义）+ 状态枚举 |
| `ConsoleUI/ModListScreen.cs:850` | ~100 | `class ChangePlan` 等价实现 |

约 200 行逻辑被写了两遍，含义一致但不共享：都是「(模组, 从版本, 到版本, 动作)」的集合，都要处理"用户显式选择 vs 依赖隐式带入"。

### 提案

在 `Core/Types/` 下新增 `ChangeSet.cs`：

```csharp
namespace CKAN
{
    public enum ChangeAction { Install, Upgrade, Downgrade, Remove, Replace }
    public enum ChangeOrigin { UserRequested, Dependency, Recommendation, AutoInstalled }

    public sealed record ChangeItem(
        CkanModule    Module,
        ChangeAction  Action,
        ModuleVersion? FromVersion,
        ChangeOrigin  Origin);

    public sealed class ChangeSet : IEnumerable<ChangeItem>
    {
        public IReadOnlyCollection<ChangeItem> Items { get; }
        public bool IsEmpty { get; }
        public bool HasConflicts(out IReadOnlyList<string> reasons);
        public long EstimatedDownloadBytes { get; }
        public static ChangeSet From(GameInstance inst, Registry registry,
                                     IEnumerable<CkanModule> toInstall,
                                     IEnumerable<CkanModule> toRemove,
                                     RelationshipResolverOptions options);
    }
}
```

### 收益 / 代价

| 收益 | 代价 |
|---|---|
| 消除 200 行重复；CLI 也能输出结构化变更 | Core 变更需过上游评审（若目标是上游 merge） |
| 新 UI 与 ConsoleUI 行为必然一致 | 需要给 Core 加测试（`Tests/` 里新增约 150 行） |
| `EstimatedDownloadBytes` 可复用 | 短期不阻塞 UI（可先复制） |

**执行时机**：M2 完成、变更集行为被真实用户验证后，再提 RFC。M2 之前**复制**（决策 D6）。

## 6. 服务层数据结构（DTO）

| DTO | 字段 | 用途 |
|---|---|---|
| `ModRowData` | `Identifier, Name, Author, InstalledVersion?, LatestVersion?, DownloadCount, Tags[], SizeBytes?, IsInstalled, HasUpdate, IsCompatible, IsAutoInstalled, IsSelected, IsImplicit` | 列表行投影 |
| `RegistrySnapshot` | `Version, IReadOnlyDictionary<string,InstalledModuleInfo>, IReadOnlySet<string> Dlls, DateTime TakenAt` | 注册表只读快照 |
| `RepositorySnapshot` | `IReadOnlyList<ModRowData> Available, IReadOnlyList<string> RepoIds, DateTime UpdatedAt` | 索引快照 |
| `ChangeSetPreview` | `IReadOnlyList<ChangeItemDto>, Conflicts[], long EstimatedBytes` | 变更集预览 |
| `OperationProgress` | `TaskId, Kind, Percent, Message, CanCancel, State` | 进度 |

**快照的意义**：ViewModel 永不持有 `CkanModule` 的活引用；详情面板按 `Identifier` 回查快照，快照整体替换时详情面板自动失效重取。这消除了"注册表变了但界面还显示旧对象"的一整类 bug。

## 7. 命令清单（每页的"动作"）

| 命令 | 所属 | 异步 | 可并发 | 幂等 |
|---|---|---|---|---|
| `RefreshRepositories` | MainWindow | ✅ | 否（重入即返回） | ✅ |
| `Search`（内部，防抖） | Mods | — | — | ✅ |
| `ToggleSelection(id)` | Mods | 否（触发后台重算） | ✅ | ✅ |
| `ApplyChanges` | Changes | ✅ | 否 | 否（二次点击忽略） |
| `CancelOperation(taskId)` | Operations | ✅ | ✅ | ✅ |
| `SwitchInstance(name)` | Main | ✅ | 否 | ✅ |
| `AddInstance` / `CloneInstance` / `RemoveInstance` | Instances | ✅ | 否 | 否 / 否 / ✅ |
| `SaveSetting(key, value)` | Settings | ✅ | 串行 | ✅ |
| `ImportModpack(path)` | Changes | ✅ | 否 | ✅ |

## 8. 一份 ViewModel 骨架（示范 ModsViewModel 的形态）

```csharp
public sealed partial class ModsViewModel : ObservableObject, IActivatable
{
    private readonly IRepositoryService _repo;
    private readonly IRegistryService _registry;
    private readonly IChangeSetService _changes;
    private IReadOnlyList<ModRowData> _all = Array.Empty<ModRowData>();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ModRowData> _rows = Array.Empty<ModRowData>();
    [ObservableProperty] private ModRowData? _selected;

    public void Activate()
    {
        _repo.Updated      += OnRepositoryUpdated;      // 已封送到 UI 线程
        _registry.Changed  += OnRegistryChanged;
        _all = _repo.Current.Available;                 // 快照
        ApplyFilter();
    }

    public void Deactivate()
    {
        _repo.Updated     -= OnRepositoryUpdated;
        _registry.Changed -= OnRegistryChanged;
    }

    partial void OnSearchTextChanged(string value) => ScheduleApplyFilter(150);

    private async void ScheduleApplyFilter(int debounceMs) { /* 取消上一个 → 后台过滤 → UI 整体替换 */ }
    private void ApplyFilter() => Rows = Filtering.Apply(_all, SearchText, ActiveFilters);
}
```

## 9. 附：全特性 ViewModel 清单（v1.2 回填，依据 [12 篇](12-feature-parity.md)）

> §2 的清单是按"MVP 页面"列的；本节的 24 个 VM 是"**完全特性对齐**"后新增的（WBS `VM-06` 16h 为公共基座，其余随各 `UI-2x/3x` 任务交付）。约定不变：VM 不引用 Avalonia，全部可 headless 测试。

| VM | 承载页面/对话框 | 关键状态与方法 | 对应 F-ID | 归属任务 |
|---|---|---|---|---|
| `InstancesViewModel` | 页面「实例」 | 实例列表、默认实例、`Select/SetDefault/Forget`、锁定状态 | F-01..04 | UI-05, UI-32 |
| `InstanceCloneViewModel` | `InstanceCloneDialog` | 源/目标路径、新名、共享 stock、可选路径集合、校验 | F-02 | UI-32 |
| `InstanceEditViewModel`（新增/重命名） | 小对话框 | 路径探测、重名检查、Steam 库扫描结果 | F-01/03 | UI-32 |
| `CommandLinesViewModel` | `CommandLinesDialog` + Play 菜单 | 命令行条目、默认重置、当前选中项 | F-05/06 | UI-28 |
| `RepositoriesViewModel` | 页面「仓库」 | 有序仓库表、增删改、上下移、官方预设 | F-10 | UI-06 |
| `AuthTokensViewModel` | 设置页「认证令牌」组 | host/token 列表、脱敏显示、增删 | F-11 | UI-34 |
| `CacheViewModel` | 设置页「下载缓存」组 | 文件数/总容量、上限 MiB、`PurgeToLimit/PurgeAll/Reset/Open` | F-50 | UI-19 |
| `CompatibleVersionsViewModel` | `CompatibleVersionsDialog` | 当前版本、追加版本集合、通配展开预览、警示开关 | F-14 | UI-29 |
| `PreferredHostsViewModel` | `PreferredHostsDialog` | 可用（按热度）/首选（按优先级）两列表、拖动重排 | F-15 | UI-30 |
| `InstallFiltersViewModel` | `InstallFiltersDialog` | 全局/实例两组规则、校验、生效范围提示 | F-16 | UI-31 |
| `PluginsViewModel` | 页面「插件」 | Active/Dormant 两栏、`Activate/Deactivate/Reload/Unload/AddNew`、来源 dll 路径 | F-17 | UI-33 |
| `SearchBuilderViewModel` | `SearchBuilderFlyout` | Search/Or 两组、18 个字段值、生成的 `ModSearch` | F-20/21 | UI-14 |
| `LabelsViewModel` | `LabelsDialog` + 右键子菜单 | 标签集合、10 项属性、颜色、导出、应用顺序 | F-23/24 | UI-24b |
| `ProvidedModsViewModel` | `ChooseProvidedDialog` | 候选组、单选、确认后的 `List<CkanModule>` | F-34 | UI-16 |
| `RecommendationsViewModel` | `RecommendationsDialog` | 三组候选、批量勾选策略、`AlwaysUncheckAll` 持久化 | F-35 | UI-15 |
| `RecommendationAuditViewModel` | 工具组页面 | 遍历已装 mod 的推荐/建议、差异清单、批量采纳 | F-36 | UI-17 |
| `DownloadsFailedViewModel` | `DownloadsFailedDialog` | 失败项、逐项 Retry/Skip、错误文本、复制 | F-38 | UI-35 |
| `LeftoverDirsViewModel` | `DeleteLeftoverDirsDialog` | 目录→内容映射、勾选、全部保留 | F-43 | UI-22 |
| `InstallHistoryViewModel` | 页面「安装历史」 | 时间点列表、已装/未装分组、`RestoreTo(timestamp)` | F-44 | UI-23 |
| `ExportViewModel` | 导出向导（两步） | 导出类型、已装集合、`ExportOption`、modpack 元数据 | F-45/46 | UI-24 |
| `ModpackViewModel` | `ExportModpackDialog` | 依赖分组、单 mod 关系单选、`SaveVersions`/`IncludeOptional` | F-46 | UI-24 |
| `StatisticsViewModel` | 页面「下载统计」 | 数据聚合、排序、图表数据点 | F-47 | UI-25 |
| `PlayTimeViewModel` | 页面「游戏时长」 | 实例时长、就地编辑、校验与写回 | F-48 | UI-10 |
| `UnmanagedViewModel` | 页面「未托管文件」 | 树构建、展开状态、接管/删除 | F-42 | UI-10 |
| `TrayViewModel` | 托盘（无窗口） | 菜单命令绑定、最小化策略、气泡提示 | F-51 | UI-26 |
| `UpdateViewModel` | 设置页「自动更新」组 + 提示条 | 当前/最新版本、频道（稳定/dev）、检查与安装流程 | F-52 | UI-27 |
| `DeduplicateViewModel` | 工具组页面 | 跨实例扫描进度、可共享文件预览、执行 | F-41 | UI-21 |
| `ImportViewModel` | 导入流程 | `.ckan`/`.zip` 批量选择、结果汇总 | F-39/40 | UI-20 |

**约定三条**（新增 VM 必须遵守）：

1. **对话框 VM 也走 `IDialogService` 契约** —— 与 Core 的同步回调（`RaiseYesNoDialog`/`RaiseSelectionDialog`）共用同一条封送通道（05 篇 §8），不为对话框单开线程模型；
2. **长任务一律"命令 → 服务 → 进度事件"**，VM 只订阅，不自己 `Task.Run` 干业务；
3. **VM 的每个可写属性都要能写回 Core**（`ISettingsService`/`*Service`），禁止只在内存里改——否则"完全特性"会退化成"看起来有"。

## 10. 存档工作台的 ViewModel（E1，v1.4）

> 与 [13 篇](13-savegame-sfs.md) / [03 篇](03-ui-spec.md) §14 配套。子系统**不依赖 Core**（纯文本工具），因此这些 VM 的依赖只有 `ISfsService` / `ISaveIndexService` / `IDialogService` / `IProcessProbe`，可完全 headless 测试（用真实 `.sfs` 夹具，不需要 KSP）。

| VM | 页面 | 职责 | 关键状态 | 任务 |
|---|---|---|---|---|
| `SavesViewModel` | 存档列表 | 加载槽位列表、排序、行操作（打开/快照/删除/格式化入口）、KSP 运行警示 | `IsGameRunning`、`Items`、`SortKey` | UI-SAVE-01 |
| `SaveDetailViewModel` | 存档详情壳 | 三视图切换、脏标记、保存/放弃、共享 `SfsDocument` 生命周期 | `ActiveTab`、`IsDirty`、`CanWrite` | UI-SAVE-01/06 |
| `SfsTreeViewModel` | 树视图 | 节点树投影、懒展开、就地编辑、路径搜索、增删键、复制子树 | `SelectedPath`、`ExpandedPaths`、`Filter` | UI-SAVE-02 |
| `SfsTextViewModel` | 原始文本 | 分词着色、行号、可选编辑、定位到树节点、大文件分块 | `IsEditable`、`CaretPath` | UI-SAVE-03 |
| `SfsDiffViewModel` | 差异 | 三源对比（草稿/磁盘/快照或 A/B）、节点级折叠、逐项采用 | `LeftSource`、`RightSource`、`OnlyChanges` | UI-SAVE-05 |
| `FormatPreviewViewModel` | 格式化预览 | `--check` 结果、改动统计、确认写盘 | `DiffLines`、`Stats`、`PreserveTimestamp` | UI-SAVE-04 |

**约定（与 §9 三条一致的扩展）**：

1. **禁止 VM 直接 `File.*`**：所有读写经 `ISaveWriter`（内部做快照 + 原子替换 + 时戳），这样"安全网"只有一个实现点；
2. **`SfsDocument` 是唯一真相**：树视图与文本视图共用同一实例，任一视图改动后另一视图通过 `DocumentChanged` 事件刷新，避免两套缓冲区打架；
3. **写盘前的门禁在服务层，不在 VM**：`validate → 结构自校验 → 快照 → 原子写` 四步由 `ISaveWriter.Write()` 一次性完成，VM 只能调用它，不能绕开。

---

## §11 E2 日志与 AI 排障的 ViewModel 清单（v1.6 新增，依据 [14 篇](14-log-and-ai-debug.md)）

| VM | 职责 | 关键成员（设计稿） | 依赖的服务接口 |
|---|---|---|---|
| `LogViewModel` | 主列表、聚合、时间轴、跟随末尾 | `ObservableCollection<LogRowViewModel> Rows`、`bool FollowTail`、`long? SelectedSeq`、`ScrollTo(long seq)` | `ILogStream` |
| `LogFilterViewModel` | 三段过滤 + 搜索语法解析 | `ISet<LogLevel> Levels`、`ISet<LogFamily> Families`、`ISet<LogSource> Sources`、`string Query`、`CompiledFilter Filter` | — |
| `AiAnalysisViewModel` | 预览 → 发送 → 结构化结果 → 证据回链 | `string PayloadPreview`、`int EstimatedTokens`、`decimal EstimatedCost`、`IReadOnlyList<HypothesisViewModel> Results`、`bool IsStreaming`、`string? Error` | `IDiagnosticsBundler`、`IAiClient`、`IAiCache` |

**约定（与 §9 三条一致的扩展）**：

1. **VM 不直接发 HTTP**：`IAiClient` 藏在服务层，便于用假端点做 headless 测试（`07` 篇 T-LOG-05/06/08）；
2. **VM 不持有明文 Key**：Key 只在 `ISecretStore`（DPAPI 实现）内部流动，VM 只见 `bool HasKey` 与尾 4 位；
3. **`LogEvent` 不可变**：VM 里只放投影行（`LogRowViewModel`），不改事件本身——否则同一事件在多视图（主列表/时间轴/详情）会不一致；
4. **级别判定在采集层，不在 VM**：VM 只做"过滤/分组/格式化"，保证 headless 可测且与 UI 无关。

---

## §12 E3 Mod 配置的 ViewModel 清单（v1.5 新增，依据 [15 篇](15-mod-config-adapters.md)）

| VM | 职责 | 关键成员（设计稿） | 依赖的服务接口 |
| --- | --- | --- | --- |
| `ModConfigListViewModel` | 左列卡片（名字、版本、状态徽标、`N 项已改`）、无适配器分组 | `ObservableCollection<ModConfigCardViewModel> Cards`、`int AdaptorlessCount` | `ModConfigDetector` |
| `ModConfigEditorViewModel` | 表单、分组、搜索、"仅看已改"、diff 条、写入 | `IReadOnlyList<ConfigFieldViewModel> Fields`、`bool OnlyChanged`、`int PendingCount`、`Task WriteAsync()` | `IConfigDocumentService`、`IAdapterWriter` |

**约定（与 §9/§11 一致的扩展）**：

1. **VM 不做版本判定**：`modVersionRange` 的判定在 `ModConfigDetector`（纯函数，可单测），VM 只消费结果三态；
2. **VM 不说话文件**：所有读写经服务；`PendingCount` 来自"内存模型 vs 磁盘快照"的比较，不重新读盘；
3. **写入是原子的**：`WriteAsync` 成功后重新加载文档并清空改动集，避免"写了一半"的中间态；
4. **只读态不可绕过**：VM 的 `CanWrite` 由 detector 的状态直接决定，UI 不做二次判断。

---

## §13 E4 本地化的 ViewModel 约定（v1.6 新增，依据 [16 篇](16-i18n.md)）

| VM | 职责 | 关键成员 | 说明 |
| --- | --- | --- | --- |
| `SettingsViewModel`（扩展） | 语言选择 + 覆盖率显示 | `ObservableCollection<LocaleOption> Languages`、`LocaleOption? SelectedLanguage` | 切换即写 `Config.Language` 并触发 `LanguageChanged` |

**四条约定**：

1. **VM 不产出显示文案**：VM 暴露**键名与参数**（如 `TitleKey`、`CountTextKey + Count`），由视图用 `{loc:T}` 解析；
2. **不缓存已解析字符串**：任何 `string` 属性若来自 `ILocalizer.Get`，必须随 `LanguageChanged` 重算（推荐直接暴露键名，交给绑定）；
3. **`ILocalizer` 可注入 VM**（用于构造 `Format` 参数），但**不得**在 VM 里保存解析结果；
4. 语言是**全局状态**，不进 `ckan-ui.json`，不参与撤销/重做。

## §14 E5 元数据译文相关 VM（v1.2 新增）

| VM | 职责 | 关键成员 |
|---|---|---|
| `ModTextViewModel` | 单个 mod 的摘要展示态：译文/原文切换、徽标、按需翻译命令 | `Text`、`OriginalText`、`ShowOriginal`、`Badge`（`Human`/`Machine`/`Stale`）、`TranslateCommand` |

三条约定：

1. **VM 只读 provider**：`IModTextProvider` 是只读服务，VM 不得自己解析 jsonl 或访问包目录；
2. **切换原文是视图状态**：`ShowOriginal` 存进 `ckan-ui.json` 的视图偏好，**不进** `Config`、不影响 Core；
3. **按需翻译必须显式命令**：`TranslateCommand` 由用户点击触发；自动机翻只允许发生在同步队列（后台服务），VM 不得在构造时偷偷发请求。

## §15 E6 相关 ViewModel（v1.3 新增）

| VM | 职责 | 关键成员 |
|---|---|---|
| `GameLaunchViewModel` | 启动游戏的状态机：阶段（启动中/让路中/精简/已恢复）、进度、读数字符串、错误 | `Stage`、`ProgressText`、`FreedText`、`IsCompactMode`、`RestoreCommand`、`CancelReleaseCommand` |
| `ResourceReportViewModel` | 五阶段读数展示（诊断页用） | `Samples`（只读集合）、`Delta`、`ExportCommand` |

三条约定：

1. **VM 不碰 `Process`**：进程/窗口探测全在 `IGameLaunchMonitor`，VM 只消费事件；
2. **读数字符串由 VM 格式化**（本地化 + 单位），服务层只给 `long`；
3. **精简模式下所有"会写"的命令必须 `CanExecute=false`**，且理由文本可见（不是静默禁用）。

## §16 E7 相关 ViewModel（v1.4 新增）

依据 [19 篇](19-windows-install-flow.md) §12。

| ViewModel | 来源 | 职责 | 覆盖 |
| --- | --- | --- | --- |
| `SetupWizardViewModel` | 首启向导 | 三步状态机（实例→语言→集成）、可跳过、完成写 `ckan-ui.json` 的 `setupCompletedUtc` | I-01, I-04 |
| `InstancePickStepViewModel` | 向导第 1 步 | 列表 + 添加目录 + 校验（`GameInstanceManager.AddInstance`） | — |
| `ShellIntegrationViewModel` | 向导第 3 步 / 设置 → 集成 | 协议、文件关联两个开关的真实状态（读注册表由 `IShellIntegration` 提供）、修复/移除、旧版 HKLM 检测结果 | I-04 |
| `AboutViewModel` | 关于页 | 版本、安装路径、安装方式、检查更新、卸载入口、打开数据目录、复制诊断信息 | I-02, I-08 |

四条约定：

1. 向导**不直接读写注册表**，全部经 `IShellIntegration`（返回 `ShellIntegrationState` 值对象，便于 headless 测试）；
2. 「卸载」只调 `Process.Start(UninstallString)` 并要求二次确认，**不在进程内删文件**；
3. 「检查更新」的分支由 Velopack `UpdateManager` 提供，VM 只消费结果（`UpdateInfo?`），频道选择（稳定/dev builds）映射到 F-52；
4. 向导完成后不重复弹：判定条件是 `ckan-ui.json` 的 `setupCompletedUtc` **或** 已配置实例且已注册协议，两者任一成立即视为完成。


## §17 E8 外观相关 ViewModel（v1.5 新增）

依据 [20 篇](20-appearance-personalization.md) §13。

| ViewModel | 职责 | 关键点 |
| --- | --- | --- |
| `AppearanceViewModel` | 设置页宿主：加载/保存 `AppearanceSettings`，分组恢复默认，导入导出 | 保存去抖 300ms；失败回滚到上一份快照 |
| `ThemePickerViewModel` | 变体三选 + 主题包列表/启用/回滚/删除 | 主题包校验结果带原因码 |
| `AccentColorViewModel` | 系统/预设/自定义 + 派生色阶 + 对比度徽标 | 对比度计算在服务层（`IContrastService`），VM 只展示 |
| `MaterialViewModel` | 材质三选 + 实际档位与原因 | 只读属性来自 `Backdrop` 探测结果 |
| `BackgroundViewModel` | 来源/填充/暗化/模糊/不透明度 + 选图 + 按实例 | 选图走 `IFilePickerService`；预览用缩略图不加载原图 |
| `TypographyDensityViewModel` | 密度三档 + 字号五档 + 动效三档 | 改字号要通知虚拟化列表刷新行高 |

三条约定：①所有外观状态**单一真相**在 `AppearanceSettings`（不可变记录，改一份换一份），避免多处写同一字段；②VM 不直接改 `Application.RequestedThemeVariant`，经 `IThemeService.Apply()` 一次性提交；③外观设置**不进** Core 的 `config.json`（那是 ckan.exe 的地盘），只进 `ckan-ui.json`。
