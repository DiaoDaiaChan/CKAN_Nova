# 下载统计（F-47 / UI-25）

## 目标与范围
根据当前 KSP 实例（IInstanceService.Current）与注册表/仓库快照（IRegistryService / RepositoryDataManager）及下载缓存（ICacheService）生成模组下载量与缓存大小的统计切片与热榜表格。
页面支持按模组、标签、安装状态、仓库等维度统计分析。统计过程为只读纯计算，不触发外部网络或磁盘写入。

## 分层与文件
- Abstractions：
  - `CkanUI.Services.Abstractions/Dtos/DownloadStats.cs`：DownloadStats DTO，包括 `DownloadStatsDimension` (Mods, Tags, Installed, Repositories)、`DownloadStatsSlice`、`DownloadStatsRow`、`DownloadStatsReport` 与 `DownloadStatsRequest`。
  - `CkanUI.Services.Abstractions/IDownloadStatsService.cs`：`IDownloadStatsService` 接口契约，暴露 `ComputeAsync`。
- Services：
  - `CkanUI.Services/Stats/DownloadStatsService.cs`：实现 `IDownloadStatsService`。注入 `IInstanceService`、`IRegistryService`、`ICacheService`，按仓库模组和已安装模组聚合下载量与缓存字节，按选定维度（含仓库维度映射）分组；无实例时优雅降级为空快照而非抛出异常。
  - `CkanUI.Services/Stats/DownloadStatsAggregator.cs`：纯聚合算法。前 8 项作为独立切片，超出部分自动聚合成“其它”；同时计算总下载量、总缓存占用与各切片百分比。
- ViewModels：
  - `CkanUI.ViewModels/Pages/DownloadStatsPageViewModel.cs`：继承 `PageViewModel`，实现 `IActivatable`。暴露 `Title`（“下载统计”）、`ChartSlices`、`Rows`、`TotalDownloads`、`DimensionLabel`、`IsBusy`、`HasError`、`StatusText`、`SwitchDimensionCommand` 与排序命令。
- App / UI：
  - `CkanUI.App/Controls/DonutChart.cs`：自绘圆环控件，基于 Avalonia 原生 DrawingContext 渲染。调色板取自主题资源 token（`CkanAccentBrush`、`CkanSuccessBrush`、`CkanWarningBrush`、`CkanDangerBrush`、`CkanTextSecondaryBrush`），杜绝硬编码灰黑 fallback。最多支持 8 个主段位 + 1 个“其它”段位。
  - `CkanUI.App/Views/Pages/DownloadStatsPageView.axaml`：下载统计视图，严格匹配 ViewModel 暴露的属性和命令名，内嵌 DonutChart 与 DataGrid 表格。
- Tests：
  - `tests/CkanUI.Services.Tests/DownloadStatsServiceTests.cs`：覆盖空实例、单仓库、多仓库排序、同名模组去重、零下载、缺版本、维度切换、取消、错误传播等场景（20 例）。
  - `tests/CkanUI.ViewModels.Tests/DownloadStatsPageViewModelTests.cs`：覆盖空状态文案、加载成功、切片聚合、维度切换重建、错误栏、缺服务降级、排序翻转、激活等场景（16 例）。

## 关键口径
- 未知/零下载量不计入总下载数；零/未知下载量模组单独标记。
- 仓库维度（Repositories）：通过已安装模块的 `InstalledByRepository` 或仓库元数据将模组映射到仓库（如 "KSP-CKAN/CKAN-meta"），未指定或本地安装归类为“未知仓库”。
- 切片聚合：段数 > 8 时自动将前 8 项之后的数据聚合到“其它”，并在图例中完整展示。
- 标签维度：多标签模组分别计入对应标签切片。
- 缓存字节：按 Identifier 聚合已缓存文件大小。
- 排序：支持按下载量、缓存大小、名称排序，相同排序列再次点击翻转正序/倒序。

## 安全与失败语义
- 纯只读快照计算，无外部 I/O 阻塞。
- 无有效 KSP 实例或无可用数据时返回 `DownloadStatsReport.Empty`，页面展示友好空状态提示。
- 取消令牌透传，当用户切走或取消时及时中止计算。
- 依赖服务故障时通过 `OperationResult<T>.Fail` 传递错误，页面展示错误横幅，不伪造错误数据。

## 图表自绘设计
- 不引入 OxyPlot / LiveCharts 等第三方图表库，使用 Avalonia `DrawingContext` 自绘圆环（DrawGeometry / PathGeometry + ArcSegment）。
- 颜色从主题 ResourceNode 获取（如 `CkanAccentBrush`、`CkanSuccessBrush` 等），无硬编码颜色 fallback。
