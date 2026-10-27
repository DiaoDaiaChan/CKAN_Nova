# 文档索引

> 唯一入口（21 篇 §6 规则 3：新文档不登记 = 不存在）。
> 规格真相源在规格仓 `C:\Users\43701\Documents\github\CKAN\doc\uir\`，迁移后副本进 `docs/plan/`。

| 目录 | 内容 |
| --- | --- |
| `plan/` | 规格正文（24 篇，与规格仓同编号同文件名） |
| `design/` | 单点设计：契约、状态机、协议、格式规范 |
| `adr/` | `NNNN-slug.md`，一页一决策，永不删除 |
| `research/` | 取证记录（实测数据、官方文档摘录，带日期与链接） |
| `ops/` | 运维/发布手册 |
| `test/manual/` | 手工用例 `H-NN-name.md`（与 07 篇编号对齐） |

## 索引 · 运维与设计

| 文档 | 说明 |
| --- | --- |
| [ops/engineering-conventions.md](ops/engineering-conventions.md) | 工程约定：构建、测试、代码风格、架构铁律、提交纪律 |
| [ops/team-brief.md](ops/team-brief.md) | 并行开发简报：作用域划分、提交纪律、报告格式 |
| [ops/verify-layout.md](ops/verify-layout.md) | `verify-layout` 仓库卫生门禁：12 条 H-01..H-12 用法与分级 |
| [design/service-contracts.md](design/service-contracts.md) | 服务层契约冻结表：13 个接口 + 8 组 DTO + 实现归属 |
| [design/pages.md](design/pages.md) | 页面设计：M1 各页布局、控件与交互落点 |
| [design/theme-tokens.md](design/theme-tokens.md) | 主题 token 表：Ckan* 语义色/字号/间距在三套主题下的取值 |
| [design/feature-matrix.md](design/feature-matrix.md) | F-01..F-55 实现状态矩阵（已实现 34 / 部分 21 / 未开始 0）与四波次优先级 |
| [design/milestones.md](design/milestones.md) | M0–M14 里程碑执行实况、已落地模块清单与已知缺口 |
| [design/labels.md](design/labels.md) | F-24 标签组：分层、DIM 契约、Mods 页筛选与四条缺口 |
| [design/mod-dialogs.md](design/mod-dialogs.md) | F-34/35/36 模组对话框族：推荐、虚拟提供者、冲突明细 |
| [design/audit-recommendations.md](design/audit-recommendations.md) | F-36 推荐与建议审计对话框设计与实现（UI-17） |
| [design/context-menu.md](design/context-menu.md) | F-31 模组列表上下文菜单（重装、复制标识符、复制下载地址与打开主页 UI-18） |
| [design/cache-panel.md](design/cache-panel.md) | F-50/F-40 下载缓存面板：上限、清理顺序与 ZIP 灌缓存链路 |
| [design/residual-cleanup.md](design/residual-cleanup.md) | F-43 卸载残留目录清理：数据流、四类拒绝理由与逐个跳过语义 |
| [design/play-time-and-version-display.md](design/play-time-and-version-display.md) | F-48 游戏时长存储与 F-54 版本文本格式 |
| [design/dedup.md](design/dedup.md) | F-41 跨实例去重：六条安全边界、扫描两阶段与组内回滚 |
| [design/install-history.md](design/install-history.md) | F-44 安装历史：JSONL 落盘格式、重装编排与已知缺口 |
| [design/plugins.md](design/plugins.md) | F-17 插件系统：两栏模型、加载上下文与页签声明 |
| [design/content-preview.md](design/content-preview.md) | F-29 内容预览：ZIP 中央目录建树、临时分卷下载与原子入库 |
| [design/download-stats.md](design/download-stats.md) | F-47 下载统计：三档维度、环形图与不可用下载量口径 |
| [design/modpack-editor.md](design/modpack-editor.md) | F-46 modpack 编辑器：CkanModule 往返、版本折叠与互斥校验 |
| [design/tray.md](design/tray.md) | F-51 系统托盘：Avalonia 12 图标装配、七项菜单与两个开关 |
| [ops/i18n-guide.md](ops/i18n-guide.md) | 本地化指南：语言包格式、回退链、App 侧接线契约（§6） |

## 索引 · 规格正文（docs/plan）

| 文档 | 说明 |
| --- | --- |
| [plan/00-index.md](plan/00-index.md) | 计划总览：22 份执行计划导航、总工时与里程碑（v2.6） |
| [plan/00a-feasibility.md](plan/00a-feasibility.md) | 可行性评估：Core 边界、风险与方案选型（12 节） |
| [plan/01-architecture.md](plan/01-architecture.md) | 01 目标架构：分层表、依赖规则、组装根 |
| [plan/02-core-integration.md](plan/02-core-integration.md) | 02 Core 集成契约：启动序列、`IUser`、切换实例、事件表 |
| [plan/03-ui-spec.md](plan/03-ui-spec.md) | 03 UI 规格：页面、导航、控件与交互 |
| [plan/04-mvvm-and-models.md](plan/04-mvvm-and-models.md) | 04 MVVM 结构与 Model 迁移 |
| [plan/05-threading.md](plan/05-threading.md) | 05 线程模型与封送规范 |
| [plan/06-wbs-milestones.md](plan/06-wbs-milestones.md) | 06 WBS 与里程碑（M0–M14） |
| [plan/07-testing.md](plan/07-testing.md) | 07 测试策略：分层、替身、门禁 |
| [plan/08-risk-register.md](plan/08-risk-register.md) | 08 风险登记表 |
| [plan/09-migration-packaging.md](plan/09-migration-packaging.md) | 09 迁移、共存、打包与发布 |
| [plan/10-spike-plan.md](plan/10-spike-plan.md) | 10 Spike 计划（2–3 天任务卡） |
| [plan/11-tech-stack-decision.md](plan/11-tech-stack-decision.md) | 11 技术栈与系统兼容性决策（Avalonia 12 + net10.0） |
| [plan/12-feature-parity.md](plan/12-feature-parity.md) | 12 全特性对齐清单（55 条 F-01..F-55） |
| [plan/13-savegame-sfs.md](plan/13-savegame-sfs.md) | 13 存档工作台与 SFS 格式化工具 |
| [plan/14-log-and-ai-debug.md](plan/14-log-and-ai-debug.md) | 14 日志审查与 AI 辅助排障（E2） |
| [plan/15-mod-config-adapters.md](plan/15-mod-config-adapters.md) | 15 社区 Mod 配置适配层（E3） |
| [plan/16-i18n.md](plan/16-i18n.md) | 16 国际化与官方文案复用（E4） |
| [plan/17-mod-metadata-i18n.md](plan/17-mod-metadata-i18n.md) | 17 Mod 源信息 i18n 与仓库同步（E5） |
| [plan/18-launch-resource-release.md](plan/18-launch-resource-release.md) | 18 游戏启动后的资源释放（E6） |
| [plan/19-windows-install-flow.md](plan/19-windows-install-flow.md) | 19 Windows 标准安装 / 卸载 / 注册（E7） |
| [plan/20-appearance-personalization.md](plan/20-appearance-personalization.md) | 20 外观个性化（E8） |
| [plan/21-repo-layout.md](plan/21-repo-layout.md) | 21 仓库布局、模块边界与工作区卫生 |
| [plan/22-test-cases.md](plan/22-test-cases.md) | 22 测试用例与单元测试规格 |

## 未登记目录（按需增长）

- `adr/`：尚未产生决策记录；第一条 ADR 落地后在此登记。
- `research/`：取证记录；现有 `research/i18n-evidence/`（`coverage-official.csv`、`glossary-zh.tsv`、`seed-zh.tsv`），供 16/17 篇使用。
- `test/manual/`：手工用例目录，随 07 篇 H-NN 用例落地后逐条登记。
