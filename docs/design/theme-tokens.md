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
| `CkanCornerRadiusSmall` | `CornerRadius` | `8` | 紧凑型标签徽章、复选框、进度条指示槽 |
| `CkanCornerRadiusMedium` | `CornerRadius` | `12` | 标准按钮、输入框、下拉菜单、列表选中光标 |
| `CkanCornerRadiusLarge` | `CornerRadius` | `18` | 浮动面板卡片、弹窗容器、详情折叠面板 |

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
- **「游戏截图」来源（20 篇 §9.1，第四轮补齐）**：设置页「背景来源」多了「游戏截图（最新一张）」，选它不落盘路径，
  每次重算背景时由 `CkanUI.Services.Settings.BackgroundImageLocator.FindLatestScreenshot(<GameDir>)` 现找 `<GameDir>/Screenshots`
  里 `LastWriteTimeUtc` 最新的一张（`.png/.jpg/.jpeg/.bmp/.webp`；同时间戳按序数定序保证稳定；目录缺失/读盘失败一律当「没有截图」，
  退回无背景而不是弹错）。解析出的路径**必须一起算进背景缓存签名**（`BackgroundHost.BuildSignature(profile, imagePath)`），
  否则切换实例后配置没变、签名相同，宿主会早退导致背景不换。切换实例→重算背景的链路本来就在，
  所以「切到另一个实例背景跟着换」是免费得到的。
- **与 20 篇规格的差异**（诚实记录）：① 截图来源只取最新一张、不拷贝到应用目录、不随启动轮换
  （20 篇 §4 Q2 的「自动轮换」仍留 v2）；
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

---

## 5. 第 2 轮设计系统升级（本轮新增）

本轮把「能看」提升到「耐看、可交互」：补齐海拔、焦点、间距与骨架四类基础设施，
把先前只在页面内零散出现的按钮/徽章/标题写法收敛成样式类，并给外壳加上可折叠图标栏。

### 5.1 新增主题色 Token（三变体，均在 `ResourceDictionary.ThemeDictionaries` 内）

| Token Key | 语义说明 | 浅色 | 深色 | 高对比度 |
|---|---|---|---|---|
| `CkanFocusRingBrush` | 键盘焦点环颜色（`:focus-visible`） | `#0A66C2` | `#4C9AFF` | `#FFFF00` |
| `CkanHoverOverlayBrush` | 通用悬停叠加层（半透明，不改底色） | `#0F000000` | `#14FFFFFF` | `#33FFFFFF` |
| `CkanPressedOverlayBrush` | 按下叠加层（比悬停更重一档） | `#1F000000` | `#24FFFFFF` | `#66FFFFFF` |
| `CkanAccentHoverBrush` | 强调色按钮的悬停态底色 | `#0857A6` | `#6BAEFF` | `#FFFF66` |
| `CkanSuccessBackgroundBrush` | 成功态徽章/横幅的淡底（此前只有前景色） | `#E7F6EC` | `#14301F` | `#003300` |
| `CkanSkeletonBrush` | 骨架屏占位块底色 | `#E9ECF1` | `#2A303C` | `#555555` |

> 高对比度下 `CkanAccentHoverBrush` 用 `#FFFF66` 而不是加深：HC 只允许纯黑白与荧光黄系列，
> 加深会掉到「看不见的灰」；`CkanSkeletonBrush` 同理用中灰而不是深灰——骨架块必须与纯黑底有可辨边界。

### 5.2 海拔（BoxShadow）Token

| Token Key | 语义说明 | 浅色 | 深色 | 高对比度 |
|---|---|---|---|---|
| `CkanElevationLow` | 卡片、工具条默认海拔 | `0 1 3 0 #12000000` | `0 1 4 0 #40000000` | `0 0 0 0 #00000000` |
| `CkanElevationMedium` | 可交互卡片悬停、浮层 | `0 4 14 0 #1C000000` | `0 6 18 0 #59000000` | `0 0 0 0 #00000000` |
| `CkanElevationHigh` | 对话框、命令面板 | `0 12 32 0 #29000000` | `0 14 36 0 #73000000` | `0 0 0 0 #00000000` |
| `CkanElevationNone` | 显式取消阴影（嵌套卡片不叠两层阴影） | 全零 | 全零 | 全零 |

> 高对比度下一律归零：HC 的层级靠 1px 纯白描边表达，阴影只会把文字边缘糊掉。
> 三档数值都写进 token 而不是留 `BoxShadow="..."` 字面量，理由与 §1.1 同：主题包与高对比度要能整体改写。

### 5.3 新增非主题 Token（尺寸与节奏）

| Token Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `CkanCornerRadiusPill` | `CornerRadius` | `8` | 筛选片 / 徽章 / 搜索框的「圆角矩形」半径（名字沿用 Pill，形态不是胶囊） |
| `CkanSpacingXs` / `Sm` / `Md` / `Lg` / `Xl` | `x:Double` | `4 / 8 / 12 / 16 / 24` | 8pt 间距刻度，替代随手写的 `Margin="13,0,0,0"` |
| `CkanSpacingGutter` | `Thickness` | `16,12` | 页面级外边距 |
| `CkanSpacingStack` | `Thickness` | `0,0,0,8` | 纵向堆叠项间距 |
| `CkanIconSizeSmall` / `Medium` | `x:Double` | `14 / 16` | 行内图标与导航图标 |
| `CkanControlHeightSmall` / `Medium` | `x:Double` | `28 / 32` | 小按钮与标准控件高度 |
| `CkanFocusRingThickness` | `Thickness` | `2` | 焦点环描边厚度（必须 `Thickness`，写成 `x:Double` 会在 `BorderThickness` 上解析失败） |
| `CkanOverlayCardWidth` | `x:Double` | `480` | 浮层卡片（命令面板、抽屉）宽度基准 |

> **别把 Pill 调回 999**：Avalonia 把「超大半径」按 `rx = 宽/2、ry = 高/2` 解释，非正方形容器会渲染成**真椭圆**。
> 旧构建的 75×34 选中徽章因此露成椭圆（见 `docs/design/milestones.md` 的 UI 文案治理条目），现在统一 8px 圆角矩形。

### 5.4 样式类清单（`src/CkanUI.App/Resources/FluentStyles.axaml`）

新增的样式类都是「视图里写 `Classes="..."` 就能用」的语义单元，页面不要再拼行内属性：

- **按钮家族**：`Button.CkanSecondary`（次要实心）、`Button.CkanGhost`（无底悬停才亮）、
  `Button.CkanDanger`（危险操作）、`Button.CkanIcon`（30×30 纯图标）、`Button.CkanSmall`（紧凑高度）。
- **徽章**：`Border.CkanBadge` + `.Success` / `.Warning` / `.Danger` / `.Info` / `.Accent`；
  内部 `TextBlock` 自动接 Caption 字号与 SemiBold，不必逐个写前景色。
- **文本层级**：`TextBlock.CkanPageTitle` / `.CkanSectionTitle` / `.CkanCardTitle` / `.CkanCaption` /
  `.CkanMuted` / `.CkanNumeric` / `.CkanNumericLarge`（后两者 `FontFeatures="tnum"`，数字列对齐不跳动）。
- **页头范式**：`TextBlock.CkanPageTitle` + 同行 `Button.CkanHelper`（20×20、图标 `CkanIconHelp`、`ToolTip.Tip` 承载机制/风险/例外）
  + 下一行 `TextBlock.CkanCaption` 副标题（≤20 汉字，只答「这页做什么」）。正文不再出现成段说明：
  能进 ToolTip 的进 ToolTip，能删的删。19 个页面页签均已按此收敛。
- **可点击卡片**：`Border.CkanCardInteractive`（悬停抬升到 `CkanElevationMedium` + `translateY(-2px)`，
  按下 `scale(0.995)`）与 `Border.CkanCardSelected`（选中态描边）。
- **骨架屏**：`Border.CkanSkeleton` / `.CkanSkeletonLine`（`IsHitTestVisible=False`，不挡点击）。
- **页签**：`TabControl` / `TabItem` 全量自定义模板——表头下方 `Border#PART_TabUnderline`（高 2、圆角 1、
  底色 `CkanAccentBrush`）在 `:selected` 与 `:focus-visible` 时 `Opacity` 0→1、`ScaleX` 0.4→1，
  过渡时长取 `CkanMotionDurationBase`。
- **焦点可见**：`Button/TextBox/ComboBox:focus-visible` 加 `CkanFocusRingBrush` 描边；
  `ListBoxItem`/`DataGridRow:focus-visible` 用 `CkanAccentSubtleBrush` 底 + 1px 环。鼠标点击不触发环（`:focus-visible` 而非 `:focus`）。
- **浮层与细节**：`ToolTip`（`MaxWidth=420`、CardBorder 描边、DialogBackground 底）、`ContextMenu`/`MenuItem`
  （Small 圆角、`:pointerover` 用 `CkanAccentSubtleBrush`）、细滚动条（宽 10）、`ProgressBar`（MinHeight 6）、
  `Separator`/`GridSplitter`（`:pointerover` 才显色）。

> **两条 Avalonia 硬约束**（踩过，别再试）：① `ToolTip` 与 `ContextMenu` **没有** `BoxShadow` 属性，
> 设置会编译期报 `AVLN2000: Unable to resolve suitable regular or attached property BoxShadow`，
> 要给它们阴影只能换自定义 `Popup`；② 自定义 `TabItem` 模板里被样式选择器命中的子元素必须用 `x:Name`
> （`Selector="TabItem:selected /template/ Border#PART_TabUnderline"`）。

### 5.5 骨架屏行为（`src/CkanUI.App/Theming/SkeletonPulse.cs`）

- 用法：`<Border Classes="CkanSkeleton" app:SkeletonPulse.IsEnabled="True" />`。
- 实现：附加属性 `SkeletonPulse.IsEnabled` 挂 `AttachedToVisualTree` / `DetachedFromVisualTree`，
  用 `Animation`（`IterationCount.Infinite`、`PlaybackDirection.Alternate`，`Opacity` 1 ↔ 0.35）`RunAsync`。
- **动效关闭时不闪**：脉动时长取 `CkanMotionDurationBase`，该 token 为 0（`reduced`/`none` 档或系统关闭动画）时直接不启动动画，
  只留静态骨架块——避免为了「好看」违反无障碍设置。
- 附加属性的 `TOwner` 必须是**非静态**类型（`public sealed class SkeletonPulse`）：静态类型作泛型参数会 `CS0718`。

### 5.6 外壳：FluentAvalonia 原生导航（`src/CkanUI.App/MainWindow.axaml`）

> 第 2 轮先做过「手写 Border#NavPanel 折叠图标栏」，第 2 阶段按 FluentAvalonia 官方标准（`external/_fa-tmp/samples/FAControlsGallery/Views/MainView.axaml:73-81`）
> 整体换成了 `ui:FANavigationView`。手写侧栏（含它那 7 条 `Classes.Collapsed` 样式）已删除。

- **控件**：`<ui:FANavigationView PaneDisplayMode="Left" PaneTitle="CKAN" SelectionChanged="OnNavSelectionChanged">`，
  `OpenPaneLength=248` / `CompactPaneLength=52` / `AlwaysShowHeader=False` / `IsSettingsVisible=False` /
  `IsBackButtonVisible=False` / `IsPaneToggleButtonVisible=False`（后三项关掉是因为本项目的后退在内容区顶栏、设置是普通页）。
  收起= `PaneDisplayMode="LeftCompact"`，展开= `Left`；`MainWindow.ApplyPaneState` 切换并同步 `Classes.Collapsed`。
- **分组**：`NavGroups`（`MainWindow.axaml.cs:33`）= 核心业务 / 工具与数据 / 扩展三组，组标题用
  `FANavigationViewItemHeader`（**是列表里的一个元素，不是容器**）；日志与设置放 `FooterMenuItems` 常驻底部。
  插件页（未知键）并入「扩展」组末尾。组标题键 `Ui_NavGroup_Core` / `_Tools` / `_Extensions`，取不到时用中文兜底。
- **图标**：`NavGlyphToGeometryConverter` 把 `NavItem.Glyph` 转 `Geometry` 后包成 `FAPathIconSource`（`FANavigationViewItem.IconSource`）。
- **PaneHeader / PaneCustomContent**：品牌块（26×26 圆角 8 的 `CkanAccentBrush` 方块 + 文字「C」+「CKAN」）与「关于 + 折叠/展开」按钮；
  `IsNavExpanded` / `IsNavCollapsed` 决定显示文字按钮还是 `?` 图标按钮。
- **选中同步**：视图模型仍是唯一真相源——`SelectedNavItem` 变化走 `SyncSelectedNavItem`（带 `_syncingNav` 防回环），
  点击导航走 `OnNavSelectionChanged` 回写视图模型，导航历史不经过视图。
- **主题接入**：三套 `ThemeDictionaries` 里各写一条
  `<StaticResource x:Key="NavigationViewDefaultPaneBackground" ResourceKey="CkanSidebarBackgroundBrush" />` ——
  FluentAvalonia 模板用 `{DynamicResource NavigationViewDefaultPaneBackground}`，覆盖这个键即可让侧栏跟着主题走。
- **键盘**：`Ctrl+B` 折叠/展开侧栏；`Alt+←` / `Alt+→` 前进后退（`Window.KeyBindings` 直接绑命令，视图模型不引用 Avalonia）。
- **状态栏**：右侧常驻版本号（`MainWindow.ApplyStatusBarFacts` 取 `AboutInfoFactory.Create().AppVersion`，
  悬停提示 CKAN 核心版本与运行时版本）——报问题截图即带版本。

### 5.7 设置页：FASettingsExpander + 自动保存（`src/CkanUI.App/Views/SettingsPageView.axaml`）

- **分组即行**：每个设置项一个 `<ui:FASettingsExpander Header="{Binding DisplayName}" Description="{Binding Description}">`，
  编辑器放 `FASettingsExpander.Footer`；**每行的「保存」按钮已删除**（官方样例 `SettingsPage.axaml:68-95` 就没有行内保存）。
- **编辑器按类型选**：整数 `ui:FANumberBox`（`SpinButtonPlacementMode="Inline"`，绑 `NumericValue`/`NumericMinimum`/`NumericMaximum`）、
  布尔 `ToggleSwitch`、枚举 `ComboBox`、短文本单行 `TextBox`、长文本多行 `TextBox`。
- **自动保存**：`SettingItemViewModel.AutoSave`（默认开）+ `AutoSaveDelay`（默认 450ms）——停止输入后落盘；
  连续编辑只保存最后一次（每次编辑取消上一个 `CancellationTokenSource`），非法值（`HasError`）不保存，
  `Revert()` 与 `ApplyLoaded` 会先取消待保存，保存失败写 `FailureHint` 可重试。
- **页面级提示**：错误条与高对比度接管提示改用 `ui:FAInfoBar`（`Severity="Error"` / `"Warning"`，`IsClosable="False"`）。
- **约束**：`FASettingsExpander` 继承 `HeaderedItemsControl`，**没有 `HorizontalContentAlignment`**（写了报 `AVLN2000`）。

### 5.8 实例页：FACommandBar + 右侧 Inspector（`src/CkanUI.App/Views/InstancesPageView.axaml`）

- **顶部命令栏**：`ui:FACommandBar`（`DefaultLabelPosition="Right"`、`ItemsAlignment="Left"`、`OverflowButtonVisibility="Auto"`）。
  主命令 = 添加 / 从 Steam 库导入 / 刷新 / 设为当前 / 启动游戏；次命令（溢出菜单）= 克隆 / 重命名 / 打开游戏目录 /
  编辑启动命令 / 实例自检 / 遗忘此实例，分组用 `ui:FACommandBarSeparator`。窄窗时由控件自己动态溢出，不写自适应逻辑。
- **右侧 Inspector**：主操作 `启动游戏`（`Classes="CkanPrimary"`）+ `更多操作` 按钮挂 `ui:FAMenuFlyout`（6 项 + 分隔符），
  与命令栏绑定同一批命令，所以两处文案必须同源。
- **约束**：`FACommandBar.PrimaryCommands` / `SecondaryCommands` 是 `IAvaloniaList<IFACommandBarElement>`（DirectProperty），
  **不能用绑定填**，只能在 XAML 里静态声明或代码里 Add；`FACommandBarButton` 用 `Label` + `IconSource` 表达内容（`Content` 被忽略），
  但 `Command` / `IsEnabled` / `ToolTip` 照常可用。`FAMenuFlyoutItem` 只能在 `FAMenuFlyout` 里用，放进原生 `MenuFlyout` 会失效。
- **按钮主样式类名是 `CkanPrimary`**（`FluentStyles.axaml:45`），仓库里没有 `accent` 这个类名。

### 5.9 动效：状态类骨骼 + 物理弹簧（第二、三阶段）

第二阶段先把视觉树改到「加动效时不必再改结构」：所有会动的节点都用「状态类 + Opacity + RenderTransform」
表达显隐与位置，节点常驻可视化树，位移与透明度都不参与 Measure/Arrange。第三阶段只往这些状态类上挂
`Transitions` + `SpringEasing`，**视觉树结构一行没改**（除补 `x:Name` / `Tag` 供代码定位）。

#### 5.9.1 两条渲染铁律

- 位移一律挂 `RenderTransform`（`TransformOperations` 字符串语法），**绝不改 Margin/Height/Width**。
- 浮层（日志抽屉、未保存条）与内容同处一个 Grid 单元格叠放，而不是 `DockPanel` / `Grid` 里单独占一行。
- **禁止 `IsVisible` 硬切**：`IsVisible="False"` 会在第一帧把节点从渲染树剔除，退场过渡直接消失。改用
  `Classes.is-open` / `.is-active` 加 `IsHitTestVisible`（隐藏时鼠标穿透），透明度与位移全部由样式类提供。
- 裸类选择器必须带类型前缀：`Selector=".view-layer"` 会编译失败（`AVLN2200: Can not find parent Style Selector`），
  必须写成 `Selector="Control.view-layer"`；Border/ListBox/StackPanel/ScrollViewer 都派生自 `Control`，一条规则即可全命中。

#### 5.9.2 状态类 → 过渡映射（`src/CkanUI.App/Resources/FluentStyles.axaml`「动效骨骼（第三阶段）」段）

| 选择器 | 隐状态 / 静止态 | 显状态 | 过渡（弹簧档位 + 时长 token） | 用途 |
| --- | --- | --- | --- | --- |
| `Border.overlay-drawer` | `Opacity=0` + `translate(0px,260px)` | `.is-open` 归零 | 档位 2 + `CkanMotionDurationSpringSlide`；Opacity 用 `CkanMotionDurationBase` + `CubicEaseOut` | 底部日志抽屉 |
| `Border.floating-pill-bar` | `Opacity=0` + `translate(0px,40px) scale(0.96,0.96)` | `.is-open` 归零 | 档位 2（`InitialVelocity=-2`，微冲量）+ SpringSlide | 未保存改动浮条 |
| `Control.view-layer` | `Opacity=0` + `scale(0.98,0.98) translate(0px,6px)` | `.is-active` 归零 | 档位 1 + `CkanMotionDurationSpringSoft` | 空态/数据态、未选中/已选中面板 |
| `Control.stagger-card` | 就绪原位（`Opacity=1` + `translate(0px,0px)`） | — | 档位 1 + SpringSoft | 一级卡片入场锚点，由 `StaggerAnimator` 驱动 |
| `Control.stagger-card.stagger-prepare` | `Transitions={x:Null}` + `Opacity=0` + `translate(0px,14px)` | 摘类瞬间弹回 | **必须置空过渡**，否则回起点这一步也会被动画化 | 错峰起跑预备态 |
| `Control.is-invalid` | 标记类，无视觉效果 | — | 档位 4 + `CkanMotionDurationSpringImpulse` | 校验失败冲量 |
| `TextBox.is-invalid, ComboBox.is-invalid` | `BorderBrush={DynamicResource CkanDangerBrush}` | — | — | 错误描边 |
| `Control.shake-impulse` | `Transitions={x:Null}` + `translate(8px,0px)` | 摘类瞬间释放 | **必须置空过渡**，靠"瞬间拉偏→放手"产生冲量 | 抖动蓄力帧 |
| `ui\|FANavigationViewItem` | `RenderTransform="translate(0px,0px)"` | — | 档位 2 + SpringSlide；Background/BorderBrush 走 `BrushTransition` | 导航项悬停/按下 |
| `ui\|FANavigationViewItem.nav-catch-up` | 静止 | `translate(4px,0px)` | 同上 | 导航项被选中时的追赶形变 |

全局 `Button`：`RenderTransform="none"` → 弹簧档位 3（`CkanMotionDurationSpringCrisp`）；
`:pressed` 覆盖为 `scale(0.96,0.96)` + `CkanMotionDurationFast` + `CubicEaseOut`（按下要跟手，不能用弹簧；
松手回到 `pressed` 之外时才会用档位 3 的弹簧回弹）。

#### 5.9.3 四档黄金弹簧与收敛门禁

Avalonia 的 `SpringEasing` 实现 `IEasing.Ease(double progress)`，引擎把 `Duration` 归一化成归一化进度 t ∈ [0, 1] 再喂给弹簧求解器。
若 `t=1` 时弹簧还没收敛到 1，动画结束的最后一帧会肉眼可见地跳一下。**工程铁律：|Ease(1)−1| < 0.002 且尾部斜率≈0。**

| 档位 | Mass | Stiffness | Damping | InitialVelocity | Duration token | 场景 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 临界阻尼（稳重零回弹） | 1 | 180 | 26 | 0 | `CkanMotionDurationSpringSoft` = 320 ms | 页面卡片入场、视图层切换、大抽屉收起 |
| 2 微欠阻尼（灵动跟手） | 1 | 220 | 22 | 0（浮条用 −2） | `CkanMotionDurationSpringSlide` = 360 ms | 抽屉弹出、浮条弹出、导航滑动 |
| 3 高刚度微反馈（清脆回弹） | 1 | 320 | 24 | 0 | `CkanMotionDurationSpringCrisp` = 250 ms | 按钮松手回弹 |
| 4 强欠阻尼冲量（衰减震荡） | 1 | 650 | 14 | 30 | `CkanMotionDurationSpringImpulse` = 450 ms | 表单必填项校验轻颤 |

实测 `Ease(1.0)`（Avalonia 12.1.4）：档位 1 = 1.00000、档位 2 = 1.00002、浮条 −2 = 1.00003、档位 3 = 0.99999、档位 4 = 0.99876
（误差 0.00124，小于 0.002 门槛，可直接用）。
`tests/CkanUI.App.Tests/SpringConvergenceTests.cs` 会正则抓 `FluentStyles.axaml` 里**所有** `<SpringEasing>` 构造真实
`SpringEasing` 断言收敛，并锁定这五组参数（`Mass` 必须恒为 1）——以后改弹簧会被测试挡住。

#### 5.9.4 弹簧时长 token（受「动效强度」设置控制）

`src/CkanUI.ViewModels/Pages/Settings/UiMotionScale.cs` 新增 `enum MotionSpring { Soft, Slide, Crisp, Impulse }` 与
`Milliseconds(intensity, systemAnimationsEnabled, MotionSpring)`：`full` → 320/360/250/450；`reduced` → 一律 90（保留"有反馈但不晃"）；
`none` → 0（瞬变）。`src/CkanUI.App/Theming/MotionHost.cs` 把结果写进
`CkanMotionDurationSpringSoft` / `SpringSlide` / `SpringCrisp` / `SpringImpulse` 四个资源，样式里用 `{DynamicResource}` 引用，
所以"关闭动画"是运行时换资源值生效，不需要改样式。

#### 5.9.5 错峰级联入场（`src/CkanUI.App/Animations/StaggerAnimator.cs`）

`MainWindow` 监听 `PageHost.PropertyChanged` 的 `ContentControl.ContentProperty`，页面换完（`DispatcherPriority.Loaded`）
调 `StaggerAnimator.PlayAsync(PageHost, token)`：收集所有 `Classes` 含 `stagger-card` 的 `Control` → 全部加 `stagger-prepare`
（瞬间落到 +14px / Opacity 0）→ 等 16 ms 让合成器提交起始帧 → 每 25 ms 摘一张的 `stagger-prepare`，卡片各自用档位 1 弹回原位。
**`finally` 里无条件清掉全部 `stagger-prepare`**：页面被快速切走时若提前取消，卡片会停在 `Opacity=0`（等于页面空白），
所以取消路径也要复位。悬挂点是外壳的 `PageHost`，不是 19 个页面根标签——新增页面无需接线。

#### 5.9.6 校验冲量轻颤（`src/CkanUI.App/Animations/MotionFeedback.cs`）

`ShakeAsync(target)` = 加 `is-invalid`（挂上档位 4 的弹簧过渡）→ 加 `shake-impulse`（过渡置空，瞬间拉到 +8px）→
等 16 ms（一帧）→ 摘掉 `shake-impulse`：弹簧从 +8px 释放回 0，自动跑出 3 次衰减摆动，无需关键帧。

- **modpack 编辑器**：`ModPackEditorPageViewModel.ApplyIssues` 收集 `Severity==Error` 的 `Field` 到
  `InvalidFields`（去重、剔空字段，且列进 `IsInfrastructureProperty` 白名单——校验反馈不能把表单标脏）→
  `ModPackEditorPageView.axaml.cs` 订阅这条通知，用 `Tag` 上的字段名在可视树里找输入框：命中 `ShakeAsync`，未命中 `ClearInvalid`。
- **实例对话框**：`InstanceFormPanel.axaml.cs` 监听 `DialogViewModelBase.HasValidationError`，true 抖一次、false 清描边；
  根 Border 必须 `ClipToBounds="False"` + 预置 `RenderTransform="translate(0px, 0px)"`，否则 ±8px 会被父容器切边。

#### 5.9.7 导航选中指示器：与 UI 规格的差异（诚实记录）

规格要求"跨项物理滑动 + 追赶形变"，并给了 `Border#NavFloatingPill` 的样式。本仓库的导航已换成 FluentAvalonia 的
`ui:FANavigationView`，选中高亮是控件模板内的 `Border#SelectionIndicator`，由
`FANavigationView.AnimateSelectionChanged`（`external/_fa-tmp/src/FluentAvalonia/UI/Controls/NavigationView/FANavigationView.cs:3282`，
`PlayIndicatorAnimations` 在 `:3484`）以固定的 **600 ms 组合动画**驱动，**时长与 Easing 都不对外暴露**。
因此这里只做我们能控制的那一层：导航项自身挂 `nav-catch-up`（`translate(4px,0px)`，110 ms 后摘掉）表达"追赶"，
指示器的滑动沿用 FluentAvalonia 原生动画。若将来要真正拿到弹簧滑动，得自绘导航列表（放弃 `FANavigationView` 的分组与溢出行为）。
追赶位移用**代数守卫**（`MainWindow._navPulseGeneration`）：连点同一项时只有最新一次有权摘类，否则早到的 `Task.Delay`
会把新一次刚挂上的类提前摘掉；换项时则先摘掉上一项的类，避免它 110 ms 后回来误摘当前项。

#### 5.9.8 可打断性

`TransformOperationsTransition` / `DoubleTransition` 在属性变更时会从**当前插值位置**接着跑，所以抽屉半空中反向点击、
导航快速扫过都不会跳变；页面的错峰入场由 `CancellationTokenSource` 取消上一轮（换页即中止，并在 `finally` 复位）。
物理动画只改 `RenderTransform` / `Opacity`，**不触发 Measure/Arrange**，连点时表单与列表尺寸不动。

#### 5.9.9 外壳三明治、防裁剪、未保存数据链

- **三明治外壳**（`src/CkanUI.App/MainWindow.axaml`）：`Grid RowDefinitions="*,28" ClipToBounds="True"`
  → Row 0 内容（`PageHost`）+ 同格 `VerticalAlignment="Bottom"` 的浮层容器（抽屉 260px、浮条居中）
  → Row 1 合并状态栏（左状态、中「日志 (最近 N 行) · 命中 …」开关、右版本号）。
- **防裁剪二分法**：滑动抽屉视口 `ClipToBounds="True"`（滑出屏幕的部分要裁掉）；
  需要抖动/缩放的表单输入框及其父容器 `ClipToBounds="False"` + 预置 `RenderTransform`。
- **未保存提示条的数据链**：页面实现 `CkanUI.ViewModels.IUnsavedChangesAware`
  （`HasUnsavedChanges` + 可选的 `SaveChangesCommand` / `DiscardChangesCommand`，两者为 null 时外壳隐藏对应按钮）→
  `ShellViewModel` 在 `OnCurrentPageChanged` 换监听页并重算 `HasUnsavedChanges` / `UnsavedSaveCommand` / `HasUnsavedSaveAction` 等派生属性 →
  浮条走 `Classes.is-open="{Binding HasUnsavedChanges}"`。当前实现者：`SettingsPageViewModel`、`ModPackEditorPageViewModel`、
  `ModConfigPageViewModel`、`ModsPageViewModel`；文案键 `Ui_Common_UnsavedHint` / `Ui_Common_Unsaved_Discard` / `Ui_Common_Unsaved_Save`。
- **已删除**：`src/CkanUI.App/Theming/MotionPrimitives.cs`（`SlideInAsync` / `StaggerEntranceAsync`）与
  `src/CkanUI.App/Theming/ShakeBehavior.cs`——职责已由样式类 + `StaggerAnimator` / `MotionFeedback` 接手，
  `tests/CkanUI.Architecture.Tests/MotionSkeletonTests.cs` 会断言这两个文件与两个方法名不再出现、新两文件必须存在。
- **保留旧经验**：关键帧必须在 `TranslateTransform` 之类 `Animatable` 上 `RunAsync`（在 `Control` 上报「属性不属于宿主」）；
  `TranslateTransform` 不是 `Control`，写 `x:Name` 报 `AVLN2000`；附加属性的 `TOwner` 不能是静态类型（`CS0718`）；
  字符串形式的 `TransformOperations` 与 `TranslateTransform` 对象是两种类型，不要混用。
- **标题条拖动**：`MainWindow.axaml` 设了 `ExtendClientAreaToDecorationsHint="True"`（客户区铺到标题栏上），
  系统就不再代管标题栏的拖动与双击最大化——必须由顶栏自己调 `BeginMoveDrag`。
  实现：`src/CkanUI.App/MainWindow.axaml.cs` 的 `OnTitleBarPointerPressed`（顶部 34px 拖拽条与侧栏品牌区共用），
  命中 `Button` 祖先的事件直接返回（前进/后退不是拖窗）、`e.ClickCount == 2` 切最大化/还原、最大化状态下拖动先还原；
  由 `tests/CkanUI.Architecture.Tests/WindowChromeTests.cs` 锁住这段接线（删掉即整窗不可移动）。
- **豁免**：`Theming/SkeletonPulse.cs` 的骨架屏脉动只循环 `Opacity`、不位移不参与布局，属于加载态表现，不算物理动画。

### 5.10 表面分层（第 2 阶段 P1）

- **画布与卡片分家**：`CkanCanvasBackgroundBrush`（Dark `#0D1015` / Light `#F4F6F9` / HC `#000000`）只做窗口底，
  卡片用 `CkanCardBackgroundBrush`（Dark `#151921` / Light `#FFFFFF`），面板用 `CkanSubtleBackgroundBrush`；
  去掉了「全屏 1px 硬边框」，卡片边框改为顶光渐变（`CkanCardBorderBrush`，HC 档仍是纯白实线）。
- **输入内凹**：`TextBox` / `ComboBox` 背景 `CkanRecessedInputBrush`（Dark `#0A0D12` / Light `#EFF2F6`），比卡片更暗才读得出凹陷。
- **可点卡片悬停**：`Border.CkanCardInteractive:pointerover` = 抬到 `CkanElevatedCardHoverBrush` + `CkanElevationMedium` + `translateY(-2px)`；
  **不再**把边框刷成强调色（一屏多卡时会互相抢）。
- **对比度口径**：强调色文字绝大多数落在卡片上，所以 `AccentContrast` 的两极取**卡片表面**（Light `#FFFFFF` / Dark `#151921`），
  不是画布；画布更深，实际对比度只会更高。`AccentContrastThemeTests.SurfacePoles_MatchTokensAxaml` 读的也是卡片刷。

