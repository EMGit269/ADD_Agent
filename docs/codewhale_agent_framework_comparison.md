# CodeWhale Agent 框架对 ADD Agent 的借鉴建议

本文基于 `CodeWhale-main` 中与 agent runtime、prompt engineering、context management、tool surface、skill ecosystem 相关的文档和源码进行对比，目标不是评价 CodeWhale 本身，而是提炼 ADD Agent 当前可落地的优化方向。

重点参考内容：

- `CodeWhale-main/docs/ARCHITECTURE.md`
- `CodeWhale-main/docs/AGENT_RUNTIME.md`
- `CodeWhale-main/docs/MODES.md`
- `CodeWhale-main/docs/MEMORY.md`
- `CodeWhale-main/docs/SKILL_INVOCATION_DESIGN.md`
- `CodeWhale-main/docs/TOOL_LIFECYCLE.md`
- `CodeWhale-main/docs/TOOL_SURFACE.md`
- `CodeWhale-main/docs/SUBAGENTS.md`
- `CodeWhale-main/crates/tui/src/prompts/base.md`
- `CodeWhale-main/crates/tui/src/prompts/agent.txt`
- `CodeWhale-main/crates/tui/src/prompts/compact.md`
- `CodeWhale-main/crates/tui/src/prompts/memory_guidance.md`
- `CodeWhale-main/crates/tui/src/core/engine/tool_catalog.rs`
- `CodeWhale-main/crates/tui/src/core/engine/context.rs`
- `CodeWhale-main/crates/tui/src/context_budget.rs`
- `CodeWhale-main/crates/tui/src/compaction.rs`
- `CodeWhale-main/crates/tui/src/skills/mod.rs`
- `CodeWhale-main/crates/tui/src/tools/skill.rs`
- `CodeWhale-main/crates/tui/src/tools/schema_sanitize.rs`
- `CodeWhale-main/crates/tui/src/tools/schema_canonicalize.rs`
- `CodeWhale-main/crates/tui/src/model_routing.rs`
- `CodeWhale-main/crates/tui/src/request_tuning.rs`

## 1. 总体判断

CodeWhale 对 ADD Agent 最有价值的地方不是“多 agent”或“更复杂的框架”，而是它把几个容易混在一起的概念拆开治理：

- 模式和权限分离：Plan / Agent / YOLO 是交互模式，approval 是工具执行权限，model auto 是模型路由，WhaleFlow 是长期 workflow 覆盖层。
- 工具注册和工具可见性分离：工具可以注册并可执行，但不一定进入首轮 active catalog。
- skill 目录和 skill 正文分离：系统提示只注入 skill 名称、描述、路径，真正需要时再读取 `SKILL.md`。
- 长输出和父上下文分离：子 agent、测试日志、大工具输出都尽量返回 summary / handle / artifact，不把全文塞回父对话。
- 静态 prompt 和动态上下文分离：稳定内容放在系统提示前缀，动态内容放在后面，尽量保护 DeepSeek prefix cache。

ADD Agent 当前的问题正好集中在这些边界上：tools、workflow、skill、reference、视觉流程、自训练流程都能工作，但缺少统一治理层，所以调用链越来越依赖 prompt 和 `ChatWindow` 内部状态。

建议 ADD Agent 不要照搬 CodeWhale 的全部 runtime，而是优先吸收以下四类机制：

```text
ToolSurfacePolicy
WorkflowRouter
SkillCatalog + load_skill
ContextLedger + ContextBudget
```

## 2. CodeWhale 的关键设计

### 2.1 模式不是 workflow

CodeWhale 在 `docs/MODES.md` 中把几件事拆得很清楚：

- Plan：只读调查和计划。
- Agent：允许多步工具执行，写文件和 shell 受权限控制。
- YOLO：高信任自动批准。
- Model auto：只决定模型和 thinking effort，不决定工具权限。
- WhaleFlow：长期 workflow/progress overlay，不作为第四种普通模式。

这对 ADD Agent 的启发是：不要把“用户现在要做什么 workflow”直接等同于 UI mode 或 tool filter。比如：

- 生成 Grasshopper 定义；
- 修复 C# Script；
- 从图片生成参数化模型；
- 读取 reference；
- 自训练沉淀 skill；
- 视觉复核；
- Web research；

这些应该是 workflow intent，而不是散落在 prompt、按钮状态、工具 blocked set、visual workflow state 里的隐式分支。

建议 ADD Agent 增加一个显式路由层：

```csharp
public enum WorkflowIntent
{
    GeneralChat,
    GrasshopperCreate,
    GrasshopperModify,
    CSharpScriptFix,
    VisualAnalysis,
    VisualFeedbackLoop,
    SkillLookup,
    SkillAuthoring,
    ReferenceLookup,
    SelfTraining,
    WebResearch
}

public sealed record WorkflowRoute(
    WorkflowIntent Intent,
    string Reason,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> OptionalTools,
    IReadOnlyList<string> ContextPacks,
    bool RequiresVisualReview,
    bool AllowsSelfTrainingWrite
);
```

`WorkflowRouter` 的输出应该成为后续工具面、上下文包、提示词补充的唯一入口。这样 workflow 分流不再靠多个地方的 if/else 互相猜。

### 2.2 工具面有生命周期，而不是一股脑给模型

CodeWhale 的工具治理核心在 `tool_catalog.rs` 和 `TOOL_LIFECYCLE.md`：

- active：首轮直接可见，模型默认可选。
- deferred：注册并可搜索，但首轮不展示完整 schema。
- hidden-compatibility：旧名称仍可执行，但不再主动展示。
- deprecated：仍可执行，但结果 metadata 提醒替代工具。
- removed：彻底移除。

它还特别强调工具目录前缀稳定：active head 排序稳定，deferred 工具激活后追加到 tail，不重排 head，避免破坏 DeepSeek prefix cache。

ADD Agent 当前最大的问题之一是工具 schema 描述和工具过滤都不够“政策化”。建议建立 `ToolSurfacePolicy`：

```csharp
public enum ToolLifecycle
{
    Active,
    Deferred,
    HiddenCompatibility,
    Deprecated,
    Removed
}

public sealed record ToolDescriptor(
    string Name,
    ToolLifecycle Lifecycle,
    string Capability,
    string CanonicalUseCase,
    string? Replacement,
    IReadOnlyList<WorkflowIntent> IntendedWorkflows,
    bool IsReadOnly,
    bool IsDestructive,
    int TokenCostRank
);
```

落地规则：

- 每个工具必须有唯一 niche，不能只写 `"Description"`。
- 同类工具只保留一个 canonical 名称给模型看。
- 兼容旧名称可以保留在 dispatcher，但从模型可见 catalog 移除。
- 高成本、少用、容易误用的工具默认 deferred。
- 每个 workflow 只拿到一组精简工具，而不是全量工具再靠 prompt 约束。

优先治理对象：

- Grasshopper 创建/编辑/连接/检查类工具。
- C# Script 写入、修复、执行类工具。
- skill 读取、skill 写入、自训练写入类工具。
- reference 导入、API 查询、Web research 类工具。
- 视觉预处理、视觉复核、图像生成类工具。

### 2.3 deferred tool 的第一次调用不要直接失败

CodeWhale 的 deferred tool 机制有一个细节值得借鉴：如果模型调用了还没 active 的 deferred 工具，runtime 不直接执行，而是返回“schema hydrated，工具已加载，请按 schema 重试”的结果。

这比“工具不存在”更适合弱模型或低成本模型，因为它给了模型一次自修正机会。

ADD Agent 可以实现类似机制：

```text
模型调用 deferred 工具
-> 不执行副作用
-> 返回工具名、用途、schema、必填字段、常见字段纠错
-> 下一轮该工具进入 active tail
-> 模型重试
```

对准确性和安全性都有帮助，尤其适合：

- `execute_gh_definition`
- `set_csharp_script_code`
- `import_reference`
- `write_skill`
- `self_training_save_skill`
- `visual_final_review`

### 2.4 skill 只常驻目录，不常驻全文

CodeWhale 的 `skills/mod.rs` 和 `tools/skill.rs` 体现了一个清晰契约：

- 系统提示中的 `## Skills` 只列出 name、description、file path。
- 每个 description 截断到固定长度。
- 整个 skills block 有总字符预算。
- 需要某个 skill 时调用 `load_skill`，一次性读取 `SKILL.md` 正文和伴随文件列表。
- skill 搜索目录有明确优先级，workspace-local 优先于 global。
- 名称冲突采用 first-wins，而不是随机覆盖。

ADD Agent 目前的 skill 生态如果继续把大量摘要塞进 system prompt，会带来三类问题：

- token 成本线性增长；
- 模型误匹配概率上升；
- prompt prefix 更容易变化，影响缓存。

建议改为：

```text
skills/
  index.json
  official_xxx.md
  trained_xxx.md

system prompt:
  只注入 index 中匹配当前 workflow 的前 N 个 skill 摘要

tool:
  load_skill(name)
  search_skills(query, workflow, tags)
  validate_skill(name)
```

推荐 `skills.index.json` 结构：

```json
{
  "version": 1,
  "skills": [
    {
      "id": "grasshopper-csharp-window-generator",
      "path": "skills/trained_parametric_rectangular_window_genera_20260612_1659.md",
      "title": "Parametric rectangular window generator",
      "description": "Generate a parametric rectangular window definition with C# script and preview checks.",
      "tags": ["grasshopper", "csharp", "window", "trained"],
      "workflows": ["GrasshopperCreate", "CSharpScriptFix"],
      "quality": "trained",
      "verified": true,
      "last_verified_at": "2026-06-12T16:59:00+08:00",
      "token_estimate": 1800,
      "inputs": ["width", "height", "frame_depth"],
      "outputs": ["GH components", "C# script", "preview"]
    }
  ]
}
```

并且区分三类 skill：

- official：人工维护、优先级最高。
- trained：自训练生成，但必须有验证证据。
- experimental：可检索但默认不自动注入。

### 2.5 skill 激活要显式，不要“似乎相关就塞进去”

CodeWhale 的 `$skill-name` 设计虽然当前文档标注为 design only，但方向正确：skill invocation 应该有明确入口。

ADD Agent 可以采用更适合 UI 的形式：

- 用户显式提到 skill 名称：加载该 skill。
- workflow router 判断强相关：加载 skill 摘要，必要时再拉全文。
- 模型调用 `search_skills`：返回候选，不自动全文注入。
- 模型调用 `load_skill`：全文进入上下文，并记录到 `ContextLedger`。

不要做：

- 每轮全量注入所有 skill 正文。
- 通过模糊匹配静默加载多个 skill。
- 自训练 skill 写入后立即变成高优先级 skill。

### 2.6 上下文预算应该是结构化预算，不只是压缩消息

CodeWhale 有三层上下文管理：

- `context_budget.rs`：根据 context window、input tokens、output reservation 计算压力等级。
- `context.rs`：工具结果按工具类型压缩，noisy tool 有更低阈值。
- `compaction.rs`：压缩时 pin 最近消息、错误、patch、工作集路径、tool call/result 成对关系。

ADD Agent 目前更需要的是 `ContextLedger`，不是更长的摘要 prompt。

建议维护结构化上下文账本：

```json
{
  "canvas_state": {
    "component_count": 42,
    "important_components": [],
    "last_verified_preview": "..."
  },
  "workflow_state": {
    "intent": "GrasshopperCreate",
    "current_phase": "visual_review",
    "pending_questions": []
  },
  "tool_evidence": [
    {
      "tool": "execute_gh_definition",
      "status": "success",
      "summary": "Definition solved with 0 runtime errors",
      "artifact": "..."
    }
  ],
  "loaded_skills": [
    {
      "id": "grasshopper-csharp-window-generator",
      "path": "skills/...",
      "why_loaded": "matched workflow and user request",
      "token_estimate": 1800
    }
  ],
  "reference_evidence": [
    {
      "source": "RhinoCommon API",
      "symbol": "Rhino.Geometry.Brep",
      "summary": "..."
    }
  ],
  "decisions": [
    {
      "decision": "Use C# Script component for geometry generation",
      "reason": "..."
    }
  ]
}
```

之后每轮 prompt 只注入 ledger 的精简投影，而不是完整历史。

### 2.7 工具输出要有 summary / artifact / handle

CodeWhale 对大输出的处理非常克制：

- 测试输出、验证输出、子 agent 输出会被摘要化。
- 大输出放到 artifact 或 handle，父上下文只保留摘要。
- 子 agent 结果明确标注“self-report，需要父 agent 验证”。

ADD Agent 可以直接套用到以下场景：

- GH definition dump。
- C# Script 编译错误和执行日志。
- reference API 大段内容。
- Web research 页面内容。
- visual analysis 结果。
- self-training 原始对话。

建议工具结果统一返回：

```json
{
  "success": true,
  "summary": "short model-facing summary",
  "evidence": [],
  "artifact_path": "optional/path/or/id",
  "full_output_truncated": true,
  "next_actions": []
}
```

父上下文只放 `summary + evidence + artifact_path`。

## 3. 对 ADD Agent 的具体改造建议

### 3.1 第一阶段：工具治理，收益最高

目标：减少错误工具选择、降低 token、提高函数调用准确性。

建议工作：

1. 建立 `ToolDescriptor` 注册表，不再从零散方法直接拼 tool definitions。
2. 给所有工具补全明确 description、required fields、错误纠正提示。
3. 划分 active/deferred/hidden/deprecated。
4. 每个 workflow 输出一个 active tool allowlist。
5. 增加测试：同一 workflow 下工具列表稳定、无重复 niche、无 placeholder description。

首批 active 工具应该非常少，例如：

```text
GeneralChat:
  search_reference
  search_skills
  load_skill

GrasshopperCreate:
  inspect_canvas
  create_component
  connect_components
  set_component_params
  execute_gh_definition
  preview_canvas

CSharpScriptFix:
  get_csharp_script
  set_csharp_script
  execute_gh_definition
  inspect_errors

VisualWorkflow:
  preprocess_image
  create_component
  preview_canvas
  visual_final_review
```

### 3.2 第二阶段：WorkflowRouter

目标：把分流从 prompt 和 if/else 中抽出来。

建议输入：

- 用户原始文本；
- 是否有图片/附件；
- 当前 canvas 是否为空；
- 最近失败工具；
- 已加载 skill；
- 当前 mode；
- 是否处于 self-training。

建议输出：

- intent；
- confidence；
- allowed tools；
- required context packs；
- 是否需要视觉闭环；
- 是否允许写 skill；
- 是否需要用户确认。

关键规则：

- router 可以是启发式优先，LLM 分类兜底，不必一开始复杂。
- workflow route 必须可记录、可回放。
- 后续所有 prompt 和工具目录都从 route 派生。

### 3.3 第三阶段：SkillCatalog

目标：skill 从“markdown 文件堆”变成可检索、可治理的生态。

建议工作：

1. 自动扫描 `skills/*.md` 生成 `skills.index.json`。
2. 给每个 skill 增加 frontmatter 或 sidecar metadata。
3. 实现 `search_skills(query, workflow, tags)`。
4. 实现 `load_skill(id)`，只加载单个 skill 正文。
5. 自训练 skill 默认进入 `experimental`，通过验证后升为 `trained`。

自训练写入必须加质量门：

- 有成功执行证据；
- 有视觉复核或用户确认；
- 有适用条件；
- 有失败边界；
- 不重复已有 skill；
- token 估算不过大。

### 3.4 第四阶段：ContextLedger

目标：避免每轮靠历史消息和压缩摘要猜当前状态。

建议工作：

1. 每个工具执行后写入结构化 evidence。
2. 每次 workflow phase 变化写入当前 phase。
3. 每次加载 skill/reference 记录来源和原因。
4. 每轮 prompt 构建时由 `ContextPackBuilder` 选择 ledger 投影。
5. 压缩历史时保留 ledger，不让关键信息只存在自然语言摘要里。

建议 prompt 中上下文顺序：

```text
Static system policy
Project/domain rules
Available tool/skill catalog summary
Workflow route
ContextLedger projection
Loaded skill full body, only if needed
Current user message
```

### 3.5 第五阶段：DeepSeek 适配

CodeWhale 对 DeepSeek 特别重视三件事：

- strict tool schema；
- prompt-cache prefix stability；
- cheap router / flash model 路由。

ADD Agent 如果主要使用 DeepSeek，建议做：

1. 工具 schema canonicalize：字段顺序稳定。
2. strict schema sanitizer：补 `additionalProperties: false`，修 required 和 nullable。
3. system prompt 分层：静态在前，动态在后。
4. 工具目录排序稳定：active head 不因 mode 小改动反复变化。
5. 低成本 router：用便宜模型只做 workflow/model/skill 分类，主模型负责执行。

## 4. 哪些不建议照搬

### 4.1 不建议照搬 CodeWhale 的宪章式长人格 prompt

`base.md` 中有大量 constitution / personality / hierarchy 内容。它对通用 coding agent 有作用，但 ADD Agent 是 Rhino/Grasshopper 垂直工具，过长的人格层会挤占专业上下文。

ADD Agent 更适合短规则：

- 不伪造工具结果；
- 不声称未验证的视觉/执行结果；
- 写 GH 前先确认 workflow；
- 失败后读取错误而不是盲改；
- skill 写入必须有证据。

### 4.2 不建议一开始做完整 Fleet / durable worker

CodeWhale 的 fleet、sub-agent、headless runtime 适合通用 coding agent 的长期任务。ADD Agent 当前更紧迫的问题是单 agent 内部治理，不是高并发 worker。

可借鉴的是子任务结果契约：

```text
SUMMARY
CHANGES
EVIDENCE
RISKS
BLOCKERS
```

但不必马上引入完整多进程 worker。

### 4.3 不建议过早引入复杂 RLM

RLM 对长文档和大规模语义批处理有价值，但 ADD Agent 当前可先用更简单的 artifact/summary/handle 机制解决 80% 上下文污染问题。

## 5. 推荐落地顺序

### P0：先止血

- 修复工具 description placeholder。
- 为工具增加唯一 niche 和 workflow allowlist。
- skill 不再全量注入，先只注入 index 摘要。
- 大工具输出统一摘要化。
- self-training skill 写入增加验证门。

### P1：建立核心治理层

- `WorkflowRouter`
- `ToolSurfacePolicy`
- `SkillCatalog`
- `ContextLedger`
- `ContextPackBuilder`

### P2：提升 DeepSeek 成本和准确性

- prompt 静态/动态分层；
- tool schema canonicalize；
- strict schema sanitizer；
- active/deferred 工具目录；
- cheap router 分类。

### P3：长期能力

- workflow run record；
- artifact store；
- skill marketplace / versioning；
- 子任务摘要契约；
- 可回放的 tool execution ledger。

## 6. 针对当前混乱点的直接回答

### tools 调用混乱

根因不是工具太多，而是工具缺少生命周期、唯一 niche 和 workflow allowlist。

优先做 `ToolSurfacePolicy`，让模型每轮只看到与当前 workflow 相关的少量 canonical 工具。旧工具名可以保留给 dispatcher 兼容，但不要继续展示给模型。

### workflow 混乱

根因是分流规则没有单一出口。

优先做 `WorkflowRouter`。任何视觉流程、自训练流程、skill 流程、reference 流程，都应该先经过 route，再决定工具和上下文。

### skill 读取混乱

根因是 skill 同时承担了“目录、知识、流程、记忆、自训练结果”的角色。

优先拆成：

- `skills.index.json`：可检索目录；
- `load_skill`：按需加载正文；
- `SkillQualityGate`：控制自训练写入；
- `ContextLedger.loaded_skills`：记录本轮到底读了什么。

### 效率和 token 成本

最有效的不是压缩更多，而是少放无关内容：

- 首轮少工具；
- skill 只放目录；
- reference 只放命中摘要；
- 大输出只放 summary；
- 稳定 prompt 前缀；
- router 用便宜模型或启发式。

### 准确性

准确性来自证据闭环：

- 工具结果结构化；
- 执行失败必须进入 ledger；
- 视觉判断必须有 preview/analysis evidence；
- 子流程 self-report 不能直接当成功；
- skill 写入必须绑定验证证据。

## 7. 建议新增/调整的文件

建议后续在 ADD Agent 中逐步新增：

```text
ADDGH/Agent/WorkflowRouter.cs
ADDGH/Agent/WorkflowRoute.cs
ADDGH/Agent/ToolSurfacePolicy.cs
ADDGH/Agent/ToolDescriptor.cs
ADDGH/Agent/ContextLedger.cs
ADDGH/Agent/ContextPackBuilder.cs
ADDGH/Agent/SkillCatalog.cs
ADDGH/Agent/SkillQualityGate.cs
skills/skills.index.json
reference/reference.index.json
```

短期也可以先不移动文件，只在现有 `ChatWindow` partial 旁边增加这些类，逐步把逻辑迁出。

## 8. 版本管理建议补充

当前本地长期积累后再一次性推 GitHub，会让 GitHub 上的版本管理变弱：

- PR/diff 太大，难以 review。
- 回滚只能回滚一大坨。
- 很难定位哪个改动引入 bug。
- 文档、构建产物、代码改动混在一起。
- issue/branch/release 难以对应。

建议改成：

```text
main
  稳定可运行

feature/agent-tool-policy
  工具治理

feature/workflow-router
  workflow 分流

feature/skill-catalog
  skill 索引和读取

feature/context-ledger
  上下文账本
```

每个 feature 分支保持小步提交：

```text
commit 1: add ToolDescriptor model
commit 2: migrate GH tools to descriptor registry
commit 3: add workflow-specific allowlist
commit 4: add tests/docs
```

本地也应频繁 commit，不必等到 GitHub。GitHub 更适合作为远端备份、PR review、release tag 和 issue 追踪；本地 Git 则负责日常可回滚历史。

建议最低要求：

- 每完成一个可独立验证的小功能就 commit。
- 每天结束前 push 当前 feature 分支。
- release/build 产物不要进 Git。
- 文档变更和代码变更尽量分 commit。
- 大重构前先建分支，不在 main 上直接堆。

## 9. 最小可执行下一步

如果只做一件事，建议先做：

```text
ToolSurfacePolicy + skills.index.json
```

原因：

- 改动相对局部；
- 立刻降低 prompt 和 tool schema token；
- 立刻减少错误工具选择；
- 为 WorkflowRouter 和 ContextLedger 打基础；
- 不需要重写整个 `ChatWindow` 主循环。

完成后，再把 `BuildToolDefinitionsForCurrentMode` 改成：

```text
WorkflowRouter.Route(...)
-> ToolSurfacePolicy.BuildCatalog(route)
-> ContextPackBuilder.Build(route, ledger)
-> CallLLMAPI(...)
```

这样 ADD Agent 的 agent 架构会从“长 prompt + 大 if/else”逐步变成可治理的 runtime。
