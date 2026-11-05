# KSP Wheel 适配器

## 覆盖范围

- 匹配：CKAN 标识 `KSPWheel`，或文件 `GameData/KSPWheel/KSPWheelConfig.cfg` 存在。
- 文件：`KSPWheelConfig.cfg`，`layout: node`，根节点 `KSPWHEELCONFIG`，可编辑。
- 字段：**1 个**——`powerConversion`（int，默认 65，范围 1–1000）。

## 取证来源

本机 `D:/Kerbal Space Program_newmod/GameData/KSPWheel/KSPWheelConfig.cfg` 实测只有一项：

```
KSPWHEELCONFIG
{
  // 注释写明原版换算比是 65:1
  powerConversion = 65
}
```

## 为什么标 medium 风险

这个值影响所有 KSPWheel 系轮子的电机功率换算，改了会改变全部载具的驱动手感，不属于「随便调」的开关，因此 `risk: medium` + `requiresRestart: true`。

## 安全

- `unlisted: "preserve"`：将来 KSPWheel 新增键不会被本适配器删掉。
- `onMissing: "readonly"`：文件不在时只读提示，不造模板。
