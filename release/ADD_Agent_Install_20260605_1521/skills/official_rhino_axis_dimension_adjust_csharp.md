---
name: official-rhino-axis-dimension-adjust-csharp
description: 官方 reference。用于 Rhino LinearDimension 轴网尺寸标注的二次调整；当任务明确涉及 reference/dimension.gh、reference/axis.gh 生成的 AxisDimensions 输出、AdjustedDimensions、尺寸文字高度、模型空间比例、尺寸线与对象距离、内外尺寸线间距，或需要调整 Rhino 原生尺寸对象时使用。普通正交“轴网并标注/5*5轴网并标注”默认优先使用 official-rhino-axis-with-dimensions-csharp 和 reference/00 axis with dimensions.gh。
---

# 建筑轴线尺寸调整 / Axis Dimension Adjust C# 电池

## Reference

- 画布文件：`reference/dimension.gh`
- 联动上游：`reference/axis.gh` 中 Axis 电池的 `AxisDimensions` 输出。
- 主要用途：接收已有 `LinearDimension` 轴线尺寸，重建为新的尺寸对象，并统一调整文字高度、模型空间比例、尺寸线距离和内外尺寸线间距。
- 推荐方式：与 `skills/official_rhino_axis_grid_csharp.md` 配合使用；先生成轴线和原始尺寸，再用本电池做尺寸表现调整。

触发关键词必须指向 `axis.gh + dimension.gh` 体系：

- “复用 dimension.gh”
- “AxisDimensions / AdjustedDimensions”
- “LinearDimension 尺寸调整”
- “调整轴线尺寸文字 / 尺寸线距离 / 尺寸比例”
- “调整内外尺寸线间距”

普通“轴网并标注”默认不要触发本 skill；只有已经选择 `axis.gh` 体系或用户明确要求 Rhino 原生尺寸对象调整时才使用。使用时不能只导入 `dimension.gh`；必须同时使用 `axis.gh`，并把 Axis 电池的 `AxisDimensions` 输出接到 Dimensions 电池的 `AxisDimensions` 输入。

## 与 Axis 电池的连接关系

正式接法：

```text
Axis.AxisDimensions -> Dimensions.AxisDimensions
Dimensions.AdjustedDimensions -> 后续显示 / Bake / 图层处理
```

注意：

- `Dimensions.AxisDimensions` 输入端口需要的是 `LinearDimension` 列表或数据树。
- 它可以直接接上一个 Axis 电池的 `AxisDimensions` 输出。
- 不要用普通 Number Slider 作为正式的 `AxisDimensions` 输入；滑块只能用于其他数值参数。
- 如果输入不是尺寸对象，`Log` 会显示 `Source dimensions: 0`，输出为空。

## 功能

这个电池不是从零生成尺寸，而是“重建/调整已有尺寸”：

- 收集输入中的所有 `LinearDimension`。
- 读取每个尺寸的两个标注点和尺寸线位置。
- 识别逐跨尺寸和总尺寸：跨度最长的尺寸会被视为外层总尺寸。
- 按参数重建尺寸对象，保留源尺寸的部分显示属性。
- 将箭头类型设置为建筑常用 tick。
- 输出调整后的 `LinearDimension` 列表。

## 输入端口

| 输入 | 类型/含义 | 说明 |
|---|---|---|
| `AxisDimensions` | `LinearDimension` list/tree | 必填。接 `axis.gh` 的 `AxisDimensions` 输出；支持列表、数据树、GH wrapper 和 RhinoObject 中的尺寸几何。 |
| `DimensionTextHeight` | double | 可选。尺寸文字高度；大于 0 时覆盖源尺寸文字高度。示例约 `270.66`。 |
| `DimensionModelSpaceScale` | double | 可选。尺寸对象 `DimensionScale`；大于 0 时覆盖源尺寸比例。示例可用 `1.0` 或按图纸比例设置。 |
| `DimensionObjectDistance` | double | 可选。尺寸线到被标注对象/轴线端点中点的基础距离；`>= 0` 时生效。示例约 `2238.7`。 |
| `DimensionLineGap` | double | 可选。总尺寸相对逐跨尺寸的额外间距；`>= 0` 时生效。示例约 `2603.1`。 |

## 输出端口

`AdjustedDimensions`：

- 调整后的 `LinearDimension` 列表。
- 用于替代上游 Axis 电池的原始 `AxisDimensions` 输出。
- 可直接接 Geometry/Annotation 参数、Bake、图层处理或后续出图整理。

`Log`：

- `Source dimensions`：成功收集到的源尺寸数量。
- `Adjusted dimensions`：成功重建的尺寸数量。
- 后续列出实际生效的文字高度、模型空间比例、对象距离和线间距。

## 参数逻辑

`DimensionTextHeight`：

- 大于 0 时统一覆盖所有尺寸文字高度。
- 为空、无效或 `<= 0` 时沿用源尺寸的 `TextHeight`。

`DimensionModelSpaceScale`：

- 大于 0 时统一覆盖 `DimensionScale`。
- 如果 Rhino 中测量文字/尺寸大小异常，优先检查这个值是否被错误放大。
- 常规模型空间直接标注通常用 `1.0`；需要按图纸比例时再设置比例值。

`DimensionObjectDistance`：

- 控制逐跨尺寸线到轴线端点中点的距离。
- 设置后，所有非外层总尺寸会统一到这个距离。
- 不设置时保留源尺寸各自距离。

`DimensionLineGap`：

- 控制外层总尺寸相对内层逐跨尺寸的额外偏移。
- 脚本以“跨度最长”的尺寸识别外层总尺寸。
- 如果所有尺寸跨度相同，脚本不会强行识别外层，避免误偏移。

## 推荐工作流

1. 导入/复用 `reference/axis.gh`，生成 `AxisLines`、`AxisBubble`、`AxisDimensions`。
2. 导入/复用 `reference/dimension.gh`。
3. 将 Axis 电池的 `AxisDimensions` 输出连接到 Dimensions 电池的 `AxisDimensions` 输入。
4. 设置：
   - `DimensionTextHeight`
   - `DimensionModelSpaceScale`
   - `DimensionObjectDistance`
   - `DimensionLineGap`
5. 用 `AdjustedDimensions` 作为最终尺寸输出；不要再同时显示原始 `AxisDimensions`，避免重叠。
6. 检查 `Log`，确认源尺寸数量和调整后数量一致。

## 常见问题

`Source dimensions: 0`：

- `AxisDimensions` 没有接上游 Axis 电池输出。
- 输入被接成了 Number Slider、Panel 文本或普通曲线。
- 上游 Axis 电池参数错误，未生成尺寸。

尺寸文字过大或过小：

- 检查 `DimensionTextHeight`。
- 检查 `DimensionModelSpaceScale`，不要把图纸比例误当文字高度。
- 如果 Rhino 中测量文字高度异常，优先把 `DimensionModelSpaceScale` 改为 `1.0` 验证。

总尺寸偏移不对：

- 检查 `DimensionLineGap`。
- 脚本按最大跨度识别总尺寸；如果输入只包含逐跨尺寸或所有尺寸跨度相同，不会产生外层偏移。

尺寸和原始尺寸重叠：

- 下游只使用 `AdjustedDimensions`。
- 隐藏或断开上游原始 `AxisDimensions` 的显示输出。

## 何时改 C# 脚本

只有在这些需求出现时才改脚本：

- 需要保留 `AxisDimensions` 原有数据树分支，而不是输出扁平列表。
- 需要分别设置 X/Y 方向尺寸文字高度或距离。
- 需要用显式分支识别总尺寸，而不是按最大跨度识别。
- 需要额外控制箭头、文字位置、TextGap、尺寸样式或图层 Bake。

改脚本时优先保留端口名称，尤其是 `AxisDimensions` 输入和 `AdjustedDimensions` 输出，保证它能继续与 `axis.gh` 联动。
