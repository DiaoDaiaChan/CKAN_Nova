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
| `ckan_navigate` | `page_key` | `{ok, message?}` | 唯一会改界面的工具；失败把原因回给模型 |
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

## 7. 安全边界（为什么现在不给写权限）

「让 AI 操作 CKAN」的完整形态会包含安装与卸载。那类动作需要：变更预览、用户逐项确认、失败回滚、以及「AI 说装好了」与真实结果的分离。当前版本把边界收在**只读 + 导航**：

- 模型能做的最大破坏是把你切到另一页；
- 想装 mod 必须在界面上自己点，AI 只能给建议与标识符。

这条线要放宽时，先加 **确认闸门**（`AiToolRunner` 里对写工具返回 `needs_confirmation` 而不是执行），再谈自动化。

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
