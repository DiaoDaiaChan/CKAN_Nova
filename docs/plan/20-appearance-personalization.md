# 20 外观个性化（E8）：背景 / 磨砂 / 圆角 / 深色

> 依据：用户指令 m01700「更多的个性化界面（背景，磨砂，圆角，深色等等）支持」。
> 上游：[11 篇](11-tech-stack-decision.md)（Avalonia 12 + `net10.0` 自包含，下限 Win10 1607）、[03 篇](03-ui-spec.md) §12（材质三档降级与动效 token）、[01 篇](01-architecture.md) §10（Presentation 分层）。
> 本文只定义"外观可配置"的能力边界、token 体系、安全约束与工时；不写实现代码。

---

## 1. 目标与非目标

### 1.1 目标（每条都可在界面上点到）

| ID | 目标 | 验收口径 |
| --- | --- | --- |
| P-01 | 深色 / 浅色 / 跟随系统 三选，切换**即时生效**（不重启） | 切换后主界面所有页面无残留旧配色 |
| P-02 | 高对比度变体（跟随系统 `HighContrast`），且**强制压过主题包与用户覆盖** | 开启后所有自定义颜色被忽略，只剩对比度合规的色板 |
| P-03 | 强调色：跟随系统 / 从 12 个预设选 / 自定义 hex；自动派生 6 阶并使用 | 按钮、选中态、焦点环同时变；对比度不达标的强调色被拒绝并给建议色 |
| P-04 | 磨砂（Acrylic）与 Mica 材质，三档降级（Mica → Acrylic → 纯色） | 三档在不同机器上都能显示，降级时有说明而不是静默变丑 |
| P-05 | 圆角尺度三档（0 / 6 / 12px）作用于控件、卡片、对话框；窗口圆角尽力而为 | 切档后列表行、卡片、对话框、输入框同步变化 |
| P-06 | 背景：纯色 / 线性渐变 / 图片（本机文件），带填充方式、暗化、模糊、透明度 | 换背景后文字仍清晰（暗化自动建议值） |
| P-07 | 背景可按实例区分（每个 KSP 实例一套外观） | 切实例后外观跟着切，且不互相污染 |
| P-08 | 密度三档（紧凑 / 标准 / 舒适）影响行高、内边距、图标尺寸 | 列表首屏可见行数明显不同，且不破版 |
| P-09 | 字号缩放 90%–130%，只影响 UI 文案（不影响 Mod 名与标识符等"数据"） | 130% 下德语/俄语长文案不裁切（接 16 篇 §7） |
| P-10 | 动效强度：完整 / 精简 / 关闭，默认跟随系统"减少动画" | 关闭后无过渡动画，但状态变化仍可感知 |
| P-11 | 主题包：把一套 token 打成目录即可分发、可导入、可回滚 | 导入一份第三方主题后外观变化，且**无法执行任何代码** |
| P-12 | 导出 / 导入外观配置（一份 JSON），可分享 | 导入后逐项生效，未知字段被忽略而非报错 |
| P-13 | 恢复默认（单项恢复 + 全局恢复） | 恢复后与首次启动一致 |
| P-14 | 一切个性化**不降低**既有性能门禁（滚动 p99 ≤ 16.7ms）与无障碍门禁 | 见 §14 / §15 |

### 1.2 非目标

- 不做**任意 XAML / 脚本主题**（安全边界，见 §12.2：加载外部 XAML 等于加载可执行代码）。
- 不做动画 GIF / 视频背景（解码与帧率代价不可控）。
- 不做"主题编辑器"式可视化设计器（只做 token 表单 + 实时预览）。
- 不做云同步（外观配置是本地文件，随实例可选）。
- 不改 Core 一行（颜色/材质全部在 Presentation 层）。

---

## 2. 现状取证

### 2.1 老 GUI 没有任何"外观设置"

| 事实 | 证据 |
| --- | --- |
| "深色模式"不是设置，是从注册表**推断**的只读状态 | `GUI/Util.cs`：`public static bool DarkMode => Platform.IsWindows ? Platform.IsWindows11 && WinReg.GetValue(DarkModeKey, "AppsUseLightTheme", 1) is not 1 : ...` —— 且 net481 分支恒为 `false` |
| 外观是逐控件硬编码的 | `GUI/` 下 `BackColor =` **151 处**、`ForeColor =` **88 处**、`SystemColors.` **130 处**、`Color.FromArgb` **5 处** |
| 为此专门做了两个"跟着系统色"的控件 | `GUI/Controls/ThemedListView.cs:10`（注释即 "obeys system colors to look less awful in a dark theme"）、`GUI/Controls/ThemedTabControl.cs:10` |
| 深色下还有补丁式妥协 | `GUI/Controls/ThemedListView.cs:28-37 EnsureReadableGroupHeaders()`：深色时把 `BackColor` 改成 `Color.FromArgb(117,117,117)`，因为 Windows 强制分组标题用蓝字 |
| 没有强调色、没有背景、没有圆角概念 | `GUI/Model/GUIConfiguration.cs` 全字段中无任何 theme / color / background / accent 项（WinForms 也没有圆角概念） |

结论：**"个性化"在老 GUI 里是零**，新做的是从 0 到 1，不存在迁移包袱；也意味着**老 GUI 的 `GUIConfig.json` 里没有外观可迁**（16/20 篇都不必做偏好迁移）。

### 2.2 新 UI 已有的外观基建（避免重复造）

| 已有 | 位置 | 本文的关系 |
| --- | --- | --- |
| 材质三档降级链（Mica→AcrylicBlur→纯色） | [03 篇](03-ui-spec.md) §12（v1.2 回填） | 本文把它做成**用户可选项**并补 Win10 圆角/性能细节 |
| 动效 token（`Motion.Fast/Base/Page` + 缓动） | [03 篇](03-ui-spec.md) §12 | 本文加"强度三档"与"跟随系统减少动画" |
| 分层与硬约束（禁行内 BlurEffect/DropShadowEffect、材质不得硬编码 `TransparencyLevelHint`） | [01 篇](01-architecture.md) §10 | 本文扩为"所有颜色/圆角/字体也必须走 token" |

---

## 3. 设计原则

1. **Token 化**：界面代码里不允许出现颜色字面量、圆角字面量、字号字面量。CI 检查（`grep` 白名单）拦 `#[0-9A-Fa-f]{6}`、`CornerRadius="\d`、`FontSize="\d`。
2. **三层优先级**：`系统/无障碍（最高） > 主题包 > 用户个人覆盖 > 内置默认（最低）`。高对比度模式优先级最高，见 §15。
3. **可逆**：任何个性化都能一键回到默认；主题包可回滚到上一套。
4. **不静默降级**：材质/圆角能力不足时，在设置页写清"这台机器上不可用，原因 X"，而不是悄悄给个纯色。
5. **不装裱数据**：外观不改变任何"数据类"文本的字体（Mod 名、标识符、版本号、路径），避免用户认错 mod。
6. **零遥测**：外观配置只存本地（`%LocalAppData%\CKAN\ckan-ui.json`），不上传、不统计。

---

## 4. Token 体系（8 组）

命名空间统一 `Ui.<Group>.<Name>`，全部定义为 `DynamicResource` 可覆盖的资源键，落位 `Presentation/Themes/Tokens/<Group>.axaml`。

| 组 | 示例键 | 说明 |
| --- | --- | --- |
| Color（语义色） | `Ui.Color.Surface` / `SurfaceAlt` / `SurfaceRaised` / `Text` / `TextMuted` / `Border` / `BorderStrong` / `Accent` / `AccentForeground` / `Danger` / `Warning` / `Success` / `Info` / `Focus` | **只允许语义命名，不允许 `Gray1/Gray2`**；每个键在 Light/Dark/HighContrast 三套字典里各有一个值 |
| Material（材质） | `Ui.Material.WindowBackdrop`（`Mica|Acrylic|None`）/ `Opacity` / `AcrylicTint` / `AcrylicTintOpacity` | 与 `TransparencyLevelHint` 的映射集中在 `Backdrop.cs` |
| Radius | `Ui.Radius.Control`（6）/ `Card`（8）/ `Dialog`（12）/ `Pill`（999）/ `Window`（12） | 三档整体缩放系数 0 / 1 / 2 |
| Density | `Ui.Density.RowHeight`（32/36/44）/ `Padding.Page` / `Padding.Control` / `Icon`（16/20/24） | 三档 |
| Typography | `Ui.Font.Family` / `Mono` / `Body`（13）/ `Caption`（12）/ `Title`（18）/ `Display`（24）/ `Scale`（1.0） | `Scale` 是 0.9–1.3 的乘数，套在下表所有尺寸上 |
| Motion | `Ui.Motion.Duration.Fast/Base/Page` + `Easing.Standard/Exit` | 与 03 篇 §12 同一组；强度档改变的是"是否播放"而非时长 |
| Background | `Ui.Background.Kind`（Color/Gradient/Image）/ `Source` / `Fit` / `Dim` / `Blur` | 见 §9 |
| Elevation | `Ui.Elevation.1..3`（阴影或不含） | 与材质互斥规则见 §7.4 |

**约束**：`ThemeDictionaries` 里定义的资源只能用 `DynamicResource` 引用——官方文档明确「`StaticResource` 会找不到并抛运行时异常」。因此 CI 必须拦 `StaticResource {Ui.*}`。

---

## 5. 主题变体与解析顺序

- 三个变体：`Light`、`Dark`、`HighContrast`（自定义为 `ThemeVariant` 实例，键名 `"HighContrast"`），另有 `Default` 表示"继承上层"。
- 用户选项：`跟随系统` / `浅色` / `深色` → 映射到 `Application.RequestedThemeVariant`（`Default` / `ThemeVariant.Light` / `ThemeVariant.Dark`）。
- 局部对照：设置页的预览卡片用 `ThemeVariantScope` 并排展示浅/深两套（官方用例即"side-by-side theme preview"），**预览不污染全局变体**。
- 解析顺序（从高到低）：`HighContrast（系统开启时强制）` → `主题包 theme.json` → `用户覆盖 ckan-ui.json` → `内置默认`。
- 切换时机：切变体只改 `RequestedThemeVariant` 一处，所有 `DynamicResource` 自动重解析；**禁止**在代码里遍历控件改颜色（否则必然漏）。
- 窗口装饰：改 `Window.RequestedThemeVariant` 会一并影响标题栏/边框的明暗（官方行为），这正是我们想要的。

---

## 6. 强调色

### 6.1 来源

1. **跟随系统**：读注册表 `HKCU\Software\Microsoft\Windows\DWM\ColorizationColor`（REG_DWORD，ARGB）或 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\`；不可得时回退内置蓝。
2. **预设 12 色**：蓝 / 靛 / 紫 / 品红 / 红 / 橙 / 琥珀 / 黄绿 / 绿 / 青 / 天蓝 / 灰蓝（每色附浅色与深色两版，因为同一 hex 在深底上饱和度观感差异大）。
3. **自定义**：`#RRGGBB` 或 `#AARRGGBB`，即时校验。

### 6.2 派生与注入

用官方 `FluentTheme.Palettes` + `ColorPaletteResources`（按变体键 `Light`/`Dark`）注入 `Accent`，并由我们额外派生 6 阶：`AccentLight1/2/3`、`AccentDark1/2/3`（在 HSL 上按 ±8/±16/±24 明度、饱和度微调），这样 `Ui.Color.Accent` 与控件内部的 `SystemAccentColor*` 系列保持一致。

### 6.3 校验（硬门禁）

- 正文文本对比度 ≥ **4.5:1**、大字（≥18.66px 或 ≥14px 粗体）与 UI 边界 ≥ **3:1**（WCAG 2.2 AA）。
- 强调色作为按钮底色时，`AccentForeground` 自动在黑白之间选对比度更高者；两者都不达标 → **拒绝该强调色**，并给出 3 个"最近的合规色"建议。
- 校验函数是纯函数（`ContrastChecker`），单元测试覆盖 500 组随机色。

---

## 7. 材质与透明（"磨砂"）

### 7.1 三档能力

| 档 | Windows 条件 | 实现 | 观感 |
| --- | --- | --- | --- |
| Mica | Win11 build ≥ 22000 | `TransparencyLevelHint = [Mica]` + `ExtendClientAreaToDecorationsHint` | 桌面取色的"云母"底（**Win10 上系统直接退化为纯色**，learn.microsoft.com 明确 "Windows version is below 22000" 时退化为纯色） |
| Acrylic（磨砂） | Win10/11，需 `win10-x64`/`win10-x86` RID 构建（Avalonia 已知限制，GitHub #9055） | `TransparencyLevelHint = [AcrylicBlur, ...]` | 背景模糊透出壁纸，带 tint |
| 纯色 | 任何 | `TransparencyLevelHint = [None]` | 完全不透明 |

降级链由 `Backdrop.Resolve()` 决定，顺序 `Mica → AcrylicBlur → None`，运行期读 `Window.ActualTransparencyLevel` 确认**实际**生效档位，并把结果写进设置页显示（"当前：磨砂（Acrylic）"）。

### 7.2 开关与边界

- 设置项"窗口材质"：`自动（推荐）/ 磨砂 / 纯色`，默认自动。
- 用户关掉材质 → 所有材质的 tint token 归零，避免"半透明但不模糊"的脏观感。
- 最大化/全屏/多显示器切换、DPI 变化时重新解析一次（否则会看到残影）；这类事件只在 `TopLevel` 层监听一次。
- **行内禁用模糊**：列表行、卡片内不做 `BlurEffect`（延续 01 篇 §10 硬约束），模糊只允许在"窗口的整体 backdrop"与"背景图"两处出现。

### 7.3 性能红线

- `AcrylicBlur` 在 Win10 上走 DWM 合成，通常开销可接受；但**一旦叠加"背景图 + 模糊"**就是两次全屏混合，必须走 §14 的预算与实测（SP-46）。
- 软件渲染（Tier 3 机器）时强制降为纯色，并在设置里说明。

### 7.4 与阴影/高度的互斥

材质为 Mica/Acrylic 时，`Ui.Elevation.*` 降为"仅 1px 描边"，不叠加投影（半透明底 + 投影会显脏）。这是 token 层规则，不改代码路径。

---

## 8. 圆角

### 8.1 内部圆角（永远可用）

`Border.CornerRadius` 走 `Ui.Radius.*`；三档尺度 0 / 1 / 2 倍。列表行用 `Pill` 之外的 `Control` 档，选中态用同一半径——避免"选中时从直角变圆角"的跳动。

### 8.2 窗口圆角（尽力而为）

| 系统 | 情况 | 方案 |
| --- | --- | --- |
| Win11 ≥ 22000 | DWM 默认给顶窗口圆角 | 无需处理；`DWMWA_WINDOW_CORNER_PREFERENCE`(=33) 可强制 `Round` |
| Win10 1607–19045 | DWM **不会**给窗口圆角 | 两条路：①（默认）保持直角 + 内部圆角，观感"方窗口、圆控件"；②（可选开关）`AllowsTransparency=true` + 透明根 + 自绘圆角边框 —— 代价见下 |

`AllowsTransparency` 的代价必须写进 UI：**关闭硬件加速的部分路径、最大化/贴边行为需自实现、某些第三方输入法候选框定位异常、拖动时可能轻微撕裂**。因此：

- 默认 **关**；开关文案写"实验性：Win10 上窗口圆角"，并在开启时提示上述风险；
- 开启后自动禁用 Acrylic 材质（两者叠加会明显掉帧）；
- 最大化时自动切回直角（否则四角露桌面）。

---

## 9. 背景

### 9.1 四种来源

1. **纯色**（默认，取 `Ui.Color.Surface`）；
2. **线性渐变**：2–3 个色停 + 角度（0–360°）；
3. **图片**：本机文件（`png/jpg/jpeg/webp/bmp`），也支持"用当前游戏截图"（`<GameDir>/Screenshots`，读最新一张，只拷贝不修改）；
4. **按实例区分**：外观配置里 `byInstance: { "<instanceId>": {...} }`，切实例自动切外观。

### 9.2 处理参数

| 参数 | 范围 | 默认 | 说明 |
| --- | --- | --- | --- |
| 填充方式 | 填充 / 适应 / 拉伸 / 平铺 / 居中 | 填充 | 平铺时按原始像素重复，超大图自动先降采样 |
| 暗化 | 0–80% | 深色 20% / 浅色 0% | 叠一层 `Ui.Color.Surface` 半透明 |
| 模糊 | 0–40px | 0 | 只对背景层做一次 `BlurEffect`（合成到离屏位图后复用，不逐帧算） |
| 不透明度 | 20–100% | 100% | 低于 100% 时窗口底色透出 |

### 9.3 资源约束

- 解码后位图**硬上限 8 MB / 4096×4096**；超出先降采样再解码（避免 8K 图直接吃爆）。
- 同一时刻只保留**当前背景 + 上一张**（便于撤销），切走后释放。
- 图片路径记绝对路径；文件被删/移动 → 回退纯色 + 一条可点击的提示，不崩。
- 不做动画 GIF/视频（非目标）。
- 背景图**不进入**诊断包（14 篇 §5 已定"默认不含 .sfs"，此处补一条"不含用户背景图"）。

---

## 10. 密度与字号

| 档 | 行高 | 控件内边距 | 图标 | 首屏可见 Mod 行（1080p） |
| --- | --- | --- | --- | --- |
| 紧凑 | 32 | 6/10 | 16 | ≈ 26 |
| 标准（默认） | 36 | 8/12 | 20 | ≈ 23 |
| 舒适 | 44 | 12/16 | 24 | ≈ 19 |

字号：`Ui.Font.Scale` 0.9 / 1.0 / 1.1 / 1.2 / 1.3 五档。**只作用于 `Ui.Font.Body/Caption/Title/Display`**，不作用于数据文本（§3 原则 5）。切档后布局必须重算（`VirtualizingStackPanel` 行高缓存要失效，否则列表错位——见 05 篇 §16）。

触控目标：即使紧凑档，可点区域仍 ≥ 32×32（Windows 触摸规范下限），空间不足时用透明外扩。

---

## 11. 动效强度

| 档 | 行为 |
| --- | --- |
| 完整（默认） | 全部 `Motion.*` 生效 |
| 精简 | 只保留 90ms 的快速反馈（悬停/按下/切换），去掉页面切换与列表增删动画 |
| 关闭 | 全部瞬时；但**状态必须仍可感知**（选中态、加载进度用静态指示而非旋转动画） |

默认值跟随系统：`SystemParameters.ClientAreaAnimation` / `SPI_GETCLIENTAREAANIMATION` 为 0，或 Win11 的"动画效果"关闭 → 自动落到"精简"。用户显式选择后不再跟随系统。

---

## 12. 主题包（"目录即主题"）

### 12.1 目录结构

```
%LocalAppData%\CKAN\themes\<packId>\
  theme.json        # 只含 token 覆盖（见 12.3）
  preview.png       # 200x120 预览图（可选）
  LICENSE.txt       # 可选
  README.md         # 可选，展示在主题卡片详情里
```

随应用内置的官方主题放 `themes/<packId>/`（只读，升级时整体替换）；**用户主题一律放数据侧**，与 09 篇 §12 的"程序目录整体替换"不冲突（R-68 的同一处提醒）。

### 12.2 安全边界（最关键的一条）

**绝不允许主题包携带 XAML / `.axaml` / 脚本 / DLL。** 原因：Avalonia 的 XAML 支持事件绑定与自定义控件加载，加载一份外部 XAML 等价于**执行任意代码**。主题包只能是**声明式 JSON**，且：

- 允许的键 = §4 token 表的**白名单**（`color.*` / `radius.*` / `density.*` / `font.*` / `motion.*` / `background.*` / `material.*`）；
- 值类型校验：颜色必须是 `#RGB`/`#RRGGBB`/`#AARRGGBB` 或 `system:` 语义引用；数值有上下限；字符串禁止 `<`、`{`、`}`（防标记注入）；
- 未知键 → 忽略并记一条警告，不报错（保证向前兼容）；
- 加载后**必过对比度校验**（§6.3），不合规的主题包直接拒绝并给原因；
- 主题包不参与任何"路径/命令/文件"行为，无法让应用读写别的文件。

### 12.3 theme.json 形状

```json
{
  "schemaVersion": 1,
  "id": "nord-night",
  "name": "Nord Night",
  "author": "...",
  "baseVariant": "dark",
  "tokens": {
    "color.surface": "#2E3440",
    "color.text": "#ECEFF4",
    "color.accent": "#88C0D0",
    "radius.scale": 1,
    "density.rowHeight": 36,
    "material.windowBackdrop": "acrylic",
    "background.kind": "gradient",
    "background.gradient": ["#2E3440", "#3B4252"],
    "background.angle": 135
  }
}
```

### 12.4 生命周期

导入（校验 → 预览 → 确认）→ 启用（写 `ckan-ui.json` 的 `activeTheme`）→ **回滚**（保留上一套 `activeTheme`，一键换回）→ 删除（正在使用时提示"将回退默认"）。主题包目录被外部修改 → 下次启动重新校验，不合规自动回退默认并提示。

---

## 13. 外观设置页 IA

`V-05 设置 → 外观`（独立子页，左侧 6 组 + 右侧常驻实时预览）：

| 组 | 控件 | 关联 |
| --- | --- | --- |
| 主题 | 跟随系统 / 浅色 / 深色（分段控件）+ 主题包卡片列表（内置 3 套 + 用户导入） | P-01, P-11 |
| 颜色 | 强调色：跟随系统 / 12 预设色块 / 自定义 hex + 对比度徽标（AA/AAA/不合规） | P-03 |
| 材质 | 窗口材质：自动 / 磨砂 / 纯色；本机能力说明；"当前实际：Mica/Acrylic/纯色" | P-04 |
| 形状 | 圆角三档（0/6/12 的示意块）+ "Win10 实验性窗口圆角"开关（带风险提示） | P-05, §8.2 |
| 版面 | 密度三档 + 字号 5 档 + 动效三档（含"跟随系统"复选） | P-08/09/10 |
| 背景 | 来源（纯色/渐变/图片/游戏截图）、填充、暗化、模糊、不透明度 + "仅本实例"开关 | P-06, P-07 |

页脚：`恢复本组默认` / `恢复全部默认` / `导出外观…` / `导入外观…`；右下角一条"实时预览"迷你窗口（复刻主界面三行一卡，用 `ThemeVariantScope` 对照浅/深）。

---

## 14. 性能与内存预算

| 指标 | 预算 | 失败处理 |
| --- | --- | --- |
| 主题切换耗时 | ≤ 200 ms（含布局重算），期间不白屏 | 超过则在切换前显示 60ms 的骨架遮罩 |
| 背景图解码内存 | ≤ 8 MB / 张，常驻 ≤ 2 张 | 超限先降采样 |
| 背景模糊 | 只做一次离屏合成；切窗口尺寸时重算（<= 1 次/100ms 节流） | 帧率掉 > 10% → 自动关闭模糊并提示 |
| 列表滚动 | p50 ≤ 8 ms、p99 ≤ 16.7 ms（沿用 07 篇 §7 门禁） | 任一外观档位下都必须达标 |
| 启动到可交互 | ≤ 3 s（沿用 07 篇 §7） | 主题包校验耗时分摊到后台，不阻塞首帧 |

---

## 15. 无障碍与对比度

1. **高对比度**：系统开启（`SystemParameters.HighContrast` / `UISettings.HighContrast`）→ 强制 `HighContrast` 变体，**忽略主题包与用户覆盖**，并在设置页把这些控件置灰显示"已由系统接管"。
2. **对比度**：所有内置配色与所有可导入主题包过 §6.3 门禁；不合规不启用。
3. **不只靠颜色**：状态（已选/已装/需更新）除颜色外必须有形状/字形（沿用 14 篇 §3.4 的 `!`/`×`/`»` 前缀规则）。
4. **减少动效**：§11 的"跟随系统"默认项即无障碍开关。
5. **焦点可见**：`Ui.Color.Focus` 在所有材质/背景/主题包下都必须 3:1 可见；背景图上改用 2px 描边 + 1px 内描边双环保证可见性。

---

## 16. WBS（206h）

| 任务 | 内容 | 工时 | 依赖 | 覆盖 |
| --- | --- | --- | --- | --- |
| THM-01 | token 体系落位：8 组资源字典 × 3 变体 + CI 白名单检查 | 20 | ARC-03 | P-01, P-02 |
| THM-02 | 强调色：系统读取 / 12 预设 / 自定义 / 6 阶派生 / 注入 `ColorPaletteResources` | 12 | THM-01 | P-03 |
| THM-03 | 材质与透明：`Backdrop.Resolve()`、`ActualTransparencyLevel` 探测、能力说明、软件渲染降级 | 16 | THM-01, SP-46 | P-04 |
| THM-04 | 圆角：三档 token、控件同步、Win11 DWM 检测、Win10 实验性假圆角开关 | 18 | THM-01 | P-05 |
| THM-05 | 背景：四来源、五填充、暗化/模糊/不透明、降采样与缓存、按实例覆盖 | 20 | THM-01 | P-06, P-07 |
| THM-06 | 密度与字号：三档 × 五档、虚拟化行高失效、触控下限 | 14 | THM-01 | P-08, P-09 |
| THM-07 | 动效强度：三档 + 跟随系统检测 + 强制可感知状态 | 10 | THM-01 | P-10 |
| THM-08 | 主题包：加载、白名单校验、对比度门禁、未知键兼容、回滚与热重载 | 24 | THM-01, THM-02 | P-11 |
| THM-09 | 导入/导出外观 JSON（版本化、未知字段忽略）、恢复默认（单项/全局） | 12 | THM-08 | P-12, P-13 |
| THM-10 | 无障碍：高对比度强制、焦点可见、对比度计数器与测试夹具 | 10 | THM-02 | P-02, P-14 |
| UI-THM-01 | 外观设置页视图 + 实时预览迷你窗（`ThemeVariantScope` 对照） | 20 | VM-THM | P-01..P-13 |
| VM-THM | 上述 ViewModel | 8 | THM-09 | — |
| TST-THM | T-THM-01..12 + 手工组 H-14 | 16 | UI-THM-01 | P-14 |
| DOC-THM | 用户文档（"怎么调好看"）+ 主题包编写规范 | 6 | — | P-11 |
| **合计** | | **206** | | |

里程碑 **M14 = W56–W60（5 周）**。

---

## 17. 测试

| ID | 用例 | 判据 |
| --- | --- | --- |
| T-THM-01 | 三变体切换 | 全页面无旧配色残留；`ActualThemeVariant` 正确 |
| T-THM-02 | 高对比度强制 | 开启系统高对比度后，主题包的 12 个自定义 key 全部被忽略 |
| T-THM-03 | 强调色校验 | 不合规色被拒；建议色可一键采纳；500 组随机色单测 |
| T-THM-04 | 材质三档 | 三档在对应系统上都可显示；`ActualTransparencyLevel` 与显示文案一致 |
| T-THM-05 | 圆角三档 | 列表行/卡片/对话框/输入框同步变化；无"选中变圆"跳动 |
| T-THM-06 | 背景四来源 | 每种来源 + 五填充可组合；文件被删 → 回退纯色不崩 |
| T-THM-07 | 背景内存 | 8K 图降采样；常驻 ≤ 2 张；`GC` 后位图释放 |
| T-THM-08 | 按实例覆盖 | 切实例外观跟随；导出/导入后按实例段仍正确 |
| T-THM-09 | 密度/字号 | 130% 字号 + 德语长文案不裁切；列表不错位 |
| T-THM-10 | 主题包安全 | 含 `.axaml`/`<`/`{` 的主题包被拒；未知键忽略；损坏 JSON 回退默认 |
| T-THM-11 | 性能 | 每个外观档位下滚动 p50/p99 与首帧达标 |
| T-THM-12 | 恢复默认 | 单项与全局恢复后与首次启动 token 全等 |

手工组 **H-14**（发版前一次）：①Win10 1607 纯色 + 无 Acrylic ②Win10 22H2 磨砂 ③Win11 24H2 Mica ④高对比度 ⑤130% 字号 + 中文/德语/俄语 ⑥背景图 4K + 模糊 ⑥2 双屏不同 DPI 拖动。出口规则沿用 07 篇：**"未测"等同"不通过"**。

---

## 18. 风险与待验证

### 18.1 风险（R-73..R-80）

| ID | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| R-73 | 背景图 + 模糊叠加导致滚动掉帧 | 高 | 中 | 离屏合成一次、预算门禁、掉帧自动关模糊 |
| R-74 | Win10 假圆角（`AllowsTransparency`）引发输入法/最大化/多屏问题 | 高 | 中 | 默认关、开启时禁用 Acrylic、最大化自动直角、风险文案明示 |
| R-75 | 第三方主题包对比度不合规 → 用户看不清 | 高 | 高 | 强制门禁 + 设置页徽标 + 一键回默认 |
| R-76 | 主题包注入（XAML/脚本 = 任意代码执行） | 中 | **极高** | 只允许声明式 JSON + 白名单 + 值类型与字符校验（§12.2），代码里不出现 `XamlLoader.Load` 外部路径 |
| R-77 | AcrylicBlur 依赖 `win10-x64` RID，混用 RID 后静默变纯色 | 中 | 中 | 构建矩阵固定 RID；运行期探测 `ActualTransparencyLevel` 并显示实际档位 |
| R-78 | 主题切换时 `DynamicResource` 漏更新（局部硬编码残留） | 中 | 中 | CI 白名单 grep + T-THM-01 逐页断言 |
| R-79 | 高 DPI / 多屏下背景图与圆角错位 | 中 | 中 | 用 `TopLevel.RenderScaling`，切换屏/DPI 时重解析 |
| R-80 | 个性化把"数据文本"也改了字体，导致认错 mod | 低 | 中 | 数据文本用独立 token，不受 `Font.Scale` 影响（§3 原则 5） |

### 18.2 待验证（SP-46..SP-50，共 11h）

| ID | 问题 | 时间盒 | Go/No-Go |
| --- | --- | --- | --- |
| SP-46 | Win10 22H2 + `win10-x64` RID 下 AcrylicBlur 的实际观感与滚动帧率；叠加背景图模糊后是否仍 ≤16.7ms | 3h | **是**（否则"磨砂"只保留在 Win11） |
| SP-47 | Win10 上 `AllowsTransparency` 假圆角的输入法候选框、最大化、贴边、多屏表现 | 3h | 记录即可（决定该开关是否面向用户开放） |
| SP-48 | 4K 背景图解码峰值内存与降采样策略（`Bitmap` + `RenderOptions.BitmapInterpolationMode`） | 2h | 是（否则限制到 2560 宽） |
| SP-49 | 主题包白名单校验器能否拦住 `<`、`{`、超范围数值、未知键；对比度门禁在 200 个随机主题上的稳定性 | 2h | **是**（安全门禁） |
| SP-50 | `RequestedThemeVariant` 运行期切换时，Avalonia 是否残留旧画刷缓存（尤其是 `AcrylicBlur` 下的窗口底） | 1h | 是（否则需重建窗口材质） |

---

## 19. 决策（2026-10-27 已拍板）

> **决策**：Q1 = **开放但默认关**（文案写明风险，R-74）；Q2 = **只做手动选背景**（自动轮换放 v2）；Q3 = **不做主题市场**（只支持导入本地文件夹/zip）；Q4 = **允许强调色按实例**（默认跟随全局）；**Q5 = 不做"高对比度专用主题包"**（用户明确"不需要"，覆盖原文建议；高对比度可用性仍由 §15 的强制压制规则保证）。工时不变（206h）。

| ID | 问题 | 我的建议 |
| --- | --- | --- |
| Q1 | Win10 的"实验性窗口圆角"是否对用户开放？ | **开放但默认关**，文案写明风险（R-74） |
| Q2 | 背景图是否允许"用游戏截图自动轮换"（每次启动换一张）？ | 先只做"手动选一张"，自动轮换放 v2 |
| Q3 | 主题包是否要打包分发市场（社区提交）？ | 不做市场，只支持"导入本地文件夹/zip"，降低审核与安全成本 |
| Q4 | 强调色是否允许"按实例不同"？ | 允许（与背景同享 `byInstance`），但默认跟随全局 |
| Q5 | 是否需要"高对比度专用主题包"（系统未开高对比度但用户想要）？ | 需要，作为内置主题之一 |

---

## 20. 修订记录

- **v1.0（E8 落盘）**：新建本文。取证基于 `GUI/Util.cs` 的 `DarkMode` 只读推断、`GUI/` 下 151 处 `BackColor =` / 88 处 `ForeColor =` / 130 处 `SystemColors.`；Avalonia 侧依据官方文档（`RequestedThemeVariant`、`ThemeVariantScope`、`ThemeDictionaries` 必须 `DynamicResource`、`FluentTheme.Palettes` + `ColorPaletteResources`）。WBS 206h，M14 = W56–W60。
