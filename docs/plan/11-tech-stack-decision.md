# CKAN UI 重构 · 技术栈与系统兼容性决策（11）

> 上层依据：[00-index.md](00-index.md)、[08-risk-register.md](08-risk-register.md)
> 本篇回答两个新增约束：**① 兼容性最好（覆盖早期 Win10 → Win11）② 要有 WinUI 3 那种流畅感**
> 状态：决策稿 v1 · 结论 = **Avalonia 12**（修订 ADR D3，详见 §7）

---

## 1. 需求翻译成可判定的判据

| 用户说法 | 可判定判据 | 度量方式 |
|---|---|---|
| 兼容性最好 | 目标 OS 覆盖表（§3）中"必须支持"的格子全部 ✔ | 虚拟机矩阵实测启动（SP-09） |
| 覆盖早期 Win10 到 Win11 | 最低支持 Win10 1607；Win11 全版本 | 同上 |
| WinUI 3 那样的流畅感 | 7 要素全部达标（§4） | 帧率探针 + 主观评分（§6 清单） |

**"早期 Win10"需要先界定。** 装得上 Avalonia/现代 .NET 的下限由 .NET 决定，不由 UI 框架决定：

| Win10 版本 | 出厂年 | 主流支持结束 | .NET 8/10 能否运行 | 现实装机占比 |
|---|---|---|---|---|
| 1507 / 1511 | 2015 | 2017 | ✗（.NET 下限 1607） | ≈ 0 |
| 1607 / 1703 / 1709 / 1803 | 2016–2018 | 已 EOL | ✔ 1607 起（.NET 10 官方列 `10 1607 (E)`） | 极少 |
| 1809 / LTSC 2019 | 2018 | ESU/LTSC 至 2029 | ✔ | 少量（LTSC 企业机） |
| 21H2 LTSC 2021 / 22H2 | 2021/2022 | LTSC 至 2032 | ✔ | 主体 |
| Win11 22H2/23H2/24H2/25H2/26H1 | 2022–2026 | 在支持 | ✔ | 主体 |

结论：**把下限定在 Win10 1607，等于覆盖了所有"还活着"的机器**；1507/1511 只剩历史存量，为它们保留一条 .NET Framework 4.8 构建分支的收益极低（见 §8 决策点 Q1）。

---

## 2. 三个候选的硬事实（均为官方文档原文核实）

### 2.1 Windows App SDK / WinUI 3

| 项 | 事实 | 来源 |
|---|---|---|
| OS 下限 | APIs 跑在 Win11 及**从 Windows 10 1809 起**的更低版本 | learn.microsoft.com《Windows App SDK》系统要求 |
| 背景材质 | **Mica 在 `Windows version below 22000` 时退化为纯色**；Mica Alt 需 Win11 22000+；Win10 上只能用 Acrylic | learn.microsoft.com/windows/apps/design/style/mica（"app runs on ... The Windows version is below 22000" 明确列在 solid fallback 条件里） |
| 运行时依赖 | 需 Windows App SDK Runtime（打包 MSIX 或非打包 bootstrapper 二选一） | 同上 |
| 目标框架 | `net10.0-windows10.0.19041.0` 类，Windows-only | — |
| 观感 | Fluent 原生，动画/材质/控件与系统一致度最高 | — |
| 跨平台 | 无 | — |

> 关键点：**在 Win10 上 WinUI 3 给不了 Mica**，你拿到的"WinUI 3 感觉"在 Win10 上其实是 Fluent 排版 + 流畅动画 + Acrylic，这三样 Avalonia 都能给。WinUI 3 的独占优势只在 Win11 22H2+ 的 Mica/云母层叠上。

### 2.2 Avalonia（当前稳定版 12.1.4）

| 项 | 事实 | 来源 |
|---|---|---|
| Windows 分层 | Win11 24H2 = Tier 1；Win11 22H2、Win10 22H2 = Tier 2；**Win10/11 更早 build = Tier 3（x64/x86，尽力支持）** | docs.avaloniaui.net/docs/supported-platforms |
| 桌面 .NET 下限 | **.NET 8.0**（→ `net10.0` 完全 OK） | 同上 |
| 背景材质 | 支持 `TransparencyLevelHint="Mica, AcrylicBlur, Transparent"` + `ExtendClientAreaToDecorationsHint` | avaloniaui.net/avalonia/windows；Avalonia GitHub #9055 讨论（AcrylicBlur/Mica 在 Win10 需 `win10-x64` RID） |
| 渲染 | 自带合成器 + Skia（GPU），与 WPF/WinForms 的 GDI/D3D9 老路径不同 | 官方文档 |
| 目标框架 | `net10.0`（可跨平台），Core 的 netstandard2.0 程序集可直接引用 | — |

> Tier 3 不是"跑不了"，是"不进官方测试矩阵、bug 修复优先级低"。对 CKAN 这种单一 Windows 桌面应用，Tier 3 = **早期 build 上你能跑，出问题自己扛**，用 SP-09 在 1607/1809 虚拟机上实测兜住。

### 2.3 WPF（对照项）

| 项 | 事实 |
|---|---|
| OS 下限 | `net481` → Win7 SP1+（.NET Framework 4.8 可装于 1607+/8.1/7 SP1，1903+/Win11 预装） |
| 流畅感 | 无合成器级隐式动画；Mica/Acrylic 只能靠 P/Invoke 硬凑；`DataGrid` 万行虚拟化易掉帧 |
| 优势 | 迁移心理成本最低、生态最全、net481 免装运行时 |

---

## 3. 兼容性矩阵（谁覆盖哪些机器）

| 目标系统 | Avalonia 12 + `net10.0` 自包含 | WinUI 3 + WinAppSDK | WPF + `net481` |
|---|---|---|---|
| Win11 24H2+ | ✔ Tier 1 | ✔（Mica 可用） | ✔ |
| Win11 22H2 | ✔ Tier 2 | ✔（Mica 可用） | ✔ |
| Win10 22H2 / 21H2 LTSC | ✔ Tier 2 | ✔ 1809+，**无 Mica** | ✔ |
| Win10 1809 / LTSC 2019 | ✔ Tier 3 | ✔（下限版本） | ✔ |
| Win10 1607–1803 | ✔ Tier 3（需自包含发布） | ✗（低于 1809） | ✔ |
| Win10 1507/1511、Win7/8.1 | ✗（.NET 8/10 下限 1607） | ✗ | ✔（需另立 net481 分支） |
| 运行时前置 | **无**（self-contained 打包含运行时） | 需装 Windows App SDK Runtime | 需 .NET Framework 4.8（1903+/Win11 已预装） |

**结论**：在"早期 Win10 → Win11 + 免装运行时"这个交集上，**只有 Avalonia 12 + 自包含发布**同时满足。WinUI 3 直接丢掉 1607–1803；WPF 能覆盖但拿不到流畅感。

---

## 4. "WinUI 3 那种流畅感"拆成 7 个可交付要素

流畅感不是控件库给的，是这 7 件事同时成立。逐条给出 Avalonia 的实现手段与验收法。

| # | 要素 | WinUI 3 的做法 | Avalonia 12 的做法 | 达标判据 |
|---|---|---|---|---|
| F1 | 视觉语言（Fluent） | WinUI 控件 + Fluent 2 资源 | `Avalonia.Themes.Fluent`（内置）+ Fluent 2 色板/圆角/间距 token 化 | 与 WinUI 3 Gallery 并排比对，观感差异仅限主题色 |
| F2 | 隐式过渡（悬停/选中/展开） | 主题内置 Storyboard | `Transitions`（`BrushTransition`/`DoubleTransition`/`TransformOperationsTransition`）挂在样式里 | 400+ 交互无跳变，过渡 90–200ms |
| F3 | 页面切换动画 | `Frame` 的 NavigationTransition | `TransitioningContentControl` + `PageTransition`（`CrossFade`/`PageSlide`/`CompositePageTransition`） | 列表↔详情切换不闪白 |
| F4 | 背景材质 | Mica（Win11）/Acrylic | `TransparencyLevelHint` + `ExtendClientAreaToDecorationsHint`，DWM 互操作；Win10 走 AcrylicBlur（需 `win10-x64` RID） | Win11 有 Mica；Win10 有 Acrylic 或纯色降级且不报错 |
| F5 | GPU 合成、无撕裂 | Composition | Avalonia 自有合成器 + Skia（可选 ANGLE/D3D11 后端） | 60fps 无撕裂；`--render-timing` 采集帧时间 |
| F6 | 大列表虚拟化 | `ItemsRepeater` + 元素复用 | `DataGrid`（`Avalonia.Controls.DataGrid`）/`ListBox` 虚拟化 + `ItemsRepeater` | 3,000 mod 滚动 p99 帧时间 ≤ 16.7ms |
| F7 | UI 线程不阻塞 | 异步 + DispatcherQueue | `Dispatcher.UIThread` 封送 + 全部 I/O `Task` 化（05 篇已定三重保险） | 安装/下载期间列表仍可滚动 |

> F7 是最大的现实风险：Core 的 `IUser` 回调来自后台线程、`RaiseSelectionDialog` 同步返回 int（见 02 篇 §3、05 篇）。**卡顿的第一来源从来是 UI 线程被阻塞，不是框架**。

---

## 5. 与 Core 的关系（不变）

- Core 产出 `netstandard2.0` 程序集 → 任何 TFM 都能引用，**换框架不影响 Core 一行不改**的约束（D5）。
- `IUser` / `ServiceLocator` / `CkanTransaction` 契约与框架无关（02 篇）。**框架选择纯粹是表现层决策，可逆性中等偏高**。

---

## 6. 流畅感的工程清单（写进 WBS，不靠"选了框架就流畅"）

1. 列表虚拟化：只为可视行建控件；行内禁用 `Effect`（阴影/模糊）——Skia 的 `BlurEffect` 每帧全量重绘。
2. 行高固定化（`RowHeight` 定值），避免逐行 Measure 抖动。
3. 图标用 `StreamGeometry` 而非位图 PNG 缩放；CJK 文本用 `TextRenderingMode`/字体回退预置。
4. 搜索输入 200ms debounce（03 篇已定 100ms 延迟上限，实测后调）。
5. 过滤/排序在后台线程算，结果一次性 `Replace` 集合，不做逐条增删。
6. 图片（模组截图/图标）异步加载 + 内存 LRU，绝不阻塞首帧。
7. 启动分帧：先画骨架 → 读注册表 → 填充列表（02 篇启动序列已拆）。
8. 安装进度用 250ms 节流 + `DispatcherPriority.Background`（05 篇）。
9. 主题切换用 `RequestedThemeVariant`，避免整树重建。
10. 禁用 `AllowsTransparency`（会退化到软件渲染）；材质走 `TransparencyLevelHint`。
11. 只发布 `Release` + `PublishReadyToRun`（冷启动约 -35%）。
12. 帧率探针进 CI/验收脚本：滚动 10s 采样 p50/p99。

---

## 7. 决策

**D3 修订版：UI 框架 = Avalonia 12（`net10.0`，自包含发布），Fluent 主题 + 自建动效层 + DWM 材质互操作。**

理由（按权重）：
1. **兼容性唯一最优**：Win10 1607 → Win11 26H1 全覆盖，且自包含发布免装 .NET / 免装 WinAppSDK（§3）；
2. **流畅感可完全实现**：F1–F7 在 Avalonia 上都有明确落地手段（§4），Mica 在 Win11 上可获得，Win10 上退化为 Acrylic——**这与 WinUI 3 在 Win10 上的实际表现一致**，不是让步；
3. **不牺牲既有收益**：Core `netstandard2.0` 直接引用、跨平台（macOS/Linux 第一套 GUI）、MVVM 可 headless 测试（D4）；
4. **可逆性中等**：表现层重写即换框架，业务契约不动。

否决 WinUI 3 的**唯一**硬理由：**OS 下限 1809** + 需 WinAppSDK 运行时。若将来确认"只支持 Win11 22H2+"，WinUI 3 会翻盘（§8 Q2）。

---

## 8. 拍板结果（2026-10-26，用户决策）

| # | 问题 | 决定 | 影响 |
|---|---|---|---|
| Q1 | Win10 1507/1511 与 Win7/8.1 是否支持？ | **不支持**。系统下限 = **Win10 1607** | 不设 `net481` 分支；工作量维持基线（v1.3 全特性对齐后为 **23 周 / 854h**，见 [12 篇](12-feature-parity.md) §6）；`03`/`09` 篇加"低于 1607 明确提示退出" |
| Q2 | 是否接受"Win11 才有 100% 原生观感"？ | **接受**。Win10 上以 Fluent + Acrylic 交付 | 材质按三档降级链实现（Mica→Acrylic→纯色），见 03 篇 §12 |

**定稿：Avalonia 12 + `net10.0` 自包含发布，系统下限 Win10 1607。** 理由覆盖绝大多数在役 KSP 玩家（见 §1 表格：1607 之后的 Win10 与全部 Win11），且与"Core 一行不改"完全兼容。

---

## 9. 对既有计划文档的修订点

| 文档 | 修订 |
|---|---|
| [00-index.md](00-index.md) | D3 改为"Avalonia 12"；X5 增注"下限 Win10 1607"；文档地图新增本篇 |
| [01-architecture.md](01-architecture.md) | 项目 TFM 由 `net10.0` 明确为 `net10.0` + `PublishSingleFile/SelfContained`；新增 `Themes/`、`Motion/`、`Platform/Windows/Backdrop.cs` |
| [03-ui-spec.md](03-ui-spec.md) | 新增 §动效规范（时长/曲线/token）与材质降级链 Mica→Acrylic→纯色 |
| [06-wbs-milestones.md](06-wbs-milestones.md) | 新增 2 个任务：`UI-MOTION`（动效层，16h）、`PLT-BACKDROP`（材质互操作，10h）；M1 Gate 增"滚动 p99 ≤ 16.7ms" |
| [07-testing.md](07-testing.md) | 新增帧率探针用例与 4 个 VM 启动矩阵（1607/1809/22H2/Win11） |
| [08-risk-register.md](08-risk-register.md) | 新增 R-16（Avalonia 在 Win10 早期 build 为 Tier 3，行为需自测）、R-17（AcrylicBlur 在 Win10 依赖未公开 DWM，可能失效 → 降级纯色） |
| [10-spike-plan.md](10-spike-plan.md) | 新增 SP-09（1607/1809 虚拟机启动 + 渲染后端探测）、SP-10（3,000 行虚拟化滚动帧率）、SP-11（Mica/Acrylic 三档降级）、SP-12（自包含发布体积与冷启动） |

---

## 10. 待验证清单（spike 必须回答）

- [ ] SP-09：Win10 1607 / 1809 上自包含 `net10.0` Avalonia 应用能否启动、渲染后端是否落到软件渲染。
- [ ] SP-10：3,000 / 10,000 行 `DataGrid` 滚动 p99 帧时间。
- [ ] SP-11：Win11 `Mica`、Win10 `AcrylicBlur`、降级纯色三档是否都能正确切换且无透明穿透 bug。
- [ ] SP-12：自包含单文件体积（预计 60–90MB）与冷启动时间（目标 ≤ 1.5s 到首帧）。
- [ ] SP-13：CJK 字体回退与 100% / 150% / 200% DPI 下布局不破。

---

## 11. 参考来源

- .NET 10 支持的 Windows 版本（含 `10 1607 (E)`）：github.com/dotnet/core 的 `release-notes/10.0/supported-os.md`
- Windows App SDK 系统要求（Win10 1809 起）：learn.microsoft.com/windows/apps/windows-app-sdk/system-requirements
- Mica 材质（低于 22000 退化为纯色）：learn.microsoft.com/windows/apps/design/style/mica
- Avalonia 支持平台分层表：docs.avaloniaui.net/docs/supported-platforms
- Avalonia Windows 平台与材质：avaloniaui.net/avalonia/windows
- .NET Framework 预装矩阵：learn.microsoft.com/dotnet/framework/migration-guide/versions-and-dependencies

## 12. Win10 主战场的再评估（WPF vs Avalonia）

> 触发：KSP 玩家群 Win10 占比仍然很高 → 提问「是不是只能搓 WPF」

### 12.1 先纠正一个错位目标

Win11 的 Mica、12px 圆角、云母层叠，是 **Win11 的视觉语言**，不是"现代"的通用定义。在 Win10 机器上硬套 WinUI 3 观感，等于让应用看起来像"没适配的移植品"。Win10 上的"最好的桌面体验"应定义为：

1. 贴合 Win10 自身语言（Acrylic 材质、Segoe UI、系统控件手感）；
2. 高帧率、无卡顿、无阻塞；
3. **零前置依赖**（双击 exe 就能用，不装运行时、不装 WinAppSDK）；
4. 无障碍/IME/高对比度不退化。

前两条 Avalonia 与 WPF 都能做到，**决定性差异在第 2、3、4 条之外的四项"非功能指标"**（§12.3）。

### 12.2 逐维度对比（限定"Win10 为主战场"这个前提）

| # | 维度 | WPF（`net481`） | Avalonia 12（`net10.0` 自包含） |
|---|---|---|---|
| 1 | 观感上限 | 需第三方主题（WPF-UI / MahApps / MaterialDesign）+ 手工 Acrylic；能做到接近 Win11 的观感 | Fluent 主题内置 + `TransparencyLevelHint`（AcrylicBlur/纯色降级）；开箱即现代 |
| 2 | 渲染与动画 | MILCore/D3D9 时代管线；隐式过渡要自己写 Style/Trigger；`BlurEffect` 走 CPU | 自有合成器 + Skia GPU；`Transitions`/`PageTransition` 声明式开箱 |
| 3 | 万行列表 | `DataGrid` 虚拟化可用，复杂行仍会掉帧（需实测） | `DataGrid`/`ItemsRepeater` 虚拟化，元素复用更彻底（需实测） |
| 4 | 前置依赖 | 需 .NET Framework 4.8（**Win10 1903+ 已预装**；1607–1809 需装） | 无（自包含打包含运行时） |
| 5 | 体积 / 冷启动 | 约 3–6MB / 0.3–0.6s | 约 60–90MB / 预估 0.8–1.5s 首帧 |
| 6 | OS 覆盖 | Win7 SP1+（含 1507/1511） | Win10 1607+（更早 build 为 Tier 3） |
| 7 | 无障碍 / IME | UIA 支持成熟，Narrator/高对比度基本白送 | 需自建 AutomationPeer，IME 候选框位置需验证（40–60h 投入） |
| 8 | 跨平台 | ✗ | ✔ macOS/Linux（仓库内目前只有 ConsoleUI） |
| 9 | 成熟度风险 | 无 Tier 风险，20 年生态 | Win10 早期 build 属 Tier 3（官方不进测试矩阵） |
| 10 | 从 WinForms 迁移 | XAML + MVVM，心理成本最低 | XAML 语法近似，事件/绑定差异需适应 |

### 12.3 真正决定胜负的四个非功能指标

1. **零前置依赖**：这条 Avalonia 用自包含发布也能达成，代价是体积（#5）。所以"Win10 用户多"**不构成"必须 WPF"的理由**——它构成的是"必须免装运行时"的理由。
2. **体积与启动**：60–90MB vs 3–6MB。对 KSP 玩家（本机动辄几十 GB 的 GameData）不是障碍，但会拖慢首次启动。
3. **无障碍与 IME**：Win10 上 WPF 明显更省事。若目标用户含大量中文用户（输入法候选框、`IME` 组合串），这是真实成本。
4. **早期 build 风险**：Avalonia Tier 3 意味着 Win10 1607/1809 出问题要自己扛；WPF 是"官方覆盖"。

### 12.4 结论与判据

> **定稿（2026-10-26）：采纳上一条 —— Avalonia 12 + `net10.0` 自包含发布，下限 Win10 1607。** 下表保留为"若日后要翻盘"的判据依据。

**不是"只能搓 WPF"**；两者是不同权重下的最优解：

- 若 **团队 ≤ 1–2 人**、以 **Win10 为绝对主战场**、**首要是"稳 + 小 + 无障碍省事"** → **WPF（`net481`）+ WPF-UI** 是更稳的解，且与现有 `GUI/` 同 TFM，可比性最好。
- 若 **团队 ≥ 2 人**、能接受 **23 周**（v1.2 口径下为 13–15 周，全特性对齐后增至 23 周，见 [12 篇](12-feature-parity.md) §6）、看重 **长期观感上限 / 跨平台 / 未来 Win11 原生** → **Avalonia 12**。

两条路都**不违反"Core 一行不改"**，因为 Core 是 `netstandard2.0`。

### 12.5 另外两条被忽略的路径（诚实记录）

- **Avalonia 11 + `net481` 双发布**：Avalonia 12 要求 .NET 8+，但 Avalonia 11 以 `netstandard2.0` 出货、可被 `net481` 工程引用（社区用法，非官方支持）→ 能同时拿到"免前置依赖 + Win7 覆盖 + 现代渲染"。**风险最高**（未官方测试、SkiaSharp 原生依赖在 net48 上的坑），不建议首发采用，但值得在 spike 里花 4 小时验证一次。
- **保留 WinForms，只换列表控件**：把 `DataGridView` 换成虚拟化列表，解决"卡"，不解决"旧"。成本约 1–2 周。若目标其实只是"别卡"，这是性价比之王（与本次"重构 UI"目标不同，故不推荐作为主方案）。

### 12.6 让决策可测，而不是凭框架名声

把"最好的桌面体验"落成 5 个可测指标，在 spike 里对两条路同时打分：首帧时间、滚动 p99 帧时间、安装期 UI 可用性、安装包体积、无障碍检查表（Narrator 朗读、高对比度、100/150/200% DPI、中文输入法）。**同一份 03 篇 UI 规格，两套实现各做 1 个屏（模组列表 + 详情），实测后定稿。**

---

## 13. 修订记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-26 | v1 | 新建；修订 D3；回答"兼容性 + 流畅感"双约束 |
| 2026-10-26 | v1.1 | 新增 §12「Win10 主战场的再评估」：给出 WPF 与 Avalonia 的判据阈值与两条被忽略路径，明确"Win10 用户多"不等于"必须 WPF" |
| 2026-10-26 | v1.2 | **定稿**：§8 Q1/Q2 已拍板（下限 Win10 1607；Win11 才 100% 原生观感）；§12.4 结论落定 Avalonia 12；已回填 01/03/06/07/08/09/10/00 八篇 |
| 2026-10-26 | v1.3 | 与 [12 篇](12-feature-parity.md) 全特性对齐口径同步：§8 Q1、§12.5 中的"15 周 / 846h"改为"**23 周 / 854h**"（框架决策本身不变） |
