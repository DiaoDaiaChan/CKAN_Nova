# 01 目标架构

## 1. 架构原则

| # | 原则 | 落实方式 |
|---|---|---|
| P1 | **Core 是唯一真相源** | 已装模组、可用模组、配置、实例，一律从 Core 读，UI 不自建镜像数据库 |
| P2 | **UI 不持有 Core 对象的可变引用** | ViewModel 只持有不可变快照 + 标识符（`Identifier` / `GameInstance.Name`），刷新时整体替换 |
| P3 | **所有跨线程接触点集中在一个门面** | 见 05 篇：`CoreFacade` + `Dispatcher` 两处封送，业务代码不写 `Dispatcher.Invoke` |
| P4 | **副作用显式化** | 改盘操作必须经过 `IInstallerService`（内部包 `CkanTransaction`），任何 ViewModel 不得直接 `File.Copy` |
| P5 | **可 headless 测试** | ViewModel 与 Service 不引用任何 `Avalonia.Controls` 类型，可在 NUnit 里 new 出来 |
| P6 | **依赖单向** | Views → ViewModels → Services → Core；反向引用为 0 |
| P7 | **失败可见** | 所有 Core 调用包 `try/catch`，异常统一进 `IErrorReporter`，绝不静默吞掉 |

## 2. 分层

```
┌─────────────────────────────────────────────────────┐
│ Presentation (Views)        .axaml + code-behind     │  只做布局/绑定/动画
├─────────────────────────────────────────────────────┤
│ ViewModels                  CommunityToolkit.Mvvm    │  状态 + 命令 + 校验，无 UI 引用
├─────────────────────────────────────────────────────┤
│ Services                    ★ 唯一接触 Core 的地方    │  封送、事务、缓存、事件转 Observable
├─────────────────────────────────────────────────────┤
│ Core (CKAN)                 151 文件 / 24,661 行     │  ★ 原样复用，零修改
└─────────────────────────────────────────────────────┘
```

**关键约束**：`Avalonia.*` 命名空间只允许出现在 `Presentation` 与 `Services/Threading`（后者需要 `IDispatcher` 抽象，但接口定义在 `Services.Abstractions`，实现才引用 Avalonia）。

## 3. 仓库与解决方案布局

### 3.1 仓库位置（方案 C 落地形态）

```
C:\Users\43701\Documents\github\ckan-ui\        ← 新仓库（独立 git）
├── ckan-ui.sln
├── Directory.Build.props                       统一 LangVersion / Nullable / WarnAsError
├── global.json                                 锁定 SDK
├── external\
│   └── CKAN\                                   ← git submodule → KSP-CKAN/CKAN (或你的 fork)
├── src\
│   ├── CkanUI.App\                             Avalonia 可执行产物
│   ├── CkanUI.Services\                        服务层（唯一接触 Core）
│   ├── CkanUI.ViewModels\                      VM（可 headless 测试）
│   ├── CkanUI.Controls\                        自绘控件（虚拟化列表项、Tag 徽章等）
│   └── CkanUI.Platform\                        平台适配（单实例、协议、托盘、路径探测）
└── tests\
    ├── CkanUI.ViewModels.Tests\                headless VM 测试
    ├── CkanUI.Services.Tests\                  集成测试（沙盒 KSP 实例）
    └── CkanUI.TestSupport\                     FakeUser、沙盒实例构造器
```

> 备选形态：把 `src/` 直接放进 CKAN fork 的 `AvaloniaUI/` 目录下（决策 D2 已否决，但保留作为"不想维护 submodule"时的降级路径，见 09 篇 §2.2）。

### 3.2 项目清单与依赖

| 项目 | 类型 | TFM | 引用 | 说明 |
|---|---|---|---|---|
| `CkanUI.App` | WinExe/Exe | net10.0 | Services, ViewModels, Controls, Platform, Avalonia.* | 组装根（Composition Root），唯一允许 `new` 具体服务的地方 |
| `CkanUI.Services` | Library | net10.0 | Core, Abstractions | `Core` 的 `ProjectReference` 指向 `external/CKAN/Core/CKAN-core.csproj` |
| `CkanUI.ViewModels` | Library | net10.0 | Services.Abstractions, CommunityToolkit.Mvvm | **不引用** Services 实现，只引用接口 |
| `CkanUI.Controls` | Library | net10.0 | Avalonia | 虚拟化列表、徽章、进度环、Toast |
| `CkanUI.Platform` | Library | net10.0 | Avalonia (Win32/X11/macOS) | 单实例 IPC、`ckan://`、托盘、Steam/Registry 探测补充 |
| `CkanUI.Services.Abstractions` | Library | net10.0 | 无 | 全部服务接口 + DTO（保持零依赖，便于测试） |

### 3.3 NuGet 依赖（尽量少）

| 包 | 用途 | 备注 |
|---|---|---|
| `Avalonia` (11.x) | UI 框架 | 含 `Avalonia.Desktop`、`Avalonia.Themes.Fluent` |
| `CommunityToolkit.Mvvm` | `ObservableObject` / `[RelayCommand]` / `IMessenger` | 源生成器，无反射开销 |
| `Microsoft.Extensions.DependencyInjection` | DI 容器 | **不复用** Core 的 Autofac `ServiceLocator`（见 02 篇 §2.2 说明） |
| `Microsoft.Extensions.Logging` + `Serilog` 或直接 `log4net` | 日志 | 建议跟随 Core 用 log4net，避免两套日志文件 |
| `NUnit` + `NUnit3TestAdapter` | 测试 | 与 CKAN 现有 Tests 一致 |

**明确不用**：AutoMapper（手写映射，模组数 3,000 时映射开销被放大）、ReactiveUI（与 CommunityToolkit 二选一，后者更轻）、任何 `DynamicProxy`/反射型框架（启动时间敏感）。

## 4. 依赖规则（可自动检查）

| 规则 | 检查方式 |
|---|---|
| `ViewModels` 项目不得引用 `Avalonia.Controls` | 在 `.csproj` 里不加 `Avalonia` 包即天然成立；另加架构测试断言 |
| `Services.Abstractions` 不得引用 `CKAN` 程序集 | 同上，csproj 不加引用 |
| `Views` 的 code-behind 只允许 `InitializeComponent()` + 纯布局代码 | Code Review checklist + 行数阈值（>40 行的 code-behind 需说明） |
| 业务代码里出现 `Dispatcher.UIThread` 一律拒绝 | `grep` 白名单只允许 `Services/Threading/` 与 `App.axaml.cs` |
| 业务代码里出现 `File.`/`Directory.` 写操作一律拒绝 | `grep` 白名单只允许 `Services/Installer/` 与 `Services/Files/` |

## 5. 进程与生命周期

### 5.1 启动状态机

```
[进程启动]
  → 解析参数 (--instance=, ckan://, --headless-check)
  → SingleInstanceGuard：已有实例？→ 转发参数给它并退出(:0)
  → Logging.Initialize()                      ← Core/Logging.cs:23
  → 读启动前置配置（配置损坏时进入"恢复模式"）
  → 构造 DI 容器 → 构造 GameInstanceManager    ← Core/GameInstanceManager.cs:49
  → SplashWindow（可选，仅在实例/仓库索引扫描慢时显示）
  → 确定当前实例：命令行 > 上次使用 > 唯一实例 > 无实例（进入选择向导）
  → 创建 RegistryManager（取实例锁）           ← Core/Registry/RegistryManager.cs:265
  → 订阅事件 → 主窗口 → 后台开始仓库索引刷新
```

**无实例**是必须支持的正常路径：显示实例选择向导，不崩、不退出。

### 5.2 关闭状态机

```
[关闭请求] → 有进行中的安装/下载？
   ├─ 是 → 询问（取消 / 等待完成 / 放弃并回滚）→ 按选择处理
   └─ 否 → 保存窗口几何与列宽（写自己的 ui-settings.json，不写 Core 的 config.json）
        → 取消订阅
        → RegistryManager.DisposeAll()          ← Core/Registry/RegistryManager.cs:295 —— 必须
        → 释放 GameInstanceManager
        → 退出码 0
```

**强杀/崩溃路径**：因为 `CkanTransaction` 保证文件事务原子性，强杀后注册表不会处于半装状态；`RegistryManager` 的锁文件置于进程退出时由 OS 释放（文件句柄），但**目录里的 lock 文件可能残留**——启动时若检测到"锁文件存在但对应的 KSP 无进程/无锁"，给出可点的一键修复（删除 stale lock），见 09 篇 §3.3。

### 5.3 单实例与协议

- 单实例：命名互斥体 + 本地 socket（Windows: Named Pipe；Linux/macOS: Unix Domain Socket）传参；
- `ckan://` 协议：注册到 `HKCU\Software\Classes\ckan` / `~/.local/share/applications`，把 URI 转发给运行中实例；
- 已存在的 `ckan.exe` 老 GUI 若也占用了同一实例的锁，新 UI 检测到后显示"该实例被其他 CKAN 进程占用"，并列出可关闭的进程——**不要抢占**。

## 6. 状态管理

不做全局状态容器，改为"服务即真相 + ViewModel 局部状态"：

| 状态 | 归属 | 生命周期 |
|---|---|---|
| 当前实例 | `IInstanceService`（包装 `GameInstanceManager`） | 应用级，单例 |
| 已装模组集合 | `IRegistryService`（包装 `RegistryManager`） | 实例级，实例切换时重建 |
| 可用模组索引 | `IRepositoryService`（包装 `RepositoryDataManager`） | 应用级，仓库变更时重建 |
| 兼容性/过滤配置 | `ISettingsService`（包装 `IConfiguration`） | 应用级 |
| 进行中的操作（下载/安装） | `IOperationQueue` | 应用级，可跨页面存活 |
| 列表选择、滚动位置、排序 | 对应 `ListViewModel` | 页面级 |
| 窗口几何 | `IUiSettingsStore`（自有文件） | 应用级 |

**规则**：任何服务层对象的状态变更，都以"快照 + 事件"形式暴露（`RegistrySnapshot Installed { get; }` + `event Action<RegistryChangedReason>`），ViewModel 订阅后就地 `SetProperty`，避免 ViewModel 之间互发消息。

## 7. 配置与文件落位

| 文件 | 归属 | 谁写 | 说明 |
|---|---|---|---|
| `%LocalAppData%\CKAN\config.json` | Core | 仅 `ISettingsService`（转调 `IConfiguration`） | 与 CLI/老 GUI 共享，**新 UI 不直接写** |
| `%LocalAppData%\CKAN\ckan-ui.json` | 新 UI | `IUiSettingsStore` | 窗口几何、列宽、上次页面、主题 |
| `%LocalAppData%\CKAN\registry\*.ckan` | Core | `RegistryManager` | 不改 |
| `%LocalAppData%\CKAN\downloads\` | Core | `NetAsyncModulesDownloader` | 不改 |
| `%LocalAppData%\CKAN\CKAN.log` | Core (log4net) | `Logging` | 新 UI 追加自己的 logger 名，写同一文件 |
| `<KSP>\CKAN\` | Core | 实例级配置 | 不改 |

`ckan-ui.json` 独立的原因：Core 的 `IConfiguration` 是强类型且会被 CLI 读取，塞 UI 专属字段进去会造成跨工具schema污染，且 `JsonConfiguration` 的未知字段处理策略不适合放 UI 状态。

## 8. 错误处理与日志

| 层级 | 策略 |
|---|---|
| Core 抛的异常 | 在 `Services` 边界统一 catch，转 `OperationResult<T>`（成功值 / 用户可读消息 / 技术细节 / 是否可重试） |
| 预期失败（网络、磁盘满、游戏在运行） | 不抛异常，返回失败结果，UI 用非模态提示条 |
| 未预期异常 | 全局 `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` → 崩溃窗口（显示日志路径 + "复制诊断信息"） |
| 日志级别 | Info：操作起止；Warn：重试、降级；Error：异常。**禁止**在循环里打 Info（3,000 mod 会刷爆日志） |

## 9. 演进预留

| 预留点 | 免改造的实现方式 |
|---|---|
| 换主题/深色模式 | Avalonia 主题资源字典 + `ThemeVariant`，不硬编码颜色 |
| 多语言 | 起步就用 `.resx` + `DynamicResource`，不要事后补 `ResourceManager` 调用 |
| 第二窗口（如 modpack 编辑器） | 视图注册表按 `Type` 解析，新增窗口只加一行映射 |
| 无头/CLI 复用 ViewModel | VM 不依赖 Avalonia，靠 `IUser` 走 Core；必要时可做"headless UI 驱动器" |

## 10. 附：技术栈定稿（v1.1 回填，依据 [11 篇](11-tech-stack-decision.md)）

| 项 | 定稿值 |
|---|---|
| 框架 | Avalonia **12**（不回退 11；12 要求 .NET 8+，与 `net10.0` 一致） |
| TFM | `net10.0` 单目标，**不设** `net481` |
| 发布形态 | `SelfContained=true` + `PublishReadyToRun=true` + `PublishSingleFile=false`，zip 分发（理由见 09 篇 §11） |
| 系统下限 | **Win10 1607**；1507/1511、Win7/8.1 不支持（11 篇 §8 Q1 已拍板） |
| 新增目录 | `Presentation/Themes/`（Fluent token 覆盖）、`Presentation/Motion/`（过渡与页面动画）、`Services/Platform/Windows/Backdrop.cs`（材质能力探测与降级） |
| 新增硬约束 | ① 列表行内禁止 `BlurEffect`/`DropShadowEffect`；② 材质必须"能力探测 + 三档降级"（Mica→Acrylic→纯色），不得在 XAML 硬编码 `TransparencyLevelHint` |

---

## §11 附：E2 可观测性域的分层落位（v1.4 新增，依据 [14 篇](14-log-and-ai-debug.md)）

| 层 | 新增内容 | 依赖规则 |
|---|---|---|
| `CkanUI.Core`（领域） | `LogEvent`/`LogLevel`/`LogFamily` 记录类型、`DiagnosticBundle` 描述对象 | 不依赖 Avalonia、不依赖 HTTP |
| `CkanUI.Services`（服务） | `ILogStream`、`LogCollector`（log4net appender + IUser 适配 + 异常钩子）、`LogClassifier`（规则表）、`IDiagnosticsBundler`/`Redactor`、`IAiClient`/`IAiCache`、`ISecretStore`（DPAPI） | 可依赖 `CKAN`（Core），不可依赖 `Presentation` |
| `CkanUI.Presentation`（UI） | 日志页、底部抽屉、发送预览、AI 结果面板、染色主题 token | 只依赖 VM 与 `I*Service` |
| `CkanUI.Cli`（可选） | `logs tail/export/bundle`、`ai analyze --bundle` | 与 App 复用同一 `Services` 实现 |

**两条新增硬约束**：

1. **外发出口唯一**：整个解决方案里只有 `IAiClient.SendAsync()` 一个方法会发出含日志内容的请求；架构测试断言"除 `AiClient` 外无人引用 `HttpClient` 发送日志类负载"（同 `01` 篇既有架构测试风格）；
2. **脱敏不可绕过**：`IAiClient` 的入参类型是 `RedactedBundle`（由 `Redactor` 产出），**不接受裸字符串**——类型系统层面堵住"绕过脱敏直接发"的路。

---

## §12 附：E3 Mod 配置域的分层落位（v1.5 新增，依据 [15 篇](15-mod-config-adapters.md)）

| 层 | 新增内容 | 依赖规则 |
| --- | --- | --- |
| `CkanUI.Core` | `ModConfigAdapter`/`ConfigField`/`FieldType`/`PresetSpec`/`AdapterMatch` 记录类型 | 不依赖 Avalonia、不依赖文件系统 |
| `CkanUI.Services` | `AdapterLoader`（内置 + 用户目录 + 导入）、`ModConfigDetector`（注册表 + 文件 + 版本区间，纯函数）、`ConfigNodeDocument`（**复用 E1 的解析器**）、`IAdapterWriter`（快照 → 原子写 → 重新解析自校验）、`AdapterDraftExporter` | 可依赖 `CKAN`；不可依赖 `Presentation` |
| `CkanUI.Presentation` | Mod 配置页（左卡片列表 + 右表单 + 底部 diff 条）、字段渲染器、只读原始视图 | 只依赖 VM 与服务接口 |
| `adapters/`（随包目录） | 每个 mod 一个子目录：`adapter.json` + `fixtures/` + `presets/` + `README.md` | **目录即插件**，加目录即加支持，不改代码 |

**三条新增硬约束**：

1. **Presentation 不读文件**：所有读写经 `IConfigDocumentService`；
2. **写盘唯一入口** `IAdapterWriter.WriteAsync(...)`，与 E1 的 `ISaveWriter` 共用同一套安全网（快照 + 原子替换 + 自校验）；
3. **adapter 是纯声明式 JSON**（不含可执行代码/脚本），加载时做 schema 校验，失败则该 adapter 不启用而非崩溃。

---

## §13 附：E4 本地化的分层落位（v1.6 新增，依据 [16 篇](16-i18n.md)）

| 层 | 新增内容 | 依赖规则 |
| --- | --- | --- |
| `CkanUI.Localization` | `ILocalizer`/`Localizer`（L1→L2→L3→L4 解析）、`LegacyCatalog`（官方 resx 快照）、`CoreCatalog`、`LocaleInfo`（含覆盖率） | 只依赖 `CKAN`（取 Core 资源），**不依赖 Avalonia** |
| `CkanUI.App.Localization` | `{loc:T}` 标记扩展、`LanguageObservable`、`LanguageSwitcher` | 依赖 Avalonia + `CkanUI.Localization` |
| `Presentation/Themes/Typography.axaml` | `ThemeFontFamily` / `ThemeFontFamilyMono`（含 CJK 后备链） | 视图**禁止**硬编码 `FontFamily` |
| `tools/LegacyResxSync` | 构建期把官方 resx 复制为快照 + `--report/--verify/--audit` | 独立 console 工具，不进运行时 |

**三条新增硬约束**：

1. **文案只走 `ILocalizer`**：视图里禁止字面量 UI 文本（CI 用伪本地化 + 源码扫描双重校验）；
2. **字体只走主题资源**：任何 `FontFamily="…"` 字面量在评审中直接打回；
3. **`GUI` 程序集不可引用**（WinExe + WinForms）：官方文案只能以 resx 快照形式复用，这是 D11 的物理原因。

## §14 E5 Mod 元数据译文的分层（v1.2 新增）

- `CkanUI.ModText`（纯库，**无 IO**）：`IModTextProvider`、`ModText`（`Text`/`Src`/`Stale`）、`KeyLevel` 策略；
- `Services/ModText`：译文包下载/校验（`UI18n.Core.Update`）、`DictPack` 装载、`state.json`、预算与队列；
- `Presentation`：只读 provider，**不得**触碰包文件。

三条硬约束（评审红线）：

1. **译文永不写回 `CkanModule`**：`abstract`/`description` 在内存里始终是英文原文，译文只在展示层替换——否则 `registry.json` 与导出的 modpack 会被污染；
2. **哈希只用 `UI18n.Core.Hashing.Fnv1a`**：禁止在 C# 侧另写 FNV 或改用 `string.GetHashCode()`（跨进程不稳定，且与 `i18n_tools` 契约不一致）；
3. **包根与 CKAN 数据根严格分离**：译文包只落 `%LocalAppData%\CKAN\ckan-ui\i18n\packs\`，绝不进 `%LocalAppData%\CKAN\repos`（那是 Core 的仓库缓存目录）。

## §15 E6 资源释放的分层归属（v1.3 新增）

- `CkanUI.Services.Lifecycle`：`IGameLaunchMonitor`（探测 KSP 进程/窗口/CPU）、`IResourceGovernor`（L0–L4 编排）；
- `CkanUI.Services.Platform.Windows`：`WorkingSetTrimmer`（`EmptyWorkingSet`/`SetProcessWorkingSetSizeEx`）、`ProcessPriority`（`BelowNormal`、`MEMORY_PRIORITY_LOW`）；
- `Presentation`：只显示阶段与进度，**不得**直接调用任何 trim API。

四条硬约束（评审红线）：

1. **Core 侧数据的生命周期归 DI 作用域**：`RepositoryDataManager` 必须放在可释放的子作用域里，否则 L2 无从下手（Core 没有 `Clear()`，见 [18 篇](18-launch-resource-release.md) §2.4）；
2. **`GC.Collect` 只允许在 L3 一处出现**，且必须 `blocking: true, compacting: true` 并配 `WaitForPendingFinalizers`；其它任何路径出现即评审不通过；
3. **`Directory.SetCurrentDirectory` 的坑必须隔离**：`PlayGame` 会把进程 CWD 改成游戏目录（`Core/GameInstance.cs:370`），我们的路径服务只接受绝对路径，并在启动后立即把 CWD 复位；
4. **释放必须可逆**：任何一层新增释放动作，必须同时提供恢复路径与进度，否则不许合入。

## §16 E7 安装/注册/卸载的分层归属（v1.4 新增）

依据 [19 篇](19-windows-install-flow.md)。

| 关注点 | 落位 | 说明 |
| --- | --- | --- |
| 安装/卸载本身 | **仓库外的打包脚本**（`build/pack/*.ps1` + Velopack `vpk`） | 不进解决方案的运行时程序集；产物是 `CkanUISetup.exe`（EXE-only，不出 MSI）与便携 zip |
| 注册表读写 | `Presentation/` 之外的 `Services/Platform/Windows/ShellIntegration.cs`（接口 `IShellIntegration`） | 协议、文件关联、`Capabilities`、`App Paths`、ARP 补充项全部只经此一处 |
| 版本与标识常量 | `CkanUI.Core/BuildInfo.cs`：`PackId = "CkanUI"`、`ProductName`、`Publisher`、`Version` | `PackId` 与版本号格式**只允许在这里出现一次** |
| 首启向导 | `Presentation/Views/Setup/` + `ViewModels/SetupWizardViewModel` | 纯 UI，动作走 `IShellIntegration` 与 `IGameInstanceService` |
| 依赖检测（VC++） | `Services/Platform/Windows/PrerequisiteProbe.cs` | 只读注册表判断，安装动作由安装器负责；App 侧只做"启动前自检 + 可复制诊断" |

两条硬约束（写进 Code Review 必查）：

1. **Presentation 层不得出现 `Microsoft.Win32.Registry`**（与 01 篇 §4 的依赖规则同一条检查项）；
2. **运行时不写 HKLM**。任何需要 HKLM 的动作（例如清理旧版 `ckan.exe` 写的协议键）必须是"提示用户在安装器/提权上下文中执行"，不得由 App 静默尝试。


## §17 E8 外观个性化的分层与硬约束（v1.5 新增）

依据 [20 篇](20-appearance-personalization.md)。

| 关注点 | 落位 | 说明 |
| --- | --- | --- |
| token 资源 | `Presentation/Themes/Tokens/*.axaml`（8 组）与 `Presentation/Themes/Variants/{Light,Dark,HighContrast}.axaml` | 只有这里允许出现颜色字面量 |
| 主题包加载 | `Services/Theme/ThemePackLoader.cs`（接口 `IThemePackService`） | 只读 JSON；不引用任何 XAML 加载器 |
| 材质/窗口能力 | `Services/Platform/Windows/Backdrop.cs` | 唯一的 `TransparencyLevelHint` 书写处（01 篇 §10 已有此条，此处扩为"唯一"） |
| 背景图 | `Services/Theme/BackgroundService.cs` | 解码、降采样、缓存、释放都在服务层；VM 只拿 `IImage` |
| 设置页 | `Presentation/Views/Settings/AppearanceView.axaml` + `ViewModels/AppearanceViewModel` | 实时预览用 `ThemeVariantScope` |

四条硬约束（Code Review 必查，且 CI 用 grep 兜底）：

1. **界面代码里禁止颜色/圆角/字号字面量**：拦 `#[0-9A-Fa-f]{6}`、`CornerRadius="\d`、`FontSize="\d`（白名单：`Presentation/Themes/**`）；
2. **禁止 `StaticResource` 引用 `Ui.*`**：`ThemeDictionaries` 里的键只能用 `DynamicResource`（用它取不到并会抛异常）；
3. **禁止从外部路径加载 XAML**：不出现 `XamlLoader.Load` / `AvaloniaRuntimeXamlLoader` 对用户目录的调用——XAML 可挂事件与自定义控件，等于任意代码执行；
4. **数据文本用独立 token**：`Ui.Font.Data*` 不受 `Ui.Font.Scale` 影响（Mod 名/标识符/版本号/路径不得随字号缩放）。

## §18 目录纪律与工作区卫生（v1.1 回填，依据 [21 篇](21-repo-layout.md)）

§3 的项目清单是**边界定义**，[21 篇](21-repo-layout.md) 把它落成目录与 CI 门禁。分工如下：

| 事项 | 本文（01） | 21 篇 |
| --- | --- | --- |
| 项目边界 | 项目清单、TFM、允许引用 | 四条架构测试的具体断言（命名空间白名单） |
| 目录 | 顶层 src / tests / external | 完整树（含 adapters、themes、i18n、docs、artifacts、tools、spikes） |
| 文档与产物 | — | docs / artifacts / tests/fixtures 三套落位规则 |
| 命名 | — | 项目 = 命名空间 = 目录，统一 CkanUI.<Layer>[.<Domain>] |

**三条与本文原则直接绑定的硬约束**（由 21 篇 §5/§12 的门禁拦）：

1. Avalonia 命名空间只在 CkanUI.App、CkanUI.Controls、CkanUI.Platform 与 Services/Threading 出现——否则 P5（可 headless 测试）立刻失效；
2. CKAN（Core）命名空间只在 CkanUI.Services 与 CkanUI.Localization 出现——否则 P1（Core 唯一真相源）会被绕过；
3. 命名统一为 CkanUI.*（历史文档中的 CkanUi.* 已全部改正，与 PackId=CkanUI 一致）。
