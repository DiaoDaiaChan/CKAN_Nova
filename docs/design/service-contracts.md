# 服务层契约（Lead 冻结，2026-10-27）

> 真相源：`docs/plan/04-mvvm-and-models.md` §3/§6、`docs/plan/02-core-integration.md` §4/§5/§6、`docs/plan/05-threading.md` §2。
> 本文件只登记**已落地**的接口与 DTO，以及谁负责实现。改接口要通知 Lead（契约冻结，避免并行时互相踩）。

## §1 已落地的契约

全部在 `src/CkanUI.Services.Abstractions/`，该工程**零 ProjectReference、零 PackageReference**（架构测试会红）。

| 类型 | 文件 | 说明 |
| --- | --- | --- |
| `IDispatcher` | `IDispatcher.cs` | `IsOnUiThread` / `Post` / `InvokeAsync(Action)` / `InvokeAsync<T>(Func<T>)` |
| `IActivatable` | `IActivatable.cs` | `Activate()` / `Deactivate()`，View 在 `OnAttachedToVisualTree` 调用 |
| `OperationResult<T>` | `OperationResult.cs` | `Ok` / `Fail(message, detail, retryable)` |
| `IInstanceService` | `IInstanceService.cs` | 实例枚举/切换/增删克隆；切换序列内部保证 |
| `ISteamLibraryLocator` | `ISteamLibraryLocator.cs` | `DiscoverGameDirsAsync`：注册表 `SteamPath` 优先，再 `config/libraryfolders.vdf`、旧版 `steamapps` 版；IO 失败可重试、取消单独收敛（F-03 / UI-32） |
| `IRegistryService` | `IRegistryService.cs` | `Current` 快照 + `Changed` + `OpenAsync`/`CloseAsync` |
| `IRepositoryService` | `IRepositoryService.cs` | `Current` 快照 + `Updated` + `LoadCachedAsync`/`RefreshAsync` |
| `IChangeSetService` | `IChangeSetService.cs` | `BuildAsync(ChangeRequest[])` / `BuildLocal` |
| `IInstallerService` | `IInstallerService.cs` | `ApplyAsync(ChangeSetPreview, IProgress<OperationProgress>, ct)` → `InstallReport` |
| `ISettingsService` | `ISettingsService.cs` | 字符串键值 + `SetAndVerifyAsync` 读回校验 |
| `IUiSettingsStore` | `IUiSettingsStore.cs` | `UiSettings` 记录 + `Load`/`Save`（原子写） |
| `IOperationQueue` | `IOperationQueue.cs` | `Enqueue`/`Cancel`/`Operations`/`Updated`（100ms 节流） |
| `IDialogService` | `IDialogService.cs` | `ConfirmAsync`/`SelectAsync`(-1=取消)/`ShowErrorAsync`/`RunWithProgressAsync` |
| `IErrorReporter` | `IErrorReporter.cs` | `Report` + `Reported` 事件 + `Recent` |
| `IUnmanagedService` | `IUnmanagedService.cs` | 未管理文件树扫描 / 预演 / 删除 / 接管为受管安装（F-42；接管见 §4.5） |
| `IModSearchParser` | `IModSearchParser.cs` | 搜索文本 → `ModSearchSpec`（语法权威仍是上游 `ModSearch`），并逐 token 报告无法解析项 |
| `IModPackService` | `IModPackService.cs` | 导出 modpack / 预览并执行 `.ckan` 导入 / 两实例差集比对（F-39·F-45） |
| `IDownloadService` | `IDownloadService.cs` | 字节级进度 + 大小/SHA256 校验 + 原子落盘 + 取消清理（M2） |
| `ISaveFileService` | `ISaveFileService.cs` | 存档枚举 / 解析诊断 / 格式化（M7/E1，SFS 内核之上） |
| `ICacheService` | `ICacheService.cs` | 缓存快照 / 条目表 / 按上限清理 / 全部清理 / 从 ZIP 灌缓存（F-50·F-40） |
| `IResidualCleanupService` | `IResidualCleanupService.cs` | 卸载残留目录候选 / 预演 / 删除（F-43，逐个跳过语义）；配套 `IResidualCleanupDialogService` |
| `IModDialogService` | `IModDialogService.cs` | 推荐选择 / 虚拟提供者选择 / 冲突明细（F-34..F-36，取消一律 null） |
| `ILabelService` | `ILabelService.cs` | 标签组读写（走上游 `ModuleLabelList`，落盘交 Core `Save(path)`）（F-24） |
| `IPlayTimeService` | `IPlayTimeService.cs` | 游戏时长读写（`%LocalAppData%/CKAN/play-time.json`）（F-48） |
| `IInstanceDedupService` | `IInstanceDedupService.cs` | 跨实例去重扫描与执行（F-41，六条安全边界在服务层） |
| `IInstallHistoryService` | `IInstallHistoryService.cs` | 安装历史读写与重装编排（F-44，JSONL 落盘） |
| `IPluginService` | `IPluginService.cs` | 插件发现 / 激活 / 停用 / 重载 / 卸载 / 新增 / 工具项回调（F-17，可回收 ALC） |

### DTO（`src/CkanUI.Services.Abstractions/Dtos/`）

`ModRowData`（readonly record struct）、`InstalledModuleInfo`、`RegistrySnapshot`、`RepositorySnapshot`、
`ChangeAction` / `ChangeOrigin` / `ChangeItemDto` / `ChangeSetPreview`、`OperationState` / `OperationProgress`、`InstanceInfo`、
`RepositoryEntryInfo` / `RepositoryEditRequest`、`ModPackExportRequest` / `ModPackExportResult` / `ModPackPreview` / `ModPackImportReport` / `InstanceComparison`（+ 行级 DTO）、
`DownloadTaskInfo` / `DownloadProgressInfo`、`SaveGameInfo` 与诊断 DTO、`CacheSnapshot` / `CacheEntryInfo` / `CachePurgeResult`、`ResidualDirectoryInfo` / `ResidualCleanupReport`、`ModDialogs`（推荐/提供者/冲突）、`LabelInfo` / `LabelEditRequest`、`PlayTimeEntry`、`DedupScanRequest` / `DedupGroup` / `DedupScanReport` / `DedupExecuteReport`、`InstallHistoryEntry` / `InstallHistorySnapshot` / `InstallHistoryRestorePlan`、`PluginInfo` / `PluginLoadFailure` / `PluginPageDescriptor`、
`UnmanagedFileInfo` / `UnmanagedAdoptOutcome` / `UnmanagedAdoptItem` / `UnmanagedAdoptResult`。

**铁律**：DTO 里不出现任何 `CKAN.*` 类型。VM 永不持有活的 `CkanModule` 引用，只按 `Identifier` 回查快照。

## §2 实现归属

| 契约 | 实现位置 | 归属任务 |
| --- | --- | --- |
| `IDispatcher` | `src/CkanUI.App/Threading/AvaloniaDispatcher.cs` | ARC-02 |
| `IUser`（Core 接口） | `src/CkanUI.App/Interop/AvaloniaUser.cs` | INT-03 |
| `IInstanceService` / `IRegistryService` / `IRepositoryService` | `src/CkanUI.Services/Instances`、`Registry`、`Repository` | INT-04/05/06 |
| `IChangeSetService` / `IInstallerService` | `src/CkanUI.Services/ChangeSets`、`Install` | INT-07/08 |
| `ISettingsService` / `IUiSettingsStore` | `src/CkanUI.Services/Settings` | INT-02 |
| `IOperationQueue` | `src/CkanUI.Services/Operations` | VM-05 |
| `IDialogService` / `IErrorReporter` | `src/CkanUI.App/Dialogs`、`src/CkanUI.Services/Errors` | INT-03 / ARC-04 |
| `IModPackService` | `src/CkanUI.Services/ModPacks`（Core 触达面收在 `IModPackStore` 后） | UI-20 / UI-24 |
| `IDownloadService` | `src/CkanUI.Services/Downloads`（字节搬运收在 `IDownloadTransport` 后） | M2 |
| `ISaveFileService` | `src/CkanUI.Services/Saves` | M7 / E1 |

## §3 契约的使用规则

1. **接口方法一律 async + CancellationToken**（除纯计算与属性）。
2. **事件一律"已在 UI 线程"**：由 Services 层用 `IDispatcher.Post` 封送后再触发；VM 不需要自己再封送。
3. **异常不穿过接口**：所有可失败操作返回 `OperationResult<T>`；意外异常由 `IErrorReporter` 兜底。
4. **快照整体替换**，不做增量通知（04 篇 §2.1：3000 项 `CollectionChanged` 是卡顿主因）。
5. 新增成员要同步改 `tests/CkanUI.Services.Tests` 的契约测试（`ContractShapeTests`）。

## §4 实现决策（Lead 裁定，实现 agent 照此办）

### §4.1 INT-04 InstanceService 的构造形状

契约只规定**行为**，不规定构造形状。实现依赖用构造函数注入，全部必需参数，禁止 optional/nullable 兜底（DI 组装根负责提供）：

```csharp
public InstanceService(
    GameInstanceManager manager,        // Core
    RepositoryDataManager repositoryData, // Core，RegistryManager.Instance 的第二参数
    IRegistryService registry,
    IOperationQueue operations,
    IDispatcher dispatcher)
```

- `RepositoryDataManager` 直接从 Core 注入：Services 层是唯一允许接触 CKAN 命名空间的层（01 篇 §5）。
- `IOperationQueue` 依赖**接口**即可，实现属 VM-05，暂由测试侧 `tests/CkanUI.TestSupport/FakeOperationQueue.cs` 承担（新建文件，不要改已有 `FakeDispatcher`）。

### §4.2 实例切换第 6 步"取消进行中操作"的判定

02 篇 §5 的原文是"下载可保留，安装必须取消"。落地规则：

- 遍历 `IOperationQueue.Operations` 快照，`OperationProgress.Kind` 前缀为 `install` 或 `registry` 的任务调 `Cancel(taskId)`；
- 前缀为 `download` 的**不取消**；
- 该前缀约定是 INT-05 与 VM-05 之间的临时契约，代码内必须写注释标明。

### §4.3 锁定检测与回滚

- `GameInstanceManager` 若无可用的"是否可能被锁"入口，改用实例目录下 lock 文件的独占打开检测；失败返回 `OperationResult.Fail("实例正在被另一个 CKAN 使用", ...)`，Retryable=false。
- 顺序铁律：`RegistryManager.DisposeAll()` 必须在**锁定检测之后、SetCurrentInstance 之前**。
- 回滚：切换过程放 try/finally，任一环节失败必须把旧实例恢复为当前实例并释放已取得的新锁。

### §4.4 导入导出与下载的分层（M2 裁定）

- **导出不自己拼 JSON**：`RegistryManager.GenerateModpack(recommends, with_versions).ToJson()` 直出，保证 `ckan import` 可消费；「打包缓存 zip」是本仓自定朴素归档（根 `.ckan` + `Downloads/<id>-<ver>.zip`）。
- **导入只做编排**：勾选 → `ChangeRequest` → `IChangeSetService.BuildAsync`（有冲突即中止且不调 `ApplyAsync`）→ `IInstallerService.ApplyAsync`；模块内零自有下载/解包逻辑。
- **下载两层**：`IDownloadTransport` 搬字节（生产 `NetAsyncDownloaderTransport`，测试用假传输层，测试绝不触网）；`DownloadService` 管落盘语义（`.tmp-{guid}` → 大小校验 → SHA256 校验 → `File.Move(overwrite:true)` 重试 → finally 删临时文件）。
- **失败分类产出 `retryable`；自动重试在 `DownloadService`**：`DownloadRetryPolicy`（默认 3 次，400ms→1200ms 递增）只重试网络类失败，
  重试**复用同一个临时文件**——传输层把已有字节数作为 `DownloadTargetFile.size` 交给 Core，由 `ResumingWebClient` 发 `Range` 续传。
  校验类（大小 / SHA256）与取消一律不重试；跨进程续传未做（临时文件名带 GUID）。手动「逐项重试」对话框（F-38 / UI-35）保留，
  它处理的是「自动重试用尽之后」的处置（F-38 / UI-35）。

### §4.5 未管理文件接管的写入路径（INT-09 裁定）

- **上游只有一条登记路径**：Core 的「接管」= 把 `.dll` 写进注册表 `installed_dlls`（标识符 → **GameDir** 相对路径，
  上游 `ScanUnmanagedFiles` 就是这么写的）。`Registry.SetDlls` 是**整体替换**语义，所以写入前必须自己合并既有映射，
  否则接管一个 DLL 会把其它手工安装的条目一起清掉。
- **写入收在一个接缝后面**：`IManagedFileRegistrar`（`src/CkanUI.Services/Unmanaged/IManagedFileRegistrar.cs`，
  服务层内部接缝，与 `IManagedPathProvider` 同层，不进 Abstractions）。唯一实现
  `src/CkanUI.Services/Unmanaged/CkanManagedFileRegistrar.cs` 是整个仓里唯一写注册表的类型，
  它负责「读既有映射 → 合并 → 生成 `registry.json.bak-yyyyMMdd-HHmmss` 快照 → SetDlls → `RegistryManager.Save()`」，
  Save 失败时同时还原内存与磁盘（快照覆盖）并返回可重试失败；`RegistryInUseKraken` 一律不可重试。
- **判定是纯函数**：`UnmanagedAdopter.Plan` 只依赖文件系统与已登记映射，输出逐项结局 + 待写映射。
  规则：只接管 `.dll`（目录递归展开）；路径必须是 GameData 内相对路径（复用 `UnmanagedScanner.NormalizeRelative`）；
  已受管的跳过（幂等）；批内标识符重复、或标识符已登记给**别的**路径时显式拒绝并给原因
  ——上游会静默丢掉冲突项，结果就是「接管了却没生效」。
- **`dryRun: true` 绝不写注册表**，只跑判定；VM 的「接管为受管」按钮走一次确认后直接写入，
  成功重扫并在错误条里聚合被拒绝项的原因（`UnmanagedAdoptResult.RefusedCount` / `Items[].Reason`）。
## 修订记录

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-28 | v1.8 | `IUnmanagedService.AdoptAsync` 改为返回 `UnmanagedAdoptResult`（逐项结局）并支持 `dryRun`；新增 §4.5 接管写入路径裁定与 `IManagedFileRegistrar` 接缝 |
| 2026-10-27 | v1.2 | 补 `ICacheService` / `IResidualCleanupService` / `IModDialogService` / `ILabelService` / `IPlayTimeService` / `IInstanceDedupService` / `IInstallHistoryService` / `IPluginService` 与对应 DTO |
| 2026-10-27 | v1.1 | 补 `IModPackService` / `IDownloadService` / `ISaveFileService` 与对应 DTO、实现归属、§4.4 导入导出与下载分层裁定 |
| 2026-10-27 | v1.0 | 新建：冻结 13 个服务接口 + 8 组 DTO，登记实现归属 |
| 2026-10-27 | v1.1 | 新增 §4 实现决策：InstanceService 构造形状、取消判定、锁定检测与回滚 |
