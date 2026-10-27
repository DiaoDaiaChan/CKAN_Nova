# 工程约定（所有 agent 必读）

> 本文件是执行层的**唯一共同约定**。规格本身的真相源是规格仓：`C:\Users\43701\Documents\github\CKAN\doc\uir\`（只读，23 篇 + `i18n/` 证据）。
> 代码仓是 `C:\Users\43701\Documents\github\ckan-ui`。两者分离（00-index D2/D16）。

## §1 目录与写作用域

权威目录树见 21 篇 §4。当前已就位：`src/` 11 个项目、`tests/` 7 个测试项目 + TestSupport + TestRunner、`adapters/`、`themes/`、`i18n/`、`docs/`、`tools/`、`.github/workflows/`。

**只改自己写作用域内的文件**（任务里写明）。跨域要改，先在任务里注明并通知 Lead，不要顺手改别人的文件。

## §2 构建

```bash
cd C:/Users/43701/Documents/github/ckan-ui
dotnet build ckan-ui.sln -v q --nologo          # 全量
dotnet build src/CkanUI.Sfs/CkanUI.Sfs.csproj   # 只建自己那块（并行时优先用这个）
```

- 自有项目 **0 警告**（`TreatWarningsAsErrors=true`，`Nullable=enable`，`AnalysisLevel=latest`）。
- 上游 `external/CKAN` 的 11 条警告（NU1904 / SYSLIB0050）已在 `Directory.Build.props` 里 `NoWarn`，**不要动上游代码**（00-index D5：不改 Core）。
- 并行开发时多个 agent 会同时写 `obj/`/`bin/`：**只构建自己那条链路**，遇到 `MSB3021/MSB3027/文件被占用` 等瞬时锁错误，等 5 秒重试一次；持续失败再报 Lead。

## §3 测试

沙箱里 `dotnet test` 不可用（VSTest testhost 打开父进程句柄被拒，Win32Exception 5）。用进程内 runner：

```bash
dotnet run --project tests/CkanUI.TestRunner                       # 全部
dotnet run --project tests/CkanUI.TestRunner -- Sfs Localization   # 只跑匹配的程序集
```

新增测试程序集时，要在 `tests/CkanUI.TestRunner/Program.cs` 的 `assemblies` 数组里登记（该文件由 Lead 维护，改前说一声）。

测试项目本身是标准 NUnit 类库，CI 上照常用 `dotnet test`。

## §4 代码风格

- 语言级别 `latest`，文件级命名空间 `namespace CkanUI.Xxx;`，`ImplicitUsings=enable`。
- 一个文件一个公共类型；VM 用 `[ObservableProperty]`/`[RelayCommand]`（CommunityToolkit.Mvvm 8.4.0，`partial class`）。
- 注释与用户可见文案用**中文**；标识符英文 PascalCase（21 篇 §10 命名规范）。
- 禁止 `#pragma warning disable` 掩盖自己的警告；确实需要就在本文件顶部 `NoWarn` 并写理由注释。
- **禁止**把本机绝对路径、日志、dump、真实存档写进仓库（21 篇 §12 H-03/H-10）。

## §5 架构铁律（`tests/CkanUI.Architecture.Tests` 会红）

1. `Avalonia.*` 只允许出现在 `CkanUI.App`、`CkanUI.Controls`、`CkanUI.Platform`、`CkanUI.Services/Threading/`；
2. `CKAN`（Core）命名空间只允许出现在 `CkanUI.Services` 与 `CkanUI.Localization`；
3. `CkanUI.ViewModels` 只能引用 `Services.Abstractions`，不得引用 `CkanUI.Services`；
4. `Core` / `Services.Abstractions` / `Sfs` / `ModText` 四个项目**零 ProjectReference 零 PackageReference**（测试框架除外）；
5. 依赖方向唯一：`App → ViewModels → Services.Abstractions ← Services → CKAN`。

## §6 提交

- 并行期间**不要各自 git commit**：Lead 统一提交与审查（避免索引竞争）。
- 完成一项就在回复里说明：改了哪些文件、构建结果、测试结果、剩余 TODO。

## §7 规格阅读顺序

动手前先读**与本任务直接相关的那一篇**（不要只读摘要）。篇目：00-index、01-architecture、02-core-integration、03-ui-spec、04-mvvm-and-models、05-threading、06-wbs-milestones、07-testing、08-risk-register、09-migration-packaging、10-spike-plan、11-tech-stack-decision、12-feature-parity、13-savegame-sfs、14-log-and-ai-debug、15-mod-config-adapters、16-i18n、17-mod-metadata-i18n、18-launch-resource-release、19-windows-install-flow、20-appearance-personalization、21-repo-layout、22-test-cases。

## §8 里程碑口径

M0 = ENV-01..05 + ARC-01..04 + INT-01..03，门禁六条：①`dotnet build` 0 警告 ②启动到主窗口 ③日志落盘 ④`IUser` 弹框不死锁 ⑤CI 三平台绿 ⑥`tools/verify-layout` 全绿。
当前进度：ENV-01 完成、ENV-02 完成、ENV-05 部分完成（9 个架构测试绿）。

## 修订记录

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-27 | v1.0 | 新建：M0 开工时的工程约定，供并行 agent 共同遵守 |
