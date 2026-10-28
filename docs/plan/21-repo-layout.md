# 21 仓库布局、模块边界与工作区卫生

> 本文件回答三个问题：**代码放哪**、**文档/调试/测试产物放哪**、**怎么让"放错"变成构建期错误**。
> 依据用户指令（2026-10-27）：「划清各个模块的边界和文件夹，保持工作区整洁，各种文档/debug/测试用例不要乱丢」。
> 本文**不新增工时**：它把 [01 篇](01-architecture.md) §3 的目录约定钉死，并把 [06 篇](06-wbs-milestones.md) M0 的 ENV-01/ENV-04/ENV-05 变成可验收的具体形状。

---

## §1 目标与非目标

| 编号 | 目标 |
| --- | --- |
| R-01 | 一个仓库一个职责：**代码仓**（新仓库 `ckan-ui`）与**规格仓**（本 CKAN fork 的 `doc/uir/`）互不混放 |
| R-02 | 模块边界 = 项目边界 = 目录边界，三者名字一致，靠 `.csproj` 引用表达，不靠"大家互相知道" |
| R-03 | 三类非代码产物各有唯一落位：**文档** → `docs/`、**调试产物** → `artifacts/`、**测试用例与夹具** → `tests/` |
| R-04 | 临时/草稿一律进 `_` 前缀目录（`_scratch/`）且被忽略，**仓库根目录保持零垃圾** |
| R-05 | 违反落位 = CI 红（`tools/verify-layout`），而不是靠 code review 记忆 |
| R-06 | 命名可预测：看到文件路径就能知道它属于哪层、哪个域、是否产物 |

非目标：不引入 monorepo 工具链（Nx/Turborepo 之类）；不做代码生成式分层；不要求把 Core 也搬进新仓库（Core 永远以 submodule 只读引入）。

---

## §2 现状盘点（2026-10-27 实测）

| 位置 | 内容 | 判定 |
| --- | --- | --- |
| 本仓库根 | 45 项，含上游 CKAN 全部工程（`Core/`、`GUI/`、`Cmdline/`、`Netkan/`、`Tests/`…） | **上游只读区**：只允许新增 `doc/uir/`、`doc/ui-refactor-feasibility.md` 与本地忽略的 `_scratch/` |
| `doc/` | `apt-repo.md`、`building.md`、`rpm-repo.md`（上游）+ `ui-refactor-feasibility.md` + `uir/` | 规划文档落在这里，最终随迁移进 `ckan-ui/docs/plan/` |
| `doc/uir/` | 21 篇规划 + `i18n/`（3 个数据文件） | 规格仓正文；`i18n/` 是**证据样本**（种子表/覆盖率/术语），进 git |
| `_probe/`（已删） | 6 项：`t.dll`、`t.exe`、`h.cs`、`np`、`app/`（含 `bin/`、`obj/`） | 早期探针 + 编译产物 → **删除** |
| `tools/SfsKit/`（已迁） | 5 文件：`SfsKit.csproj` + `SfsParser/SfsWriter/SfsModel/SfsLinter.cs` | 真实代码原型 → 移入 `_scratch/SfsKit/`，**不提交、不构建**，正式实现按 [13 篇](13-savegame-sfs.md) 在新仓库重写 |
| `spike/CkanAvaloniaSpike/`（已删） | 空目录 | 占位空目录 → **删除**（空目录在 git 里不存在，长期挂着只是噪音） |

清理后 `git status` 只剩两项未跟踪：`doc/ui-refactor-feasibility.md` 与 `doc/uir/`——这两项是**有意新增**，其余一律为零。

---

## §3 三区制（谁在哪里）

```
① 规格区（本仓库，CKAN fork）          只读上游 + doc/ 增量
   C:\Users\43701\Documents\github\CKAN\
   ├── Core\ GUI\ Cmdline\ ...          ← 上游，不改（唯一例外：RFC 批准的 Core 补丁）
   ├── doc\uir\                         ← 规划正文（21 篇）
   └── _scratch\                        ← 本地忽略：原型、探针、一次性脚本（7 天清理）

② 代码区（新仓库，单独 git）            全部实现代码在这
   C:\Users\43701\Documents\github\ckan-ui\
   └── external\CKAN\                   ← git submodule → 指向 ①

③ 产物区（不进任何 git）                机器相关、可重建、体积大
   ckan-ui\artifacts\<yyyymmdd>-<task>\ ← 日志、dump、诊断包、截图、bench 输出
```

铁律：**①不许出现 `bin/`、`obj/`、`*.exe`、`*.dll`、`*.log`、`*.sfs` 的副本**；**②不许出现一次性脚本**；**③不许出现在仓库里**（`artifacts/` 已被 ignore）。

---

## §4 新仓库权威目录树

```
ckan-ui\
├── ckan-ui.sln
├── Directory.Build.props              统一 LangVersion=latest / Nullable=enable / TreatWarningsAsErrors
├── Directory.Packages.props           集中包版本（Central Package Management）
├── global.json                        锁 SDK（10.0.401）
├── .editorconfig  .gitattributes  .gitignore
├── README.md  CHANGELOG.md  LICENSE.md
├── external\
│   └── CKAN\                          submodule（只读，见 09 篇 §2.2）
├── src\
│   ├── CkanUI.App\                    WinExe，组装根（唯一允许 new 具体实现的地方）
│   │   ├── App.axaml(.cs)  Program.cs
│   │   ├── Presentation\              视图层：Views\ Controls\ Themes\ Motion\ Converters\
│   │   ├── Localization\              {loc:T} 标记扩展（依赖 Avalonia）
│   │   └── Properties\Resources.resx  新 UI 文案（L1）
│   ├── CkanUI.ViewModels\             VM，零 Avalonia 引用
│   ├── CkanUI.Services\               服务实现（唯一接触 CKAN 的地方）
│   │   ├── Threading\  Install\  Repositories\  Saves\  Logging\  Ai\  Theme\  Lifecycle\  Platform\
│   ├── CkanUI.Services.Abstractions\  接口 + DTO，零依赖
│   ├── CkanUI.Core\                   领域模型与纯函数（无 Avalonia、无 IO）
│   ├── CkanUI.Controls\               自绘控件
│   ├── CkanUI.Platform\               单实例 IPC、协议、托盘；注册表读取在 Services/Platform（Steam 库定位）
│   ├── CkanUI.Localization\           三层解析 + 官方 resx 快照（纯库）
│   ├── CkanUI.ModText\                元数据译文 Provider（纯库、无 IO）
│   ├── CkanUI.Sfs\                    SFS 解析/序列化/格式化（纯库）
│   └── CkanUI.Sfs.Cli\                控制台工具（format/validate/diff）
├── tests\
│   ├── CkanUI.ViewModels.Tests\       headless VM 测试
│   ├── CkanUI.Services.Tests\         服务层集成（沙盒实例、假 IO）
│   ├── CkanUI.Sfs.Tests\              往返/幂等/坏文件
│   ├── CkanUI.TestSupport\            FakeUser、沙盒构造器、假端点、Dispatcher stub
│   └── fixtures\                      只读样本（见 §8）
├── adapters\<modId>\                  E3 数据：adapter.json + fixtures\ + presets\ + README.md
├── themes\<packId>\                   E8 内置主题：theme.json + preview.png + LICENSE
├── i18n\                              语言包与映射：legacy-map.json、glossary\、packs\
├── docs\
│   ├── README.md                      索引（唯一入口，禁止"无索引文档"）
│   ├── plan\  design\  adr\  research\  ops\  test\manual\
│   └── _drafts\                       未定稿（忽略）
├── tools\
│   ├── verify-layout\                 本次新增的卫生门禁（§12）
│   ├── gen-fixtures\                  大夹具生成器
│   └── gates\                         占位符/术语/脱敏校验脚本
├── spikes\                            一次性技术验证（忽略，7 天清理）
├── artifacts\                         产物区（忽略，见 §7）
└── .github\workflows\                 build.yml / hygiene.yml / release.yml
```

---

## §5 模块边界矩阵

依赖方向只有一条：`App(Presentation) → ViewModels → Services.Abstractions ← Services → CKAN`，另有 `Localization / ModText / Sfs` 三条纯库支线可被任意层引用（它们零 UI 零 Core 依赖）。

| 项目 | 允许引用 | 明令禁止 | 责任 |
| --- | --- | --- | --- |
| `CkanUI.App` | 全部 `src/` 项目 + Avalonia | 直接 `using CKAN;`（**必须**经 Services） | 组装根、DI 注册、视图与样式 |
| `CkanUI.ViewModels` | `Services.Abstractions`、`Core`、`Localization`、`ModText`、`CommunityToolkit.Mvvm` | `Avalonia.*`、`CkanUI.Services`（实现）、`CKAN`（Core） | 状态、命令、校验、可测 |
| `CkanUI.Services` | `CKAN`（Core）、`Services.Abstractions`、`Core`、`Localization`、`ModText`、`Sfs` | `Avalonia.*`（**唯一例外**：`Services/Threading/AvaloniaDispatcher.cs`） | 唯一接触 Core 的地方；封送、事务、缓存 |
| `CkanUI.Services.Abstractions` | 无（零依赖） | 任何实现、任何 UI | 接口 + DTO |
| `CkanUI.Core` | 无 | Avalonia、IO、`CKAN` | 领域记录类型与纯函数 |
| `CkanUI.Controls` | Avalonia、`Core` | `Services`、`ViewModels`、`CKAN` | 通用控件（虚拟化列表、徽章、环） |
| `CkanUI.Platform` | Avalonia、`Services.Abstractions` | `ViewModels`、`CKAN` | 平台能力（单实例、协议、托盘） |
| `CkanUI.Localization` | `CKAN`（读 Core 资源） | Avalonia | 三层解析与快照 |
| `CkanUI.ModText` | 无 | Avalonia、IO、`CKAN` | 译文查找（纯函数） |
| `CkanUI.Sfs` | 无（连 Core 都不引用） | Avalonia、`CKAN` | SFS 文本模型与格式化 |
| `CkanUI.Sfs.Cli` | `Sfs` | Avalonia | 命令行 |
| `*.Tests` | 被测项目 + `TestSupport` | `CkanUI.App`（除 UI 快照测试专用项） | 自动化验证 |

**四条自动检查**（`CkanUI.Architecture.Tests`，违规即红）：

1. `Avalonia` 命名空间只出现在 `App`、`Controls`、`Platform`、`Localization\App`、`Services\Threading`；
2. `CKAN`（Core）命名空间只出现在 `Services`、`Localization`、`Sfs` 之外**禁止**出现；
3. `ViewModels` 不得引用 `CkanUI.Services`（只能 `Abstractions`）；
4. `Sfs`、`ModText`、`Core`、`Abstractions` 四个项目的引用列表必须为空（除测试框架）。

> 命名统一说明：历史文档里出现过 `CkanUi.*` 与 `CkanUI.*` 两种写法，**已统一为 `CkanUI.*`**（与 `packId=CkanUI`、`PackId` 常量一致，见 [19 篇](19-windows-install-flow.md) §4）。命名空间 = 项目名，`CkanUI.Services.Saves` 这类点分层级来自目录，不再另造后缀。

---

## §6 文档落位规则

| 目录 | 放什么 | 生命周期 |
| --- | --- | --- |
| `docs/plan/` | 本套 21 篇的**迁移后位置**（文件名不变，`NN-topic.md`） | 长期，随版本修订 |
| `docs/design/` | 单点设计：状态机、协议、格式规范（如 SFS 格式、adapter schema） | 长期 |
| `docs/adr/` | `NNNN-slug.md`，一页一决策（现状、选项、结论、可逆性） | **永不删除**，只追加"被 NNNN 取代" |
| `docs/research/` | 取证记录：实测数据、抓包、官方文档摘录（带日期与链接） | 长期只读 |
| `docs/ops/` | 运维/发布手册：签名、GPO、Chocolatey、回滚 | 长期 |
| `docs/test/manual/` | 手工用例 `H-01-instances.md`…（与 07 篇编号对齐，含证据要求） | 长期 |
| `docs/_drafts/` | 未定稿、待评审 | 忽略；30 天未动即删 |

规则：
1. **命名**：`NN-topic.md`（两位编号，全局唯一）；ADR 用四位 `0001-*.md`；
2. **每篇必须有**：一级标题、目标章节、末尾修订记录；缺任一项 `verify-layout` 红。两类篇的标题风格不同，都算合规：
   - **基线篇**（`00`–`10`，含 `00-index`）：`## N. 标题`（如 `## 6. Definition of Done`），修订历史统一记在 [00 篇](00-index.md) 的「修订记录」里（v1.0 → v2.3 演进链），本篇不再各写一份；
   - **能力域篇**（`11`–`21`）：`## §N 标题`（如 `## §1 目标与非目标`），每篇末尾自带 `## 修订记录`；
   - 例外：[12 篇](12-feature-parity.md) 与 [13 篇](13-savegame-sfs.md) 的目标章节沿用原编号（`## 1. 判据`、`## 1. 目标`），只要含"目标/非目标"语义即通过；
3. **索引强制**：`docs/README.md` 是唯一入口，新文档不登记 = 不存在；
4. **图表**：`mermaid` 内联；截图存 `docs/design/img/` 且 ≤512 KB（超了走 artifacts 不提交）；
5. **不写进文档的东西**：完整日志、dump、真实存档、API Key、玩家个人路径（用 `<GameDir>` 占位）。

---

## §7 调试产物落位（`artifacts/`）

```
artifacts\
├── logs\            应用与 CLI 的滚动日志（ckanui-*.log）
├── dumps\           崩溃 dump、未捕获异常快照
├── bundles\         诊断包 zip（14 篇 §5）
├── screenshots\     截图/录屏（帧率取证、UI 回归对比）
├── bench\           BenchmarkDotNet 输出、帧时间 csv
└── install\         安装矩阵取证（`Setup.exe` 日志、reg query 输出）
```

规则：
1. **一次运行一个目录**：`artifacts\<yyyymmdd>-<task>\`，例如 `20261027-m14-backdrop\`；
2. `artifacts\` 整棵被 ignore，**任何情况下不提交**；需要留证的内容改为摘录进 `docs/research/`；
3. 保留期 7 天（超过由 `tools\verify-layout --prune` 清理），`bundles/` 可留最近 3 个；
4. 日志默认写 `%LocalAppData%\CKAN\logs\`（用户数据），`artifacts\logs\` 只在调试模式下启用并重定向；
5. **禁止**在仓库根、`src\`、`tests\` 下出现 `*.log`、`*.dmp`、`*.zip`、`*.tmp`、`out.txt`。

---

## §8 测试用例与夹具落位（`tests/`）

> 测试项目共 **6 个**（`Architecture` / `ViewModels` / `Services` / `Sfs` / `Localization` / `ModText`），外加 1 个共享库 `TestSupport`（不被 `dotnet test` 当测试项目）。用例级规格见 [22 篇](22-test-cases.md)。

| 位置 | 内容 | 入库 |
| --- | --- | --- |
| `tests\CkanUI.ViewModels.Tests\` | headless VM 单测，`XxxViewModelTests.cs` | 是 |
| `tests\CkanUI.Services.Tests\` | 集成测试（沙盒实例、假 HTTP、假 IO） | 是 |
| `tests\CkanUI.Sfs.Tests\` | 往返/幂等/坏文件矩阵 + 属性测试（`PT-*`） | 是 |
| `tests\CkanUI.Architecture.Tests\` | 程序集引用图与命名空间白名单（`UT-ARC-01..06`） | 是 |
| `tests\CkanUI.Localization.Tests\` | 三层解析 / 占位符 / `legacy-map` 构建期校验（`UT-L10N-*`） | 是 |
| `tests\CkanUI.ModText.Tests\` | `fnv1a64` + `normalize` 跨语言契约（`UT-MTXT-*`） | 是 |
| `tests\CkanUI.TestSupport\` | 共享库（**不是测试项目**）：`FakeUser`、`SandboxInstance`、`FakeDispatcher`、`FakeClock`、`FakeAiEndpoint`、`RepoSnapshotBuilder`、`RegistryAssert` | 是 |
| `tests\fixtures\sfs\` | 真实存档（脱敏、≤256 KB 裁剪版）+ 9 类坏文件 | 是（小） |
| `tests\fixtures\cfg\` | 6 个 adapter 的 `Settings.cfg` 原文与期望输出 | 是 |
| `tests\fixtures\registry\` | 最小 `registry.json` / `config.json` 样本 | 是 |
| `tests\fixtures\repos\` | 迷你仓库 JSON（3–5 个 identifier，不是 37 MB 真库） | 是 |
| `tests\fixtures\golden\` | 黄金输出（树结构、诊断包脱敏结果、日志染色快照） | 是 |

规则：
1. **体积门限**：单文件 ≤256 KB 进 git；256 KB–2 MB 必须经 `tools\gen-fixtures` 从公开仓库数据重建；>2 MB 一律 LFS 或拒绝；
2. **只读断言**：测试跑完对 `fixtures\` 做目录哈希比对，被改写即红（防止"测试就地改夹具"）；
3. **不得依赖本机路径**：禁止在测试里写 `D:\Kerbal Space Program_newmod`，一律用夹具或临时目录；
4. **真实大体量数据**（4.8 MB 存档、37.9 MB 仓库 JSON）只做**一次性调研**（记进 `docs/research/`），**不进测试集**；需要压力测试时用生成器造 10 万行等价文件；
5. 手工用例脚本进 `docs\test\manual\`，编号与 [07 篇](07-testing.md) 的 `H-01..H-14` 一一对应。

---

## §9 临时区与草稿区

| 位置 | 允许放什么 | 清理 |
| --- | --- | --- |
| `_scratch\`（本仓库） | 探针程序、SfsKit 之类原型、一次性 bash/py 脚本 | 7 天；每次任务结束即删 |
| `spikes\`（新仓库） | 2–3 天的技术验证工程（对应 10 篇 SP-xx） | 结论写进 `docs/research/` 后删除；14 天硬上限 |
| `docs\_drafts\` | 未定稿文档 | 30 天 |

反模式（已在本文之前的实践里真实出现过，一并记下来）：
- 把编译产物（`t.dll`/`t.exe`/`bin/`/`obj/`）留在仓库根 → 改成 `_scratch\app\` 并在 `.gitignore`/`exclude` 里忽略；
- 把探针源码散在根目录（`h.cs`） → 进 `_scratch\<probe>\`；
- 建了空目录当"以后要用"（`spike\CkanAvaloniaSpike\`） → 直接删，用到时再建；
- 用一次性脚本改文档（`_backfill_e4.py` 等 4 个） → 用完立刻删，**脚本不能比它修改的文档活得久**。

---

## §10 命名规范

| 对象 | 规则 | 例 |
| --- | --- | --- |
| 项目 / 命名空间 | `CkanUI.<Layer>[.<Domain>]` | `CkanUI.Services.Saves` |
| 目录 | 同名于命名空间末段，PascalCase | `src/CkanUI.Services/Saves/` |
| 视图 | `XxxView.axaml` + `XxxView.axaml.cs` | `ManageModsView.axaml` |
| ViewModel | `XxxViewModel.cs`（一个文件一个类） | `ChangesetViewModel.cs` |
| 服务 | 接口 `IXxxService`，实现 `XxxService`，DI 注册集中在 `App/ServiceRegistration.cs` | `IInstallerService`/`InstallerService` |
| 测试 | `<被测类>Tests.cs` | `LogFilterViewModelTests.cs` |
| 适配器 | `adapters/<modId in registry>/adapter.json`（大小写按 registry 的 identifier） | `adapters/KSPCommunityFixes/` |
| 主题包 | `themes/<packId>/theme.json`，packId 小写短横线 | `themes/midnight/` |
| 语言包 | `i18n/packs/<locale>/<mod>.jsonl`，locale 用 `zh-CN` 形式 | `i18n/packs/zh-CN/CKANMeta.jsonl` |
| 文档 | `NN-topic.md`；ADR `NNNN-slug.md`；手工用例 `H-NN-name.md` | `21-repo-layout.md` |
| 产物目录 | `<yyyymmdd>-<task-slug>/` | `20261027-m14-backdrop/` |

---

## §11 `.gitignore` / `.gitattributes` 增量

```gitignore
# 产物与临时
artifacts/
spikes/
docs/_drafts/
_scratch/
*.log  *.dmp  *.tmp  *.bak
out.txt  results*.csv

# 构建
[Bb]in/  [Oo]bj/  _build/  .vs/  *.user

# 本机路径痕迹（防止把个人路径提交上去）
local.paths.json
```

```gitattributes
# 文本归一 + 二进制标注
*.cs      text eol=crlf
*.axaml   text eol=crlf
*.md      text eol=lf
*.sfs     text eol=crlf  # KSP 存档固定 CRLF，测试夹具必须保真
*.cfg     text eol=crlf
*.png     binary
*.zip     binary
```

`*.sfs` 与 `*.cfg` 的 `eol=crlf` 不是洁癖：13 篇 §3 的铁律 L3（往返字节一致）依赖它，被 autocrlf 改写会直接让黄金测试失真。

---

## §12 卫生门禁 `tools/verify-layout`（CI：`hygiene.yml`）

| # | 规则 | 失败信息模板 |
| --- | --- | --- |
| H-01 | 仓库根只允许白名单条目（上表 §4 的 15 项 + 上游 CKAN 目录） | `根目录出现未登记条目: {path}` |
| H-02 | 无 `bin/`、`obj/`、`.vs/`、`_build/` 被跟踪 | `构建产物被提交: {path}` |
| H-03 | 禁止跟踪 `*.log/*.dmp/*.zip/*.tmp/*.sfs(>256KB)/*.dll` | `禁止入库的文件类型: {path}` |
| H-04 | `docs/**` 命名与编号唯一 + 必须有 `## §1` 与 `## 修订记录` | `文档不符合模板: {path}` |
| H-05 | `docs/README.md` 索引覆盖所有 `docs/**/*.md` | `文档未登记索引: {path}` |
| H-06 | `tests/fixtures` 目录 sha256 前后一致（测试前后各算一次） | `夹具被测试改写: {path}` |
| H-07 | 单文件 >256 KB 且非 LFS、非生成物 → 红 | `超大文件: {path} ({size})` |
| H-08 | `adapters/**/adapter.json` 过 JSON Schema | `adapter 校验失败: {path}: {error}` |
| H-09 | `themes/**` 不含 `.axaml/.js/.dll`（[20 篇](20-appearance-personalization.md) §12） | `主题包含可执行内容: {path}` |
| H-10 | 源码里无个人绝对路径（正则 `[A-Z]:\\\\Users\\\\`、`Kerbal Space Program_newmod`） | `疑似本机路径: {path}:{line}` |
| H-11 | `--prune` 模式：清理 7 天前的 `artifacts/`、14 天前的 `spikes/`、30 天前的 `docs/_drafts/` | `已清理: {n} 项` |
| H-12 | 文档内相对链接可解析（自行实现，勿用 shell 拼接） | `断链: {file} → {target}` |

> H-10 与 H-12 是吃过亏的：早期检查脚本用 `tr -d ']('` 拼相对路径，残留 `)` 导致全量假阳性，最后弃用——**门禁本身也要可测**（`docs/research/` 里留一份"故意违规样本"用于自测）。

---

## §13 本工作区（规格仓）的即刻规则

1. **只允许**在 `doc/`、`doc/uir/`、`_scratch/` 三处写入；`Core/`、`GUI/` 等上游目录只读（Core 补丁须走 [04 篇](04-mvvm-and-models.md) §5 的 RFC 流程）；
2. `_scratch/` 已加入 `.git/info/exclude`（本地忽略，不动上游 `.gitignore`）；
3. 一次性脚本写完即删，最长活得比它改的文件少一天；
4. 本次已执行清理（可回溯）：删除 `_probe/`（含 `t.dll`/`t.exe`/`app/bin`/`app/obj`）、删除空目录 `spike/CkanAvaloniaSpike`、把 `tools/SfsKit/` 迁到 `_scratch/SfsKit/`（原型保留、不构建、不入库）、删除空的 `tools/`；
5. 迁移到新仓库时，`doc/uir/*.md` → `docs/plan/` 原样平移，`doc/uir/i18n/*` → `docs/research/i18n-evidence/`（证据类），`doc/ui-refactor-feasibility.md` → `docs/plan/00a-feasibility.md`。

---

## §14 与其它篇的接口（谁引用本文）

| 篇 | 关系 |
| --- | --- |
| [01 篇](01-architecture.md) §3 | 项目清单与 TFM 的权威定义；本文补 §5 的门禁与命名统一（`CkanUI.*`） |
| [07 篇](07-testing.md) | 测试项目与夹具落位、`fixtures/` 体积门限、手工用例目录 |
| [09 篇](09-migration-packaging.md) | 发布时`docs/`、`adapters/`、`themes/`、`i18n/` 哪些随包、哪些留数据侧 |
| [13 篇](13-savegame-sfs.md) | `tests/fixtures/sfs/` 与 10 个真实存档往返用例的落点 |
| [15 篇](15-mod-config-adapters.md) | `adapters/<modId>/` 的目录即插件形态（本文只钉目录与 schema 门禁 H-08） |
| [20 篇](20-appearance-personalization.md) | `themes/<packId>/` 与 H-09（禁可执行内容） |
| [06 篇](06-wbs-milestones.md) | M0 的 ENV-01/04/05 由此获得可验收形状；M0 门禁加第 ⑥ 条 |

---

## §15 风险

| ID | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| R-81 | 门禁过严导致"先绕过再说"（H-04/H-07 最容易） | 中 | 中 | 分级：H-01..H-03/H-09/H-10 为**阻断**，H-04/H-05/H-07 为**警告 + PR 注释**，两周后收紧 |
| R-82 | 规格仓与代码仓漂移（改文档不改代码，或反之） | 高 | 中 | 迁移后 `docs/plan/` 与原 `doc/uir/` 的 sha256 记进 `docs/research/migration.md`；ADR 变更必须同 PR 改两面 |
| R-83 | `fixtures` 体积失控（真实存档不断被加进来） | 中 | 低 | H-07 拦 + 生成器替代 + 明确"真实数据只做一次性调研" |
| R-84 | 多人协作时临时区被误提交 | 中 | 低 | `_scratch/`、`spikes/`、`artifacts/` 三重 ignore + H-01/H-03 |

---

## §16 修订记录

| 日期 | 版本 | 变更 |
| --- | --- | --- |
| 2026-10-27 | v1.0 | 新建。依据用户指令「划清各个模块的边界和文件夹，保持工作区整洁，各种文档/debug/测试用例不要乱丢」：定三区制、权威目录树、模块边界矩阵（12 项目 × 允许/禁止）、文档/调试/夹具三套落位规则、命名规范、`.gitignore`/`.gitattributes` 增量、`verify-layout` 12 条门禁、本工作区即刻规则；实测盘点并清理 `_probe/`、`spike/`、`tools/SfsKit/`；统一项目名 `CkanUi.*` → `CkanUI.*`（20 处）。不新增工时，M0 门禁加第 ⑥ 条。 |
