# Parallax Continued 适配器

## 覆盖范围

配置文件：`<游戏目录>/GameData/ParallaxContinued/Config/ParallaxGlobalSettings.cfg`
根节点：`ParallaxGlobal`

这份配置的根节点下面**一个直属键都没有**，所有设置都在 6 个子节点里。
这也是本 adapter 存在的意义：它是 `fields[].nodePath` 的主要用例。

| 子节点 | 字段数 | 说明 |
| --- | --- | --- |
| `TerrainShaderSettings` | 5 | 地形细分与贴图混合 |
| `ScatterSystemSettings` | 5 | 地表散射物（草/石头）密度与碰撞 |
| `LightingSettings` | 2 | 光照阴影 |
| `ScaledSystemSettings` | 4 | 行星尺度空间阴影与贴图加载 |
| `DebugSettings` | 2 | 调试开关 |
| `ObjectPoolSettings` | 1 | 碰撞体缓存池 |
| **合计** | **19** | |

## 取证口径

2026-11-04 在 `D:/Kerbal Space Program_newmod` 上核对：

1. 文件共 760 字节，全文人工通读（不像 KSPCF 那样有 137 条键需要脚本筛）。
2. 6 个子节点名各出现 1 次 —— `FindPair` 按完整名链匹配，同名重复的节点会退化到"只命中第一个"，
   那种文件不能这么写（对比 DynamicBatteryStorage 的 `HANDLERCATEGORY` 出现 14 次，所以那份 adapter 只做根层键）。
3. 每个键的值类型按实际字面量判定：`64`/`1024` → int，`2.5`/`0.8` → float，
   `True`/`False` → bool，`Medium` → enum（白名单 Low/Medium/High）。

## 为什么不提供创建模板

`onMissing` 是 `readonly` 而不是 `create-from-template`：Parallax 的地形细分预算、
散射密度、阴影步进次数都是跟贴图包配套调过的，手写一份"官方默认"等于替用户瞎猜，
猜错了画面直接崩（地形穿模、散射物悬空）。配置文件不存在时让用户先跑一次游戏。

## 改键流程

1. 在真机文件里找到键，确认它属于哪个子节点。
2. 加 `"nodePath": "<子节点名>"`（相对 rootNode，可多级写 `A/B`；以 `/\` 开头表示从根算起的绝对名链）。
3. 键名必须与文件逐字一致（区分大小写）。
4. 跑 `dotnet run --project tools/verify-layout --no-build` 的 H-08 门禁 + adapter 目录测试。
