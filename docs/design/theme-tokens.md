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

### 4.1 THM-02 强调色扩展点
- **能力**：允许用户从 12 种预设色选择或自定义 Hex，并动态派生 6 阶色调（`AccentLight1/2/3`、`AccentDark1/2/3`）。
- **注入方式**：通过 `ColorPaletteResources` 动态替换 `CkanAccentBrush` 与 `CkanAccentTextBrush`。
- **对比度守卫**：强制校验 WCAG 2.2 AA 标准（正文 ≥ 4.5:1，UI 边界 ≥ 3:1），不合规色自动拒绝并提示建议色。

### 4.2 THM-03 材质与透明（Mica / Acrylic）
- **降级链路**：`Mica` (Win11 build ≥ 22000) → `AcrylicBlur` (Win10 `win10-x64` 构建) → 纯色 (`None`)。
- **Token 联动**：新增 `Ui.Material.WindowBackdrop` 资源槽位；当材质生效时，`CkanSurfaceBackgroundBrush` 的 Alpha 透明度动态调整，同时禁用投影高度以免界面变脏。

### 4.3 THM-04 圆角尺度三档切换
- **扩展点**：通过设置项倍率（0 / 1 / 2）整体驱动 `CkanCornerRadiusSmall`、`Medium`、`Large`。
  - 平直模式：全部为 `0`；
  - 标准模式：`2 / 4 / 8`；
  - 圆润模式：`4 / 8 / 16`。

### 4.4 THM-05 背景图与壁纸
- **扩展点**：引入背景源 Token，支持用户配置壁纸、模糊度（Blur）与遮罩暗化（Dim），文字前景色根据背景暗化度自适应增强对比度。

### 4.5 THM-06 密度与字号动态缩放
- **扩展点**：
  - 密度：切换紧凑(24px)、标准(28px)、舒适(36px) 行高，动态更新 `CkanRowHeight` 与 `CkanPagePadding`；
  - 字号：通过乘数因子 (0.9 ~ 1.3) 等比缩放四级字号，保证长文案不裁切。

### 4.6 THM-07 动效强度分级
- **扩展点**：定义 `Motion.Fast` (90ms)、`Motion.Base` (150ms)、`Motion.Page` (200ms)；当检测到系统「减少动画」或用户关闭动效时，时长归零。

### 4.7 THM-08 ~ THM-10 主题包安全、导入导出与无障碍
- **主题包格式**：纯 JSON 数据包（禁止执行任何 XAML/脚本代码），只允许对声明在白名单中的 Token 键值进行重载。
- **高对比度优先**：由 `ThemeGuard` 兜底，检测到 Windows 高对比度开启时强行封锁任何外部主题包重载。
