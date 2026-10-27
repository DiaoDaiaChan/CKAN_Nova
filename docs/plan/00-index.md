# CKAN UI 重构 · 计划总览（00 Index）

> 上层依据：[doc/ui-refactor-feasibility.md](../ui-refactor-feasibility.md)（可行性评估，11 节）
> 本文档集面向：执行者（开发者）与决策者（要不要投这个工）
> 状态：计划稿 **v2.6**（31 项待确认已全部拍板；E3 MemGraph 取证完成）（技术栈已定稿、范围为完全特性对齐 + E1 存档 + E2 可观测性 + E3 Mod 配置 + E4 国际化 + E5 元数据本地化 + E6 启动后资源释放 + E7 Windows 标准安装/卸载/注册 + E8 外观个性化 + 工程规范与工作区卫生 + 测试用例规格），未开工

## 1. 文档地图

| 编号 | 文档 | 内容 | 读者 |
|---|---|---|---|
| 00 | 本文 | 目标、范围、里程碑总表、决策记录、术语 | 全员 |
| 01 | [01-architecture.md](01-architecture.md) | 目标架构、项目布局、分层、依赖规则、生命周期 | 开发 |
| 02 | [02-core-integration.md](02-core-integration.md) | Core 集成契约逐条 + 可编译骨架代码 | 开发 |
| 03 | [03-ui-spec.md](03-ui-spec.md) | 信息架构、页面清单、布局、列定义、交互流程 | 开发/设计 |
| 04 | [04-mvvm-and-models.md](04-mvvm-and-models.md) | ViewModel 清单、Model 复用决策、变更集下沉 Core 的 RFC | 开发 |
| 05 | [05-threading.md](05-threading.md) | 线程模型、封送规则、取消、死锁规避 | 开发 |
| 06 | [06-wbs-milestones.md](06-wbs-milestones.md) | WBS 任务表（工时/依赖/验收）、里程碑门禁、排期 | 项目管理 |
| 07 | [07-testing.md](07-testing.md) | 测试金字塔、headless ViewModel 测试、CI 改动 | 开发 |
| 08 | [08-risk-register.md](08-risk-register.md) | 风险登记表、Top5 深挖、技术债登记 | 全员 |
| 09 | [09-migration-packaging.md](09-migration-packaging.md) | 迁移策略、共存冲突、打包、发布与回滚 | 发布 |
| 10 | [10-spike-plan.md](10-spike-plan.md) | 2–3 天 spike 的逐半天任务卡与验收 | 开发 |
| 11 | [11-tech-stack-decision.md](11-tech-stack-decision.md) | 框架与 OS 覆盖决策：Win10 1607→Win11、WinUI3 级流畅感的落地手段、降级链 | 决策者/开发 |
| 12 | [12-feature-parity.md](12-feature-parity.md) | **全特性对齐清单 F-01..F-55**（原版每条功能点的证据、映射任务、验收）、缺口分析与新增任务工时 | 全员 |
| 13 | [13-savegame-sfs.md](13-savegame-sfs.md) | **存档工作台与 SFS 格式化工具**（E1）：`.sfs` 格式实测规范、保字/幂等/自校验算法、存档包一致性、250h / M7 排期 | 开发 |
| 14 | [14-log-and-ai-debug.md](14-log-and-ai-debug.md) | **日志审查与 AI 辅助排障**（E2）：四路日志采集、9 族自动染色、脱敏诊断包、OpenAI 兼容端点请求、186h / M8 排期 | 全员 |
| 15 | [15-mod-config-adapters.md](15-mod-config-adapters.md) | **社区 Mod 配置适配层**（E3）：声明式 adapter（加目录即加支持）、9 种字段类型、注释即说明、骨架导出闭环、174h / M9 排期 | 开发/插件作者 |
| 16 | [16-i18n.md](16-i18n.md) | **国际化与官方文案复用**（E4）：先复用官方 943 条文案（L2 快照）+ 再补新 UI 文案（L1）、三层解析、热切换、CJK 字体回退、Crowdin 工作流，156h / M10 排期 | 全员/翻译贡献者 |
| 17 | [17-mod-metadata-i18n.md](17-mod-metadata-i18n.md) | **Mod 源信息（仓库元数据）i18n 与仓库同步**（E5）：`abstract`/`description` 译文包、`h=fnv1a64(normalize(s))` 内容寻址、随仓库增量更新、复用 `i18n_tools` 门禁与 `UI18n.Core` 运行时，134h / M11 排期 | 全员/翻译贡献者 |
| 18 | [18-launch-resource-release.md](18-launch-resource-release.md) | **游戏启动后的资源释放**（E6）：分层释放 L0–L4（视图/应用缓存/Core 数据/托管堆/工作集）、T0–T5 时机编排（等 KSP 主窗口出现）、可逆恢复路径、三档设置、度量与阈值，110h / M12 排期 | 开发/性能 |
| 19 | [19-windows-install-flow.md](19-windows-install-flow.md) | **Windows 标准安装 / 卸载 / 注册**（E7）：Velopack 单产物 `Setup.exe`（EXE-only，不出 MSI）、per-user 免提权、HKCU-only 注册（`ckan://` / `.ckan` / ARP / App Paths）、VC++ 依赖引导、卸载保留用户数据、0 成本签名策略、静默部署（`-s`/`-t`），138h（净增 126h）/ M13 排期 | 发布/开发 |
| 21 | [21-repo-layout.md](21-repo-layout.md) | **仓库布局、模块边界与工作区卫生**：三区制（规格仓 / 代码仓 / 产物区）、权威目录树、12 项目边界矩阵与四条架构测试、文档 / 调试产物 / 测试夹具三套落位规则、命名规范、`verify-layout` 12 条 CI 门禁、临时区清理纪律，**0h（并入 M0 门禁，不新增工时）** | 全员 |
| 22 | [22-test-cases.md](22-test-cases.md) | **测试用例与单元测试规格**：五层用例体系（架构 6 / 纯函数 16 / VM 36 / 服务 22 / SFS 16 + 属性 4 / 本地化 10 / 元数据 8 / 集成 24 / CLI 对拍 10 / headless 冒烟 8，合计 **160 条**）、替身与夹具构造器、覆盖率与七条硬门禁、CI 矩阵、三向追溯与缺口登记，**0h（并入既有 `TST-*`，不新增工时）** | 全员 |
| 20 | [20-appearance-personalization.md](20-appearance-personalization.md) | **外观个性化**（E8）：8 组 token 体系 × 三变体（Light/Dark/HighContrast）、强调色派生与对比度门禁、材质三档（Mica→Acrylic→纯色）、圆角三档（Win11 原生 / Win10 实验性）、背景（纯色/渐变/图片/游戏截图 + 暗化/模糊/不透明度，可按实例）、密度与字号、动效强度、**仅声明式 JSON 的主题包**（禁 XAML/脚本），206h / M14 排期 | 前端/体验 |

阅读顺序建议：**决策者**读 00 → 06 → 08 → 09；**开发者**读 01 → 02 → 05 → 04 → 03 → 10。

## 2. 目标

用 Avalonia 重写 CKAN 的图形界面，**Core 一行不改**（除第 04 篇里一项可选的 200 行下沉提案），使：

- 模组的浏览 / 搜索 / 依赖求解 / 下载 / 安装 / 升级 / 卸载 全流程可用，行为与现有 GUI 一致；
- **完全特性对齐**：原版 GUI 的 11 个页签、18 个对话框、全部菜单/右键动作一个不少（逐条清单与验收见 [12 篇](12-feature-parity.md)），不做 MVP 减配；
- 界面观感现代，列表性能足以承载 3,000+ 模组（当前 WinForms `DataGridView` 在 4,000 mod 时明显卡顿）；
- 顺带产出 macOS / Linux 的第一套可用的图形界面（现有只有 ConsoleUI / CLI）；
- 与现有 `ckan.exe`、CLI、配置、KSP 实例**数据兼容**；
- 新增能力域 **E1 存档工作台**：浏览/编辑 `.sfs` 存档，并提供一个**保字、幂等、可校验**的 SFS 格式化工具（[13 篇](13-savegame-sfs.md)）。它与 mod 管理正交、不依赖 Core，独立里程碑 M7。
- 新增能力域 **E2 可观测性**：日志**自动染色**审查 + 一键**脱敏诊断包** + 请 **OpenAI 兼容端点**协助排障（结论带证据行回链）（[14 篇](14-log-and-ai-debug.md)）。**默认关闭、零遥测、每次发送需用户点确认**，独立里程碑 M8。
- 新增能力域 **E3 Mod 配置**：为常见社区 mod（KSP Community Fixes、Kerbalism、TUFX、Scatterer、B9PartSwitch、KSP Performance Profiler）提供表单化配置，且**模块化到"加一个 adapter 目录就加一个 mod"**（[15 篇](15-mod-config-adapters.md)）。写入走 E1 的同一套安全网（快照 + 原子写 + 自校验），独立里程碑 M9。
- 新增能力域 **E4 本地化**：**先用官方 i18n，再补我们 GUI 新增的**——官方 943 条文案（13 种语言，zh-CN 已译 927 条）以 resx 快照方式零成本复用，新 UI 文案进独立目录；语言**热切换**、覆盖率可见、CJK 字体回退（[16 篇](16-i18n.md)）。独立里程碑 M10。
- 新增能力域 **E5 元数据本地化**：仓库里 3,799 个 mod 的 `abstract`（325 KB）与 57 条 `description` 走**译文包**，键用 `fnv1a64(normalize(源文))`（与 `UniversalI18n/tools/i18n_tools` 同一契约），仓库刷新后**只译变了的**；运行时直接复用 `UI18n.Core` 的 `Fnv1a`/`DictPack`/`UpdateVerify`（[17 篇](17-mod-metadata-i18n.md)）。独立里程碑 M11。
- 新增能力域 **E8 外观个性化**：**背景 / 磨砂 / 圆角 / 深色 全部可配**——深色浅色跟随系统或手动、强调色可派生且自动校验对比度、窗口材质三档自动降级、圆角三档（Win11 原生，Win10 实验性）、背景支持纯色/渐变/图片/游戏截图并按实例区分、密度三档与字号五档、动效强度三档（默认跟随系统减少动画）、主题包"目录即主题"且**只允许声明式 JSON**（加载外部 XAML 等于执行任意代码，绝对禁止）（[20 篇](20-appearance-personalization.md)）。独立里程碑 M14。
- 新增能力域 **E7 安装形态**：**按 Windows 标准软件流程安装/卸载/注册**——一键安装程序（per-user、**免提权**）（静默 `-s`、自定义目录 `-t`，供 GPO/包管理器）；在「已安装的应用」里可见可卸；`ckan://` 与 `.ckan` 关联由安装器写入 **HKCU**（不再写 HKLM，也不需要 UAC）；卸载**绝不删**实例注册表与仓库缓存（[19 篇](19-windows-install-flow.md)）。独立里程碑 M13。
- 新增能力域 **E6 资源让路**：**启动游戏后自动释放大量资源**——按 L0–L4 分层把视图缓存、Core 的仓库元数据对象图（37.9 MB JSON 解析后推算 150–300 MB）、`installed_files` 索引、托管堆与工作集逐步归还，**等 KSP 主窗口出现后才动手**（避免抢 IO），用户切回时按需长回来且带进度（[18 篇](18-launch-resource-release.md)）。独立里程碑 M12。

## 3. 范围

### 在范围内

| # | 内容 |
|---|---|
| S1 | 启动、日志、配置、实例选择与切换 |
| S2 | 模组列表（浏览/搜索/过滤/排序/标签/冲突标记），虚拟化渲染 |
| S3 | 模组详情（元数据、内容、依赖、版本、描述、主页） |
| S4 | 变更集计算与展示（CkanModule 的 install/replace/upgrade/remove 规划） |
| S5 | 下载 + 安装 + 升级 + 卸载，含进度、取消、错误恢复 |
| S6 | 设置（缓存、安装过滤器、语言、仓库优先级、兼容版本、代理） |
| S7 | 实例管理（新增/编辑/克隆/删除/自动探测） |
| S8 | 未托管文件（unmanaged files）查看与接管 |
| S9 | 安装历史、下载统计、PlayTime（**完整版**：含从历史重装、统计图表、时长就地编辑） |
| S10 | modpack（`.ckan`）导入导出，含导出编辑器 |
| S11 | 打包、发布、CI、回滚方案 |
| S12 | **全特性对齐**：55 条功能点逐条实现与验收（[12 篇](12-feature-parity.md)），另有插件系统、托盘、自动更新、缓存管理、去重、残留目录清理、组合搜索、推荐选择与审计、认证令牌管理、安装过滤器、首选主机、兼容版本对话框 |
| S13 | **E1 存档工作台**（新增域，非原版功能）：存档列表、树/文本/差异三视图编辑、格式化工具（CLI + GUI）、校验与修复、快照与回滚（[13 篇](13-savegame-sfs.md)） |
| S17 | **E5 Mod 元数据 i18n**（新增域）：`abstract`/`description` 译文包（单文件 `packs/<lang>/CKANMeta.jsonl`，`ctx=identifier`）、`h=fnv1a64(normalize(s))` 内容寻址、仓库 `Updated` 增量同步、机翻/人工双来源、G2–G9 门禁复用、搜索并集不回归（[17 篇](17-mod-metadata-i18n.md)） |
| S18 | **E6 启动后资源释放**（新增域）：L0–L4 五层释放、`IGameLaunchMonitor` 判定启动成功（`MainWindowHandle` + CPU 回落 + Steam 兜底）、DI 子作用域丢弃 `RepositoryDataManager`、`RegistryManager.DisposeAll()`、`GC.Collect` 压缩 LOH、`EmptyWorkingSet`、恢复路径与三档设置（[18 篇](18-launch-resource-release.md)） |
| S16 | **E4 国际化**（新增域）：官方 resx 快照复用（L2）、新 UI 文案目录（L1）、`Core.` 前缀走 Core 资源（L3）、三层解析与缺译可见、语言热切换、CJK/德语/俄语布局、伪本地化、Crowdin 接入（[16 篇](16-i18n.md)） |
| S15 | **E3 Mod 配置适配层**（新增域）：声明式 adapter 加载、字段表单、注释即说明、分组/搜索/仅看已改、diff 预览与回滚、只读兜底、骨架导出（[15 篇](15-mod-config-adapters.md)） |
| S19 | **E7 Windows 标准安装/卸载/注册**（新增域）：Velopack `Setup.exe`（一键、per-user、免提权；静默 `-s`、`-t <DIR>`；**不出 MSI**）；ARP 条目与卸载器；HKCU-only 的 `ckan://` 协议、`.ckan` 关联、`Capabilities`/`RegisteredApplications`、`App Paths`；VC++ 运行库检测与引导；卸载保留 `%LocalAppData%\CKAN` 与 `<GameDir>\CKAN\registry.json`；Authenticode 签名（[19 篇](19-windows-install-flow.md)） |
| S21 | **仓库与工作区卫生**（工程规范）：代码仓 / 规格仓 / 产物区三区制；项目边界即目录边界（`CkanUI.*` 命名统一 + 四条架构测试）；文档进 `docs/`、调试产物进 `artifacts/`（不入库）、测试夹具进 `tests/fixtures/`；临时物只进 `_scratch`/`spikes`/`docs/_drafts/` 且定期清理；`tools/verify-layout` 12 条门禁接入 CI（[21 篇](21-repo-layout.md)） |
| S22 | **测试用例级规格**：每条用例有 ID、输入、可自动判定的期望；三向追溯（F-ID / T-*/WBS）；覆盖率硬阈值（`Sfs` ≥95%/90%、`Core` ≥90%、VM ≥75%、Services ≥60%、diff ≥80%）与七条合并门禁（[22 篇](22-test-cases.md)） |
| S20 | **E8 外观个性化**（新增域）：token 化外观（8 组 × 三变体）、强调色 12 预设 + 自定义 + 6 阶派生 + WCAG 对比度硬门禁、材质 Mica→AcrylicBlur→纯色自动降级、圆角三档、背景四来源（含游戏截图）与暗化/模糊/不透明度、密度三档 / 字号五档、动效三档、主题包（声明式 JSON + 白名单 + 回滚）、外观导入导出与按实例覆盖（[20 篇](20-appearance-personalization.md)） |
| S14 | **E2 日志审查与 AI 排障**（新增域）：四路日志采集（log4net / IUser / 未捕获异常 / 子进程）、9 族自动染色与过滤、时间轴与聚合、脱敏诊断包、OpenAI 兼容端点的结构化排障建议（[14 篇](14-log-and-ai-debug.md)） |

### 不在范围内（明确排除）

| # | 内容 | 原因 |
|---|---|---|
| X1 | 修改 `Core/` 的业务逻辑 | 本次目标是换 UI；下沉变更集模型为可选提案（04 篇 §5） |
| X2 | 替换 `TxFileManager` / 跨平台事务层 | Core 自带 Windows 绑定，替换成本高于收益（08 篇 R-07） |
| X3 | 复用 Core 的自动更新（`Core/AutoUpdate/`） | 它替换运行中 exe 并拉起 `ckan.exe`，与新 exe 语义冲突（02 篇 §9）；**但"自动更新"这一功能必须保留**，由新 UI 自建（12 篇 F-52 / UI-27） |
| X4 | 与老 GUI **同时**操作同一 KSP 实例 | 实例锁文件导致不可能（09 篇 §3） |
| X5 | 兼容 `net481` / .NET Framework | 新 UI 只出 `net10.0` + 自包含发布，系统下限 = **Win10 1607**（细节见 11 篇 §1/§3） |
| X6 | 重写 netkan / Cmdline / ConsoleUI | 完全不动 |

## 4. 已定决策（ADR 摘要）

| ID | 决策 | 理由 | 备选与否决原因 | 可逆性 |
|---|---|---|---|---|
| D1 | Core 通过**源码/submodule 引用**，不用 NuGet 包 | 需要 master 的修复（1.36.5），NuGet 停在 1.36.4；便于断点进 Core 源码 | NuGet：版本卡死、Core bug 只能等发版 | 高（改 csproj 一行） |
| D2 | UI 为**独立仓库** | UI 仓库干净、CI 快、不被 ILRepack/打包链路绑住 | 仓库内新项目：会污染上游 CI 与 release 产物 | 中（迁回仓库只是移动目录） |
| D3 | 框架选 **Avalonia 12**（`net10.0`，自包含发布）——**已拍板 2026-10-26** | netstandard2.0 Core 可直接消费；**Win10 1607→Win11 全覆盖且免装运行时**；Fluent+动效层可达 WinUI3 观感；跨平台 | WPF：流畅感最难且 Win-only；WinUI3：OS 下限 1809 且需 WinAppSDK 运行时、Win10 上同样没有 Mica；Blazor Hybrid：本地文件与进程交互要过桥（论证见 11 篇） | 中（重写 UI 层即换框架，代价大） |
| D4 | 采用 **MVVM + 轻量 DI（Microsoft.Extensions.DependencyInjection）** | 可 headless 测试 ViewModel；不与 Core 的 Autofac `ServiceLocator` 混用 | 直接调用 Code-behind：无法测试，回调线程问题会失控 | 高 |
| D5 | **不改 Core**；如必须，先写 RFC 并在 fork 内单独提交 | 保证 Core 与上游可同步 | 大改 Core：上游无法 merge，长期维护成本爆炸 | 高 |
| D6 | 变更集模型（`ModChange`）**先复制 GUI 实现**，达标后再提 RFC 下沉 | 避免第一阶段就产生 Core 分歧 | 立刻下沉：阻塞在 RFC 评审 | 高 |
| D7 | **全特性对齐，不设 MVP 减配**（2026-10-26 用户指令） | 换 UI 的目的不是做一个"够用的新客户端"，而是让现有用户**无损迁移**；任何缺失功能都会把人赶回 `ckan.exe`，两条 UI 长期并存 | 先 MVP 再补：老 GUI 无法下线，维护成本翻倍，且补功能时代码结构已定型 | 低（范围已写进 12 篇，收窄等于重排里程碑） |
| D8 | **存档工作台（E1）独立成域，先 CLI 后 GUI**（2026-10-26 用户指令） | 格式化工具不需要 GUI 也不需要 Core，84h 即可独立交付；编辑器的风险全部在"写盘安全"而非界面，先把解析/序列化/校验做扎实 | 先做 GUI 编辑器：在没有往返一致性证明前就允许写盘 = 拿用户存档试错 | 中（CLI 与 GUI 共用同一 `Sfs` 库，方向不可逆但范围可暂停） |
| D12 | **元数据译文走 i18n_tools 契约、旁路存储**（2026-10-27）：不自建哈希/门禁/审计，一律复用 `UniversalI18n/tools/i18n_tools` 的 `h=fnv1a64(normalize(s))` 与 G2–G9；运行时 `ProjectReference` `UI18n.Core`；包与 `mods.json`/`registry.json` **完全隔离**（用 `--root` 独立根） | 已有 24k 行工具链与 C# 侧 `Fnv1a`/`DictPack`/`UpdateVerify` 现成，自研等于把同一契约实现两遍，必然漂移 | 自研 sha256 键 + 自写门禁：+2 套实现、术语无法与原版汉化对齐 | 中（包格式可换；契约测试锁死漂移） |
| D13 | **启动游戏后分层释放、且只在主窗口出现后做**（2026-10-27）：L0 视图 → L1 应用缓存 → L2 Core 数据 → L3 托管堆 → L4 工作集，逐层可独立成败；L2/L3 延后到 KSP 主窗口出现 + CPU 回落 <20% | KSP 启动是重 IO/重 CPU，抢资源会拖慢装载；而仓库对象图是真正的内存大头，先动它等于没放 | 一律在启动瞬间做 L0–L4：装载变慢 10–20%，用户会认为 UI 拖慢了游戏 | 中（层与时机都是策略参数，可单层关闭） |
| D11 | **i18n 先复用官方、再补新增**（2026-10-27 用户指令）：官方 resx **复制进新程序集**（不引用 `GUI`：它是 WinExe+WinForms，资源管理器被类型名绑死），新 UI 文案独立目录并用 `legacy-map.json` 显式映射 | 官方已有 943 条文案 / 927 条中文译文，重写等于白扔一年翻译；显式映射让借用范围可审计、可测 | 全部自己重写：+927 条翻译维护量，术语反而更乱 | 中（映射可随时收窄；L1 自写部分不受影响） |
| D10 | **Mod 配置（E3）用声明式 adapter，不做通用 `.cfg` 编辑器**（2026-10-27 用户指令） | 实测 `GameData` 有 **3,598 个** ModuleManager 补丁 `.cfg`（`@`/`+` 语法），通用编辑器必然改坏游戏；声明式 JSON（无可执行代码）既能做到"加目录即加支持"，又把攻击面与维护成本压到最低 | 通用编辑器：用户一改补丁就炸，且无法验证 | 高（adapter 与渲染器解耦，随时可收窄到白名单） |
| D14 | **安装形态走 Windows 标准流程：Velopack 单产物 `Setup.exe`（EXE-only）+ per-user 免提权 + HKCU-only 注册**（2026-10-27 用户指令），`packId` 固定为 `CkanUI` 以免与数据目录 `%LocalAppData%\CKAN` 冲突 | 一个 EXE 同时覆盖双击装/静默装/指定目录/企业派发（`-s`、`-t`），不必维护 MSI 第二条产物线；免提权正好修掉旧 `GUI/URLHandlers.cs:59-82` 那段"提权重启自己"；自带 ARP 条目与卸载器；`Update.exe` 直接取代 R-21 的自建替换器 | MSIX：Win10 **1709** 下限与 1607 目标冲突、侧载策略、包容器会把 `registry.json` 重定向；纯 WiX：可控但要自建更新机制 | 中（安装器可换，注册项与卸载语义已定） |
| D16 | **代码仓与规格仓分离、三类产物各有唯一落位**（2026-10-27 用户指令「划清各个模块的边界和文件夹，保持工作区整洁，各种文档/debug/测试用例不要乱丢」）：实现代码只在新仓库 `ckan-ui`（`src/` + `tests/`），本 CKAN fork 只作为规格与上游只读区；文档进 `docs/`、调试产物进 `artifacts/`（不入库）、测试夹具进 `tests/fixtures/`；临时物只进 `_scratch`/`spikes` 并 7 天清理 | 老 GUI 把 30 个控件、18 个对话框、模型与本地化全塞进 `GUI/` 一个项目，靠 Designer 与分部类维持秩序，这是维护成本的主要来源；而"探针程序留在仓库根、编译产物被误提交"这类问题只能靠目录约定 + CI 门禁解决，靠记性必然复发 | 单仓 monorepo（代码 + 文档 + Core 全在一起）：需要把 CKAN 上游 vendored 进来，与 D1（submodule 只读）冲突 | 高（纯约定，随时可调整） |
| D17 | **测试用例文档化，且工时并入既有 `TST-*`**（2026-10-27 用户指令「最后编写测试用例/单元测试文档」）：每条用例必须能被 06 篇某个 `TST-*` 任务吸收，指不到就说明是新工作量、要改 WBS；禁止用"再补一份测试文档"的方式偷偷加范围 | — | 让测试另行估工（会与 2196h 基线脱钩） | — | 低 |
| D15 | **外观一律 token 化，主题包只允许声明式 JSON**（2026-10-27 用户指令）：颜色/圆角/密度/字号/动效/材质/背景 7 类全部走 `Ui.*` 的 `DynamicResource`，CI 拦颜色与圆角字面量；主题包是 `theme.json` 白名单覆盖，**不带 XAML/脚本/DLL** | 老 GUI 的"深色"是从注册表推断的只读状态（`GUI/Util.cs` `DarkMode`），151 处 `BackColor =` 全散在控件里，改一次要改 200 多处；Avalonia 的 `ThemeDictionaries` 必须配 `DynamicResource`（`StaticResource` 直接抛异常），只有 token 化才能一处切换全局生效。而外部 XAML 在 Avalonia 里能挂事件与自定义控件，加载它等于任意代码执行——所以主题包必须是纯数据 |
| D9 | **日志与 AI 排障（E2）独立成域；默认关闭、零遥测、逐次确认**（2026-10-27 用户指令） | 日志天然含路径/用户名/令牌，外发必须由用户逐次点头；AI 只出**带证据行的建议**，绝不自动执行 | 默认开启或后台自动上报：隐私不可接受，一旦出事无法挽回信任 | 高（整域可整段删除，不影响主计划） |

## 5. 里程碑总表

| 里程碑 | 名称 | 周期 | 出口标准（Gate） | 关联 WBS |
|---|---|---|---|---|
| **M0** | 环境与骨架 | 1 周 | 新仓库可 `dotnet build`；启动到主窗口；`IUser` 可弹一次确认框；日志落盘 | ENV-*, ARC-*, INT-01~03 |
| **M1** | 只读浏览 | 2 周 | 列表可展示 3,000+ mod 且滚动无卡顿；搜索/过滤/排序可用；详情四页可用 | UI-01~03, VM-01, MOD-01~04 |
| **M2** | 安装闭环 | 3 周 | 能装/升级/卸载；进度与取消正确；取消后无残留半装状态；退出无锁残留 | INT-07~08, UI-04, VM-02 |
| **M3** | 设置、实例与仓库 | 3 周 | 设置全项与 `config.json` 双向一致；实例增删克隆改名可用；仓库/令牌/首选主机/过滤器/兼容版本/语言全可用（F-01..17） | UI-06, UI-07, UI-29~34, VM-03, VM-06 |
| **M4** | 文件与数据族 | 5 周 | 缓存管理、未托管、残留目录、安装历史、去重、导出/modpack 编辑器、统计、PlayTime、托盘全可用（F-35..51） | UI-10, UI-19~26, INT-09~10 |
| **M5** | 打磨与无障碍 | 3 周 | 键盘全流程、无障碍、动效、帧率门禁、本地化 | UI-12, UI-13, UI-MOTION, 07 篇 §7 |
| **M5'** | 全特性收口 | 3 周 | 插件系统与自建自动更新完成；**TST-05 的 55 条全部有结论、红色项为 0**（12 篇 §2） | UI-27, UI-33, TST-05 |
| **M6** | 发布 | 3 周 | 打包产物可分发；崩溃兜底；文档；回滚路径验证 | PKG-*, TST-01~04 |
| **M7a** | 格式化 CLI（E1） | 2 周 | 10 个真实存档往返字节一致；幂等；9 类坏文件识别；SP-18 端到端通过 | SFS-01/02/03/08/10 |
| **M7b** | 只读工作台（E1） | 2 周 | 列表首屏 ≤300ms；5MB 文本视图 ≤1.5s；与 KSP 主菜单一致 | SFS-04/05/06, UI-SAVE-01/03 |
| **M7c** | 编辑与安全（E1） | 2 周 | 就地编辑 6 类值；快照回滚；写盘中 kill 原文件完好；KSP 运行时写盘被拦 | SFS-07, UI-SAVE-02/06, VM-SAVE |
| **M7d** | 差异与收口（E1） | 1 周 | 三源 diff；格式化预览 == 落盘；文档齐 | SFS-09, UI-SAVE-04/05, DOC-SAVE |
| **M8** | 日志与 AI 排障（E2） | 5 周 | 四路日志汇流；9 族染色快照全绿；10 万行 p99 ≤16.7ms；诊断包反向扫描零命中；AI 结果每条假设都能回链日志行 | LOG-01~10, UI-LOG-01/02, VM-LOG, TST-LOG, SP-19~SP-22 |
| **M9** | Mod 配置适配层（E3） | 5 周 | 6 个 adapter fixtures 往返逐字节一致；版本越界强制只读；写盘 diff 只含目标行；骨架导出产物可被加载器自举 | CFG-01~08, UI-CFG-01, VM-CFG, TST-CFG, DOC-CFG, SP-23~SP-26 |
| **M10** | 国际化（E4） | 4 周 | 官方文案零改动复用（映射 ≥90% 常用键）；四语言截图无豆腐块/无截断；热切换 20 屏全刷新；伪本地化零截断 | I18N-01~11, TST-I18N, DOC-I18N, SP-27~SP-30 |
| **M11** | Mod 元数据 i18n（E5） | 4 周 | C#/Python 的 `h` 交叉校验 100/100；摘要覆盖率 ≥95%；仓库增量只译变更；英文词搜索结果集与老 GUI 完全一致 | MTI-01~09, UI-MTI-01/02, VM-MTI, TST-MTI, DOC-MTI, SP-31~SP-35 |
| **M14** | 外观个性化（E8） | 5 周 | 三变体与强调色即时切换且对比度全部合规；材质/圆角三档在对应系统显示正确并写明实际档位；130% 字号 + 德语长文案零截断；主题包白名单能拦住 `.axaml`/`<`/`{`/超范围值；每个外观档位下滚动 p99 ≤16.7ms | THM-01~10, UI-THM-01, VM-THM, TST-THM, DOC-THM, SP-46~SP-50 |
| **M13** | 安装/卸载/注册（E7） | 4 周 | 普通账户双击 `Setup.exe` 全程无 UAC；控制面板显示名称/版本/发布者正确；`ckan://` 与 `.ckan` 由 HKCU 注册（HKLM 零新增）；`CkanUISetup.exe -s` 静默装/卸退出码 0；卸载后 `%LocalAppData%\CKAN` 与实例 `registry.json` 仍在 | PKG-01~11, UI-INS-01/02, VM-INS, TST-INS, DOC-INS, SP-41~SP-45 |
| **M12** | 启动后资源释放（E6） | 3 周 | 均衡档私有字节下降 ≥40%、托管堆 ≥60%；恢复 ≤3 s 且无空白列表；对 KSP 装载影响 ±3% 内不可测 | RLS-01~08, UI-RLS-01, VM-RLS, TST-RLS, DOC-RLS, SP-36~SP-40 |

合计 **60 周**（主计划 M0–M6 = 23 周 / 854h；E1 存档工作台 M7 = 7 周 / 250h；E2 日志与 AI M8 = 5 周 / 186h；E3 Mod 配置 M9 = 5 周 / 174h；E4 国际化 M10 = 4 周 / 156h；E5 元数据本地化 M11 = 4 周 / 134h；E6 启动后资源释放 M12 = 3 周 / 110h；E7 安装/卸载/注册 M13 = 4 周 / 138h（EXE-only），其中 12h 抵扣 `UI-27` 的自建替换器 → **净增 126h**；E8 外观个性化 M14 = 5 周 / 206h；总 2196h ≈ 54.9 周净工时 + 联调/评审缓冲；工时明细见 [06 篇](06-wbs-milestones.md)，范围依据见 [12 篇](12-feature-parity.md)、[13 篇](13-savegame-sfs.md) 、[14 篇](14-log-and-ai-debug.md)、[15 篇](15-mod-config-adapters.md)、[16 篇](16-i18n.md)、[17 篇](17-mod-metadata-i18n.md) 、[18 篇](18-launch-resource-release.md) 与 [19 篇](19-windows-install-flow.md)）。M0 后即产生可演示物，M2 后即"可日常使用"，M5' 后即可宣告**完全特性对齐**，M7a 后即可交付**独立 SFS 格式化工具**，M8 后即具备**自助排障**能力，M9 后即可**在 UI 里配置主流社区 mod**，M10 后即可**用母语操作整个界面**，M11 后即可**读懂每个 mod 在做什么**，M12 后即可**一启动游戏就把内存还给系统**，M13 后即可**像正经软件一样安装、注册与卸载**。

## 6. 验收总纲

一次重构算成功，需同时满足：

1. **功能**：M2 的闭环（浏览→搜索→安装→升级→卸载）在真实 KSP 实例上零手工干预跑通；
2. **正确性**：注册表（`CKAN/registry.json` 单文件，含 `installed_modules`/`installed_files`）变更结果与 `ckan.exe install` 的结果**逐字段一致**（见 07 篇 §5 对拍法）；
3. **安全**：任何时刻强杀进程，重开不出现锁冲突、不留半装文件（`CkanTransaction` 生效）；
4. **性能**：3,000 mod 冷启动到可交互 ≤ 3s；滚动 60fps；搜索输入延迟 ≤ 100ms；
5. **不改 Core**：`git diff` 对 Core 零改动（或仅 RFC 已批准项）；
6. **特性完全性**：[12 篇](12-feature-parity.md) §2 的 **55 条 F-xx 全部为"通过"**，且每条都有可复核的验收记录（自动化用例 ID 或手工步骤编号）。任何一条为红，即视为"未对齐"，不得发布。
7. **存档无损**（E1）：07 篇 §9.1 的三条不变量（往返一致 / 幂等 / 值不变）在 10 个真实存档上恒真；且**格式化后的存档能被 KSP 正常载入**（SP-18）。任一失败则 M7 只交付只读校验形态。
8. **隐私与零遥测**（E2）：诊断包出口反向扫描零命中（用户名/令牌/邮箱/SteamID/IP）；AI 请求**只在用户点"发送"后发出**；不存在任何后台自动上报（14 篇 §5/§6）。
9. **配置写入安全**（E3）：6 个 adapter 的 fixtures 往返后**只有目标行变化**（其余逐字节不变）；版本越界一律只读；写盘后重新解析自校验通过；快照可回滚（15 篇 §9）。
10. **本地化**（E4）：官方 943 条文案零改动复用；四语言（zh/de/ru/ja）截图无豆腐块、无截断；语言切换后 20 个视图文案全部变化且历史日志保持原文；伪本地化零截断（16 篇 §11）。
11. **元数据译文不误导**（E5）：每条译文可辨人工/机翻/已过期；原文一键可达；英文词搜索结果集与老 GUI 零差异；`--root` 隔离保证未污染 UniversalI18n 仓库（17 篇 §10/§11）。
12. **资源让路可量化**（E6）：`metrics.jsonl` 五阶段读数齐全；均衡档私有字节下降 ≥40%、托管堆 ≥60%、句柄 ≥20%；恢复 ≤3 s 且全程有进度、无空白列表；对 KSP 装载时间影响在 ±3% 内（18 篇 §10）。
13. **安装即标准**（E7）：普通账户双击安装全程无 UAC；「已安装的应用」条目四项（名称/版本/发布者/图标）正确；`ckan://` 与 `.ckan` 均由 HKCU 注册生效且 **HKLM 零新增**；静默装/卸退出码 0；卸载后 `%LocalAppData%\CKAN`、`<GameDir>\CKAN\registry.json`、`repos` 缓存**全部仍在**；四个二进制签名 `Valid` 且带时间戳（19 篇 §14）。
14. **外观可配而不伤可读性**（E8）：三变体 + 强调色 + 材质 + 圆角 + 背景 + 密度 + 字号 + 动效八项**全部即时生效且可恢复默认**；所有内置配色与导入主题包过 WCAG AA（4.5:1 / 3:1）门禁，不合规不启用；系统高对比度开启时压过一切自定义；每个外观档位下滚动 p99 ≤16.7ms、首帧 ≤1.5s；主题包无法执行任何代码（20 篇 §14/§15）。
15. **工作区无菌**（工程规范）：仓库根只剩登记在册的条目；`docs/` 编号唯一且有索引，`artifacts/`/`spikes/`/`_scratch/`/`docs/_drafts/` 一律不入库；`tests/fixtures/` 跑测前后哈希不变且单文件 ≤256 KB；`Avalonia` 与 `CKAN` 命名空间只出现在允许的项目里；`tools/verify-layout` 全绿（[21 篇](21-repo-layout.md) §5/§12）。
16. **测试可追溯**：任一 F-ID、`T-*` 验收项、`TST-*` 任务都能指到 [22 篇](22-test-cases.md) 的具体用例 ID；七条合并门禁（架构测试、CLI 对拍零差异、SFS 不变量、夹具只读、脱敏出口扫描、无 `Thread.Sleep`/`DateTime.Now`、覆盖率阈值）全绿（[22 篇](22-test-cases.md) §17）。

## 7. 术语

| 术语 | 含义 |
|---|---|
| Core / CKAN-core | `Core/CKAN-core.csproj`，业务库，24,661 行 |
| `IUser` | Core 与 UI 的唯一人机交互接口，6 个成员（`Core/User.cs:10`） |
| Registry / 注册表 | 每实例的**单文件**记录 `<GameDir>/CKAN/registry.json`（`Core/Registry/RegistryManager.cs:61`）+ 锁文件 `registry.locked`（`:46`），内含 `installed_modules`/`installed_files`/`sorted_repositories` |
| 变更集 / ChangeSet | 一次操作要 install/replace/upgrade/remove 的模组集合 |
| 未托管文件 | 存在于 GameData 但注册表里没有的文件（手工装/别的工具装的） |
| 事务 / CkanTransaction | 基于 TxF 的文件操作事务（`Core/CkanTransaction.cs:24`） |
| Headless | 无 UI 运行，`IUser.Headless == true` |
| Modpack | `.ckan` 文件，一组模组+版本+仓库来源的清单 |

## 8. 修订记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-26 | v1 | 初稿，依据可行性评估文档定稿 D1–D6 |
| 2026-10-26 | v1.1 | 新增 11 篇（技术栈与 OS 覆盖决策）；修订 D3（Avalonia 12 + 自包含发布）与 X5（下限 Win10 1607） |
| 2026-10-26 | v1.2 | **技术栈定稿**：Avalonia 12 + `net10.0` 自包含，下限 Win10 1607（放弃 1507/1511、Win7/8.1）；WBS 合计 589h→615h；M1 门禁增滚动 p99；新增 R-16~R-18、SP-09~SP-13、材质与动效规范。已回填 01/03/06/07/08/09/10/11 篇 |
| 2026-10-26 | v1.3 | **完全特性对齐**（用户指令）：新增 12 篇（55 条 F-xx + 缺口任务）；新增 D7；S9/S10 由"MVP 版"改为"完整版"，新增 S12；X3 澄清"自动更新功能保留、Core 实现不复用"；排期 15 周→**23 周**，里程碑增至 M0–M6（含 M5' 全特性收口）；验收总纲加第 6 条"特性完全性"。已回填 03/04/06/07/08 篇 |
| 2026-10-26 | v1.4 | 工时自检校正：06 篇 §1.9 表逐行相加实为 **211h**（原写 203h），故本节 239h、WBS 846h→**854h**（615h 基线不变），排期 23 周不变；同步改 06/11/12/08/本文件与 `doc/ui-refactor-feasibility.md`；另新增 07 篇 §8、08 篇 §6、10 篇 SP-14 |
| 2026-10-26 | v1.5 | **新增能力域 E1：存档工作台与 SFS 格式化工具**（用户指令：「存档管理和编辑（sfs 文件），需要编写一个格式化工具」）。新建 13 篇（实测本机真实存档：4.86MB / 270,706 行 / CRLF / Tab 缩进 / 最大深度 8 / 110,682 键值对 / 7,333 空值 / 1 处反斜杠）；新增 S13、D8、M7a–M7d；WBS 854h→**1104h**、排期 23 周→**30 周**（E1 250h / 7 周，与主计划无技术依赖可并行）；验收总纲加第 7 条"存档无损"；回填 03 §14 / 04 §10 / 06 §1.10 / 07 §9 / 08 §7 / 10 §10 |
| 2026-10-27 | v1.6 | **新增能力域 E2：日志审查与 AI 辅助排障**（用户指令：「log 的 review 和 OpenAI AI 请求帮助 debug，最好 log 能自动染色和区分信息」）。新建 14 篇（实测：`Core/Logging.cs:23` 依 CWD 找 `log4net.xml`、默认 root=**WARN**、`GUI/log4net.xml` 为 1MB×30 滚动 `./CKAN.log`；Core 全库仅 59 条日志语句；`GUI/Controls/Wait.cs:330-335` 为无染色 TextBox）；新增 S14、D9、M8；WBS 1104h→**1290h**、排期 30 周→**35 周**；验收总纲加第 8 条"隐私与零遥测"；顺带修正 §7 术语中 Registry 的错误描述（原写 `installed-<gamever>-<repo>.ckan`）；回填 03 §15 / 04 §11 / 06 §1.11 / 07 §10 / 08 §8 / 10 §11 |
| 2026-10-27 | v1.7 | **新增能力域 E3：社区 Mod 配置适配层**（用户指令：「对常见社区mod的配置（比如说community fix和memgraph等）提供支持，这里要模块化方便添加」）。新建 15 篇（实测本机：`KSPCommunityFixes/Settings.cfg` 29,850 字节 / 618 行 / 根节点 `KSP_COMMUNITY_FIXES` / 138 条键（true 118、false 17、enum 1、数值 2）/ 325 行 `//` 注释即说明 / 2 空格缩进；`GameData` 中 **3,598 个** `.cfg` 是 ModuleManager 补丁语法→不可通用编辑）；新增 S15、D10、M9；WBS 1290h→**1460h**、排期 35 周→**40 周**；验收总纲加第 9 条"配置写入安全"；回填 01 §12 / 03 §16 / 04 §12 / 06 §1.12 / 07 §11 / 08 §9 / 10 §12 |
| 2026-10-27 | v1.8 | **新增能力域 E4：国际化与官方文案复用**（用户指令：「原生GUI i18n支持」→「首先先使用官方的i18n再自己写我们GUI新增的」）。新建 16 篇（实测：官方 **943** 条文案 = designer 420 + GUI 目录 314 + Core 209，zh-CN 已译 **927** 条；`GUI/SingleAssemblyComponentResourceManager.cs:13` 从本程序集按清单名取流；`GUI/Main/Main.cs:104-111` 只设 `CurrentUICulture`；`GUI/Dialogs/SettingsDialog.cs:795-797` 改语言需重启；**`es-ES` 有 36 个翻译文件却不在 `AvailableLanguages`，`en-GB` 在列表却无资源**）；新增 S16、D11、M10；WBS 1460h→**1602h**、排期 40 周→**44 周**；验收总纲加第 10 条「本地化」；回填 01 §13 / 03 §17 / 04 §13 / 05 §13 / 06 §1.13 / 07 §12 / 08 §10 / 10 §13 |

| 2026-10-27 | v1.9 | **新增能力域 E5：Mod 源信息 i18n 与仓库同步**（用户指令：「mod 源信息 i18n 翻译，连仓库同步」→「使用这里的 tool `UniversalI18n/tools/i18n_tools`」）。新建 17 篇：`h=fnv1a64(normalize(s))` 内容寻址、单文件 `packs/<lang>/CKANMeta.jsonl`（`mod:"CKANMeta"`、`ctx=identifier`）、复用 G2–G9 门禁与 `--root` 隔离、运行时引用 `UI18n.Core`；实测仓库 3,799 个 identifier / 31,132 个 module_version，`abstract` 325 KB（去重 3,639 块）、`description` 仅 57 条；新增 S17、D12、M11；WBS 1602h→**1736h**、排期 44 周→**48 周**；验收总纲加第 11 条「元数据译文不误导」；回填 01 §14 / 03 §18 与 §17.4 改写 / 04 §14 / 05 §14 / 06 §1.14 / 07 §13 / 08 §11 / 10 §14 |
| 2026-10-27 | v2.0 | **新增能力域 E6：游戏启动后的资源释放**（用户指令：「启动游戏后自动释放大量资源」）。新建 18 篇：分层释放 L0 视图 / L1 应用缓存 / L2 Core 数据（DI 子作用域丢弃 `RepositoryDataManager` + `RegistryManager.DisposeAll`）/ L3 托管堆压缩 / L4 `EmptyWorkingSet`；T0–T5 时机编排（**等 KSP 主窗口出现 + CPU 回落才动手**，实测 `Core/GameInstance.cs:360-403` 只有 `onExit` 回调、`:370` 会改全局 CWD）；实测 Core 无 `Clear()` 只能靠作用域释放；新增 S18、D13、M12；WBS 1736h→**1846h**、排期 48 周→**51 周**；验收总纲加第 12 条「资源让路可量化」；回填 01 §15 / 03 §19 / 04 §15 / 05 §15 / 06 §1.15 / 07 §14 / 08 §12 / 10 §15 |

| 2026-10-27 | v2.1 | **新增能力域 E7：Windows 标准安装 / 卸载 / 注册**（用户指令：「使用windows标准软件安装/卸载/注册流程」）。新建 19 篇：定 Velopack 双产物（`Setup.exe` + `CkanUI.msi`，`--instLocation PerUser` 免提权）、`packId=CkanUI`（避开数据目录 `%LocalAppData%\CKAN`）、HKCU-only 注册（`ckan://`、`.ckan`、`Capabilities`/`RegisteredApplications`、`App Paths`、ARP）、VC++ 运行库引导（app-local 官方"不推荐"）、卸载保留用户数据、Authenticode 签名；否决 MSIX（下限 1709 且包容器与 `registry.json` 真路径语义冲突）；实测旧版注册写 **HKLM**（`GUI/URLHandlers.cs:113`）且失败时提权重启自己（`:59-82`）；新增 S19、D14、M13；WBS 1846h→**1990h**（156h 中 12h 抵扣 `UI-27`）、排期 51 周→**55 周**；验收总纲加第 13 条「安装即标准」；回填 01 §16 / 03 §20 / 04 §16 / 06 §1.16 / 07 §15 / 08 §13 / 09 §12 / 10 §16 |
| 2026-10-27 | v2.3 | **新增工程规范篇 21：仓库布局、模块边界与工作区卫生**（用户指令：「划清各个模块的边界和文件夹，保持工作区整洁，各种文档/debug/测试用例不要乱丢」）。定三区制（规格仓 / 代码仓 / 产物区）、权威目录树、12 项目边界矩阵 + 四条架构测试（`Avalonia` 与 `CKAN` 命名空间受限）、文档 `docs/` / 调试 `artifacts/` / 夹具 `tests/fixtures/` 三套落位规则、命名规范、`verify-layout` 12 条 CI 门禁；实测盘点并清理 `_probe/`（含 `t.dll`/`t.exe`/`bin`/`obj`）、删除空目录 `spike/`、把 `tools/SfsKit/` 迁入 `_scratch/`；统一项目名 `CkanUi.*`→`CkanUI.*`（20 处）、统一全部文档行尾为 LF；新增 S21、D16；**工时不变（仍是 2196h / 60 周）**，M0 门禁加第 ⑥ 条 |
| 2026-10-27 | v2.5 | **31 项待确认全部拍板并回填**（15/16/17/18/19/20/22 篇的决策节）。要点：①**E7 改 EXE-only，删除 MSI 产物线**（用户指令「使用EXE，不要使用msi，重新评估」）→ 19 篇 156h→**138h**（净增 126h），One-file `Setup.exe -s`/`-t` 覆盖静默与自定义目录；②**签名走 0 成本策略**（本期不签 + 发布页 SHA256；v1.0 前可申请 SignPath Foundation 免费 OSS 签名；不采用付费 Azure Artifact Signing）；③E4 i18n 采纳热切换 → 16 篇 142h→**156h**；④E3 MemGraph 由用户现场装包取证 → 15 篇 170h→**174h**（`CFG-08b` 计入）；⑤E8 不做"高对比度专用主题包"；⑥测试 headless 只在 Windows 跑；⑦`es-ES` 补入语言列表、数字/日期只跟系统区域、不引 Humanizer。**总工时仍 2196h / 60 周**（+18h 与 E7 的 −18h 相抵） |
| 2026-10-27 | v2.6 | **E3 MemGraph 取证完成**（用户现场装包后实测）：`GameData/MemGraph/Plugins/PluginData/MemGraph/` 下 3 个文件——`settings.cfg`（227 B / 10 键 / **flat：无根节点、无缩进、无注释** / LF / ` = ` 分隔，3 个 `bool` + 7 个 `keybinding`）、`padheap.cfg`（201 B / 22 行 `<size>\t: <count>` + `total`，**第三种语法**，控制约 1024 MB 堆垫片 → v1 只读）、`test.cfg`（20 B，只读）；DLL 实测 `ReadAllLines`+`WriteAllText`（会整份重写 → 幂等性要求）、未知键被忽略、README 路径与实际不一致（需按 `installed_files` 反查）。**15 篇加 §2.5 实测 + §4.5 完整 adapter 示例，`files[]` 增 `layout`(node/flat/custom) 与 `mode`(editable/readonly)**；**工时不变（15 篇仍 174h，flat 分支在 `CFG-04` 20h 内吸收），总量仍 2196h / 60 周** |
| 2026-10-27 | v2.4 | **新增测试用例篇 22**（用户指令：「最后编写测试用例/单元测试文档」）：五层用例体系与 **160 条用例**、替身构造器清单、10 条反模式、覆盖率与七条合并门禁、CI 五阶段矩阵、三向追溯与已知缺口（F-01..F-09 大实例克隆、F-49..F-55 SmartScreen 仅手工）。新增 S22、D17、验收第 16 条；**工时不变（2196h / 60 周）** |
| 2026-10-27 | v2.2 | **新增能力域 E8：外观个性化（背景/磨砂/圆角/深色）**（用户指令：「更多的个性化界面（背景，磨砂，圆角，深色等等）支持」）。新建 20 篇：token 8 组 × 三变体（Light/Dark/HighContrast）；强调色系统/12 预设/自定义 + 6 阶派生 + WCAG 门禁；材质 Mica→AcrylicBlur→纯色（Win10 需 `win10-x64` RID）；圆角三档（Win11 DWM 原生，Win10 实验性 `AllowsTransparency`）；背景四来源 + 暗化/模糊/不透明度 + 按实例；密度三档 / 字号五档；动效三档（默认跟随系统）；主题包声明式 JSON（禁 XAML）；实测老 GUI 无任何外观配置（`GUI/Util.cs` 的 `DarkMode` 是从注册表推断的只读状态，151 处 `BackColor =` 散在控件里）；新增 S20、D15、M14；**1990h→2196h**、排期 **55 周→60 周**；已回填 01/03/04/05/06/07/08/09/10/12/13/15/17/18/19 篇 |
