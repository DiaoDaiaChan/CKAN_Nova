# RP-1 生涯日志

工具分节下的「RP-1 生涯日志」页签（`rp1-career`），只读当前实例的 RP-1 存档，把散落在多个
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
- 页面：左栏存档下拉 + 搜索 + 筛选 + 时间线；右栏统计卡（合同/里程碑/项目/退役/损失/资金/科技/声望/信心/预算周期）
  与「事件详情 / 预算周期」两个页签；空态分四层（无实例 / 未装 RP-1 / 无存档 / 存档无事件）。
- 只读：不修改 `persistent.sfs`，不写任何存档文件；页面只保存路径与选中项。

## 5. 实测（D:/SteamLibrary/steamapps/common/Kerbal Space Program/saves/DSC）

- 存档 45,887,550 B，RP-1 4.7.0.0，KSP 1.12.5。
- 事件 205 条：合同 162、里程碑 10、项目启动 24、乘员退役 8、乘员损失 1；预算周期 451 条。
- 未知时间事件 0 条；当前资金 4,575,691.60、科技 5,150.42、信心 10,021.43、声望 15,833.93。

## 6. 用例与未验证项

`tests/CkanUI.Plugin.Rp1.Tests` 20 条：日期/文本 5、存档读取 2、时间线合并 2、服务 3、
插件入口 2、页面视图模型 6。另有 `tests/CkanUI.App.Tests` 里 1 条真实 Avalonia 视图加载、
1 条真实插件 dll + 宿主 DI 接线，以及 `ViewLocatorTests` 的插件视图解析断言。

未验证：真实桌面 GUI 未复跑（遵守「无头调试不要打扰使用电脑」）；只有本机 DSC 一个真实存档，
没有第二个 RP-1 版本或损坏存档的样本；预算周期很多时页面只列最近 12 期，完整数据仍在服务层。
