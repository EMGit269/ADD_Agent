---
name: workflow-optimization
description: 专注于提高批量构建效率、优化画布清理逻辑和提升响应速度。
---

# 效率与优化技能 (Workflow Optimization)

与**建模三阶段**对齐：本节技巧属于**阶段三**的效率手段——在**阶段二**已把组件与连线设计定稿后，再一次性落地画布（例如批量 **create_component_graph**）、复用已有电池，避免边想边改。

## 子任务与分块落地
- 复杂方案在**阶段二**拆成**有序子任务**（每步要哪些电池、连哪些端口），不必一次生成整张图。
- **阶段三**严格按子任务顺序执行：**同一子任务内**仍用单次 **create_component_graph** 打完该块的放置与连线；**子任务之间**可多轮调用；上一轮稳定再扩展下一轮。
- 需要跨已存在实例 id 布线时，再穿插 `connect_gh_components` 或更小范围的补图。

## 1. 批量构建原则
- 创建3个以上电池时，优先用 create_component_graph 一次性完成
- 不要循环创建 alias_id 可以引用，连线一起建
- 示例参考 create_component_graph 可以带 value 初始化 Slider/Panel

## 2. 画布清理与重用
- 操作前用 get_gh_components 检查已有电池
- 已有正确电池直接用，不要删再建
- 逻辑重构才用 remove_gh_component，局部改优先重连

## 3. 效率提示
- 操作前简单说明要做什么
- 完成后确认结果正确

---
提示：需要建模细节见 general_modeling.md
