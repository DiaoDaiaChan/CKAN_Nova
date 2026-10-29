# 日志与 AI 排障：落地说明（M8 / LOG-01..05 + UI-LOG-01）

> 规格正文见 `docs/plan/14-log-and-ai-debug.md`，工时见 `docs/plan/06-wbs-milestones.md` 的 M8 段。
> 本文只记录「实际怎么落的、为什么这么落、外壳还需要接什么」。

## 1. 分层与文件

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约 | `src/CkanUI.Services.Abstractions/Logs/` | `LogEvent` 事件模型，`LogQuery`/`LogSnapshot`/`LogAggregateResult` 查询模型，`ILogStore` / `ILogQueryService` / `ILogClassifier` / `ILogRedactor` / `ILogBundleService` / `ILegacyLogImporter` 接口，`LogSearchSyntax` 搜索语法，`LogRule` / `LogRuleSet` 规则模型。这一层不引用 Avalonia，也不做 IO。 |
| 实现 | `src/CkanUI.Services/Logs/` | `LogStore`（环形缓冲）、`LogStoreAppender`（log4net 内存 appender）、`LogPipeline`（appender + 自建滚动文件接线）、`LoggingUser`（IUser 装饰器）、`LogExceptionRouter`（三处异常钩子）、`ChildProcessLogCapture`（子进程 stdout/stderr）、`LegacyLogImporter`（旧 CKAN.log 导入）、`LogClassifier` + `BuiltinLogRules`（9 族规则表）、`LogQueryService`（过滤/聚合/时间轴/指纹）、`LogRedactor`（脱敏）、`LogBundleService`（诊断包）。 |
| 视图模型 | `src/CkanUI.ViewModels/Pages/Logs/` | `LogPageViewModel`（审查工作区）、`LogFilterViewModel`（三段过滤 + 搜索语法 → `LogQuery`）、`LogRowViewModel` / `LogFingerprintGroupViewModel` / `LogTimelineBarViewModel`（行/聚合组/时间轴柱）、`LogDrawerViewModel`（底部抽屉 + 徽标）、`LogGlyphs`（字形与中文标签）、`LogObjectLinks`（mod / URL / instance / task 联动识别）。 |
| 视图 | `src/CkanUI.App/Views/Logs/LogPageView.axaml`、`LogDrawerView.axaml` | 纯绑定，视图层不写业务判断。 |

## 2. 事件是怎么流进来的（四路采集）

1. **log4net**：`LogPipeline.Install()` 把自建 `LogStoreAppender`（阈值 INFO）挂到 `Hierarchy.Root`，同时挂自建 `RollingFileAppender`（5MB × 5，MinimalLock，`%LocalAppData%\CKAN\logs\ckanui-yyyyMMdd.log`）。不依赖 log4net.xml，也不受 CWD 影响，CKAN Core 一行未改。
2. **IUser**：`LoggingUser` 装饰 `AvaloniaUser`，`RaiseError` → Error，`RaiseMessage` → Info，`RaiseProgress` 只在跨整十百分点或 100% 时落一条 User 级（否则进度会把缓冲淹了）。
3. **异常**：`LogExceptionRouter` 同时挂 `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`（并 `SetObserved` 防进程终止）、`Dispatcher.UnhandledException`。
4. **子进程**：`ChildProcessLogCapture.Attach(process)` 按行转事件，`Source = child:<pid>`。

### 2.1 log4net 3.x 的坑（必须记住）

log4net 3.x 在 `ILoggerRepository.Configured == false` 时会**短路整条日志链**（`LoggerImpl.IsEnabledFor` 恒 false）：appender 挂上了、阈值也对，却一条日志都收不到。所以 `LogPipeline.Install()` 在装好 appender 与阈值后显式置 `hierarchy.Configured = true`（`Dispose` 不还原，还原会让后续日志继续被丢）。

由此推出**启动顺序要求**：`LoggingSetup.Initialize()` 先、`LogPipeline.Install()` 后。反过来的话 pipeline 抢先置位 Configured，`LoggingSetup.ConfigureOnce` 会因 `repo.Configured` 提前 return，把 `log4net.xml` 的配置跳过。

## 3. 诊断包与脱敏（LOG-05）

`LogBundleService.CreateAsync` 产出 zip：`summary.json` / `env.txt` / `log.tail.txt` / `log.errors.txt` / `registry.summary.json`，可选 `config.redacted.json`、`console.log`。所有条目统一过 `ILogRedactor.Redact`，然后**重新打开内存 zip 逐条目再扫一遍**（`FindLeaks`）：命中就拒绝落盘并返回 `OperationResult.Fail("诊断包出口校验未通过，已丢弃")`。这就是规格里「反向扫描硬门禁」的实现方式——扫的是真实出口字节，不是内存里的中间字符串。

脱敏覆盖：Windows / macOS / Linux 用户目录、盘符与 UNC 绝对路径、`key=value` / JSON / `Bearer` 形态的令牌、SteamID / SteamUser / SteamAppId、邮箱、IPv4 / IPv6、`*.sfs` 存档名、主机名。`%LOCALAPPDATA%` / `%APPDATA%` 这类既有占位保留不动。

## 4. 外壳（Lead）接线清单

1. **DI**（`src/CkanUI.App/ServiceRegistration.cs`，Lead 独占）：
   - `ILogClassifier` → `new LogClassifier(Path.Combine(appDir, "Themes", "log-rules.json"))`（文件不存在自动回落内置规则）；
   - `ILogStore` 单例 → `new LogStore(classifier)`（默认容量 20k）；
   - `ILogQueryService` 单例 → `new LogQueryService(store)`；
   - `ILogRedactor` 单例 → `new LogRedactor()`；
   - `ILogBundleService` → `new LogBundleService(store, redactor, factsProvider)`（`IDiagnosticFactsProvider` 由外壳实现：版本 / 实例数 / 仓库源 / mod 数）；
   - `ILegacyLogImporter` → `new LegacyLogImporter(store)`；
   - `IUser` 的注册改成 `new LoggingUser(new AvaloniaUser(...), store)`；
   - 启动序列：`LoggingSetup.Initialize()` → `LogPipeline.Install()`（失败只降级为仅内存，不要抛出）→ `LogExceptionRouter.Attach()`。
2. **导航**：NavRail 一级入口 → `LogPageViewModel`；`ViewLocator` 按命名约定自动映射到 `CkanUI.App.Views.Logs.LogPageView`（已放在该路径，无需改 ViewLocator）。
3. **底部抽屉**：`LogDrawerView` 放在外壳底部行，`IsOpen` 时把该行高度设为半屏；`OpenPageRequested` 事件用于跳到日志页；`AiAnalysisRequested` 事件给出打包后的 zip 路径（AI 只读脱敏产物）。
4. **主题 token**（`src/CkanUI.App/Resources/Tokens.axaml`）：规格 §3.4 的 `Log.*` 系列尚未存在，当前视图复用 `CkanDangerBrush` / `CkanWarningBrush` / `CkanInfoBrush` / `CkanAccentBrush` 等既有 token。若要严格对齐规格，需要补 `Log.Text`、`Log.Trace`、`Log.Debug`、`Log.Info`、`Log.Warn`、`Log.Error`、`Log.Fatal`、`Log.User`、`Log.Highlight` 的深色 / 浅色与高对比度变体。
5. **本地化键**（`LocalizationKeys.cs` + `i18n/packs/*`）：`Log_Title`（日志）、`Log_Nav`（日志）、`Log_Search_Placeholder`、`Log_Badge_Errors`（有 {0} 个错误）、`Log_Badge_Warnings`、`Log_Badge_None`、`Log_Clear`、`Log_OnlyErrors`、`Log_FollowTail`、`Log_Aggregate`、`Log_ExportBundle`（导出诊断包）、`Log_Analyze`（请 AI 分析）、`Log_Empty`。目前 VM 里是中文硬编码（与仓库既有页面一致），切换本地化需要 Lead 统一接。
6. **测试断言**：`ViewLocatorTests` 建议加一条 `LogPageViewModel` → `LogPageView` 的映射断言（该文件不在本轮改动范围）。

## 5. 证据

- 构建：`Services.Abstractions` / `Services` / `ViewModels` / `App` 四条链路均 0 警告 0 错误。
- 测试：`CkanUI.Services.Tests` 1091 例（失败 24 例全部是另一分支在写的 `ModConfig.*`，不属日志域）；`CkanUI.ViewModels.Tests` 1213 例全绿。
- 性能：10 万行（容量 100k）过滤查询 median 6.10ms / p99 9.24ms，满足规格 §9.1 T-LOG-02 的 16.7ms 帧预算（测试里用 `[NonParallelizable]` 隔离）。
  用例标记 `[Category("Perf")]`，默认不跑（`dotnet run --project tests/CkanUI.TestRunner --no-build -- CkanUI.Services.Tests --perf` 才执行）；
  门禁取「5 轮里最好的一轮」，要求 **median ≤ 16.7ms 且 p99 ≤ 33.4ms**。本机单独跑实测 median 8.4–10.4ms、p99 13.6ms，落在 16.7ms 帧预算内；
  但整套 8 个程序集并发跑时一次 GC 停顿就能把尾巴顶到 17–25ms，所以默认不进常规测试门禁。
- 脱敏：20 条泄漏样本（中文用户名、用户名带空格、Linux / macOS home、SteamID、Bearer、query / JSON 令牌、邮箱、IPv4 / IPv6、带空格游戏目录、UNC、Unix 绝对路径、`.sfs`、`%COMPUTERNAME%`、密码赋值）全部被抹且反向扫描零命中。

## 6. 与规格的差异（有意为之）

- **规则表位置**：规格写 `Presentation/Themes/log-rules.json`；实现把路径作为 `LogClassifier` 的构造参数（由外壳决定），文件缺失或格式坏时静默回落内置规则并保留 `LastLoadError`。这样测试与 CLI 场景不依赖安装目录布局。
- **搜索语法与过滤条的关系**：规格没写两条入口冲突时怎么办。实现取交集；交集为空时以搜索框条件为准——因为 `LogQuery` 里「空集合」的语义是「不限」，回落成空集合反而会把全部日志放开。
- **诊断包 `registry.summary.json`**：只出计数与版本号，读写注册表由已有的实例服务负责，日志域不触碰注册表（对应 T-LOG-09 的隔离要求）。
- **`logs/index.sqlite`**：本里程碑未落库，内存缓冲 + 文件滚动即可满足 W31–W33 出口；若后续要做跨会话检索，可在 `LogStore` 之上加落库订阅者，不必改查询层。
