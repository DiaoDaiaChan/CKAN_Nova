# 里程碑状态表（M0–M14）

> 本文是 06 篇 WBS 的**执行实况**对照：每一行给出该里程碑的验收门禁、当前状态、证据所在文件。
> 规格侧口径以 `docs/plan/06-wbs-milestones.md` 与 `docs/plan/00-index.md` §3 为准；状态一旦变化必须同步 `docs/design/feature-matrix.md`。
> 生成方式：人工核对 + `dotnet build ckan-ui.sln`、`dotnet run --project tests/CkanUI.TestRunner`、`dotnet run --project tools/verify-layout -- --all` 三条命令的实测结果。

## 1. 总览

| 里程碑 | 主题 | 状态 | 门禁实测 |
| --- | --- | --- | --- |
| M0 | 骨架与契约冻结（ENV-01..05、ARC-01..05） | ✅ 完成 | 构建 0 警告 0 错误；架构测试 9/9；CI 三平台 matrix 已落 `.github/workflows/ci.yml` |
| M1 | 五个主页面 + 服务层打通（UI-01..UI-09、INT-04..08） | ✅ 完成 | 页面 VM 与视图全部可解析（`ViewLocatorTests` 钉死）；测试全绿 |
| M2 | 安装/卸载真实执行（INT-08 收口、E2） | 🚧 进行中 | `IInstallerService` + `CkanInstallerBackend` 可用；`IDownloadService`（临时文件 + 大小/SHA256 校验 + 原子 Move + 取消清理 + 失败分类）已落地；仍缺「真实联网装一个模组」的端到端冒烟 |
| M3 | 仓库、实例与设置周边补全（UI-06、UI-07、UI-28..UI-34） | ✅ 基本完成 | 仓库页（F-10）、设置页 8 组与密度、实例重命名（`RenameAsync`）、启动命令编辑（F-05/F-06）均已落地 |
| M4 | 模组详情、关系与搜索打磨（UI-03、UI-10、UI-25..UI-27） | 🚧 进行中 | 详情面板与关系树已实现；高级搜索语法只支持 is:/not:/@author/tag:/- 子集 |
| M5 | 导入导出与工具族（F-39..F-48） | 🚧 进行中 | `.ckan` 导入、modpack 导出、实例比对已落地（F-39 已实现 / F-45 部分）；F-40（ZIP 灌缓存）、F-41（去重）、F-42（未管理文件）未做 |
| M6 | 插件系统与外观个性化（UI-33、THM-xx） | 🚧 进行中 | 主题 token 与 Light/Dark/HighContrast 已生效；插件反射加载未开始 |
| M7 | 存档工作台（E1，SFS 格式） | 🚧 进行中 | SFS 解析/格式化/校验内核在实现中（`src/CkanUI.Sfs`），尚无页面 |
| M8–M14 | 日志与 AI 调试、托盘、打包发布、性能与验收 | ⏳ 未开始 | 见 `docs/plan/06-wbs-milestones.md` |

## 2. M0 已交付证据

| 条目 | 证据 |
| --- | --- |
| ENV-01 仓库骨架 | `src/` 11 个项目 + `tests/` 8 个项目 + `tools/verify-layout`，全部进 `ckan-ui.sln` |
| ENV-02 接入 Core | `src/CkanUI.Services/CkanUI.Services.csproj` 对 `external/CKAN/Core/CKAN-core.csproj` 的 ProjectReference（本地 junction，CI 走浅克隆） |
| ENV-03 日志落位 | `src/CkanUI.App/log4net.xml` + `src/CkanUI.Services/Logging/LoggingSetup.cs`（`__CKAN_LOG_DIR__` 令牌替换） |
| ENV-04 CI | `.github/workflows/ci.yml`（三平台 matrix + `TestCategory!=RequiresTxF` 分流 + Windows publish） |
| ENV-05 架构测试 | `tests/CkanUI.Architecture.Tests/DependencyRulesTests.cs`（7 条）+ `NamespaceRulesTests.cs`（2 条） |
| ARC-01 DI 组装根 | `src/CkanUI.App/ServiceRegistration.cs` |
| ARC-02 IDispatcher | `src/CkanUI.Platform/Threading/AvaloniaDispatcher.cs` |
| ARC-03 ViewLocator | `src/CkanUI.App/ViewLocator.cs`（`ResolveViewType` 可测） |
| ARC-04 异常兜底 | `Program.InstallCrashHandlers` + `IErrorReporter` + `CrashDialogViewModel` |
| ARC-05 单实例与 `ckan://` | `SingleInstanceGuard` + `App.HandleExternalArguments` |

## 3. M1–M5 已落地模块清单

| 模块 | 入口文件 | 说明 |
| --- | --- | --- |
| 外壳与导航 | `src/CkanUI.ViewModels/ShellViewModel.cs` | 六项导航：模组 / 变更集 / 仓库 / 导入导出 / 实例 / 设置 |
| 模组管理 | `src/CkanUI.ViewModels/Pages/ModsPageViewModel.cs` | DataGrid 8 列、列宽持久化、80ms 防抖搜索、勾选→变更请求 |
| 变更集 | `src/CkanUI.ViewModels/Pages/ChangesPageViewModel.cs` | 预览/应用状态机；有 `IInstallerService` 时执行真实安装 |
| 仓库管理 | `src/CkanUI.ViewModels/Pages/ReposPageViewModel.cs` | 增删/上下移/单仓库刷新/恢复默认（F-10） |
| 游戏实例 | `src/CkanUI.ViewModels/Pages/InstancesPageViewModel.cs` | 列表/克隆/遗忘/重命名/打开目录/Steam 库导入/启动（多命令选择） |
| 启动命令编辑 | `src/CkanUI.ViewModels/Pages/Instances/LaunchCommandsPanelViewModel.cs` | 增删改排序/重置默认/持久化到 `GUIConfig.json.CommandLines`（F-05/F-06） |
| 设置 | `src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs` | 七组、改一项写一项、读回校验 |
| 操作浮层 | `src/CkanUI.ViewModels/Operations/OperationQueueViewModel.cs` | 100ms 节流、可取消 |
| 导入导出 | `src/CkanUI.ViewModels/Pages/ModPacksPageViewModel.cs` | `.ckan` 导入预览与执行、modpack 导出、实例差集比对（F-39/F-45） |
| 服务层 | `src/CkanUI.Services/**` | Instances / Registry / Repositories / Settings / ChangeSets / Installers / Downloads / ModPacks / Logging / Bootstrap |
| 存档内核 | `src/CkanUI.Sfs/**` + `src/CkanUI.Sfs.Cli/**` | SFS 解析 / 格式化 / 校验（不含页面） |

## 4. 已知缺口（按影响排序）

1. **真实下载未端到端验证**：`CkanInstallerBackend` 的联网路径只有离线假后端用例；沙箱内无法访问 CDN，需要在有网络的机器上跑一次「装一个真实模组」的冒烟。
2. **实例重命名只改配置不搬目录**：`IInstanceService.RenameAsync` 已接 `GameInstanceManager.RenameInstance`（重名可重试拒绝、自启动名同步跟随），但不移动磁盘目录——与上游语义一致，界面已注明。
3. **高级搜索语法是子集**：只支持 `is:`/`not:`/`@author`/`tag:`/`-`，`desc:`/`lic:`/`lang:`/`dep:`/`rec:`/`sug:`/`conf:`/`sup:`/`label:` 在模型层（`ModSearch`）已支持，页面层尚未接线。
4. **Steam 库扫描只读 `libraryfolders.vdf`**：`net10.0`（非 `-windows`）下访问注册表会触发 CA1416，故未实现注册表兜底。
5. **下载层未做断点续传与自动重试**：`DownloadTargetFile.size` 恒传 0（全新下载），`retryable` 只作为标志交给上层，重试策略待 UI-35 落地。
6. **插件系统、托盘、打包发布、性能验收**（M6–M14 主体）尚未开工。

## 5. 如何复核本表

```bash
cd ckan-ui
dotnet build ckan-ui.sln -v q --nologo          # 期望 0 警告 0 错误
dotnet run --project tests/CkanUI.TestRunner    # 期望 0 失败
dotnet run --project tools/verify-layout -- --all   # 期望「结论: 绿」
```

> 注：本沙箱内 `dotnet test` 不可用（Testhost 被拒），故本地一律用 `tests/CkanUI.TestRunner`（NUnitLite 进程内跑）；CI 上仍用 `dotnet test`。
