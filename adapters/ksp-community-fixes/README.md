# KSP Community Fixes 适配器

## 这是什么

`adapter.json` 是**纯声明式**的：告诉 ckan-ui「哪个 mod、哪个文件、哪些键可以改、边界在哪」。
它不含任何可执行代码，加载时会被 JSON Schema 校验（docs/design/adapter.schema.json），
校验不过就整个 adapter 停用（不半可用）。

## 当前覆盖范围（诚实说明）

- ✅ 匹配：CKAN 标识 `KSPCommunityFixes` + 文件探测 `GameData/KSPCommunityFixes/Settings.cfg`（大小写不敏感，实测该目录同时存在 Settings.cfg 与 settings.cfg 两个名字）。
- ✅ 文件：`Settings.cfg`，`layout: node`，根节点 `KSP_COMMUNITY_FIXES`，可编辑，缺失时从 `fixtures/settings.default.cfg` 创建。
- ✅ 字段：**21 个**，全部真机取证（键名、类型、默认值对照实测文件）。
- ❌ 未覆盖：实测该文件有 137 条顶层键 + 4 个嵌套子节点，其余键与全部嵌套节点键没有写进来。

## 已收录的键

| 分组 | 键 |
| --- | --- |
| 主要修复 | RefundingOnRecovery / KerbalInventoryPersistence / RoboticsDrift / DockingPortRotationDriftAndFixes / AutoStrutDrift / ModuleIndexingMismatch / PackedPartsRotation / PartStartStability |
| 次要修复 | PAWGroupMemory / PAWItemsOrder / ReactionWheelsPotentialTorque / CorrectDragForFlags / RespawnDeadKerbals / StrategyDuration |
| 性能 | SceneLoadSpeedBoost / MemoryLeaks / FewerSaves / SkipIndentsOnSavesAndCraftFiles |
| 使用体验 | DisableNewGameIntro / DisableHiddenPortraits / DebugConsoleDontStealInput |

## 为什么没把 137 条全写进来

1. **嵌套节点写不了**：`MANEUVER_TOOL_DEFAULTS` / `MEMORY_LEAKS_DEBUGGING` / `COMMNET_THROTTLING_SETTINGS` 等子节点里的键，当前 `layout: node` 只能定位根节点直属键（`ConfigNodeDocument` 的 NodePath 形如 `ROOT/CHILD`，schema 还没有 nodePath 表达），硬写会读不到值。
2. **宁缺勿错**：adapter 的价值全在键名与默认值准确。写进去的每一条都要能在真机文件里找到对应行，否则就是"幽灵键"——页面显示空白，用户以为没设置，真去写却报「文件里没有键」。

> 历史教训：本适配器第一版声明的 `Language` 与 `MaxPhysicsDelta` 在真实 Settings.cfg 里**根本不存在**（来自规格文档而非真机），已删除并替换为实测存在的键。

## 补键步骤

1. 打开 `<游戏目录>/GameData/KSPCommunityFixes/Settings.cfg`，确认目标键**在根节点第一层**（缩进 2 空格），复制真实键名与默认值；
2. 在 `fields` 里按 `bool`/`int`/`float`/`enum`/`string` 分类追加，务必带 `label`（中文说明）与 `risk`；
3. 同步更新 `fixtures/settings.default.cfg`（模板只含声明过的键）；
4. 跑 `dotnet run --project tools/verify-layout`（H-08 会用 docs/design/adapter.schema.json 校验本文件）。

## 只读与安全

- `unlisted: "preserve"`：插件对未知键"忽略但不报错"，我方**更不会删**。
- 版本区间 `[">=1.30", "<2.0"]`：已装版本越界时整卡强制只读并显示横幅，不写盘。
- 写盘永远走 `IAdapterWriter`：快照 → 原子替换 → 写后重新解析自校验 → 失败回滚。
