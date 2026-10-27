# 跨实例去重（F-41 / UI-21 / INT-09）

## 1. 目标与范围

扫描多个游戏实例的 `GameData`，找出**内容相同**的文件，算出可释放空间，并在用户显式确认后把它们
改成硬链接（共享同一份磁盘数据）。规格出处：`doc/uir/12-feature-parity.md` F-41 行、`doc/uir/03-ui-spec.md` UI-21；
旧 WinForms 客户端口径参考 `GUI/Main/MainDeduplicate.cs` 与 `Core/IO/InstalledFilesDeduplicator.cs`。

继承了旧客户端的两个关键口径：

- **最小去重尺寸 128 KiB**（旧 `MinDedupFileSize = 128 * 1024`）：小文件收益低，且更容易被用户事后编辑，两个实例"共用"了用户以为独立的配置就会踩坑。
- **同卷才硬链接**：硬链接不能跨卷，旧实现先按卷（device identifier）分组再找重复，不同卷直接跳过而不是报错。

不做的部分：跨卷共享存储、压缩包内去重、按相似度（非完全相同内容）去重。

## 2. 契约层（CkanUI.Services.Abstractions）

`IInstanceDedupService`（零依赖，纯 BCL）：

- `Task<OperationResult<DedupScanReport>> ScanAsync(DedupScanRequest request, CancellationToken ct = default)`
- `Task<OperationResult<DedupExecuteReport>> ExecuteAsync(DedupExecuteRequest request, CancellationToken ct = default)`

DTO（`Dtos/DedupScan.cs`、`Dtos/DedupExecute.cs`）：

| 类型 | 关键字段 |
| --- | --- |
| `DedupScanRequest` | `InstanceNames`（空 = 全部实例）、`MinimumFileSizeBytes`（默认 128 KiB）、`IncludeManagedFiles` |
| `DedupCandidateFile` | `InstanceName`、`RelativePath`、`AbsolutePath`、`SizeBytes`、`VolumeId`、`IsManaged` |
| `DedupGroup` | `ContentHash`、`SizeBytes`、`VolumeId`、`Files`；派生 `FileCount`、`EffectiveDistinctCount`、`ReclaimableBytes`、`KeepFile` |
| `DedupScanReport` | `Groups`、`Skipped`（`DedupSkipInfo`：实例/相对路径/原因/中文原因）、`Support`、各项计数 |
| `DedupExecuteRequest` | `Groups`、`DryRun`（**默认 true**）、`IncludeManagedFiles`；`From(scanReport, dryRun)` |
| `DedupExecuteReport` | `DryRun`、`Support`、`Results`（`DedupLinkResult`）、`FreedBytes`、各类计数 |

`DedupSkipKind`：`TooSmall` / `Managed` / `Link` / `OutsideGameData` / `Unreadable` / `MissingInstance` /
`CrossVolume` / `Unsupported`——界面据此解释"为什么这个文件没被处理"，服务层**从不因为单个文件失败而抛异常**。

## 3. 安全边界（六条，服务层强制执行）

1. **只在 `<实例>/GameData` 之内**：绝对路径规范化后必须落在 GameData 前缀下，否则跳过（`OutsideGameData`）。
2. **不碰符号链接 / 重解析点**：扫描时判 `FileAttributes.ReparsePoint`，建链前再判一次（扫描与执行之间可能被改）。
3. **跳过受管路径**：`IManagedPathProvider.GetManagedPaths` 给出的相对路径参与比较，默认跳过
   （`Managed`）——受管文件由 CKAN 自己管理，把它改成硬链接会让"卸载某个实例的模组"删掉别的实例的数据。
4. **同卷**：组内所有文件的 `VolumeId` 必须一致且等于组上的 `VolumeId`，否则跳过（`CrossVolume`）。
5. **至少 2 个副本**：分组只在 `EffectiveDistinctCount >= 2` 时成立；`CountDistinct` 能用文件标识（inode）
   识别"其实已经是同一份文件"的路径，此时整组丢弃（已经去过重了，不需要再动）。
6. **默认 dry-run**：`DedupExecuteRequest.DryRun` 默认 `true`；只有显式传 `false` 且界面两次确认后才写盘。

## 4. 扫描流程（`Services/Dedup/DedupScanner.cs`）

1. 每个实例解析 `GameData` 并读取受管清单（读不到清单 → **整个实例**跳过并给出中文原因，不猜）。
2. 手工栈遍历目录：不跟进重解析点目录、跳过临时文件名前缀 `.ckanui-dedup-`、按 `FileAttributes` 跳过链接文件、
   受管路径跳过、`size < MinimumFileSizeBytes` 只计数不记录。
3. **先按大小分组**（不同大小不可能同内容），只对 ≥2 个副本的桶算 SHA-256（`IncrementalHash` +
   128 KiB 缓冲，逐块检查取消）。
4. **再按 (哈希, 卷) 分组**取 ≥2 的组；组内文件按 (实例名, 相对路径) `Ordinal` 稳定排序（同一份磁盘状态结果顺序一致，
   也决定谁当"保留副本"）；输出按 `ReclaimableBytes` 降序、`ContentHash` 升序。

## 5. 执行流程（`Services/Dedup/DedupExecutor.cs`）

- 平台不支持（`IDedupLinker.Support == UnsupportedPlatform`）→ 每个文件记 `Skipped` + 中文原因，`FreedBytes = 0`，**不抛异常**。
- 每组先选保留副本：组内第一个通过全部校验的文件；其余文件逐个校验：
  实例仍在 / 在 GameData 内 / 存在且是普通文件 / 非重解析点 / 非受管（除非放开） / 同卷 / 文件标识与保留副本身份不同。
- `DryRun` 通过校验的文件记 `Planned`（并把尺寸计入"可释放"）；真写盘则：
  1. 在**目标同目录**建临时名 `.ckanui-dedup-<guid>.tmp`；
  2. `CreateHardLink(临时名, 保留副本)`（先写临时名，避免中途失败留下半成品路径）；
  3. 两侧文件标识可读时校验相同，再 `File.Move(临时名, 目标, overwrite: true)`；
  4. 任何一步失败 → 删临时名、把**本组**已建好的链接逆序用独立副本还原（`File.Copy` 从保留副本复制回来），
     该组已成功的条目改记 `RolledBack`，然后继续下一组。
- 组是原子单位：一组失败不影响其它组，单文件失败也不影响其它组。

## 6. 平台实现（`NativeDedupLinker`）

`File.CreateHardLink` 在 .NET 里不存在，所以：

- Windows：`[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] CreateHardLinkW`，
  用 `[SupportedOSPlatform("windows")]` 标注 + `OperatingSystem.IsWindows()` 守卫（避免 CA1416）。
  错误码映射：17 跨卷、80/183 目标已存在、5 权限不足、1142 该文件系统不支持硬链接。
- Linux / macOS / FreeBSD：`libc` 的 `link(2)`（参数标 `LPUTF8Str`），errno 映射同理（18 跨卷、17 存在、13 权限、31 链接数上限）。
- 文件标识（inode）：Windows 用 `GetFileInformationByHandle` 的 `VolumeSerialNumber + FileIndex`，非 Windows 返回"未知"（见已知缺口）。
- 卷判定 `DedupVolume`：`DriveInfo.GetDrives()` 里最长根前缀匹配，Windows 忽略大小写；匹配不到退化到 `Path.GetPathRoot`。
- `IDedupLinker` 是 public 接缝（同 `IInstallerBackend` 的先例），测试用它做确定性的失败注入。

## 7. 页面层（`DedupPageViewModel` + `Views/DedupPageView.axaml`）

实例多选（默认全选，只记住用户手动取消的项）→ 最小尺寸 + 预演开关 → 扫描 → 左侧分组表格（哈希前缀 / 实例名 /
组内文件数 / 单份尺寸 / 可释放字节），右侧组内文件清单 + 执行结果表。执行按钮文案随预演开关变化（"预演去重"/"执行去重"）；
关闭预演后走 `IDialogService.ConfirmAsync` **两次**（第一次讲操作，第二次讲"多个实例共用同一份数据"的后果），
任一拒绝都记"已取消执行，未改动磁盘。"。
真写盘成功后清空分组并要求重新扫描，避免对已失效的计划重复执行。

## 8. 测试

- `tests/CkanUI.Services.Tests/DedupServiceTests.cs`：同内容跨实例分组、同尺寸不同内容不分组、
  128 KiB 门槛、受管跳过、符号链接跳过、受管清单读不到整实例跳过、只扫描点名实例、默认 dry-run 不写盘、
  写盘后两路径内容一致且文件标识相同（Windows）、单文件失败不影响其它组、跨卷跳过不抛、
  平台不支持返回不支持结果、已硬链接的文件不再被处理。全部用 `TempWorkspace` 建临时树。
- `tests/CkanUI.ViewModels.Tests/DedupPageViewModelTests.cs`：默认预演、表格投影、请求参数、
  无勾选不可扫描、预演不确认、两次确认与两次拒绝、写盘后清空、失败文案、不支持态、空态、实例清单变化刷新。
- `tests/CkanUI.App.Tests/ViewLocatorTests.cs` 增加 `DedupPageViewModel → CkanUI.App.Views.DedupPageView` 断言。

沙箱里符号链接需要 `SeCreateSymbolicLinkPrivilege`，`Scan_符号链接被跳过` 在本机会 `Assert.Ignore`（已实测 1 skipped）。

## 9. 已知缺口

- **非 Windows 的文件标识未知**：`TryGetIdentity` 在 Linux/macOS 返回 false，于是"已经硬链接的路径"不会被
  `CountDistinct` 合并、也不会被判为"已经是同一份文件"；重复扫描仍会列出它们，执行阶段第二次建链会被跳过，不会破坏数据。
  补齐需要在 Unix 上读 `stat` 的 `st_dev/st_ino`。
- 执行阶段不做文件系统事务：一组内的回滚是"逐个 `File.Copy` 复原"，若回滚途中被外部进程占用会留下
  失败状态（结果里逐条给出中文原因，不会静默）。
- 没有"共享存储"模式（把公共文件搬到单独目录再用链接引用），当前只做原地硬链接。
- 扫描是单线程 CPU 哈希（大小分组已把候选量压到位），超大 GameData 耗时未做基准测试；分组表格没有分页，依赖 ListBox 虚拟化。

## 10. 需要 Lead 落地（本文件禁止自行改动的位置）

- `src/CkanUI.Services/ServiceCollectionExtensions.cs`：追加
  `services.TryAddSingleton<IDedupLinker, NativeDedupLinker>();` 与
  `services.TryAddSingleton<IInstanceDedupService, InstanceDedupService>();`（放在 `ResidualCleanupService` 那两行之后）。
- `src/CkanUI.App/ServiceRegistration.cs`：页面 VM 若走工厂注册，需要把 `DedupPageViewModel` 加进页面清单。
- `src/CkanUI.ViewModels/ShellViewModel.cs`：导航项加"跨实例去重"；`docs/README.md` 登记本文件。
- 本地化键（`LocalizationKeys.cs` + `i18n/packs/*.json`）：本页文案按仓库现状直接写在 VM/axaml 里，收口到语言包需另开一组 `Dedup.*` 键。
