# 品牌图标（M6 / PKG-02）

## 一句话

应用图标是**生成物**不是设计稿：`tools/icon-gen` 按 `Resources/Tokens.axaml` 的配色现场光栅化，改令牌后重跑一次即可，仓库里只留产物 `src/CkanUI.App/Assets/ckan.ico`。

## 为什么自绘

1. 上游 CKAN 的图形是第三方商标（`external/CKAN/assets`），随包分发有 trademark 风险，也不属于本应用的设计语言。
2. 「列表行 + 轨道环」直接取自本应用自己的视觉语汇（页签、变更集、胶囊按钮、星空背景的轨道弧），与界面同源。
3. 零依赖生成：不引入 SkiaSharp/ImageSharp，几何用有符号距离场（SDF）+ 中心采样自绘，PNG 编码走 `System.IO.Compression.ZLibStream` 与自写 CRC32。中央包管理与「0 警告」门禁都不被牵动。

## 构成

| 元素 | 几何 | 作用 |
| --- | --- | --- |
| 砖面 | 圆角方块，圆角 = 0.225 × 边长，竖向渐变（暗色 #262B36 → #0F1218） | 承载面，圆角比例呼应 `CkanCornerRadiusLarge` |
| 辉光 | 径向渐变，中心 (0.74, 0.78)，半径 0.34，强度 0.30（暗）/ 0.22（亮） | 右下角偏暖的强调色氛围，取自 `CkanAmbientGlowBrush` |
| 轨道环 | 旋转 −0.35 rad 的椭圆，rx 0.335 / ry 0.126，线宽 `max(0.010, 1.1px)`，alpha 0.42（≥32px 才有） | 星空背景的轨道弧缩影 |
| 轨道节点 | 角 −1.02 处的小圆点，半径 `max(0.016, 1.7px)` | 环上的「模组」 |
| 列表行 | 三条左对齐圆角横条：0.46/0.335/0.235 宽，高 0.072，色阶 1.00 / 0.55 / 0.32 | 模组列表；第一行用 `CkanAccentBrush`，其余向 `CkanTextSecondaryBrush` 偏移 |

## 尺寸分档

小尺寸不做等比缩放，直接换构图——否则 16px 上三条 1.8px 的横条会糊成灰块。

| 边长 | 细节档 | 构图 |
| --- | --- | --- |
| ≥ 48 | 1.0 | 全要素：砖面 + 辉光 + 轨道环 + 节点 + 3 行 + 6 个星点 |
| 24–32 | 0.55 | 去掉星点与节点，轨道环 alpha 降到 0.30；**≤24 连轨道环一起删掉**（16/24 上它退化成一团泥），只剩砖面 + 辉光 + 两行 |
| ≤ 16 | 0 | 只剩砖面 + 辉光 + 2 行（0.50 / 0.32 宽，高 0.108） |

## 产物

| 文件 | 用途 |
| --- | --- |
| `src/CkanUI.App/Assets/ckan.ico` | 7 档（16/24/32/48/64/128/256）PNG-entry ICO，同时被 `<ApplicationIcon>`（exe 内嵌）、`Branding/AppIcon`（窗口）与 `Tray/TrayHost`（托盘）使用 |
| `src/CkanUI.App/Assets/ckan-256.png` | 256px 暗色单图，文档与商店页用 |
| `src/CkanUI.App/Assets/ckan-256-light.png` | 亮色主题变体（`#FFFFFF` 砖面 + `#275DAD` 强调色），浅色背景的展示位用 |
| `artifacts/icon/preview-sheet.png` | 640×560 预览板：上半暗底、下半亮底，各档 1:1 排开，用于人工验收（不放仓库，artifacts 属临时区） |

ICO 用 PNG 条目而不是 BMP 条目：256px 档只有 PNG 条目才能压进合理体积，Windows Vista 以后全部支持。整包 7 档合计 29 411 字节。

## 重新生成

```bash
dotnet run --project tools/icon-gen/CkanUI.Tools.IconGen.csproj -c Release
# 可选：指定输出目录
dotnet run --project tools/icon-gen/CkanUI.Tools.IconGen.csproj -c Release -- /tmp/icon-out
```

工具不在 `ckan-ui.sln` 里（与 `tools/gen-fixtures`、`tools/perf-probe` 同规格），不参与常规构建门禁；产物是二进制，改动图标要连同 `ckan.ico` 一起提交。

## 与主题的关系

图标只读令牌的**暗色**一套：exe 图标与窗口标题栏在 Windows 上不随应用内主题切换，跟着暗色走才和任务栏、开始菜单的观感一致；浅色展示位由 `ckan-256-light.png` 单独提供。

托盘图标优先加载同一份 ICO；图标文件缺失（裁剪过的发布包）时退回自绘方块：底色取当前 `CkanAccentBrush`、横杠用 `#0F1218`，因此换强调色后兜底图标也会跟着变。
