# Textures Unlimited 适配器

## 覆盖范围

- 匹配：CKAN 标识 `TexturesUnlimited`，或文件 `GameData/000_TexturesUnlimited/GeneralConfiguration.cfg` 存在。
- 文件：`GeneralConfiguration.cfg`，`layout: node`，根节点 `TEXTURES_UNLIMITED`，可编辑。
- 字段：**7 个**。
  - 根层 2 个：`logReplacements`、`logErrors`（bool，默认 false）。
  - `UV_EXPORT` 子节点 5 个（`nodePath: "UV_EXPORT"`）：`exportUVs`（bool，默认 false）、`exportPath`（string，默认 `exportedUVMaps`）、
    `width` / `height`（int，1024，16–8192 px）、`thickness`（int，1，0–64 px）。

## 未收录

- `name`：这是这份配置的身份标识（实测值 `default`，官方注释要求只能有一个且必须叫 default），不是开关，改它会改变文件的语义，不放进编辑器。
- `UV_EXPORT` 里也有一个 `name`，同理不收录。

## 取证来源

本机 `D:/Kerbal Space Program_newmod/GameData/000_TexturesUnlimited/GeneralConfiguration.cfg` 实测：

```
TEXTURES_UNLIMITED
{
  name = default
  logReplacements = False
  logErrors = False
  UV_EXPORT { ... }
}
```

## 安全

- `unlisted: "preserve"` / `onMissing: "readonly"`，口径同其它适配器。
