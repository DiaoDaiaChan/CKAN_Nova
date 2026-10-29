# verify-layout：仓库卫生门禁

> 规则出处：[21-repo-layout.md](../plan/21-repo-layout.md) §12（门禁表 H-01..H-12、分级 R-81）、§4（根目录白名单）、§9（临时区时效）、§11（gitignore/gitattributes）。
> 工具位置：`tools/verify-layout/`；CI 入口：`.github/workflows/hygiene.yml`。
> 项目名 `CkanUI.Tools.VerifyLayout`（带 `CkanUI` 前缀以沿用 Directory.Build.props 的"自有项目"规范：net10.0 / Nullable / TreatWarningsAsErrors / 中央包管理），产物名 `verify-layout`。

## 1. 用法

```bash
# 全部门禁（默认模式）；有阻断级违规时退出码 1
dotnet run --project tools/verify-layout -- --all

# 只跑指定几条（排查时用，省时间）
dotnet run --project tools/verify-layout -- --only H-01,H-04

# 夹具哈希：改动 tests/fixtures 后重新记录，平时只校验
dotnet run --project tools/verify-layout -- --record
dotnet run --project tools/verify-layout -- --verify

# 临时区时效：先预演，确认无误再真正删除
dotnet run --project tools/verify-layout -- --prune --dry-run
dotnet run --project tools/verify-layout -- --prune
```

| 参数 | 含义 |
| --- | --- |
| `--all` | 默认模式，跑全部门禁；H-11 不在此模式执行 |
| `--prune` | 按 21 篇 §9 清理临时区（artifacts 7 天 / spikes 14 天 / docs/_drafts 30 天） |
| `--record` | 重新生成 `tools/verify-layout/fixtures.sha256` |
| `--verify` | 只校验夹具与清单一致 |
| `--only H-01,H-04` | 只跑指定门禁 |
| `--root <路径>` | 显式指定仓库根（默认从当前目录向上找 `.git`） |
| `--manifest <路径>` | H-06 清单路径，默认 `tools/verify-layout/fixtures.sha256` |
| `--dry-run` | `--prune` 只打印不删除 |
| `-v` / `--verbose` | 打印每条门禁的统计说明 |

**退出码**：`0` 通过（含只有警告）、`1` 有阻断级违规、`2` 命令行用法错误。

## 2. 十二条门禁

| 编号 | 级别 | 一句话说明 |
| --- | --- | --- |
| H-01 | 阻断 | 仓库根只允许 21 篇 §4 白名单里的 22 个条目，多出目录或文件即红。 |
| H-02 | 阻断 | `bin/` `obj/` `.vs/` `_build/` 不得被 git 跟踪（构建产物永不入库）。 |
| H-03 | 阻断 | 禁止跟踪 `*.log` `*.dmp` `*.zip` `*.tmp` `*.dll`，以及超过 256 KB 的 `*.sfs`。 |
| H-04 | 警告 | `docs/plan/` 下编号唯一；基线篇（00–10）用 `## N. 标题`，能力域篇（11–21）须有 `## §1` 与 `## 修订记录`（12/13 篇沿用原编号，免检小节号）。 |
| H-05 | 警告 | `docs/README.md` 的索引必须覆盖 `docs/**/*.md`（不登记 = 不存在）。 |
| H-06 | 阻断 | `tests/fixtures` 的 sha256 与清单一致，防夹具被测试就地改写。 |
| H-07 | 警告 | 单个入库文件 >256 KB 且非 LFS、非生成物时提示改走 LFS 或改生成。 |
| H-08 | 阻断 | `adapters/**/adapter.json` 过 JSON Schema，schema **唯一真源**为 `docs/design/adapter.schema.json`（旧 `tools/verify-layout/adapter.schema.json` 已删除，不再有兜底）；该文件缺失即阻断，并在输出里点名缺的是哪一份。 |
| H-09 | 阻断 | `themes/` 只放声明式资源，出现 `.axaml` `.js` `.dll` 即红。 |
| H-10 | 阻断 | 源码里不得出现个人绝对路径（`[A-Z]:\\Users\\` 与 `Kerbal Space Program_newmod`）；门禁自己的匹配式用片段拼接，不会自匹配。 |
| H-11 | 独立 | `--prune` 按保留期清理临时区；`--all` 下只提示 SKIP。 |
| H-12 | 阻断 | `docs/**/*.md` 里的相对链接必须能在工作树里解析（自写解析，不依赖 shell）。 |

**分级依据 R-81**：客观、可机械判定且"没有以后再说空间"的违规阻断（H-01/02/03/06/08/09/10/12）；需要作者判断或允许逐步收敛的只警告（H-04/05/07）。H-11 会改文件，因此单独成模式，绝不混在 `--all` 里。

## 3. 接入与维护

- **CI**：`.github/workflows/hygiene.yml` 在 push/PR 上跑 `--all`；阻断级不过即红。同一 workflow 还会跑一次 `--prune --dry-run`（`continue-on-error`），让过期临时区保持可见但不误锁。
- **夹具变更流程**：新增/替换 `tests/fixtures` 下的文件后，跑 `--record` 把新清单与改动一起提交，否则 H-06 会红。
- **清理节奏**：`--prune` 适合放进本地收尾或定时任务；`--dry-run` 先看清单。
- **扩展**：新门禁加在 `GatesLayout.cs`（结构类）或 `GatesDocs.cs`（文档类），并在 `Program.cs` 的 `RunAll` 表里登记编号、标题、级别。

## 修订记录

| 日期 | 变更 |
| --- | --- |
| 2026-10-27 | 初版：12 条门禁落地、三平台 CI 与 hygiene workflow 接入。 |
| 2026-10-29 | H-08 去掉 tools 侧兜底 schema，只认 `docs/design/adapter.schema.json`；缺失即阻断并点名路径（配套删 csproj 拷贝项与旧文件）。 |
