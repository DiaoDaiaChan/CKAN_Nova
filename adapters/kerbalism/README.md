# Kerbalism 适配器

## 覆盖范围

- 匹配：CKAN 标识 `Kerbalism`，或文件 `GameData/KerbalismConfig/Settings.cfg` 存在。
- 文件：`GameData/KerbalismConfig/Settings.cfg`，`layout: node`，根节点 `Kerbalism`，可编辑。
- 字段：**41 个**（string 1 / bool 12 / float 28）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/KerbalismConfig/Settings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- Profile 保留但标 high：它引用整套配置档案，改了会让存档玩法不兼容
- 其余 40 个根键全部收录（12 bool + 28 float 左右）

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 高风险字段（写盘前二次确认）：`Profile`、`Reliability`、`Science`、`SpaceWeather`、`PressureFactor`。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
