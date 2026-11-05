# 存档工作台可用性（0.2）

> 触发：用户给出真实生涯存档工作台截图（DSC / CAREER），反馈「存档编辑器可用性很差，请你自己调研一下」。
> 本文只记录**从真实数据复现出来的问题**与本次改动，不把主观审美当缺陷。

## 1. 真实存档基数

探针：`artifacts/tmp/sfsprobe`（临时工程，不提交），目标
`D:/SteamLibrary/steamapps/common/Kerbal Space Program/saves/DSC/persistent.sfs`。

| 指标 | 值 |
| --- | --- |
| 文件大小 | 45,887,550 B |
| 模式 | 流式（large=True） |
| 分区 | 17 |
| 键值 / 块 | 988,798 / 655,693 |
| 行数 | 2,955,877 |
| 整份截断 | true |

分区暴露的差异：

| 分区 | 已列出字段 / 总字段 | 已列出块 / 总块 |
| --- | --- | --- |
| 存档信息 / GAME | 21 / 21 | 0 / 0 |
| 资金 / Funding | 3 / 3 | 0 / 0 |
| 科技 / ResearchAndDevelopment | 3 / 23,349 | 400 / 2,772 |
| 部件升级 / PartUpgradeManager | 2 / 358 | 1 / 3 |
| 模组数据 / PARAMETERS | 1 / 524 | 78 / 78 |
| 模组数据 / SCENARIO ×5 | 0 / 0 | 0 / 0 |

重复标题：9 个「模组数据」；科技分区 400 个同名 `Tech` 节点。

## 2. 确认的问题 → 处理

| 问题 | 处理 |
| --- | --- |
| 左栏 9 个「模组数据」，只靠灰色小字区分 | 同名分区追加 `#1`/`#2`；`SCENARIO` 副标题改成「场景 #N」 |
| 所有分区都挂「已截断」，21/21 的存档信息也挂 | 流式大纲按分区记录自己的字段/块预算是否被裁；快照仍保留整份截断 |
| 分区、键值、块三栏都没有就地筛选 | 三栏各加筛选框；计数写成「筛出 N / M」「已列 N / M」 |
| 23,349 个键值只列 3 行、400 个同名 Tech 节点，无法定位 | 就地筛选 + 顶部搜索（搜索会无视当前筛选并跳行） |
| 进子块要先选中再点底部「展开块」 | 按钮上移到列表上方；列表支持双击展开 |
| 编辑区没有脏标记、撤销，改名与保存分成两个按钮 | 值/键名一改亮「未保存」；保存/改名按差异启用；新增「还原」 |
| 大文件模式禁用复制/删除，原因只在顶部横幅 | 按钮旁直接写原因 |
| 搜索框只能点按钮 | Enter 直接搜索 |

没做（刻意保留）：三栏宽度仍可拖动但默认固定；字段行仍同时显示中文标签与键名；语法/路径高亮不在本次范围。

## 3. 实现位置

- `src/CkanUI.Sfs/SfsStreamOutline.cs`：根分区与每个顶层块各自记录 `Truncated`，不再复用 `profile.Truncated`；`snapshot.Truncated` 仍是整份文件含义。
- `src/CkanUI.Plugin.Saves/SfsWorkbenchViewModel.cs`：
  - `VisibleSections` / `VisibleFields` / `VisibleNodes` 与三个筛选词；
  - `SectionCountText` / `FieldCountText` / `NodeCountText` / `SectionRangeText` / `TruncationHint`；
  - `HasValueEdit` / `HasKeyEdit` / `HasPendingEdit` / `CanApplyValue` / `CanRenameKey` 与 `RevertEditCommand`；
  - `SfsSectionItem.DisplayTitle` / `DisplaySubtitle` / `Matches`。
- `src/CkanUI.Plugin.Saves/SfsWorkbenchWindow.axaml(.cs)`：三栏筛选框、计数、截断提示、还原/未保存、双击展开、搜索回车；
  无系统标题栏（`WindowDecorations=BorderOnly` + 自绘最小化/最大化/关闭），头部可拖动、双击标题栏切换最大化。
- 回归：
  - `tests/CkanUI.Plugin.Saves.Tests/SfsWorkbenchViewModelTests.cs`：分区筛选、字段筛选、块筛选、还原、重名与场景副标题、截断计数与提示。
  - `tests/CkanUI.Plugin.Saves.Tests/SfsWorkbenchWindowTests.cs`：三栏筛选框、双击提示、还原按钮。
  - `tests/CkanUI.Sfs.Tests/SfsStreamOutlineTests.cs`：全局截断仍上快照；只标被裁的分区。

## 4. 已知边界

- 筛选只筛**已经列出的行**；未列出的 23,346 个键值仍要靠顶部搜索，截断提示已写明这一点。
- 左侧搜索命中列表与分区筛选互斥（搜索结果显示时隐藏分区列表），返回大纲后筛选词仍在。
- 探针是只读解析，不写真实存档；写盘路径（备份 + 原子替换）沿用既有服务，不在本次改动内。
