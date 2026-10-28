# F-52 自动更新：下载 → 校验 → 解包 → 备份 → 替换（含回滚）

> 规格来源：[12 全特性对齐清单](../plan/12-feature-parity.md) F-52、[06 WBS 与里程碑](../plan/06-wbs-milestones.md) UI-27。
> 检查侧（GitHub Releases / S3 Dev Builds、版本比对、弹窗）见 [自动更新检查](update-check.md)；实例侧故障处置见 [实例可靠性处置](instance-repair.md)。

---

## §1. 定位与边界

「发现新版本」之后原先是死路：只能打开发布页手动下载、手动解压覆盖。本特性把后半程补齐成一条可取消、可回滚、失败有原因的动作链。

| 层 | 文件 | 职责 |
|---|---|---|
| 契约 | `src/CkanUI.Services.Abstractions/Update/UpdateInstallerModels.cs`、`IUpdateInstaller.cs` | 安装计划/进度/结果 DTO 与单方法接口 |
| 服务 | `src/CkanUI.Services/Update/UpdateInstaller.cs` | 下载（复用既有下载端口）、校验、解包、备份、替换、回滚 |
| 表现层 | `src/CkanUI.ViewModels/Dialogs/UpdateAvailableDialogViewModel.cs`（:33 起） | 进度、状态、失败原因、安装/取消命令 |

网络口径：**不新建 `HttpClient`**，下载完全走已注册的 `IDownloadService`（`src/CkanUI.Services.Abstractions/IDownloadService.cs`）——它已有临时文件、重试、大小/哈希校验、进度节流与取消清理。安装器只负责「拿到一个合格的 zip 之后做什么」。

## §2. 契约

`IUpdateInstaller`（`src/CkanUI.Services.Abstractions/Update/IUpdateInstaller.cs:14`）：

```csharp
string UpdatesDirectory { get; }   // %LocalAppData%/CKAN/updates
string ProgramDirectory { get; }   // 默认 AppContext.BaseDirectory
Task<OperationResult<UpdateInstallResult>> InstallAsync(
    UpdateInstallPlan plan,
    IProgress<UpdateInstallProgress>? progress = null,
    CancellationToken ct = default);
```

- `UpdateInstallPlan(DownloadUrl, ExpectedSha256?, ExpectedSize, Version?, DisplayName?)`：`FileName` 从 URL 末段推出（非法字符替换为 `_`、无扩展名补 `.zip`、空/全点回落到 `ckan-update.zip`），`HasChecksum` 表示上游有没有给 SHA256。
- `UpdateInstallProgress(Stage, Percent, Message, BytesReceived, TotalBytes)`：`Stage` ∈ `Idle/Downloading/Verifying/Extracting/BackingUp/Replacing/Completed/Failed/Canceled`，`StageText` 给中文阶段名，`Percent` 统一夹在 0–100。
- `UpdateInstallResult`：`Stage`、`RestartRequired`、`RolledBack`、`Message`、`Detail`（多行技术细节）、`DownloadPath`、`ChecksumStatus`、`StagingDirectory`、`BackupDirectory`、`ProgramDirectory`、`ReplacedFiles`/`RestoredFiles`/`CreatedFiles`；`IsInstalled` = `Stage == Completed`。

构造：`UpdateInstaller(IDownloadService downloadService, string? updatesDirectory = null, string? programDirectory = null, Func<DateTimeOffset>? clock = null)`；只有 `downloadService` 必填，其余留给测试注入临时目录与固定时钟（DI 侧写 `new UpdateInstaller(downloadService)` 即可）。

## §3. 安装流程（`InstallAsync` :80）

1. **前置**：`plan == null` →「缺少更新包信息，无法安装。」；`DownloadUrl == null` →「更新包没有下载地址，无法安装。」；令牌已取消则直接「更新已取消。」且**一次下载请求都不发**。
2. **命名**：一次安装用一个时间戳 `yyyyMMdd-HHmmss` 串起三个路径——`updates/download-<stamp>-<FileName>`、`updates/staging-<stamp>`、`updates/backup-<stamp>`。
3. **下载**：`IDownloadService.DownloadAsync(new DownloadRequest(Urls: [plan.DownloadUrl], ExpectedSize, ExpectedSha256, "application/zip", DisplayName))`，进度转成 `Downloading` 阶段。
4. **校验**（§4）→ **解包**（§5）→ **备份+替换**（§6）。
5. **收尾**：成功统一返回「更新已就位，请重启应用以完成更新。」，`RestartRequired=true`；未处理异常一律 `Fail`（不抛给界面）。

## §4. 校验策略

| 上游是否给哈希 | 行为 | `ChecksumStatus` |
|---|---|---|
| 给了 SHA256 | `SHA256` 与期望值比对（忽略 `sha256:` 前缀、`-`、空格，大小写不敏感） | 「SHA256 校验通过。」 |
| 没给 | 只做大小校验，**明确记录**「未提供」而不是假装校验通过 | 「上游未提供 SHA256 校验值，已跳过完整性校验（只做了大小校验）。」 |
| 不一致 | 判定下载不可信：删掉下载文件并失败 | 「更新包 SHA256 校验失败，可能下载不完整或被篡改，已丢弃。」+ detail「预期…，实际…」 |
| 文件 0 字节 | 失败 | 「更新包为空文件，已丢弃。」 |
| 大小与 `ExpectedSize` 不符（且预期 >0） | 失败 | 「更新包大小与预期不符（预期 N 字节，实际 M 字节），已丢弃。」 |

## §5. 解包安全

解包到本次专用的 `updates/staging-<stamp>`（先删同目录残留再建），用 `ZipFile.OpenRead` 逐条处理：

- **条目数 0** →「更新包是空压缩包，已拒绝安装。」；**解出文件数 0** →「更新包里没有任何文件，已拒绝安装。」
- **路径穿越防护**（`TryResolveEntryPath` :315）：含 `..` 段、`Path.IsPathRooted` 为真、或落在 staging 之外的条目 →「更新包内含越界路径（可能是恶意包），已拒绝解压。」（detail 记完整条目名），**一条命中即整包拒绝**，不写任何越界文件。
- **不是 zip**（`InvalidDataException`）→「更新包不是有效的 zip 文件，已拒绝安装。」
- **剥壳**：GitHub Release 的 zip 通常套一层 `CKAN-vX.Y/` 目录。`ResolvePayloadRoot`（:349）在「只有一个子目录且没有并列文件」时逐层下探（最多 4 层），把真正的程序文件所在目录当载荷根；否则直接用 staging（不猜、不重排目录结构）。

## §6. 备份、替换与回滚

`ApplyPayload`（`InstanceRepairService` 之外的 `UpdateInstaller.cs:374`）两阶段：

1. **备份阶段**：对每个待替换的相对路径，若目标程序目录里已存在同名文件，`File.Copy` 到 `backup-<stamp>/<相对路径>`（保留子目录结构）。只备份「将被覆盖的」文件，不整目录快照——程序目录里可能有用户自己的配置，整目录复制既慢又无意义。
2. **替换阶段**：逐文件 `File.Copy(staging → 程序目录, overwrite: true)`，每条进度 `Replacing n/N`（百分比 = n/N）；本次新建的文件记入 `CreatedFiles`。
3. **不删除多余文件**：更新包里没有的程序文件（用户插件、日志、配置文件）一律保留——升级不是「镜像同步」。

**回滚**（`Rollback` :470）：任何一条替换失败或取消，都把已备份的文件按原路径还原、把本次新建的文件删除，返回 `RolledBack=true` 的回执：

| 触发 | 返回 |
|---|---|
| 取消（`OperationCanceledException`） | `Fail("更新已取消，已回滚到旧版本。")`，detail 记「已还原 N 个文件」，`retryable=false` |
| 其它异常（占用、权限、磁盘满） | `Fail("替换文件失败，已回滚到旧版本：" + ex.Message)`，detail 是完整异常，`retryable=true` |

失败详情统一拼成多行：「阶段=… / [已回滚] / 原始异常 / 更新包=… / 校验=… / 暂存=… / 备份=… / 已还原文件=N」，界面「详情」直接展示这一段，用户可据此贴 issue。

## §7. 界面落点（`UpdateAvailableDialogViewModel`）

- 视图：`src/CkanUI.App/Views/Dialogs/UpdateAvailableDialogView.axaml`（`x:DataType` 编译期绑定，属性名写错即构建失败）——安装区显示 `InstallAvailabilityText`、`ShowInstallProgress` 时的进度条（`Value` 绑 `InstallPercent`）+ 阶段文案、`HasInstallError` 时的错误条（`InstallErrorText`/`InstallErrorDetail`）、`HasRestartHint` 时的重启提示；底栏新增「安装此更新」（`CanInstall`）与「取消安装」（`CanCancelInstall` 时可见），主按钮仍是「关闭」。
- `InstallCommand`（:33，`AsyncRelayCommand(InstallAsync)`）、`CancelInstallCommand`（:36）。
- 宿主注入：`Installer`（`IUpdateInstaller?`）与 `InstallPlan`（`UpdateInstallPlan?`）都是可观察属性注入，**既有构造函数签名未变**，老用例不受影响。**DI 注册与弹窗接线由 Lead 完成**：注册 `IUpdateInstaller`（`new UpdateInstaller(downloadService)`），弹窗时把 `IUpdateInstaller` 与由 `UpdateCheckResult`/`ReleaseUrl` 推出的 `UpdateInstallPlan` 赋给 VM。
- 状态：`IsInstalling`、`InstallPercent`、`InstallStatusText`、`InstallErrorText`/`InstallErrorDetail`、`IsRestartRequired`；派生 `CanInstall`、`CanCancelInstall`、`ShowInstallProgress`、`HasInstallError`、`HasRestartHint`、`InstallAvailabilityText`。
- 可用性文案（为什么不能点）：没接安装器 →「自动安装未接线：可以打开发布页手动下载。」；上游没给可直接下载的资源 →「上游没有提供可直接下载的更新包地址：请打开发布页手动下载。」；可安装 →「将从 `<文件名>` 安装：先下载并校验 SHA256，替换前自动备份旧文件。」（无哈希版说明「只做大小校验」）。
- 失败必须可见：`SetInstallError` 同时写 `InstallErrorText`、`InstallErrorDetail` 与基类 `ValidationError`（`ErrorMessage` 契约不变），对话框错误条就能渲染；`OperationCanceledException` →「已取消安装，程序文件没有被替换。」；其它异常 →「安装更新失败：…」+ 完整堆栈进详情。
- 进度回调必须是**同步** `IProgress`（`InlineProgress<T>`）：`Progress<T>` 在无同步上下文的 headless 环境把回调丢线程池，界面会看到跳帧、单测也不确定。
- 主按钮文案保持「关闭」，安装是弹窗内的附加动作；装完只显示「请重启应用」提示，**不自动重启进程**（由用户决定何时重启，避免打断正在进行的下载/安装任务）。

## §8. 失败矩阵

| 场景 | 结果 | 界面 |
|---|---|---|
| 未接安装器 / 无下载地址 | 不调服务 | 错误条 + 可用性文案 |
| 网络失败（下载端口已分类） | `Fail(…)` `retryable=true` | 错误条「（可重试）」+ 详情 |
| 哈希不符 / 空文件 / 大小不符 | `Fail` 且删掉下载文件 | 同上，提示「已丢弃」 |
| 非 zip / 空压缩包 | `Fail` | 同上 |
| 路径穿越 | `Fail` 整包拒绝，staging 无越界文件 | 同上 |
| 替换中被占用 | 回滚后 `Fail(retryable=true)` | 「已回滚到旧版本：…」 |
| 用户取消（下载中或替换中） | 下载端口清临时文件 / 替换走回滚 | 「已取消安装，程序文件没有被替换。」 |
| 装完 | `Ok(Completed, RestartRequired=true)` | 进度 100% + 「请重启应用以完成更新。」 |

## §9. 测试落点

- 服务层 `tests/CkanUI.Services.Tests/UpdateInstallerTests.cs`（17 例，全离线）：`FakeDownloadService` 把预制的 zip 复制到目标路径（不联网），`TempWorkspace` 造程序目录/更新目录，固定时钟。覆盖替换 + 备份、全阶段进度（末次 100%）、SHA 通过、SHA 不符（下载文件被删）、未提供哈希（记录文案）、大小不符、zip-slip 拒绝且越界文件未落盘、非 zip、空压缩包、剥壳、不删无关文件、目标被同名目录占用时回滚、替换中取消回滚、预取消（不发下载请求）、下载失败可读、空计划/空地址、文件名推导。
- 表现层 `tests/CkanUI.ViewModels.Tests/UpdateInstallDialogTests.cs`（14 例）：`FakeUpdateInstaller` 可注入「同步返回 / 阻塞到取消 / 抛异常」三种行为，覆盖可用性判定、进度可见性、成功提示重启、服务失败原因、回滚原因、异常兜底、重复点击被忽略、安装中取消、空闲取消幂等、基类弹窗契约不变。
- 跑法：`dotnet build tests/CkanUI.Services.Tests/CkanUI.Services.Tests.csproj && dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.Services.Tests`；表现层换 `CkanUI.ViewModels.Tests`。

## §10. 已知限制与后续

1. **不校验代码签名**：只校验 SHA256（且仅当上游提供）。上游 Release 目前不附哈希文件，所以「未提供」是常态——文案已明确降级，不做「假装校验」。
2. **运行中的可执行文件无法就地替换**：Windows 会对正在运行的 `exe`/`dll` 加共享读锁，`File.Copy` 覆盖会抛 `IOException`，此时按 §6 回滚并给出可读原因。真正的原地升级需要独立 updater 进程（先退出主进程再替换），本轮不做；界面已提示「重启应用以完成更新」，非占用文件（多数 `dll`、资源）可先就位。
3. **只覆盖当前程序目录**：`ProgramDirectory` 默认 `AppContext.BaseDirectory`，不支持把更新装到别处；`updates` 目录固定在 `%LocalAppData%/CKAN/updates`。
4. **无断点续传、无差分更新**：整包下载；下载端口自带重试与临时文件清理。
5. **进度只有下载与替换两段百分比**：校验、解包阶段给阶段文案但不给细粒度百分比（解包耗时通常秒级）。
6. **安装后不自动重启**：只置 `RestartRequired`，避免打断用户正在进行的操作。
