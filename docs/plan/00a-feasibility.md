# CKAN UI 重构可行性评估

> 调查对象：`C:\Users\43701\Documents\github\CKAN`（fork of KSP-CKAN/CKAN，master，CHANGELOG 顶部为 v1.36.5）
> 调查方式：静态代码分析 + 公开包元数据（本机 NuGet 源不可达，未能编译验证，详见第 10 节）

## 1. 结论

**可行，而且边界比一般遗留项目干净得多。** 三条硬证据：

1. `Core/CKAN-core.csproj:25` 的 `TargetFrameworks` 是 `netstandard2.0;net481;net10.0`，并且 `:29` 的 `PackageId` 是 `CKAN`、`:40` 开了 `GeneratePackageOnBuild` —— 核心本来就是作为**独立类库**设计和发布的（nuget.org 上 `CKAN 1.36.4` 已上线，同时提供 netstandard2.0 / net481 / net10.0 三个 TFM）。
2. `Core/` 里 **0 处** `System.Windows.Forms`、**0 处** `CKAN.GUI` 引用。唯一残留的 UI 类型是 `Core/Types/Labels/ModuleLabel.cs:31` 的 `public Color? Color`（`System.Drawing.Color`，结构体，跨平台安全）。
3. 仓库里**已经存在第二套 UI**：`ConsoleUI/`（49 文件 / 9,244 行，含自研 Toolkit 4,259 行），`ConsoleUI/CKAN-ConsoleUI.csproj:49` 只引用 `Core`，完整实现了实例管理、仓库源、依赖解析、安装、标签、过滤等流程。这就是"换 UI 不动 Core"的现成证明。

**真正的成本不在"能不能复用 Core"，而在"要重写多少 GUI 层"。** GUI 共 29,756 行，其中 10,153 行是 WinForms 自动生成的 `.Designer.cs`（直接丢弃），剩下 19,603 行是界面与业务混杂的逻辑，只有约 1,300 行可以原地搬走。

## 2. 项目拓扑与规模

| 项目 | 文件 | 行数 | 依赖 | 重构中的角色 |
|---|---|---|---|---|
| `Core/` | 151 | 24,661 | 无 UI 依赖 | **原样复用** |
| `GUI/` | 135 | 29,756（Designer 10,153 / 逻辑 19,603） | Core + WinForms | 被替换对象 |
| `ConsoleUI/` | 49 | 9,244（Toolkit 4,259 / 应用 4,975） | Core | 参考实现 |
| `Cmdline/` | 29 | 6,475 | Core + ConsoleUI + GUI | 不动 |
| `Netkan/` | 116 | 7,708 | Core | 不动 |
| `Tests/` | 179 | 30,792（566 个 `[Test]`） | Core / Cmdline / ConsoleUI / GUI | 回归安全网 |

Core 内部分布（行数）：`IO` 3,371、`Types` 3,477、`Registry` 3,148、`Net` 2,790、`Versioning` 2,094、`Relationships` 2,009、`Repositories` 1,324、`Games` 1,283、`Extensions` 965、`Converters` 851、`Configuration` 617、`AutoUpdate` 335。也就是说：**解包安装、下载队列、注册表、版本匹配、依赖求解、仓库索引、配置 —— 全在 Core**。

## 3. 复用边界

| 层 | 逻辑行数 | 结论 |
|---|---|---|
| `GUI/Model/ModSearch.cs` | 716 | **直接复用**（仅 `System.*`，搜索语法解析） |
| `GUI/Model/ModChange.cs` | 192 | **直接复用**（变更集模型，`:50` `class ModChange`） |
| `GUI/Model/GUIMod.cs` | 399 | 几乎可直接复用（`using System.Windows.Forms` 实际未用到符号） |
| `GUI/Model/ModList.cs` | 707 | **需解耦**：`:67` 用 `Dictionary<string, DataGridViewRow>` 当数据容器、`:144` `MakeRow` 直接产出 `DataGridViewRow`；过滤/排序/标签判定逻辑要抽出来重写 |
| `GUI/Model/GUIConfiguration.cs` | 274 | 需改写（窗口几何等 WinForms 特有状态） |
| `GUI/Controls/*.cs` | 8,085 | 重写（其中 `ManageMods.cs` 2,336 行是主列表，含真实业务逻辑，值得当规格读） |
| `GUI/Dialogs/*.cs` | 3,571 | 重写（多为薄封装：ErrorDialog / YesNo / Selection / Settings 910 行） |
| `GUI/Main/*.cs` | 3,507 | 重写，但**编排顺序就是最好的规格文档** |
| `GUI/*.Designer.cs` | 10,153 | 丢弃 |
| 顶层（`Util.cs`/`ControlFactory.cs`/`GUIUser.cs`/`TabController.cs` 等） | 1,538 | 重写 |

## 4. 新 UI 必须实现的集成契约

这是全部工作量里最"必须做对"的部分，一共 7 件事：

1. **日志**：`Logging.Initialize()`。注意 `Core/Logging.cs:23` 是从 `Environment.CurrentDirectory` 找 `log4net.xml`，新 UI 必须保证 CWD 或自带该文件，否则只按 Warn 级输出到控制台。
2. **配置**：`ServiceLocator.Container.Resolve<IConfiguration>()`（`Core/ServiceLocator.cs:12`，全局 Autofac 容器，只注册了 4 个类型，没有过度抽象）。配置落在 `%LocalAppData%/CKAN/config.json`（`Core/Configuration/JsonConfiguration.cs:87` + `Core/IO/CKANPathUtils.cs:15`）。
3. **`IUser` 实现**：`Core/User.cs:10` 只 6 个成员（`Headless`、`RaiseYesNoDialog`、`RaiseSelectionDialog`、`RaiseError`、`RaiseProgress` ×2、`RaiseMessage`）。参照 `GUI/GUIUser.cs:19`（139 行，实际逻辑约 80 行）。**这是 Core 与 UI 之间唯一的人机交互出口**。
4. **实例管理**：`new GameInstanceManager(user, config)`（`Core/GameInstanceManager.cs:49`）→ `GetPreferredInstance()`（:75）/ `SetCurrentInstance()`（:476）/ `AddInstance()`（:204）/ `CloneInstance()`（:245）。
5. **注册表锁**：`RegistryManager.Instance(inst, repoData)`（`Core/Registry/RegistryManager.cs:265`）取实例锁；退出必须 `RegistryManager.DisposeAll()`（:295），否则下次启动报锁冲突。
6. **事件订阅**（进度都从 Core 主动推）：`GameInstanceManager.InstanceChanged`（:35）/ `CacheChanged`（:38）、`RepositoryDataManager.Update()`（`Core/Repositories/RepositoryDataManager.cs:144`）+ `Updated` 事件（:253）、`ModuleInstaller.InstallProgress/RemoveProgress/OneComplete`（`Core/IO/ModuleInstaller.cs:46-48`）、`NetAsyncModulesDownloader.*`（`Core/Net/NetAsyncModulesDownloader.cs:23-27`）。
7. **文件事务**：所有改盘操作要包在 `CkanTransaction.CreateTransactionScope()`（`Core/CkanTransaction.cs:24`）里 —— 参考 `GUI/Main/MainInstall.cs:259`、`GUI/Main/MainRepo.cs:88`、`ConsoleUI/InstallScreen.cs:57`。

启动骨架（现有 GUI 的等价序列，`GUI/Main/Main.cs:98,130,166-180`）：

```
Logging.Initialize()
config = ServiceLocator.Container.Resolve<IConfiguration>()
user   = new YourUser(dispatcher)            // IUser
mgr    = new GameInstanceManager(user, config)
mgr.GetPreferredInstance()                    // 或 SetCurrentInstance(name)
repoData = ServiceLocator.Container.Resolve<RepositoryDataManager>()
... 退出时 RegistryManager.DisposeAll()
```

## 5. 风险与坑（按危险度排序）

| # | 坑 | 证据 | 影响与对策 |
|---|---|---|---|
| 1 | **Core 自己会弹框**：`ModuleInstaller.InstallList` 内部调用 `User.RaiseYesNoDialog(ModuleInstallerContinuePrompt)` | `Core/IO/ModuleInstaller.cs:119` | 新 UI 不能"纯 MVVM 展示"，必须在 `IUser` 里做线程封送；或传 `ConfirmPrompt: false` 自行确认 |
| 2 | **回调来自后台线程** | 所有 progress 事件由下载/解包线程触发 | Avalonia/WPF 都要 `Dispatcher.Post`；注意 `RaiseSelectionDialog` 是同步返回 int，实现不好会死锁（用 `Dispatcher.Invoke` + `TaskCompletionSource`） |
| 3 | **Windows 绑定**：`TxFileManager`（TxF 事务文件管理器，17 处使用）、junction/hardlink（`Core/IO/DirectoryLink.cs`、`HardLink.cs`）、Steam 注册表探测、`Win32RegistryConfiguration` | `grep TxFileManager Core` = 17 处 | Windows-only 重写可直接沿用；若要做 Linux/macOS GUI，需要替换文件事务层（ConsoleUI 在 Linux 上跑得通说明有回退路径，但事务性会降级） |
| 4 | **实例互斥锁** | `RegistryManager` 每实例持锁文件，`GameInstance.IsMaybeLocked` | 新旧 UI **不能同时对同一 KSP 实例操作**，并存期必须提示用户 |
| 5 | **配置/缓存/实例列表与 CLI、老 GUI 共享** | 同一个 `AppDataPath` | 并存期会有写覆盖风险；独立仓库方案要明确"替换"而非"并存" |
| 6 | **AutoUpdate 只对"当前 exe"有效** | `Core/AutoUpdate/AutoUpdate.cs:51` 的 `CanUpdate` 看运行中 exe，`:62` `StartUpdateProcess` 会替换 exe 并拉起 `ckan.exe` | 新 UI 不要用 Core 的自动更新，自己做（或先禁用） |
| 7 | **Model 层 WinForms 粘着** | `GUI/Model/ModList.cs:67,144` | 过滤/排序/标签逻辑要从 `DataGridViewRow` 里拆出来，否则只能复制粘贴后重写 |
| 8 | **本地化是 resx key 体系**（Crowdin） | `GUI/Localization/<lang>/*.resx`、`Core/Properties/Resources.resx` | Core 的字符串会自动跟语言走；新 UI 自己的字符串要新建一套 key，老 key 不能直接复用（`Properties.Resources` 是命名空间级） |
| 9 | **打包链路** | `build/BuildPaths.cs` + ILRepack 把 Cmdline/Core/ConsoleUI/GUI 合成单个 `ckan.exe` | 若在仓库内加项目，要决定它是否进 payload、以及 `ckan.exe` 与独立 exe 的关系 |

## 6. 三种落地方案

| 方案 | 做法 | 优点 | 代价 | 适用 |
|---|---|---|---|---|
| **A 仓库内新项目** | 新增 `AvaloniaUI/` 或 `GUI2/`，`ProjectReference` 到 `Core/CKAN-core.csproj`，`Cmdline` 加一个 `gui2` verb | 单次构建、CI/测试/版本号统一、Core 改动立刻可用、可并行 A/B | 仓库变重；release 打包需决策；上游 KSP-CKAN 基本不会接受第二套 GUI（仅本地/fork 有意义） | 你要长期维护这个 fork |
| **B 独立仓库 + NuGet** | 新仓库 `dotnet add package CKAN` | UI 仓库极干净；版本化清晰；不背 Core 的构建包袱 | 卡在已发布版本（当前 1.36.4，master 1.36.5）；Core bug 只能等发版，或本地 override；测试/CI 断链 | 想快速出一个"外观完全不同"的前端 |
| **C 混合（推荐）** | 独立仓库 + `Core/` 作为 git submodule 或 `<ProjectReference>` 指向本地 checkout | 拿到 master 的 Core 修复，同时 UI 仓库独立 | 需要维护 submodule 同步脚本 | 大多数情况的最优解 |

## 7. UI 技术选型

| 框架 | TFM | 跨平台 | 评价 |
|---|---|---|---|
| **Avalonia**（推荐） | net10 / netstandard2.0 | Win/macOS/Linux | 与 Core 的 netstandard2.0 完美契合；MVVM + XAML 与 WPF 经验互通；能让 macOS/Linux 用户第一次拥有真正的图形界面（现在是 ConsoleUI/CLI） |
| WPF | net481 / net10.0-windows | 仅 Windows | 迁移心理成本最低（都是 XAML + 数据绑定），生态最稳；但维持 Windows-only 现状 |
| WinUI 3 | net10.0-windows | 仅 Win10+ | 观感最现代，但打包/MSIX 与 AOT 限制多，管理类工具收益不抵复杂度 |
| Blazor Hybrid / 本地 Web | net10 | 跨平台 | 复用 Web 技能、UI 迭代最快；但本地文件/进程/权限交互要过一层桥，安装器类应用体验偏重 |

## 8. 工作量估算（单人有经验开发）

| 模块 | 新写代码量 | 工期 |
|---|---|---|
| 启动骨架 + `IUser` + 线程封送 + 实例选择 | 500–800 | 1–2 周 |
| 模组列表（12 列、过滤/搜索/标签/冲突显示） | 1,200–1,800 | 3–5 周 |
| 模组详情（Metadata/Contents/Relationships/Versions） | ~1,000 | 2 周 |
| 变更集 + 下载/安装/升级/卸载 + 进度与取消 | ~1,500 | 2–3 周 |
| 设置（缓存、install filters、语言、仓库优先级） | ~800 | 1–2 周 |
| 实例管理（增删改、克隆、自动探测） | ~600 | 1 周 |
| PlayTime / 安装历史 / 未托管文件 / 下载统计 / modpack 导入导出 | ~1,000 | 2 周 |
| 对话框、托盘、`ckan://` 协议、单实例、异常兜底 | ~500 | 1 周 |

**合计**：新写 6,000–9,000 行；MVP（能装能卸能升级）4–6 周；全功能对齐 10–14 周；再加 30% 风险缓冲。参照物：ConsoleUI 用 9,244 行（含 4,259 行自研控件库）实现了功能完整的替代 UI。

## 9. 建议的第一步：2–3 天 spike

验收标准是"**不改 Core 一行代码**能走通全链路"：

1. 建空 Avalonia 项目，`ProjectReference` → `Core/CKAN-core.csproj`（net10.0）；
2. 实现 `IUser`（含 Dispatcher 封送）+ 启动骨架，列出实例、选实例；
3. `RegistryManager.Instance()` 读注册表，渲染 `Registry.InstalledModules` + `RepositoryDataManager.GetAvailableModules()` 到列表；
4. 选一个 mod → `RelationshipResolver` 算出变更 → `NetAsyncModulesDownloader` 下载 → `ModuleInstaller.InstallList(..., ConfirmPrompt: false)` 装进 KSP 目录，全程进度条 + 取消；
5. 退出时 `RegistryManager.DisposeAll()`，重开验证无锁残留。

任何一步卡住，都会立刻暴露是不是 Core 的接口不够用 —— 从静态分析看，**不够用的概率很低**；已知唯一需要"回到 Core 加东西"的是变更集模型（`ModChange` 目前在 GUI，`ChangePlan` 在 ConsoleUI 各写了一遍，共 ~200 行重复，可以顺手下沉到 Core）。

## 10. 本次调查的环境限制

- `dotnet build` 无法验证：本机 `https://api.nuget.org/v3/index.json` 不可达（`NU1301`，SSL/认证失败），本地缓存缺 `Autofac`、`SharpZipLib`、`TxFileManager`、`NJsonSchema`、`YamlDotNet` 等包，`restore` 失败。
- 因此以上结论基于：源码静态分析（行数、引用、ifdef、事件、`csproj` 配置）、nuget.org 上 `CKAN 1.36.4` 的目标框架与依赖清单、以及仓库内 ConsoleUI 作为既有第二 UI 的事实。
- 结论中"能否编译"这一环，需要一次联网 restore 来最终确认（预期 OK：NuGet 包本身已在 CI 里产出并通过测试）。

## 11. 后续详细计划

可行性确认后的 **22 份**执行计划在 [`doc/uir/`](uir/00-index.md)：架构、Core 集成契约、UI 规格、MVVM 与 Model 迁移、线程与封送、WBS 与里程碑（**854h / 23 周**；含新增能力域 E1 后为 **1104h / 30 周**；含 E2 后为 **1290h / 35 周**；含 E3 后为 **1464h / 40 周**；含 E4 后为 **1620h / 44 周**；含 E5 后为 **1754h / 48 周**；含 E6 后为 **1864h / 51 周**；含 E7 后为 **1990h / 55 周**；含 E8 后为 **2196h / 60 周**）、测试策略、风险登记、迁移打包发布、2–3 天 spike 任务卡、技术栈与 OS 覆盖决策、**全特性对齐清单（55 条 F-01..F-55）**、**存档工作台与 SFS 格式化工具（13 篇，250h / M7）**、**日志审查与 AI 辅助排障（14 篇，186h / M8）**、**社区 Mod 配置适配层（15 篇，174h / M9）**、**国际化与官方文案复用（16 篇，156h / M10）**、**Mod 源信息 i18n 与仓库同步（17 篇，134h / M11，复用 `i18n_tools` 契约与 `UI18n.Core` 运行时）**、**游戏启动后资源释放（18 篇，110h / M12，L0–L4 分层 + 可逆恢复）**、**Windows 标准安装/卸载/注册（19 篇，138h / 净增 126h / M13：Velopack 一键 per-user、EXE-only 不出 MSI、HKCU-only 注册、0 成本签名策略）**、**外观个性化（20 篇，206h / M14：深色/磨砂/圆角/背景/密度/字号/动效 + 只允许声明式 JSON 的主题包）**、**仓库布局与工作区卫生（21 篇，0h：三区制 + 12 项目边界矩阵 + 文档/调试/夹具三套落位 + `verify-layout` 12 条门禁，不新增工时）**、**测试用例与单元测试规格（22 篇，0h：五层用例体系 174 条用例行 + 替身构造器 + 覆盖率与七条合并门禁 + 三向追溯，全部并入既有 `TST-*`）**。

技术栈已定稿：**Avalonia 12 + `net10.0` 自包含发布，系统下限 Win10 1607**（[11 篇](uir/11-tech-stack-decision.md)）；范围已定稿：**完全特性对齐，不设 MVP 减配**（[12 篇](uir/12-feature-parity.md)）；新增能力：**`.sfs` 存档浏览/编辑 + 保字幂等的格式化工具**（[13 篇](uir/13-savegame-sfs.md)，不依赖 Core，可独立交付 CLI）。

## 12. 一句话建议

**用方案 C（独立 UI 仓库 + 源码引用 Core）+ Avalonia，先做 2–3 天 spike 打通"读实例 → 装一个 mod → 退出无残留"。** Core 的边界（`IUser` + `ServiceLocator` + 事件 + `CkanTransaction`）已经为多 UI 设计好了，重构风险主要落在"重写 19.6K 行混在界面里的逻辑"，而不是"能不能复用核心代码"。
