# Dynamic Battery Storage 适配器

## 覆盖范围

- 匹配：CKAN 标识 `DynamicBatteryStorage`，或文件 `GameData/DynamicBatteryStorage/DynamicBatteryStorageSettings.cfg` 存在。
- 文件：`DynamicBatteryStorageSettings.cfg`，`layout: node`，根节点 `DYNAMICBATTERYSTORAGE`，可编辑。
- 字段：**9 个直属键全部收录**——7 个 `Debug*` 开关（默认全 false）、`MinimumWarpFactor`（100）、`BufferScaling`（1.75）。
- 未收录：`HANDLERCATEGORY` / `HANDLER` 子节点。它们是数据声明（哪些模块算电力来源/负载），不是用户选项，且当前 schema 的 `layout: node` 只能定位根节点直属键。

## 取证来源

本机 `D:/Kerbal Space Program_newmod/GameData/DynamicBatteryStorage/DynamicBatteryStorageSettings.cfg` 实测：

```
DYNAMICBATTERYSTORAGE
{
  DebugVesselData = False
  ...
  MinimumWarpFactor = 100
  BufferScaling = 1.75
  HANDLERCATEGORY { ... }
}
```

默认值里 `1.75` 的注释写明「1.0 是绝对最小值，别低于 1.25」，因此 `min` 取 1.25。

## 安全

- `unlisted: "preserve"`：未声明键一律原样保留。
- `onMissing: "readonly"`：文件不存在时只读并提示，不凭模板凭空造一份配置。
