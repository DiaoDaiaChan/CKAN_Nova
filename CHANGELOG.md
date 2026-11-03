# 变更记录

本文件记录 CKAN UI 面向用户的变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)：主版本对应"能不能替换旧版"，
次版本对应"新增了什么"，修订号对应"修了什么"。

CKAN UI 的版本号只有一个手改点：`src/CkanUI.App/Version.props` 的 `CkanUiVersion`。
发版时改那一行，再在本文件顶部加一段。绑定的 CKAN Core 版本不体现在本文件名里，
在「关于」对话框里看（两者独立演进，混在一起会让人误以为换了界面就该换 Core）。

## [未发布]

### 新增

- 插件功能微型视觉角标（页面与子页面统一标出）：
  - 架构数据层：`NavItem` 与 `NavTab` 契约增加 `IsPlugin` 字段，插件解析与分节装配时自动置位；`ShellViewModel` 暴露 `IsCurrentPagePlugin` 用于外壳顶层感知。
  - 子页面分节页签（`SectionTabsControl`）：在「实例」（存档/飞船/旗帜/加载图/KSP设置）与「模组」（Mod配置）等内嵌插件页签上，标题右侧呈现精致微型「插件」胶囊角标（`CkanPluginCornerTag`），激活时自适应主题反色，清晰辨识原生与插件功能。
  - 侧栏导航与悬浮卡片（`FANavigationView` / `NavPopout`）：插件级导航项配置 `FAInfoBadge` 强调点，悬浮卡片标题旁与子项列表同步标注插件角标，并在抽屉模式 ToolTip 中追加 `(插件)`。
  - 顶栏外壳联动：当前处于插件页面或插件子页面时，窗口标题右侧同步呈现微型插件角标。
- Mod 配置解耦为内置插件并增加通用配置读写支持（`src/CkanUI.Plugin.ModConfig`，`com.ckanui.modconfig`）：
  - 架构解耦：将原本硬编码在宿主中的「Mod 配置」整套视图与视图模型迁移至独立插件工程，声明 `Section=mods` 与 `Order=50`，无缝嵌回「模组」分节内部页签条，宿主侧移除死类型注册与预热绑定。
  - 通用 ConfigNode 配置模式：针对绝大多数没有专属适配器包的模组，新增通用配置扫描器（`GenericConfigScanner`）与编辑服务（`GenericConfigEditService`）。递归扫描当前游戏实例 `GameData` 下可配置的 `*.cfg` 文件（自动排除 ModuleManager 补丁及原版零件节点）。
  - 双模式无缝切换：页头新增「适配器配置」与「通用配置」切换分段控件。通用模式下提供按 mod 归类的文件列表与快速搜索，右侧直观展示节点层级、键值对列表（支持 bool 勾选及文本/数值就地编辑、行脏标记、撤销修改与写回前安全备份）。
  - 写入安全纪律：支持运行中游戏探测与冲突阻止，采用原子临时写入 + `.bak-*` 时间戳备份，失败时自动回滚，杜绝直接写坏用户原版 cfg。
- AI 助手插件（`src/CkanUI.Plugin.Ai`，`com.ckanui.ai`）：工具分节下的「AI 助手」页签，能边聊边查 CKAN 数据。
  - 接 OpenAI 兼容端点（默认 `http://100.111.24.121:8317/v1` / `gemini-3.8-flash-high`），API Key 用 DPAPI 加密后落
    `%LocalAppData%/CKAN/ai-assistant.json`，明文只活在内存里。
  - 8 个工具：`ckan_context`、`ckan_search_mods`、`ckan_mod_detail`、`ckan_installed`、`ckan_list_pages`、
    `ckan_navigate`（唯一会切页面的一个）、`ckan_install_mod`（唯一会动磁盘的一个）、`web_search`。
    数据与写权限都经契约 `IAiCkanBridge` 由宿主实现，插件不引用 App。
  - 底栏常驻入口：和后台任务胶囊同一行的「AI 助手」按钮，点开右侧抽屉就地提问（不用先切到插件页）；
    面板关闭即释放页面实例（它不是导航页，页面缓存认不到它），Esc 也能收起。
  - AI 能直接装 mod，但闸门在宿主：先算变更计划 → 弹确认框逐条列步骤 → 用户拒绝回 `applied=false`（不是失败）→
    同意后重算计划再交安装器，并走底栏后台任务（可见、可取消）；`applied=false` 不得被模型说成「装好了」。
  - 轮次用尽不再回一句「工具调用轮次用尽」：再发一次 `tool_choice=none` 的收口请求，让模型拿已查到的结果写正文
    （真实端点上模型会连着几轮只回工具调用；实测一条完整问答 `stop=Completed`、6 次工具调用、960 字回答）。
  - 搜索服务默认不配置：`web_search` 直接回 `web_search_unavailable`，模型据实说「没联网查到」而不是白等一次进程启动超时。
    `free-search-mcp`（Python，非 Node）的打包与运行时成本评估见该仓库的 `PACKAGING.md`。
  - 真实端点用例在 `tests/CkanUI.Plugin.Ai.Tests/LiveEndpointTests.cs`，默认忽略；设 `CKANUI_AI_LIVE=1` 与 `CKANUI_AI_KEY` 才跑，Key 不入库。
- 插件页签自带图标：`PluginPageDescriptor` 新增可选 `Glyph`（宿主字形表里的名字），侧栏不再把每个插件都画成同一枚七巧板。
  - 「性能」用速度表、「模组翻译」用翻译字形；字形名认不出来时退回通用插件图标而不是留一格空白（插件 dll 是第三方的，不能因为图标名写错就拒绝加载）。悬停动效另由侧栏悬停改造统一接线，未随本次提交。
  - `tests/CkanUI.App.Tests/PluginGlyphTests.cs` 用真插件 dll 钉住「声明了图标」「宿主认识这个图标名」「两个插件不同图标」三件事。
- 插件系统试点（F-17）：新增随包携带的「性能」页签，作为首个**内置（首方）插件**，端到端跑通「插件目录 → 激活 → 导航栏页签 → 宿主注入服务 → 卸载」。
  - 内置目录 `<App>/plugins` 与用户目录 `%LocalAppData%/CKAN/plugins` 分开：内置插件由宿主背书激活，用户插件仍默认不激活；同 Id 时内置优先并把用户那份记进失败清单。
  - 激活状态持久化到 `%LocalAppData%/CKAN/plugin-activation.json`（tmp → 原子替换，读坏按空表处理，写失败只告警）。
  - 启动引导放后台（不占首帧），完成后经 `IPluginService.Changed` 差分重建导航栏，页签自动出现在「插件」之后。
  - 「性能」页：5 个指标（FPS / 帧耗时 / 1% low / PTR / 抖动）+ 七段耗时分解 + GPU 五段与括号耗时 + 四类热点页签（插件 / 模块 / 部件 / 程序集）+ 诊断与检查项 + 内存 / 垫高 / 探针 / 分配器 + 尖峰，迷你折线与占比条自绘，不引图表库。
  - 数据来自 KSPPerformanceProfiler 的文件桥 `GameData/KSPPerformanceProfiler/Telemetry`（latest / status / commands + acks）：1 Hz 轮询、容忍半写、契约主版本不兼容时明确提示而不是画空图；缺字段显示 `N/A`，无效数据不显示成 0。
  - 页内命令：导出报告 / 重置统计 / 垫高内存 / 释放垫高 / 刷新，按钮忙碌态与失败原因在页内可见。
  - 空态分得清「没装」和「装的是旧版」：遥测目录不在但同级有 KPP 本体（`Plugins/` 或 `*.version`）时，标题是「KPP 已安装，但版本不带遥测桥」、徽标「待升级」，并提示升级到 1.2.0+ 而不是让人去重装同一个旧版本。
  - 侧栏重建同时订阅 `RailItems.CollectionChanged`：视图模型是「先改 NavItems、最后刷 RailItems」，只订阅前者会永远拿旧快照重建（表现：视图模型里有 `performance`，侧栏看不见）。
  - 插件页一路验到视图：`ViewLocator` 的插件分支按程序集查同名 View，断掉时静默落到占位视图，看上去就是「插件只是个占位符」。
- 模组翻译解耦为内置插件：把原来写死在宿主里的「模组翻译」页整套搬进 `src/CkanUI.Plugin.ModText`（`com.ckanui.modtext`），宿主不再声明它的导航项、分区、DI 登记与预热项。
  - 页面视图模型与视图都在插件程序集里，宿主只保留领域服务 `IModTextService` 的注入；停用插件后侧栏不会留一个点进去空白的入口。
  - `PluginPageDescriptor` 新增可选 `TitleKey`：插件页的导航标题可以跟语言包走，键不存在时回退插件自述标题（不会把键名当标题显示）。
  - 页面投影缓存（L1 释放）现在也覆盖插件页：`PluginPageCache` 提成 DI 单例，由 `PageProjectionCacheParticipant` 一并 Trim。
  - 顺手修掉 `ModTextPageView.axaml` 的 `TextBox.Watermark` 过时用法（AVLN5001），App 工程构建回到 0 警告。
- 存档 / 飞船 / 旗帜 / 加载图 / KSP 游戏设置解耦为内置插件，并且**仍然待在「实例」这一节里**：
  `src/CkanUI.Plugin.Saves`（`com.ckanui.saves`）、`src/CkanUI.Plugin.Assets`（`com.ckanui.assets`，三个页签）、
  `src/CkanUI.Plugin.KspConfig`（`com.ckanui.kspconfig`）。宿主不再声明这五页的导航项、DI 登记与预热项。
  - `PluginPageDescriptor` 新增可选 `Section` 与 `Order`：填了 `Section` 的插件页进对应分节的顶部页签条，而不是
    在侧栏顶格多出一项；分节里的相对顺序由 `SectionSpecs` 的**槽位**决定（插件补位，历史顺序不变），插件额外
    声明的同分节页面按 `Order → Title` 排在末尾。
  - 停用插件后页签一起消失，当前页自动回退到那一节的首页（不会留在空白页上）；未知 `Section` 也会退回侧栏入口，
    不会因为插件写错一个键就让页面凭空消失。
  - 显式停用过的内置插件在重启后保持停用：`activateBundled` 只是宿主对自带插件的默认背书，不再覆盖
    `plugin-activation.json` 里 `activated: false` 的记录（原来点「停用」只管到本次进程退出，入口会周期性回来）。
  - 可发现性：插件管理页的页签摘要点名每个入口落在哪一节，形如
    `提供 4 个页签 · 实例：存档、飞船、旗帜、加载图 · 已停用，重新激活后这些入口才会出现`——内嵌页签从
    分节里消失后侧栏没有任何痕迹，只有这里能说清它原来在哪儿；重新激活即按原槽位回到页签条，不需要重启。
  - 主页的「存档」卡片走的是 `saves` 键：插件停用后这个键解析不出页面，卡片会退到「实例」分节，
    而不是留在原地点了没反应。
  - 新增共享 UI 层 `src/CkanUI.Ui`：`PathToThumbnailConverter` / `SkeletonPulse` / `DdsDecoder` 从宿主搬进这一层，
    动效资源键下沉为 `CkanMotionTokens`（宿主写资源、共享基元读资源），加载图的停用后缀下沉为
    `LoadingImageNaming`。插件因此不再需要反向引用 `CkanUI.App`。
  - 缩略图预热由 `IPageThumbnailWarmup` 承担：外壳不再按具体页面类型 `switch`（那是把插件的键写进外壳），
    由页面自己处理「行集合异步填充完再预热」的时序。
  - 命令面板的静态导航表不再列这几页（插件可停用，留一条 `nav:<键>` 就是留一个点了没反应的面板项）；
    动态注册记为后续工作，见 `docs/design/plugins.md` §11.11。
- 主菜单（启动落地页）：新增「主页」页并把它设为应用启动后的默认页（侧栏第一个入口）。
  - 实例概览：名称/版本/模组数/游戏目录，一键「启动游戏」「打开游戏目录」或跳到「实例管理」；实例被占用时禁用启动并说明原因。
  - 「快捷操作」分区按主次三层排布：两张大卡（模组库、变更集）承载核心工作流，三张普通卡（实例管理、存档、模组仓库）是常用页面，两个胶囊（日志、设置）收工具页；同样七个入口，不再平铺成一片。
  - 「游戏截图」分区：自动枚举 `<GameDir>/Screenshots`（与背景层同一批扩展名），按拍摄时间倒序取最近 12 张，标注拍摄时间与体积；还没截过图时只留一行「还没有截图」，不铺解释文案与目录路径（路径在「打开截图目录」的悬停提示里）。
  - 截图以横向胶片带呈现，不切页签就能看到；静止时约 50 px/s 自动向左滚动播放最近截图，触底停留约 1.2 秒后回卷，鼠标悬停暂停。缩略图在 App 层解码（宽 640），缓存按文件修改时间失效。
- 日志与诊断（M8）：新增「日志」页与底部日志抽屉。
  - 十万行级内存环形缓冲（2 万条）+ 滚动文件（5 MB × 5），日志写入失败时自动降级为仅内存，不影响主流程。
  - 三段过滤（级别/模块/来源）+ 过滤条与搜索语法合并为同一个查询、按级别聚合、时间轴直方图、指纹折叠同类报错、F3 跳转。
  - 导出诊断包：脱敏后的 zip（用户目录、盘符、UNC、令牌、SteamID、邮箱、IP、主机名等 13 组规则）+ SHA256，出口再反向扫描一遍，命中即拒发。
  - 旧版 `CKAN.log` 导入、子进程输出捕获、未处理异常（AppDomain/任务/UI 线程）统一入日志流。
  - 内置 20 条分类规则，可用 `Themes/log-rules.json` 外置热加载；坏规则只跳过单条。
  - 游戏日志接入：日志页可按「界面日志 / 游戏日志」分档，后者增量跟随当前实例的 `<GameDir>/KSP.log`（每 750 ms 轮询、从末尾 256 KB 起读、文件被截断即从头重读，UTF-8 多字节安全），增量行按 `WRN/ERR/EXC` 着色、模块名进标签、续行淡化；可一键「导入已有日志」补齐整个文件。
  - AI 排障：日志页「请 AI 分析」现在先给出脱敏后的完整预览，确认后才发往 OpenAI 兼容接口（地址/模型/Key 可配，Key 以 Windows DPAPI 密文落盘）；返回的假设按置信度降序，每条都带 `#序号` 证据行，低于 50% 的假设折叠显示。
- Mod 配置适配层（M9）：新增「Mod 配置」页与 `adapters/` 适配器目录。
  - 适配器用 JSON 声明（`docs/design/adapter.schema.json`）描述配置文件布局与字段类型，内置 KSP-Community-Fixes 与 Kerbalism 两枚。
  - 配置读写走自带的无损文本编辑：保留注释、缩进、行尾与键序，写前存快照、写后重新解析自校验，失败自动回滚；检测到不可无损往返的编码直接拒写。
  - 版本区间、字段校验、KSP 运行中默认拒绝写入（二次确认后才写并提示需重启生效）。
  - 页面支持九类字段就地编辑（布尔/整数/小数/枚举/文本/列表 + 路径、颜色、按键三种专门控件），带脏标记与逐文件保存。
- 国际化收口：注入 CKAN Core 的 209 条资源文案作为第三层来源；新增伪本地化开关（环境变量 `CKAN_UI_PSEUDO_LOCALE=1`）用于暴露未翻译与排版溢出；补缺键与回退键计数，便于量化覆盖率。
- Mod 文本：新增「仅用现有译文 / 允许机翻 / 关闭」三档策略、屏蔽名单，以及语言包完整性校验（大小与 SHA256 不符、无清单时明确报错）。
- 命令面板与快捷键目录（UI-27）：Ctrl+K 打开模糊搜索面板（19 项页面导航 + 10 项界面动作，按标题/分类/关键词加权打分并高亮命中片段），Ctrl+/ 打开快捷键目录（分组、可过滤、可复制为纯文本）。
- 深链 `ckan://`（ARC-05）：支持 `ckan://mod/<标识符>`、`ckan://instance/<实例名>`、`ckan://repo/<名称或地址>` 三类落点，启动参数与单实例转发都会执行；落不到目标只记日志，不打断启动。
- 模组详情四页签（F-26）：元数据 / 依赖关系 / 全部版本 / 文件与内容，标题带计数角标，切换模组复位到第一页，并真正挂进模组页右侧面板（此前该视图全仓库无宿主）。
- 全部版本与指定版本安装（F-30）：版本列表取自跨仓库索引（版本号降序、KSP 兼容范围、发布时间、下载体积与链接、已装/不稳定标记），可对任一版本发起安装并进变更集。
- 实例自检与修复（F-08）：锁文件/锁目录诊断与占用者线索、游戏运行中拒绝强制解锁、坏注册表隔离（改名保留字节）、CKAN 或游戏版本过旧给升级建议，每个动作写 JSONL 记录。
- 自动更新闭环（F-52）：解析 GitHub release 资产挑 Windows zip、SHA256 与大小校验、剥壳与备份后逐文件替换、失败或取消自动回滚，成功提示重启。
- Mod 配置可编辑（CFG-04）：九种字段编辑器、脏标记与逐文件保存、部分失败保留草稿、KSP 运行中与高风险字段双重确认、可从 `.bak` 快照恢复。
- 启动流程状态机（18 篇 E6）：点「启动」先只还回注册表锁与仓库快照（`RegistryManager.DisposeAll()`），再拉进程——锁不必等 10 秒后的均衡档；放锁成功后页面进入「精简模式」并显示状态栏徽标与「恢复数据」按钮，启动失败立刻回滚、游戏退出或手动恢复时退出精简模式。放锁失败不阻断启动，只提示原因。
- 工作集归还（L4）：Windows 上走 `psapi.dll` 的 `EmptyWorkingSet`，用回收前后的工作集差值报数；非 Windows 或 Win32 调用失败只记 Warn 不抛，报告里记 0 项。
- Mod 配置的三个专门控件：颜色字段给 22×22 色块预览 + 十六进制输入（认不出的写法仍按原文写盘，因为解析方是 mod 自己），按键字段支持按住修饰键直接捕获成 `Ctrl+Alt+Shift+Win+Key`，路径字段多一个「浏览…」按钮选目录。
- 第三方适配器导入（ADP-04）：「Mod 配置」页可从本地 `.json` / `.zip` 安装第三方 adapter 包，包内 SHA-256 清单校验、zip 路径穿越整包拒绝、原子落盘失败回滚，装完自动重扫适配器目录；远程 URL 与 ETag 条件请求（304→无更新）在服务层就绪。
- Mod 文本的失败出口与过期判定：导出 JSONL、同步 UniversalI18n 改为返回结果对象（不再抛异常），同步时显式指定的目录不存在就直接报错、不静默回落到用户主目录；机翻条目在原文变化后被判为「过期」并可回退到人工译文。
- 安装历史新增「同时卸载快照之外的模组」开关（默认关闭，仅在勾选时预览里多一句风险说明）；游玩时长新增按实例「重置」（二次确认，重置后自动累计从零开始）。
- 背景新增「游戏截图」来源（20 篇 §9.1）：设置页「背景来源」多一项「游戏截图（最新一张）」，取当前实例 `<GameDir>/Screenshots` 里最新的一张图，切换实例背景跟着换；不落盘截图路径（游戏会不断按时间命名新文件），读不到就退回无背景而不是报错。
- 原生控件改造（按 FluentAvalonia 官方样例）：设置页改为 `FASettingsExpander` 行式布局（整数走数值微调器，改完自动保存，不再每行一个「保存」按钮）；实例页顶部换成 `FACommandBar`（窄窗自动溢出）+ 右侧 Inspector 的「更多操作」溢出菜单；左侧导航换成 `FANavigationView`（三组分组标题、日志与设置常驻底部）。
- 表面分层：窗口画布与卡片分家、卡片顶光渐变边框、输入框内凹、主按钮与幽灵按钮体系；按钮按下改用欠阻尼弹簧（可中途打断）。
- 动效骨骼（XAML 骨架重构，不写物理动画代码）：日志抽屉与未保存提示条改为与内容同格叠放的浮层，用状态类 `is-open` + 透明度 + `RenderTransform` 表达显隐，隐藏时鼠标穿透——不再绑定 `IsVisible`，也不再占用布局行，加动画时无需再改结构。
  - 底部状态栏合并：左侧状态文案、中段「日志 (最近 N 行) · 命中 …」开关（点开即抽屉）、右侧版本号合成一条 28px 状态栏。
  - 实例页的空状态与实例列表、未选中引导与详情面板各自同格叠放（`.view-layer` / `.is-active`），切换选中不再触发整页重排；未选中时右侧只留一句引导，不再是一墙禁用按钮。
  - 一级卡片预埋 `stagger-card` 锚点，表单输入框预埋 `is-invalid` 错误态类与外置 `RenderTransform`（父容器不再裁剪，为后续抖动留出空间）。
  - 未保存提示条补「放弃修改 / 保存更改」两个按钮，按页面是否提供对应命令自动显隐。
- 物理弹簧与时间编排（第三阶段）：把四档配平好的弹簧参数注入第二阶段预埋的状态类，XAML 视觉树结构一行未改。
  - 四档弹簧：临界阻尼（卡片入场、视图层切换）、微欠阻尼（日志抽屉与未保存浮条弹出、导航项过渡）、高刚度微反馈（按钮松手回弹）、强欠阻尼冲量（校验轻颤）；四档时长各有 token，受「动效强度」设置控制（减弱→90 ms，关闭→瞬变）。
  - 收敛门禁：新增测试解析样式里全部 `SpringEasing` 参数，断言归一化时间轴终点处 |Ease(1)−1| < 0.002 且尾部速度约等于 0，杜绝动画结束那一帧的位置跳变。
  - 错峰入场：页面切换后，页面内一级卡片按 25 ms 间隔从下方 14 px 处逐张以临界阻尼归位；切页会取消上一轮并复位，卡片不会停在透明态。
  - 校验轻颤：模组包编辑器与实例对话框的必填项出错时，输入框瞬时拉偏 8 px 后由强欠阻尼弹簧释放出三次衰减摆动，未通过的字段自动标红描边。
  - 可打断：抽屉、浮条、导航项在动画途中反向操作会从当前位置接着跑，不跳变；动效只改 `RenderTransform` / `Opacity`，不触发 Measure/Arrange。
  - 诚实差异：左侧导航的选中指示器是 FluentAvalonia 原生固定 600 ms 组合动画（不对外暴露时长），本轮只给导航项加了 4 px 追赶位移。

- 原生独立安装程序（CkanUISetup.exe，M13/E7）：新增自研单文件安装程序（`tools/installer`），
  在发布脚本 `release.ps1` 步骤 7.5 全自动编译内嵌自包含便携包产出 `ckan-ui-<ver>-win-x64-setup[-unsigned].exe`（约 85 MB），
  支持双击图形安装向导或命令行静默安装（`-Quiet` / `/S` / `/DIR=`），自动处理同名进程安全退出、开始菜单与桌面快捷方式创建、
  `HKCU` 卸载注册表与 `App Paths` 登记；内置系统敏感目录防误删安全守卫与 `.staging` 原子回滚机制；支持独立卸载向导。
  配合随包分发的 `install/{install.ps1,uninstall.ps1}` 脚本与 Inno Setup 模板 `CkanUISetup.iss` 构成完整分发矩阵。
  手册见 `docs/ops/install.md`，决策见 `docs/design/installer.md`。
- 代码签名通道：`tools/release/Sign-Artifacts.ps1`（`-PfxPath` / `-Thumbprint` / `-TimestampUrl` / `-DryRun`，
  signtool 从 PATH 或 Windows SDK 自动查找）；`release.ps1` 新增 `-PfxPath` / `-PfxPassword` / `-Thumbprint` /
  `-TimestampUrl` / `-SignDryRun`，签名在打包前对 stage 里的主程序执行，`manifest.json` 记 `signed` 与
  `signing{requested,applied,method,timestampUrl}`；没有证书时照旧出 `-unsigned` 包。
  `tools/release/New-DevCertificate.ps1` 能生成自签名证书，用于本机验证签名链路。
- 发布体积开关：`release.ps1 -NoReadyToRun`。自包含包解压 152 MB → 119 MB、zip 63.7 MB → 49.7 MB，代价是
  冷启动 0.81 s → 1.6 s；`manifest.json` 的 `publishOptions.publishReadyToRun` 记录实际取值。
- 项目许可证落到仓库根：新增 `LICENSE.md`（GPL-3.0 全文 + 上游 MIT 说明），发布包随带该文件（缺失即中止出包），
  「关于」对话框的双声明从此有承载文件。
- 页面缩放（75%–150%，5% 一档）：底栏中段新增滑块与加减按钮，Ctrl+滚轮微调、Ctrl+0 复位，读数常显。
  缩放只作用页面承载区——`LayoutTransformControl` 把 `ScaleTransform` 折进测量，文字、图标、间距一起按比例重排
  （不是位图拉伸），标题栏/侧栏/状态栏保持原尺寸，拖动时滑块不会被自己缩放。换档走静止阈值 0.05% 的临界阻尼弹簧，
  与 `FontScale` 相比是两件事、可叠加；比例写入 `UiSettings.PageZoom`，重启保持。
- 模组元数据中文化全链路（CKAN × UniversalI18n）：`i18n/tools/ckan_i18n_pipeline.py` 统一编排「抽取 → 翻译 → 校验 → 同步」四阶段，
  路径零硬编码（`--ui18n-root` → `UNIVERSAL_I18N_ROOT` → 仓库内 `external/UniversalI18n` 联接），退出码分级（0/10/11/12/13/14/20/21）；
  `--check` 是 15 项零网络门禁（官方向量、pool 解析/哈希/键形状、包哈希/归一化/同包 `h` 唯一/`k` 形状/`k` 孤儿/`k` 哈希对齐、清单一致、运行时 sha256、覆盖率缺口），
  `--strict` 把告警当失败，`--selftest` 47 项自证；`--stage sync` 走 `.tmp` → sha256 → `os.replace` 原子替换，用户覆盖层只增不减。
- 包行新增可选稳定键 `k`（`ckan:<identifier>:<field>`，形状 `^[a-z][a-z0-9-]*:[^\s]+$`、长度 ≤200）：
  `api.py` 校验形状并允许同一 `k` 在同包多行（上游原文改版后的历史译文并存，`stats.dupKeyRows` 只计数不报错），
  `entry put --key` / `entry put-batch --stdin` / `mod put` 全通路透传；读取侧 `DictEntry.Key` + `ModTextProvider.BuildKeyIndex` 以 `k` 优先建索引（同键人工 > 机翻、同类后写胜），
  无 `k` 的历史行回落「`n` 形如 `ckan:`」的旧约定。
- `CKANMeta` 升为 UniversalI18n 一等包（98 包 / 19837 行）：真实 LLM 跑满 4807 条元数据译文，覆盖 4833 条候选的 99.46%
  （余 26 条：缩写/纯 URL/已是中文等前置跳过，另有 6 条术语门禁拒收）；`packindex` 覆盖率报告新增「CKAN 元数据」一节。
- 模组详情面板接上译文：`ModDetailsViewModel` 的摘要/描述先查 `IModTextService.Provider.Find(identifier, "abstract"|"description", 原文)`，
  命中用译文、未命中回落英文原文（无 Provider、原文空白、标识为空时与接线前逐字节一致）；命中挂 `CkanBadge Success`「已译」，
  原文改版后译文仍是旧版时挂 `CkanBadge Warning`「译文对应的是旧版原文，英文内容已更新」，徽标文案在 VM 内、颜色走主题 token。
- 启动预热语言包：此前全仓唯一调用 `ReloadPacksAsync` 的是 i18n 页面，用户不点开那一页模组详情永远英文；
  `App.StartAsync` 在 bootstrap 完成后后台预热一次（失败只降级为英文原文，不挡首帧）——运行时目录实测一次载入 4807 条条目。
- 模组翻译页（i18n）重做信息密度与筛选：条目从「三行卡」改为单行表格行（`标识 + 字段` / `原文` / `译文` / `类型` 徽标，200 / * / * / 96 固定列 + 表头 + 6,2 行内边距），同屏可见条目数由 3 条提升到 17 条以上；
  左栏新增「按模组查看」分组导航（按 CKAN identifier 归堆、条目数降序，首项「全部模组」，没有 identifier 的老数据回落语言包 mod 名），点一下只看该模组的词条；
  筛选条保留字段 / 类型两个下拉（候选项按条目数降序、空值归「无字段」，选项随语言包刷新重建），搜索范围扩展到 `Mod`、`Field` 与 `Context`，
  「清除筛选」按是否有活动筛选显隐（分组也是筛选维度之一），页脚在有筛选时显示「已显示 N / M 条 · 已筛选」；
  条目列表加 `MinHeight`、行加 `MinHeight=22`，视图代码后置对「列表零高度视口导致虚拟化一行都不实现」做最多 3 次布局自愈。
  筛选逻辑全部在 `ModTextPageViewModel`，视图只绑定；`tests/CkanUI.App.Tests/ModTextPageDensityTests.cs` 用无头渲染断言行高 < 36 px 并留档快照，真实 4807 行运行包实测单行渲染、首行容器高 30 px。
- 语言包同步扩到**全量语料**：`ckan_i18n_pipeline.py --stage sync` 从只搬 `CKANMeta.jsonl` 改为搬 `packs/<lang>/*.jsonl` 全部语言包
  （新增 `--langs`，默认 `all` 覆盖所有语言目录），游戏内 modui / 部件描述等译文因此进入 ckan-ui；
  运行时目录实测 98 个包 / 19837 行 / 6.9 MB，i18n 页一次载入 19837 条（204 ms）、按模组归堆 3730 组。
  `--check` 新增 `runtime.packs`（运行时语言目录缺包即 warn，`--strict` 判红）；`--selftest` 51 项（+4：其余语言包一并搬运、全集同步 exit 0、缺包可见、`--langs` 无匹配目录 exit 13）。
- i18n 页配合全量语料：游戏内语言包没有稳定键 `k`，条目首列改为 `DisplayKey`（identifier → mod 名 → 上下文）回落，否则整列空白；
  左栏分组栏新增「搜索模组」过滤框（`GroupFilterQuery`，首项「全部模组」常驻，选中组被过滤掉时回落「全部」并重跑筛选）；
  文件清单卡改为最高 96 px 的滚动区——99 个包会把条目列表整个挤出视口（无头实测列表 `bounds.Y = -59`）。
- 模组翻译页按**范围**拆分 + 二次提密度：筛选条改成三个显式按钮（全部范围 / CKAN 元数据 / 模组翻译，选中态用 `CkanPrimary`、未选中 `CkanGhost`，
  由 `IsScopeAll / IsScopeMetadata / IsScopeMods` 驱动、`SelectScopeCommand` 切换；下拉框藏得太深，用户找不到"只看元数据"），
  元数据条目按稳定键 `k` 以 `ckan:` 前缀或 `mod = CKANMeta` 判定，两个范围各自分组、各自计数，切换范围时清空分组选中避免筛空；
  筛选条外层换成 `WrapPanel`（窄窗口自动换行，否则三个按钮会把字段/类型下拉挤出卡片，无头实测 416 px 宽时换成两行）；
  工具栏新增「覆盖编辑」开关（隐藏右栏后列表宽度 416 → 796 px），文件清单卡折叠为 `Expander`、左栏分组 220 → 200 px、右栏 400 → 380 px；
  行高 30 → 22 px（`MinHeight=18` + `Padding=6,0`），同屏行数 11 → 21；列宽由固定 `200,*,*,96` 改为 `2.5*,3*,3*,Auto`，字段移入首列悬停提示（`KeyTooltip`）；
  「类型」徽标只在非默认来源（人工覆盖 / 保留 / 非空 `src`）时显示，整批都是默认词典条目时连表头一起隐藏（`ShowsSourceBadgeColumn`）；
  无头实测（1052×814，800 条假数据）：左栏 200 + 分组、列表 416×434（筛选条两行）/ 416×476（单行）、首行容器高 22 px、分组栏在元数据范围下 401 组、模组翻译范围下 2 组。
- `--stage sync --dest <实例>/GameData/UI18n/Packs` 打通「装进游戏实例」：游戏侧只认 `GameData/UI18n/Packs/<lang>/*.jsonl`
  （`UI18n.Runtime/Update/UpdateWorkflow.cs:261`），与 ckan-ui 自己的运行时目录是两份；实测把实例从 99 包补到 100 包
  （多出 `CKANMeta.jsonl` 4807 行 / 1,780,151 字节），`--check --dest <实例>` 16 项全绿，同步逐文件原子替换、覆盖层只增不减。

### 变更

- 安装与出包体验重做：安装程序改为原生 Win32 图形向导，出包多了一条「双击即出」的入口。
  - `CkanUISetup.exe` 改用 Native AOT 编译（不再是自包含 CLR）：引导内核 36 MB → 1.95 MB，从「自带运行时」变成「零前置依赖、毫秒级起来」。
  - 新增五步安装向导（欢迎 → 目标目录与磁盘空间校验 → 桌面/开始菜单/应用路径选项 → 带当前文件名的实时进度条 → 完成并可立即启动）与三步卸载向导（含「是否保留配置与缓存」），高分屏按 PerMonitorV2 感知；`-Quiet`/`/S`/`-InstallDir`/`-Uninstall` 等静默参数保持不变。
  - 出包脚本剔除 12 MB 用不上的非 Windows 与调试组件（`Avalonia.X11/Metal/Native/FreeDesktop`、`createdump`、`mscordbi`、`mscordaccore*`、`clrgcexp`、原文 XML 文档），zip 走 7-Zip 极限 Deflate（`-mx=9`）；安装包 85.7 MB → 51.8 MB（`-NoReadyToRun` 档）。
  - 新增一键出包入口 `tools/release/build-setup.cmd`（逻辑在 `Build-Setup.ps1`）：先验 .NET SDK 与 Native AOT 需要的 MSVC 链接器，再透传调用 `release.ps1`，收工用 `CkanUISetup.exe /?` 真跑一次自检并列出产物体积。
- 主程序名统一为 `CkanUI.exe`（程序集名 `CkanUI`），与文档里的写法一致；Avalonia 资源 URI 同步改为
  `avares://CkanUI/…`，发布包里不再有 `CkanUI.App.exe`。
- 导航栏新增「日志」「Mod 配置」两个一级入口（共 19 项），并补对应字形。
- 「模组翻译」从「模组」的第 6 个页签拆出来，独立成侧栏一级入口（紧跟「模组」，单页签时不再显示页签条）；导航项总数仍为 19，命令面板与深链的页键 `modtext` 不变。
- 全局字体回退链：`Inter → 微软雅黑 UI → 微软雅黑 → 宋体 → Yu Gothic UI → Meiryo → Malgun Gothic → Segoe UI`，中文与日文界面不再出现豆腐块。
- 启动时恢复上次选择的界面语言（此前保存了但重启后不生效）；设置页语言下拉改为按磁盘上真实存在的语言包生成，不再列出没有语言包的语言。
- 「关于」对话框改为分别声明 CKAN UI 自身（GPL-3.0）与内置 CKAN 核心库（上游 MIT）的许可，第三个链接指向第三方组件清单。
- 设计系统升级：新增海拔四档、焦点环、悬停/按下覆盖、骨架屏等语义 token，按钮与文本层级样式家族，页签下划线动效、细滚动条、统一的禁用态透明度；海拔阴影由单层改为「漫射 + 主光」双层（浅色与深色各一套，高对比度仍是实线边框）。
- 外壳交互：侧栏可折叠（Ctrl+B）、后退/前进（Alt+← / Alt+→）、状态栏显示版本号、页面切换淡入、对话框入场动画、忙碌遮罩与骨架屏。
- 新增「当前页有未保存改动」的底部提示条，跟随设置、模组包编辑、Mod 配置与模组变更清单四处的脏状态显隐。
- 8 个高流量页面按新样式重写：仓库、模组（含搜索面板）、内容、缓存、下载统计、去重、安装历史、存档。
- 页面运行期文案接入本地化：页面 ViewModel 里拼出来的标题、状态栏、空态、校验反馈、对话框正文改走 `Text(中文原文, 键)`，有译文取译文、没译文回落中文原文；17 个页面的标题与正文共 852 处已迁移（设置页 197、实例页 105、模组页 71、安装历史 62、未托管 55、缓存 51、变更 43、整合包 42、仓库 38、存档 38、去重 32、模组翻译 26、插件 20、日志 18、整合包编辑器 18、内容 15、时长 12、下载统计 9），文案键 76 → 930，中英两套包严格同步，并由架构测试钉住「代码 fallback 与 zh-CN 译文逐字一致」。排障用的诊断码、日志上下文与字段名刻意不翻译——它们要能被 grep 到；子 VM 与静态格式化器（`ChangesFormatter`、`ModPackEditorText`、`LogGlyphs` 等）留下轮。

### 修复

- 存档工作台在 CRLF 存档里搜到的东西点不开：原文命中那一支按 CR 与 LF 两个字符切行，每个 CRLF 因此
  多出一个空元素，行号整体翻倍（20 行变成 39 行），于是"搜索命中的行"和文档行号对不上——点命中落到
  错误的块上，或者干脆展开不出字段。Windows 上的 .sfs 全是 CRLF，这条对真实用户是必现的。改成先把行尾
  统一再按单个换行切分。回归用例：`SfsCrlfRegressionTests.原文命中的行号在CRLF存档里仍然是真实行号`。
- 插件启停卡顿之外的同类边角：`TelemetryFixtures` 读契约样例时统一行尾（样例是 JSON，签出时的行尾由
  `.gitattributes` 的 `* text=auto` 决定），避免换台机器重新签出后按 `\n` 拼的替换片段命不中而假红。
  回归用例：`TelemetryFixtureLineEndingTests.样例读取后统一成LF`。
- 启停插件时界面会卡住（激活/停用一次，界面几秒钟不响应）：这条链上有两处把活压在 UI 线程上。
  一是分节入口项每轮重建都造新实例，而侧栏按引用差分——激活一个「实例」分节里的插件，五个分节入口全部
  「变成新项」，外壳（逐项销毁重建整套菜单）在同一个调度回合里重建十几次；现在标题与字形没变就复用上一轮实例，
  一次启停只动真正变化的那一项。二是 `PluginService` 的激活/停用本来是同步实现（`Initialize` / `Deinitialize`、
  写激活表、广播变更全在一条调用栈里），页面命令直接 `await` 它等于在 UI 线程上跑完；现在命令把服务调用丢到
  线程池、`IsBusy` 先上屏，变更通知也回到 UI 线程收口（插件页缓存的释放、导航栏重建都不再跨线程）。
  开关另加防连击（忙碌时把勾选回滚），避免手抖连点堆命令。
  回归用例：`PluginPageCatalogTests.Shell_TogglingSectionEmbeddedPlugin_DoesNotTouchTheRail`（分节内嵌插件启停对
  `RailItems` 零变更，改回旧写法会红在「实际 12 次」）、`PluginPageCatalogTests.Shell_TogglingRailPlugin_TouchesTheRailExactlyOncePerToggle`、
  `PluginToggleResponsivenessTests.启停插件在无头宿主里能跑完且侧栏只做最小重建`（无头宿主里真插件走完激活→停用，
  限时且限制重建次数）、`PluginsPageViewModelTests.Activate_RunsTheServiceOffTheThreadThatOwnsTheDispatcher` /
  `Deactivate_RunsTheServiceOffTheThreadThatOwnsTheDispatcher`（用手动泵的同步上下文模拟界面线程，服务体必须在别的线程上跑）。
- 从一个选项（A）切到另一个（B）时，A 会「又亮一下」：行的底色挂了 120–200 ms 的 `BrushTransition`，
  选中色退场要跑完整段过渡，旧行在那段时间里一直带着选中色。现在 `ListBoxItem` / `DataGridRow` 的底色瞬时切换，
  位移弹簧仍挂在模板内的 `ContentPresenter` 上；键盘焦点只画焦点环、不再重绘整行底色（否则焦点迁移也像一次选中闪烁）。
  回归用例：`RowSelectionFlashTests.MovingTheSelectionAwayFromARow_LeavesNoLingeringHighlight`、
  `RowSelectionFlashTests.RowContainers_DoNotAnimateTheirBackground`、
  `HoverStyleContractTests.RowSelection_SwapsInstantly_WithoutAFadingBackground`、
  `HoverStyleContractTests.KeyboardFocus_DoesNotRepaintTheRowBackground`（后两个读样式表，去掉过渡后单测会红）。
- 指针停在列表行的边缘时，行会在「选中 / 未选中」之间疯狂闪烁：悬停的 2px 位移原本挂在
  `ListBoxItem` 自己身上，而 `RenderTransform` 参与命中测试——行一右移，停在左缘的指针就掉出包围盒，
  悬停态消失、行弹回、指针又落回行内，每帧来回切换。位移现在下沉到模板内的 `ContentPresenter`
  （行的包围盒原地不动），表格行 `DataGridRow` 同样处理；卡片与引导卡不再整体上浮 2px，抬升感交给阴影，
  指针压住卡片下缘也不会再抖。回归用例：`HoverStyleContractTests.HoverEffects_DoNotMoveTheControlThatOwnsThePointer`、
  `HoverStyleContractTests.RowHoverSlide_LivesOnTheTemplatePresenter_SoTheRowKeepsItsGeometry`、
  `HoverInteractionTests.ListBoxItem_HoverMovesItsPresenterNotItself_SoTheHitBoxStaysPut`。
- 启动游戏前的那次 L1「释放页面投影」会把当前正显示着的列表也清空（模组翻译 19837 条词条、模组表、内容树都中招）：
  释放后不会再有 `OnActivated`，用户看到的是一张只剩表头的空列表，而状态栏还挂着「载入完成，共 N 条词条」——
  启动失败时尤其刺眼。现在 `TrimProjection()` 在页面 `IsActive` 时直接返回 0（离屏页面照常释放，
  内存目标不变），`ModsProjectionTrimTests`/`ModTextPageViewModelTests` 各加一条「在屏页面不释放」的用例。
- 滚动条在静止时是「看不见也拖不到」的：全局样式把 `ScrollBar` 的不透明度设成 0，只有指针悬停才变 1，
  而 FluentAvalonia 的模板还会在收起态把 thumb 压成 `scaleX(0.35)`（8px → 2.8px 的一条线）。
  现在滚动条常驻 0.7 不透明度、指针上去加亮到 1 且 thumb 转强调色，模板的收缩变换被取消（thumb 满宽 10px），
  并加 `Ui_Common_ScrollBar_Tip` 悬停提示；`tests/CkanUI.App.Tests/ScrollBarAffordanceTests.cs` 钉住「静止可见、悬停加亮」。
- 快速拖滚动条时日志列表整段空白：虚拟化列表在大跳转时来不及回收/新建行容器，视口里会夹着视口外的孤儿容器。
  日志行有硬上限（`LogPageViewModel.RowLimit = 500`）且行高固定 20px，改成常驻 `StackPanel` 全量实现，
  `EveryLogRowIsRealized_SoAFastScrollbarDragCannotLeaveHoles` 断言 500 行全部 realized。
- 页面缩放只缩版面、不缩字：缩放矩阵被写在 `PageHost` 上，而页面动效的 `TransformSpring` 一接管就把
  `PageHost.RenderTransform` 改写成 identity —— 矩阵丢了，只剩「按视口/scale 重新测量」这一步，
  表现出来就是卡片变窄、文字和图标还是原尺寸（150% 与 100% 的字宽逐像素相同）。
  现在 `LayoutTransformControl` 与 `PageHost` 之间夹一层没人碰的 `PageZoomSurface`，矩阵落在这一层；
  并补 `PageZoom_MatrixLandsOnTheSurfacePanel_SoTextAndIconsActuallyGrow` 锁住「矩阵在谁身上」这个判据。
- 页面缩放从 100% 往上调时页面纹丝不动：首帧弹簧值还在 1.0，100% 摘变换的分支把刚建好的变换和弹簧一起置空，
  下一帧又重新建、再摘 —— 每秒 60 次原地空转。摘变换改为「目标也是 100%」才执行，从 100% 拖到 150% 全程连续放大；
  `tests/CkanUI.App.Tests/TitleBarLayoutTests.cs` 逐档断言变换不消失（10 档 × 6 帧）。
- 带 UTF-8 BOM 的 `ckan-ui.json` 会让全部界面设置静默回落默认值：设置按字节反序列化，`0xEF` 是非法起始字符，
  整份 JSON 抛异常后被当成「文件不可读」吞掉 —— 用户手改或用 PowerShell 5.1 / 记事本另存一次，缩放比例、主题、列宽就一起丢。
  读取前统一剥掉 BOM，并补 `Load_FileWithUtf8Bom_StillReadsTheUsersSettings` 回归。
- 日志列表的阅读顺序与滚动：改为时间正序（最新的在最下面），跟随末尾时自动贴住最新一行；后台每来一条日志只追加尾部增量，
  不再整表 Clear 重建 —— 之前每次刷新都会让虚拟化列表发出 Reset，把滚动位置顶回顶部、行容器来不及重建，屏幕上就剩一大片空白。
  行高同时从 26px 压到 20px，一屏多显示近三成日志。快速拖动滚动条后残留的半透明「残影行」另有根因：
  页面缩放承载区的恒等 `ScaleTransform`（100% 时也挂着）把整页变成一层合成层，合成器来不及失效就留下旧内容；
  现在 100% 直接摘掉变换回到直绘，日志行也不再给悬停挂弹簧 `RenderTransform` 过渡。
- 页面缩放滑块的拖动体验：此前每挪一格都同步落盘一次（每格一次 fsync），拖动全程把 UI 线程按在磁盘上，
  写失败时还会一格一条「保存 UI 设置失败」把日志抽屉刷满。改为手势内只改内存、松开滑块合并写一次；
  设置写入对瞬时占用重试一次，同类失败 30 秒内只记一条。
- 底栏缩放控件排版：FluentAvalonia 的 Slider 模板给刻度留了上下各 14px 的固定行（默认要求 32px 高），
  硬压进 28px 状态栏后滑轨被挤到中线以下约 10px、18px 的滑块被窗口底边裁掉一角；改为只对该滑块局部覆盖
  （去掉预留行、轨道与滑块收到 12px），滑轨、加减按钮与读数回到同一条水平中线，两侧间距对称。
- 游玩时长：重启后手动基线与自动累计不再互相覆盖，崩溃遗留的会话在下次启动时丢弃而不是计入时长。
- 安装历史：新增「删除选中」「清空历史」入口（含二次确认），按时间点成对删除条目与快照行，不可解析的行保留。
- 旧版日志导入：保留毫秒字段，同秒内的多行不再被抹平顺序。
- 未托管文件扫描：跳过目录联接与符号链接，采纳与删除都不会跨到游戏目录之外。
- 模组详情四页签此前没有任何宿主、更新检查弹窗在生产里从不出现，现均接通（详情挂进模组页右侧、弹窗由设置页经宿主窗口弹出）。
- 命令面板的 31 个本地化键常量命名不合「常量名 = 键去掉 Ui_ 前缀」的校验规则，已改名并同步两套语言包。
- 窗口无法拖动：客户区铺到标题栏后系统不再代管标题栏，顶部 34px 拖拽条与侧栏品牌区改为自己发起移动（同一处理器支持双击最大化/还原，最大化时拖动先还原）；新增架构用例锁住这段接线。
- 文档账本对账：`docs/design/milestones.md` 里早已交付的「M8–M14 未开工」与 1841 例测试基线改成实测值
  （3110 例）；`docs/design/feature-matrix-verification.md` 的「证据不足」表移出 F-08/F-26/F-30/F-52 四条已补齐项、
  其余行逐条复核；`docs/design/feature-matrix.md` 的「55/55」与 milestones 的已知残留清单两个口径互相声明，
  不再读起来像矛盾。
- `adapter.json` 的 JSON Schema 收成单一真源：删掉 `tools/verify-layout/adapter.schema.json` 旧副本，H-08 只读
  `docs/design/adapter.schema.json`，真源缺失时明确报错而不是静默跳过。

## [0.1.0] - 2026-10-29

首个可运行预览版：界面外壳、模组管理主流程与打包链路可用，但未做完整性打磨，
不建议在主力 KSP 存档上直接使用。

### 新增

- 界面外壳（Avalonia + FluentAvalonia + MVVM）：导航栏、页面宿主、对话框服务、崩溃对话框、
  单实例守卫与外部参数转发。
- 功能页面：模组列表、内容、变更集、仓库、插件、缓存、去重、下载统计、安装历史、
  未托管文件、实例、存档、ModPack 编辑器、Mod 文本、设置、游玩时长。
  其中部分页面当前只有界面与视图模型骨架，行为仍在补齐（见 `docs/plan/06-wbs-milestones.md`）。
- 对话框：关于、确认、错误、诊断、崩溃、Mod 详情、兼容版本、安装过滤器、标签编辑、
  首选主机、下载错误、冲突详情、推荐、审核建议、残留清理、选择、文本输入、更新可用、虚拟模块。
- CKAN Core 复用：模组数据、仓库与安装逻辑仍由 CKAN Core 提供，界面层不重复实现。
- 日志：log4net 滚动日志（`log4net.xml` 随包分发），配置目录不可写时自动退回程序目录。
- 国际化：内置 `i18n/packs/en-US.json` 与 `zh-CN.json` 两套界面文案，磁盘语言包优先于内嵌版本。
- 打包（M6）：
  - Windows x64 双发布配置：自包含（自带 .NET 运行时）与框架依赖（需 .NET 10 Desktop Runtime）。
  - 两种配置都开 PublishReadyToRun（实测冷启动 1.58 s → 0.81 s），都不做裁剪（XAML 绑定与反射不是 trim-safe）。
  - 应用图标内嵌 exe，便携 zip 根目录即程序目录，含 `LICENSE.md`（本项目 GPL-3.0 全文）、`README.txt`、`THIRD-PARTY-NOTICES.txt`、`licenses/CKAN-LICENSE.md`（上游 MIT）。
  - 一条命令出包：`tools/release/release.ps1` 产出两个 zip + `SHA256SUMS.txt` + `manifest.json`（版本、提交、构建参数、逐包哈希、冒烟结果）。
  - 版本号写入程序集与「关于」对话框：`v0.1.0 (26.g4a888a5)` 形式，提交数+短哈希可追溯构建来源。
- 工程内建：三平台 CI（构建 + 测试）、`tools/verify-layout` 仓库卫生门禁 12 条、统一测试运行器。

### 修复

- 测试宿主 `tests/CkanUI.TestRunner`：`--config <自定义配置>` 的配置名不再被当成程序集过滤词。此前只硬编码跳过 `Debug`/`Release`，用 `-c Verify` 绕开被占用的 Debug 输出目录（构建/测试并行时的常见情形）会把 `Verify` 当过滤词，一个测试程序集都匹配不上，报「没有可运行的测试程序集」。

### 已知问题

- **未签名**：仓库里没有代码签名证书，Windows SmartScreen 仍会提示"未知发布者"。签名通道已就绪
  （`release.ps1 -PfxPath / -Thumbprint / -SignDryRun` + `tools/release/Sign-Artifacts.ps1`），拿到证书即可出
  带时间戳的签名包；默认包名照旧带 `-unsigned`，分发时必须同时给出 `SHA256SUMS.txt` 的值。
- **体积**：自包含包解压 119 MB（`-NoReadyToRun`）/ 152 MB（默认 R2R），下载包 49.7 MB / 63.7 MB。
  解压体积的地板由 .NET 运行时决定（约 95 MB）加 SkiaSharp / HarfBuzzSharp 原生库，因此 09 篇 §11 的
  「解压 60–90 MB」只有框架依赖版（解压 42 MB、zip 15.7 MB，需 .NET 10 Desktop Runtime）能满足；
  两种口径与取舍写在 `docs/ops/release.md` §3.5 与 `docs/plan/09-migration-packaging.md` §14。
- **安装器**：交付的是随包的 PowerShell 安装脚本（per-user、不需要管理员）与 Inno Setup 模板；模板未在
  本机实测构建（环境里没有 Inno Setup），未签名的 `Setup.exe` 不分发。
- 未做 macOS / Linux 冒烟（M6 范围外）。
