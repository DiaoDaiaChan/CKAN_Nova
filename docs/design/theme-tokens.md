��、角标、状态栏细节、小标签 |
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

### 4.2 THM-03 材质与透明（Mica / Acrylic，已落地控件级玻璃）
- **降级链路**：`Mica` (Win11 build ≥ 22000) → `AcrylicBlur` (Win10 `win10-x64` 构建) → 纯色 (`None`)。
- **Token 联动**：新增 `Ui.Material.WindowBackdrop` 资源槽位；当材质生效时，`CkanSurfaceBackgroundBrush` 的 Alpha 透明度动态调整，同时禁用投影高度以免界面变脏。
- **控件级落地（本轮）**：窗口级 Mica 之外，按钮/胶囊/列表行/页签/菜单/输入框统一取 `CkanGlass*` 系列（见 §5.2.1）——不依赖平台材质，三套主题都能表达「玻璃面」；高对比度降级为纯黑不透明 + 纯白描边。

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

### 5.2.1 磨砂玻璃材质 Token（MAT，三变体）

| Token Key | 语义说明 | 浅色 | 深色 | 高对比度 |
|---|---|---|---|---|
| `CkanGlassFillBrush` | 玻璃面默认填充（半透明，让下层背景透上来） | `#8CFFFFFF` | `#14FFFFFF` | `#000000` |
| `CkanGlassFillHoverBrush` | 玻璃面悬停填充（比默认更亮一档） | `#B8FFFFFF` | `#24FFFFFF` | `#111111` |
| `CkanGlassBorderBrush` | 顶亮底暗的 1px 渐变描边（亚克力厚度感） | 白→黑 三段 | 白→白 三段 | `#FFFFFF` 纯色 |
| `CkanGlassBorderHoverBrush` | 悬停描边（转向强调色） | 强调蓝三段 | 强调蓝三段 | `#FFFF00` 纯色 |
| `CkanGlassShadow` | 玻璃面海拔（内顶光 + 柔外影） | 见 Tokens.axaml | 见 Tokens.axaml | 全零 |
| `CkanGlassShadowHover` | 悬停海拔（内顶光更亮、外影更沉） | 见 Tokens.axaml | 见 Tokens.axaml | 全零 |
| `CkanGlassShadowAccent` | 强调色按钮专用（外影带强调色） | 见 Tokens.axaml | 见 Tokens.axaml | 全零 |

> 高对比度下玻璃必须退化：半透明填充在纯黑底上会变成一层灰雾，HC 直接把填充压成不透明、描边压成纯白。
> 按钮不直接吃 `CkanGlassShadow`——`Button` 没有 `BoxShadow` 属性，投影只能挂在 `Border` 上；本轮的实际观感来自「半透明填充 + 渐变描边」，阴影只用于卡片/浮层。


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

`ShakeAsync(target)` = 加 `is-invalid`（换红描边）→ 对控件的位移通道踢一记 10 px 冲量
（`TransformSpring.For(target, SpringSpec.Impulse).Kick(10)`，与样式里档位 4 的 `k650 c14 v0=30` 同参数）。
冲量由帧驱动弹簧自己衰减（首峰接近请求幅度、约 3 次摆动后停），**不再有「加类 → 等 16 ms → 摘类」那套猜帧机制**：
那套做法靠「瞬间拉偏再放手」骗出冲量，帧没对齐时会漏拍。方法签名照旧返回 `Task`，但已经同步完成，调用方 `await` 不会让出线程。

- `Control.shake-impulse`（`translate(8px,0px)` + `Transitions={x:Null}`）**保留但不再驱动视觉**：
  `tests/CkanUI.Architecture.Tests/MotionSkeletonTests.cs` 把这条选择器钉成动效骨骼契约，删样式会让门禁红；
  真正产生位移的是弹簧引擎。`Control.is-invalid` 同理只留描边语义。
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
因此这里只做我们能控制的那一层：导航项被选中时给它的位移通道踢一记 6 px 冲量
（`MotionFeedback.PulseNav(item)` → `TransformSpring.For(item, SpringSpec.Snappy).Kick(6)`）表达"追赶"，
指示器的滑动沿用 FluentAvalonia 原生动画。若将来要真正拿到弹簧滑动，得自绘导航列表（放弃 `FANavigationView` 的分组与溢出行为）。
冲量是自衰减的，**没有延时、没有代数守卫**：原来那套 `_navPulseGeneration` + `Task.Delay(110 ms)` 摘类的写法
（连点同一项时，早到的延时会把新一次刚挂上的类提前摘掉，所以不得不用代数比较）随 `nav-catch-up` 一起退场；
现在连点只会叠加冲量，不会打断当前摆动。`ui|FANavigationViewItem.nav-catch-up` 仍被门禁钉着，保留但不驱动视觉。

#### 5.9.8 可打断性

*`TransformOperationsTransition` 在目标值二次变化时会从**当前插值位置**接着跑* ——这句以前写在这里，实测是**错的**：
Avalonia 12.1.4 会把新目标当成一次全新过渡，**从 t=0 重新计时、速度归零**。位置是连续的（所以不跳），
但观感是"顿一下再起步"——这正是"没有完整的中断、不流畅"的机制层根因（`DoubleTransition` 同理）。

所以位移与形变的"可打断"改由**代码帧驱动**接管（§5.9.10）：`SpringChannel.Retarget` 只换目标、保留速度，
胶囊被连点页签半路拦截时是"被接住拐弯"而不是急停再起步。样式里的 `Transitions` 继续负责抽屉 / 浮条 / 导航项的
悬停与显隐（单程过渡，不会在半空被反向），页面错峰入场由 `CancellationTokenSource` 取消上一轮。
帧驱动只写 `RenderTransform` 的**子对象属性**（`TranslateTransform.X` / `ScaleTransform.ScaleX`），
既不触发 Measure/Arrange，也不会被样式里的 `TransformOperationsTransition` 当成新目标反复重启。
注意：`Transitions` 挂/摘的旧套路（"瞬间拉偏→等一帧→放手"）本身就是在猜帧，已随 `MotionFeedback` 改写一并删除。

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

#### 5.9.10 帧驱动弹簧引擎（`src/CkanUI.App/Animations/`）

样式里的 `SpringEasing` 只能给出"从 A 到 B 的一条曲线"，目标中途一变就重新计时（§5.9.8）。
要真正可打断，位移必须由**自己的时间基准**积分。三个文件分工：

| 文件 | 职责 | 关键常量 |
| --- | --- | --- |
| `SpringChannel.cs` | 一根标量弹簧（位置 / 速度 / 目标）；`Retarget` 保留速度，`Kick` 反解初速让首峰等于设计幅度 | `MaxSubstep = 1/960 s`、`MaxFrameSlice = 0.05 s`、静止阈值 0.25 px + 4 px/s（缩放通道 0.0004 + 0.005） |
| `MotionFramePump.cs` | 每个 `TopLevel` 一台泵：`RequestAnimationFrame` 循环，`dt` 实测（首帧 1/60、封顶 1/30），没有活跃轨道就停止排帧 | `ConditionalWeakTable<TopLevel, MotionFramePump>` |
| `TransformSpring.cs` | 把 2~4 根通道接到控件的 `RenderTransform`：**只在构造时**赋一次 `TransformGroup`，之后每帧只改子属性 | `For(control, spec)` 每控件一根，连点叠加冲量 |

弹簧预设（代码侧，与样式里的四档同源）：

| 预设 | Mass | Stiffness | Damping | ζ | 实测 0→300 px |
| --- | --- | --- | --- | --- | --- |
| `SpringSpec.Glide` | 1 | 380 | 39 | 1.00（实测过冲 −0.30 px） | 0.483 s 停 |
| `SpringSpec.Snappy` | 1 | 420 | 30 | 0.73（过冲 9.55 px ≈ 3.2%） | 0.483 s 停 |
| `SpringSpec.Bounce` | 1 | 560 | 26 | 0.55（过冲 36.6 px） | 0.567 s 停 |
| `SpringSpec.Impulse` | 1 | 650 | 14 | 0.27（与档位 4 同参） | 只用于冲量，不用于归位 |

- **顶栏胶囊与激光条**：`MainWindow.ApplySectionTabIndicatorGeometry` 不再拼 `TransformOperations.Parse("translate(...) scaleX(...)")`，
  改为 `_pillSpring.MoveTo(x: x, scaleX: pillScale)` / `_barSpring.MoveTo(x: barX, scaleX: barScale)`：
  位移走 `Glide`（零过冲，胶囊不会啃到相邻页签的文字），宽度走 `Snappy`（回弹感只留给形变）。
  首次定位（`_indicatorPlaced == false`）传 `snap: true` 直接落位，否则冷启动时胶囊会从 0 处横穿整条页签栏飞过来。
  页签条尚未完成首次排版（`Bounds.Width <= 0`）时，沿用 `DispatcherPriority.Loaded` 补一次定位。
- **动效强度三档**由 `MotionBudget` 从资源 token 反推（`page > 0` → 完整；否则 `fast > 0` → 精简；都为零 → 关闭）：
  `none` 一律瞬时落位、不排帧、不留半截中间态；`reduced` 把位移与冲量压到 **45%**（`ReducedAmplitudeScale`），
  并取消页面级编排与排队入场——"有反馈但不铺开"，与 `UiMotionScale` 的口径一致。token 未注入（设计期、单测没有
  `Application`）时按完整处理，避免把"没配"误判成"关掉动效"。
- **离开可视树**：`Advance` 发现控件已不在树上就 `SnapAll` 并返回 false，帧泵立刻摘掉该轨道——页面切走后不会有幽灵帧泵拖着跑。
- **还没挂上树**：换页那一拍新内容常常还没进可视树，`TopLevel.GetTopLevel` 返回 null。这里**不** `SnapAll`（那会把入场压成一帧瞬移），
  保留起点姿态并等 `AttachedToVisualTree` 到了再起跑；只有确实已被切走（曾经挂过又离开）才直接落位。
- **门禁**：`tests/CkanUI.App.Tests/SpringChannelTests.cs`（12 项：换目标保速度、打断后无瞬移且不是一两帧落位、临界阻尼不过冲、
  欠阻尼必须有可见回弹、0.5 s 内收敛、冲量首峰等于设计值、连点叠加不重启、`SnapTo` 立即停、帧率无关、
  排队期间不走不攒速度且到时自己出发、换目标不带等待时清掉排队）；
  `tests/CkanUI.App.Tests/TransformSpringTests.cs`（位移/形变只写在子属性上、`RenderTransform` 本身只赋值一次、
  不透明度通道会夹到 [0,1] 且只在传了 `opacity` 时才写、缩放以中心为原点、每控件复用同一根）。
- **整页过渡仍然不挂**（`PageHost.PageTransition = null`，注释里记录了三种整页过渡的实测缺陷：双影、亮度 38.3→28.6→34.7 的
  凹陷、叠化重影）。它们共同的病根是"两页同时在屏幕上"；换场表现改由**单页编排**承担（§5.9.11）——换页仍瞬时替换，
  只有新内容浮入，屏幕上永远只有一页，所以既无重影也无亮度凹陷。

#### 5.9.11 交互反馈编排：让"有反应"看得见（`MotionBudget` / `InteractionMotion` / `EntranceChoreographer`）

上一版只把"不流畅"修好了，观感仍偏弱：单帧幅度小、页面入场根本没有、按压没有任何触感。
这一层补的是**看得见**，同时保证三档强度都受同一个预算约束。

| 文件 | 职责 | 关键常量 |
| --- | --- | --- |
| `MotionBudget.cs` | 从 `CkanMotionDurationPage/Fast` 反推档位（完整 / 精简 / 关闭），给出幅度系数与是否允许页面编排 | `ReducedAmplitudeScale = 0.45` |
| `InteractionMotion.cs` | 按下时让**可交互表面**往中心收一点，松手弹回；表面识别复用 `PointerAuraLayer.FindSurface`（按钮、页签、树项、列表项、卡片…），数据行与菜单项不参与 | `PressedScale = 0.95`（`SpringSpec.Tactile`）、`PressInsetPixels = 12` |
| `PageRevealSpec.cs` | 把页签索引差翻译成换页起点姿态：横切以水平 28 px 为主轴（左切镜像）、一级导航换页只上浮 22 px；三档统一走 `MotionBudget` | `DirectionalHorizontalPixels = 28`、`DirectionalVerticalPixels = 10`、`LiftOnlyPixels = 22`、`StartScale = 0.986`、`DirectionalStartOpacity = 0.30`、`LiftOnlyStartOpacity = 0.34` |
| `EntranceChoreographer.cs` | 从 `PageHost` 往下六层找"有 ≥2 个可见子元素"的容器，最多取 8 个，按 30 ms 错峰上浮；首层不足 3 个（典型的"头部 + 内容"两子级根）时沿子级最多/面积最大的那支继续下钻补齐 | `OffsetPixels = 18`、`StepMilliseconds = 30`、`ReducedOffsetPixels = 4`、`MinTargets = 3`、`MaxTargets = 8` |

- **排队而不是定时器**：错峰不是"等 30 ms 再改目标"，而是让弹簧自己扣等待时间——`SpringChannel.Retarget(target, holdSeconds)`
  在排队期间只扣时长、不积分、不攒速度，到点自己出发（`IsHeld` 可观测，`IsMoving` 仍为真以保持帧泵存活）。
  好处是掉帧不会让队列错位，也不会出现"排到第三个时前两个已经停了"的空档；取消排队只需再 `Retarget` 一次不带 `hold`。
- **页面入场（带方向、只淡一次）**：`MainWindow.PlayPageReveal(int direction)` 先让 `PageHost` 同步落到
  `PageRevealSpec.For(direction)` 给出的姿态，再交给 `Glide` 弹簧回到 `0 / 0 / 1 / 1`。
  **两个方向两条姿态**：同组页签横切（`direction = ±1`）走 `+28 / -28 px` 水平主轴、垂直只剩 10 px、不透明度 0.30——
  位移主轴跟着指示器方向走，宽屏上一眼看出"往哪边切了"；一级导航换页（`direction = 0`）没有横向可读的参照，改成 22 px 纯上浮 + 0.34，
  不硬塞假方向。两条都缩放 0.986。
  水平上限取 28 px 是因为 `PageHost` 所在 Panel 不裁剪：再大，低不透明度的边缘会压到侧栏上；横切时同时给满垂直分量会像"斜着甩进来"，所以只留 10 px。
  一级元素随后只做**位移**错峰（`Play(..., includeOpacity: false)`），不再压一次透明度——
  两层一起淡会变成"淡两次"，中间那一段反而更闷。起点与第一次目标切换在同一个调度回合里完成，渲染前就已落位，不会闪一帧原位。
  仅完整档执行整页与块级编排；精简档跳过这两层，只留文字的轻量淡入（§5.9.12）。
- **按压触感**：只在 `PointerPressed`/`Released`/`Exited` 上驱动，不改模板、不碰布局；`SpringSpec.Tactile`（`900/45/1`，ζ=0.75、
  过冲约 2.8%）让松手有一点回弹但不至于弹成玩具。缩放下限按尺寸算：`ScaleFor(width, height)` 取
  `clamp(1 - 12px / max(w,h), 0.95, 1)`——小按钮按 5% 收（看得出"按下"），1000px 宽的卡片只收 12px 边
  （否则整块塌 32px，像被压扁）。悬停中的卡片样式已把 `RenderTransform` 换成 `TransformOperations`，
  按压仍然接管它（按下这一瞬间的触感优先于 +2px 的悬停抬升），松手后由样式重新接管。
- **样式抢走 RenderTransform 的兜底**：可点卡片的 `pointerover` 会把 `RenderTransform` 换成 `TransformOperations`。
  `TransformSpring.MoveTo`/`Kick` 每次显式触发时把 `TransformGroup` 收回来一次（`ReclaimRenderTransform`），
  但**不**逐帧重赋——逐帧赋正是"越动越僵"的老病因（§5.9.8）。
- **幅度口径**：导航追赶冲量 4 → **6 px**；校验抖动与导航追赶都过 `MotionBudget.Amplitude(...)`，精简档只有 45%，关闭档为零。
- **门禁**：`tests/CkanUI.App.Tests/MotionChoreographyTests.cs`（档位由 token 反推、完整档真正排队且最终全部归位、
  精简档一起出发且只留 4 px、关闭档一次编排都不发生）、`tests/CkanUI.App.Tests/MotionFramePumpTests.cs`（真控件上的打断连续性）、
  `tests/CkanUI.App.Tests/TextRevealChoreographerTests.cs`（文字按 7 px / 12 ms 逐行淡入、精简档一起淡且只留 2 px、
  关闭档不动、空白与隐藏文本不占队列、重复入场从 0 重来）。
- **不做的事**：仍不改任何被 `MotionSkeletonTests` / `SpringConvergenceTests` 钉住的样式；错峰与按压全部走 `RenderTransform`
  子属性，不动 `Margin`/`Height`/`Width`/`Padding`，不触发 Measure。

#### 5.9.12 文字逐行上屏（`TextRevealChoreographer`）

页面切到另一页签时，先由 §5.9.11 把整页与块级容器浮入，再由这一层让**文字自己快速逐行出现**：
按视觉顺序（前序深度优先）取页面里的 `TextBlock`，每个先落到"上浮 7 px + 不透明度 0"，再交
`SpringSpec.Glide` 淡回原位，行间节拍 12 ms。20 行文字 0.23 s 扫完，是"内容正在加载"的观感，不是逐字蹦。

| 项 | 口径 |
| --- | --- |
| 收集范围 | `PageHost` 往下 10 层、最多 48 行、扫描上限 2400 个可视节点 |
| 跳过 | 不可见、`Opacity <= 0`、空白文本（`Text` 与 `Inlines` 都为空）、`DataGridRow` 内的单元格文字（表格行错落会像抖动） |
| 完整档 | 上浮 7 px，节拍 12 ms |
| 精简档 | 上浮 2 px，不排队（全部一起淡） |
| 关闭档 | 一次都不发生 |
| 与块级的关系 | 块先动、字后到；两层各用自己的 `TransformGroup`，互不覆盖（父级位移叠加子级位移，属预期） |
| 页内页签切换 | 首次遇到 `TabControl` 时订阅 `SelectionChanged`：换页签不换页，新内容此前是硬出现；订阅后对新选中的内容宿主补齐整套入场（先 `ScrollRevealMotion`/`FeedArrivalMotion` 接管，再块级与本类文字，模板未套上时排到 `Loaded` 再来一次），每个 `TabControl` 只订阅一次 |

- **节拍为什么比块级快**：文字是密集对象，按块级的 30 ms 排 20 行要 0.6 s，看起来像卡顿；12 ms 才有"快速扫过"的速度感。
- **模板晚一拍**：`PlayPageReveal()` 里同步调用一次，返回 0（`TextBlock` 还没实例化）时在 `DispatcherPriority.Loaded` 重试一次，
  避免整页文字静默出现在屏幕上。
- **淡出不单独做**：换页仍是瞬时替换（§5.9.10 已说明两页同屏的三种缺陷），旧页没有淡出帧；观感上的"淡入淡出"
  来自新页低不透明度起步 + 文字逐行跟上，而不是两页交叉淡化。

#### 5.9.13 提示条入场（`BannerMotion`）

`Border.CkanInfoBanner` 的显隐由 `IsVisible` 绑定控制：控件从不在树上到在树上是一次硬切换，
样式里那条 `DoubleTransition Opacity` 从来没有中间帧（透明度没有机会变）。这一层监听 `IsVisible` 变 true 的那一刻，
先压到"上浮 8 px + 不透明度 0"，再交 `SpringSpec.Glide` 淡回原位——错误提示、警告条出现时有了"因果感"。

| 项 | 口径 |
| --- | --- |
| 挂载点 | `MainWindow.PlayPageReveal()`：每次换页对 `PageHost` 扫一遍提示条 |
| 已可见的提示条 | 挂载时立即入场一次（页面带着错误状态打开也看得见） |
| 完整档 | 上浮 8 px |
| 精简档 | 上浮 3 px |
| 关闭档 | 只挂监听，不做任何写入（不透明度与 `RenderTransform` 保持原样） |
| 订阅去重 | `ConditionalWeakTable` 保证每个提示条只订阅一次；处理器是静态方法，控件销毁后无反向引用 |

- **为什么要摘掉 `Transitions`**：样式给提示条挂了 `DoubleTransition(Opacity)`，逐帧写不透明度会被过渡再平滑一次，
  起点永远落不下去（读出来还是 1）。提示条本身没有可交互状态，摘掉过渡不影响其它观感；这正是 §5.9.8「过渡与逐帧驱动不能共存」的又一例。
- **门禁**：`tests/CkanUI.App.Tests/BannerMotionTests.cs`（出现时真的从 0 淡入且不是一帧落位、挂载时已可见的也入场、
  精简档只抬 3 px、关闭档不动、重复挂载不叠成两点出发）。

#### 5.9.14 浮层弹簧接管（`SurfaceMotion`）

底部日志抽屉（`Border.overlay-drawer`）与未保存浮条（`Border.floating-pill-bar`）原本靠样式里的
`TransformOperationsTransition + SpringEasing` 播 260 px / 40 px 的位移。这条通道只有一个目标值：收起途中又点开，
过渡从 t=0 重算、速度归零——而抽屉恰恰最容易被连点。这一层只监听 `Classes` 里的 `is-open`，XAML 与绑定一行不改，
位移与不透明度改由弹簧通道带速度反向。

| 项 | 口径 |
| --- | --- |
| 接管范围 | `overlay-drawer`（`SpringSpec.Glide`，260 px）与 `floating-pill-bar`（`SpringSpec.Snappy`，40 px + 0.96 缩放） |
| 接入点 | `MainWindow.PlayPageReveal()` 首行 `SurfaceMotion.Attach(this)`，`ConditionalWeakTable` 去重 |
| 状态源 | 仍只看 `is-open` 类；`IsVisible`、`IsHitTestVisible` 绑定不动 |
| 三档 | 完整档 260/40 px；精简档按 45% 收（117/18 px）；关闭档直接落位、不排帧 |
| 首次接管 | 先摘 `Transitions`（本地值压过样式），再按当前类 `snap` 到目标，避免首帧闪一下 |

- **为什么只接管这两个**：它们是窗口级单例、位移量固定、最容易连点；`view-layer` 分散在十几处、部分还叠着悬停类，
  由 §5.9.15 单独处理（跳过悬停类，其余逐个按类接管）。
- **门禁**：`tests/CkanUI.App.Tests/SurfaceMotionTests.cs`（开合真的走帧、反向时不瞬移、浮条缩放跟着长大、
  关闭档不排帧、重复挂载不加订阅）。

#### 5.9.15 视图层弹簧接管（`ViewLayerMotion`）

页面内的“同格叠放层”——空态 ↔ 数据态、未选中 ↔ 已选中——用 `Control.view-layer` + `is-active` 互斥。
样式给的是 6 px 上浮 + 0.98 缩放 + 不透明度 0→1，走 `TransformOperationsTransition`。这类切换最容易被打断：
先渲染空态、请求回来再叠数据态，过渡每次都从 t=0 重算，观感是“停一下再重新起步”。

| 项 | 口径 |
| --- | --- |
| 接管范围 | 带 `view-layer` 的控件（`InstancesPageView` 四处、`ModsPageView`、`ModsSearchPanelView`、`ModDetailsView` 两处） |
| 状态源 | 只看 `Classes` 里的 `is-active`；`Classes.is-active` 绑定（如 `ModDetailsView` 的 `IsEmpty`/`HasSelection`）一行不改 |
| 几何 | 未激活 → `y = 9 px`、`scale = 0.97`、`opacity = 0`；激活 → `0 / 1 / 1`，`SpringSpec.Glide`（样式里的 6 px / 2% 在 1080p 上几乎看不出来，接管时放大到 9 px / 3%） |
| 三档 | 完整档 9 px / 0.97；精简档按 45% 收（4.05 px / 0.9865）；关闭档直接落位 |
| 接入点 | `MainWindow.PlayPageReveal()` 里 `BannerMotion.Attach(PageHost)` 之后，`ConditionalWeakTable` 去重；迟到的层由根节点节流补扫接管（§5.9.20） |

- **跳过的类**：`CkanCardInteractive`（`:pointerover` 自己写 `translateY(-2px)`、`:pressed` 写 `scale(0.995)`）、
  `stagger-card`、`overlay-drawer`、`floating-pill-bar`。接管这些会把悬停/按压位移吃掉，所以放行给样式。
- **为什么先摘 `Transitions`**：与 §5.9.13 同理——挂着 `TransformOperationsTransition` 时逐帧写 `RenderTransform`
  会被过渡再平滑一次，弹簧几乎冻结。视图层没有别的过渡需要保留（不透明度也由同一通道接管）。
- **首帧不闪**：接管时先按当前类 `snap` 一次，再开始监听；绑定晚一拍的 `is-active` 由 `Classes.CollectionChanged` 接住。
- **门禁**：`tests/CkanUI.App.Tests/ViewLayerMotionTests.cs`（隐藏层从 0.98/6 px/0 出发、激活真的长大淡入并落位、
  反向时不瞬移、带 `CkanCardInteractive` 的不接管且 `RenderTransform` 同一实例、关闭档落位、重复挂载不加订阅、换页之后才挂上树的视图层同样被补扫接管）。

#### 5.9.16 对话框内容编排（`DialogWindow`）

对话框本身已有入场/退场（`CubicEaseOut` 200 ms：不透明度 0→1、缩放 0.96→1，实现在
`src/CkanUI.App/Views/Dialogs/DialogWindow.cs` 的 `OnOpened`/`OnClosing`）。这一层只补“正文自己浮上来”：
窗口整体放大淡入的同时，块级元素与文字各做一段**纯位移**的错峰——不再压透明度，否则窗口淡一次、文字再淡一次，
中间那段会闷掉（与 §5.9.7 整页“只淡一次”同一口径）。

| 项 | 口径 |
| --- | --- |
| 挂载点 | `DialogWindow.OnOpened` → `DispatcherPriority.Loaded` → `DialogEntrance.Play(this)`（内部 = `EntranceChoreographer.Play(..., includeOpacity: false)` + `TextRevealChoreographer.Play(..., includeOpacity: false)`） |
| 为什么排到 Loaded | `OnOpened` 时模板刚套上，同步扫可视树常常还扫不到正文 `TextBlock` |
| 三档 | 与页面一致：完整档块级 18 px / 文字 7 px；精简档 45%；关闭档两个编排都直接返回 |
| 不重复淡入 | 窗口 `Opacity` 由关键帧动画负责，正文只做位移 |

- **门禁**：`tests/CkanUI.App.Tests/DialogEntranceTests.cs`（正文先落位移起点且不压不透明度、60 帧后回零；关闭档正文 `RenderTransform` 保持 `null`）。
- **已知覆盖缺口**：`DialogWindow.Show()` 在无头 NUnit 线程上会因既有的 `Animation.RunAsync` 抛
  `The current SynchronizationContext may not be used as a TaskScheduler`（与本次改动无关，改动前就有），
  所以门禁测的是 `DialogEntrance.Play` 本身，窗口接线靠代码评审与真机观感。

#### 5.9.17 卡片悬停弹簧（`HoverLiftMotion`）

样式里 `Border.CkanCardInteractive:pointerover` 用 `TransformOperationsTransition` + `CkanMotionDurationFast`
（90 ms）播 2 px 抬升。鼠标横着扫过一排卡片时，每张卡都要先把上一段速度清零再起步，抬升看起来是一段段“顶上去”。
这一层按类接管这 2 px，位移之间共享速度：

| 项 | 口径 |
| --- | --- |
| 范围 | `Classes.Contains("CkanCardInteractive")`/`CkanGuideCard`/`CkanGuide` 的控件，`ListBoxItem`/`TreeViewItem`/`DataGridRow` 整行，以及普通 `Button`（`MaxDepth=12`、`MaxVisited=3000`）；命中即 `PointerEntered`/`PointerExited` 接弹簧 |
| 幅度 | 完整档卡片 `LiftPixels=2` 向上（`y: -2`）、整行 `RowLiftPixels=1`、普通按钮 `ButtonLiftPixels=1.5`；精简档 45%，关闭档直接返回 |
| 规格 | `SpringSpec.Glide`（与浮层、视图层同一曲线，避免同屏两套手感的观感差） |
| 与 `:pressed` 共存 | 样式的 `:pressed` 与 `InteractionMotion` 改缩放通道，本类只改 `translateY`，互不覆盖；本地值优先于样式，所以样式那条 90 ms 过渡不再生效 |
| 与样式悬停共存 | 样式还给了背景刷与阴影（`CkanElevatedCardHoverBrush`/`CkanElevationMedium`），这部分照旧由样式过渡播，只有位移被接管 |

- **门禁**：`tests/CkanUI.App.Tests/HoverLiftMotionTests.cs`（悬停抬到 -2 后落回 0；反向时单帧位移 < 0.4 且仍在负值区，证明带速回位；精简档 45%；关闭档 `RenderTransform` 保持同一实例；`Attach` 只认带类卡片且第二次返回 0）。
- **未做**：FluentAvalonia 3.1.0 的导航选择指示器动画时长写死在库内（其 XML 文档无相关公开 API），要接管只能自绘覆盖层胶囊并做坐标映射，风险高于收益，暂不做。

#### 5.9.18 滚动入场（`ScrollRevealMotion`）

设置页、模组列表这类信息流一页放不下：页面入场只编排"换页那一刻"已经存在的元素，继续往下滚，新露出来的行是硬出现的。
这一层挂在滚动容器上，元素**各自**补一次入场，滚动快时多条弹簧并行，不等前一条演完：

| 项 | 口径 |
| --- | --- |
| 挂载点 | `MainWindow.PlayPageReveal(int direction)` → `ScrollRevealMotion.Attach(PageHost)`（每个 `ScrollViewer` 只订阅一次）；`DialogEntrance.Play` 里同样挂一次，对话框内的长列表一起覆盖 |
| 触发 | `ScrollViewer.ScrollChanged`：元素**底边**跨进 `Viewport.Height + 32 px` 阈值即算露出来 |
| 幅度 | 完整档下移 18 px + 不透明度 0 → `Glide` 弹簧回位；精简档 3 px；关闭档整层不挂（`Attach` 返回 0） |
| 只演一次 | 演过按**数据对象**（`DataContext`）记而不是按控件：虚拟化列表回收容器后换成新数据，行控件还是同一个，按控件记会把滚出来的每一行都当成"见过" |
| 首屏不重演 | 挂载时的首轮扫描只登记、不播放——首屏由页面入场负责，两层都演会变成"淡两次" |
| 范围 | 从 `ScrollViewer.Content` 往下找**可见子级最多**的那一层（设置页大分组里还套着卡片列表，只看第一层会变成几片大块在挪），最多 400 个，跳过不可见项；同数量时保留更浅的那层 |
| 行层不缓存 | 行层每次评估重选（`MaxVisited` 1600）：列表晚到位时缓存会钉在"空态壳子"上，之后一直演错误的两三个元素 |
| 找滚动容器 | 深度上限 20 层、最多 12000 个节点：`ScrollViewer` 常嵌在模板/列表第 11–15 层（ships、saves 页就是这样），按挑行层的 10 层限制会整页扫不到 |
| 与虚拟化共存 | 只处理已经实现的容器；容器被回收后换了数据会按新数据重新演一次（见"只演一次"） |
| 挂载时机 | `Attach` 当场扫一遍，之后跟着根节点的 `LayoutUpdated` 补扫（节流 8 Hz）：页面模板、数据与滚动容器常晚于内容切换成形，只赌换页那一拍会一个容器都扫不到 |
| 首屏登记 | 视口高度还是 0（布局未完成）时不登记任何“看过”，等布局就绪的那次补扫再登记，否则会把后面的行一起标掉 |

- **门禁**：`tests/CkanUI.App.Tests/ScrollRevealMotionTests.cs`（跨进底边的行确实还在下移位置且半透明、45 帧后落位；首屏元素不被滚动层碰；回滚再下来不重演；仍在视口下方的行不动；关闭档 `Attach` 返回 0 且不写变换；嵌在 12 层容器里的 `ScrollViewer` 照样接管；容器换数据后重新演）与 `ScrollRevealAttachTests.cs`（换页后才挂载/才布局的滚动容器同样接上；布局前挂载不会把首屏之后的行误标为看过）。
- **为什么不用 `EntranceChoreographer` 的 hold 队列**：滚动触发的元素是"谁露出来谁演"，彼此不在同一时间轴上；硬排队会让快速滚动时后面的行明显滞后于手指。

#### 5.9.19 数据到达入场（`FeedArrivalMotion`）

页面换页那一刻列表通常还是空的，数据是异步回来的：页面入场演完，行才“啪”地整块出现。
这一层订阅 `ItemsControl` 的数据源增删，数据到达后按 10 行一批、24 ms 间隔错峰浮上来。

| 项 | 口径 |
| --- | --- |
| 挂载点 | `MainWindow.PlayPageReveal(int direction)` 里 `ScrollRevealMotion.Attach` 之后；`DialogEntrance.Play` 同样挂 |
| 触发 | `ItemsSource` 的 `CollectionChanged`，按 `DispatcherPriority.Loaded` 合并成一次（同一批 add 只演一遍）；数据先到容器后实现时补试一次 |
| 幅度 | 完整档下移 14 px + 不透明度 0 → `Glide` 弹簧；精简档 4 px、无错峰；关闭档不挂（`Attach` 返回 0） |
| 只演新行 | 容器演过写进 `ConditionalWeakTable`；筛选/追加只动新容器，老行不重演 |
| 跳过 | `ComboBox` 的下拉项不是信息流；不可见或尚未实现的容器跳过（`ContainerFromIndex` 为 null） |
| 上限 | 单次最多 10 行，超出的直接显示——错峰是节奏，不是排队 |

- **门禁**：`tests/CkanUI.App.Tests/FeedArrivalMotionTests.cs`（三行同时到达时最后一行确实落在第一行后面、60 帧全部落位；后加一行只演新行；关闭档 `Attach` 返回 0 且不写变换）。
- **hold 单位**：`SpringChannel.Retarget(target, holdSeconds)` 收的是**秒**；编排层把 `StepMilliseconds` 先 `/ 1000d` 再乘序号，别直接塞毫秒（塞 24 会变成 24 秒）。

#### 5.9.20 覆盖矩阵：每一处动效由谁驱动、迟到控件怎么接住

动效分散在十来个模块里，最容易出的不是"做得不好看"，而是"这页有、那页没有"。这张表把每个模块的触发源、
接管方式与补扫口径钉在一起，加新页面时按表核对，不再靠运气：

| 模块 | 触发 | 接管对象 | 迟到控件 |
| --- | --- | --- | --- |
| `SpringChannel` / `TransformSpring` / `MotionFramePump` | 谁改目标谁驱动 | 任意 `Control` 的位移/缩放/不透明度 | 引擎层，不涉及扫描；控件还没挂树时保留起点姿态等 `AttachedToVisualTree` |
| `PageRevealSpec` + `PlayPageReveal(int direction)` | 换页（`PageHost.Content` 变化） | 整页 | 换页那一拍必有，不需要补扫 |
| `EntranceChoreographer` | 换页、对话框打开 | 页面块级元素的错峰入场 | 由 `PlayPageReveal` 当场跑；模板晚一拍时在 `Loaded` 重试一次 |
| `TextRevealChoreographer` | 换页、对话框打开 | `TextBlock` 逐行淡入 | 同上（`Loaded` 重试） |
| `SurfaceMotion` | `Classes` 里 `is-open` 增删 | `overlay-drawer`、`floating-pill-bar` | 根节点 4 Hz 补扫（`MotionScan`） |
| `ViewLayerMotion` | `is-active` 增删 | `view-layer`（空态 ↔ 数据态） | 根节点 4 Hz 补扫 |
| `BannerMotion` | `IsVisible` 变 true | `CkanInfoBanner` | 根节点 4 Hz 补扫 |
| `HoverLiftMotion` | `PointerEntered`/`Exited` | `CkanCardInteractive`、`CkanGuideCard`、`CkanGuide`、`ListBoxItem`/`TreeViewItem`/`DataGridRow` | 根节点 4 Hz 补扫 |
| `ScrollRevealMotion` | `ScrollChanged` + `LayoutUpdated` | 滚动容器里跨进底边的行 | 自带 8 Hz 补扫（§5.9.18） |
| `FeedArrivalMotion` | `ItemsSource` 的 `CollectionChanged` | 数据到达后的前 10 行 | 根节点 4 Hz 补扫 + `Loaded` 重投一次 |
| `InteractionMotion` | `PointerPressed`/`Released`/`Exited` | 可交互表面的按压缩放 | 事件驱动，控件在树上才有指针事件，不需要补扫 |
| `MotionFeedback` | 校验失败、导航追赶 | 冲量轻颤、导航项抖动 | 调用方按控件触发 |
| `DialogEntrance` | `DialogWindow.OnOpened` | 对话框正文块级 + 文字 + 滚动/列表 | 打开时挂一次；对话框内的迟到控件走各模块自己的补扫 |

- **补扫为什么是 4 Hz**：这是"捡漏"不是逐帧动画。滚动与窗口缩放时 `LayoutUpdated` 一帧能来好几次，
  每次全树扫一遍会拖慢滚动本身；250 ms 一次足够覆盖"模板晚一拍""数据晚到""互斥切换换控件"这三类迟到。
- **补扫为什么安全**：每个 `Attach` 都用 `ConditionalWeakTable` 按控件去重，重复调用只捡新控件，不会叠加订阅——
  `MotionScan` 再按"根节点 + 模块名"去重，同一次换页里调用三次也只挂一个 `LayoutUpdated` 处理器。
- **门禁**：`ViewLayerMotionTests.LayerMountedAfterThePageRevealIsStillPickedUp`（换页之后才加进树的视图层，
  在布局回调后仍被接管到 9 px / 0.97 起点）；`SurfaceMotionTests`、`BannerMotionTests`、`HoverLiftMotionTests`
  各自覆盖"接管"/"重复挂载不叠加"；`ScrollRevealAttachTests` 覆盖容器晚挂载。

### 5.10 表面分层（第 2 阶段 P1）

- **画布与卡片分家**：`CkanCanvasBackgroundBrush`（Dark `#0D1015` / Light `#F4F6F9` / HC `#000000`）只做窗口底，
  卡片用 `CkanCardBackgroundBrush`（Dark `#151921` / Light `#FFFFFF`），面板用 `CkanSubtleBackgroundBrush`；
  去掉了「全屏 1px 硬边框」，卡片边框改为顶光渐变（`CkanCardBorderBrush`，HC 档仍是纯白实线）。
- **输入内凹**：`TextBox` / `ComboBox` 背景 `CkanRecessedInputBrush`（Dark `#0A0D12` / Light `#EFF2F6`），比卡片更暗才读得出凹陷。
- **可点卡片悬停**：`Border.CkanCardInteractive:pointerover` = 抬到 `CkanElevatedCardHoverBrush` + `CkanElevationMedium` + `translateY(-2px)`；
  **不再**把边框刷成强调色（一屏多卡时会互相抢）。
- **对比度口径**：强调色文字绝大多数落在卡片上，所以 `AccentContrast` 的两极取**卡片表面**（Light `#FFFFFF` / Dark `#151921`），
  不是画布；画布更深，实际对比度只会更高。`AccentContrastThemeTests.SurfacePoles_MatchTokensAxaml` 读的也是卡片刷。

#### 5.9.21 开关选中反馈：让"勾上"有手感（`TogglePopMotion`）

筛选条上的胶囊、视图切换、日志等级开关都是 `ToggleButton`，勾上只换背景色（`BrushTransition`），形状一点没动，
点完像没点。`TogglePopMotion` 给每个 `ToggleButton` 订阅 `IsCheckedProperty`，勾上时打一次缩放冲量。

| 项 | 口径 |
| --- | --- |
| 幅度 | `PopScale = 1.035`（峰值 +3.5%），精简档 `MotionBudget.Amplitude` 收到 +1.58%，关闭档不接管 |
| 实现 | `TransformSpring.KickScale(amount)`：对 `scaleX/scaleY` 通道各打一次初速度冲量，峰值由 `SpringChannel.Kick` 反解 |
| 为什么不用两次 `MoveTo` | 同一拍里"先设 1.035 再设回 1"会让目标等于当前值，弹簧判定已静止，观感上等于没动（实测踩到） |
| 接管时机 | 由 `MotionScan.Subscribe` 统一附带：任何被补扫的根（页面、对话框）都会顺手接上，并作为补扫项捡迟到的开关 |
| 与按压的关系 | 与 `InteractionMotion` 共用同一个 `TransformSpring` 实例，按下收、松开弹各改各的目标，不会互相覆盖成跳变 |
| 不做 | 不给未带 `CkanPill` 的普通复选框加弹跳（原生复选框有自己的视觉反馈）；不改模板、不动 `IsChecked` 语义 |
| 门禁 | `TogglePopMotionTests`（勾上真的放大再落回 1、挂载时已选中的不凭空动、精简档幅度更小、关闭档零改动、重复接管为 0） |

#### 5.9.22 表格行的悬停（`HoverLiftMotion` 的行分支）

`DataGridRow` 原来只有 `translate(2px,0)` 的样式过渡，模组表格里横向扫过一行行时每行都从零起步。
现在 `DataGridRow` 与 `ListBoxItem`/`TreeViewItem` 同走 row 分支：向上抬 `RowLiftPixels = 1`，与其他行共享速度；
样式里的横向 2 px 保留（本类只改 `translateY`），两者叠加即"抬起 + 右移"。

- **门禁**：`HoverLiftMotionTests.GridRowsLiftByTheRowDistance`（`DataGridRow` 被接管且落到 -1 px）。

#### 5.9.23 按钮悬停：把"1 px 静态抬升"改成速度接力的弹簧

全局 `Button` 基础样式给自己的 `:pointerover` 设 `translateY(-1px)`，过渡是 `TransformOperationsTransition` +
`CkanMotionDurationSpringCrisp`（250 ms）。单个按钮看着没问题，但工具条、对话框底部、卡片操作区这种"按钮挨着按钮"的
地方，鼠标横扫过去时每一个都要把速度清零再起步，观感是一顿一顿的；而 `CkanGuide` 引导按钮又因为同时被
`HoverLiftMotion` 接管，抬升距离在两类按钮之间不一致。

现在普通 `Button` 也进 `HoverLiftMotion`：

| 项 | 口径 |
| --- | --- |
| 距离 | `ButtonLiftPixels = 1.5`（样式原值 1 px 略加重，横扫工具条时才看得出方向） |
| 弹簧 | 按钮用 `SpringSpec.Snappy`（m=1, k=420, c=30，ζ≈0.73，带一点点回弹），不再用临界阻尼的 `Glide`——按钮要"跟手"而不是"庄重" |
| 卡内按钮 | 卡片/引导卡内部的按钮**不**接管：整张卡已经在抬 2 px，再叠 1.5 px 会让按钮浮到卡面之上，看起来像布局错位（`LiftOf` 沿 `GetVisualAncestors()` 判一次） |
| 与按压的关系 | 与 `InteractionMotion` 共用同一个 `TransformSpring` 实例：悬停改 `translateY`、按下改 `scale`，松手只回 `scale`，`translateY` 保持悬停值 |
| 不接管谁 | 导航项（`FANavigationViewItem`）、菜单项、`DataGridRow` 之外的表格内控件都不在页面根下，不受影响；模板里的滚动条按钮也不在扫描范围 |
| 门禁 | `HoverLiftMotionTests.PlainButtonsLiftByTheButtonDistance`（1.5 px 落位）、`ButtonsInsideALiftingCardAreLeftToTheCard`（卡内按钮零接管） |
| 引导箭头 | 引导卡/引导按钮里的 `GuideArrow` 与宿主一起动：宿主抬 2 px，箭头同时右滑 `ArrowTravelPixels = 3`；箭头用 `Snappy`，跟宿主抬升同一条曲线。找不到就不缓存（模板/数据晚到后仍能接管） |
#### 5.9.24 交互面覆盖矩阵（谁在演、谁明确不演）

`FluentStyles.axaml` 里带交互态（`:pointerover` / `:pressed` / `:selected` / `:focus-visible`）的选择器共 66 条。
按"这个面上的位移/缩放由谁驱动"归口如下——凡是列在左边的面，都已经不再依赖 `TransformOperationsTransition`。

| 交互面 | 动效归属 | 幅度与曲线 |
| --- | --- | --- |
| `Button` 全家族（Primary/Secondary/Ghost/Danger/Chip/Icon/Helper/Tab/DropDownButton） | `HoverLiftMotion` + `InteractionMotion` | 悬停 `ButtonLiftPixels=2`（Snappy）；按下按边收 12 px（下限 0.95，锚点落在按下点） |
| `Button.CkanGuide` | 同上，外加箭头 | 卡片级 2.75 px；卡内 `GuideArrow` 同拍右滑 4 px |
| `Border.CkanCardInteractive` | `HoverLiftMotion` + `InteractionMotion` | 悬停 2.75 px；按下按边收 6 px（`CardInsetPixels`，卡片比按钮大，12 px 会像整块塌） |
| `Border.CkanGuideCard` | 同上 | 悬停 2.75 px + 箭头 4 px；按下按边收 6 px |
| `Border.CkanCard` / `CkanEmptyStateCard` 容器 | 只悬停（若带 Interactive 类） | 普通容器按下不缩：边框/分隔线会跟着位移，故 `InteractionMotion` 对非 Interactive 的 `Border` 直接返回 |
| `ListBoxItem` / `TreeViewItem` / `DataGridRow` / `TabItem` | `HoverLiftMotion` 行分支 | 悬停 1.35 px（整行比卡宽，2 px 像抖动） |
| `Border.overlay-drawer` / `Border.floating-pill-bar` | `SurfaceMotion` | 抽屉 260 px、浮条 40 px + `scale(0.96)`，Glide/Snappy |
| `Control.view-layer` | `ViewLayerMotion` | 12 px + `scale(0.97)` + 淡入 |
| `Border.CkanInfoBanner` | `BannerMotion` | 11 px + 淡入（先摘掉样式上的 `DoubleTransition`，否则逐帧写入会被再平滑一次） |
| `ToggleButton`（CkanPill、视图切换、日志等级） | `TogglePopMotion` | 勾选瞬间 `KickScale(+3.5%)`，用冲量而不是两次 `MoveTo`（同拍设回原值会被判静止） |
| 页内 `TabControl` 换页签 | `TextRevealChoreographer.Watch` | 对 `PART_SelectedContentHost` 补整套入场（滚动/数据到达接管 + 块级 + 文字） |
| `ScrollViewer` 里的信息流 | `ScrollRevealMotion` | 跨过底边 24 px（精简 4 px），只演一次、回滚不重演、虚拟化换数据重新演 |
| `ItemsControl` 新增数据 | `FeedArrivalMotion` | 18 px（精简 5 px），24 ms 错峰，最多 10 项 |
| `TextBlock` 正文 | `TextRevealChoreographer` | 9.5 px（精简 2.5 px），12 ms 行间节拍，最多 48 行，跳过空白/隐藏/`DataGridRow` 内文字 |
| 外壳页签胶囊 + 激光条 | 帧驱动弹簧（`MainWindow` 直连 `TransformSpring`） | 位置/宽度/不透明度三通道 |
| `TextBox` / `ComboBox` 的 `:focus`、`:focus-visible` | **明确不接** | 输入框缩放会让光标与命中区错位；焦点环是纯画笔变化，交给 `BrushTransition` |
| `MenuItem` / `CheckBox` / `ScrollBar` / `GridSplitter` | **明确不接** | 前两者在 `PopupRoot` 里（见 §5.9.23 的覆盖限制），后两者是结构件，位移会被误读成布局在动 |
| `ui|FANavigationViewItem` | **不接（并行改动范围）** | FluentAvalonia 的选中指示器 600 ms 写死在库内，XML 文档无公开可调项 |

#### 5.9.25 外壳悬停与样式抢变换的兜底

`HoverLiftMotion` 原来只在 `PlayPageReveal` 里对 `PageHost` 挂载，标题栏、状态条、工具条这些**页面外面**的按钮永远轮不到。
真实窗口审计（headless 起 `MainWindow`，逐页统计交互面、再合成 `PointerEntered` 采样按钮）改前结果：`instances` / `downloadstats` / `modtext` / `logs` 四页 0/4，外壳 20 个按钮 0/6。

| 项 | 口径 |
| --- | --- |
| 接入点 | `MainWindow.PlayPageReveal` 里与 `SurfaceMotion.Attach(this)` 并列调用 `HoverLiftMotion.AttachShell(this)`，每次换页都跑（幂等，`ConditionalWeakTable` 去重） |
| 遍历预算 | 页面级 `MaxDepth=24` / `MaxVisited=20000`（旧 12/3000 扫不到深页里的按钮）；整窗 `ShellMaxVisited=60000` |
| 跳过 | 只跳 `FANavigationViewItem` 子树：导航项自己的悬停由 `IconHoverRole` 的 `IconHoverShape.Sway` 负责，再叠 1 px 行抬升会互相打架。`FANavigationView` 自身不跳——标题栏按钮就挂在它的模板里 |
| 图标按钮 | `Button.CkanIcon` 不接管：`FluentStyles.axaml` 的 `Button.CkanIcon:pointerover` 明确写 `RenderTransform = none`，硬接管会跟那条 setter 抢 RenderTransform，实测两边都不动（`y=0.00`）。反馈交给 `IconHoverMotion` 的图标旋转 |
| `Apply` 语义 | `LiftOf` 返回 `null` 直接不动（原来回退成 `LiftPixels`，等于对不认识的控件也动手） |
| 样式抢变换的兜底 | `TransformSpring.Advance` 每帧检查 `_target.RenderTransform` 是否还是自己那组，不是就收回来。`:pointerover` 里写 `translateY(-1px)`（`Button.CkanPrimary`）或 `none` 的样式会在飞行中把组换掉，弹簧写的是脱离可视树的旧组，看起来"卡住不动"；只在动画期间收回，停住后不跟样式反复抢 |
| 门禁 | `HoverLiftMotionTests.IconButtonsAreLeftToTheirIconMotion`、`HoverLiftMotionTests.ShellAttachAdoptsChromeButtons`（整窗挂载接管外壳按钮、重复挂载返回 0）；悬停类共 13 项 |
| 审计脚本 | 临时工程 `motionprobe`（不入库）：导航每个 `shell.NavItems`，统计 `btn/card/row/view/sv`，对 4 个可见按钮合成 `PointerEntered`/`PointerExited`，输出 `hoverLift=n/m`；采样相位会抖动（同一按钮在循环里 0.00、单独测 -1.50），只作覆盖趋势证据，不作为门禁 |

改后：24 个页面里 21 个 `hoverLift=4/4`，其余页面首个布局拍前采样会低 1 个（`direct=4` 证明按钮一旦出现就能接管，真实应用靠 `MotionScan` 4 Hz 补扫覆盖）；外壳非图标按钮 `-1.50` 落位。

#### 5.9.26 选中与键盘焦点的弹簧反馈

`src/CkanUI.App/Animations/SelectionMotion.cs`。列表/树/表格的选中态原来只有背景色瞬间切换，键盘 Tab 也只有一圈静态描边。

- 接管范围：`ListBoxItem` / `TreeViewItem` / `DataGridRow`（行容器级，虚拟化回收复用后照样生效）+ 窗口级 `GotFocus` 冒泡。
- 选中：`KickScale(SelectionPop 1.5%)`，`SpringSpec.Snappy`；行容器在选中之后才被虚拟化实现时，由列表 `SelectionChanged` 先在当拍数一次、再在下一拍 `LayoutUpdated` 补数一次（不用 `RequestAnimationFrame`：无头并发跑时帧泵不推，弹不起来）。`NotifySelected(Control)` 是同一入口的公开接缝。
- 键盘焦点：`FocusPop 1.2%`；指针已在控件上（`IsPointerOver`，即鼠标点出来的焦点）跳过，`TextBox`/`ComboBox`/`AutoCompleteBox` 跳过——文本输入自带插入符与滚动，别再叠缩放。
- 为什么用 `KickScale` 而不是位移：悬停已经占住位移通道（y），选中要能叠在悬停态上。
- 接入点：`MainWindow.PlayPageReveal` 内 `SelectionMotion.Attach(this)` + `SelectionMotion.Attach(PageHost)`。
- 三档：`MotionBudget.Amplitude` 统一缩放，`none` 在 `Attach` 入口直接返回。
- 门禁：`SelectionMotionTests` 4 项（选中弹起、键盘焦点弹起、文本输入不动、重复挂载/关闭档不接管）。
- 冲量前先 `control.Transitions = null`：样式里的 `TransformOperationsTransition` 会把写下的 `TransformGroup` 遮成"过渡中的 TransformOperations"，逐帧位移被再平滑一次，看起来卡住不动（与 §5.9.13 提示条同一类问题）。
- 不做：`ComboBox` 弹层（`PopupRoot` 不在可视树里）、`TabItem`（顶栏与页内页签已有自己的指示器动画）。

#### 5.9.27 幅度基准：把"看得见"放进设计值

用户反馈"动画效果不是很明显"。根因不在曲线也不在时长——弹簧早已是帧驱动、可打断的——而是设计幅度按"不抢戏"的保守值定，
实机 150% DPI 下 2 px 的悬停抬升与 7 px 的文字上浮几乎看不出来。这一轮把各模块的位移基准统一提了约 30%，
时长、曲线、错峰节拍、透明度起点全部不动：观感变明显，但节奏没有变快。

| 模块 | 常量 | 旧 → 新 |
| --- | --- | --- |
| 整页入场（有方向 / 同组上浮） | `PageRevealSpec.DirectionalHorizontalPixels` / `DirectionalVerticalPixels` / `LiftOnlyPixels` | 28·10·22 → 36·13·28 |
| 块级错峰 | `EntranceChoreographer.OffsetPixels` / `ReducedOffsetPixels` | 18 / 4 → 24 / 5 |
| 文字逐行 | `TextRevealChoreographer.OffsetPixels` / `ReducedOffsetPixels` | 7 / 2 → 9.5 / 2.5 |
| 滚动入场 | `ScrollRevealMotion.OffsetPixels` / `ReducedOffsetPixels` | 18 / 3 → 24 / 4 |
| 数据到达 | `FeedArrivalMotion.OffsetPixels` / `ReducedOffsetPixels` | 14 / 4 → 18 / 5 |
| 提示条 | `BannerMotion.OffsetPixels` / `ReducedOffsetPixels` | 8 / 3 → 11 / 4 |
| 视图层 | `ViewLayerMotion.ClosedOffsetPixels` | 9 → 12 |
| 卡片悬停 / 行悬停 / 按钮悬停 / 引导箭头 | `HoverLiftMotion.LiftPixels` / `RowLiftPixels` / `ButtonLiftPixels` / `ArrowTravelPixels` | 2·1·1.5·3 → 2.75·1.35·2·4 |

为什么要逐模块改而不是在 `MotionBudget.Amplitude` 里乘一个全局系数：试过，系数会连带动到并行模块（`IconHoverTrack` 的
`NudgePixels` 也走 `MotionBudget.Amplitude`），把它们的门禁（推出距离上限 4.5 px）顶爆。基准属于各模块自己的设计值，
缩放属于档位，两者分开之后"哪一档多大幅度"仍然只有 `MotionBudget` 一个出口。

门禁用例同步改为按常量与 `MotionBudget.Amplitude(...)` 计算期望值，不再钉字面量：把幅度写死进断言，
下一次调基准就得改一堆测试，那种门禁挡不住回归，只会挡住改动。

验证：`CkanUI.App.Tests` 457 项全绿；`dotnet build ckan-ui.sln` 0 警告 0 错误；真实窗口探针 24 页重新采样，
`hoverLift` 与 `popped` 覆盖不变（各页 4/4 项中的图标按钮仍按 §5.9.25 排除），说明提幅度没有破坏接管范围。

#### 5.9.28 平滑滚动：把目标带进视野时不再瞬跳

`src/CkanUI.App/Animations/SmoothScrollMotion.cs`。列表默认走 `ScrollIntoView`：一按方向键内容整屏换位，眼睛要重新找位置。
这里只改"怎么到那儿"——**目标位置仍由可见性算法算**，弹簧只负责滑过去，并且能被下一次按键打断。

| 项 | 口径 |
| --- | --- |
| 触发 | 窗口级 `GotFocus` 冒泡（键盘方向键、Tab、命令式 `EnsureVisible(Control)` 三条路同一个入口） |
| 目标计算 | 控件顶/底相对 `ScrollViewer` 的坐标 + `EdgeMarginPixels=28`（视口不足 1/4 时收到 1/4），夹到 `[0, Extent-Viewport]` |
| 通道 | `SpringChannel`（`SpringSpec.Glide`，静止阈 0.5 px / 6 px·s⁻¹），只驱 Y；X 原样保留（横向流由页面自己处理） |
| 打断 | `Retarget` 前 `SnapTo(当前偏移)`：换目标时位置与速度都连贯，不会跳回原点重新起步 |
| 帧泵 | `MotionFramePump` 从"只推 `TransformSpring`"改成推 `IMotionTicker`（`TransformSpring` 显式实现）；滚动通道与变换弹簧共用同一条 VSync 泵，空闲自动停 |
| 三档 | `none` 直接写 `Offset` 落位、不排帧；`reduced/full` 同样滑（滚动位移不是"装饰幅度"，压缩它会变成别扭的半程滚动） |
| 接入点 | `MainWindow.PlayPageReveal` 与 `DialogEntrance.Play` 各挂一次（幂等） |
| 门禁 | `SmoothScrollMotionTests` 4 项：跨屏滑入不瞬跳、已可见不动、换目标保持当前位置（一帧走完新距离一半以内）、关闭档直落 |

不接：鼠标滚轮与触摸拖动（用户已经在直接控制偏移，再加弹簧会"抢方向"）、横向内容流（`Viewport.Width > Extent.Height` 的那些由 `ScrollRevealMotion` 的横向分支负责入场）。


#### 5.9.29 整页幕布过渡：两页都不透明，只让接缝扫过去

`src/CkanUI.App/Animations/SpringPageSlide.cs`。`TransitioningContentControl` 的`PageTransition` 从"恒为 null"
（换页硬切、只有新页浮入）改成幕布式过渡：**两页始终不透明、互不重叠**，旧页让到一侧、新页从另一侧补位。

| 项 | 口径 |
| --- | --- |
| 为什么以前恒为 null | 三种旧做法（CrossFade+PageSlide、接力淡出、叠化）都要求两页同时在屏幕上且被改不透明度：文字双影、亮度 38.3→28.6→34.7 的凹陷、叠印重影 |
| 现在的形状 | 只动 `TranslateX`：新页 `-sign·travel → 0`，旧页 `0 → +sign·travel`；`sign = forward ? +1 : -1` |
| 行程 | `travel = min(新页宽度或 640, 900) × MotionBudget.Amplitude`（`MaxTravelPixels=900`、`FallbackTravelPixels=640`） |
| 收敛 | 帧泵上的 `SettleTicker` 累计 `SettleSeconds=0.42`（`SpringSpec.Glide` 0.32 s + 余量），到点把两页 `snap` 回 0 |
| 打断 | `CancellationToken` 注册 `Complete`：下一次换页立刻结束等待并把两页归位，新的过渡从干净状态起步 |
| 线程 | 续体可能落在帧泵之外（取消路径），写控件前 `Dispatcher.UIThread.CheckAccess()`，必要时 `InvokeAsync`；真实应用里不额外调度 |
| 三档 | `none` → `PageTransition = null`（瞬时替换）；`reduced` 行程 ×0.45；`full` 全行程 |
| 接入点 | `MainWindow.ApplyPageTransition()`：`PageHost.PageTransition = MotionBudget.IsCollapsed ? null : SlideTransition` |
| 门禁 | `SpringPageSlideTests` 5 项：正向扫过（新页先在外、旧页随后让位、120 帧两页归零、两页不透明度始终为 1）、反向镜像、取消即归位、关闭档不接管 `RenderTransform`、**宿主集成**（真 `TransitioningContentControl` 换 `Content` 时两个呈现器确实横移并回位） |

与页面入场编排的分工：幕布在场时 `PlayPageReveal` 不再给 `PageHost` 本身加减整页姿态——宿主机一淡，旧页跟着一起暗，
就是当初那个亮度凹陷；整页姿态只在 `PageHost.PageTransition` 为空时兜底（设计期、或外部清空）。块级错峰、文字逐行、
滚动/数据到达这些"页内"编排照旧运行，所以观感是"帘子扫过 + 新页内容自己长出来"。

不做：任何不透明度混合（双影的根源）、3D 翻转（文字在 headless/Skia 下会发虚）、按导航层级换方向（`PageHost` 拿不到导航深度，
硬塞方向会出现"同级页签也横着甩"的假语义）。


#### 5.9.30 缩略图淡入：图片"有"的那一刻不再硬切

`src/CkanUI.App/Animations/ImageLoadMotion.cs`。缩略图走 `PathToThumbnailConverter` 同步解码，绑定一到位
`Image.Source` 就变成完整位图，渲染出来是"啪"地出现。这里只做一件事：**源变了就淡一次**（`0 → 1`，同时 0.985 → 1 缩放）。

| 项 | 口径 |
| --- | --- |
| 触发 | `Image.SourceProperty` 变化；挂载时源已存在则立刻演一次 |
| 通道 | `TransformSpring`（`SpringSpec.Glide`）的 opacity + scale，不动布局 |
| 起点 | `opacity 0`、`StartScale=0.985`（经 `MotionBudget.Scaled` 收幅度） |
| 去重 | 按控件 `ConditionalWeakTable` 记上一张源：同一张重复赋值（虚拟化回收容器）不重播，否则滚动时缩略图会闪 |
| 三档 | `none` 直接返回；`reduced` 缩放同步收 45%，淡入保留（淡入不是装饰，是"图出现了"这件事本身） |
| 接入点 | `MainWindow.PlayPageReveal`（`PageHost` 内）、`MainWindow` 自身（背景图在 `PageHost` 外面）、`DialogEntrance.Play` |
| 补扫 | `MotionScan.Subscribe`：模板晚实现的 `Image` 4 Hz 补扫接管 |
| 门禁 | `ImageLoadMotionTests` 4 项：源后到淡入、挂载时已有源也演、同源不重播、关闭档不接管 `RenderTransform` |


#### 5.9.31 页内页签：下划线弹簧 + 内容舞台按方向滑入

`src/CkanUI.App/Animations/TabStripMotion.cs`。页内页签（`TabControl`）原来有两处问题：
下划线靠样式里的 `TransformOperationsTransition + SpringEasing`，连点页签时目标二次变化就从 t=0 重算、速度归零；
内容舞台 `PART_SelectedContentHost` 的样式写了 `TransformOperationsTransition`，但**没有任何代码去改它的 `RenderTransform`**，等于死配置。

| 项 | 口径 |
| --- | --- |
| 接管入口 | `Attach(Visual?)` 广度优先找 `TabControl`，每个订阅一次 `SelectionChanged`；`MotionScan` 4 Hz 补扫晚实现的页签 |
| 下划线 | 新页签 `opacity 0 → 1`、`scaleX 0.35 → 1`（`SpringSpec.Snappy`）；旧页签反向收回 |
| 内容舞台 | 按页签序号算方向：向后 `+12 px → 0`、向前 `-12 px → 0`（`MotionBudget.Amplitude` 收档位幅度） |
| 方向基准 | 接管时记下 `SelectedIndex` 作为起点：`TabControl` 建项时就选中第一项、那一拍没有 `SelectionChanged`，不记就会把第一次切换误判成"无方向" |
| 摘过渡 | 下划线与内容舞台在接管前都 `Transitions = null`，否则逐帧写入会被再平滑一次（起点读出来仍是旧值） |
| 无方向切换 | 首次选中（`sign == 0`）只做半程纵向轻沉，不硬塞水平方向 |
| 三档 | `none` 直接返回；`reduced` 行程 ×0.45，弹簧仍走帧泵 |
| 接入点 | `MainWindow.PlayPageReveal`（`PageHost` 内）、`DialogEntrance.Play` |
| 门禁 | `TabStripMotionTests` 5 项：选中后下划线弹出并落位、旧下划线收回、内容从右侧滑入后归零、回退镜像、关闭档不接管 |

不做：页签下划线的"跨页签滑行"（需要把指示器搬出模板做覆盖层，和顶栏胶囊同一套坐标映射，收益低于复杂度）；
`FANavigationViewItem` 的选中指示器（库内 600 ms 写死，无公开 API）。
