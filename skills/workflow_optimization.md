---
name: workflow-optimization
description: 专注于提高批量构建效率、优化画布清理逻辑和提升响应速度。
---

# 效率与优化技能 (Workflow Optimization)

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
