# 模组详情多页签架构（F-26 & F-30 / UI-03）

> 状态：**已实现**（ViewModel / 视图 / 用例 / 文档同批落地）。
> 实现文件：`src/CkanUI.ViewModels/Dialogs/ModDetailsViewModel.cs`、`src/CkanUI.ViewModels/Dialogs/ModDetailsSnapshot.cs`、`src/CkanUI.ViewModels/Dialogs/ModVersionItemDto.cs`、`src/CkanUI.App/Views/Dialogs/ModDetailsView.axaml`。
> 用例落点：`tests/CkanUI.ViewModels.Tests/DialogsModDetailsTabsTests.cs`（替身在同目录 `DialogsModDetailsTabsFakes.cs`）。

## 目标与范围
模组详情面板（Mod Details View）由原先的单视口平铺滚动结构升级为 4 页签（TabControl）多维度浏览架构，并补齐 F-30「全部版本列表 + 指定版本安装」：
1. **元数据与简介页（Metadata / Overview）**：模组基础元数据、稳定性容差覆盖、描述正文。
2. **依赖关系页（Relationships）**：依赖（Depends）、推荐（Recommends）、冲突（Conflicts）三组关系，带「已满足 / 缺失」状态标签与计数角标。
3. **全部版本页（Versions，对齐 F-30）**：全部历史与最新版本列表，标识版本号、KSP 兼容范围、发布日期、下载体积、已装标记、兼容性与稳定性徽章，并支持对指定版本发起安装（生成变更集）。
4. **文件与内容页（Contents，对齐 F-26 & F-29）**：展示已装模组的本地文件清单；并通过导航服务或宿主委托跳转到全局 ZIP 内容预览页。

---

## 实现清单

### 1. DTO 与模型（CkanUI.ViewModels）
- **`Dialogs/ModVersionItemDto.cs`**（record，主构造参数全部可选，向后兼容）
  - 数据：`Version`、`GameCompatibility`、`ReleaseDate`、`DownloadSizeBytes`、`DownloadUrl`、`IsInstalled`、`IsCompatible`、`IsUnstable`。
  - 展示投影：`VersionText`、`CompatibilityText`（无兼容信息时「未知兼容」）、`ReleaseDateText`（`yyyy-MM-dd` 或「未知」）、`DownloadSizeText`（B / KB / MB / GB 或「未知」）、`InstalledStatusText`、`StatusBadgeText`。
  - 徽章文案：`CompatibilityBadgeText`（已装 →「当前已装」；否则「兼容」/「不兼容」）、`StabilityBadgeText`（「不稳定/测试版」/「稳定版」）。
  - 徽章配色不走 DTO 字符串类名，改由视图 `Classes.Success` / `Classes.Danger` / `Classes.Warning` 条件类绑定布尔属性决定（编译绑定友好，无需运行时字符串比对）。
- **`Dialogs/ModDetailsSnapshot.cs`**
  - 末尾追加 `IReadOnlyList<ModVersionItemDto>? Versions = null`、`IReadOnlyList<string>? InstalledFiles = null`（record 可选参数，既有 2 参 / 15 参构造调用零改动）。
  - 新增 `public sealed record InstalledFileItem(string Path)`，提供 `FileName`、`DirectoryText`、`HasDirectory`（同时识别 `/` 与 `\\` 分隔符），供文件清单行模板使用（避免在编译绑定里用 `x:String` 模板）。
- **`Dialogs/ModDetailsViewModel.cs`**（仍是手写 `OnPropertyChanged` 风格，类已 `partial` 以承载 `[RelayCommand]`）
  - 构造向后兼容扩展：`ModDetailsViewModel(IRepositoryService repository, IRegistryService registry, IChangeSetService? changeSets = null, IChangeRequestSink? changeSink = null, INavigationService? navigation = null)`，三个新依赖均可空——未接线的宿主不会崩，只会在命令里给出中文提示。
  - 页签：`SelectedTabIndex`（越界自动收敛到 `0..TabCount-1`）、`TabCount = 4`、`MetadataTabHeaderText` / `RelationsTabHeaderText` / `VersionsTabHeaderText` / `ContentsTabHeaderText`（后三者带 `(N)` 计数）、`RelationsTotal`、`HasRelations`。
  - 版本页：`Versions`（快照 `Versions` 投影，并按 `FirstNonEmpty(_installed?.Version, _row?.InstalledVersion)` 补齐 `IsInstalled`，无变化时返回原引用避免无谓分配）、`HasVersions`、`VersionsCountText`、`VersionsEmptyText`。
  - 内容页：`InstalledFiles`、`InstalledFileRows`（`InstalledFileItem` 投影）、`HasInstalledFiles`、`InstalledFilesCountText`、`ContentsEmptyText`、`ContentPreviewButtonText`、`ContentPreviewHintText`（按已装/未装分流文案）、`RelationsEmptyText`。
  - 导航：`NavigateToContentPreviewCommand` + `ContentPreviewPageKey = "content"`，优先走 `INavigationService.NavigateAsync(key, identifier)`，返回 `false` 或未注入导航服务时回落到 `ExternalNavigateAction`（`Action<string>?`，宿主接管），仍无可用通道则给出中文提示；异常被捕获成 `ContentNavigationStatusText`。
  - 安装：`InstallVersionCommand`（`CanExecute = CanInstallVersion`，忙态 `IsInstalling` 期间禁用），经 `IChangeSetService.BuildAsync` 发起 `new ChangeRequest(identifier, ChangeAction.Install, version.Version)`；成功时把请求同步给 `IChangeRequestSink.SetRequests`（供变更集页确认），并暴露 `LastChangeSet`、`InstallStatusText`、`InstallStatusIsError`；失败/异常时清空预览与 `LastChangeSet` 并回报中文原因。VM 不引用 Core，精确版本解析由 Core 的 `CoreChangeSetPlanner.ResolveTarget` 消费 `TargetVersion` 完成。
  - 重置：`Select` / `ShowDetails` 在标识符变化时调用 `ResetTransientState`（页签回 0、清安装与导航状态、丢弃旧变更集）；`Clear` 同样复位。

### 2. 视图（CkanUI.App）
- **`Views/Dialogs/ModDetailsView.axaml`**（整体重写，编译绑定 `x:DataType="vm:ModDetailsViewModel"`）
  - 根 `Panel`：空状态提示（`IsEmpty` / `EmptyText`）+ `TabControl`（`IsVisible="{Binding HasSelection}"`、`SelectedIndex="{Binding SelectedTabIndex, Mode=TwoWay}"`）。四个 `TabItem` 为内联声明，切换页签不重建控件实例，列表滚动位置天然保持；选中态下划线与强调色沿用 `Resources/FluentStyles.axaml` 的全局 `TabItem` 样式。
  - 页签一（元数据与简介）：名称 / 作者 / 兼容性徽章 / 摘要 / 元数据两列网格 / 稳定性容差选择 / 可选中描述正文。
  - 页签二（依赖关系）：三组关系列表 + 全局 `Border.CkanEmptyStateCard` 空态。
  - 页签三（全部版本）：计数文本 → 状态横幅 → `ListBox`；行模板为 `DataTemplate x:DataType="vm:ModVersionItemDto"`，含版本号、兼容/稳定性/已装 `CkanBadge`、发布日期与体积次级行，以及「安装此版本」按钮（`Command="{Binding $parent[ListBox].((vm:ModDetailsViewModel)DataContext).InstallVersionCommand}"`，已装行隐藏），列表为空时显示 `VersionsEmptyText`。
  - 页签四（文件与内容）：引导卡片 +「打开内容预览」主按钮 → 导航状态横幅 → 文件计数 → `ListBox`（`DataTemplate x:DataType="vm:InstalledFileItem"`，文件名 + 目录次级行）。
  - 全视图零颜色/圆角字面量：颜色、圆角、字号、间距、动效时长全部 `{DynamicResource Ckan*}`；行悬停/选中高亮由局部 `ListBoxItem` 样式（沿用 `Views/ReposPageView.axaml` 的做法）与全局样式共同保证；状态横幅用新增的 `Border.StatusBanner` / `Border.StatusBanner.Error` 局部样式（`CkanInfo*` / `CkanDanger*` Token）。

### 3. 用例（CkanUI.ViewModels.Tests）
- **`DialogsModDetailsTabsTests.cs`**（27 例，全部通过）分四组：
  1. 页签标题与切换：三种计数角标、无计数不带括号、`SelectedTabIndex` 读写与越界收敛、换模组回页签 0、`Clear` 复位。
  2. 版本页投影：`Versions` 投影与已装补齐、快照已标记保留、空版本/空快照防空、三种徽章文案、日期与体积格式化。
  3. 内容页：已装文件投影与跨平台分隔符拆分、空清单、`ContentPreviewHintText` 随已装变化、导航成功（键 `content`、参数为模组标识符）、导航返回 `false` 回落 `ExternalNavigateAction`、导航抛异常只给提示、仅外部跳转、无通道提示、未选中拦截。
  4. 指定版本安装：`TargetVersion` 透传给 `IChangeSetService`（替身捕获 `ReceivedRequests`）、`ChangeAction.Install`、同步 `IChangeRequestSink`、`LastChangeSet` 赋值、空预览提示、失败清空预览并回报、异常复位、忙态 `CanExecute` 为 `false`、缺服务与未选中拦截、`null` 版本参数直接返回。
- 替身：`DialogsModDetailsTabsFakes.cs`（记录型 `IChangeSetService` / `IChangeRequestSink` / `INavigationService`，与既有 `DialogsFake*.cs` 同目录平行，不改动他人文件）。

---

## 未覆盖项与宿主接线清单
以下内容属于应用接线与数据装配，本页签实现主动留白，不算遗漏但需宿主补上：
1. **DI 注册**：`ModDetailsViewModel` 尚未在 `src/CkanUI.App/ServiceRegistration.cs` 注册（需把 `IChangeSetService` / `IChangeRequestSink` / `INavigationService` 一并注入）。
2. **页面嵌入**：`ModDetailsView` 目前没有挂载在 `ModsPageView` 右侧面板（当前仍是独立视图），嵌入式布局与选中联动由页面层接线。
3. **数据装配**：`ModDetailsSnapshot.Versions` / `InstalledFiles` 需要装配方填充（版本可用性来自 Core 的 `AvailableModuleVersions`，已装文件来自安装记录），目前仓库里还没有 `DetailsProvider` 的生产实现。
4. **内容页目标**：全局内容预览页键为 `content`，其"当前查看哪个模组/文件"由 `IContentTargetProvider` 注入，需要宿主把导航参数（模组标识符）桥接到该 Provider。
5. **本地化**：本批新增文案全部为视图/VM 内的中文字面量，与仓库现有 UI 一致；若要入 `LocalizationKeys` 与 `i18n/packs` 需另行登记（Lead 独占文件）。
6. **未做**：同页签内的键盘导航（左右方向键切页签）与页签切换动效、版本过滤/排序控件、已装文件的逐项"打开目录"动作。
