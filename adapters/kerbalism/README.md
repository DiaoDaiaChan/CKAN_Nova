# Kerbalism 适配器（骨架）

## 状态：骨架（不是完整适配器）

本目录只证明"目录即插件"这套机制对 Kerbalism 可用，**不承诺可编辑**：

- ✅ 匹配：CKAN 标识 `Kerbalism`，探测路径 `GameData/KerbalismConfig/Settings.cfg`。
- ✅ 文件：按 `layout: custom` + `mode: readonly` 登记，只读展示原始内容（不做键值解析）。
- ❌ 字段：`fields: []`，一个都还没有。

## 为什么是只读骨架

Kerbalism 的配置体量大、且与游戏进程/存档状态强耦合，写错代价高。
规格（docs/plan/15 `7 Q4）要求"adapter 只声明已取证的键"，而本机没有 KSP 安装，
连 `Settings.cfg` 的根节点名与缩进风格都无从核验，所以先只读。

## 升级为可编辑的步骤

1. 取证：真实 `Settings.cfg` 的根节点名、缩进、注释风格、行尾与编码；
2. 若为 `node` 形态：把 `layout` 改 `node`、补 `rootNode`、`mode` 改 `editable`，删掉 `readonlyReason`；
3. 按"一个键一条 field"补键（带中文 `label` 与 `risk`），危险项（信号/生命维持相关）标 `risk: high`，写入需二次确认；
4. 加 `fixtures/` 样本 + 往返测试，跑 H-08 schema 校验。
