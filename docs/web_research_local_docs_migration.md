# web_research 本地文档检索迁移记录

日期：2026-06-15

## 背景

用户明确要求 `web_research` 不再承担公网联网搜索能力，简化为本地搜索。目标是降低调用不确定性、等待时间和 token 成本，同时避免 API 文档查证被 Bing/网页索引质量影响。

## 本轮修改

- `ADDGH/ChatWindow.WebResearch.cs`
  - `mode=search` 改为只搜索本机镜像文档目录。
  - `mode=api_pipeline` 的 fallback 阶段改为 `fallback_local_documentation_search`，provider 改为 `local_documentation`。
  - `mode=fetch` 只允许读取本地镜像可解析的官方文档 URL 或本地文件路径。
  - 移除 Bing 搜索结果解析函数和未使用的 HTTP using。
- `ADDGH/ChatWindow.ToolDefinitions.cs`
  - `web_research` schema 描述改为 local mirrored documentation lookup，明确不访问公网。
- `ADDGH/Agent/ToolRegistry.cs`
  - `web_research` 注册描述改为 `Fetch/search local mirrored documentation`。
- `ADDGH/ChatWindow.cs`
  - 系统提示第 4b 段从“联网查询”改为“本地文档查证”。
- `ADDGH/Agent/ContextPackBuilder.cs`
  - API doc / web research context pack 明确该工具只读本地镜像文档。
- `ADDGH/Agent/WorkflowRouter.cs`、`ADDGH/Agent/WorkflowSignalExtractor.cs`
  - WebResearch route 的解释与触发词收窄为本地文档或镜像 URL 查证，避免把“最新/联网”误路由到该工具。

## 后续建议

如果以后确实需要公网能力，建议新增独立工具名，例如 `external_web_research`，不要重新扩张 `web_research`。API 文档查证、本地镜像搜索、外部网页搜索应保持三套不同风险和成本模型。
