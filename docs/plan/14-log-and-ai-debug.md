# 14 · 日志审查（Log Review）与 AI 辅助排障（OpenAI）

> 状态：v1.0（2026-10-27）｜ 本文只做**计划与设计**，不含实现代码；实现须等用户指令。
> 归属域：**E2 可观测性域**（与 E1 存档工作台并列，独立可交付）。
> 上游依据：`00-index.md` 的 D1/D5/D7、`02-core-integration.md` 的 IUser 契约、`05-threading.md` 的线程规则。

---

## §1 目标与非目标

### 1.1 目标

| ID | 目标 | 验收方式 |
| --- | --- | --- |
| G-01 | 把 CKAN 运行期的**四路日志源**汇成一个统一事件流，可实时看、可回看 | 同一进程内能同时看到 log4net、IUser 消息、未捕获异常、子进程输出 |
| G-02 | 日志**自动染色**：按级别与语义族着色，一眼分清"下载失败/依赖无解/文件被占用/权限问题" | 每条日志有 `Level`+`Family`，颜色由主题 token 决定，深浅色与高对比度均可读 |
| G-03 | 日志**可审查**：过滤、搜索、时间轴、错误聚合、pin、跳转到相关对象 | 10 万行仍可流畅滚动（p99 ≤16.7ms） |
| G-04 | 一键生成**脱敏诊断包**（zip），可贴给他人 | 包内无用户名/绝对路径/令牌/SteamID |
| G-05 | 一键请 **AI 协助排障**：把脱敏后的上下文发给 OpenAI 兼容端点，返回结构化结论并**高亮证据行** | 发送前有逐字预览；结果里每条假设都能点回日志行 |
| G-06 | 全流程**默认关闭**、**显式同意**、**不产生遥测** | 首次使用需手动开启；任何一次发送都要用户点"发送" |

### 1.2 非目标

- 不做上传/云端自动上报（无遥测、无后台发送）。
- 不改 Core 的日志代码（D5）。Core 一行都不动。
- 不做 KPBS/群组共享诊断（X 类，未纳入）。
- 不做 AI 自动修复（AI 只出建议，不执行任何写盘动作）。

---

## §2 现状取证（本仓库实测）

### 2.1 log4net 初始化

- `Core/Logging.cs:23`：从 **`Environment.CurrentDirectory`** 下找 `log4net.xml`。
  - 找不到 → `BasicConfigurator.Configure(repo)` 且 `repo.Threshold = Level.Warn`（`:25-28`）。
  - 找到 → `XmlConfigurator.ConfigureAndWatch(repo, logConfig)`（`:33`），失败再回退 Basic。
- 结论：**CWD 决定日志配置**。用户从快捷方式/Steam 启动时 CWD 不一定是我们 exe 目录 → 不能依赖"放一个 log4net.xml 在 exe 旁"就生效。

### 2.2 随包发布的配置（`GUI/log4net.xml`，`Cmdline/`、`Core/`、`Netkan/`、`Tests/` 四份同名文件内容同构）

```
<file value="./CKAN.log" />            → 日志落在 CWD 的 CKAN.log
rollingStyle=Size, maximumFileSize=1MB, maxSizeRollBackups=30   → 最多 31MB
lockingModel = FileAppender+MinimalLock                          → 可与其它进程共用
conversionPattern = "%date{yyyy-MM-dd HH:mm:ss} %level %logger - %message%newline"
<root><level value="WARN" /></root>                              → 只记录 WARN 及以上
```

**关键事实：默认阈值是 `WARN`**。也就是说用户手里的 `CKAN.log` 里**没有 INFO/DEBUG**，出事时信息严重不足——这是"日志审查"要解决的第一个问题。

### 2.3 Core 的日志密度极低

`grep` 统计（`Core/**/*.cs`）：`Debug` 39 处、`Info` 5 处、`Warn` 12 处、`Error` 3 处、`Fatal` 0 处；共 45 处 `LogManager.GetLogger(...)`。

> 24,661 行 Core 里只有 59 条日志语句 → **真实信息量在别处**：`IUser.RaiseMessage/RaiseError` 的流式消息（安装每个 mod、下载每个文件都会说话）以及未捕获异常。所以采集层不能只接 log4net。

### 2.4 老 GUI 的日志显示能力（要超越的基线）

- `GUI/Controls/Wait.cs:330` `ClearLog()` 每次任务开始 `LogTextBox.Text = ""`；`:335` `AddLogMessage()` 只是 `AppendText(message + "\r\n")`。
- 控件本体是 `System.Windows.Forms.TextBox`（`Wait.Designer.cs:36,105-117`）：**纯文本、零染色、无过滤、无搜索、任务结束即清空**。
- 消息主通道：`GUIUser.RaiseError`（`GUI/GUIUser.cs:78`）与 `GUIUser.RaiseMessage`（`:125`）。

### 2.5 CKAN 没有任何 `.sfs`/存档相关日志，也没有诊断包/上报机制（E1 与 E2 都是净新增域）。

---

## §3 统一事件模型

### 3.1 一条日志的内部形状

```csharp
// 设计稿，非最终代码
public sealed record LogEvent(
    long        Seq,          // 单调递增序号，用于"证据行"引用
    DateTime    TimeLocal,
    LogLevel    Level,        // Trace/Debug/Info/Warn/Error/Fatal/User
    LogFamily   Family,       // 语义族，见 §3.3
    string      Source,       // "log4net:loggername" | "user" | "exception" | "child:pid" | "ui"
    string      Message,      // 原文（不裁剪，展示层再省略）
    string?     Detail,       // 异常的 ToString()、HTTP 响应体等
    string?     CorrelationId,// 一次安装/一次刷新/一个任务
    IReadOnlyDictionary<string,string>? Tags);   // mod=xxx, instance=xxx, url=...
```

### 3.2 四路采集

| 源 | 接法 | 注意 |
| --- | --- | --- |
| log4net | 向**同一个** log4net repository 注册自建 `MemoryAppender`（环形缓冲）+ 自建 `RollingFileAppender`；并把 root 阈值设 `INFO` | 不依赖 XML、不受 CWD 影响；阈值是我们设的，Core 一行不改 |
| `IUser` 消息 | 我们的 `IUser` 实现（`02` 篇契约）在 `RaiseMessage/RaiseError/RaiseProgress` 里同时投递到事件流 | 已在 UI 线程/已封送，天然安全 |
| 未捕获异常 | `AppDomain.CurrentDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、`Dispatcher.UnhandledException` | 三者都要，`UnobservedTaskException` 在下载/解包场景最常见 |
| 子进程 | 调用 `ckan.exe`（若走 CLI 策略）时捕获 stdout/stderr，按行转事件 | 需要 `Process.StandardOutput` 异步读，注意编码 |

### 3.3 语义族 `LogFamily`（决定颜色，决定过滤）

| 族 | 含义 | 识别依据（示例） |
| --- | --- | --- |
| `Net` | HTTP/网络/证书 | url、`Timeout`、`SSL`、`connection reset`、`WebException` |
| `Download` | 下载/校验/解包 | `SHA256`、`checksum`、`mismatch`、`unzip`、文件大小 |
| `Solve` | 依赖求解/冲突 | `unresolved`、`conflict`、`dependency`、`NotSatisfiable`、`RelationshipResolver` |
| `Install` | 安装/卸载/替换 | `Installing`、`Removing`、`Transaction`、`Registry` 写 |
| `Fs` | 文件系统/权限/占用 | `IOException`、`UnauthorizedAccess`、`Access to the path`、`EBUSY` |
| `Reg` | 注册表/实例/锁 | `registry.locked`、`RegistryVersionNotSupported`、`instance` |
| `Save` | 存档（E1） | `.sfs`、`loadmeta` |
| `Sec` | 安全/凭据 | `AuthToken`、`api key`、`401`、`403` |
| `Ui` | 我们的 UI 自身 | 导航失败、绑定错误、主题降级 |
| `Other` | 兜底 | 无匹配 |

分类实现：**规则表驱动**（有序 `(regex, family, weight)` 列表，取最高权重；命中即归属），规则表外置为 `Presentation/Themes/log-rules.json`，可热更新、可测试快照。

### 3.4 级别与来源的着色 token

不改魔法值，全部走主题资源（`03` 篇 §12 的动效/材质规范同源）：

| 键 | 深色主题 | 浅色主题 | 用途 |
| --- | --- | --- | --- |
| `Log.Text` | `#D4D4D4` | `#1F1F1F` | 正文 |
| `Log.Trace` / `Log.Debug` | `#7A7A7A` | `#8A8A8A` | 弱化，可默认隐藏 |
| `Log.Info` | `#D4D4D4` | `#1F1F1F` | 正文 |
| `Log.Warn` | `#E0A030` | `#9A6B00` | 黄 |
| `Log.Error` | `#E05252` | `#B3261E` | 红 |
| `Log.Fatal` | `#FFFFFF` on `#B3261E` | 同 | 红底反白 |
| `Log.User` | `#4FA3D1` | `#0B6EA8` | IUser 消息蓝 |
| `Log.Highlight` | `#FFD75E` 底 | `#FFE9A8` 底 | 搜索命中 / AI 证据行 |

**无障碍**：颜色**永不单独承载语义**——每条前面有 `!`(Warn) `×`(Error) `»`(User) 等字形前缀，高对比度模式下改用下划线+粗体。这是 M-23 用例的检查点。

---

## §4 日志审查 UI

### 4.1 形态

- **日志页**（左侧 NavRail 一级入口）：全屏审查工作区。
- **底部日志抽屉**：所有页面可用，默认收起，显示最近 200 行 + "有 3 个警告"徽标，点击上拉成半屏。`Motion.Base` 150ms。

### 4.2 组成（自左到右）

1. **源/级别/族 三段过滤条**：级别多选（默认 Info+），族多选（默认全选），源多选（log4net/user/exception/child）。
2. **搜索框**：子串 + `族:net` `级别:error` `mod:xxx` 前缀语法；命中高亮（`Log.Highlight`）；`F3`/`Shift+F3` 上下跳。
3. **主列表**：虚拟化行（等高 20px，等宽字体，`Seq` + 时间 + 级别字形 + 族标签 + 消息）。单行超长时省略，选中后右侧详情面板显示原文（含 `Detail` 的异常栈）。
4. **聚合视图切换**：连续相同"指纹"（`级别+族+消息骨架`，数字/路径替换为占位）折叠成一行 `×N`，展开看全部。
5. **时间轴条**：右缘一条细柱状图，每个错误是红点、警告黄点，点击定位。用于"安装失败的 10 秒"这种场景。
6. **操作栏**：清空 / 仅错误 / 跟随末尾（自动滚动，用户手动上滚即暂停）/ 导出 zip / **请 AI 分析**。

### 4.3 与对象联动（这是"审查"而非"看日志"的关键）

- 命中 `mod=<identifier>` 标签的行 → 右键"查看该 mod 详情"。
- 命中 URL 的行 → "在浏览器打开"（脱敏后可）。
- 命中 `instance=<name>` → "切换实例"。
- 安装任务行 → "重试此任务"（复用 `05` 篇的重试通道）。

---

## §5 诊断包（Diagnostic Bundle）

### 5.1 内容清单（zip）

| 文件 | 内容 | 脱敏 |
| --- | --- | --- |
| `summary.json` | 版本、Avalonia/OS/架构/DPI/主题、实例数、仓库源数与名称、mod 数、耗时统计、错误计数按族 | 已脱敏 |
| `log.tail.txt` | 最近 N 行（默认 5,000，可调 500–50,000） | 已脱敏 |
| `log.errors.txt` | 所有 Error/Fatal 行 + 其 `Detail` | 已脱敏 |
| `registry.summary.json` | **只** 版本号、`installed_modules` 计数、`installed_files` 计数、仓库排序 | 不含模块内容 |
| `config.redacted.json` | 生效配置的键值（`AuthTokens` **整键删除**、路径占位） | 强制 |
| `env.txt` | `dotnet --info` 摘要、CKAN 版本、log4net 阈值 | 已脱敏 |
| `console.log` | 我们的 UI 自身事件（含绑定错误、渲染后端） | 已脱敏 |

默认**不含**任何 `.sfs`、任何 `GameData/**` 文件、任何 `.ckan`/`.zip`。

### 5.2 脱敏规则（写死在 `Services/Diagnostics/Redactor.cs`，可单测）

| 目标 | 规则 |
| --- | --- |
| 用户名 | 匹配 `[A-Za-z]:\\Users\\<name>\\` → `%USERPROFILE%\`；`/home/<name>/`、`/Users/<name>/` 同理 |
| 绝对路径 | `<GameDir>` → `<KSP>`；`%LocalAppData%`、`%APPDATA%` 保留占位；其余盘符路径 → `<PATH>` |
| 令牌 | `AuthTokens` 整键删除；`token`/`key`/`password`/`secret` 相邻的值 → `<REDACTED>` |
| Steam ID | `7656119\d{10}` → `<STEAMID>`；`SteamUser`/`SteamAppId` 值 → `<ID>` |
| IP | IPv4/IPv6 → `<IP>` |
| 邮箱 | 正则 → `<EMAIL>` |
| 主机名 | `%%COMPUTERNAME%%`、`gethostname` 结果 → `<HOST>` |
| 存档名 | `.sfs` 文件名 → `<SAVE>`（玩家常拿真实人名命名存档） |

**出口校验**：打包完成后跑一次"反向扫描"——在 zip 文本里再搜一遍用户名/令牌/邮箱模式，命中则**拒绝生成**并报错。这条是硬门禁。

---

## §6 AI 辅助排障（OpenAI 兼容端点）

### 6.1 交互流程（五步，每步都要用户可见）

1. 用户在日志页点 **"请 AI 分析"**。
2. 打开**发送预览**对话框：左边是**将要发送的完整文本**（可编辑、可裁剪），右边是统计（字符数、预估 token、估算费用、包含哪些文件）+ 一行固定提醒"这是要发到互联网的第三方服务"。
3. 用户点 **"发送"**（默认按钮是 `取消`）。首次使用还需在设置里填入 API Key 并勾选"我了解日志会被发送到第三方"。
4. 流式返回：思考骨架先出现，随后结构化结果渲染。
5. 结果中的 `evidenceLines` 按 `Seq` 高亮定位（`Log.Highlight`），可"查看上下文"、"复制"、"报告误判"（存本地，不发出去）。

### 6.2 客户端设计

| 项 | 决定 |
| --- | --- |
| 协议 | OpenAI Chat Completions（`POST {baseUrl}/chat/completions`），**baseUrl 可配**（默认 `https://api.openai.com/v1`） |
| 兼容性 | 任何 OpenAI 兼容端点可用：Azure OpenAI、LM Studio、Ollama（`/v1`）、vLLM、各类中转 |
| 模型 | 用户填模型名，默认 `gpt-4.1-mini` 一类的廉价档；不要硬编码最新型号 |
| 传输 | `HttpClient`（复用 `Services/Http` 的连接池），`stream: true`，超时 60s，重试 2 次（指数退避，仅对 5xx/超时） |
| 密钥 | **DPAPI(`CurrentUser`)** 加密存 `%LocalAppData%\CKAN\ckan-ui.secrets`；不写 `config.json`、不进日志、不进诊断包、不显示明文（只显示尾 4 位） |
| 预算 | 单次输入上限 32k token（超了先做摘要/裁剪，见 §6.4）；输出上限 2k；月累计上限默认 200k token，超限只提示不发 |
| 缓存 | 以 `sha256(错误指纹集合 + 诊断包摘要)` 为键存本地 SQLite；命中直接显示上次结论并标注"命中缓存，未产生费用" |
| 离线降级 | 无网/401/超时 → 明确提示 + 按钮"复制脱敏文本"，用户可自行粘贴到网页版 |

### 6.3 提示词契约（系统提示固定，用户不可改，避免提示注入）

- 角色：KSP mod 管理工具 CKAN 的排障助手。
- 输入结构：`<env>` `<manifest>` `<log>` 三段，日志行带 `#Seq` 前缀。
- 硬要求：
  1. **只依据提供的日志**；不足时输出 `needMoreInfo` 而不是猜测。
  2. 每条假设必须给出 `evidenceSeq` 列表（引用 `#Seq`），无证据的假设不得输出。
  3. 不输出可执行文件操作指令；只输出"建议用户做什么"。
  4. 输出**严格 JSON**（下述 schema），不包裹 Markdown。
- 输出 schema：

```json
{
  "summary": "一句话结论",
  "severity": "info|warning|error|blocker",
  "hypotheses": [
    {"cause": "...", "confidence": 0.0, "evidenceSeq": [12, 34],
     "explain": "为什么这些行支持这个判断", "fix": "用户可执行的下一步"}
  ],
  "needMoreInfo": ["需要用户提供 X"],
  "relatedKnowledge": ["若确定是某 mod/依赖问题，给出可查的关键词"]
}
```

- **校验**：客户端收到后先 `JSON.parse` + schema 校验；失败则重试一次（带"上次输出不合规"的纠正消息），再失败就降级为纯文本展示并标注"未通过校验"。

### 6.4 Token 预算策略（省钱且够用）

1. 先拼 `env`（<1k token）+ `manifest`（错误计数、族分布、前 10 条 error 的骨架，<1k）。
2. 再按"从错误向外扩窗"填日志：每个 Error 前后 ±30 行为一个窗，按族优先级（Sec > Fs > Solve > Download > Net > Other）填到上限。
3. 仍超 → 用"指纹去重 + 计数"替代重复行（`×47`）。
4. 全部裁剪都在预览里可见，用户能手动改。

### 6.5 AI 结果的可信度护栏

- 每条假设带 `confidence`，<0.5 的折叠显示并标"低置信"。
- 结果顶部固定 banner："AI 可能出错，请以日志证据为准"。
- 用户可对单条假设点"这条是错的"→ 本地记录（只在本机，用于将来调整提示词，不上传）。
- 绝不因为 AI 说"删除某文件"就自动执行——所有动作仍需用户在现有 UI 里手动触发。

---

## §7 数据与状态

| 文件 | 位置 | 内容 |
| --- | --- | --- |
| `logs/ckanui-YYYYMMDD.log` | `%LocalAppData%\CKAN\logs` | 我们自建 RollingFileAppender，5MB × 5 |
| `logs/index.sqlite` | 同上 | 事件索引（只存 `Seq/时间/级别/族/指纹/偏移`），供跨会话审查 |
| `ckan-ui.secrets` | `%LocalAppData%\CKAN` | DPAPI 加密的 API Key |
| `ai-cache.sqlite` | `%LocalAppData%\CKAN\cache` | 指纹→结论缓存 + 花费累计 |
| `log-rules.json` | 安装目录 `Presentation/Themes` | 分类规则表（可被用户覆盖） |

**不写 `config.json`**（`09` 篇 §3.2 硬约束：整份重写会丢未知键）。
**不写 `registry.json`，不加锁**——日志域与实例注册表完全解耦。

---

## §8 WBS（186h）

| 任务 | 内容 | 工时 |
| --- | --- | --- |
| LOG-01 | 事件模型 + log4net MemoryAppender + IUser 适配 + 三处异常钩子 | 16h |
| LOG-02 | 环形缓冲（默认 20k 行）+ 自建滚动文件 + 旧 `CKAN.log` 导入解析 | 12h |
| LOG-03 | 分类染色引擎（规则表 + 9 族 + 反色安全 + 高对比度） | 16h |
| LOG-04 | 审查视图（虚拟化 + 三段过滤 + 搜索语法 + 聚合 + 时间轴） | 20h |
| LOG-05 | 诊断包构建 + 脱敏 + 出口反向扫描硬门禁 | 16h |
| LOG-06 | AI 客户端（兼容端点、流式、超时重试、速率与预算） | 16h |
| LOG-07 | 提示词 + 结构化输出校验 + 证据行高亮渲染 | 14h |
| LOG-08 | 指纹缓存 + token 计数 + 花费显示 | 10h |
| LOG-09 | 凭据（DPAPI）+ 隐私开关 + 首次同意流 | 10h |
| LOG-10 | CLI 子命令（`logs tail/export/bundle`、`ai analyze --bundle`） | 8h |
| UI-LOG-01 | 日志页 + 底部抽屉 + 徽标 | 12h |
| UI-LOG-02 | 发送预览对话框 + AI 结果面板 | 12h |
| VM-LOG | `LogViewModel`/`LogFilterViewModel`/`AiAnalysisViewModel`（3 个 VM） | 8h |
| TST-LOG | 脱敏单测（20 个泄漏样本必须全灭）+ 假端点集成 + 规则快照 + 10 万行帧率 | 12h |
| DOC-LOG | 用户文档：如何开启诊断模式、如何填 Key、费用说明、隐私声明 | 4h |
| **合计** | | **186h ≈ 23 人日 ≈ 4.7 周净工时** |

### 里程碑 M8（W31–W35，5 周）

| 周 | 内容 | 出口 |
| --- | --- | --- |
| W31 | LOG-01/02（采集跑通，能在页面看到 log4net+IUser 两路） | 一个真实安装流程的事件流完整 |
| W32 | LOG-03/04 + UI-LOG-01（染色 + 审查视图） | 染色快照测试全绿；10 万行 p99 ≤16.7ms |
| W33 | LOG-05/09（诊断包 + 脱敏 + 凭据） | 反向扫描零命中；Key 不出现在任何产物里 |
| W34 | LOG-06/07/08 + UI-LOG-02（AI 全链路） | 假端点端到端通；真实端点一次真实分析成功 |
| W35 | LOG-10 + TST-LOG + DOC-LOG + 打磨 | 验收清单全绿 |

---

## §9 测试计划（接 `07-testing.md`）

### 9.1 不可自动化也要有门禁的项

| ID | 用例 | 判据 |
| --- | --- | --- |
| T-LOG-01 | 脱敏泄漏矩阵（20 个样本：中文用户名、空格路径、SteamID、Bearer token、邮箱、IPv6、`.` 路径、UNC 路径…） | 全部被抹；反向扫描零命中 |
| T-LOG-02 | 10 万行虚拟化滚动 | p99 ≤16.7ms（同 `07` 篇帧率门禁） |
| T-LOG-03 | 染色规则快照（50 条真实日志片段 → 期望族/级别） | 100% 命中期望 |
| T-LOG-04 | 高对比度 + 深/浅主题下的可读性 | 对比度 ≥4.5:1（正文） |
| T-LOG-05 | AI 输出不合规（返回 Markdown/空 JSON/超长） | 降级为纯文本并标注，不崩溃 |
| T-LOG-06 | 401/超时/断网 | 明确提示 + 可复制脱敏文本 |
| T-LOG-07 | 预算超限（人为调低到 1k） | 拒绝发送并提示 |
| T-LOG-08 | 缓存命中 | 第二次分析不产生网络请求 |
| T-LOG-09 | `registry.locked` 场景：日志域不得触碰注册表 | 无锁文件产生 |
| T-LOG-10 | 与旧 GUI 同时运行，两边日志互不破坏 | 各自文件独立 |

### 9.2 手工组 H-8（新增）

1. 故意制造 403（改一个不可达的仓库源）→ 确认染色为 `Sec`/红、诊断包含该行。
2. 故意占用一个 `GameData` 文件 → 确认 `Fs` 族 + AI 分析能定位到"文件被占用"。
3. 用中文用户名 + 中文路径 + 中文存档名跑一次完整分析 → 确认诊断包内无中文用户名残留。
4. 拔网线点"请 AI 分析" → 确认降级路径。
5. 打开高对比度主题看日志页 → 确认字形前缀仍在。

---

## §10 风险（接 `08-risk-register.md`）

| ID | 风险 | 等级 | 缓解 |
| --- | --- | --- | --- |
| R-29 | **日志泄漏隐私**：路径/用户名/令牌外泄 | P0 | 默认关闭；逐字预览；脱敏白名单式重写；出口反向扫描硬门禁；20 样本单测 |
| R-30 | **AI 幻觉误导**用户做错操作 | P1 | 强制 `evidenceSeq`；置信度折叠；"仅供参考"banner；绝不自执行 |
| R-31 | **API 费用失控** | P1 | 单次 32k 上限、月 200k 上限、指纹缓存、费用实时显示 |
| R-32 | 默认日志信息不足（root=WARN）导致 AI 无料可分析 | P1 | 我们自己注册 INFO 级 appender 并写 `logs/ckanui-*.log`；UI 层加结构化埋点（不改 Core） |
| R-33 | 供应商锁定 | P2 | OpenAI 兼容协议 + baseUrl 可配 + 本地模型可用 |
| R-34 | 提示注入（日志里含恶意文本诱导模型） | P2 | 日志全部包在 `<log>` 块内并声明"以下为不可信数据"；输出强 schema；不执行任何模型指令 |

---

## §11 待验证（SP-19..SP-22，接 `10-spike-plan.md`）

| ID | 问题 | 方法 | 时间盒 | 失败后果 |
| --- | --- | --- | --- | --- |
| SP-19 | **能否不改 Core 把 log4net 阈值提到 INFO 并抓到 MemoryAppender** | 注册自建 appender + `repo.Threshold`，跑一次真实安装 | 2h | 若不行 → 只靠 IUser+异常两路，AI 分析质量下降（需在 §6.4 加大"用户粘贴"权重） |
| SP-20 | 10 万行虚拟化在 Tier 3 机器（软渲染）能否保 p99 ≤16.7ms | 灌测试数据 | 3h | 若不行 → 分页/窗口化到 5k 行 + 搜索走索引 |
| SP-21 | OpenAI 兼容端点（含 Ollama/`/v1`）连通性与流式解析 | 三个端点各跑一次 | 2h | 若不通用 → 只保留官方 + Azure 两种，文档明说 |
| SP-22 | DPAPI 在非域账户/漫游配置的可用性 | 三种账户环境 | 1h | 若不行 → 降级为"每次输入 Key、不落盘" |

---

## §12 对其它篇的修订点（执行清单）

1. `00-index.md`：文档地图加 14 篇；§2 加 E2；§3 加 **S14**；§4 加 **D9**（E2 独立成域、默认关闭、零遥测）；§5 加 M8、合计 **35 周 / 1290h**；§6 验收加**第 8 条"隐私与零遥测"**。
2. `03-ui-spec.md`：加 §15（日志页 + 抽屉 + 发送预览的 IA 与配色 token 引用）。
3. `04-mvvm-and-models.md`：加 §11（3 个 VM + 约定：VM 不直接发 HTTP、不持有明文 Key、`LogEvent` 不可变）。
4. `06-wbs-milestones.md`：加 §1.11（186h 表）、头部改 **1290h / 161 人日 / 32.3 周净 / 排期 35 周**、§2 加 M8、§3 加 W31–W35、§4 资源表。
5. `07-testing.md`：加 §10（T-LOG-01..10 + H-8 手工组 + 脱敏门禁）。
6. `08-risk-register.md`：加 §8（R-29..R-34）。
7. `10-spike-plan.md`：加 §11（SP-19..SP-22）。
8. `doc/ui-refactor-feasibility.md` §11：改"14 份"，补 E2 与 1290h。

---

## §13 修订记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v1.0 | 2026-10-27 | 首版：四路采集、9 族染色、诊断包脱敏、OpenAI 兼容 AI 排障、186h / M8 |
