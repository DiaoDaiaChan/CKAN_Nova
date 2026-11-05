# Time Control 适配器

## 覆盖范围

- 匹配：CKAN 标识 `TimeControl`，或文件 `GameData/TimeControl/Plugins/PluginData/GlobalSettings.cfg` 存在。
- 文件：`GameData/TimeControl/Plugins/PluginData/GlobalSettings.cfg`，`layout: node`，根节点 `GlobalSettings`，可编辑。
- 字段：**10 个**（int 4 / float 3 / bool 2 / string 1）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/TimeControl/Plugins/PluginData/GlobalSettings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- Version 是插件版本号，不是设置
- KeyBinds 下 16 个同名 KeyBind 子节点是按键绑定数据；nodePath 只能定位第一个同名节点，故整段不收录
- SPACECENTER/TRACKSTATION/FLIGHT 窗口坐标属于界面状态

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
