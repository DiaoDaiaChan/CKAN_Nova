# tools/gen-fixtures

> 占位（任务 ENV-04 只登记目录，不实现）。真正实现由 TST 系列任务补齐，见 21 篇 §8 / 07 篇夹具约定。

## 为什么需要它

`tests/fixtures/` 里的 `*.sfs` 与 `*.cfg` 是**往返字节一致**的证据（13 篇 §3 铁律 L3），但：

- `.sfs` 超过 256 KB 不许入库（21 篇 §12 H-03），真实存档动辄数 MB；
- `.gitattributes` 给 `*.sfs`/`*.cfg` 钉了 `eol=crlf`，手工编辑很容易把 CRLF 洗成 LF，黄金用例当场失真。

所以大夹具必须**由脚本确定性生成**，仓库只存生成器和它的期望哈希。

## 约定（实现时遵守）

| 项 | 约定 |
| --- | --- |
| 入口 | `dotnet run --project tools/gen-fixtures -- <case> [--out tests/fixtures]` |
| 确定性 | 同一 `<case>` + 同一参数必须逐字节可复现（时间戳、GUID、路径一律从参数派生，不用 `DateTime.Now`） |
| 大小 | 默认生成 < 64 KB 的小样本；>256 KB 的样本走 `artifacts/`，不入库 |
| 哈希 | 生成后自动调用 `tools/verify-layout --record` 刷新 `fixtures.sha256`（H-06 的清单） |
| 命名 | 小写短横线 + 用例编号：`sfs-m7a-01-v13-roundtrip.sfs` |

## 当前状态

空目录占位。在实现之前，`tests/fixtures/` 只放手工挑选的**小于 256 KB** 的真实样本，
并由 `tools/verify-layout --record` 记录 sha256，保证"测试跑完夹具没被改写"（H-06）。
