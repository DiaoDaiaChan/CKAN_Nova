# 安装历史（F-44 / UI-23 / INT-08）

> 对应规格：[12 全特性对齐清单](../plan/12-feature-parity.md) F-44、[03 界面规格](../plan/03-ui-spec.md) UI-23、[04 MVVM 与模型](../plan/04-mvvm-and-models.md) 的 `InstallHistoryViewModel`。
> 代码：`src/CkanUI.Services.Abstractions/IInstallHistoryService.cs` + `Dtos/InstallHistory.cs`、`src/CkanUI.Services/History/`、`src/CkanUI.ViewModels/Pages/InstallHistoryPageViewModel.cs`、`src/CkanUI.App/Views/InstallHistoryPageView.axaml`。

## 1. 分层

| 层 | 类型 | 职责 |
|---|---|---|
| 契约 | `IInstallHistoryService`（`InstallHistoryEntry` / `InstallHistorySnapshot` / `InstallHistoryRestoreRequest` / `InstallHistoryRestoreResult` 等） | 六个成员全部带 DIM 兜底（`"当前安装历史服务不可用。"`），零 CKAN 类型 |
| 落盘 | `InstallHistoryLine` + `InstallHistoryStore` | 一行的 JSON 读写、坏行跳过、滚动截断、原子替换 |
| 编排 | `InstallHistoryRecorder` / `InstallHistoryService` | 路径解析（当前实例）、追加、读取配对、重装走 `IChangeSetService` + `IInstallerService` |
| 记账 | `HistoryRecordingInstaller`（装饰器） | 包住真 `IInstallerService`，每次安装/卸载/升级成功后补一条历史 |
| 页面 | `InstallHistoryPageViewModel` + `Pages/InstallHistory/` | 时间戳列表、已装/未装分组、重装前变更数预览、状态与错误文案 |

## 2. 落盘格式与位置

- **位置**：`<GameDir>/CKAN/install_history.jsonl`（每实例一份）。选实例目录而不是 App 数据目录，理由是历史必须**跟着实例走**：换实例/搬目录后历史仍然对应同一份已装集合，反之 App 数据目录会出现"历史记的是 A 实例、当前却在 B 实例"的错位。
- 与上游旧 GUI 的 `<GameDir>/CKAN/history/*.ckan` **并存不冲突**：我们不去读写那些注册表快照文件，只用自己的 JSONL，旧客户端升级/回退都不会踩踏。
- **格式**：JSONL，一行一个对象，camelCase，枚举写字符串（可读、可 grep）：

```json
{"kind":"entry","at":"2024-05-01T10:00:01.0000000+00:00","instance":"KSP","operation":"Upgrade","outcome":"Succeeded","changes":[{"identifier":"A","action":"Upgrade","fromVersion":"1.0","toVersion":"2.0","succeeded":true}]}
{"kind":"snapshot","at":"2024-05-01T10:00:01.0000000+00:00","instance":"KSP","modules":[{"identifier":"A","version":"2.0"},{"identifier":"B","version":"3.1"}]}
```

- **一次操作 = entry 行 + snapshot 行，两行共用同一个 `at`**（时间戳只由 `InstallHistoryRecorder` 取一次）。读取时按 `at` 精确配对；没有配对快照的条目照样显示，但 "从历史重装" 按钮禁用。
- **快照只落标识与版本**：`IsInstalled` / `InstalledVersion` 在读取时按 `IRegistryService.Current` 现算，历史文件因此不会因为注册表变化而失真。
- **原子追加**：读全量 → 追加新行 → 超过 `maxLines`（默认 2000，下限 8）只保留最新的 N 行 → 写 `*.tmp` → `File.Move(tmp, path, overwrite: true)`。读者永远看不到半行文件。
- **坏行**：`JsonException` / 未知 `kind` / 缺 `at` / 未知枚举一律跳过且**不抛**，且原样保留在文件里（便于人工排查），只有新追加才会触发重写。

## 3. 重装口径

- `PreviewRestoreAsync` 与 `RestoreAsync` 共用同一段 `PrepareAsync`：找快照 → 与当前注册表比对，**已同版本的模组不发变更请求**（"将被变更的模组数量"要诚实）→ `IChangeSetService.BuildAsync`（依赖与冲突由 Core 的 RelationshipResolver 带出）。
- `RestoreAsync` 遇 `HasConflicts` **不调用** `ApplyAsync`，返回可重试失败并带上冲突清单；计划为空直接返回空结果（等价"已经一致，无需重装"）。
- 安装一律走 `IInstallerService.ApplyAsync`，不自己拼安装逻辑。重装成功后服务自己记一条 `Operation=Restore` 的历史。
- **不重复记账**：`InstallHistoryScope`（`AsyncLocal<int>`）在 `ApplyAsync` 外层开抑制域，`HistoryRecordingInstaller` 见到抑制就直接返回——否则一次重装会落两条历史。用 `AsyncLocal` 而不是静态 bool，是为了多实例/多页面并发时不吞别人的历史。
- 页面口径：列表按时间倒序；分组按"当前是否装着"（与旧 GUI 的 `registry.IsInstalled` 一致）；版本不同的模组留在已装分组里，状态列显示"版本不同（当前 x）"，并在重装预览里计入"换版本"。
- 失败文案统一 `InstallHistoryText.Compose/FromException`：`"…（可重试）"` / `"…（不可重试）"`，异常不抛给界面，同时 `IErrorReporter.Report` 上报。

## 4. 已知缺口

1. **滚动截断按行数**（2000 行 ≈ 1000 次操作）。没有按时间/大小滚动，也没有归档到 `install_history.1.jsonl`；超大实例长期使用会丢掉最旧的记录。
2. **不支持删除/清空历史**（规格 F-44 未要求）；用户只能手动删文件。
3. **没有真实的 CKAN 实例集成测试**：`InstallHistoryService` 的重装编排在替身（`RecordingChangeSetService` / `RecordingInstallerService`）上验证；`Registry.json` + `GameData` 的真实往返超出单测范围。
4. **装饰器接线由 Lead 落地**：`IInstallerService` 只有注册成 `HistoryRecordingInstaller` 才会"每次安装自动落历史"；若容器仍直接注册 `InstallerService`，历史页只能显示服务自己重装产生的记录。
5. **旧客户端写的历史不导入**：`<GameDir>/CKAN/history/*.ckan` 不解析，两边历史互不可见。
6. `RecordExtras`（"要不要卸载快照之外的模组"）已进契约（`RemoveExtras`）但页面未暴露开关，默认只补装不卸载。
