# RP-1 生涯日志

独立侧栏入口的「RP-1 生涯日志」页签（`rp1-career`），只读当前实例的 RP-1 存档，把散落在多个
`SCENARIO` 里的生涯数据折成一条按游戏时间排序的时间线。

## 1. 数据源（都是存档内的真实块）

以 `saves/DSC/persistent.sfs`（45,887,550 B）为样本，事件来自五个场景：

| 场景 | 用途 | 关键字段 |
| --- | --- | --- |
| `RP1ContractTracker` | 合同完成的**真实时间** | 每个 32 位十六进制块里的 `Contract { Name; CompletionTime }` |
| `CareerLog` | 合同完成（备用来源）、预算周期、乘员损失 | `_contractDict.ContractEvent { InternalName; Type=Complete; UT }`、`_periodDict.Values.LogPeriod` |
| `MilestoneHandler` | 里程碑与相关飞船/人员 | `seenMilestones.item`、`milestoneData.Keys/Values` 按索引配对 |
| `ProgramHandler` | 项目启动/完成 | `ActivatedStrategies { 项目名 = UT }`、`ACTIVEPROGRAM { name; acceptedUT; completedUT }` |
| `CrewHandler` | 人员退役 | `_retireTimes { 名字 = UT }` |

当前资金/科技/声望/信心优先读 `Funding.funds`、`ResearchAndDevelopment.sci`、`Reputation.rep`、
`Confidence.confidence`；只有缺失时才回退到最后一个预算周期。`CareerLog` 最后一条 `LogPeriod`
的 `CurrentFunds` 在真实存档里是 0，不能拿来当当前资金。

## 2. 大文件读取

存档 45.9 MB、65.6 万块、295 万行，不能整棵树解析。`Rp1SaveReader` 先用
`SfsStreamScanner`（`BlockDepth=1`）拿到所有 `SCENARIO` 的行区间，再单次顺序读文件，
只对目标场景切片调用 `SfsParser.Parse`。九个目标场景：`CareerLog`、`MilestoneHandler`、
`RP1ContractTracker`、`ProgramHandler`、`CrewHandler`、`Funding`、`ResearchAndDevelopment`、
`Reputation`、`Confidence`；其余场景不读正文。

## 3. 时间与标题

RP-1 使用 RealSolarSystem 的日历：纪元 1951-01-01、每天 86,400 秒、每年 31,536,000 秒
（`RSSTimeFormatter` 的 `RealDateTimeFormatter`）。本机没有自定义 epoch 配置，因此
`Rp1GameDate.FromUt` 直接按 UTC 秒加到 1951-01-01；真实存档 `UT=1,186,012,800` 显示为
`1988-08-01 00:00`。

标题增强读取 `GameData/RP-1` 下的定义文件：`Contracts/**/*.cfg` 的 `CONTRACT_TYPE.title`、
`Strategies/Milestones.cfg` 的 `RP0_MILESTONE.headline`、`Programs/**/*.cfg` 的 `RP0_PROGRAM.title`。
映射不到时保留内部名，不隐藏事件。

## 4. 事件与页面

- 事件：合同完成、里程碑、项目启动、项目完成、人员退役、乘员损失、预算周期（默认在时间线里，可按筛选隐藏）。
- 里程碑时间用定义里的 `contractName` 去合同完成时间表里取；同一合同多条完成记录取最早一次。
- 页面布局优化（紧凑表头 + 交互式时间轴）：
  - 紧凑操作区：将原来纵向堆叠的 5 层冗余信息（长标题说明、多行版本徽标、绝对存档全路径、大横条通知）紧凑精简为单行标题与状态徽章、集成化存档选择与检索筛选工具条，释放 ~180px 垂直浏览空间；
  - 交互式纵向时间线：左栏采用时间轴轴线（垂直导轨 + 各类别专属图标节点与彩色光晕），里程碑带有专属金黄强调与星标徽章；
  - 联动筛选胶囊与统计卡：提供「全部 / 里程碑 / 合同 / 项目 / 人员」带实时数量的筛选胶囊；右侧统计卡（合同、里程碑、项目、退役/损失、资金、科技、信心、声望）支持一键点击联动过滤或直达对应指标趋势图；
  - 时间线快速步进：支持上一条、下一条、跳至最早、跳至最新及一键定位至核心里程碑；
  - 统一全景交互式时间线工作台（合并左右双面板为一体化工作站）：
    - 消除分裂的双卡片布局：打破原本左侧窄栏列表与右侧详情/走势割裂的两个面板限制，融合成统一完整的宏微观全景时间线工作台；
    - 顶部全景宏观走势与标尺：生涯宏观走势曲线（`CurveChart`）常驻于工作台顶部全宽呈现，兼具全局宏观趋势监控与时间线定位标尺能力，直观呈现全周期演进脉络；
    - 实时双向联动：
      - 正向联动（时间轴事件 → 走势曲线与指标）：在下方时间轴中选中事件时，顶部全宽曲线上的金色定位游标与标签实时滑动聚焦至该期，右侧故事区同步展示事件详细叙事、里程碑成就横幅及当期经济指标快照；
      - 逆向联动（走势曲线与周期表 → 时间轴）：点击顶部全宽走势图上任意数据点，下方时间轴即刻跳至对应历史事件并同步更新详情；
    - 底部集成式工作区：通过标签页优雅整合「事件时间线与详情」（左侧纵向时间轴导轨与筛选胶囊 + 右侧详细叙事与当期指标）与「预算周期明细表」（全量周期列表与视窗切换），曲线在两页签间均常驻可见，避免反复切页。
- 只读：不修改 `persistent.sfs`，不写任何存档文件；页面只保存路径与选中项。

## 5. 实测（D:/SteamLibrary/steamapps/common/Kerbal Space Program/saves/DSC）

- 存档 45,887,550 B，RP-1 4.7.0.0，KSP 1.12.5。
- 事件 205 条：合同 162、里程碑 10、项目启动 24、乘员退役 8、乘员损失 1；预算周期 451 条。
- 未知时间事件 0 条；当前资金 4,575,691.60、科技 5,150.42、信心 10,021.43、声望 15,833.93。

## 6. 用例与未验证项

`tests/CkanUI.Plugin.Rp1.Tests` 32 条：日期/文本 5、存档读取 2、时间线合并 2、服务 3、
插件入口 2、页面视图模型与交互时间线/双向联动测试 8、定义与序列测试。另有 `tests/CkanUI.Ui.Tests` 32 条（含 `CurveChart` 高亮与点击命令测试）、`tests/CkanUI.App.Tests` 里 1 条真实 Avalonia 视图加载、
1 条真实插件 dll + 宿主 DI 接线，以及 `ViewLocatorTests` 的插件视图解析断言。

未验证：真实桌面 GUI 未复跑（遵守「无头调试不要打扰使用电脑」）；只有本机 DSC 一个真实存档，
没有第二个 RP-1 版本或损坏存档的样本；预算周期很多时页面只列最近 12 期，完整数据仍在服务层。
