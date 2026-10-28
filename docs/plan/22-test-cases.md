# 22 · 测试用例与单元测试规格

> 状态：v1.0（2026-10-26）｜依赖：[07 篇](07-testing.md)（策略与门禁）、[21 篇](21-repo-layout.md)（项目与目录）、[12 篇](12-feature-parity.md)（F-01..F-55）
> 定位：**07 篇说"测什么层面、什么门禁"，本篇说"具体哪条用例、输入是什么、判据是什么"**。本篇**不新增工时**——所有用例都落在既有 `TST-*` 任务里（见 §2.2 的工时归属表）。

## §1 目标与非目标

**目标**

- T-01 每条自动化用例都有唯一 ID、明确输入、可自动判定的期望（禁止"看起来正常"这类判据）；
- T-02 用例 ID 可被 WBS 任务、F-ID、T-* 验收项三向追溯（§19）；
- T-03 写明**替身怎么造**（沙盒实例、假端点、假时钟、假 Dispatcher），保证 CI 上可重复；
- T-04 写明**什么时候写**（TDD 优先于实现，每任务 DoD 的一部分）；
- T-05 覆盖三类最贵的风险：数据破坏（SFS/cfg 写坏）、线程死锁（`IUser` 同步回调）、隐私外泄（诊断包/AI 外发）。

**非目标**

- X-01 不重复 07 篇的验收矩阵（F-ID 与 T-* 的行都在 07 篇，本篇只给用例级展开）；
- X-02 不做 UI 像素级视觉回归基线（只在 §15 做 headless 冒烟；像素比对另立 ADR）；
- X-03 不追求行覆盖率数字本身（只对四类模块设硬阈值，见 §17）；
- X-04 不测 Core（CKAN 上游）自身逻辑——它有自己的 `Tests/`（566 个 `[Test]`），本项目只测**我们的适配层**；
- X-05 不引入新测试框架（NUnit 4 + NSubstitute + 内建 `Assert.That`，与上游 Core 一致，降认知成本）。

## §2 用例分层与编号

### 2.1 五层与编号前缀

| 层 | 前缀 | 运行载体 | 数量 | 速度预算 | 失败即 |
| --- | --- | --- | --- | --- | --- |
| 架构约束 | `UT-ARC-nn` | `CkanUI.Architecture.Tests`（Roslyn 扫引用与命名空间） | 6 | <2 s | 阻塞合并 |
| 单元（纯函数/领域/纯库） | `UT-CORE-nn`、`UT-SFS-nn`、`PT-nn`、`UT-L10N-nn`、`UT-MTXT-nn` | `ViewModels.Tests`、`Sfs.Tests`、`Localization.Tests`、`ModText.Tests` | 54 | <10 s | 阻塞合并 |
| 单元（VM/服务，含替身） | `UT-VM-nn`、`UT-SVC-nn` | 同上 + `TestSupport` | 58 | <15 s | 阻塞合并 |
| 集成（沙盒实例/真 IO） | `IT-nn` | `CkanUI.Services.Tests`（Temp 目录 + 夹具实例） | 24 | <3 min | 阻塞合并 |
| 对拍 / 冒烟 | `ORC-nn`、`SNAP-nn` | 脚本 + headless 渲染 | 18 | <2 min | 阻塞合并（`ORC`）/ 阻塞里程碑（`SNAP`） |
| 手工 | `H-01..H-14` | 人 | 14 组 | — | 阻塞里程碑（定义在 07 篇） |

属性测试用 `PT-nn`（Fuzz/Property，跑在 `CkanUI.Sfs.Tests` 里，固定 seed）。

**合计 160 条**：`UT-ARC` 6、`UT-CORE` 16、`UT-VM` 36、`UT-SVC` 22、`UT-SFS` 16、`PT` 4、`UT-L10N` 10、`UT-MTXT` 8、`IT` 24、`ORC` 10、`SNAP` 8；另有 14 组手工（`H-*`，定义在 07 篇）。

### 2.2 工时归属（保证总工时 2196h 不变）

| 用例族 | 落在哪个既有任务 | 已有工时 |
| --- | --- | --- |
| `UT-ARC` / `UT-CORE` / `UT-VM` / `UT-SVC` | `TST-01` 12h（基础设施）+ `TST-02` 20h | 32h |
| `IT-nn` / `ORC-nn` | `TST-03` 20h + `TST-04` 10h | 30h |
| `UT-SFS` / `PT-nn` | `TST-SAVE` 16h | 16h |
| `UT-SVC` 日志与脱敏段 + `T-*LOG` | `TST-LOG` 12h | 12h |
| `UT-CORE` adapter 段 + `T-*CFG` | `TST-CFG` 14h | 14h |
| `UT-L10N` | `TST-I18N` 8h | 8h |
| `UT-MTXT` | `TST-MTI` 12h | 12h |
| 资源释放段 | `TST-RLS` 12h | 12h |
| 安装段 | `TST-INS` 14h | 14h |
| 外观段 | `TST-THM` 16h | 16h |
| `SNAP` / `TST-05` 矩阵勾选 | `TST-05` 12h | 12h |

> 结论：本篇**每新增一条用例，都必须能指到上表某一行**；指不到就说明它属于新工作量，要改 WBS 而不是塞进测试文档。

## §3 测试项目与目录

| 项目 | 被测对象 | 不测什么 |
| --- | --- | --- |
| `tests/CkanUI.Architecture.Tests` | 程序集引用图、命名空间白名单、`*ViewModel` 不得 `using Avalonia` | 业务逻辑 |
| `tests/CkanUI.ViewModels.Tests` | 28 个 VM、命令、校验、状态机 | 真实文件 IO |
| `tests/CkanUI.Services.Tests` | 服务层 + Core 适配（沙盒实例、真 IO、Temp 目录） | Avalonia 渲染 |
| `tests/CkanUI.Sfs.Tests` | 解析/序列化/格式化/坏文件/属性测试 | Core |
| `tests/CkanUI.Localization.Tests` | 三层解析、占位符校验、`legacy-map` 构建期校验 | Core 资源内容正确性 |
| `tests/CkanUI.ModText.Tests` | `fnv1a64` + `normalize` 跨语言契约、查找链 | 译文质量 |
| `tests/CkanUI.TestSupport`（共享库，不计入测试项目数） | 替身与构造器（§5） | — |

> 与 [21 篇](21-repo-layout.md) §4 的"tests/ 4 个项目"口径差 2：本篇把 `Localization` 与 `ModText` 的用例独立成项目（两者是零依赖纯库，独立后可用 `dotnet test` 单跑，也避免 VM 测试项目被迫引用 resx 快照）。已同步回填 21 篇。

## §4 编写约定

1. **一测一个行为**：一个 `[Test]` 只断言一类事实；多场景用 `[TestCase]` 参数化，不用 for 循环里 `Assert`。
2. **命名** `方法_场景_期望`：`Install_WhenUserCancels_LeavesRegistryUnchanged`；中文场景名允许写在 `[Description]`，不进方法名（编码/工具链兼容）。
3. **AAA 三段**，空行分隔；`// Arrange` 注释可省，但**禁止把断言写在 Arrange 里**。
4. **禁止 `Thread.Sleep` / `Task.Delay`**：等待用 `FakeClock.Advance()` 或 `await Waiter.UntilAsync(cond, timeout)`（后者内部用 `SpinWait` + 3 s 上限，超时即失败并打印状态）。唯一例外：性能用例（`T-LOG-02` 等）允许计时，但必须显式标注 `[Category("Perf")]`，默认 CI 不跑。
5. **确定性**：任何涉及时间、随机、环境变量、当前目录的代码必须经注入点（`IClock`、`IRandom`、`IEnvironment`）；测试里出现 `DateTime.Now` 直接判红（Roslyn 分析器 `CKANUI0001`）。
6. **并行**：`[Parallelizable(ParallelScope.All)]` 默认开启；凡写真实文件系统的集成用例用 `SandboxInstance`（每个用例独立 Temp 目录），不得共享路径。
7. **夹具只读**：`tests/fixtures/**` 只读断言（21 篇 §8 H-06）。
8. **断言消息**：自定义 `Assert.That(actual, Is.EqualTo(expected), () => Dump(ctx))`，失败必须能看出输入。
9. **不 mock 静态**：Core 的 `ServiceLocator`/`Logging` 通过 `TestSupport` 的 `GlobalSetup` 夹具初始化一次，不在用例里 mock。

## §5 替身与测试数据

| 替身/构造器 | 位置 | 作用 | 关键行为 |
| --- | --- | --- | --- |
| `FakeUser` | `CkanUI.TestSupport` | 实现 `IUser`（`Core/User.cs:10`） | 记录全部 `RaiseMessage/RaiseError`（可断言序列）；`RaiseYesNoDialog` 可预设返回队列；`RaiseSelectionDialog` 可配置延迟（用于死锁用例） |
| `FakeDispatcher` | 同上 | `IDispatcher` 的同步执行版 + 可切"记录模式" | 记录封送次数与线程 ID，用于 `UT-SVC-04` |
| `FakeClock` | 同上 | `IClock` | `Advance(TimeSpan)` 推进；节流/超时用例靠它，不用 sleep |
| `FakeAiEndpoint` | 同上 | `HttpMessageHandler` 桩 | 返回 200 合规 JSON / 500 / 401 / 超时 / 流式分片 / 不合规输出；记录请求体与次数（`UT-SVC-14` 断言"缓存命中零请求"） |
| `SandboxInstance` | 同上 | 造一个假 KSP 实例目录 | 生成 `GameData/`、`CKAN/registry.json`、`CkanUI.json`、最小 `KSP.x86_64` 占位；`Dispose()` 删目录 |
| `RepoSnapshotBuilder` | 同上 | 造 `available_modules` 结构 | 可按参数生成 N 个 mod × M 个版本，含长摘要、中文摘要、重复摘要、`localizations` |
| `SfsSampleFactory` | `CkanUI.Sfs.Tests` | 造 SFS 文本 | 从 3 行到 10 万行的分层样本 + 9 类坏文件（07 篇 §9.2） |
| `RegistryAssert` | `CkanUI.TestSupport` | 注册表断言助手 | `AssertUnchanged(snapshot)`：逐字段 diff，失败打印差异路径 |

## §6 架构测试（`UT-ARC`）

| ID | 断言 | 实现要点 |
| --- | --- | --- |
| UT-ARC-01 | `Avalonia*` 命名空间只出现在 `App`、`Controls`、`Platform`、`Services/Threading` | 反射读 `Assembly.GetReferencedAssemblies()` + Roslyn 扫 `using`/全限定名 |
| UT-ARC-02 | `CKAN`（Core）命名空间只出现在 `Services`、`Localization`、`Sfs.Cli` 之外禁止 | 同上；白名单按项目而非按文件，避免逐个豁免 |
| UT-ARC-03 | `CkanUI.ViewModels` 的引用列表不含 `CkanUI.Services`、`Avalonia.*`、`CKAN` | 只查程序集引用即足够（VM 一旦 `using CKAN` 必然要引程序集） |
| UT-ARC-04 | `Core`、`ModText`、`Sfs`、`Services.Abstractions` 的引用列表为空（测试框架除外） | 四个项目必须保持零依赖 |
| UT-ARC-05 | 所有 `*ViewModel` 类型不含 `using Avalonia`、不出现 `Dispatcher.UIThread`、不出现 `File.`/`Directory.` | 源码级扫描 + 排除 `#if DEBUG` 段 |
| UT-ARC-06 | 所有面向用户的字符串不出现字面量（走 `ILocalizer`），例外：日志与异常消息白名单 | 扫 `CkanUI.App` 的 `.axaml` 与 `CkanUI.ViewModels` 的 `string` 字面量；白名单集中在一个 `allowed-literals.txt` |

## §7 纯函数与领域模型（`UT-CORE`）

| ID | 被测对象 | 输入/构造 | 期望 |
| --- | --- | --- | --- |
| UT-CORE-01 | `GUIMod` 投影 | 3 个 `CkanModule`（已装/未装/可升级） | `IsInstalled`/`HasUpdate`/`IsAutoInstalled` 与注册表一致 |
| UT-CORE-02 | `GUIMod.Searchable*` 归一化 | 含全角、重音、`Mod`/`mod`、前缀 `CKAN-` | 归一化后小写、去重音、去前缀；与老 GUI `GUI/Model/GUIMod.cs:256-289` 同结果（对拍 30 条） |
| UT-CORE-03 | 过滤器 11 种 | 一份含 20 个 mod 的内存列表 × 11 个 `GUIModFilter` | 每种过滤器命中的 identifier 集合与手工预期表逐项相等 |
| UT-CORE-04 | 组合过滤 | 过滤器 + 4 个布尔 + 搜索词（18 个字段） | 结果为各条件交集；与 12 篇 F 表 §C 的定义一致 |
| UT-CORE-05 | 排序 | 15 列 × 升/降 | 稳定排序：相同值保持原始相对顺序；`InstallDate` 空值固定排尾 |
| UT-CORE-06 | 变更集合并 | 安装 + 升级 + 移除 + 自动装依赖 | 结果去重、顺序规则（先卸后装）、冲突（同 mod 既装又卸）报错 |
| UT-CORE-07 | 依赖选择排序 | 三棵合成依赖树 | 拓扑序稳定；循环依赖不栈溢出而是返回错误 |
| UT-CORE-08 | 版本比较适配 | `v1.2` / `1.2.0` / `1.2.0-beta` | 与 Core `ModuleVersion` 结果一致（23 组对拍） |
| UT-CORE-09 | 徽标三态 | 译文存在/缺失/过期 | `Translated`/`Original`/`Stale` |
| UT-CORE-10 | 外观 token 解析 | 主题包 JSON（合法/未知键/错类型/`<` 注入） | 合法项生效、非法项忽略并记原因，不抛 |
| UT-CORE-11 | 对比度计算 | 500 组随机前景/背景（固定 seed） | 与手算参考实现误差 ≤0.01；`4.5:1` 判定在边界值正确 |
| UT-CORE-12 | 诊断包脱敏规则 | 20 个样本（同 T-LOG-01） | 逐条占位符正确、无残留（纯函数层先测，服务层再测出口扫描） |
| UT-CORE-13 | `normalize`（文本） | NFKC/CRLF/NBSP/U+3000/前后空白 | 与 `i18n_tools` 的 17 条单测样本逐一相等 |
| UT-CORE-14 | adapter schema 校验 | 6 份合法 + 8 份恶意（类型错、范围反、`unlisted` 缺失） | 合法全通过；恶意逐条报出字段路径 |
| UT-CORE-15 | 资源让路分层计划 | 三档 × 6 种启动结果（正常/秒退/无窗口/Steam 壳/双开/CWD 变更） | 每档产生既定的层集合；中止条件正确 |
| UT-CORE-16 | 安装布局解析 | 便携/Setup 两种路径 | 程序目录、数据目录、日志目录各归各位 |

## §8 ViewModel（`UT-VM`）

> 全部 headless：`new XxxViewModel(deps)`，不建窗口；命令用 `await cmd.ExecuteAsync(null)`；状态断言只看公开属性与 `FakeUser` 记录。

| ID | VM / 场景 | 输入 | 期望 |
| --- | --- | --- | --- |
| UT-VM-01 | `ManageModsViewModel` 首次加载 | 沙盒实例 + 50 mod 仓库快照 | 列表 50 行、`IsBusy` 由 true→false、无异常 |
| UT-VM-02 | 同/UT-VM-02 勾选算变更集 | 勾 3 装 1 卸 2 升级 | 变更集条数与动作正确；`ApplyCommand.CanExecute` 变 true |
| UT-VM-03 | 搜索 18 字段 | 每种字段一条命中样本 | 命中数 ≥1 且结果含目标 identifier |
| UT-VM-04 | 过滤器切换 11 种 | 11 次切换 | 每次行数等于 UT-CORE-03 的期望集合大小 |
| UT-VM-05 | 搜索防抖 | 连续输入 8 次（`FakeClock` 推进 100 ms） | 只触发 1 次查询（计数断言） |
| UT-VM-06 | 空仓库 | 空 `available_modules` | 显示空态文案 key，不抛，`ApplyCommand` 禁用 |
| UT-VM-07 | 详情面板 | 选中 1 个 mod | 元数据/依赖/版本 4 个分区填充；依赖树展开子节点懒加载（`FakeClock` 验证只解析一次） |
| UT-VM-08 | 安装流程（成功） | 3 个 mod，进度事件 0/50/100 | 进度环 0→100；`FakeUser` 无 error；变更集清空 |
| UT-VM-09 | 取消安装 | 进度到 50 时 `Cancel()` | 状态回滚为未装；`registry.json` 未变（配合 IT-05） |
| UT-VM-10 | 安装失败（下载 404） | 假下载器抛 `DownloadFailedKraken` | 横幅显示错误、可重试、不崩溃 |
| UT-VM-11 | 半装保护 | Core 抛一半后中断 | UI 进入"需修复"状态并给"回滚"入口 |
| UT-VM-12 | 提供了/推荐 mod 对话框 VM | 3 个 provided、2 个 recommended | 默认全选、取消不改变更集 |
| UT-VM-13 | 兼容版本 VM | 3 个 KSP 版本 | 勾选后 `Config` 写回、刷新触发一次 |
| UT-VM-14 | 实例 VM：切换 | 两个沙盒实例 | 切换后列表来源变、`CurrentInstance` 更新、旧实例锁释放 |
| UT-VM-15 | 实例 VM：克隆 | 沙盒实例 | 克隆目录结构完整；源实例不受影响 |
| UT-VM-16 | 实例 VM：删除 | 沙盒实例 + 用户确认/拒绝 | 确认才删；拒绝零变化；删除时提示"数据目录保留" |
| UT-VM-17 | 仓库 VM：增/删/上移/下移 | 3 个仓库源 | `sorted_repositories` 顺序与 UI 一致；删除后缓存失效一次 |
| UT-VM-18 | 仓库 VM：URL 校验 | 8 个非法 URL | 逐条报错、不写入配置 |
| UT-VM-19 | AuthToken VM | 2 个 host token | 保存/清除；明文只在校验瞬时内存出现（不进日志） |
| UT-VM-20 | 设置 VM：47 项往返 | 逐项改值 → 保存 → 重新构造 | 值全等；未知键不被写回 Core（`config.json` 只含 10 键） |
| UT-VM-21 | 设置 VM：语言切换 | 13 项语言 | `LanguageChanged` 触发一次、20 个视图文本刷新（`UT-L10N-06` 在 UI 层覆盖） |
| UT-VM-22 | 插件 VM | 2 个假插件 dll（TestSupport 编的） | 列出名称/版本；`Initialize` 被调一次；失败插件标红不影响其它 |
| UT-VM-23 | 日志 VM：过滤 + 搜索 | 1 万条合成事件 | 三段过滤结果正确；`族:net 级别:error` 查询命中数等于预期；聚合 ×N 折叠生效 |
| UT-VM-24 | 日志 VM：环形缓冲 | 灌 20 万条（容量 5 万） | 保留最新 5 万，`Seq` 连续不回绕错误；内存不线性增长（抽样断言） |
| UT-VM-25 | AI 分析 VM：未配置密钥 | 无密钥 | 发送按钮禁用 + 提示配置路径；不发请求（计数 0） |
| UT-VM-26 | AI 分析 VM：预览与确认 | 构造错误事件 | 预览包非空且脱敏；点"发送"前零请求，点后 1 次 |
| UT-VM-27 | AI 分析 VM：流式与取消 | 假端点分 5 片返回 | 文本增量追加；取消后停止追加、不抛 |
| UT-VM-28 | 存档 VM：列表 | 3 个 `.sfs` + `Backup/` | 显示大小/时间/版本；备份分组折叠 |
| UT-VM-29 | 存档 VM：三视图切换 | 选定存档 | 树/文本/差异三视图数据一致；切换不重新解析（`FakeClock` 断言解析次数=1） |
| UT-VM-30 | 格式化预览 VM | 校验失败样本 | 显示 diff 行数与首个差异位置；"应用"禁用 |
| UT-VM-31 | 配置 VM：五态横幅 | 5 种状态各一例 | 横幅文案 key、编辑能力、`requiresRestart` 提示正确 |
| UT-VM-32 | 外观 VM：非法强调色 | 20 组低对比色 | 拒绝并给 3 个建议色；再次应用合法色成功 |
| UT-VM-33 | 外观 VM：按实例覆盖 | 两实例不同背景 | 切换实例后 token 跟随；导出/导入保持 `byInstance` |
| UT-VM-34 | 统计 VM | 假下载量数据 | 饼/柱数据与源码一致；空数据不除零 |
| UT-VM-35 | 更新 VM（自建替换器） | 假更新包 + 假进程 | 检测到新版本 → 下载 → 校验 sha256 → 提示重启；校验失败删除临时文件 |
| UT-VM-36 | 托盘 VM | 刷新/暂停/开 CKAN/开游戏/退出 | 5 个命令各自调用一次对应服务；关闭窗口不退出（按默认档） |
| UT-VM-37 | 未管理文件页：接管为受管 | 无选中 / 取消确认 / 全部接管 / 部分拒绝 / 服务失败 | 无选中与取消都不调服务；成功重扫并报「已接管 n 个、跳过 m 个、拒绝 k 个」；拒绝项原因聚合进错误条；可重试失败带「（可重试）」 |
| UT-VM-38 | 插件工具项 | 3 个假插件（Active/Dormant/Failed）+ 未知页键 | 只收 Active、按页分发、同「页面 + 键」去重留排序在前者；按钮回调带上声明者与键、失败文案进 ToolTip、连点只执行一次 |

## §9 服务层（`UT-SVC`）

| ID | 被测对象 | 场景 | 期望 |
| --- | --- | --- | --- |
| UT-SVC-01 | `User` 适配器 | Core 在后台线程调 `RaiseMessage/RaiseError` | 全部经 `IDispatcher` 封送；UI 线程 ID 在记录中一致 |
| UT-SVC-02 | `RaiseYesNoDialog` 同步语义 | 后台线程调用，`FakeDispatcher` 正常 | 返回用户答案、无死锁（3 s 内完成） |
| UT-SVC-03 | `RaiseSelectionDialog` 同步语义 | 同上，UI 侧延迟 200 ms 再答 | 仍返回正确答案、不超时 |
| UT-SVC-04 | 封送次数 | 1000 条消息 | 批处理/节流后封送次数 ≤ 200（策略见 05 篇 §12） |
| UT-SVC-05 | `CkanTransaction` 包装 | 安装中途抛异常 | 事务回滚，目录内容恢复到操作前快照（逐文件 sha256） |
| UT-SVC-06 | 注册表访问 | 两处并发读 | 不产生 `registry.locked` 竞争错误；写操作串行化（信号量断言） |
| UT-SVC-07 | 锁文件检测 | 存在 `registry.locked` | 启动即提示并给"删除/回滚"选项（对应老 GUI `RegistryInUseKraken`） |
| UT-SVC-08 | 配置读写 | 读写 `config.json` | 只改 10 键内字段；未知顶层键行为与 `ckan.exe` 一致（记为已知限制） |
| UT-SVC-09 | 缓存记账 | 下载 3 个文件 | 缓存目录与 Core 记账一致；手动删除后下次重建 |
| UT-SVC-10 | 日志采集 | log4net 从 4 个线程各写 1000 条 | 环形缓冲无丢失（`Seq` 连续）、无锁争用超时 |
| UT-SVC-11 | 异常钩子 | 人为抛未观察异常 | 被捕获、记 `Fatal`/`Error`、应用不退出（除致命） |
| UT-SVC-12 | 诊断包生成 | 完整上下文 | 7 个文件齐全、`config.redacted.json` 无敏感值、默认不含 `.sfs` |
| UT-SVC-13 | 出口反向扫描 | 故意注入 5 个泄漏样本 | 打包前拦截并报错（硬门禁），不生成包 |
| UT-SVC-14 | AI 指纹缓存 | 同一错误分析两次 | 第二次 0 次 HTTP（`FakeAiEndpoint` 计数） |
| UT-SVC-15 | AI 预算 | 预算调低到 1k token | 拒绝发送并说明原因，不静默截断 |
| UT-SVC-16 | AI 输出校验 | 4 种不合规返回 | 重试 1 次后降级纯文本并标注 |
| UT-SVC-17 | 备份策略 | 写 `.sfs` 前 | 生成快照、保留 10 份、最早被清理；快照名含时间戳 |
| UT-SVC-18 | 原子写 | 写盘过程抛异常 | 原文件字节不变；临时文件被清理 |
| UT-SVC-19 | 单实例/IPC | 启动第二个进程 | 转发参数到首实例并退出；不重复初始化 Core |
| UT-SVC-20 | 资源让路执行器 | 三档 × 成功/异常/超时 | 每层结果被记录；异常不阻断后续层（按策略） |
| UT-SVC-21 | 恢复路径 | 释放后返回列表 | 重建 ≤3 s、无空白帧（先出骨架行） |
| UT-SVC-22 | KSP 进程监视 | 假进程 6 种形态 | 时机判定正确（`UT-CORE-15` 的注入版） |
| UT-SVC-23 | 未管理文件接管（INT-09） | DLL / 目录 / 非 DLL / 已受管 / 批内重名 / 标识符被占 / 非法路径 / dryRun | `UnmanagedAdopter.Plan` 逐项结局正确且纯函数；`installed_dlls` 合并保留旧键（`SetDlls` 是整体替换）；写入失败可重试且不落盘 |
| UT-SVC-24 | 插件工具项回调（F-17） | 已声明 / 未声明 / 未激活 / 未加载 / 插件抛异常 / 未实现回调 | 前四种在校验阶段被拒（不触碰插件）；异常转成可重试失败且异常文本进 `Detail`；未实现回调时提示「未实现工具项执行」 |

## §10 SFS（`UT-SFS` + `PT`）

| ID | 场景 | 输入 | 期望 |
| --- | --- | --- | --- |
| UT-SFS-01 | 最小文档 | `GAME { }` 三种换行 | 解析成功、行索引正确 |
| UT-SFS-02 | 110,682 键的真实存档 | 裁剪版夹具（≤256 KB） | 键数量、层级、`OpaqueLine` 均与源一致 |
| UT-SFS-03 | 往返 | 10 个夹具 | `Serialize(Parse(b)) == b`（sha256 相等） |
| UT-SFS-04 | 幂等 | 同上 + 畸形样本 | `Format(Format(x)) == Format(x)` |
| UT-SFS-05 | 值不变量 | 同上 | `(path,key)→value` 多重集相等；`GAME.UT`/`Seed`/`_modVersions` 字面相同 |
| UT-SFS-06 | 重复键 | 同节点两个同名键 | 不合并、顺序保持、各自可寻址 |
| UT-SFS-07 | 空值 | `key = ` 7333 处形态 | 保留"等号后 1 空格"，不补默认值 |
| UT-SFS-08 | 值内特殊字符 | 反斜杠/中文/`=`/`|` | 不做转义、不改写 |
| UT-SFS-09 | 缩进异常 | Tab/空格/4 空格混合 | 只读模式可解析；`format --indent tab` 可规范化 |
| UT-SFS-10 | CRLF 保持 | 全 CRLF 文件 | 默认写回 CRLF；`--eol lf` 生效 |
| UT-SFS-11 | 9 类坏文件 | 07 篇 §9.2 逐条 | 逐条给出错误码 + 行号，不崩溃、不改原文件 |
| UT-SFS-12 | 超大文件 | 合成 100 MB | 解析内存 ≤3× 文件、时间 <10 s（`[Category("Perf")]`） |
| UT-SFS-13 | 中文路径/文件名 | `默认/快速存档.sfs` | 正确读写（`Sfs.Cli` 覆盖） |
| UT-SFS-14 | CLI 退出码 | 成功/校验失败/坏文件/文件不存在 | 0/2/3/4 各自映射，stderr 有可读消息 |
| UT-SFS-15 | `--check` 无副作用 | 只读模式 | 文件 mtime 与 sha256 不变 |
| UT-SFS-16 | `--diff` 输出 | 两版本 | 统一 diff 格式、行号正确 |
| PT-01 | 属性：随机节点树往返 | 1000 个随机文档（seed 固定） | 往返字节相等 |
| PT-02 | 属性：随机格式化幂等 | 同上 | 幂等成立 |
| PT-03 | 属性：随机插入/删除键 | 1000 组 | 值多重集差异恰好等于预期改动 |
| PT-04 | Fuzz：随机字节 | 10,000 个随机串 | 不抛未捕获异常、不 OOM（<2 s/样本） |

## §11 本地化（`UT-L10N`）

| ID | 场景 | 期望 |
| --- | --- | --- |
| UT-L10N-01 | 三层解析顺序（L1 新表 → L2 官方快照 → L3 `Core.` 前缀 → 缺） | 顺序正确；缺键返回 key 并计数 +1 |
| UT-L10N-02 | culture 链 `zh-CN → zh → 缺` | 不跨语言回退（不出现"日文填中文"） |
| UT-L10N-03 | 占位符校验 | `{0}/{1}` 数量与顺序不一致即失败（构建期 + 运行期各一次） |
| UT-L10N-04 | `legacy-map.json` 完整性 | 每条官方键在快照里存在；一条新键最多映射一个官方键 |
| UT-L10N-05 | 语言列表来源 | 只列**实际有资源**的语言；`es-ES` 存在、`en-GB` 作 en-US 别名 |
| UT-L10N-06 | 热切换 | 20 个视图文本全刷新；历史日志条目文本不变 |
| UT-L10N-07 | 数字区域化 | 德语 `2,5` 解析为 2.5；存盘为 Invariant `2.5` |
| UT-L10N-08 | 缺译探针 | 删一条 zh 键 → 显示 key、不崩溃、计数 +1 |
| UT-L10N-09 | 伪本地化 | 全部字符串 +45% 长度时零截断（供 `SNAP` 用） |
| UT-L10N-10 | 与 `ckan.exe` 互读 `Language` | 双向一致；`es-ES` 的降级行为符合 16 篇记录 |

## §12 元数据译文（`UT-MTXT`）

| ID | 场景 | 期望 |
| --- | --- | --- |
| UT-MTXT-01 | 跨语言哈希契约 | C# `Fnv1a.Hash(normalize(s))` 与 Python 侧逐条相等（100 条真实摘要 + 3 条官方向量） |
| UT-MTXT-02 | `normalize` 细节 | NFKC、CRLF→LF、NBSP/U+3000 折叠、trim；**不做**大小写折叠 |
| UT-MTXT-03 | 查找链 | L1 覆盖 → L2 主包 → L4 英文原文；过期回退路径各一条 |
| UT-MTXT-04 | 增量比对 | 旧/新/删三份 `RepoSnapshotBuilder` 快照，输出新增/变更/删除条数 |
| UT-MTXT-05 | 行格式 | `mod:"CKANMeta"`、`ctx=identifier`、`n:"ckan:<id>:<field>"` 与工具链解析一致 |
| UT-MTXT-06 | 覆盖率分母保护 | 绝不写 `packs/pool.jsonl`（断言该文件哈希在测试前后不变） |
| UT-MTXT-07 | 搜索对拍 | 中文词命中；**英文词结果集与老 GUI 零差异**（30 组） |
| UT-MTXT-08 | 徽标三态 | 三种状态的 ViewModel 属性与 `UT-CORE-09` 一致 |

## §13 集成（`IT`）

> 全部在 `SandboxInstance` 上跑：临时目录 + 真 `registry.json` + 假下载器（本地 HTTP 服务，`Kestrel` 起在 127.0.0.1 随机端口）或预置缓存文件。**禁止访问外网**（CI 里 `--no-network` 开关直接拦 `HttpClient`）。

| ID | 场景 | 构造 | 期望 |
| --- | --- | --- | --- |
| IT-01 | 装 1 个无依赖 mod | 沙盒 + 本地假仓库 | `registry.json` 增 1 条；文件落盘；`installed_files` 记账完整 |
| IT-02 | 装 1 个带 5 层依赖的 mod | 依赖链 a→b→c→d→e | 全部装上；顺序按拓扑；失败时整体回滚 |
| IT-03 | 卸载含被依赖项 | x 被 y 依赖 | 提示并给"连 y 一起卸"选项；拒绝时零变化 |
| IT-04 | 升级 | v1 → v2（含文件改名） | 旧文件删除、新文件就位；`installed_files` 无残留 |
| IT-05 | 取消（下载中断） | 假下载器在第 2 个文件断流 | 注册表未变或完整回滚；Temp 清理；无半装 |
| IT-06 | 磁盘满 / 只读目录 | 只读 `GameData` | 明确错误（含路径）+ 回滚；不留半截文件 |
| IT-07 | 事务回滚（人为抛） | 在 `CkanTransaction` 内抛 `IOException` | 目录 sha256 恢复到操作前 |
| IT-08 | 与 CLI 同时操作同一实例 | `ckan.exe` 模拟进程持锁 | 我方提示"实例被占用"并按 `registry.locked` 策略回滚；不写坏 |
| IT-09 | 双开本程序 | 启动两次 | 第二个进程转发并退出；首实例不死锁 |
| IT-10 | 退出无残留 | 装完后退出 | `registry.locked` 已删；无 Temp 目录；日志句柄释放 |
| IT-11 | 切换实例 | 两沙盒实例来回切 3 次 | 锁正确释放/获取；列表来源正确；无泄漏（句柄数增长 ≤5） |
| IT-12 | 缓存重建 | 删除 `repos/*.json` 缓存 | 从本地假仓库重新拉取并重建；UI 有刷新进度 |
| IT-13 | 仓库不可达 | 假仓库 500 | 保留旧缓存并提示"使用缓存数据（时间）"；不崩溃 |
| IT-14 | 写 cfg（KSPCF） | 6 份 adapter fixtures | 只改目标行；其余逐字节不变（sha256 分段比对） |
| IT-15 | cfg 写盘 kill -9 | 写盘中途 kill 进程（子进程脚本） | 原文件完好或完整新文件；快照可回滚 |
| IT-16 | 写 sfs（格式化） | 3 个夹具副本 | 幂等/往返成立；快照生成；`loadmeta` 同步规则生效 |
| IT-17 | 大存档打开 | 4.8 MB 真实副本 | 打开 ≤2 s；内存峰值 ≤3×；不阻塞 UI（帧时间断言另见 `SNAP`） |
| IT-18 | 日志 10 万行 | 合成 | 滚动 p99 ≤16.7 ms（`[Category("Perf")]`） |
| IT-19 | 诊断包落地 | 完整上下文 | 7 文件、大小 ≤5 MB、脱敏扫描零命中 |
| IT-20 | AI 全链路（假端点） | 错误事件 → 预览 → 发送 → 结果 | 结果含 `evidenceSeq` 且能回链到日志行；请求体大小 ≤32k token 估算 |
| IT-21 | 资源让路 | 假游戏进程启动 → 主窗口出现 | 分层释放执行、读数下降达标、恢复 ≤3 s |
| IT-22 | 安装布局 | 便携/Setup 两形态各起一次 | 程序目录/数据目录/日志目录正确；注册表新增仅 HKCU 11 条（setup 形态） |
| IT-23 | 单实例 + 协议 | `ckan://` 拉起 | 参数转发、进入对应页面、不新建第二实例 |
| IT-24 | 与老 `ckan.exe` 共存 | 两边同时读同一实例 | 读到一致数据；任一方写时另一方给出占用提示；`config.json` 未被互相破坏 |

## §14 与 CLI 对拍（`ORC`）

> 老 `ckan.exe` 是**行为 oracle**：同输入下比较注册表与文件系统差异。脚本在 `tools/gates/oracle/`，CI 里跑（`[Category("Oracle")]`），本地可跳。

| ID | 输入 | 比较对象 | 通过判据 |
| --- | --- | --- | --- |
| ORC-01 | 装 1 个无依赖 mod | `registry.json` diff | 空 diff（除时间戳字段） |
| ORC-02 | 装 1 个带依赖 mod | 同上 + 文件树 | 空 diff |
| ORC-03 | 卸载 | 同上 | 空 diff |
| ORC-04 | 升级 | 同上 | 空 diff |
| ORC-05 | 中文路径实例 | 同上 | 空 diff |
| ORC-06 | 中文 mod 名 | 同上 | 空 diff |
| ORC-07 | 10 个 mod 批量 | 同上 | 空 diff |
| ORC-08 | 缓存清理后重装 | 同上 | 空 diff |
| ORC-09 | 搜索对拍（英文词 30 组） | 结果 identifier 集合 | 零差异（硬门禁） |
| ORC-10 | `ckan list` 输出 | 文本（排序后） | 行数与非 ASCII 字符一致 |

## §15 headless 冒烟（`SNAP`）

> `Avalonia.Headless` 渲染到内存位图，只做**结构性**断言（元素存在、层级、文本、尺寸），不做像素基线（X-02）。跑在 `CkanUI.ViewModels.Tests` 的 `[Category("Headless")]` 分组。

| ID | 场景 | 期望 |
| --- | --- | --- |
| SNAP-01 | 主窗口启动 | 3 个根区域（NavRail/内容/任务抽屉）存在；首帧 ≤1.5 s |
| SNAP-02 | 20 个视图逐个实例化 | 无异常、无未绑定 `{loc:T}`（文本非空且非 key 原样） |
| SNAP-03 | 深/浅/高对比度三变体 | 每个视图渲染成功；关键文本对比度 ≥4.5:1（读 `Foreground`/背景色计算，不量像素） |
| SNAP-04 | 密度 3 × 字号 5 | 列表行高与 token 一致；无溢出警告（`TextBlock` 不截断断言用 `DesiredSize`） |
| SNAP-05 | 伪本地化 +45% | 所有文本 `DesiredSize.Width ≤ 容器宽` |
| SNAP-06 | 日志页 5 万行 | 虚拟化生效（可视元素数 ≤60） |
| SNAP-07 | 存档三视图 | 切换 3 次渲染成功、无重新解析（计数） |
| SNAP-08 | 空态/错误态/加载态 | 三种骨架都存在且文案 key 正确 |

## §16 手工用例映射

手工组定义在 [07 篇](07-testing.md) §4、§8–§16，本篇只做映射与证据要求：

| 组 | 覆盖 | 执行时机 | 证据 |
| --- | --- | --- | --- |
| H-01 实例族 | 新建/克隆/重命名/删除/切换 | M1、M6 | 每步截图 + 目录树 |
| H-02 安装族 | 装/卸/升级/取消/半装修复 | M1、M3 | 注册表 diff + 截图 |
| H-03 仓库族 | 增删改序/镜像/离线 | M2 | 配置快照 |
| H-04 搜索族 | 18 字段 + 11 过滤器 | M2 | 结果截图 |
| H-05 更新与托盘 | 自建更新器、5 个托盘命令 | M5' | 录屏 |
| H-06 帮助与外观 | 语言、Discord/论坛、主题包导入 | M5' | 截图 |
| H-07 存档 | 三视图、格式化、备份、KSP 载入 | M7d | 存档可载入的**实机证据** |
| H-08 日志与 AI | 染色、过滤、诊断包、AI 结果回链 | M8 | 日志截图 + 请求体摘要 |
| H-09 配置 | 6 个 adapter、五态、注释即说明 | M9 | 改前/改后文件 diff |
| H-10 四语言 × 五屏 | zh/de/ru/ja | M10 | 20 张截图 |
| H-11 元数据译文 | 3 个 mod × 5 种操作 | M11 | 截图 + 搜索对拍表 |
| H-12 资源让路 | 真机启动 KSP、三档读数 | M12 | `metrics.jsonl` + 秒表 |
| H-13 安装矩阵 | 6 种机器形态 | 每次发版 | 每台截图 + 注册表导出 |
| H-14 外观 | 7 种系统形态 | 发版前 | 7 张截图 |

## §17 覆盖率与质量门禁

| 对象 | 指标 | 阈值 | 执行 |
| --- | --- | --- | --- |
| `CkanUI.Sfs` | 行 + 分支 | **≥95% / ≥90%**（数据破坏风险最高） | 每 PR |
| `CkanUI.Core` | 行 | ≥90% | 每 PR |
| `CkanUI.ViewModels` | 行 | ≥75%（命令与状态机为主） | 每 PR |
| `CkanUI.Services` | 行 | ≥60%（大量 IO 适配，靠集成覆盖） | 每 PR |
| 新增/变更代码 | diff 覆盖 | ≥80% | 每 PR |
| 变异测试（抽样） | `Sfs` + `Core` 关键类 | 变异体被杀 ≥70% | 每周一次（Stryker.NET，`[Category("Mutation")]`） |

**七条硬门禁**（任一红即不可合并）：①`UT-ARC-01..06` 全绿；②`ORC-01..10` 零差异；③`UT-SFS-03/04/05` + `PT-01..04` 全绿；④夹具目录哈希不变；⑤`UT-SVC-13`（脱敏出口扫描）零命中；⑥无 `Thread.Sleep`、无 `DateTime.Now`（分析器）；⑦覆盖率不低于上表阈值。

## §18 CI 矩阵与耗时预算

| 阶段 | 内容 | 平台 | 目标耗时 |
| --- | --- | --- | --- |
| build | `dotnet build` 0 警告 + 架构测试 | win/linux/mac | ≤3 min |
| unit | `UT-*`（除 Perf/Headless/Mutation） | 三平台 | ≤2 min |
| integration | `IT-*`（沙盒，`--no-network`） | win（主）+ linux（抽样 8 条） | ≤6 min |
| headless | `SNAP-*` | win + linux（skia 软渲染） | ≤3 min |
| oracle | `ORC-*` | win only（需旧 `ckan.exe`） | ≤5 min |
| perf | `[Category("Perf")]`（`IT-18`/`UT-SFS-12`） | win only，夜间 | ≤10 min |
| mutation | `[Category("Mutation")]` | 夜间每周 | ≤30 min |

**分层失败策略**：`build`/`unit`/`UT-ARC` 阻塞；`integration` 阻塞；`headless` 阻塞里程碑；`perf`/`mutation` 只出报告并开 issue，不阻塞合并（但连续两周不达标必须进风险登记）。

## §19 追溯矩阵

**三向追溯**（`verify-layout` H-05 检查本表与 07 篇、12 篇的一致性）：

| 方向 | 说明 | 落点 |
| --- | --- | --- |
| F-ID → 用例 | 55 条特性各至少有 1 条自动化或 1 条手工用例 | 07 篇 §8 矩阵 + 本篇 §8/§13 |
| T-* 验收项 → 用例 | 07 篇 §9–§16 的每条 T-* 都要有实现载体（`UT-*`/`IT-*`/`ORC-*`/`SNAP-*`） | 本篇对应小节 |
| WBS 任务 → 用例 | 每个 `TST-*` 任务的"产出"列必须写到用例 ID 段 | 06 篇 §1.x |

**抽样校验规则**（每月一次）：随机抽 10 个 F-ID，检查其用例是否存在、是否真跑；缺失即开缺陷。

**已知映射缺口（v1.0 显式登记）**：

| F-ID 段 | 覆盖方式 | 缺口说明 |
| --- | --- | --- |
| F-01..F-09（实例族） | `UT-VM-14..16`、`IT-11`、H-01 | 克隆大实例（>10 GB）无自动化用例，仅手工 |
| F-10..F-17（仓库族） | `UT-VM-17..19`、`IT-12..13` | 企业代理/自签证书场景无自动化 |
| F-18..F-34（列表与搜索） | `UT-CORE-02..07`、`UT-VM-01..07`、`ORC-09` | 空 |
| F-35..F-48（安装/缓存/文件） | `IT-01..10`、`ORC-01..08` | 磁盘满用只读目录模拟，真满盘无自动化 |
| F-49..F-55（外壳） | `UT-VM-35..36`、`IT-22..23`、`SNAP-01` | SmartScreen/签名验证只能手工 |

## §20 反模式清单（评审必查）

1. **断言"不抛异常"当通过**——必须断言具体状态变化；
2. **共享可变夹具**（同一个 `SandboxInstance` 给多个用例）——并行下互相污染；
3. **测试里 sleep**——用 `FakeClock`；
4. **大夹具进 git**（>256 KB）——用生成器或 LFS；
5. **改夹具让它通过**——夹具只读门禁直接拦；
6. **只测 happy path**——每个服务类至少 1 条失败路径（磁盘/网络/权限/取消）；
7. **`Assert.Throws` 后不查异常内容**——必须查消息关键字或 `Kraken` 类型；
8. **UI 断言依赖像素坐标**——用元素查找与 `DesiredSize`；
9. **把 Core 的逻辑在测试里重写一遍当 oracle**——用 `ORC` 对拍或手算常量表；
10. **跳过用例不留痕**——`[Ignore]` 必须带原因与关联 issue 号。

## §21 决策（2026-10-27 已拍板）

> **决策**：Q1 = **只在 M7/M3 前跑变异测试**（平时 week 级）；Q2 = **冻结 `ckan-1.36.5` 作 CI 制品**（`tests/oracle/ckan-1.36.5/`，不入 git）；**Q3 = headless 只在 Windows 跑**（用户选择，覆盖原建议：Linux CI 不跑 `SNAP-*`）；Q4 = **覆盖率按项目卡死**（不调低 `Sfs` 的 95%）。工时不变（0h，全部并入既有 `TST-*`）。

| 编号 | 问题 | 我的建议 |
| --- | --- | --- |
| Q1 | 变异测试是否值得每周跑 30 min？ | 只在 M7（SFS）与 M3（安装）两个里程碑前跑，平时 week 级 |
| Q2 | `ORC` 需要旧 `ckan.exe`，CI 里是否放一份冻结的 1.36.5 二进制？ | 放（`tests/oracle/ckan-1.36.5/`，不入 git，发布为 CI 制品），否则对拍无法在 CI 复现 |
| Q3 | headless 是否要在 Linux CI 也跑？ | 跑 `SNAP-01..05` 即可（软渲染差异大，像素不做基线） |
| Q4 | 覆盖率阈值是否按项目卡死（本篇 §17）？ | 是；`Sfs` 的 95% 是数据安全底线，不再往下调 |

## 修订记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v1.1 | 2026-10-27 | §21 四项决策定稿（变异测试频率、冻结对拍二进制、headless 仅 Windows、覆盖率按项目卡死）；用例集与门禁不变 |
| v1.0 | 2026-10-26 | 初版：五层用例体系（`UT-ARC` 6 / `UT-CORE` 16 / `UT-VM` 36 / `UT-SVC` 22 / `UT-SFS` 16 + `PT` 4 / `UT-L10N` 10 / `UT-MTXT` 8 / `IT` 24 / `ORC` 10 / `SNAP` 8）、替身清单、覆盖率与其他六条硬门禁、CI 矩阵、三向追溯与缺口登记、两条与 21 篇的口径修正（测试项目 6 个） |
