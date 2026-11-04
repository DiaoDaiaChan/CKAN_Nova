# 自定义主页部件

主页不再是一张写死的长图：实例概览、快捷操作、最近截图是三块默认部件，
另有模组更新、安装历史、下载缓存、存档、后台任务五块宿主数据部件可添加；
插件也可以声明自己的部件（RP-1 生涯日志是第一块）。用户能加、能减、能拖动排序，
布局写进 `%LocalAppData%/CKAN/ckan-ui.json` 的 `HomeWidgets` 数组。

## 1. 为什么布局只存身份

`HomeWidgets` 里存的是部件身份，不是标题、不是快照：

- 内置：`instance` / `quick-actions` / `screenshots` / `mod-updates` / `install-history` / `cache` / `saves` / `operations`；
- 插件：`plugin:<插件 Id>:<部件键>`，例如 `plugin:com.ckanui.rp1:career`。

标题、图标与内容每次进入主页重新解析。这样插件升级换标题、换图标、换数据形状时，
旧布局仍然指向同一块部件；插件临时停用也不会把它的槽位从布局里删掉（重新启用后回到原位）。

`null` 与空数组是两个意思：`null` 表示用户从没动过，用默认三块；空数组表示用户明确清空了主页，
不强行塞回默认值。`SanitizeLayout` 会去掉空串、重复项与不认识的内置键，插件键保留三段格式。

## 2. 契约

`src/CkanUI.Services.Abstractions/Home/PluginHomeWidget.cs`：

```csharp
public sealed record PluginHomeWidgetDescriptor(
    string Key, string Title, string? Description = null, string? Glyph = null, string? PageKey = null);

public sealed record PluginHomeWidgetContext(string? InstanceName = null, string? GameDir = null, string? GameVersion = null);

public sealed record PluginHomeWidgetMetric(string Label, string Value, string? Tone = null);
public sealed record PluginHomeWidgetLine(string Text, string? Detail = null, string? Badge = null);
public sealed record PluginHomeWidgetSnapshot(
    string Title, string? Subtitle = null,
    IReadOnlyList<PluginHomeWidgetMetric>? Metrics = null,
    IReadOnlyList<PluginHomeWidgetLine>? Lines = null,
    string? Footer = null, string? Error = null);
```

`ICkanUiPlugin` 两个带默认实现的成员：

```csharp
IReadOnlyList<PluginHomeWidgetDescriptor> HomeWidgets => Array.Empty<PluginHomeWidgetDescriptor>();
Task<OperationResult<PluginHomeWidgetSnapshot>> GetHomeWidgetAsync(
    string key, PluginHomeWidgetContext context, CancellationToken cancellationToken = default);
```

默认实现返回「插件未实现主页部件数据。」的不可重试失败——旧插件不用改代码。
`IPluginService` 对应暴露 `ActiveHomeWidgets`（只含 `State == Active` 的插件，按插件名/部件标题排序）
与 `GetHomeWidgetAsync(pluginId, widgetKey, context, ct)`；`PluginService` 会校验插件存在、已激活、
确实声明了这个键，再在锁外调用插件，异常转可重试失败（取消转「已取消」）。

声明 `HomeWidgets` 只表示「可添加」，不自动占位。主页「自定义」工具条的「添加部件」下拉里会出现它，
用户选了才进布局。

## 3. 页面与数据流

- 主页 VM：`src/CkanUI.ViewModels/Pages/HomePageViewModel.cs`。`Widgets` 是可见部件（按布局顺序），
  `AvailableWidgets` 是「还没在布局里」的部件。添加 / 移除 / 上移 / 下移 / 拖放都改 `_layoutIds`，
  立刻 `SaveLayout()` 并 `RebuildWidgets()`。
- 插件部件的快照：进入主页、实例切换、插件启停（`IPluginService.Changed`）时刷新；
  `_widgetRefreshGeneration` 与实例引用双重校验，慢请求回来时实例已经换了就丢弃。
  快照没有 metrics/lines/error 时显示「没有可显示的数据」，不用 0 冒充。
- 视图：`src/CkanUI.App/Views/HomePageView.axaml` 五套 `DataTemplate`
  （`InstanceHomeWidgetViewModel` / `QuickActionsHomeWidgetViewModel` / `ScreenshotsHomeWidgetViewModel` /
  `PluginHomeWidgetViewModel` / `FeedHomeWidgetViewModel`），统一头部：拖动把手、标题、上移/下移/移除；
  插件与数据部件另有「刷新」和「打开页面」（`PageKey` 非空时显示），插件部件还有角标。
- 拖拽：`HomePageView.axaml.cs` 在把手上 `PointerPressed` 捕获指针，移动超过 8 px 才算拖动，
  用 `HomeWidgetsHost.ContainerFromIndex` 的容器中点判断落在目标的上半还是下半，
  松手时调 `MoveWidgetBefore` / `MoveWidgetAfter`。**移动过程中不重排**：`ItemsControl` 重建会摘掉
  正在被捕获的把手，`PointerCaptureLost` 会立刻中断拖动。所以拖动中只画虚线落点，松手才落地。

## 4. 第一块插件部件：RP-1 生涯日志

`src/CkanUI.Plugin.Rp1/Rp1Plugin.cs` 声明 `career`（标题「生涯日志」，Glyph `History`，
PageKey `rp1-career`）。`GetHomeWidgetAsync`：

1. 没有 `GameDir` → 快照带 `Error`「还没有选中实例：先到「实例」选一个游戏目录。」，不显示 0；
2. `Rp1CareerService.ListSavesAsync` → 没有 `persistent.sfs` → `Error` 说明这个实例没有存档；
3. 取最近修改的存档 `LoadAsync`，`Metrics` 六项（当前日期、资金、科技、声望、合同、里程碑），
   `Lines` 取最近 5 条非财务事件（标题、日期 · 副标题、徽标），`Footer` 写存档更新时间；
4. 读坏了也返回 `Ok` + `Error`，主页显示可读文案而不是整块消失。

真实存档的数字见 [rp1-career.md](rp1-career.md) §5；部件只做展示，不写存档。

## 5. 宿主自带的数据部件

除了三块默认部件，主页目录里还有五块直接读宿主服务的数据部件（不需要任何插件）：

| 部件键 | 标题 | 数据源 | 打开到 |
|---|---|---|---|
| `mod-updates` | 模组更新 | `IRepositoryService.Current`（已装 / 可更新 / 兼容更新，前 5 条） | `mods` |
| `install-history` | 安装历史 | `IInstallHistoryService.LoadAsync(5)` | `installhistory` |
| `cache` | 下载缓存 | `ICacheService.GetSnapshotAsync()`（条目 / 占用 / 上限 / 使用率） | `cache` |
| `saves` | 存档 | `ISaveFileService.ListSavesAsync(gameDir)`（存档 / 备份 / 写前快照，按写入时间倒序） | `saves` |
| `operations` | 后台任务 | `IOperationQueue.Operations`，并订阅 `Updated` 只重画这一块 | 无 |

- 纯格式化集中在 `src/CkanUI.ViewModels/Pages/Home/HomeWidgetFeeds.cs`（`HomeWidgetFeeds` 静态方法），
  数据部件本身是 `FeedHomeWidgetViewModel`：进入主页、实例切换、手动刷新时 `LoadAsync`。
- **没有数据就说没有数据**：仓库没加载、没有安装记录、没有存档、队列为空时给页脚说明与下一步，
  不画「0 个已安装」「0 B」这种看起来像真数据的零。读取失败显示错误文案，部件不整块消失。
- **后台任务块是活的**：`IOperationQueue.Updated` 在 UI 线程触发，只 `Apply` 这一块，
  不重排布局、不重读其他部件。
- 五块都在第一次打开主页时出现在「添加部件」下拉里，但不会自动占位——默认布局仍是三块。

## 6. 测试

- `tests/CkanUI.ViewModels.Tests/HomePageViewModelTests.cs`：默认三块、添加/移除持久化、
  上移/下移/拖放前后顺序、插件快照落到度量与行、插件停用后槽位保留、五块数据部件在目录里且能添加。
- `tests/CkanUI.ViewModels.Tests/HomeWidgetFeedTests.cs`：五块数据部件的纯格式化（空数据、计数、
  排序、上限与剩余、字节单位、运行中排在排队前）。
- `tests/CkanUI.Services.Tests/HomeWidgetPluginServiceTests.cs`：只暴露已激活插件的声明、
  上下文原样转发、未知键拒绝、插件抛异常转可重试失败。
- `tests/CkanUI.Plugin.Rp1.Tests/Rp1PluginTests.cs`：声明唯一 `career` 部件、没有实例时给可执行的下一步。
- `tests/CkanUI.App.Tests/HomePageViewWidgetTests.cs`：三块默认部件在真实 Avalonia 里生成实体卡片、
  清空后显示空态、插件部件与数据部件各自走自己的模板并画出度量/行/徽标/页脚。

无头用例用 `HeadlessAppHost.CreateTestWindow` 把视图挂到窗口上（**不** `ForceRenderTimerTick`）。
只 `Measure/Arrange` 一个没有视觉根的 `UserControl` 时，`ScrollViewer` 不会实例化内容，
`ItemsControl` 的容器数为 0——这是用例第一版红的原因。

## 7. 已知边界

- 拖拽只在纵向列表内排序，不是任意二维画布；移动过程中不实时重排（见 §3）。
- 插件部件只在插件激活时能添加；插件停用后它的快照不再刷新，但布局槽位保留。
- 主页自定义工具条没有「重命名部件」「设置每块宽度」这类高级布局能力。
- 快照刷新是拉取式：没有常驻定时器，主页不活跃时不会后台抓数据。
