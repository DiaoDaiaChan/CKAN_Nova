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
| `IRegistryService` | `IRegistryService.cs` | `Current` 快照 + `Changed` + `OpenAsync`/`CloseAsync` |
| `IRepositoryService` | `IRepositoryService.cs` | `Current` 快照 + `Updated` + `LoadCachedAsync`/`RefreshAsync` |
| `IChangeSetService` | `IChangeSetService.cs` | `BuildAsync(ChangeRequest[])` / `BuildLocal` |
| `IInstallerService` | `IInstallerService.cs` | `ApplyAsync(ChangeSetPreview, IProgress<OperationProgress>, ct)` → `InstallReport` |
| `ISettingsService` | `ISettingsService.cs` | 字符串键值 + `SetAndVerifyAsync` 读回校验 |
| `IUiSettingsStore` | `IUiSettingsStore.cs` | `UiSettings` 记录 + `Load`/`Save`（原子写） |
| `IOperationQueue` | `IOperationQueue.cs` | `Enqueue`/`Cancel`/`Operations`/`Updated`（100ms 节流） |
| `IDialogService` | `IDialogService.cs` | `ConfirmAsync`/`SelectAsync`(-1=取消)/`ShowErrorAsync`/`RunWithProgressAsync` |
| `IErrorReporter` | `IErrorReporter.cs` | `Report` + `Reported` 事件 + `Recent` |
| `IUnmanagedService` | `IUnmanagedService.cs` | M4，先留契约 |

### DTO（`src/CkanUI.Services.Abstractions/Dtos/`）

`ModRowData`（readonly record struct）、`InstalledModuleInfo`、`RegistrySnapshot`、`RepositorySnapshot`、
`ChangeAction` / `ChangeOrigin` / `ChangeItemDto` / `ChangeSetPreview`、`OperationState` / `OperationProgress`、`InstanceInfo`。

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

## 修订记录

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-27 | v1.0 | 新建：冻结 13 个服务接口 + 8 组 DTO，登记实现归属 |
| 2026-10-27 | v1.1 | 新增 §4 实现决策：InstanceService 构造形状、取消判定、锁定检测与回滚 |
