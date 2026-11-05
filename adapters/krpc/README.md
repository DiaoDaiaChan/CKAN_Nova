# kRPC 适配器

## 覆盖范围

- 匹配：CKAN 标识 `kRPC`，或文件 `GameData/kRPC/PluginData/settings.cfg` 存在。
- 文件：`GameData/kRPC/PluginData/settings.cfg`，`layout: node`，根节点 `KRPCConfiguration`，可编辑。
- 字段：**11 个**（bool 8 / string 1 / int 2）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/kRPC/PluginData/settings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- mainWindowVisible / infoWindowVisible 是窗口可见性状态，改了也只在下次启动生效，不算配置项
- mainWindowPosition / infoWindowPosition 是窗口坐标
- logLevel 的完整枚举值未取证，用 string 而非 enum

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
