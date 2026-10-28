# KSP Community Fixes 适配器

## 这是什么

`adapter.json` 是**纯声明式**的：告诉 ckan-ui「哪个 mod、哪个文件、哪些键可以改、边界在哪」。
它不含任何可执行代码，加载时会被 JSON Schema 校验（docs/design/adapter.schema.json），
校验不过就整个 adapter 停用（不半可用）。

## 当前覆盖范围（诚实说明）

- ✅ 匹配：CKAN 标识 `KSPCommunityFixes` + 文件探测 `GameData/KSPCommunityFixes/Settings.cfg`（大小写不敏感，实测该目录同时存在 Settings.cfg 与 settings.cfg 两个名字）。
- ✅ 文件：`Settings.cfg`，`layout: node`，根节点 `KSP_COMMUNITY_FIXES`，可编辑，缺失时从 `fixtures/settings.default.cfg` 创建。
- ✅ 字段：**只有 3 个**——`RefundingOnRecovery`(bool)、`Language`(enum)、`MaxPhysicsDelta`(float)。
- ❌ 未覆盖：实测该文件有 138 条键，其余 135 条**没有**写进来。

## 为什么只写 3 个键

docs/plan/15 `7 Q4 明确写着"盲写等于给自己埋雷"：adapter 的价值全在键名与默认值准确。
本机没有 KSP 安装（无法取到真实 Settings.cfg），因此首版只收录规格里已经取证过的三个键。

## 补键步骤（有真机后）

1. 打开 `<游戏目录>/GameData/KSPCommunityFixes/Settings.cfg`，复制真实键名与默认值；
2. 在 `fields` 里按 `bool`/`int`/`float`/`enum`/`string` 分类追加，务必带 `label`（中文说明）与 `risk`；
3. 把一份脱敏后的样本放进 `fixtures/`，并为它写"改 3 项只变 3 行"的往返测试；
4. 跑 `dotnet run --project tools/verify-layout`（H-08 会用 docs/design/adapter.schema.json 校验本文件）。

## 只读与安全

- `unlisted: "preserve"`：插件对未知键"忽略但不报错"，我方**更不会删**。
- 版本区间 `[">=1.30", "<2.0"]`：已装版本越界时整卡强制只读并显示横幅（`4.3），不写盘。
- 写盘永远走 `IAdapterWriter`：快照 → 原子替换 → 写后重新解析自校验 → 失败回滚。
