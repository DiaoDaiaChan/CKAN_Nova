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

#### 5.9.22 表格行与列表行的悬停（`HoverLiftMotion` 的行分支）

`DataGridRow` 原来只有 `translate(2px,0)` 的样式过渡，模组表格里横向扫过一行行时每行都从零起步。
现在 `DataGridRow` 与 `ListBoxItem`/`TreeViewItem` 同走 row 分支：向上抬 `RowLiftPixels = 1`，与其他行共享速度；
样式里的横向 2 px 保留（本类只改 `translateY`），两者叠加即"抬起 + 右移"。

- **门禁**：`HoverLiftMotionTests.GridRowsLiftByTheRowDistance`（`DataGridRow` 被接管且落到 -1 px）。
