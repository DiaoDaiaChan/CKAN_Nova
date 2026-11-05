# 变更记录

本文件记录 CKAN UI 面向用户的变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)：主版本对应"能不能替换旧版"，
次版本对应"新增了什么"，修订号对应"修了什么"。

CKAN UI 的版本号只有一个手改点：`src/CkanUI.App/Version.props` 的 `CkanUiVersion`。
发版时改那一行，再在本文件顶部加一段。绑定的 CKAN Core 版本不体现在本文件名里，
在「关于」对话框里看（两者独立演进，混在一起会让人误以为换了界面就该换 Core）。

## [未发布]

### 新增

- 全键盘与手柄操作（`src/CkanUI.App/Input/` + `src/CkanUI.Platform/Input/XInputGamepadSource.cs` + `src/CkanUI.Services/Input/GamepadNavigator.cs`）：从「几张页面各自处理几个键」升级为「一张映射表 + 一条派发通路 + 一套可测状态机」，键盘与手柄共用同一批语义动作。
  - 外壳快捷键（`ShellShortcutMap`，键位只在这一处定义）：`Ctrl+Tab` / `Ctrl+PageDown` 下一个页签、`Ctrl+Shift+Tab` / `Ctrl+PageUp` 上一个页签（首尾环绕，单页分节不响应）、`Ctrl+Alt+↑↓` 切侧栏入口、`Ctrl+1…9`（含小键盘）直达侧栏第 N 项、`Ctrl+,` 打开设置、`Ctrl+F` 聚焦当前页搜索框。
  - `Ctrl+F` 走冒泡阶段而不是全局抢占：模组页自己的 `Ctrl+F`（聚焦并全选搜索框）先执行，页面没接住才由外壳兜底——不抢页面已有的键。
  - 命令面板补 6 条动作（next-tab / previous-tab / next-rail / previous-rail / focus-search / open-settings），点击与按键都进 `ApplyShellShortcut`，改键位不会出现两套语义；`Ctrl+/` 快捷键目录新增「手柄」分组，条目直接由 `GamepadBindings` 驱动，绑定表改了目录自动跟着改。
  - 手柄：`XInputGamepadSource` 用 `NativeLibrary.TryLoad` 依次试 `xinput1_4` / `xinput9_1_0` / `xinput1_3`（不用 `DllImport`——目标框架是跨平台 `net10.0`，静态绑定会在非 Windows 上启动即炸），`GamepadInputHost` 以 30 Hz 轮询；`GamepadNavigator` 是纯状态机：按键只认按下沿，方向按住连发（首延迟 350 ms、间隔 120 ms、死区 0.5），扳机当翻页键（阈值 0.6，也只认按下沿），断连清空按住态。
  - 手柄动作全部复用键盘路径：方向 → `FocusManager.TryMoveFocus`，`A` → 合成一次 `Enter`，`B` → 与 `Esc` 相同的关闭顺序（引导气泡 → AI 抽屉 → 命令面板）。没有手柄时轮询不启动，键盘路径零影响。
  - 焦点环补齐 `ToggleButton` / `CheckBox` / `RadioButton` / `Slider` / `DataGridCell`（原只有按钮、文本框、下拉框、列表项、数据行），手柄和键盘走到哪都看得见。
  - 测试：`GamepadNavigatorTests` 11 条（伪造时钟精确断言首延迟与连发节奏）、`ShellNavigationCycleTests` 9 条（页签/侧栏环绕、单页不响应、下标越界）、`ShellShortcutMapTests` 18 条（键位映射与不该命中的组合）、语言包 7 条（键集一致、占位符一致、100% 覆盖）。
- 共享图标与曲线控件（`src/CkanUI.Ui` + `src/CkanUI.Plugin.Performance` + `src/CkanUI.Plugin.Rp1`）：页内图标、多序列曲线与数据展板统一到共享层，插件不再各画各的。
  - 图标：`src/CkanUI.Ui/Icons/GlyphCatalog.cs` 收 22 个 24×24 Material 路径（`Speed`/`Timer`/`Percent`/`Pulse`/`Cpu`/`Memory`/`Gpu`/`Chart`/`Curve`/`Money`/`Science`/`Confidence`/`Reputation`/`Contract`/`Milestone`/`Program`/`Crew`/`Calendar`/`Clock`/`Warning`/`Check`/`Bug`），
    `GlyphIcon` 控件按 `Size` 等比缩放后画几何，解析结果进缓存（同一个名字多次 `Resolve` 返回同一个 `Geometry` 实例）。**认不出的字形解析为 null、不画占位图**——
    与导航栏「未知字形退回 Puzzle」的策略相反：导航栏留空位会让布局错位，页内猜错则是错误暗示，空着比猜错好。
  - 曲线：`ChartSeries(Name, Values, Stroke, Fill, Dashed)` 描述一条序列；`CurveMath` 是纯函数（`ResolveRange` 归并多序列值域、`Project` 把值映射成点），
    null/NaN/∞ 一律断线不补 0，全平数据给 ±5% 的可见带；`CurveChart` 控件负责网格、图例、悬停十字线与最近点读数、面积填充与 6 色调色板，
    `IncludeZero`/`MinY`/`MaxY`/`ValueFormat`/`ValueSuffix`/`YAxisLabelWidth` 可按页调。
  - 性能页（`src/CkanUI.Plugin.Performance/FrameSeriesBuilder.cs`）：把 `timeline.frames` 摊成「帧耗时（ms）」与「帧率（fps）」两条序列，
    **量纲不同分两张图、不共用一根 Y 轴**；指标卡补 `Speed`/`Timer`/`Percent`/`Pulse`/`Chart`，耗时分解行补 `Cpu`/`Program`/`Pulse`/`Gpu`/`Curve`/`Memory`。
    没有 `timeline.frames` 时给「时间线不可用（latest.json 无 timeline.frames），不画曲线」，有帧但 `frameMs`/`fps` 全空时另给一句「不画曲线」——都不补 0、不外推。
  - RP-1 页（`src/CkanUI.Plugin.Rp1/UI/CareerSeriesBuilder.cs`）：生涯曲线画**全部**预算周期（列表仍只显示最近 12 期，页脚口径改成「曲线画的是全部周期」），
    资金/科技/信心/声望四指标可切（`CareerMetricOptions` + `SelectCareerMetric`，后缀在「 资金 / 点 / 空」之间换）；8 块统计卡补 `Contract`/`Milestone`/`Program`/`Crew`/`Money`/`Science`/`Confidence`/`Reputation`。
    某一期缺这个数值时保持 null（断线），不写 0。
  - 测试：`CurveMathTests` 13 条、`GlyphCatalogTests` 6 条、`FrameSeriesBuilderTests` 8 条、`CareerSeriesBuilderTests` 7 条，外加两插件各一条无头视图冒烟——
    性能页断言两张曲线已绑定且 `Bounds` 有尺寸、无时间线时可见曲线为 0 而说明文案出现；RP-1 页断言曲线画 15 期而列表 12 条、切指标后量纲跟着换、8 个图标全部可解析。
    `CkanUI.Ui.Tests` 25/25、`CkanUI.Plugin.Performance.Tests` 42/42、`CkanUI.Plugin.Rp1.Tests` 30/30；干净签出全量 21 套件 4349 条，仅 1 条既有 hover 红（与本次无关，基线提交同样红）。
  - 字形用例的渲染平台判定改成解析探针（`Geometry.Parse("M0 0L1 1")` 成功即说明几何实现就位），不再断言 `HeadlessHost.CanCreateControls`：
    全量套件里无头壳由别的测试程序集在另一线程建好，窗口亲和性在本线程必然为 false，7 条用例曾因此误红。
- 实例配置档与存储管理（`src/CkanUI.Plugin.ModPackShare` + `src/CkanUI.Plugin.Storage` + 服务层 `src/CkanUI.Services/Profiles`）：不复制实例，用目录换位在多套整合包之间切换。
  - 配置档布局：`<GameDir>/CKANProfiles/<名字>/{GameData,CKAN,modpack.ckan}` + `profiles.json`；当前生效那档回到游戏根目录。切换 = 校验 → 查游戏是否在跑 → 关注册表 → live 移进旧档槽位 → 目标档移回 live → 写清单 → 刷新，失败按反序回滚。
  - 「整合包分享」插件（`com.ckanui.modpackshare`，页 `modpack-profiles`，模组包分节 `Order=10`）：从 GitHub 仓库或本地 `.ckan` 导入、新建并接管当前状态、切换、删除、设为启动档；下载 20 s 超时 + 8 MiB 双闸 + 首字节必须 `{`，缓存按内容 SHA-256 前 16 位命名；写操作前现查游戏运行态，不用页面打开时的旧快照放行。
  - 「存储管理」插件（`com.ckanui.storage`，页 `storage`，数据分节 `Order=20`）：多实例占用总览（本体/配置档/存档分开计）、卷剩余空间、配置档明细、配置档之间硬链接去重（dry-run → 确认 → 执行）；缺数据显示「—」不用 0 冒充。
  - 启动时自动切到指定配置档：`InstancesPageViewModel` 先 `ApplyLaunchProfileAsync` 再拉进程，切换失败就不启动，错误文案点名是哪一步失败（`InstancesErrorText.ComposeStep`）。
  - 存储扫描的 active 档复用 live 字节数且不计入配置档合计，避免同一份文件算两遍；去重只扫 `CKANProfiles/*/GameData`，≥128 KiB、长度分组再 SHA-256、同卷判定走 `DedupVolume.SameVolume`。
  - 测试：服务层 49 条（40 配置档 + 9 存储）、整合包分享插件 34 条、存储插件 13 条、启动闸门 3 条、真实插件 dll + 宿主 DI 接线 1 条，另加两条分节页签接线（「模组包」补 `modpack-profiles`、「数据」补 `storage`，都不进侧栏）与两条真实 Avalonia 视图冒烟（XAML 解析、绑定落位、布局正尺寸）。设计与边界见 [instance-profiles.md](docs/design/instance-profiles.md)。
- 自定义主页部件（`HomePageViewModel` + `HomePageView`）：主页从写死的长图改成可增删、可拖动排序的部件板。
  - 内置三块（实例概览、快捷操作、最近截图）可以移除再添加；「添加部件」下拉只列还没在布局里的块，允许清空主页。
  - 另加五块宿主数据部件：模组更新（仓库快照）、安装历史、下载缓存、存档、后台任务（订阅队列变化，只重画这一块）。没有数据时给下一步说明，不用 0 冒充；读取失败显示错误文案，部件不整块消失。
  - 布局存进 `UiSettings.HomeWidgets`（`%LocalAppData%/CKAN/ckan-ui.json`）：`null` 表示没动过用默认三块，空数组表示用户明确清空；插件块用 `plugin:<插件 Id>:<部件键>` 存身份，插件停用后槽位保留。
  - 拖动把手按下后超过 8 px 才算拖动，用容器中点判断落在目标上半还是下半，松手才重排（拖动中重建 `ItemsControl` 会摘掉被捕获的把手，`PointerCaptureLost` 会中断拖动）。
  - 插件主页部件契约：`ICkanUiPlugin.HomeWidgets`（声明可添加）+ `GetHomeWidgetAsync`（按上下文给快照），两者都有默认实现，旧插件不用改；`IPluginService.ActiveHomeWidgets` 只列已激活插件，调用在锁外，异常转可重试失败。
  - RP-1 生涯日志是第一款部件（`com.ckanui.rp1` 的 `career`）：当前日期、资金、科技、声望、合同/里程碑计数与最近 5 条非财务事件；没有实例或没有存档时给可执行的下一步，不显示 0。
  - 测试：`HomePageViewModelTests` 14 条（默认布局/增删/上下移/拖放顺序/持久化/插件快照/停用保留槽位/五块数据部件可添加）、`HomeWidgetFeedTests` 7 条（空数据、计数、排序、上限与剩余、字节单位、运行中优先）、`HomeWidgetPluginServiceTests` 3 条、`Rp1PluginTests` 22 条、`HomePageViewWidgetTests` 4 条（真实 Avalonia 生成实体卡片、空态、插件模板、数据部件模板）。
- RP-1 生涯日志插件（`src/CkanUI.Plugin.Rp1`，`com.ckanui.rp1`）：独立侧栏入口「RP-1 生涯日志」页签（独立为单个 Tab，不落入工具分节），把当前实例存档里的生涯记录折成一条时间线。
  - 只读 `saves/<存档>/persistent.sfs`，合并 `RP1ContractTracker`（合同完成时间）、`CareerLog`（合同备用来源/预算周期/乘员损失）、`MilestoneHandler`（里程碑）、`ProgramHandler`（项目启动/完成）、`CrewHandler`（人员退役）。
  - 大文件不整树解析：先用流式扫描定位 `SCENARIO` 行区间，再只解析九个目标场景；当前资金/科技/声望/信心读 `Funding` 等场景，不用最后一条预算周期的 0 冒充。
  - 时间显示按 RP-1 的 1951-01-01 纪元与 365 天年（真实存档 UT=1,186,012,800 → 1988-08-01 00:00）；标题从 `GameData/RP-1` 的合同/里程碑/项目定义读取，映射不到就保留内部名。
  - 统一全景交互式时间线工作台（双面板合一）：
    - 架构合璧：彻底合并原本割裂的「左侧时间线列表」与「右侧统计/详情/趋势」双卡片，升级为单一全景交互式生涯工作台；
    - 顶部全景宏观走势与标尺：全宽 `CurveChart` 常驻工作台顶部，兼具生涯全景监控（资金/科技/信心/声望四指标快速切换）与宏观时间轴定位标尺功能，搭配 8 块微型统计胶囊；
    - 实时双向联动：在下方时间线点击任意事件时，顶部曲线的金色高亮针脚与标签即时滑动定位该期；点击顶部全宽曲线上任意点或周期列表行，下方时间轴同步无缝滚动并高亮事件；
    - 底部集成式工作区：通过标签页收拢「事件时间线与详情」（左侧纵向时间轴导轨与筛选胶囊 + 右侧详细叙事与当期指标）与「预算周期明细表」（全量周期列表与视窗切换），曲线在两页签间均常驻可见，避免反复切页。
  - 测试 32 条（日期/文本、存档切片、时间线合并、服务、插件入口、页面模型、交互时间线与曲线双向联动）+ 1 条真实 Avalonia 视图加载 + 1 条真实插件 dll + 宿主 DI 接线；`CurveChart` 增加高亮与点击测试（`CkanUI.Ui.Tests` 共 32 条全绿）。
- kOS 脚本插件（`src/CkanUI.Plugin.Kos`，`com.ckanui.kos`）：工具分节下的「kOS 脚本」页签，直接管理 kOS 的 Archive 卷。
  - 读写 KSP 根目录 `Ships/Script/*.ks`（kOS 官方文档明确 Archive 卷就是这些真实文件，不是打包格式）。左栏搜索与脚本列表（大小/修改时间/行数/`boot.ks` 标记），右栏纯文本编辑器。
  - 写盘安全：保存前备份 `<名>.bak-yyyyMMdd-HHmmss`，同目录临时文件 + 原子替换，UTF-8 无 BOM、按原换行还原；`expectedLastWriteUtc` 与磁盘相差超过 1 秒即拒绝覆盖（外部编辑器同时改）。
  - 管理动作：新建自动补 `.ks`、重命名不覆盖、删除先确认并保留备份；名字里的路径分隔符/冒号/`..` 一律拒绝。
  - 空态分四层：没选实例 / 没装 kOS / 装了但没有 `Ships/Script` / 目录存在但没有脚本；四层给不同下一步，缺数据不用 0 或示例脚本冒充。
  - 未保存保护：切换脚本、刷新、重新载入前挂起动作，页内确认条给「保存并切换 / 放弃修改 / 取消」，原稿不静默丢失。
  - 测试 70 条（45 条领域层 + 25 条页面层）+ 1 条真实 Avalonia 加载视图 + 1 条只注册 `IInstanceService` 的宿主 DI 接线。
- 音乐插件（`src/CkanUI.Plugin.Soundtrack`，`com.ckanui.soundtrack`）：工具分节下的「音乐」页签，管理 Soundtrack 的播放列表。
  - 读写 KSP 根目录 `Playlists/playlists.cfg`：左栏播放列表（启用开关、曲目数、场景摘要），右栏改名与 loop/shuffle/pauseOnGamePause/disableAfterPlay、曲目上移/下移/移除、从乐曲库搜索添加；缺失曲目如实标「缺失」。
  - 写盘安全：不认识的键与子节点原样保留、`enabled` 没写就不补、重复键后者覆盖；保存先备份 `.bak-yyyyMMdd-HHmmss` 再原子替换，UTF-8 无 BOM、换行沿用原文件。
  - 没有 `playlists.cfg` 时给路径与「新建播放列表并保存」入口；不做试听，也不改 `playWhen/playNext/Before/After`（保存时原样保留）。
  - 测试 65 条（44 条领域层 + 17 条页面层 + 3 条插件入口 + 1 条分节接线）。
- 插件功能微型视觉角标（页面与子页面统一标出）：
  - 架构数据层：`NavItem` 与 `NavTab` 契约增加 `IsPlugin` 字段，插件解析与分节装配时自动置位；`ShellViewModel` 暴露 `IsCurrentPagePlugin` 用于外壳顶层感知。
  - 子页面分节页签（`SectionTabsControl`）：在「实例」（存档/飞船/旗帜/加载图/KSP设置）与「模组」（Mod配置）等内嵌插件页签上，标题右侧呈现精致微型「插件」胶囊角标（`CkanPluginCornerTag`），激活时自适应主题反色，清晰辨识原生与插件功能。
  - 侧栏导航与悬浮卡片（`FANavigationView` / `NavPopout`）：插件级导航项配置 `FAInfoBadge` 强调点，悬浮卡片标题旁与子项列表同步标注插件角标，并在抽屉模式 ToolTip 中追加 `(插件)`。
  - 顶栏外壳联动：当前处于插件页面或插件子页面时，窗口标题右侧同步呈现微型插件角标。
- Mod 配置解耦为内置插件并增加通用配置读写支持（`src/CkanUI.Plugin.ModConfig`，`com.ckanui.modconfig`）：
  - 架构解耦：将原本硬编码在宿主中的「Mod 配置」整套视图与视图模型迁移至独立插件工程，声明 `Section=mods` 与 `Order=50`，无缝嵌回「模组」分节内部页签条，宿主侧移除死类型注册与预热绑定。
  - 通用 ConfigNode 配置模式：针对绝大多数没有专属适配器包的模组，新增通用配置扫描器（`GenericConfigScanner`）与编辑服务（`GenericConfigEditService`）。递归扫描当前游戏实例 `GameData` 下可配置的 `*.cfg` 文件（自动排除 ModuleManager 补丁及原版零件节点）。
  - 宽松扫描：保守模式只认 `/PluginData/`、常见配置文件名与标准根节点；一个都没扫到时自动用宽松模式再扫一次。宽松模式额外纳入自定义根节点、扁平键值文件与 `TUFX_PROFILE` / `EVE_CLOUDS` 这类视觉/行为配置，但仍排除零件、原版定义、ModuleManager 补丁与本地化目录；页面显示「另有 N 个可宽松查看」，避免把"没列出来"误读成"没有配置"（详见 `docs/design/mod-config-generic.md`）。
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

- 模组页新增「联动模组」：把能与内置 GUI 插件配合的模组识别出来，用户不用再靠猜「装 kOS 到底为了什么」。
  - 对应关系收在一张纯静态表 `src/CkanUI.ViewModels/Pages/Mods/ModsCompanionCatalog.cs`（`IsCompanion` / `TryGet` / `GetLinks`，标识符比较忽略大小写）：
    `kOS` → kOS 脚本（`GameData/kOS`）、`RP-1` → RP-1 生涯日志（`GameData/RP-1`）、`KSPPerformanceProfiler` → 性能（`GameData/KSPPerformanceProfiler`）、
    `Soundtrack` / `SoundtrackEditor` / `SoundtrackEditorForked` → 音乐（三种播放列表目录）。目录名来自插件源码里的硬编码常量，不猜。
  - 工具条新增「联动模组」开关（`CompanionOnly`）：只留清单内的模组，生效时带强调色指示点；`ModsListPipeline.Apply` 新增末位可选参数 `companionOnly`，
    不传时行为与改前逐字节一致（既有 10 处调用点无需改动）。开关计入 `HasActiveFilters`，「重置筛选」一并清掉。
  - 名称列新增「联动」徽章（`ShowCompanionBadge` / `CompanionTip`，悬停说明「与「kOS 脚本」联动：在「kOS 脚本」页里新建、重命名、删除与备份 Archive 脚本」），
    只对清单内的模组出现，普通模组零视觉噪音。
  - 详情面板新增联动卡（`HasCompanionLink` / `CompanionPluginName` / `CompanionBenefit` / `CompanionHint`）：说明这个模组点亮哪个插件、能做什么；
    未安装时给「推荐安装」口径，已安装则改为「可在「X」页里直接使用」。无联动的模组整张卡不渲染。
  - 打开「联动模组」不再依赖仓库快照：原来的实现只有过滤器 + 行徽章，仓库索引为空（首次启动、未刷新索引、离线）时表格 0 行，
    打开开关看到的是一片空白——清单本身是静态表，没有理由被索引状态拖累。现在多加一层**独立面板**（`ModsPageView.axaml` 主区 `Grid.Row="2"`、
    `IsVisible="{Binding CompanionOnly}"`），数据源是 VM 侧 `CompanionItems`（`ModsCompanionItemViewModel`，由 `RebuildCompanionItems()` 从 `ModsCompanionCatalog.All` 投影，
    再按仓库快照标注状态），索引为空时也照常列出六个模组，状态显示「仓库索引未加载」。
  - 面板每行给三样东西：模组标识 + 对应插件徽章（如 `kOS` + 「kOS 脚本」）+ 收益一句话；状态分四档「已安装 / 可安装 / 不在 CKAN 仓库 / 仓库索引未加载」；
    可安装的行右侧带「加入变更集」按钮（`AddCompanionToChangesCommand`），点了直接把该模组塞进变更集并在底部状态栏说明点亮了哪个插件页。
  - 面板与表格互斥：新增 `ShowModsTable => !ShowEmptyState && !CompanionOnly` 与 `ShowEmptyStateCard => ShowEmptyState && !CompanionOnly`，
    否则空态卡会透在面板下面；面板头有「共 N 个 · 可装 N 个 · 已装 N 个」摘要与关闭按钮（`CloseCompanionPanelCommand`）。
  - 新增语言包键 `Ui_ModsPage_CompanionFilter`（联动模组 / Companion mods）、`Ui_ModsPage_CompanionFilter_Tip`、`Ui_ModsPage_TagCompanion`（联动 / Companion），
    以及面板用的 `Ui_ModsPage_Companion{PanelTitle, PanelHint, Summary, StatusInstalled, StatusInstalledInline, StatusAvailable, StatusMissing, StatusMissingInline, StatusNoIndex, AddToChanges, Close_Tip, Added}`，
    中英两包各 2001 条、键集与占位符完全对齐；`LocalizationKeys` 同步新增十三个常量。
  - 测试：`ModsCompanionCatalogTests` 5 条（清单内容、插件 id/名与插件源码一致、每条都有收益说明、大小写与空值、`TryGet`）、
    `ModsCompanionSurfaceTests` 10 条（管线只留联动行 / 关闭后恢复全集 / 行徽章只对联动模组亮 / 详情面板给推荐安装 / 无联动时静默 /
    索引为空时清单仍列六项 / 快照里区分已装与可装 / 加入变更集并说明对应插件 / 已装与未知模组不重复加入 / 开关与关闭命令清筛选）——15/15 通过；
    `ViewModels` 1490/1490、`Localization` 76/76 全绿。
  - 无头取证：仓外探针以真实 DI（`ServiceRegistration.Build`）+ 空仓库索引渲染 1056×794，面板照常出六行、摘要「共 6 个 · 可装 6 个 · 已装 0 个」，
    快照 `artifacts/snapshots/mods-companion-panel.png`。
- 存档列表按文件夹分组：同一个存档文件夹里的槽位挨在一起，组头只露文件夹名与条数，不再让 `M6_FlightUI`、`默认`、`RLV_*` 的槽位按时间互相穿插。
  - `SaveRowViewModel` 从槽位名切出 `FolderPath` / `LeafName`（分隔符是 ` / `，Windows 文件名不含 `/`，切分可靠；根目录的 `FolderPath` 为空串、`FolderText` 回落显示 `saves/`），
    分组后首列只显示尾段（`SlotDisplay`），完整槽位名留在 `SlotText` 供悬停提示，选中态标题仍用完整槽位名。
  - `SavesPageViewModel.ApplyRowFilter` 末尾接 `GroupByFolder`：按 `StringComparer.OrdinalIgnoreCase` 分桶、文件夹名排序（父目录与 `Backup` 子目录因此相邻），
    组内保持服务层「最近修改在前」；只有一个文件夹时整页不建组头。分组只做在 VM 层——`SaveFileService.Scan` 的排序与 `SaveGameInfo` DTO 一行未动。
  - 组头不新增行类型：给每组首行打 `IsGroupStart`、组内其余行打 `IsGrouped`，行模板在首行上方画一条通栏灰底（`Classes="SavesFolderHeader"`，左文件夹名 / 右「共 N 个存档」），
    `ListBox` 仍是单一 `SaveRowViewModel` + `VirtualizingStackPanel`，虚拟化不受影响；条数文案复用既有语言包键 `SavesCountText`，不新增键。
  - 视觉对齐：`ListBox.SaveList` 把 `ListBoxItem` 的 `Margin` / `CornerRadius` / `Padding` 归零，内边距下放到行内容与组头的 `Padding="8,…"`，
    组头文字与行首列文字左缘逐像素对齐（实测同为 x=13 / x=14）；共享 `Rectangle#SelectionIndicator` 的选中底色只覆盖行本身（y=209..242），不再从组头底部漏出，视图不画自有选中条。
  - 测试：`SavesPageViewModelTests` 增两条（7 个文件夹的分组顺序 / 组头计数 / 尾段显示 / 选中保持，单文件夹不分组），
    `SavesPageViewGroupHeaderTests` 在真实无头可视树上核对「组头每组只出现一次、文案真的落在树上」——`CkanUI.Plugin.Saves.Tests` 47/47 全绿，`CkanUI.Ui.Tests` 25/25。
- 飞船页从「只看根目录」扩到「根目录 + 每个存档自己的 Ships」，并支持跨库复制与移动：
  - 工具条左侧新增载具库选择器（`Libraries` / `SelectedLibrary`）：第一项固定「根目录（所有存档共用）」，其后按文件夹名排序列出 `saves/<存档名>`。
    存档来源是 `ISaveFileService.ListSavesAsync`，按 `FilePath` 父目录名去重（同一存档的 `persistent` 与 `Backup` 槽位只算一个库）；实例切换后按 `ShipsRoot` 恢复原选中，拿不到存档列表时降级为只剩根库并落到错误条。
  - 选中载具后可「复制到…」「移动到…」（`CopyToLibraryCommand` / `MoveToLibraryCommand`）：弹窗只列**当前库以外**的库，走服务层 `TransferAsync` 把 `.craft` + 同名 `.loadmeta` + `@thumbs` 缩略图一起搬；
    目标重名不覆盖，按既有 `UniqueName` 规则落成 `Alpha (2)`；源与目标同一目录直接失败并说明，不产生空操作。没有第二个库时不弹空选择框，直接提示「没有别的载具库可以放。」
  - 服务层把「库根」抽成参数：新增 `ScanLibraryAsync(shipsRoot)` / `ImportToLibraryAsync(shipsRoot, …)` / `TransferAsync(craft, targetShipsRoot, targetKind, move)`；
    原 `ScanAsync` / `ImportAsync` / `DuplicateAsync` / `RenameAsync` 保留 `gameDir` 语义（内部换算成 `<game>/Ships`），既有调用方与测试不受影响。
    顺带修掉一处旧缺陷：存档库里的载具做重命名/复制时，缩略图路径现在按载具自身所在的 `Ships` 根计算，不再误写进根库的 `@thumbs`。
  - 新增语言包键 `Ui_Ships_Library_{Root,Label}`、`Ui_Ships_Action_{CopyTo,MoveTo}`、`Ui_Ships_Transfer_{Title,Target,Copy_Done,Move_Done,NoTarget,Failed}`，中英两包同序同步，`LocalizationKeys` 补十个常量。
  - 测试：`CkanUI.Services.Tests` 增五条（存档级 `Ships` 扫描与缩略图归属、根→存档复制三件套、移动后源消失、目标重名落 `Alpha (2)`、同目录拒绝），
    `CkanUI.Plugin.Assets.Tests` 增六条（库列表去重与根库置顶、切库改扫存档目录、复制/移动落盘参数与状态文案、没有别的库时不弹空选择框、
    以及在无头窗口里核对下拉框真的绑到 `Libraries`、界面换库会写回 VM 并改扫存档目录）；服务层 1420/1421（1 条既有跳过）、插件 30/30。
- Mod 配置页从「有页面没数据」变成真能用：适配器随包发布、页面能看见适配器加载问题、内置适配器从 2 份扩到 6 份。
  - 修掉一处会让整页空掉的打包缺口：`adapters/` 以前只存在仓库里，既不进 `bin` 也不进发布包，实机装完 `AdapterCatalog` 必然报
    `adapter-dir-missing`、左侧 mod 列表全空。`CkanUI.App.csproj` 新增 `../../adapters/**` 的 `Content`（`Link` 保持一级子目录结构，
    因为目录扫描只认一级），输出与发布目录都 `PreserveNewest`；实测 `src/CkanUI.App/bin/Debug/net10.0/adapters/` 出现 6 个适配器目录。
  - 适配器模式下新增页面级诊断条（`ModConfigPageView.axaml` 根 Grid 由 6 行扩到 7 行，新增 `Grid.Row="3"` 的 `CkanInfoBanner Warning`）：
    以前 `ModConfigInstanceModel.Diagnostics` / `catalog.Diagnostics` 只挂在单个 mod 卡片和通用模式状态栏上，适配器清单为空时整页
    没有任何线索。现在 `ModConfigPageViewModel.PageDiagnostics` 汇总全部诊断消息，`HasPageDiagnostics` 控制可见性，空态文案也按
    `HasAdapterDirectoryProblem`（`adapter-dir*` 码）分流成「没有配置适配器目录」与「没有已适配的 mod」两种——原实现错用了
    `ModConfigNoSelectionHint`（「选中一个 mod 后…」），在空列表里指向一个不存在的操作。
  - 清掉两个**幽灵键**：`adapters/ksp-community-fixes/adapter.json` 原先声明的 `Language`(enum 6 值) 与 `MaxPhysicsDelta`(float)
    在真实 `Settings.cfg` 里根本不存在（来自规格文档而非真机），页面永远显示空白，真去写会报「文件里没有键」。现按实测文件重写为
    21 个根节点直属键（主要修复 8 / 次要修复 6 / 性能 4 / 体验 3，全部 `requiresRestart`），模板 `fixtures/settings.default.cfg` 同步重写。
  - 新增 4 份内置适配器，全部键名/类型/默认值在本机实例 `D:/Kerbal Space Program_newmod/GameData/` 的配置文件里逐行核对
    （脚本按「最小缩进 = 顶层键」判定，21/9/1/2/3 条全部命中顶层，无一误收嵌套键）：
    `dynamic-battery-storage`（9 键：7 个 `Debug*` + `MinimumWarpFactor` + `BufferScaling`）、`ksp-wheel`（`powerConversion`，risk medium）、
    `textures-unlimited`（`logReplacements`/`logErrors`）、`real-fuels-mfs`（`BatteryMultiplier`/`basemassUseTotalVolume`/`radiatorMinTempMult`）。
    四份都用 `onMissing: readonly`（不凭模板造出用户没装的文件）、`unlisted: preserve`、`descriptionFromComment: true`，各配 README 写明取证来源与未收录原因。
    新增第五份 `parallax-continued`（`ParallaxGlobal` 下 6 个子节点共 19 键），用 `onMissing: readonly` 而不是模板——默认值与贴图包配套，猜错会让画面直接崩。
  - 新增语言包键 `Ui_ModConfig_AdapterlessCount`（`⊘ {0} 个 mod 无适配器`，原来是硬编码中文）与 `Ui_ModConfig_PageDiagnosticsTitle`
    （`配置适配器加载时发现问题：`），中英两包同步，`LocalizationKeys` 补两个常量。
  - `fields[].nodePath`：adapter 现在能声明**子节点里的键**。`ModConfigFieldDescription` 新增位置参数 `NodePath` 与
    `ResolveNodePath(file)`——相对名（`CHILD`）拼到 `rootNode` 后面、前导 `/` 视为绝对名链（用于 `rootNode` 之外的节点）、
    已自带根名时不再重复拼接（否则会拼成 `ROOT/ROOT/CHILD`、查找永远落空、字段永远空白这种静默失败）；
    读取器拒绝 `flat`/`custom` 文件上写 `nodePath`、拒绝 `A//B` 这类空节点名，判重按解析后的名链而不是原文；
    读侧 `ModConfigService.ReadFields` 与写侧 `AdapterWriter`（含写后校验 `VerifyWritten`）走同一条解析规则，
    写请求的 `ModConfigFieldChange` 也带上 `NodePath`，页面草稿键由「文件 + 键」改为「文件 + 节点 + 键」——
    否则同一文件里两个子节点的同名键会互相顶掉草稿。同名键散在多个节点又没给 `nodePath` 时**拒绝写入**，不猜第一个。
  - 有了 `nodePath` 之后补齐原先定位不到的键：`ksp-community-fixes` 21 → **29** 字段（`MANEUVER_TOOL_DEFAULTS` 2、
    `MEMORY_LEAKS_DEBUGGING` 4、`COMMNET_THROTTLING_SETTINGS` 2，模板同步补三个子节点）、
    `textures-unlimited` 2 → **7**（`UV_EXPORT` 5）、`real-fuels-mfs` 3 → **11**（`IgnoreFuelsForFill` 8）；
    内置适配器 6 → **7** 份，字段合计 36 → **76**。DynamicBatteryStorage 的 `HANDLERCATEGORY` 出现 14 次、名链无法唯一定位，仍只做根层 9 键。
  - 回归门禁：`AdapterCatalogTests` 由 9 条增到 11 条——`Adapters_RepoBuiltinDirectory_LoadsEveryBundledAdapter` 断言内置目录恰为
    6 份且按名排序，新增 `Adapters_CreateFromTemplateFixtures_ContainEveryDeclaredKey`（模板必须含每个声明键，防"声明有、模板无"漂移）
    与 `Adapters_BundledFields_PointAtKeysThatExistInRealConfigFiles`；`ModConfigPageViewModelTests` 增两条诊断链路用例
    （目录缺失时诊断条与空态文案 / 无诊断时保持安静）；`AdapterJsonReaderTests` 增 6 条 `nodePath` 解析用例（相对/绝对/同节点同名/重复声明/空段/非 node 布局）、
    `AdapterWriterTests` 增 4 条（只改指定子节点 / 绝对名链改到 rootNode 之外 / 节点名写错且文件字节不变 / 歧义拒绝猜）、
    新建 `ModConfigServiceTests` 补读侧此前零测试的缺口（子节点取值不拿根层同名键顶替）、
    `ModConfigPageEditingTests` 增 1 条（同 key 不同节点各存一份草稿、写盘带正确 `NodePath`）；
    `CkanUI.Services.Tests` 1435/1436（1 条既有跳过）、`ModConfig` 插件 173/173（含一条无头渲染用例：诊断条标题与目录级诊断原文
    真的落在可视树上）、
    `tools/verify-layout` 的 H-08（`docs/design/adapter.schema.json` 校验 7 份 adapter）PASS。
  - 文档：`docs/design/mod-config-adapters.md` §4.2 补 `nodePath` 写法与限制表，§8 现状表重写为 7 行并补「取证口径」「第一版的历史教训」两段，
    §10 测试用例数与覆盖范围同步更新。
- Mod 配置用 336 个 mod 的大整合包反推能力缺口：适配器 7 → **19 份**（字段 76 → **256**），`match.paths` 从死字段变成真判定，并统一了「已修改」的比较口径。
  - 语料审计（`D:/SteamLibrary/steamapps/common/Kerbal Space Program`，16,775 个 `.cfg`）：按文件名过滤只能覆盖 66 个 mod，改为解析 ConfigNode 结构、筛「根层标量键 ≥ 5 且没有更大的子节点」，得到 6 个候选，
    排除本地化词条文件后 5 个全部做成适配器：`rcs-build-aid`(19)、`waypoint-manager`(13)、`memgraph`(10，`layout: flat`)、`too-many-orbits`(5)、`lunar-transfer-planner`(5)。
  - 另 7 份按同一口径新增：`system-heat`(24)、`ferram-aerospace-research`(17，`FARConfig` 下 4 个子节点走 `nodePath`)、`time-control`(10)、`krpc`(11)、`space-dust`(15)、`real-chute`(5)、`vab-organizer`(5)；
    `kerbalism` 由 0 字段只读骨架重写为可编辑的 41 字段（12 个玩法开关 + 28 个系数 + `Profile`，risk=high）。
    所有默认值取文件原文（含 `2.7f`、`True` 这类写法），并用脚本在真实实例上逐键比对——19 份 / 256 键，新增部分全部命中。
  - `match.paths` 以前只在读取器里被解析、没有任何消费点，现在参与安装判定：CKAN 注册表没命中时按声明路径在实例里找（`caseInsensitive` 生效），
    命中就出卡片并附 Warning `matched-by-path`（说明版本无法确认）；文件路径解析同样按 `caseInsensitive` 逐级匹配（`ResolvePath`/`Exists`），
    不再因为大小写差异直接判「文件不存在」。`match.resolveByInstalledFiles` 已是默认行为，声明它只发 Warning `adapter-option-deprecated`。
  - 新增 `ModConfigValueComparer`（`Services.Abstractions/ModConfig`）：bool 忽略大小写、float 剥 `f/F` 后缀按数值比较。
    「已修改计数」与行级 `IsModified` 都改走它——原来 `True`/`true`、`2.7`/`2.7f` 会把没动过的字段标成「已修改」；顺带修掉 `ModConfigDetector` 的 float 校验拒收 `2.7f`（词法合法却报「需要数字」）。
  - 测试：`ModConfigDetectorTests` 增 float 后缀用例，`ModConfigServiceTests` 增「注册表无命中但 `match.paths` 命中」「声明的路径在磁盘上不存在」两例，
    `AdapterJsonReaderTests` 把 Kerbalism 的「骨架」断言改成「可编辑 41 字段」；`CkanUI.Services.Tests` 的 ModConfig 子集 **229/229**，`CkanUI.Plugin.ModConfig.Tests` **173/173**。
  - 文档：`docs/design/mod-config-adapters.md` §4.2 补 `match.paths`/`caseInsensitive`/弃用字段三条规则，§4.3 补 `adapter-option-deprecated` 与 `matched-by-path`，
    §5 状态机加「注册表没装但路径命中」一行，§8 重写为 19 行现状表并新增「语料审计方法」「明确排除的候选与理由」两节，
    §10 用例数与语料门禁同步，§11 补「同名重复子节点无法定位」并把已过时的「页面还没有导入 adapter 入口」改成实际实现。


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
- 设置页信息密度重做（`SettingsPageView.axaml`）：38 项设置此前每项一张 `FASettingsExpander` 卡（标题 + 独立描述行 + 右下控件），1282×802 一屏只看得到 4 项。
  - 改成两列紧凑行：左侧「名称 + 单行说明」并排（说明超长省略、悬停看全文），右侧固定 240 px 编辑器列 + 行内辅助按钮 + 「重启后生效」提示，行间 1 px 细线分隔。改前每项一张 expander 卡（标题 / 描述 / 控件三行，约 110-120 px），改后单行项 39-43 px、多行项 110 px。
  - 分组卡片化：8 个分组各包一张 `CkanCard`（组头 + 组内行，`ClipToBounds`），组与组靠卡片边界分开——改前整页只有通栏发丝线，38 行连成一片看不出归属。
  - 三列固定宽度对齐：名称列固定 200 px（说明起点不再随名称长短漂移）、编辑器列固定 240 px、辅助动作槽固定 96 px 且按钮右对齐（没有按钮的行也占位）。实测编辑器右边缘落在 x=1135/1239（240 px 列与占满整行的多行框），辅助按钮右边缘统一 1239，右列成一条竖线。
  - 分组标题与分组说明合并成一行（8 个分组省掉 8 行）；内容右侧留白 14 px，竖直滚动条不再压住「重启后生效」与「立即检查更新」。
  - 卡片留白收紧（组间距 12→8 px、组头内边距 14,9→12,6、行内边距 0,4→0,3）后，38 项 8 组内容总高 **2596 px**（视口 613 px ⇒ 一屏 7 行 + 4 个组头，4 屏看完）。
  - 工具条由 6 个同权重按钮收敛为「重新读取」+「未保存时放弃修改」+「更多」下拉（重试失败项 / 打开配置目录 / KSP 游戏设置 / 首次启动指引）。
  - 面向用户的设置文案去掉内部设计条目号（`（P-01）`～`（P-10）`，中英两包与 C# fallback 各 13-15 处），排障编号不进用户界面。
  - 保留 `stagger-card` 入场锚点（架构测试 `MotionSkeletonTests.PrimaryCards_CarryStaggerAnchors` 钉住）；设置相关 VM 测试 206 条全绿。
  - 分组区分度（返工第二轮，用户反馈「区分功能不是很明显」）：组头改成强调色带（`CkanAccentSubtleBrush` 底色 + 左侧 3 px 强调条），标题 15 px **Bold**、行名回落 13 px 常规字重——改前组头与行名同为 SemiBold 主色、同底色，38 行扫下来分不清哪行是分组；卡间距 8 → 16 px，卡片边框换成实色 `CkanBorderBrush`（不再用渐变边框）。深色 1004×758 实测：8 个组头统一 34 px 高、底色 RGB(20,43,72)，卡体 RGB(32,34,41)，页面底 RGB(32,32,32)，三张滚动截图各检出 4-5 条 33 px 色带。

### 修复

- KSP 配置页真机首进冻结 3.4 秒：插件化搬迁时把分组列表退回成 `ScrollViewer > ItemsControl > ItemsControl` 三层裸列表，settings.cfg 键一多就实体化 13,466 个视觉节点。外层换成 `ListBox` + `VirtualizingStackPanel`，用 6 条样式把 `ListBoxItem` 的内边距/边距/最小高/悬停与选中底色全部抹平（行模板一字未动，外观仍由行内 `Border` 决定）⇒ 同实例同页 13,466 → 888 节点，首进 maxGap 3,365 → 424 ms，重进 63 ms。
- 页面计时探针会在实例还没读上注册表时就开跑，量到的是空壳页面（模组页 254 节点 / 0 行，真实是 2,333 节点 / 2,650 行），据此得出的「这页很快」全是假的。`NavigationProbe` 现在先等仓库索引就绪（`[inst] repo-ready`，实测真机需 12–14 秒），并在每次遍历结束时打印实例名、库路径与错误计数。
- 卡顿到底卡在哪，之前只能靠猜：`src/CkanUI.App/Diagnostics/NavigationProbe.cs` 新增 UI 线程看门狗（`CKANUI_NAV_STALLS=1`）——后台线程每 15 ms 往 UI 线程投递一次空操作，量「投递到执行」的延迟，超过 50 ms 记为一次卡顿；归属在**投递时**取（页名 + 阶段 startup/enter/window0-2/gap/click/scroll），所以不会把卡顿记到下一个页面上。同时 `[nav]` 行尾追加本页窗口内的 `gen2=` 次数与 `pause=` 毫秒（`GC.GetTotalPauseDuration`），把「卡」和 GC 停顿放到同一张表里对账。
- 空闲回收只压堆、不松手：进过一遍全部页面后，即使页面已经切走，页里的行投影（成千上万行视图模型）仍被页面字段压着，GC 压不掉——因为它还活着。`src/CkanUI.App/Diagnostics/IdleMemoryReclaimer.cs` 新增回收前钩子 `BeforeReclaim`，`src/CkanUI.App/Lifecycle/PageProjectionCacheParticipant.cs` 在构造时挂上 `TrimAppCache`：先让各页把离页后没人要的投影丢掉，再压缩堆，一段回收就能带走。钩子抛异常不挡回收（记 Trace 后继续），并有两条单测钉住「先 trim 后 GC」的顺序与失败降级。
- 校验大存档后进程常驻内存不回落：src/CkanUI.Services/Saves/SaveFileService.cs 的校验路径 File.ReadAllBytes 把整份 sfs 读进内存再解析，实测最大的 persistent 存档 120 MB，解析出的字符块全部落在 LOH（大对象堆）上；托管堆回收了，但 LOH 默认不压缩，段一直挂在进程里。现在存档 ≥32 MB 时只登记一次 GCSettings.LargeObjectHeapCompactionMode = CompactOnce，由下一次 GC 顺带压缩，不主动 Collect、不占 UI 线程。
- 内容预览页在没有几个控件的空页面上也在掉帧：`src/CkanUI.Services/Content/ContentService.cs` 在 UI 线程上 `new ZipArchive(stream, ZipArchiveMode.Read)`——`.NET` 的 `ZipArchive` 构造函数会同步读完整个中央目录，大整合包里单个 ZIP 上万条目；随后 `src/CkanUI.ViewModels/Pages/ContentPageViewModel.cs` 又把最多 20000 个条目逐个建成节点 VM 再拼树，全在 UI 线程上（实测这一段抢占 UI 线程 480 ms，而页面只有 89 个视觉节点）。
  - ZIP 读取整体移到线程池（`Task.Run` + `ConfigureAwait(false)`，DTO 建好再回 UI 线程）；条目投影与建树同样挪到线程池，节点 VM 不碰控件，建完整体换上去。
- 进一次存档页或载具页就涨 500 MB 以上，且涨上去不还：`src/CkanUI.Services/Saves/SaveMetaReader.cs` 的 `ReadHeaderText` 用 `StreamReader.ReadLine` 读「头部 200 行」，可真实大存档里单行能上百 MB，`ReadLine` 在 `StringBuilder` 里一路翻倍，缓冲最后落进 `ArrayPool<char>.Shared` 常驻在线程上。ClrMD 实测这两页有 506 个 `Char[]` 共 512 MB（256/128/64…MB 全零，反向引用就是 `SharedArrayPoolThreadLocalArray[]`），载具页中招是因为它顺带列存档。
  - 改成 `FileStream` 读固定 256 KB → `Encoding.UTF8.GetString` → 截到行边界 → 取前 200 行，不再用 `StreamReader`/`StringBuilder`。
  - 实测：孤立页托管堆 saves/ships 都是 753 MB → 238/256 MB；全 24 页 × 2 轮遍历（336 模组）峰值工作集 3346 MB → 1180 MB，托管堆全程 711–716 MB 平稳（此前 211 → 2013 MB 单调爬升不回落）。
- 旗帜页滚动掉帧：缩略图缓存按「64 条」定额，可旗帜页一次就 200 张（160 px 约 11 MB）、截图页 640 px 每张 0.9 MB，量级差十几倍。预热把 200 张全解进缓存，前 136 张被自己的定额挤掉，绑定时在 UI 线程重解一遍，滚回去还要再解（`src/CkanUI.Ui/Converters/PathToThumbnailConverter.cs`）。
  - 改成按解码后像素字节算的 64 MB 预算 + 命中挪队首的 LRU；解码失败的负缓存给 4 KB 固定占用，不会无限增长。
  - 实测（59 帧滚动窗口）：第一批掉帧 9 → 3、maxGap 62.9 → 49.3 ms、p95 19.4 → 16.2 ms；第二批 5 → 2；第三批 0 掉帧（max 16.4 ms）。
- 超长列表拖动滚动条时整片行「消失」：`ScrollRevealMotion` 把每次露头的行都当成「从底边浮上来」，先瞬间把 `Opacity` 打成 0 再弹簧回 1（`src/CkanUI.App/Animations/ScrollRevealMotion.cs`）。拖动滚动条时视口每帧整片换行、行容器全是新实化的，于是每一帧都在把所有可见行归零，屏幕只剩表头和页脚；无头探针实测每次大跨度跳转要 ~14 帧才淡回来（`minOp` 0→0.046→0.261→0.489→…→0.995），连续拖动就永远回不来。
  - 判定改成「整片跳转 vs 从边缘露头」：本次偏移与上次相差超过视口 1.5 倍（`JumpViewportRatio`），或新露头的行 ≥3 且过半（`JumpFreshRows`）时，这一遍只记「已看过」、不动 `Opacity`；顺带修掉 `MarkSeen` 把演过的行从 `LastTop` 摘掉、导致小步滚动被误判成跳转的问题。
  - 回归：`ScrollRevealJumpTests` 3 条（12 步大跳转后每步所有已实化行 `Opacity ≥ 0.99`、静态长列表远跳不淡入、滚轮一小步仍然浮入）+ 既有 `ScrollRevealMotionTests` 8 条全绿。
- 竖直滚动条的拇指被裁掉一多半：`ScrollBar Thumb` 同时写 `MinHeight/MinWidth=28`，竖直条的拇指被撑成 28 宽、横向溢出 10px 轨道（探针实测 `-9,0,28,30`，只有三分之一落在轨道里）。改成按方向给（竖条只约束高度、横条只约束宽度），轨道内实测 `1,0,8,28`。
- 滚动条的交互补完：指针压上去拇指本身换成强调色（以前只有整条变亮，看不出可拖）；按住拖动时 FluentAvalonia 的主题按下触发器会把灰底刷回来，得单独补一条模板内 `Rectangle` 的 `:pressed` 规则；滚动期间挂 `is-scrolling` 类把整条提到不透明，松手 0.7 s 后摘掉（`src/CkanUI.App/Animations/ScrollBarActivityMotion.cs`，跟 `ScrollRevealMotion` 同一批根挂载）。
  - 无头宿主不推进 `DispatcherTimer`（`RunJobs` 只排空作业队列），门禁换成注入 `ScrollBarActivityMotion.Scheduler` 手动触发，才能确定性地验「亮起来 → 停手 → 收回 0.7」。
  - 回归：`ScrollBarInteractionTests` 4 条（拇指不外溢、悬停强调色、按下保持强调色、滚动加亮后收回）+ 既有 `ScrollBarAffordanceTests` 2 条全绿。
- 生涯时间线等「部分列表」滚动后整片空白：`TransformSpring` 收敛后不摘 `RenderTransform`，于是每一条被入场动画或滚动揭示动过的行都永久背着一个恒等 `TransformGroup`（`src/CkanUI.App/Animations/TransformSpring.cs`）。恒等变换一样会让控件单独成一层合成层，滚动条快速拖动时这些层来不及失效，屏幕上就留下一大片「行还在、就是没画出来」的空白——与「残影行」那条（页面缩放区的恒等 `ScaleTransform`）同一类根因。
  - 停到恒等姿态就把变换摘掉（新增 `ReleaseIdentityTransform()`，在 `Advance` 的收敛分支、以及「`MoveTo` 的目标正好等于当前姿态、这一枪根本不会跑」的分支调用；判据是 x/y/缩放/skew/旋转全为恒等值且当前挂的仍是自己那组）；`MoveTo` 的目标正好落在当前姿态时不再重新挂回去，否则「刚摘掉又被下一枪挂上」。下一次真正要动时由 `ReclaimRenderTransform()` 收回，样式抢走的变换照旧抢回。
  - 回归：`TransformSpringLayerTests` 2 条（收敛到恒等后 `RenderTransform` 为 null、再次起跑能收回变换）+ `TransformSpringTests` 既有用例全绿。
- KSP 游戏原生配置页「空着、信息密度低」（截图红框是分类页签那一条）：页签模板绑的是 `{Binding Header}`，而 `KspConfigCategoryTabViewModel` 只有 `Title`/`Category`/`Icon`/`Count`，六个分类全渲染成一个空圆角块，下面到表头之间留着一大片空白。
  - 页签改成图标 + 标题 + 计数（`GlyphIcon` + `Count`，计数本来就算好却没地方显示），分类栏收进工具条一行，不再单独占一条横带。
  - 行列表从 58-60px 的三行卡片压到 45px 的两行（`BorderThickness="0,0,0,1"`、`Padding="12,3"`；左列「名称 + 描述」两行、第二列「键名 + 官方词条」两行，编辑控件统一 `Height="24"`），列宽从 `*,180,240,64` 收到 `*,150,210,56`；441px 的列表高度里可见行数从 6 行变成 9 行。无头渲染实测：工具条 `Border.CkanToolbar` 从 160px 高降到 85px（`y=78 h=85`，内含按钮行 34px + 页签行 27px），状态条 26px，表体 471px，行分隔线间距 45px。
  - 回归：`KspConfigPageViewLayoutTests` 4 条（工具条按钮与页签都绑到真实成员、状态条为空时不留空行——用表头位置上移判定而不是读 `Bounds`、空结果给说明卡而不是白板、行高比旧版紧凑）；`KspConfigPageViewModelTests` 新增 10 条（页签的标题/图标/计数、仅看已改与空态文案、`VisibleCountText`、`SaveButtonText` 带待保存数等）。
- KSP 配置插件页在真机上把本地化键名当译文显示（状态条里是 `Pages.KspConfig.LoadedSu…`）：`KspConfigPageViewModel` 有 17 处 `Text("中文原文", "Pages.KspConfig.*")` 把键写成字符串字面量，而两份语言包（各 1962 键）与 `LocalizationKeys` 一条都没有；宿主导航时会挂上 `ILocalizer.Get`，`Text(fallback, key)` 因此走真翻译而不是回退，键查不到就把键名原样吐出来。
  - 17 处全改成 `LocalizationKeys.KspConfigPage*` 常量（`Ui_KspConfigPage_*` 22 条），三处插值 fallback 去掉 `$`（`"保存 {0} 项"`、`"已加载 {0} 项配置（官方词条 {1} 条）"`、`"{0} / {1} 项"` 等），两份语言包同步补齐 22 条（现各 1984 键，键集与占位符一致）。
  - 守卫升级：`PageTextLocalizationTests` 的扫描范围从 `src/CkanUI.ViewModels/Pages` 扩到 `src` 下全部 `.cs`（主程序与首方插件同一套规矩），并新增 `NoSourceUsesALiteralLocalizationKey`——第二参是字符串字面量的 `Text(...)` 直接判红。原守卫只查「包里有常量之外的死键」，缺的正是「代码用了包里没有的键」这一向。基线 800 站点/17 文件抬到 900/20。
  - 回归：`CkanUI.Plugin.KspConfig.Tests` 24/24、`CkanUI.Localization.Tests` 76/76（键集相同、常量全覆盖、占位符一致）、`CkanUI.Architecture.Tests` 28/28（含新守卫）；快照复核状态条已显示中文「已加载 306 项配置（官方词条 12028 条）」。

- 「变更集 → 安装」这条链路上的 9 处误报与死路（截图 KSP_newmod 实例复现）：
  - 冲突被算成两条：Core 的 `RelationshipResolver` 对同一对模组同时记 `(A,B)` 与 `(B,A)` 两条冲突描述，字符串方向相反去不掉。适配层 `CoreChangeSetPlanner` 改成按「两个 identifier 排序后的二元组」做无序键去重，`ChangeSetPreview` 新增 `ConflictEntries`（结构化：说明 + identifier + 名称），`Conflicts` 由其派生，条数严格一一对应。回归：`ConflictEntryDedupTests`。
  - 冲突行只有一句「A 与 B 冲突」，看不到是哪个模组、也没有出口：冲突区改绑 `ConflictRows`，行内显示「名称（identifier）」，并新增「去模组页处理」按钮（`GoToModsCommand` → 模组页）与可执行的提示文案；对话框同步吃结构化条目。
  - 只有提示（Warnings）也被判「部分失败」并给「重试」：`InstallReport.IsFullSuccess` 与安装历史 `RecordRestore` 只看 `Failed`，警告不参与成败；结果文案改成「全部成功（含 N 条提示）」，警告原文照常列出。回归：`ApplyWarningsOnly_EndsInDoneWithoutRetry`、`Restore_WithWarningsButNoFailures_RecordsSucceededOutcome`。
  - 下载阶段百分比长期 0%：后端此前从 40% 起报、下载器没接进度。现在订阅 `NetAsyncModulesDownloader` 的下载进度，按字节加权映射 8%→35%，安装段 35%→99%，并带阶段标签（「下载 3/12」「安装 2/5」）；状态栏新增独立阶段徽标，百分比不再重复写进状态文字。回归：`ApplyAsync_BackendProgressCarriesPhaseAndItemCounts`、`Applying_ProgressStageLabelBecomesItsOwnBadge`。
  - 进度条横穿状态文字、执行完就消失：状态栏改成两行（文字换行不再被压，进度条独占一行），进度条可见性由 `IsProgressVisible`（执行中 + 结束后的 Done/Failed/Cancelled）决定，保留最终 100%。回归：`ChangesPageStatusBarLayoutTests` 用无头渲染真量 Bounds，钉住「文字与进度条矩形不相交」「Done 后进度条仍在」。
  - 缓存目录警告只喊不给出路：警告区在缓存类提示出现时给「打开缓存设置」按钮，非缓存类提示（如「已忽略卸载请求」）不显示，避免误导。
  - 迟到的旧进度会把百分比拽回去（线程池回调乱序）：`OnProgressReported` 在 Applying 状态下丢弃百分比更低的过期进度。回归：`LateOlderProgress_DoesNotRewindPercentOrStage`。
  - 明细表「版本变化」列只显示「0.10.0 0.11.0」两个数字并排，中间没有箭头，读起来像两个并列版本号：单元格补上 `→` 分隔，与列头「从版本 → 到版本」对齐。
  - 像素证据：`ChangesPageVisualEvidenceTests` 把修好后的页面渲染成 `artifacts/visual-probe/changes-after-fix.png`。

- 变更页失败态把同一件事说了四遍，还把 Core 的提问句当成「执行结果」显示（截图：AIES 依赖有两个提供者）：
  - 根因一：`CkanInstallerBackend` 把 `TooManyModsProvideKraken` 降级成硬失败，`InstallerService.Failure` 又把「依赖解析失败」和 `result.Message` 用空格拼成一句，状态栏与错误栏因此各挂一条同义长句；`ApplyInstallResultAsync` 还把 `result.Detail`（kraken 的提问原文）写进 `ResultText`，界面上就多出一个不是结果的「执行结果」。
  - 提供者歧义改走选择而不是失败：后端新增 `InstallerBackendStatus.NeedsChoice` 与 `InstallerBackendResult.PendingChoice`（`VirtualModuleRequest`，候选按「已缓存 → 与请求同名 → 名称」排序），`OperationResult<T>.PendingChoice` 把它带到页面层；变更页收到就弹已有的模组选择框，选完在同一个取消令牌下自动重试，取消则回到「就绪」且磁盘没动过。`ApplyAsync` 改成循环而不是递归，一次点击仍只算一次逻辑操作。
  - 失败只说一次：`Failure()` 不再拼接（`result.Message` 优先），`StateText` 改由 `_failureHeadline` 给出各阶段准确措辞（计算失败 / 应用失败 / 部分失败 / 异常），`StatusText` 一律清空；失败原因只进错误栏，`ResultText` 不再放 Detail 或异常消息。
  - 同屏重复一并收口：摘要卡不再重复说「预计下载 X」（体积由摘要卡下面那一行专管）；失败态只剩错误栏里一个「重试」（状态栏那个删掉），`CanApply` 排除 Failed 并新增 `ShowApplyButton`，避免「重试」与「开始」并排却做同一件事。
  - 后台任务浮层不再谎报完成：`IOperationQueue.Enqueue` 新增返回 `OperationOutcome` 的重载，安装服务与 AI 桥如实上报 Succeeded/Failed/Cancelled，失败任务在浮层显示「失败」并计入 `HasFailures`，不再一边页面报错一边「全部完成 100%」。待用户选提供者仍记为成功——磁盘没动，选完原样重跑。
  - 新增本地化键 `Ui_Changes_Error_Apply_Exception_Detail` / `Ui_Changes_Error_Apply_Partial_Failed` / `Ui_Changes_Error_UnknownReason` / `Ui_Changes_Status_Virtual_Module_Needs_Attention`，两份语言包同步。
  - 回归：`EveryFailurePath_SaysTheReasonOnlyInTheErrorBar`、`PendingChoice_PromptsUserThenRetriesWithoutSecondClick`、`PendingChoice_UserCancels_ReturnsToReadyWithoutError`、`Failure_IsSaidOnceAndDetailDoesNotImpersonateResult`、`FailedState_OffersExactlyOneRetryAndHidesStart`、`Summary_LeavesDownloadVolumeToItsOwnLine`、`ApplyAsync_Failure_MarksQueueItemFailedInsteadOfCompleted`、`ApplyAsync_Success_MarksQueueItemSucceededAtFullProgress`。
  - 像素证据：失败态样本重出 `artifacts/visual-probe/changes-failure-after-fix.png`——错误栏一条原因 + 一个重试，状态栏一行，执行结果区消失。
- 侧栏底部的「设置」齿轮首次启动时看不见，点一次展开按钮才回来（截图 KSP_newmod 实例）：
  - 根因：FluentAvalonia 的 `heightForMenuItems()` 用 arrange 期的 `_leftNavFooterContentBorder.Bounds.Height` 切分菜单区与页脚区，页脚可用高度则取 `LayoutHelper.MeasureChild(_leftNavFooterMenuRepeater, ...)` 的结果——首次排版时页脚项还没实例化齐，只量到一行 40 px，于是 `FooterItemsScrollViewer.MaxHeight = 40`，第二项「设置」被排到 y=791（窗口 802 px 高，底栏再占 28 px），整个落在窗口外。展开一次会切 `PaneDisplayMode`，触发 FA 自己的重算，页脚回到 80 px——这就是「点一下展开才出现」。
  - 修法：`MainWindow` 加 `EnsureNavFooterVisible()` 兜底。按名从模板取 `FooterItemsScrollViewer`/`FooterMenuItemsHost`/`MenuItemsScrollViewer`/`MenuItemsHost`，等页脚项实例化齐全后，若实测内容高度大于当前上限、且菜单区腾得出这段空间，就把 `MaxHeight` 放宽到内容高度（窗口太矮时宁可让页脚滚动，也不挤占菜单）。模板部件还没长好时最多重试 3 次（`DispatcherPriority.Background`）。调用点：`RebuildNavigation()` 末尾、`ApplyPaneState()` 末尾、收起动画把 `PaneDisplayMode` 落回 `LeftCompact` 之后、以及 `NavView.Loaded`。
  - 回归：`NavFooterVisibilityTests` 两条（首帧两项都在窗口内且上限 ≥ 内容高度；把上限人为压成一行后，重建导航能纠正回来）。像素证据：`artifacts/visual-probe/nav-footer-first.png` 与 `nav-footer-broken.png`（复刻用户截图：底部只剩「?」「›」和 Bug 图标）、`nav-footer-recovered.png`（齿轮回来）。
- 存档工作台在大存档上「找不到东西」：拿真实生涯存档（45.9 MB / 98.9 万键值 / 65.6 万块）复现后，问题不是文件读不出来，而是列表把差异藏了起来。
  - 左栏不再出现一屏「模组数据」：同名分区自动加 `#1`/`#2`，`SCENARIO` 的副标题改成可读的「场景 #N」；分区、键值、块三栏各加就地筛选，计数写明「筛出 2 / 9」「已列 3 / 23349」，不再让人把「界面只列了一部分」误读成「文件里只有这么多」。
  - 「已截断」不再每个分区都挂：只有自己的字段/块列表真被预算裁掉的分区才标，21/21 的存档信息不会再喊截断；顶部提示说明未列出的内容要用搜索定位。
  - 键值编辑区：值或键名一改就亮「未保存」，`保存改动`/`改名` 只在真有差异时可用，新增 `还原` 回退到文件里的值；大文件模式禁用整块复制/删除时，按钮旁边直接写原因（不再只靠顶部横幅）。
  - 右栏把「展开块」移到列表上方并支持双击块展开；搜索框回车即搜。
  - 回归：`SfsWorkbenchViewModelTests` 新增筛选/还原/重名/截断 6 条，`SfsWorkbenchWindowTests` 钉住三栏筛选框与还原按钮；`SfsStreamOutlineTests` 拆成「全局截断仍上快照」与「只标被裁的分区」两条。
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

- 页面首进与交互掉帧（真实实例 D 盘 Kerbal Space Program，336 模组，32 个导航键全遍历两轮）：
  - KSP 原生配置页：插件化搬迁时丢了虚拟化，退回 `ScrollViewer > ItemsControl(Sections) > ItemsControl(Items)` 三层裸列表，真机实体化 **13,466 个节点**、首进冻结 3.4s。外层换成 `ListBox + VirtualizingStackPanel`（行模板与样式一字未改，只抹平 ListBoxItem 的选中/悬停/内边距），节点 13,466 → 888，首进 maxGap 3365 → 207ms；配置解析与行投影再从 UI 线程挪走（891KB / 12,037 行 Squad 字典逐行解析原本全在主线程）。
  - Mod 配置页 `LoadAsync` 是假异步（同步跑完再 `Task.FromResult` 包一层），导航命令同步耗时 362ms 全在 UI 线程；改成真 `Task.Run` 后 assign 362 → 236ms，重进 76ms。
  - 设置页 2016 个节点同一帧实体化：改成组级 `ObservableCollection` + 每帧 4 行 reveal，首进 maxGap 中位 601 → 330ms、再降到 69ms。
  - 下载统计页首屏与后续行同样分批：重进 maxGap 1213 → 33ms。
  - 日志页 ListBox 改虚拟化 + 固定行高 20px：节点 1281 → 573，首进 1822 → 187ms。
  - 旗帜页 96 张卡片一次灌进无虚拟化 WrapPanel，缩略图未命中时在 UI 线程同步解码：改 8 条一批 + 批间让帧，进页停顿 497 → 9ms。
  - 存档页每换一行就整份解析 .sfs（最大 120MB）：改 300ms 去抖，滚动掉帧 5 → 0、p50 11.3 → 5.7ms。
  - 空闲内存回收阈值 128MB 在真机永远等不到（两轮遍历只涨 83MB），降到 64MB。
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
