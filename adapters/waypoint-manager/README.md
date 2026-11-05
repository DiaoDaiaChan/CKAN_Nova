# WaypointManager 适配器

## 覆盖范围

- 匹配：CKAN 标识 `WaypointManager`，或文件 `GameData/WaypointManager/PluginData/WaypointManager.cfg` 存在。
- 文件：`GameData/WaypointManager/PluginData/WaypointManager.cfg`，`layout: flat`（无根节点）`，可编辑。
- 字段：**13 个**（string 3 / bool 6 / float 4）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/WaypointManager/PluginData/WaypointManager.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- 未收录 mainWindowPos.x / .y：窗口位置由 mod 自己维护
- 未收录 boxTop / boxLeft：主窗口外的拖拽框位置

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
