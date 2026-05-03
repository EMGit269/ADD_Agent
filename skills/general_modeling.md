---
name: general-modeling
description: 负责规范电池布局、Slider 命名规范和原点管理。
---

# Grasshopper 建模专家技能 (Modeling Skill)

## 1. 坐标管理规范
- 核心逻辑建议从 (0,0,0) 开始构建
- 电池 X 轴间距 200-300，Y 轴间距 50-100
- 复杂逻辑可灵活调整，保持视觉清晰

## 2. 变量控制协议
- 关键参数必须用 Number Slider，禁止直接输入固定值
- Slider 参数可用 set_gh_component_value 设置值、范围(min/max)和小数精度(decimals)
- 建议：radius 用 0-100，length 用 0-500，根据实际情况调整

## 3. 几何逻辑优化
- 批量操作前检查数据树状态（Graft/Flatten）
- 用 set_gh_component_status 控制预览：中间过程建议隐藏预览
- 逻辑完成后检查是否有错误，check_gh_errors 可以帮你发现问题

## 4. 报错处理
- 优先检查：空引用输入、Slider 数值范围、端口索引
- 遇到错误尝试修复，最多尝试 2-3 次
- 无法修复时如实告知用户

---
提示：需要更多效率技巧？读取 workflow_optimization.md
