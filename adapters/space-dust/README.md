# Space Dust 适配器

## 覆盖范围

- 匹配：CKAN 标识 `SpaceDust`，或文件 `GameData/SpaceDust/SpaceDustSettings.cfg` 存在。
- 文件：`GameData/SpaceDust/SpaceDustSettings.cfg`，`layout: node`，根节点 `SPACEDUSTSETTINGS`，可编辑。
- 字段：**15 个**（bool 8 / int 2 / float 5）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/SpaceDust/SpaceDustSettings.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- particleFieldBaseSize / ShaderName / TextureUrl / TrailTextureUrl 是粒子外观，属于视觉效果
- resourceDiscoveredColor / ResourceColors / ResourceVisibilities 是 0-1 的 RGBA 与资源名单，现有 color 类型按十六进制处理，且名单是数据不是开关
- SetAllDiscovered / SetAllIdentified 标 high：勾上等于跳过探索玩法，且不可逆

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 高风险字段（写盘前二次确认）：`SetAllDiscovered`、`SetAllIdentified`。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
