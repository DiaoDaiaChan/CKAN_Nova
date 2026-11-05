# RCS Build Aid 适配器

## 覆盖范围

- 匹配：CKAN 标识 `RCSBuildAid`，或文件 `GameData/RCSBuildAid/Plugins/PluginData/settings.cfg` 存在。
- 文件：`GameData/RCSBuildAid/Plugins/PluginData/settings.cfg`，`layout: flat`（无根节点）`，可编辑。
- 字段：**19 个**（int 3 / keybinding 1 / float 2 / bool 13）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/RCSBuildAid/Plugins/PluginData/settings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- 未收录窗口几何（window_x/window_y）：mod 自己保存的窗口位置，不是用户设置
- 未收录 drycom_* 与 drag_altitude_*：按资源/天体缓存的内部状态，改了也会被重算
- 未收录 menu_* 与 *_screen：窗口内的临时勾选状态，写进文件反而会覆盖用户现场选择

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
