---
name: graph-mapper-hints
description: Graph Mapper 电池如何通过 set_gh_component_value 切换图/曲线类型（graph_mapper_type），及排错要点。
---

# Graph Mapper（图映射）

## 用途

内置 **Graph Mapper** 的参数（右键菜单里的**图形状 / 曲线类型**）通常**不是**可写字符串脚本，不要使用普通「写 Code」思路。请在**阶段三**对画布实例调用 **`set_gh_component_value`**，并传入：

- **`graph_mapper_type`**（推荐）：与子菜单条目或内置图模板**名称片段**匹配的英文关键词，例如：`Bezier`、`Linear`、`Parabola`、`Sine`、`Gaussian`、`Square`、`Power`…（以当前 Rhino/Grasshopper 版本加载的 **GraphProxies** 列表为准）。
- 若未传 `graph_mapper_type`，可将**同一关键词**放在 **`value`** 中（仅当本次调用不是 Slider/Panel/脚本写入时）。
- 新建 **Graph Mapper** 时未指定类型会默认使用 `Bezier`；需要其它类型时在 `add_gh_component` / `create_component_graph` / `set_gh_component_value` 中传 `graph_mapper_type`。

## 工作流程

1. `add_gh_component` 或 `create_component_graph` 放置 **Graph Mapper**（标准名常为 `Graph Mapper`）。
2. 从上一轮工具结果取得实例 **`id`**，调用 `set_gh_component_value`，传入 `graph_mapper_type`（如 `Bezier`）。
3. 失败时读取返回中的 **节选可用名称**；仍不对则右键组件对照菜单字面，换用更短或更长关键词做子串匹配。
4. 若宿主报告无法写入内部图属性（API 变动），请在 Grasshopper 内手动切换图类型后再继续自动化连线。

## 与脚本电池的区别

- **Graph Mapper**：只改 **`graph_mapper_type` / value 关键词**，不要向内填公式文本。
- **Evaluate / Expression / Python**：仍用 **`value`** + 可选 **`property`**，见 gh_script_catalog.md。
