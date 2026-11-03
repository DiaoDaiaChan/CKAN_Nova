# AI 助手插件（com.ckanui.ai）

> 状态：0.2 试点。宿主侧桥 `IAiCkanBridge`、插件 `CkanUI.Plugin.Ai`、网页搜索走 MCP `free-search-mcp`。
> 本文只写**为什么这样切**和**哪里会坏**；接口签名以源码为准。

## 1. 目标与非目标

**目标**

- 在界面里交互式问 AI，答案建立在两份**可核对的事实**上：CKAN 仓库快照（有哪些 mod、下载量、依赖）与联网搜索（这个 mod 到底好不好用、最近有没有坑）。
- AI 能**操作本界面**：用户说「打开模组库」，AI 真的把页面切过去。
- 回答「这个 mod 是干什么的」这类问题：抽象、描述、依赖、许可、主页全部来自仓库索引，不靠模型记忆。

**非目标（当前版本明确不做）**

- 不做写操作：安装/卸载/升级/应用变更一律不开放给模型。AI 只能读 + 导航，原因见 §6。
- 不做后台常驻的自主 agent：一次提问一次会话，关掉页面就停。

## 2. 三层切分

```
┌──────────────────────────────┐
│ 插件 CkanUI.Plugin.Ai        │  聊天 UI、工具循环、MCP 子进程、配置与密钥
│  AiAssistantPageView/VM      │
│  AiChatClient / AiToolRunner │
└──────────────┬───────────────┘
               │ IAiCkanBridge（Abstractions）
┌──────────────┴───────────────┐
│ 宿主 CkanUI.App/Ai           │  IRepositoryService / IRegistryService /
│  AiCkanBridge                │  IInstanceService / INavigationService
└──────────────────────────────┘
```

- **为什么桥住 App**：导航要 `INavigationService`，仓库与注册表快照是宿主单例；插件不引用 App（内置插件的 `ReferenceOutputAssembly=false`），所以只能通过契约拿数据。
- **为什么桥只读**：只读查询走内存快照，不发网络请求、不写磁盘、不阻塞 UI 线程；唯一的副作用是 `NavigateAsync`，而它等价于用户点侧栏。
- **为什么工具结果要压缩**：模型上下文很贵。`SearchMods(limit)` 默认 10 条，每条只带 identifier/name/abstract 等十来个字段；`GetModDetail` 才给描述与依赖。绝不把 `ModRowData` 整个序列化出去。

## 3. 一次提问的数据流

1. 用户输入 → `AiChatClient` 组 messages（system + 历史 + 本轮）。
2. `POST {BaseUrl}/chat/completions`，带 `tools` 与 `tool_choice: auto`。
3. `finish_reason == "tool_calls"`：`AiToolRunner` 按 `function.name` 分派，返回紧凑 JSON 作为 `role: "tool"` 消息回灌，回到第 2 步（上限 `MaxToolRounds`，默认 6）。
   到上限后**不是直接报「轮次用尽」**：再发一次 `tool_choice: "none"` 的收口请求，并追加一条 system 指令
   「轮次已用完，只根据已拿到的工具结果回答」。真实端点上模型会连续几轮只回 `tool_calls`、正文为空
   （实测 gemini 系三轮都是这样），没有这一步用户看到的就是「查到了却什么都没答」。
4. `finish_reason == "stop"`：取 `choices[0].message.content` 渲染成气泡。
5. 任何一步失败：气泡里显示可读原因 + 「重试」，不静默吞掉。

## 4. 工具契约

| 工具名 | 参数 | 返回 | 说明 |
| --- | --- | --- | --- |
| `ckan_context` | 无 | 实例名/KSP 版本/已装数/可选数/索引时间/是否缓存 | 让模型知道「在哪个实例上回答问题」 |
| `ckan_search_mods` | `query`（可空）、`limit` | `AiModSummary[]` | 空查询按下载量排序；评分口径见实现注释 |
| `ckan_mod_detail` | `identifier` | `AiModDetail` 或 `null` | 「它是干什么的」主入口 |
| `ckan_installed` | `limit` | `AiInstalledMod[]` | 回答「我装了什么、有没有重复」 |
| `ckan_list_pages` | 无 | `AiPageRef[]` | 导航前先让模型知道有哪些键 |
| `ckan_navigate` | `page_key` | `{ok, message?}` | 唯一会切页面的工具；失败把原因回给模型 |
| `ckan_install_mod` | `identifier` | `{ok, applied, summary, installed, removed, failed, warnings}` 或 `{ok:false, error}` | 唯一会动磁盘的工具；先出变更计划、弹确认框，用户拒绝回 `applied=false`（不是失败） |
| `web_search` | `query` | `[{title,url,snippet}]` 或 `{ok:false,error}` | 走 MCP 子进程；不可用时降级而不是崩 |

## 5. 端点、密钥与配置

- 端点按 OpenAI 兼容协议（`/chat/completions`），默认 `http://100.111.24.121:8317/v1`，模型默认 `gemini-3.8-flash-high`（已实测支持 `tools`/`tool_calls`）。
- **API Key 不落仓库**：界面里明文输入 → `ISecretProtector.Protect`（Windows 上是 DPAPI）→ 写 `%LocalAppData%/CKAN/ai-assistant.json`。`Protect` 返回 null（无法安全保存）时只留在内存并提示。
- 配置文件写盘用 `tmp → File.Move(overwrite)`，读坏回落默认值——半写的配置文件不能变成「插件打不开」。
- 非 ASCII 请求体必须是 UTF-8（该端点对转义/编码敏感，实测内联中文 JSON 会 400）。

## 6. 失败模式（每一条都要在界面上说得清）

| 情况 | 表现 | 处理 |
| --- | --- | --- |
| 没填 Key / Key 解不开 | 设置区提示，发送按钮禁用 | 不发请求，不产生 401 噪音 |
| 端点连不上 / 超时（120s） | 气泡里「连不上端点」+ 地址 | 保留输入，可重试 |
| 模型返回 `tool_calls` 但参数是坏 JSON | 工具返回错误对象，模型自己纠正 | 不抛异常、不崩页面 |
| 工具轮次用尽 | 先发 `tool_choice: "none"` 的收口请求拿正文；只有收口也拿不到正文时才显示「工具调用轮次用尽」 | 防止死循环烧 token，同时不把已经查到的结果丢掉 |
| MCP 未安装 / 起不来 | `web_search` 返回 `web_search_unavailable` | 模型据实说「搜不了网」，仍可回答仓库侧问题 |
| 仓库快照为空（没切实例/没刷新） | `ckan_context` 里 `availableCount=0` | 提示先到「模组库」刷新索引 |

## 7. 安全边界（写权限只有一个口子，且必须过闸门）

「让 AI 操作 CKAN」的完整形态会包含安装与卸载。写动作的闸门不放在模型侧，而在宿主桥里：模型只能**发起**安装请求，真正动磁盘前必须过三道关。

- **计划先行**：`PlanInstallAsync` 用 `IChangeSetService` 算出依赖与被顺带安装/升级的项，模型与工具拿到的是计划，不是执行权。
- **用户点头**：`InstallAsync` 弹 `IDialogService.ConfirmAsync("AI 想安装模组", …)`，正文列出每一步；用户拒绝返回 `applied=false` 且磁盘无变化——拒绝不是错误，模型应原样转述。
- **确认后重算**：确认期间仓库可能刷新，落盘前重新构建变更集，不把几分钟前的计划当成事实；真正执行交给 `IInstallerService`，并通过 `IOperationQueue` 进底栏后台任务（可取消、可看进度）。
- **结果分离**：工具把 `applied/summary/installed/removed/failed/warnings` 原样回给模型；`applied=false` 不得被说成「装好了」（系统提示第 5 条钉死这一点）。

仍未开的口子：卸载、批量安装（一次多标识符）、把安装结果写回对话历史之外的状态。

## 8. 测试与验证

- 插件侧单测：工具循环（正常/工具后回答/坏参数/轮次用尽/导航失败/搜索不可用）、设置读写往返、Key 加密后落盘不含明文。
- 宿主侧单测：桥的评分排序、limit 夹取、详情兜底、导航未知键失败。
- 未在真机验证的部分写在 PR/交付说明里，不写成"已完成"。

## 9. 已知缺口

- 流式输出（`stream:true`）未接：当前整段返回，长回答会有明显等待。
- 会话不落盘：关掉页面即清空。
- MCP 子进程按需启动、空闲退出；默认命令留空（不猜用户机器上装了什么），没配时 `web_search` 直接回 `web_search_unavailable`，
  模型据实说「没联网查到」。
- **没有在无 Python 运行时的机器上给搜索回退**：`free-search-mcp` 是 Python 项目（不是 Node），
  打进安装包要走 PyInstaller onedir（232 MB / 启动 1.3 s）或复用用户已有 Python；
  真要甩掉 Python，只需抄它一个端点（`POST https://api.anysearch.com/v1/search`，无鉴权，约 60 行 C#）。
  体积、启动耗时、引擎质量风险与协议细节见 `C:/Users/43701/Documents/github/free-search-mcp/PACKAGING.md`。

## 10. 实测记录（真实端点，2026-11-03）

- `/v1/models` HTTP 200 / 2,952 B / 0.45 s，列表里含 `gemini-3.8-flash-high`。
- 原生 `tools` 往返确认：第一轮只回 `tool_calls`（`finish_reason=tool_calls`，`content` 缺失），
  回灌 `role: "tool"` 后仍可能继续要工具。
- 加收口请求后，一整轮真实问答：`stop=Completed rounds=3 traces=6 chars=960`——工具调了 6 次
  （`ckan_context` 1 次、`ckan_search_mods` 4 次、`ckan_installed` 1 次），最终 960 字中文回答。
- 这两条跑在 `tests/CkanUI.Plugin.Ai.Tests/LiveEndpointTests.cs`，默认 `Assert.Ignore`；
  设 `CKANUI_AI_LIVE=1` 与 `CKANUI_AI_KEY`（值不入库）后手动运行。
- 未验证：MCP 子进程真握手（本机没有装 free-search-mcp 的 Python 环境）、流式输出、长会话截断策略。

## 11. 底栏入口与安装闸门（2026-11-03）

### 11.1 为什么从「一个页签」变成「底栏按钮 + 抽屉」

页签要先切到「工具 › AI 助手」才能开口，用户真正的动作是「手上有问题，顺手问一句」。现在底栏和后台任务胶囊同一行有「AI 助手」按钮（`StatusAiButton`），点开右侧抽屉（`AiAssistantOverlay`，宽 470，ZIndex 18），不离开当前页面就能问。

- 抽屉内容**现造**：`AiAssistantLauncher`（`src/CkanUI.App/Ai/AiAssistantLauncher.cs`）用 `PluginPageFactory.CreateFor(services, "ai-assistant", reservedKeys: null)` 解析视图模型、`ViewLocator.Build` 建视图。
- 不复用导航页的实例：同一个视图模型不能同时挂在页面舞台和抽屉里（一个控件只能有一个父）；且 `PluginPageCache` 只认导航页的键，认不到抽屉里这一份。
- **所以释放必须自己负责**：`Close()` 一律 `Dispose`（视图模型实现 `IDisposable`，停聊天流、退订事件），窗口关闭走 `OnClosed`。插件被停用时抽屉里的旧页面也会跟着关，不留一个「还在跟已停用插件对话」的幽灵。
- 打开失败（插件没启用 / 视图没建起来）**也开抽屉**并显示一条人话错误：空面板不解释原因，用户只会以为按钮坏了。
- Esc 收起抽屉（`OnGlobalKeyDown` 与 `OnWindowKeyDownTunnel` 都在最前面处理，抽屉盖在最上层，页面里的 Esc 不该越过它）。

### 11.2 安装：模型只能发起，闸门在宿主

工具 `ckan_install_mod`（第 7 个 `ckan_*`、共 8 个工具）→ 桥 `InstallAsync`：

1. `PlanInstallAsync` 用 `IChangeSetService.BuildAsync` 算计划（每步的 Action/From/To），冲突与顺带安装项一并回给模型；
2. 冲突非空或计划为空 → 直接 Fail，不弹框、不碰盘；
3. `IDialogService.ConfirmAsync("AI 想安装模组", …)` 逐条列出步骤；**用户拒绝返回 `applied=false`**（不是失败，磁盘没动）；
4. 同意后**重算**一遍计划（确认期间仓库可能刷新），再交 `IInstallerService.ApplyAsync`；
5. 队列可用时经 `IOperationQueue.Enqueue("install", "AI 安装 <id>", …)` 进底栏后台任务：进度可见、可取消，AI 面板不阻塞；
6. 结果 `applied/summary/installed/removed/failed/warnings` 原样回给模型，系统提示第 5 条禁止把 `applied=false` 说成「装好了」。

未开的口子：卸载、一次多标识符批量安装、安装中途取消的端到端验证（只有代码路径与 `WaitAsync(ct)`）。

已知缺口：**抽屉开着时去「工具 › 插件」停用 AI 插件**，抽屉里的旧页面不会自己关（它握着自己的程序集，加载上下文收不回来）；
关掉抽屉即释放。自动关闭需要在视图层订阅插件变更并判活，收益不抵复杂度，先记为已知项。

### 11.3 测试

- `tests/CkanUI.App.Tests/AiAssistantLauncherTests.cs`（8 条）：解析一次、插件未启用给启用路径、视图缺失/构造抛异常时释放页面、关闭幂等、释放抛异常不外泄、关掉重开必须重造实例。
- `tests/CkanUI.App.Tests/AiStatusBarTests.cs`（5 条）：按钮与后台任务胶囊同栏、点击打开且「要么有页面要么有解释」、再点收起、关闭按钮收起、Esc 收起。
- `tests/CkanUI.App.Tests/AiCkanBridgeInstallTests.cs`（12 条）：计划投影、冲突/空计划拦截、确认框文案、拒绝不碰安装器、确认后重算（Build 调 2 次）、队列路径、安装器抛异常折成 Fail。
- `tests/CkanUI.Plugin.Ai.Tests/AiToolRunnerTests.cs` 新增 5 条安装工具用例（空 identifier、桥失败、applied=false、成功、参数 schema）。

