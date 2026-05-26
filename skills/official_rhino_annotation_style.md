---
name: official-rhino-annotation-style
description: 官方 reference。Rhino 8 / Grasshopper 标注样式参考画布，用于创建或整理出图标注相关的 Text / Dimension / Leader / Annotation Style 工作流。
---

# Rhino / Grasshopper 标注样式参考

## Reference

- 画布文件：`reference/标注样式.gh`
- 官方标注尺寸 JSON：`reference/official_annotation_dimension_csharp.json`
- 官方标注尺寸 C#：`skills/official_rhino_annotation_dimension_csharp.md`
- 适用环境：Rhino 8、Grasshopper
- 目标：辅助 agent 搭建或复用 Rhino 标注样式、文字样式、尺寸标注、引线标注相关的 GH/C# 工作流。

这份 reference 是标注样式相关画布。使用时先形成当前任务的标注方案，再把此画布作为结构参考，不要在没有明确需求时直接照搬。

## 适用任务

当用户需要以下能力时，优先考虑此 skill：

- 创建或修改 Rhino 标注样式。
- 统一文字高度、字体、箭头、尺寸线、引线样式。
- 为出图流程准备标准化的标注参数。
- 将标注样式应用到尺寸标注、文字对象或引线对象。
- 在 Grasshopper 中通过 C# Script 访问 Rhino 文档中的 AnnotationStyle / DimStyle。
- 配合 ClippingDrawings 或图层出图整理流程，统一剖面图、平面图、详图中的标注表达。

## 推荐工作流

1. 先确认用户要控制的是哪类标注：
   - 文字 Text
   - 尺寸 Dimension
   - 引线 Leader
   - 点位/编号/图名等自定义注释
   - Rhino 文档中的 Annotation Style / Dimension Style

2. 再确认要统一的样式参数：
   - 样式名称
   - 字体
   - 文字高度
   - 标注比例或模型空间比例
   - 箭头样式和箭头大小
   - 尺寸线、延长线、引线显示规则
   - 颜色、图层、打印线宽
   - 小数位、单位、前后缀

3. 如需复用 reference：
   - 查看 `reference/标注样式.gh` 的输入、输出、分组和 C# Script 结构。
   - 若任务是轴网、逐跨尺寸、总尺寸、尺寸界线、斜线标记或跨度文字，优先读取 `skills/official_rhino_annotation_dimension_csharp.md`。
   - 如需对照电池连接、端口和滑块默认值，再读取 `reference/official_annotation_dimension_csharp.json`。
   - 只复用与当前目标一致的部分。
   - 如果画布中已有 C# Script，优先保持其输入输出语义，不要随意改名。

4. 实现时优先使用 C# Script：
   - 需要访问 Rhino 文档真实样式、真实对象或图层时，用 C# Script。
   - 执行类输入建议接 Button，而不是长期保持 Boolean Toggle = True。
   - 修改 Rhino 文档后输出清晰日志到 `a`。

## 常见输入

根据任务选择，不要求每个脚本都包含全部输入：

- `styleName`：目标标注样式名称。
- `fontName`：字体名称，例如 Arial、Microsoft YaHei。
- `textHeight`：文字高度，使用 Rhino 当前模型单位。
- `scale`：标注比例或全局缩放。
- `arrowSize`：箭头大小。
- `decimalPlaces`：尺寸数值小数位。
- `layerInput`：目标图层对象或完整图层路径。
- `color`：标注对象或图层颜色。
- `apply`：执行按钮，True 时执行一次。

## 常见输出

- `a`：执行日志，包括创建、更新、跳过、错误原因。
- `style`：可选，返回找到或创建的样式名称。
- `ids`：可选，返回新建或修改的 Rhino 对象 id。

## C# 实现注意

- Rhino 8 中标注样式属于 Rhino 文档数据，脚本应通过 `RhinoDoc.ActiveDoc` 或当前文档访问。
- 修改文档样式前先查找是否已有同名样式；有则更新，没有则创建。
- 不要只改 GH 预览对象；需要出图时必须写入 Rhino 文档真实对象或真实样式。
- 批量修改对象时，先判断对象类型是否支持对应标注属性。
- 如果对象已有对象级覆盖设置，需要说明是否覆盖或保持 ByStyle / ByLayer。
- 对 Rhino 文档做写操作后，建议调用视图刷新。

## 与出图整理流程配合

如果用户同时在做剖面出图、图层整理或 ClippingDrawings：

- 先完成几何和图层整理。
- 再应用标注样式，避免新生成对象漏掉样式。
- 标注对象建议放入独立图层，例如 `标注`、`尺寸`、`文字`。
- 打印线宽由图层控制时，标注对象尽量保持 ByLayer。

## 常见问题

### 样式创建了但标注没有变化

检查对象是否真的引用了该 Annotation Style，或对象是否有局部覆盖。

### 字体不生效

检查目标电脑是否安装该字体。团队共享时优先使用所有机器都有的字体。

### 按钮点击后重复创建样式

脚本应先按名称查找已有样式，再决定更新或创建。执行输入建议接 Button。

### GH 预览正确但 Rhino 出图没有

说明结果可能只在 Grasshopper 预览中，没有写入 Rhino 文档。需要改为创建或修改 Rhino 文档真实对象。

## 命名建议

建议把相关脚本组件命名为：

- `Set Annotation Style`
- `Set Text Style`
- `Set Dimension Style`
- `Create Text Annotation`
- `Create Leader Annotation`
- `Apply Annotation Style`
