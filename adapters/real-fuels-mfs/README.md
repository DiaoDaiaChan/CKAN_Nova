# Real Fuels (MFSSettings) 适配器

## 覆盖范围

- 匹配：CKAN 标识 `RealFuels`，或文件 `GameData/RealFuels/MFSSettings.cfg` 存在。
- 文件：`MFSSettings.cfg`，`layout: node`，根节点 `MFSSETTINGS`，可编辑。
- 字段：**11 个**。
  - 根层 3 个：`BatteryMultiplier`（float，默认 1）、`basemassUseTotalVolume`（bool，默认 true，真机写作 `True`，改这项会被规范成小写 `true`）、
    `radiatorMinTempMult`（float，默认 0.99）。
  - `IgnoreFuelsForFill` 子节点 8 个（`nodePath: "IgnoreFuelsForFill"`，全部 bool、默认 true、risk medium、需重启）：
    `IntakeAir`、`KIntakeAir`、`U235Rods`、`DepU235Rods`、`ElectricCharge`、`Ablator`、`EnrichedUranium`、`UraniumNitride`。
- 未收录：根层的 `name`（身份标识）。

## 取证来源

本机 `D:/Kerbal Space Program_newmod/GameData/RealFuels/MFSSettings.cfg` 实测：

```
MFSSETTINGS
{
  name = MFSSettings
  BatteryMultiplier = 1
  basemassUseTotalVolume = True
  radiatorMinTempMult = 0.99
  IgnoreFuelsForFill { ... }
}
```

## 已知差异（会改一行格式）

真机文件里 `basemassUseTotalVolume` 写作 `True`（首字母大写）。ConfigNode 对布尔大小写不敏感，但本工具的写入器统一写小写 `true`——所以**只有当你真的改了这一项**，这一行的大小写会被规范化，其余行不受影响。

## 风险

三项都会改变载具的质量/电量/散热平衡，统一标 `risk: medium` + `requiresRestart: true`。
