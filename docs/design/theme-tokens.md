# 主题 Token 规范（Theme Tokens Specification）

> 依据与参考源：
> - 核心规范：docs/plan/20-appearance-personalization.md（§1 ~ §15 全文）
> - 基础资源定义：src/CkanUI.App/Resources/Tokens.axaml
> - UI 规格与动效：docs/plan/03-ui-spec.md §11、§12
> - 任务对应：docs/plan/06-wbs-milestones.md 中的 THM-01 ~ THM-10、UI-THM-01、T-THM-01..12

---

## 1. 体系概述与命名规则

### 1.1 语义 Token 化铁律
为了支持深色/浅色自由切换、系统高对比度模式无缝兼容、强调色动态派生以及后续第三方主题包扩展，界面禁止使用硬编码样式：
1. **禁止颜色字面量**：所有 XAML 视图中严禁出现如 `#FFFFFF`、`#161A22`、`Black` 等十六进制或原生命名颜色；
2. **禁止圆角/字号字面量**：严禁在控件行内硬编码 `CornerRadius="4"`、`FontSize="13"` 等数值；
3. **强制 DynamicResource**：由于主题字典切换依赖运行时动态查找，所有在 `ResourceDictionary.ThemeDictionaries` 中定义的 Key **必须**通过 `{DynamicResource KeyName}` 引用（Avalonia 官方禁止在此类资源上使用 `StaticResource`，否则切换变体时无法响应更新并可能抛错）。
4. **CI 门禁检查**：CI 流水线包含 grep 白名单门禁，扫描拦截未授权的十六进制色值与行内尺寸。

### 1.2 命名空间与 Key 命名结构
当前基线定义于 `src/CkanUI.App/Resources/Tokens.axaml`，遵循统一步长与语义约定：
- **前缀规则**：`Ckan<组名>-<语义名>`（或 `Ckan<语义名>`），组内语义名称在整个生命周期保持恒定；
- **变体规则**：浅色、深色、高对比度仅在各字典内映射不同的色彩值，资源 Key 命名严格一致，确保任意变体下解析不脱落。

---

## 2. Tokens.axaml 资源清单与三变体取值表

### 2.1 配色组（SolidColorBrush）
定义在 `ResourceDictionary.ThemeDictionaries` 下，覆盖 `Light`、`Dark` 与 `HighContrast` 三大主题变体。

| Token Key | 语义说明 | 浅色 (Light) | 深色 (Dark) | 高对比度 (HighContrast) |
|---|---|---|---|---|
| `CkanSidebarBackgroundBrush` | 侧边栏/导航抽屉背景底色 | `#F4F5F7` | `#1F2430` | `#000000` (纯黑) |
| `CkanSurfaceBackgroundBrush` | 主工作区卡片/内容面板背景 | `#FFFFFF` | `#161A22` | `#000000` (纯黑) |
| `CkanStatusBarBackgroundBrush`| 底部状态栏与工具条背景色 | `#F2F3F5` | `#1B2029` | `#000000` (纯黑) |
| `CkanBorderBrush` | 边框线、分割线与控件外描边 | `#E1E4E8` | `#2C333F` | `#FFFFFF` (纯白) |
| `CkanTextPrimaryBrush` | 主要内容文字、标题、高亮标签 | `#11161D` | `#E8ECF2` | `#FFFFFF` (纯白) |
| `CkanTextSecondaryBrush` | 次要文本、元数据属性名、提示字 | `#5A6472` | `#9AA4B2` | `#FFFFFF` (纯白强对比) |
| `CkanTextInverseBrush` | 反色文本（如强调色底上的字） | `#FFFFFF` | `#11161D` | `#000000` (纯黑) |
| `CkanAccentBrush` | 强调主色（按钮、焦点环、选中框） | `#0A66C2` | `#4C9AFF` | `#FFFF00` (纯黄) |
| `CkanAccentTextBrush` | 强调色背景上专用的前景色 | `#FFFFFF` | `#0B1220` | `#000000` (纯黑) |
| `CkanSuccessBrush` | 成功提示、已安装/最新状态徽章 | `#1B7F3B` | `#4FD07A` | `#00FF66` (高亮荧光绿) |
| `CkanWarningBrush` | 警告提示、版本不匹配或需关注 | `#B26A00` | `#E8A33D` | `#FFFF00` (纯黄) |
| `CkanDangerBrush` | 严重错误、冲突警报、危险操作 | `#B3261E` | `#FF6B5E` | `#FF5050` (鲜艳红) |

> **高对比度铁律（THM-10）**：
> 系统高对比度变体启用时，强制压制所有自定义色彩或主题包覆盖（Priority: System > ThemePack > User > Default），确保只出现纯黑、纯白与高识别荧光黄色阶，WCAG 对比度强制 ≥ 7:1。

---

### 2.2 字号组（Typography - x:Double）
定义基线标准字号，用于层级划分，支持后续 90%–130% 比例缩放（THM-06）。

| Token Key | 类型 | 基线值 | 适用场景 |
|---|---|---|---|
| `CkanFontSizeCaption` | `x:Double` | `12` | 辅助标注、角标、状态栏细节、小标签 |
| `CkanFontSizeBody` | `x:Double` | `13` | 表格内容、正文段落、描述摘要、设置选项说明 |
| `CkanFontSizeSubtitle`| `x:Double` | `15` | 分组标题、卡片子标题、弹窗引导小标 |
| `CkanFontSizeTitle` | `x:Double` | `20` | 页面大标题、首屏向导标题、主弹窗标题 |

---

### 2.3 间距与行高组（Spacing & Density）
统一步长与网格对齐，服务于紧凑/标准/舒适三档密度切换。

| Token Key | 类型 | 当前基准值 | 适用场景 |
|---|---|---|---|
| `CkanRowHeight` | `x:Double` | `28` | 模组列表虚拟化 DataGrid 单行标准高度 |
| `CkanPagePadding` | `Thickness` | `16` | 页面主工作区边缘留白（上下左右 16px） |
| `CkanToolbarPadding` | `Thickness` | `12,8` | 工具栏水平 12px、垂直 8px 间距 |

---

### 2.4 圆角组（CornerRadius）
服务于平直(0px) / 标准(4px) / 圆润(8px) 控件视觉节奏。

| Token Key | 类型 | 当前值 | 适用场景 |
|---|---|---|---|
| `CkanCornerRadiusSmall` | `CornerRadius` | `2` | 紧凑型标签徽章、复选框、进度条指示槽 |
| `CkanCornerRadiusMedium` | `CornerRadius` | `4` | 标准按钮、输入框、下拉菜单、列表选中光标 |
| `CkanCornerRadiusLarge` | `CornerRadius` | `8` | 浮动面板卡片、弹窗容器、详情折叠面板 |

---

## 3. 视图侧使用规范与最佳实践

### 3.1 严格的 XAML 绑定范例
```xml
<!-- 正确范例：全量使用 DynamicResource 驱动 -->
<Border Background="{DynamicResource CkanSurfaceBackgroundBrush}"
        BorderBrush="{DynamicResource CkanBorderBrush}"
        BorderThickness="1"
        CornerRadius="{DynamicResource CkanCornerRadiusMedium}"
        Padding="{DynamicResource CkanPagePadding}">
    <StackPanel Spacing="8">
        <TextBlock Text="模组列表"
                   FontSize="{DynamicResource CkanFontSizeTitle}"
                   Foreground="{DynamicResource CkanTextPrimaryBrush}" />
        <Button Background="{DynamicResource CkanAccentBrush}"
                Foreground="{DynamicResource CkanAccentTextBrush}"
                CornerRadius="{DynamicResource CkanCornerRadiusSmall}"
                Height="{DynamicResource CkanRowHeight}">
            <TextBlock Text="应用变更" FontSize="{DynamicResource CkanFontSizeBody}"/>
        </Button>
    </StackPanel>
</Border>
```

### 3.2 严禁的行为清单
- ❌ **严禁使用 StaticResource 绑定主题色**：
  在 ThemeDictionaries 中定义的 key 使用 StaticResource 会导致变体切换时无法热重载。
- ❌ **严禁使用硬编码字面量**：
  行内指定如 CornerRadius="4" 或 Background="#161A22" 将破坏统一主题与缩放，CI 检查将阻断提交。
- ❌ **严禁在列表行/卡片内行内增加模糊阴影**：
  在列表项模板中添加 BlurEffect 或 DropShadowEffect 将破坏虚拟化滚动性能（违反 PERF-01）。

---

## 4. 后续任务扩展点（THM-02 ~ THM-10）

在后续外观个性化迭代中，`Tokens.axaml` 将作为稳定内核承接如下功能扩展：

### 4.1 THM-02 强调色（已落地）
- **能力**：12 个预设（每色浅/深两版）+ 自定义 `#RRGGBB`；派生写入 4 个资源键
  （`CkanAccentBrush` / `CkanPrimaryBrush` / `CkanAccentSubtleBrush` / `CkanNavigationSelectionBrush`，键名常量在
  `src/CkanUI.App/Theming/AccentHost.cs`）。
- **对比度守卫**（`src/CkanUI.ViewModels/Pages/Settings/AccentContrast.cs`）：正文阈值 `MinimumRatio = 4.5`（WCAG 2.2 AA），
  比对的是**当前生效变体的底色**——浅色 `#FFFFFF`、深色 `#1B1F27`、高对比度 `#000000`（三个常量与 `Tokens.axaml` 的
  `CkanSurfaceBackgroundBrush` 由 `tests/CkanUI.App.Tests/AccentContrastThemeTests.cs` 的 `SurfacePoles_MatchTokensAxaml` 锁死）。
  主题是「跟随系统」时由视图注入的 `DarkThemeProbe` 决定比对哪一档。
- **不合规的处理**：设置页拒绝写入并给建议色（往白/黑方向按 1% 步长混合到第一个 ≥4.5:1 的色）；
  配置文件被手改坏时 `AccentHost` 在注入前做同样的替换并记 `Log.Warn`，界面不会落到读不清的状态。
- **一个 hex 不可能同时满足两档**：浅色底要深色强调色、深色底要浅色强调色，所以校验必须绑定"实际生效的变体"，
  不能对着一个固定底色算。这也是预设色板里浅色档普遍比深色档更暗的原因（`src/CkanUI.App/Theming/AccentPalettes.cs`）。
- **按实例覆盖（P-07，已落地）**：`UiSettings.AccentByInstance`（键=实例名，值=与 `AccentColor` 同一套取值，
  **无键或空值 = 跟随全局**，不是「没有强调色」）。`AccentHost.ResolveAccent(settings, instanceName)` 是唯一的生效值解析口，
  `Apply()` 与实例切换（`IInstanceService.CurrentChanged` → `Refresh()`）都走它；设置页那行「强调色作用范围」
  （`SettingsKeyNames.AccentScope`）切到全局时会把实例键删掉并把当前生效色搬回 `AccentColor`，
  所以「切回全局」看起来是接着用当前色，而不是跳回一个没人记得的旧值。

### 4.2 THM-03 材质与透明（Mica / Acrylic）
- **降级链路**：`Mica` (Win11 build ≥ 22000) → `AcrylicBlur` (Win10 `win10-x64` 构建) → 纯色 (`None`)。
- **Token 联动**：新增 `Ui.Material.WindowBackdrop` 资源槽位；当材质生效时，`CkanSurfaceBackgroundBrush` 的 Alpha 透明度动态调整，同时禁用投影高度以免界面变脏。

### 4.3 THM-04 圆角尺度三档切换
- **扩展点**：通过设置项倍率（0 / 1 / 2）整体驱动 `CkanCornerRadiusSmall`、`Medium`、`Large`。
  - 平直模式：全部为 `0`；
  - 标准模式：`2 / 4 / 8`；
  - 圆润模式：`4 / 8 / 16`。

### 4.4 THM-05 背景图与壁纸（已落地）
- **数据**：`src/CkanUI.Services.Abstractions/BackgroundProfile.cs`（`Kind` / `Color` / `GradientStart` / `GradientEnd` /
  `GradientAngle` / `ImagePath` / `Fill` / `DimPercent` / `BlurRadius` / `OpacityPercent` + `Clamp()`），
  存在 `UiSettings.Background`（全局）与 `UiSettings.BackgroundByInstance`（P-07 按实例覆盖，**无键 = 跟随全局**，不是「没背景」）。
- **落点**：`src/CkanUI.App/Theming/BackgroundHost.cs` 按名取 `MainWindow.axaml` 里的四个控件
  （`BackgroundLayer` / `BackgroundImage` / `BackgroundDim` / `BackgroundScrim`）后刷画笔；纯色与渐变走 `BackgroundLayer.Background`，
  图片非平铺走 `BackgroundImage.Source` + `Stretch`，平铺走 `ImageBrush{TileMode=Tile}`。画笔算术全在
  `src/CkanUI.App/Theming/BackgroundBrushFactory.cs`（纯函数，可单测）。
- **表面洗白（`SurfaceWashAlpha = 0.74`）**：背景层之上再压一层「主题底色 + 74%」的 `BackgroundScrim`。
  这是**没有**改写 `CkanSurfaceBackgroundBrush` 的原因——改写会把 `Window` 底色与所有卡片一起洗掉，正文对比度会掉到不可读；
  压独立一层则只影响空白处，卡片保持不透明（与 §2.1 的「卡片不透明」一致）。
- **解码预算**：`src/CkanUI.App/Theming/BackgroundImageLoader.cs` 的 `DecodeWidth = 1600`
  （1600×900×4 ≈ 5.8 MB，落在 20 篇 §14 的「≤8 MB/张」内；2560 宽会到 14.7 MB 超预算）。缓存只留当前一张，
  换路径时**先摘控件引用再 Dispose**（位图仍被画笔引用时 Dispose 会让渲染线程炸）。
- **高对比度优先级**：当前变体是 `HighContrastVariant` 时整层不画（`ClearLayers` + `Log.Info`），
  设置页那十一行也并入 `AppearanceKeys` 一起置灰并拒绝保存——否则用户会改出「改了却看不见」的设置（THM-10 同上）。
- **与 20 篇规格的差异**（诚实记录）：① 「游戏截图」来源本版未做，折叠进「本机图片」（20 篇 §4 缺口表已记）；
  ② 模糊放宽到 0–64px（规格 0–40）、暗化 0–100%（规格 0–80）、不透明度 0–100%（规格 20–100），
  因为这三项的 0 都是「关闭」语义，写死下限反而让人关不掉；③ 没有「当前 + 上一张」的位图撤销缓存，
  撤销走设置页的「放弃修改」。
- **测试**：`tests/CkanUI.ViewModels.Tests/SettingsBackgroundTests.cs`（11 行读写、作用范围、夹取、高对比度拦截）与
  `tests/CkanUI.App.Tests/BackgroundHostTests.cs`（画笔算术、实例覆盖解析、宿主写入/去抖/无控件不崩）。

### 4.5 THM-06 密度与字号动态缩放
- **扩展点**：
  - 密度：切换紧凑(24px)、标准(28px)、舒适(36px) 行高，动态更新 `CkanRowHeight` 与 `CkanPagePadding`；
  - 字号：通过乘数因子 (0.9 ~ 1.3) 等比缩放四级字号，保证长文案不裁切。

### 4.6 THM-07 动效强度分级（已落地）
- **Token**：`CkanMotionDurationFast` (90ms)、`CkanMotionDurationBase` (150ms)、`CkanMotionDurationPage` (200ms)。
- **落地方式**：`src/CkanUI.App/Theming/MotionHost.cs` 按设置改写这三个 token；视图里的 `Transition.Duration` 一律写
  `{DynamicResource CkanMotionDuration*}`，**不得再出现 `Duration="0:0:0.xx"` 字面量**（`tools/verify-layout` 之外靠评审守）。
- **档位**：`auto`（跟随系统 `SPI_GETCLIENTAREAANIMATION`）/ `full` / `reduced`（只留 90ms 快速反馈，页面切换归零）/
  `none`（全零，状态变化只靠静态指示可感知）。算法在 `src/CkanUI.ViewModels/Pages/Settings/UiMotionScale.cs`。
- **例外**：`CrossFade.Duration` 不是 Avalonia 属性，绑不了资源；页面切换动画由 `src/CkanUI.App/MainWindow.axaml.cs`
  的 `ApplyPageTransition()` 读 token 后重建（时长为 0 时干脆不挂 transition）。

### 4.7 THM-08 ~ THM-10 主题包安全、导入导出与无障碍
- **主题包格式**：纯 JSON 数据包（禁止执行任何 XAML/脚本代码），只允许对声明在白名单中的 Token 键值进行重载。
- **高对比度优先（THM-10，已落地）**：`src/CkanUI.App/ThemeHost.cs` 持有系统高对比度探针
  （`IsSystemHighContrast(Application?)` → `PlatformSettings.GetColorValues().ContrastPreference == ColorContrastPreference.High`），
  为真时把 `RequestedThemeVariant` 强制成 `HighContrastVariant`，并通过 `ForcedStateChanged` 让
  `src/CkanUI.App/Theming/AccentHost.cs` 调 `Refresh()`；`AccentHost.Apply` 检测到当前变体是高对比度时直接
  `RemoveOverride` 掉四个强调色键，**不注入任何自定义颜色**。优先级因此固定为
  `系统/无障碍 > 主题包 > 用户覆盖 > 内置默认`（20 篇 §15）。
