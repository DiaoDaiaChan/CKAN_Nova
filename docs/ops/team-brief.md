# 并行开发简报（所有 agent 开工前必读）

> 版本 v1.0 · 维护人 Lead · 本文件是并行期唯一协调口径，与 `engineering-conventions.md` 配合阅读。

## 1. 当前状态（不要重复已完成的事）

| 项 | 状态 |
| --- | --- |
| ENV-01 骨架 | ✅ 完成（`dotnet build ckan-ui.sln` = 0 警告 0 错误） |
| ENV-02 接 Core | ✅ 完成（`$(CkanCoreProject)` → `external/CKAN/Core/CKAN-core.csproj`，目录联接） |
| ENV-03 日志落位 | ✅ 完成（`LoggingSetup` + `__CKAN_LOG_DIR__` 占位符，见 conventions §10） |
| ENV-04 CI | ✅ 工作流已写（`.github/workflows/ci.yml`、`hygiene.yml`），本机无法验证 |
| ENV-05 架构测试 | ✅ 9 个用例（依赖规则 7 + 命名空间 2） |
| ARC-01..05 外壳 | ✅ DI 组装根 / IDispatcher / ViewLocator / 异常兜底 / 单实例 + `ckan://` |
| INT-01..03 | ✅ 启动序列 / 设置服务 / IUser 线程回调 |
| MOD-01..04 | ✅ 搜索语法 / ModChange / GUIMod / 过滤排序投影 |
| 服务契约冻结 | ✅ `src/CkanUI.Services.Abstractions/` 13 接口 + 8 组 DTO，见 `docs/design/service-contracts.md` |
| 本地测试通道 | ✅ `dotnet run --project tests/CkanUI.TestRunner`（202 用例全绿） |
| UI-02/03/04/05/07/08/09/13/32 页面 | ✅ 模组/实例/设置页 + 对话框组 + 操作浮层 + 本地化（提交 `56ec0e7`） |
| 主题与设置生效 | ✅ `ThemeHost` 订阅 `UiSettings.Changed` 实时切主题；`Density` 已落盘 |
| UI-06 仓库页 | 🚧 repos-page 在写（可增量扩 `IRepositoryService`） |
| 变更页 UI-10 | 🚧 changes-service / changes-page 在写，尚未接线 |

## 2. 构建与测试（沙箱里 dotnet test 不可用）

```bash
# M1 起页面 VM 与视图分散在 ViewModels/App 两个项目，通常需要整链构建；
# 若只改自己的项目，优先只构建自己那一段
dotnet build src/CkanUI.<你的项目> -v q --nologo

# 全量验证（Lead 负责）
dotnet build ckan-ui.sln -v q --nologo

# 跑测试：先构建自己的测试项目，再用宿主跑
dotnet build tests/CkanUI.<X>.Tests -v q --nologo
dotnet run --project tests/CkanUI.TestRunner --no-build -- <项目名过滤词>
```

- 自有项目 `TreatWarningsAsErrors=true`：任何警告都是错误，包括 IDE 分析器。
- 遇到 MSB3021 / 文件被占用：等 5 秒重试一次，仍失败就只构建自己的项目。
- **禁止** `dotnet test`（沙箱拒绝父进程句柄，Testhost 必崩，见 `engineering-conventions.md` §3）。

## 3. 写作用域（硬约束）

只写自己 task 里 `write_scopes` 列出的路径。需要别人作用域里的改动时：**不要动，写进最终报告交给 Lead**。

共享的、谁都不许单独改的文件：
- `ckan-ui.sln`、`Directory.Build.props`、`Directory.Build.targets`、`Directory.Packages.props`、`global.json`
- `src/CkanUI.Services.Abstractions/**`（契约已冻结；确需改动 → 报告 Lead，由 Lead 统一改 + 同步 ContractShapeTests）
- `docs/README.md`（登记新文档 → 报告 Lead）

## 4. 架构铁律（违反即返工，架构测试会红）

1. 依赖方向：`App → ViewModels → Services.Abstractions ← Services → CKAN`。
2. Avalonia 命名空间只允许出现在 `CkanUI.App`、`CkanUI.Controls`、`CkanUI.Platform`、`CkanUI.Services.Threading`。
3. CKAN 命名空间只允许出现在 `CkanUI.Services`、`CkanUI.Localization`。
4. ViewModel 里禁止：`Avalonia.*`、`System.Windows.*`、`File.*`/`Directory.*` 写、VM 互引、构造函数里发起 IO。
5. Core 事件一律后台线程触发；只有 Services 层可以订阅，ViewModel 只收已封送到 UI 线程的通知。
6. 服务接口异常不穿透：失败返回 `OperationResult`/`OperationResult<T>`。
7. 列表投影整体替换（一次 SetProperty），不做增量 `CollectionChanged`。
8. 文件级命名空间；中文注释解释"为什么"；禁 `#pragma warning disable`；禁本机绝对路径。

## 5. 代码风格

- 每个 `.cs` 顶部 `namespace X.Y;`（文件级，不用大括号块）。
- 公共类型写 `///` 摘要，一句话说清职责 + 引用规格章节。
- 测试类 `<被测类>Tests.cs`，NUnit `[TestFixture]`/`[Test]`，断言用 `Assert.That(..., Is.EqualTo(...))`。
- 不引入新 NuGet 包（CPM 已冻结；确需 → 报告 Lead）。已可用：Avalonia 12.1.4、DataGrid 12.1.2、CommunityToolkit.Mvvm 8.4.0、Microsoft.Extensions.* 10.0.3、log4net 3.3.0、NUnit 3.14.0、NUnitLite、NUnit3TestAdapter、Microsoft.NET.Test.Sdk。

## 6. 提交纪律

**并行期谁都不 commit、不 push、不 checkout 分支、不 rebase。** 完成后把改动留在工作树，写清「改了哪些文件 + 测试结果 + 遗留问题」，由 Lead 统一审阅提交。

## 7. 规格阅读顺序（只读自己 task 相关的章节）

规格副本在 `docs/plan/`，与规格仓 `doc/uir/` 同编号。章节行号索引见各 task 描述。
- 先读 `docs/plan/01-architecture.md` §2 分层表 / §5 依赖规则 / §11 约束。
- 再读自己任务对应的 WBS 条目（`docs/plan/06-wbs-milestones.md`）。
- UI 相关读 `03-ui-spec.md`；VM 相关读 `04-mvvm-and-models.md`；线程相关读 `05-threading.md`；测试相关读 `07-testing.md`。

## 8. 报告格式（最终消息必须包含）

1. 完成的任务编号（如 ARC-02）。
2. 新增/修改文件清单（相对仓库根的路径）。
3. 构建结果（命令 + 是否 0 警告）。
4. 测试结果（跑了哪些测试、几个通过）。
5. 未完成项 / 发现的问题 / 需要 Lead 决策的点。

## 9. M1 波次表（页面层）

M0（外壳）已交付 `d06c64c`。M1 把三个占位页换成真实页面，按写入范围切分，互不重叠：

| 波次 | 负责人 | 任务 | 写入范围 |
| --- | --- | --- | --- |
| 1 | mods-page | UI-02 / UI-03 / VM-01 / VM-02 | `src/CkanUI.ViewModels/Pages/Mods/**`、`ModsPageViewModel.cs`、`src/CkanUI.App/Views/ModsPageView.*`、`tests/CkanUI.ViewModels.Tests/Mods*` |
| 1 | installer-service | INT-07 / INT-08 | `src/CkanUI.Services/Installers/**`、`ServiceCollectionExtensions.cs`（只加一行）、`tests/CkanUI.Services.Tests/Installer*` |
| 1 | settings-page | UI-07 / VM-03 | `src/CkanUI.ViewModels/Pages/Settings/**`、`SettingsPageViewModel.cs`、`src/CkanUI.App/Views/SettingsPageView.*`、`tests/CkanUI.ViewModels.Tests/Settings*` |
| 1 | dialog-kit | UI-09 / VM-04 | `src/CkanUI.ViewModels/Dialogs/**`、`src/CkanUI.App/Views/Dialogs/**`、`tests/CkanUI.ViewModels.Tests/Dialogs*` |
| 1 | i18n-localization | UI-13 | `src/CkanUI.Localization/**`、`i18n/**`、`tests/CkanUI.Localization.Tests/**`、`docs/ops/i18n-guide.md` |
| 2 | instances-page | UI-04 / UI-05 / UI-32 / VM-04 | `src/CkanUI.ViewModels/Pages/Instances/**`、`src/CkanUI.App/Views/Instances*` |
| 2 | ops-overlay | UI-08 / VM-05 | `src/CkanUI.ViewModels/Operations/**`、`src/CkanUI.App/Views/Operation*/` |
| 2 | theme-tokens | THM-01 / THM-06 | `themes/**`、`src/CkanUI.App/Resources/**`、`App.axaml`（仅资源字典合并段） |
| 3 | changes-service | INT-07 / INT-09 | `src/CkanUI.Services/ChangeSets/**` |
| 3 | changes-page | UI-10 / VM-07 | `src/CkanUI.ViewModels/Pages/Changes/**`、`ChangesPageViewModel.cs`、`src/CkanUI.App/Views/ChangesPageView.*` |
| 3 | repos-page | UI-06 / VM-06 | `src/CkanUI.Services/Repositories/**`、`src/CkanUI.ViewModels/Pages/Repos/**`、`ReposPageViewModel.cs`、`src/CkanUI.App/Views/ReposPageView.*`（**IRepositoryService 只许增量**） |

Lead 独占：`ServiceRegistration.cs`（页面单例与工厂）、`MainWindow.axaml`、`ShellViewModel.cs`、`PageViewModel.cs`、`NavigationService.cs`、`App.axaml.cs`、`Program.cs`、`src/CkanUI.Services.Abstractions/**`、`ckan-ui.sln`、`Directory.*.props/targets`、`docs/README.md`。

页面 VM 一律由容器按类型解析（`services.AddSingleton<ModsPageViewModel>()` + `PageTypes` 表），所以**页面 VM 的构造参数必须全部可由容器解析**；新增依赖接口先找 Lead。
