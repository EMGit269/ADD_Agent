# ADD Agent 分层重构优化方案

本文目标是解决当前 ADD Agent 中 workflow、tool、skill、web/search、上下文压缩在同一层混跑的问题。核心原则是：**主循环只编排 turn，各策略系统通过明确契约交接**。

## 1. 当前问题

当前实现已经有 `WorkflowRouter`、`ToolRegistry`、`SkillCatalog`、`ContextPackBuilder`、`ToolResultCompactor` 等骨架，但它们仍主要围绕 `ChatWindow` 主流程运行：

```text
ChatWindow
-> PrepareAgentWorkflowRoute
-> BuildInitialSystemMessages
-> BuildToolDefinitionsForCurrentMode
-> ExecuteToolCallAsync
-> RecordAgentToolEvidence
-> ChatMessageHelpers 压缩
```

问题是这些职责仍然交叉：

- workflow 决定 route，但 tool surface、prompt、skill、web 查证仍散落在不同 partial 文件；
- `web_research` 同时承担普通网页搜索、URL fetch、RhinoCommon API pipeline；
- skill 摘要、reference 摘要、ledger、context pack 都在 system prompt 周围拼接；
- tool result 压缩一部分在 `ToolResultCompactor`，一部分在 `ChatMessageHelpers`；
- tool 执行仍集中在 `ChatWindow.ToolDispatch.cs` 的大 if/else；
- `ToolSchemaFactory` 尚未真正接管 schema，工具定义仍在 `ChatWindow.ToolDefinitions.cs` 手写。

最终结果是：新增能力越多，主流程越容易变成“策略中转站”。

## 2. CodeWhale 的参考做法

CodeWhale 的关键不是功能更多，而是边界更清楚。

### 2.1 Turn loop 只编排

参考：

- `CodeWhale-main/crates/tui/src/core/engine/turn_loop.rs`
- `CodeWhale-main/crates/tui/src/core/engine.rs`

CodeWhale 的 turn loop 负责：

- 刷新 system prompt；
- 检查上下文是否需要压缩；
- 构造请求；
- 接收模型输出；
- 执行工具；
- 写回结果；
- 处理取消、重试、最大步数。

它不直接维护每个工具的 schema，不直接解析 skill 文件，也不在主循环里写大量业务判断。

ADD Agent 应学习的是：`ChatWindow` 不应继续承担 agent runtime 职责，应逐步抽出 `AgentTurnCoordinator`。

### 2.2 Tool registry / catalog 分离

参考：

- `CodeWhale-main/crates/tui/src/tools/registry.rs`
- `CodeWhale-main/crates/tui/src/core/engine/tool_catalog.rs`
- `CodeWhale-main/docs/TOOL_SURFACE.md`
- `CodeWhale-main/docs/TOOL_LIFECYCLE.md`

CodeWhale 分成两层：

```text
ToolRegistry
  注册所有工具，执行工具，生成 API schema

ToolCatalog / ToolSurface
  决定本轮哪些工具 active，哪些 deferred
```

重点：

- 全量工具注册表长期存在；
- 模型每轮只看到 active 工具；
- deferred 工具仍可通过工具搜索/水合机制发现；
- 工具排序和 schema 稳定，保护 DeepSeek prefix cache；
- provider/mode 可以影响工具面，但不改工具执行本身。

ADD Agent 当前有 `ToolRegistry` 和 `ToolSurfacePolicy`，但还缺 `ToolSurfaceBuilder` 和 schema 统一出口。

### 2.3 Skill registry 独立

参考：

- `CodeWhale-main/crates/tui/src/skills/mod.rs`
- `CodeWhale-main/crates/tui/src/tools/skill.rs`
- `CodeWhale-main/docs/SKILL_INVOCATION_DESIGN.md`
- `CodeWhale-main/docs/MEMORY.md`

CodeWhale 的 skill 做法：

- 从多个目录发现 skill；
- 解析 frontmatter；
- exact name 优先；
- fuzzy match 只建议，不静默加载；
- 摘要/列表和正文加载分离；
- 未来计划用 `$skill-name` 作为明确 skill invocation 入口。

ADD Agent 已有 `SkillCatalog`，但还缺 `SkillActivationPolicy`。现在主要靠 prompt 要求模型自己调用 `read_skill_file`，还没有“候选 skill 决策层”。

### 2.4 Context / compaction 独立

参考：

- `CodeWhale-main/crates/tui/src/core/engine/context.rs`
- `CodeWhale-main/crates/tui/src/compaction.rs`
- `CodeWhale-main/crates/tui/src/context_budget.rs`
- `CodeWhale-main/crates/tui/src/context_report.rs`

CodeWhale 的上下文策略：

- noisy tool 有更低压缩阈值；
- 大输出不直接塞回父上下文；
- tool result 可以变成 summary + artifact / handle；
- 压缩时 pin 最近消息、错误、路径、工作集；
- system prompt 和动态上下文分层。

ADD Agent 当前已有 `ToolResultCompactor`、`ContextCompactionPlanner`、`ChatMessageHelpers.ApplyLargeToolFoldInPlace`，但还没有统一 `ContextPipeline`。

## 3. 目标架构

建议目标链路：

```text
User input / attachments
-> AgentTurnCoordinator
   -> AgentTurnContextBuilder
   -> WorkflowRouter
   -> ContextPipeline
      -> ContextPackBuilder
      -> SkillCatalog
      -> ReferenceCatalog
      -> ContextLedger
   -> ToolSurfaceBuilder
      -> ToolRegistry
      -> ToolSchemaFactory
   -> LlmTurnRunner
   -> ToolCallLoop
      -> ToolExecutor
      -> ToolResultPipeline
         -> ToolResultCompactor
         -> ArtifactStore
         -> ContextLedger
```

`ChatWindow` 最终只负责：

- UI 输入输出；
- 用户设置；
- 调用 `AgentTurnCoordinator`;
- 展示 operation card；
- Rhino/GH UI thread 调度。

## 4. 建议目录结构

建议逐步从 `ADDGH/Agent` 下再拆子目录。当前项目可以先不移动旧文件，新增文件按新边界组织。

```text
ADDGH/Agent/
  Runtime/
    AgentTurnCoordinator.cs
    AgentTurnContextBuilder.cs
    LlmTurnRunner.cs
    ToolCallLoop.cs

  Workflow/
    WorkflowIntent.cs
    WorkflowRoute.cs
    WorkflowSignals.cs
    WorkflowSignalExtractor.cs
    WorkflowRouter.cs

  Tools/
    ToolRegistry.cs
    ToolDescriptor.cs
    ToolLifecycle.cs
    ToolSurfaceBuilder.cs
    ToolSchemaFactory.cs
    ToolExecutor.cs
    ToolExecutionResult.cs
    ToolResultPipeline.cs

  Skills/
    SkillCatalog.cs
    SkillIndexModels.cs
    SkillActivationPolicy.cs
    SkillQualityGate.cs

  References/
    ReferenceCatalog.cs
    ReferenceIndexModels.cs

  Context/
    ContextPipeline.cs
    ContextPackBuilder.cs
    ContextLedger.cs
    ContextBudget.cs
    ContextCompactionPlanner.cs
    ToolResultCompactor.cs
    ArtifactStore.cs

  Research/
    ApiDocSearchPipeline.cs
    WebResearchClient.cs
    WebResearchModels.cs
```

说明：如果暂时不想移动已有文件，可以先新增新文件在 `ADDGH/Agent` 根目录，等稳定后再分目录。

## 5. 具体文件拆分方案

### 5.1 `AgentTurnCoordinator.cs`

新增。

职责：

- 作为单轮 agent 请求入口；
- 接收 user input、model input、attachments；
- 调用 route、context、tool surface、LLM runner、tool loop；
- 不包含具体 tool 执行细节；
- 不解析 skill/reference/web 细节。

建议接口：

```csharp
public sealed class AgentTurnCoordinator
{
    public Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct);
}
```

参考 CodeWhale：

- `core/engine/turn_loop.rs`
- `core/engine.rs`

迁移来源：

- `ChatWindow.cs` 中发送前后的 agent 主流程；
- `ChatWindow.AgentRouting.cs` 中当前 route 准备逻辑；
- `CallLLMAPI` 周围的 tool-call loop。

### 5.2 `AgentTurnContextBuilder.cs`

新增。

职责：

- 从 UI/附件/画布状态构造 `AgentTurnContext`；
- 捕获 canvas summary；
- 不决定 workflow。

当前来源：

- `ChatWindow.AgentRouting.cs` 的 `BuildAgentTurnContext(...)`;
- `CaptureAgentCanvasStateSummary()`.

参考 CodeWhale：

- `context_report.rs`
- `workspace_context.rs`

### 5.3 `WorkflowRouter.cs`

已有，继续保留。

职责：

- 只根据 `AgentTurnContext` 和 `WorkflowSignals` 输出 `WorkflowRoute`;
- 不执行工具；
- 不读取 skill 正文；
- 不拼 system prompt。

当前已改为结构信号评分，方向正确。

还需优化：

- 把文件移动到 `ADDGH/Agent/Workflow/`;
- 增加单元测试；
- 输出 `RequiredCapabilities`，不要过早绑定具体 tool name。

参考 CodeWhale：

- `docs/MODES.md`
- `model_routing.rs`
- `tui/auto_router.rs`

### 5.4 `ToolSurfaceBuilder.cs`

新增，优先级很高。

职责：

- 输入 `WorkflowRoute`、`ToolRegistry`、layout mode、agent mode、provider capability；
- 输出本轮模型可见 tool definitions；
- 决定 active/deferred/hidden/deprecated；
- 保持工具顺序稳定；
- 给日志输出 tool surface 诊断。

建议接口：

```csharp
public sealed class ToolSurfaceBuilder
{
    public ToolSurface Build(ToolSurfaceRequest request);
}
```

替代当前：

- `ChatWindow.ToolDefinitions.cs` 末尾的多层 filter；
- `ToolSurfacePolicy.FilterForRoute(...)` 的单薄逻辑。

参考 CodeWhale：

- `core/engine/tool_catalog.rs`
- `tools/registry.rs`
- `docs/TOOL_SURFACE.md`
- `docs/TOOL_LIFECYCLE.md`

落地原则：

- P0：先只封装现有过滤逻辑；
- P1：把 deferred 工具策略加进去；
- P2：引入 provider-specific policy；
- P3：确保 tool schema 排序稳定。

### 5.5 `ToolSchemaFactory.cs`

已有但未真正使用。

职责：

- 统一生成工具 schema；
- 移除 `"Description"` 占位描述；
- 控制 required 字段；
- 控制 `additionalProperties=false`；
- 统一排序，减少 prompt prefix 抖动。

替代当前：

- `ChatWindow.ToolDefinitions.cs` 中手写匿名对象 schema。

参考 CodeWhale：

- `tools/schema_sanitize.rs`
- `tools/schema_canonicalize.rs`
- `tools/spec.rs`

落地顺序：

1. 先迁移 `web_research`、`read_skill_file`、`read_reference_json`;
2. 再迁移 GH 基础工具；
3. 最后迁移 C# Script 和图片工具。

### 5.6 `ToolExecutor.cs`

新增。

职责：

- 替代 `ChatWindow.ToolDispatch.cs` 的大 if/else；
- 根据 tool name 查找 executor；
- 统一返回 `ToolExecutionResult`;
- 处理 deprecated/removed/alias；
- 不做上下文压缩。

建议接口：

```csharp
public interface IAgentToolExecutor
{
    string Name { get; }
    Task<ToolExecutionResult> ExecuteAsync(JObject args, CancellationToken ct);
}
```

参考 CodeWhale：

- `tools/registry.rs`
- `core/engine/tool_execution.rs`
- `docs/TOOL_LIFECYCLE.md`

迁移顺序：

1. `web_research`;
2. `read_skill_file`;
3. `read_reference_json` / `import_reference_gh`;
4. C# Script 工具；
5. GH canvas mutation 工具。

### 5.7 `ToolResultPipeline.cs`

新增。

职责：

- 接收原始 tool result；
- 调用 `ToolResultCompactor`;
- 判断是否保留原文、摘要、artifact handle；
- 写入 `ContextLedger`;
- 返回最终进入 `_messages` 的 tool content。

参考 CodeWhale：

- `core/engine/context.rs`
- `tools/large_output_router.rs`
- `tools/tool_result_retrieval.rs`

ADD Agent 需要的规则：

```text
get_gh_components:
  summary 进上下文，完整 JSON 可 artifact

read_reference_json:
  metadata/component summary 进上下文，完整 JSON artifact

web_research:
  URL/title/snippet/diagnosis 进上下文，长正文 artifact

compile/check errors:
  错误码、组件 id、关键消息必须保留
```

### 5.8 `ArtifactStore.cs`

新增。

职责：

- 保存大 tool result；
- 返回 artifact id/path；
- 提供后续读取接口；
- 避免大 JSON 反复进入对话历史。

建议存储：

```text
.addgh/artifacts/tool-results/{yyyyMMdd}/{tool}_{timestamp}_{hash}.json
```

可先仅本地文件，不需要数据库。

参考 CodeWhale：

- `tools/large_output_router.rs`
- `retrieve_tool_result`
- `handle_read`

### 5.9 `ContextPipeline.cs`

新增，优先级高。

职责：

- 统一构造进入 LLM 的上下文；
- 组合 base prompt、typed prompt、context packs、skill catalog、reference catalog、ledger；
- 调用 context budget；
- 决定是否压缩历史；
- 不直接执行工具。

当前替代：

- `BuildInitialSystemMessages()`;
- `GetSkillsSummary()`;
- `BuildAgentContextPackPrompt()`;
- `BuildAgentContextLedgerPrompt()`;
- `ChatMessageHelpers` 部分上下文处理。

参考 CodeWhale：

- `core/engine/context.rs`
- `compaction.rs`
- `context_budget.rs`
- `prompts.rs`
- `prompt_zones.rs`

核心要求：

```text
Stable prefix:
  base system prompt
  stable tool schema

Volatile context:
  route
  context packs
  ledger
  skill/reference candidates
```

这样更利于 DeepSeek prefix cache。

### 5.10 `SkillActivationPolicy.cs`

新增。

职责：

- 根据 user text、workflow route、skill catalog 推荐候选 skill；
- exact match 优先；
- fuzzy match 只建议，不自动加载；
- 对模型自主调用 `read_skill_file` 给出候选提示；
- 未来支持 `$skill-name`。

参考 CodeWhale：

- `docs/SKILL_INVOCATION_DESIGN.md`
- `skills/mod.rs`
- `tools/skill.rs`

ADD Agent 第一阶段不需要完整 `$skill` UI，只需：

```text
如果用户文本明确包含 skill file/name -> 提示 exact candidate
如果 route 是 ApiDocLookup -> 推荐 official_rhinocommon_api_reference.md
如果 route 是 VisualModeling -> 推荐视觉/建模相关 skill
```

### 5.11 `ReferenceCatalog.cs`

已有，继续完善。

职责：

- 从 `skills/reference_index.md` 和 `reference/*.json` 构建 `reference.index.json`;
- 摘要常驻；
- JSON 正文按需读取；
- GH/GHX import 和 JSON read 分开。

还需补：

- 搜索接口；
- 根据 `WorkflowRoute` 推荐 top references；
- 保存 reference 后稳定更新索引；
- 单元测试。

参考 CodeWhale：

- `skills/mod.rs` 的 registry 思路；
- `MEMORY.md` 的“持久知识”和“临时证据”区分。

### 5.12 `ApiDocSearchPipeline.cs`

新增，建议从 `ChatWindow.WebResearch.cs` 拆出。

职责：

- 专门处理 RhinoCommon/Grasshopper API 查证；
- 不混普通 web search；
- 提供稳定 pipeline：

```text
Parse intent
-> Symbol expansion
-> Local official doc index / mcneel pages
-> Candidate ranking
-> Fetch selected official URL
-> Return signature evidence
```

参考 CodeWhale：

- `TOOL_SURFACE.md` 中 `web_search` 和 `fetch_url` 分离；
- `core/engine/context.rs` 中 web/search 结果压缩；
- tool niche 明确化原则。

ADD Agent 当前：

- `web_research mode=api_pipeline` 已有雏形；
- 下一步应该拆成独立工具 `search_api_docs`。

### 5.13 `WebResearchClient.cs`

新增。

职责：

- 普通网页搜索；
- fetch URL；
- timeout/budget；
- allowed domain；
- 返回统一模型。

不负责 API 文档领域逻辑。

从当前 `ChatWindow.WebResearch.cs` 拆出：

- `DownloadTextAsync`;
- `FetchWebPageAsync`;
- `SearchWebAsync`;
- Bing result parse。

### 5.14 `LlmTurnRunner.cs`

新增。

职责：

- 调用 provider；
- 处理 endpoint candidates；
- 解析模型响应；
- 不执行工具；
- 不决定 workflow。

当前替代：

- `CallLLMAPI` 中 provider request 部分；
- `ChatWindow.LlmTransport.cs` 中部分逻辑可保留为低层 transport。

参考 CodeWhale：

- `client/chat.rs`
- `core/engine/turn_loop.rs`

### 5.15 `ToolCallLoop.cs`

新增。

职责：

- 处理模型多轮 tool call；
- 调用 `ToolExecutor`;
- 调用 `ToolResultPipeline`;
- 决定是否继续下一轮模型请求；
- 处理 loop guard。

参考 CodeWhale：

- `core/engine/turn_loop.rs`
- `core/engine/tool_execution.rs`

## 6. 分阶段落地顺序

### Phase 1：先拆 tool surface 和 context pipeline

目标：不改变工具行为，只把策略集中。

新增/修改：

- 新增 `ToolSurfaceBuilder.cs`;
- 新增 `ContextPipeline.cs`;
- `ChatWindow.ToolDefinitions.cs` 调用 `ToolSurfaceBuilder`;
- `BuildInitialSystemMessages()` 改为调用 `ContextPipeline`;
- 保留 feature flags。

验收：

- 默认开关下行为不明显变化；
- `dotnet build` 通过；
- route 日志、tool surface 日志可读。

### Phase 2：拆 web/API search

目标：让 API 文档查证不再混在普通 websearch。

新增/修改：

- 新增 `ApiDocSearchPipeline.cs`;
- 新增 `WebResearchClient.cs`;
- 新增工具 `search_api_docs`;
- `web_research` 保留普通 search/fetch；
- `ApiDocLookup` route 优先暴露 `search_api_docs`。

验收：

- RhinoCommon API 查证走 `search_api_docs`;
- 普通联网仍走 `web_research`;
- 结果都经过 `ToolResultPipeline` 压缩。

### Phase 3：拆 tool executor

目标：替代 `ChatWindow.ToolDispatch.cs` 大 if/else。

新增/修改：

- 新增 `ToolExecutor.cs`;
- 新增 `IAgentToolExecutor`;
- 逐步迁移工具 executor；
- `ChatWindow.ToolDispatch.cs` 只做兼容入口。

验收：

- 已迁移工具通过 registry 执行；
- 未迁移工具仍 fallback 到旧 dispatcher。

### Phase 4：schema 工厂化

目标：稳定工具 schema，减少 prompt 成本和混乱。

新增/修改：

- 完善 `ToolSchemaFactory.cs`;
- 迁移 `web_research` / `search_api_docs` / skill / reference schema；
- 引入 schema canonical order。

验收：

- 工具定义无 `"Description"` placeholder；
- schema 字段顺序稳定；
- required 字段清晰。

### Phase 5：artifact/handle 化大结果

目标：真正降低 token 成本。

新增/修改：

- 新增 `ArtifactStore.cs`;
- `ToolResultPipeline` 按工具类型落 artifact；
- 增加读取 artifact 的内部方法或工具；
- `read_reference_json`、`get_gh_components`、`web_research` 先接入。

验收：

- 大 JSON 不再完整进入 `_messages`;
- 模型仍能通过摘要完成常规任务；
- 必要时可以读取全文。

### Phase 6：测试

新增测试项目：

```text
ADDGH.Tests/
  WorkflowRouterTests.cs
  ToolSurfaceBuilderTests.cs
  ContextPackBuilderTests.cs
  ReferenceCatalogTests.cs
  ApiDocSearchPipelineTests.cs
  ToolResultPipelineTests.cs
```

优先测：

- `ApiDocLookup` route 不靠单一关键词；
- C# 编译错误会给 API 查证 affordance；
- reference index 解析 `skills/reference_index.md`;
- large tool result 被压缩；
- tool surface 在不同 route 下稳定。

## 7. 文件依赖关系

推荐依赖方向：

```text
Runtime
  depends on Workflow, Tools, Context, Skills, References, Research

Workflow
  depends only on AgentTurnContext and signal models

Tools
  depends on WorkflowIntent / WorkflowRoute for policy
  does not depend on ChatWindow UI

Context
  depends on WorkflowRoute, SkillCatalog, ReferenceCatalog, ContextLedger
  does not execute tools

Skills
  no dependency on Tools executor

References
  no dependency on Tools executor

Research
  no dependency on Workflow
```

禁止方向：

```text
WorkflowRouter -> Tool execution
SkillCatalog -> Tool execution
ContextPipeline -> Rhino UI mutation
ToolExecutor -> Prompt assembly
Research -> WorkflowRouter
```

## 8. 当前代码到目标文件的迁移表

| 当前文件 | 问题 | 迁移目标 |
|---|---|---|
| `ChatWindow.cs` | system prompt、send flow、UI、LLM 入口混合 | `AgentTurnCoordinator`, `ContextPipeline`, UI 保留 |
| `ChatWindow.ToolDefinitions.cs` | tool schema 手写、过滤混合 | `ToolSchemaFactory`, `ToolSurfaceBuilder` |
| `ChatWindow.ToolDispatch.cs` | 大 if/else 执行器 | `ToolExecutor`, per-tool executors |
| `ChatWindow.WebResearch.cs` | web/search/API docs 混合 | `ApiDocSearchPipeline`, `WebResearchClient` |
| `ChatMessageHelpers.cs` | 历史裁剪和 tool result 压缩混合 | `ContextPipeline`, `ToolResultPipeline` |
| `ChatWindow.AgentRouting.cs` | route/context/tool policy 都挂在 ChatWindow | `AgentTurnContextBuilder`, `AgentTurnCoordinator` |
| `ChatWindow.AgentSkills.cs` | skill catalog 接入 ChatWindow | `SkillActivationPolicy`, `ContextPipeline` |
| `ChatWindow.SkillTools.cs` | skill/reference 工具执行和 catalog 刷新混合 | `ToolExecutor`, `ReferenceCatalog` |

## 9. 关键设计决策

### 9.1 Workflow 不自动执行工具

`WorkflowRouter` 只能输出建议：

```text
intent = ApiDocLookup
required_capability = api_doc_search
context_pack = api-doc-lookup
```

是否调用工具由 agent 推理和 tool call loop 完成。

### 9.2 Tool surface 不等于 tool registry

全量工具注册表长期存在；每轮模型只看到 active subset。

这能解决：

- 工具太多；
- schema 太长；
- 模型误用不相关工具；
- DeepSeek prefix cache 不稳定。

### 9.3 Skill 摘要和正文分离

摘要可以进 prompt；正文必须按需读取。

不允许：

```text
所有 skill 正文常驻
模糊匹配后自动读取多个 skill
```

### 9.4 API docs search 不应混普通 web search

`search_api_docs` 应是专用工具。普通 `web_research` 不承担 RhinoCommon 领域 pipeline。

### 9.5 大结果必须 summary + artifact

进入模型上下文的是：

```text
summary
evidence
artifact_id/path
```

不是完整大 JSON。

## 10. 最小下一步

建议下一步只做 Phase 1：

1. 新增 `ToolSurfaceBuilder.cs`；
2. 新增 `ContextPipeline.cs`；
3. `BuildInitialSystemMessages()` 改为调用 `ContextPipeline`;
4. `BuildToolDefinitionsForCurrentMode()` 改为调用 `ToolSurfaceBuilder`;
5. 保留现有 feature flags；
6. 编译验证。

这一步不会拆工具执行，也不会改 websearch 行为，但会先把“策略混层”的问题收束到两个明确出口。
