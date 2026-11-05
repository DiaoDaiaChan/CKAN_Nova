# Ferram Aerospace Research 适配器

## 覆盖范围

- 匹配：CKAN 标识 `FerramAerospaceResearch` / `FerramAerospaceResearchContinued`，或文件 `GameData/FerramAerospaceResearch/FARConfig.cfg` 存在。
- 文件：`GameData/FerramAerospaceResearch/FARConfig.cfg`，`layout: node`，根节点 `FARConfig`，可编辑。
- 字段：**17 个**（bool 6 / float 2 / enum 1 / string 4 / int 4）。
- 未收录的键：`unlisted: "preserve"` 原样保留，不会被本适配器删掉。

## 取证来源

本机 `D:/SteamLibrary/steamapps/common/Kerbal Space Program/GameData/FerramAerospaceResearch/FARConfig.cfg`（2026-11 语料审计，整合包内有 336 个已装 mod）。

- GuiColors / Textures / Shaders / Voxelization 是界面配色、贴图、shader 与体素调试数据，不是用户设置
- Exposure.debugBackgroundColor 是 0-1 的 RGB 三元组，color 类型按十六进制处理，故不收录
- Exposure.device 的完整枚举值未取证，用 string 而非 enum，避免白名单挡掉合法值

## 默认值

`default` 一律取实测文件原文（包含 `2.7f`、`True` 这类写法），这样「已修改」判定按字节比较，不会因为大小写或后缀误报。

## 安全

- `onMissing: "readonly"`：文件不在时只读提示，不造模板——这些文件都是 mod 首次运行后自己写的，模板会掩盖真实默认值。
- 无 risk=high 字段。
- 全部字段 `requiresRestart: true`：这些 mod 只在启动时读一次配置。
