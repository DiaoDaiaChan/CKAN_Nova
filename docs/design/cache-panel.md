# 下载缓存面板（F-50 / F-40 / UI-19）

> 对应规格：[12 全特性对齐清单](../plan/12-feature-parity.md) F-50 与 F-40、[03 界面规格](../plan/03-ui-spec.md) UI-19。
> 代码：`src/CkanUI.Services.Abstractions/ICacheService.cs`、`src/CkanUI.Services/Cache/`、`src/CkanUI.ViewModels/Pages/CachePageViewModel.cs`、`src/CkanUI.App/Views/CachePageView.axaml`。

## 1. 分层

| 层 | 类型 | 职责 |
|---|---|---|
| 契约 | `ICacheService`（`CacheSnapshot` / `CacheEntryInfo` / `CachePurgeResult`） | 只暴露"条目 / 字节 / 上限 / 是否被引用"，六成员全带 DIM 兜底 |
| 收口 | `ICacheStore` / `CkanCacheStore`（internal） | 唯一接触 `NetModuleCache`、`IConfiguration.CacheSizeLimit`、`Registry` 的地方 |
| 逻辑 | `CacheService`（纯函数式） | 排序、入队、上限判定、zip 校验后的导入编排，可离线单测 |
| 页面 | `CachePageViewModel` + `Pages/Cache/` | 刷新、清理、按上限清理、从 ZIP 导入、状态栏文案 |

## 2. 口径

- **上限**：`CacheSizeLimit` 为 `null` / `0` / 负数时视为"不限制"，清理直接返回空结果（有测试钉死）。单位按 Core 的 MiB 语义，页面只做展示换算。
- **清理顺序**：按 `ModifiedTimeUtc` 升序删除，直到总量 `<=` 上限。正在被已装模组引用的条目在「按上限清理」与「全部清理」两条路径上都**跳过**，文件名进 `SkippedFileNames`，文案报「跳过 M 个正在使用的条目」。
- **从 ZIP 导入**：`NetModuleCache.ZipValid` → `Registry.GetDownloadHashesIndex()` 的 SHA256→SHA1 反查定位模块 → 读 zip 内的 `*.ckan` 元数据补一个 `https://ckan/imported-from-zip/...` 伪下载地址 → `Store(move:false, validate:false)`。坏 zip 返回不可重试失败并给中文原因。
- **和设置页的分工**：上限编辑留在设置页「缓存与下载」组，缓存页只读不写，避免两处双写 `config.json`。

## 3. 已知缺口

1. UI-19 的「打开缓存目录」「重置上限」按钮未做（打开目录可直接复用 `IUnmanagedShellLauncher` 的模式；重置上限归设置页）。
2. 没有真实实例集成测试：需要造 `Registry.json` 与 `GameData`，超出单测范围；口径逻辑已在 `CacheService` 覆盖。
3. ZIP 导入只认"zip 内有 `*.ckan`"的布局（CKAN 的真实缓存包布局）；`CKAN-Info.json` 这种东西在全仓不存在，规格里若提到按笔误处理。
