# 模组对话框族（F-34 / F-35 / F-36）

上游对照：`GUI/Controls/ChooseRecommendedMods.cs`（推荐/建议/被支持）、`GUI/Controls/ChooseProvidedMods.cs`（虚拟模组提供者）、
`Core/Relationships/RelationshipResolver.cs` 的 `ConflictDescriptions`（冲突理由）。

## 分层

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/Dtos/ModDialogs.cs` | 纯 DTO：`RecommendationDialogRequest`/`RecommendationSelection`、`VirtualModuleRequest`/`VirtualModuleChoice`、`ConflictDetailsRequest`/`ConflictEntry`。不含任何 CKAN 类型 |
| 契约 | `src/CkanUI.Services.Abstractions/IModDialogService.cs` | 窄接口三个方法，取消一律返回 null |
| VM | `src/CkanUI.ViewModels/Dialogs/RecommendationDialogViewModel.cs` | F-34 分组勾选、全选/全不选、DLC 与冲突项禁选、`SuppressRecommendations` 偏好回传 |
| VM | `src/CkanUI.ViewModels/Dialogs/VirtualModuleDialogViewModel.cs` | F-35 单选互斥、默认选第一项、未选则拒绝确认 |
| VM | `src/CkanUI.ViewModels/Dialogs/ConflictDetailsDialogViewModel.cs` | F-36 只读列表 + 复制全部，`ConflictsText` 与上游 `string.Join("; ", ConflictDescriptions)` 一致 |
| VM 行 | `src/CkanUI.ViewModels/Dialogs/Mods/*.cs` | `RecommendationCandidateViewModel`/`RecommendationGroupViewModel`/`VirtualModuleCandidateViewModel`/`ConflictRowViewModel` |
| 视图 | `src/CkanUI.App/Views/Dialogs/{Recommendation,VirtualModule,ConflictDetails}DialogView.axaml(.cs)` | 继承 `DialogWindow`，不自己 `Show()`，配色只用 `Tokens.axaml` 的 `Ckan*` token |
| 接线 | `src/CkanUI.App/Dialogs/ModDialogService.cs` | `IModDialogService` 实现；owner 由注入委托提供，取消令牌触发时关窗 |

## 语义要点

- **推荐**：初始勾选 = `IsSelectedByDefault`（且可勾选）；`SuppressRecommendations` 勾上即全不选并把偏好带回给调用方持久化（`UiSettings` 目前没有该字段）。
  DLC 与 `IsConflicting` 行 `CanSelect=false`，全选会跳过它们；冲突行在 `ConflictNotice` 里列出原因。
  全不选仍然可以确认（等价于"这次不要任何推荐"），只有**没有候选**时才禁用确认。
- **提供者**：单选互斥，默认选中第一项（与上游 `ChooseProvidedMods` 一致）；双击某行等价于选中并确定。
  没有候选或没有选中时 `Validate()` 失败并给出中文提示。取消返回 null，调用方应放弃安装（对应 `TooManyModsProvideKraken`）。
- **冲突详情**：主按钮"复制全部"（`CloseAfterPrimary=false`），次按钮"关闭"（`Acknowledged=true`）。
  冲突文案由服务层提供（`CoreChangeSetPlanner.DescribeConflicts` 已优先复用 Core 的本地化资源串），对话框不参与判定。

## 接线（由 Lead 完成）

```csharp
// src/CkanUI.App/ServiceRegistration.cs 或组合根
services.AddSingleton<IModDialogService>(sp =>
    new ModDialogService(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow));
```

## 已知缺口

- 三个对话框尚未接入页面命令：`ModsPageViewModel` 的安装流程目前由 `CoreChangeSetPlanner` 静默处理推荐项与冲突；
  需要 Lead 在安装确认前插入 `SelectRecommendationsAsync`，在 `CkanInstallerBackend` 抛 `TooManyModsProvideKraken` 时插入 `SelectVirtualModuleAsync`。
- `SuppressRecommendations` 的持久化字段不在 `UiSettings` 里，需要 `IUiSettingsStore` 追加（属 Lead 的契约改动范围）。
