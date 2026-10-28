# mod-config fixtures 来源说明（必须如实标注）

本机没有安装 KSP，也没有任何 mod 的原始文件，因此这些 `.cfg` **不是**原始文件副本，
而是按 `docs/plan/15-mod-config-adapters.md` §2 / §2.5 的实测取证**重建**的代表性样本。

重建保真的是**结构与语法事实**（这些是解析器的约束点）：

| 文件 | 复现的实测事实 |
| --- | --- |
| `kspcf-settings.cfg` | node 形态；根节点 `KSP_COMMUNITY_FIXES`；2 空格缩进；325 行 `//` 注释 + 大量空行的分布特征（节选）；分隔符恒 `" = "`；bool/int/float/enum 混合；LF 无 BOM |
| `kspcf-settings-nested.cfg` | 键值对与子节点交错；结构由花括号决定、与缩进无关（规格 §2.4 "不信任缩进"） |
| `memgraph-settings.cfg` | flat 形态（无根节点、无缩进、无注释、无空行）；10 个键（3 bool + 7 keybinding），键名与 §2.5 键表一致；分隔符 `" = "`；LF 无 BOM |
| `memgraph-padheap.cfg` | 第三种私有语法 `<size>\t: <count>`（只读）；行尾 LF |
| `memgraph-test.cfg` | flat 只读（仅开发者用）；键名按插件语义重建 |
| `mm-patch-sample.cfg` | ModuleManager 补丁语法（`@PART[...]`、`%MODULE`、`%key = value`）：必须只读并标注"这是补丁，不是设置" |

各文件的**具体行数/字节数**与规格里的实测值不同——节选就是节选，不假装等长。
测试因此只断言"与文件自身字节往返一致"这类**不依赖具体内容**的性质；
任何需要特定行尾/BOM/无尾换行的用例一律在测试代码里现场构造字符串，不依赖 fixture。

行尾注意：仓库根 `.gitattributes` 里 `*.cfg text eol=crlf` 会在 checkout 时把本目录的文件转成 CRLF。
往返测试正是与"磁盘上实际读到的字节"比较，因此这条不会让测试变脆；但**不要**在测试里断言"\.cfg 是 LF"。
