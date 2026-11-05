# System Heat 适配器

## 覆盖范围

- 匹配：CKAN 标识 `SystemHeat`，或文件 `GameData/SystemHeat/Settings.cfg` 存在。
- 文件：`GameData/SystemHeat/Settings.cfg`，`layout: node`，根节点 `SYSTEMHEAT`，可编辑。
- 字段：**24 个**（int 5 / float 12 / string 1 / bool 6）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/SystemHeat/Settings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- HarvesterPatchesInstalled / ConverterPatchesInstalled 由 ModuleManager 补丁（@SYSTEMHEAT:NEEDS[...]）在加载时改写，手改无效，故不收录
- SpaceTemperature 原文是 2.7f：本轮顺带修了浮点后缀解析，否则该行会被判为非法输入

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
