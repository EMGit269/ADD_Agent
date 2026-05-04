---
name: Grasshopper 脚本类电池
description: 如何用 ADDGH 工具按名称添加脚本/表达式电池（必要时再用 GUID），并写入代码；以当前 Rhino 中的组件库为准。
---

## 何时才需要（非必要不要用）

- **简单任务**（常规几何、连线、Slider/Panel、标准电池能表达的运算）：**不要**调用 `search_gh_component_catalog`，**不要**为「保险」先搜 C#/Script/Python；用 `search_component_library` + `add_gh_component` / `create_component_graph` 等即可。
- **仅当**用户明确要求脚本/公式，或方案**必须用** Evaluate、Expression、C#/Python/VB 等电池实现时，再按下面流程使用 catalog 与写入 `set_gh_component_value`。

## 推荐流程

1. 调用 **`search_gh_component_catalog`**：`query` 用关键词即可（如 `C#`、`Python`、`Script`、`Evaluate`）；需要缩小范围时可加 `category_contains`。不必把 GUID 当作 query 去搜。
2. 在返回的 JSON `items` 里确认目标项的 **`name`**；仅当存在同名插件或脚本类需精确定位时，再记下该项的 **`guid`**（组件**类型** ID，不是画布实例 ID）。
3. 调用 **`add_gh_component`**：默认只传 **`name`**；确有歧义时再传 **`component_guid`**。
4. 需要写入代码/公式时，在放置后对该**实例**调用 **`set_gh_component_value`**，提供画布上的 **`id`**（`get_gh_components` 中的组件 `id`）与 **`value`**（完整脚本或公式文本，勿省略）。宿主会按可写 `string` 属性名启发式匹配（如 `Code`、`Script`、`Formula`、`Expression`、`EditorText` 等，含 internal setter）。若失败，工具错误里会列出该电池上可写的 `string` 属性名便于对照。

## 名称对照（仅供参考，以 catalog 查询为准）

本工程引用 **Grasshopper 7 SDK**。菜单与 `Desc.Name` 常一致，但随 Rhino 大版本可能增减或改名（例如 Rhino 8 的 **Python 3 Script**）。务必用 **`search_gh_component_catalog`** 在你当前环境中确认。

| 菜单常见名称 | 检索关键词建议 |
|-------------|----------------|
| Evaluate    | `Evaluate`     |
| Expression  | `Expression`   |
| Script（旧） | `Script`       |
| C# Script   | `C#` 或 `C# Script` |
| IronPython 2 Script | `IronPython` 或 `Python` |
| VB Script   | `VB`           |

## 与旧工具的关系

- **`search_component_library`**：仍返回简短文本列表（约 15 条），适合快速扫一眼。
- **`search_gh_component_catalog`**：返回 **JSON**（含 `guid`），便于核对名称；多数情况用 `name` 添加即可。
