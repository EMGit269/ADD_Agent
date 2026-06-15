# Agent 架构优化实施日志

日期：2026-06-15

本日志记录本轮已实际落地的 agent 架构优化中间产物。目标是把 workflow 分流、tool surface、skill catalog、上下文证据记录先做成可回退的旁路能力，避免一次性改动主循环导致行为不可控。

## 1. 本轮实现范围

已完成：

- P0：新增 workflow route 与 ContextLedger 骨架。
- P1：新增 skill catalog/index 骨架，并接入 `read_skill_file` 与 self-training skill 写入。
- P2：新增 tool registry / lifecycle / surface policy 骨架，默认不启用过滤。
- P3：新增工具结果 evidence 记录，把工具执行结果摘要写入 ContextLedger。
- P4：新增 `ToolResultEnvelope` 与 `ToolResultCompactor`，目前只用于旁路摘要，不截断原始 tool result。
- P5：新增 `SkillQualityGate`，self-training 写入后用质量门决定 catalog 里的 `quality` / `verified` 标记，不阻断写文件。

未完成，建议下一轮继续：

- `ReferenceCatalog` / `reference.index.json`。
- `ContextPackBuilder`。
- tool schema 描述文本系统性重写。
- `ChatMessageHelpers` 大工具结果压缩策略升级。
- 针对 router / tool policy / skill catalog 的测试项目。

## 2. 新增代码文件

`ADDGH/Agent/AgentTurnContext.cs`

- 封装单轮请求上下文。
- 目前由 `ChatWindow.AgentRouting.cs` 在发送前构建。
- 输入给 `WorkflowRouter.Route(...)`。

`ADDGH/Agent/WorkflowIntent.cs`

- 定义标准 workflow intent。
- 已被 `WorkflowRoute`、`ToolDescriptor`、`SkillCatalog` 共用。

`ADDGH/Agent/WorkflowRoute.cs`

- 保存路由结果、必需工具、可选工具、上下文包、权限标记。
- 当前写入 `_currentWorkflowRoute` 与 `_contextLedger`。

`ADDGH/Agent/WorkflowRouter.cs`

- 第一版启发式 router。
- 参考 CodeWhale 的 mode/workflow 分层思想，但没有引入 LLM router。

`ADDGH/Agent/ContextLedger.cs`

- 记录 route、canvas state、tool evidence、loaded skills、references、decisions。
- prompt 注入默认关闭，只在 `ADDGH_USE_CONTEXT_LEDGER_PROMPT=1` 时进入 system message。

`ADDGH/Agent/ToolLifecycle.cs`

- 定义工具生命周期：`Active`、`Deferred`、`HiddenCompatibility`、`Deprecated`、`Removed`。

`ADDGH/Agent/ToolDescriptor.cs`

- 保存工具元数据：用途、是否只读、是否修改 canvas、是否写文件、适用 workflow。

`ADDGH/Agent/ToolRegistry.cs`

- 第一版工具注册表。
- 用于 tool surface policy，不负责执行工具。

`ADDGH/Agent/ToolSurfacePolicy.cs`

- 根据当前 route 过滤模型可见工具。
- 默认关闭：`ADDGH_USE_TOOL_SURFACE_POLICY=1` 才启用。
- 未登记工具暂时保留可见，避免漏配导致工具消失。

`ADDGH/Agent/ToolSchemaFactory.cs`

- 预留 schema 工厂位置。
- 当前尚未替换 `ChatWindow.ToolDefinitions.cs` 内的旧 schema 拼装。

`ADDGH/Agent/SkillIndexModels.cs`

- 定义 `SkillIndex`、`SkillIndexEntry`。

`ADDGH/Agent/SkillCatalog.cs`

- 扫描 `skills/*.md`，解析 frontmatter，生成 `skills/skills.index.json`。
- 渲染短摘要供 system prompt 使用。
- 读取 skill 正文时输出带来源、quality、verified、tags 的结构化正文。

`ADDGH/Agent/ToolResultEnvelope.cs`

- 定义工具结果旁路摘要结构。
- 字段包括工具名、成功状态、摘要、artifact 路径、结果类型、原始字符数、时间。

`ADDGH/Agent/ToolResultCompactor.cs`

- 从文本或 JSON 工具结果提取摘要。
- 当前只用于 `ContextLedger.RecordToolResult(...)`，不改变 `_messages` 里的原始 tool content。
- 参考 CodeWhale `core/engine/context.rs` 的思路：进入长期上下文前先摘要化，完整内容用 artifact/path 保留。

`ADDGH/Agent/SkillQualityGate.cs`

- 对生成的 skill markdown 做轻量检查。
- 检查项：frontmatter、description、section、验证/检查指导、长度。
- 不通过时把 catalog 标记降为 `experimental` / `verified=false`，但不阻止 self-training 写入，避免破坏现有流程。

## 3. 修改的现有代码文件

`ADDGH/ChatWindow.AgentRouting.cs`

- 新增 `_workflowRouter`、`_toolSurfacePolicy`、`_contextLedger`、`_currentWorkflowRoute`。
- 新增 `PrepareAgentWorkflowRoute(...)`。
- 新增 `BuildAgentTurnContext(...)`。
- 新增 `CaptureAgentCanvasStateSummary(...)`。
- 新增 `BuildAgentContextLedgerPrompt(...)`。
- 新增 `ResetAgentContextLedger()`。
- 新增 `ApplyWorkflowToolSurfacePolicy(...)`。
- 新增 `RecordAgentToolEvidence(...)`，在工具执行后把压缩摘要写入 ledger。

`ADDGH/ChatWindow.AgentSkills.cs`

- 新增 `_skillCatalog`。
- 新增 `BuildSkillCatalogSummary()`。
- 新增 `ExecuteReadSkillFileWithCatalog(...)`。
- 新增 `UpsertSkillCatalogEntry(...)`。

`ADDGH/ChatWindow.cs`

- `BuildInitialSystemMessages()` 追加 `BuildAgentContextLedgerPrompt()`，但默认不启用。
- 新对话时调用 `ResetAgentContextLedger()`。
- 发送前调用 `PrepareAgentWorkflowRoute(...)`。
- 工具执行后调用 `RecordAgentToolEvidence(...)`。

`ADDGH/ChatWindow.ToolDefinitions.cs`

- `BuildToolDefinitionsForCurrentMode()` 末尾接入 `ApplyWorkflowToolSurfacePolicy(...)`。
- 该策略默认关闭。

`ADDGH/ChatWindow.SkillTools.cs`

- `ExecuteReadSkillFile(...)` 优先走 `SkillCatalog`。
- legacy skill 读取仍保留。
- `ExecuteReadReferenceJson(...)` 读取成功后记录 reference evidence。
- `ExecuteImportReferenceGh(...)` 导入成功后记录 reference evidence。
- `ExecuteCreateGhSkill(...)` 写入后 upsert skill catalog，标记为 `experimental`。

`ADDGH/ChatWindow.SelfTraining.cs`

- `CreateOrUpdateTrainingSkill(...)` 写入 skill 后调用 `SkillQualityGate`。
- catalog upsert 使用质量门返回的 `RecommendedQuality` 与 `Verified`。
- 不通过时只写 warning，不阻断写入。

`ADDGH/DeploymentOptions.cs`

- 新增 `UseWorkflowRouter`，默认开启，可用 `ADDGH_USE_WORKFLOW_ROUTER=0` 关闭。
- 新增 `UseContextLedgerPrompt`，默认关闭，可用 `ADDGH_USE_CONTEXT_LEDGER_PROMPT=1` 开启。
- 新增 `UseSkillCatalogIndex`，默认开启，可用 `ADDGH_USE_SKILL_CATALOG_INDEX=0` 关闭。
- 新增 `UseToolSurfacePolicy`，默认关闭，可用 `ADDGH_USE_TOOL_SURFACE_POLICY=1` 开启。
- 新增 `ContextLedgerPromptMaxChars = 4000`。

## 4. 当前调用关系

```text
BtnSend_Click
-> PrepareAgentWorkflowRoute
   -> BuildAgentTurnContext
   -> WorkflowRouter.Route
   -> ContextLedger.RecordRoute
-> BuildToolDefinitionsForCurrentMode
   -> existing layout/agent/vision filters
   -> ApplyWorkflowToolSurfacePolicy (默认关闭)
-> CallLLMAPI
-> ExecuteToolCallAsync / ExecuteToolCall
-> RecordAgentToolEvidence
   -> ToolResultCompactor.BuildEnvelope
   -> ContextLedger.RecordToolResult
-> 原始 tool result 仍按旧逻辑写入 _messages
```

Skill 读取链路：

```text
GetSkillsSummary
-> BuildSkillCatalogSummary
   -> SkillCatalog.RenderSummary
   -> skills/skills.index.json

read_skill_file
-> ExecuteReadSkillFile
   -> ExecuteReadSkillFileWithCatalog
      -> SkillCatalog.FindByFileName
      -> SkillCatalog.LoadSkillBody
      -> ContextLedger.RecordLoadedSkill
   -> legacy file read fallback
```

Self-training skill 写入链路：

```text
CompleteSelfTrainingWithSkill
-> CreateOrUpdateTrainingSkill
   -> File.WriteAllText
   -> SkillQualityGate.Evaluate
   -> AppendTrainingSkillIndexEntry
   -> UpsertSkillCatalogEntry
   -> UpdateSkillLibraryUI
```

## 5. 本轮参照的 CodeWhale 内容

`CodeWhale-main/docs/MODES.md`

- 用于区分 workflow/mode/权限，不把所有判断塞进 prompt。
- ADD Agent 对应落地为 `WorkflowIntent`、`WorkflowRoute`、`WorkflowRouter`。

`CodeWhale-main/docs/TOOL_SURFACE.md`

- 用于区分工具注册表与模型可见工具面。
- ADD Agent 对应落地为 `ToolRegistry`、`ToolDescriptor`、`ToolSurfacePolicy`。

`CodeWhale-main/docs/TOOL_LIFECYCLE.md`

- 用于管理 active/deferred/deprecated/removed 工具。
- ADD Agent 对应落地为 `ToolLifecycle`，并让 deferred 工具在 policy 开启时默认隐藏。

`CodeWhale-main/docs/SKILL_INVOCATION_DESIGN.md`

- 用于“摘要常驻、正文按需读取”的 skill 设计。
- ADD Agent 对应落地为 `SkillCatalog.RenderSummary()` 与 `read_skill_file` catalog path。

`CodeWhale-main/docs/MEMORY.md`

- 用于区分持久知识和本轮临时证据。
- ADD Agent 对应落地为 skill 文件/catalog 与 `ContextLedger` 分离。

`CodeWhale-main/crates/tui/src/core/engine/context.rs`

- 用于工具结果进入上下文前摘要化的思路。
- ADD Agent 对应落地为 `ToolResultEnvelope` 与 `ToolResultCompactor`。

`CodeWhale-main/crates/tui/src/skills/mod.rs`

- 用于 skill 扫描、摘要和警告收集。
- ADD Agent 对应落地为 `SkillCatalog`。

`CodeWhale-main/crates/tui/src/tools/schema_sanitize.rs` 与 `schema_canonicalize.rs`

- 当前只作为后续 schema 整理参考，本轮没有替换 schema 生成。

## 6. 行为开关与回退策略

默认行为变化很小：

- workflow router 默认只记录 route，不过滤工具。
- ContextLedger prompt 默认不注入。
- tool surface policy 默认关闭。
- skill catalog 默认开启，但 `read_skill_file` 有 legacy fallback。
- tool result compactor 默认只写 ledger，不压缩 `_messages`。
- skill quality gate 不阻断写文件，只影响 catalog 标记。

出现问题时的快速回退：

- 关闭 router：`ADDGH_USE_WORKFLOW_ROUTER=0`
- 关闭 ledger prompt：不设置 `ADDGH_USE_CONTEXT_LEDGER_PROMPT`，或设为非 `1`
- 关闭 skill catalog：`ADDGH_USE_SKILL_CATALOG_INDEX=0`
- 关闭 tool policy：不设置 `ADDGH_USE_TOOL_SURFACE_POLICY`，或设为非 `1`

## 7. 验证结果

已执行：

```powershell
dotnet build ADDGH\ADDGH.csproj --no-restore
```

结果：

```text
0 warnings
0 errors
```

构建输出：

```text
ADDGH\bin\Debug\net48\ADDGH.gha
```

## 8. 下一轮建议顺序

1. 手动运行 Rhino/Grasshopper，验证默认开关下行为与旧版一致。
2. 设置 `ADDGH_USE_TOOL_SURFACE_POLICY=1`，分别测试 AI image、GH create、C# fix、self-training。
3. 实现 `ReferenceCatalog`，把 reference 与 skill 一样改成摘要常驻、正文按需读取。
4. 给 `ToolRegistry` 补齐所有实际工具，减少 unknown tool 依赖。
5. 改写 `ChatWindow.ToolDefinitions.cs` 中占位或弱描述的 tool schema description。
6. 在 `ChatMessageHelpers` 中引入可开关的大结果压缩，把 `ToolResultCompactor` 从旁路升级为实际 context 成本控制。
## 9. 2026-06-15 追加落地：ContextPackBuilder / ReferenceCatalog / ApiDocLookup

本轮追加完成：

- 新增 `ADDGH/Agent/WorkflowSignals.cs` 与 `ADDGH/Agent/WorkflowSignalExtractor.cs`：把 workflow 判断从简单关键词命中升级为结构化信号评分。
- 重构 `ADDGH/Agent/WorkflowRouter.cs`：`ApiDocLookup` 通过 API member pattern、RhinoCommon/Grasshopper symbol、C# 编译错误、签名/重载意图等多信号评分进入；C# create/fix route 在 API 风险中等时保留 `web_research` optional affordance，允许 agent 自主查证。
- 新增 `ADDGH/Agent/ContextPackBuilder.cs`：根据 `WorkflowRoute.ContextPacks` 渲染 route、canvas-state、reference-index、api-doc-lookup、web-research 等小型上下文包。
- 新增 `ADDGH/Agent/ReferenceCatalog.cs` 与 `ReferenceIndexModels.cs`：从 `skills/reference_index.md` 和 `reference/*.json` 构建结构化 `reference.index.json`，为 reference 摘要常驻、正文按需读取做准备。
- 新增 `ADDGH/ChatWindow.AgentReferences.cs`：集中封装 reference catalog summary 与刷新逻辑。
- `ChatWindow.AgentRouting.cs` 接入 `_contextPackBuilder`，发送前将当前 route 的 context packs 注入 system prompt；可用 `ADDGH_USE_CONTEXT_PACK_PROMPT=0` 回退。
- `ChatWindow.ChatRendering.cs`、`ChatWindow.SkillTools.cs` 在保存、删除、读取、导入 reference 后刷新 reference catalog。
- `ChatWindow.WebResearch.cs` 增加 web research 专属 timeout、request budget、API doc pipeline 查询/页面预算、elapsed/request/cache 诊断字段。

当前仍未完成：

- `ToolSchemaFactory` 尚未替换 `ChatWindow.ToolDefinitions.cs` 的手写 schema。
- `ToolRegistry` 还没有覆盖并校验所有实际工具。
- `ToolSurfacePolicy` 默认仍关闭，尚未做真实用户场景回归。
- `ContextPackBuilder` 目前只增量注入 route-specific packs，尚未替换全量 skill summary 注入策略。
- 还没有独立测试项目覆盖 WorkflowRouter / ReferenceCatalog / ContextPackBuilder。

## 10. 2026-06-15 追加落地：Phase 1 薄抽离

本轮按 `docs/agent_layered_refactor_plan.md` 的 Phase 1 先做低风险薄抽离：

- 新增 `ADDGH/Agent/ToolSurfaceBuilder.cs`：统一承接 tool surface 构建入口。当前仍复用旧的 layout / agent mode / vision / workflow filter，保持工具过滤行为不变。
- 新增 `ADDGH/Agent/ContextPipeline.cs`：统一承接初始 system messages 构建入口。当前仍保持原有 prompt 顺序：base prompt、typed prompt、context pack、ledger、skill summary。
- `ChatWindow.ToolDefinitions.cs` 不再直接串联多个过滤器，改为调用 `BuildAgentToolSurface(...)`。
- `ChatWindow.cs` 的 `BuildInitialSystemMessages()` 不再直接拼装多个上下文来源，改为调用 `_contextPipeline.BuildInitialSystemMessages(...)`。

这一步的目标不是改变行为，而是先把 tool surface 和 context assembly 两个策略出口从 `ChatWindow` 主体中收束出来，为后续迁移 `ToolSchemaFactory`、`ToolExecutor`、`ToolResultPipeline` 打基础。

## 11. 2026-06-15 追加落地：ToolSurfaceBuilder 接管 mode filter

本轮继续收束工具面策略：

- `ADDGH/Agent/ToolSurfaceBuilder.cs` 接管 layout mode / agent mode 工具过滤规则。
- 原 `ChatWindow.ToolDefinitions.cs` 中的 `FilterToolsForLayoutMode`、`FilterToolsForAgentMode`、`RestrictAddComponentToolForScriptMode` 已删除。
- `ChatWindow.AgentRouting.cs` 只把当前 layout/agent mode、计划工具名、参考选择工具名、tool name resolver 传给 `ToolSurfaceBuilder`。

行为目标保持不变：

- Battery 模式隐藏 C# Script 创建工具。
- CSharpFirst 模式隐藏批量原生图、legacy script graph、native editor、动态端口修改等工具。
- Plan 模式只暴露只读/规划相关工具和 `show_plan_steps`。
- CSharpFirst 模式下 `add_gh_component`、`set_gh_component_value`、`modify_gh_component_ports` 的描述仍按旧逻辑收窄。

这一步让 `ChatWindow.ToolDefinitions.cs` 更接近“只定义工具 schema”，不再承担 mode policy。

## 12. 2026-06-15 追加落地：ToolSchemaFactory 第一批迁移

本轮开始迁移工具 schema 生成：

- `ADDGH/Agent/ToolSchemaFactory.cs` 新增基础 schema helper：`String`、`Integer`、`Number`、`Boolean`、`StringArray`。
- `ChatWindow.ToolDefinitions.cs` 中 `web_research`、`read_skill_file`、`read_reference_json` 改为通过 `ToolSchemaFactory.Function(...)` 生成。
- 三个工具的名称、描述、参数字段、required 字段保持原语义。
- 新 factory 输出带 `additionalProperties=false`，开始向更严格、稳定的 schema 过渡。

后续可按同样方式继续迁移：

1. `import_reference_gh`、`create_gh_skill`;
2. `query_gh_components`、`get_component_context`、`read_component_script`;
3. C# Script 创建/编辑工具；
4. GH canvas mutation 工具。

## 13. 2026-06-15 追加落地：Skill / Reference 写入工具 schema 迁移

本轮继续迁移工具 schema：

- `create_gh_skill` 改为通过 `ToolSchemaFactory.Function(...)` 生成。
- `import_reference_gh` 改为通过 `ToolSchemaFactory.Function(...)` 生成。
- 与第一批保持一致，参数语义不变，新增统一的 `additionalProperties=false`。

迁移后，skill/reference 边界相关工具已有：

- `read_skill_file`
- `create_gh_skill`
- `read_reference_json`
- `import_reference_gh`
- `web_research`

下一批建议迁移只读查询工具：`query_gh_components`、`get_component_context`、`read_component_script`。

## 14. 2026-06-15 追加落地：只读组件查询工具 schema 迁移

本轮迁移只读查询类工具：

- `query_gh_components`
- `get_component_context`
- `read_component_script`

这些工具已改为通过 `ToolSchemaFactory.Function(...)` 生成。参数名称、描述和 required 语义保持不变，并统一获得 `additionalProperties=false`。

下一批可以继续迁移搜索/目录类工具，例如 `search_component_library`、`search_gh_component_catalog`，或者开始准备 `ToolExecutor` 的 registry 化。
