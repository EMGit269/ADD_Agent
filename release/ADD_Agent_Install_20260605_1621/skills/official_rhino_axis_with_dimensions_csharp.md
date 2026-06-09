---
name: official-rhino-axis-with-dimensions-csharp
description: 官方 reference。用于一体化创建建筑轴网、轴号、引线、逐跨尺寸和总尺寸的 Grasshopper C# Script 画布；当用户说创建轴网、5*5轴网、轴网并标注、建筑轴线标注、轴号泡泡、尺寸标注，或要求复用 reference/00 axis with dimensions.gh 时使用。优先导入这个一体化 reference，而不是拆分重写 C#。
---

# Axis With Dimensions 一体化轴网标注

## Reference

- 画布文件：`reference/00 axis with dimensions.gh`
- 脚本用途：一次生成轴线、尺寸线、尺寸文字、轴号圆、轴号文字和引线。
- 适用场景：建筑轴网、结构轴线、轴线编号、逐跨尺寸、总尺寸、出图前的二维轴网标注。
- 推荐调用：先读取本 skill，再调用 `import_reference_gh(file_name="00 axis with dimensions.gh")` 导入参考画布。

这个 reference 是当前“轴网并标注”的首选入口。用户说“创建 5*5 轴网并标注”时，优先使用本 skill 和 `00 axis with dimensions.gh`，不要默认改写 C#，也不要优先使用旧的拆分 `axis.gh + dimension.gh` 流程。

## 输入端口

| 输入 | 含义 | 说明 |
|---|---|---|
| `xSpans` | X 方向跨距 | 文本或数字序列，支持逗号、分号、空格分隔，例如 `8000 8000 9000`。如果只输入一个值，会按 `xCount` 重复。 |
| `ySpans` | Y 方向跨距 | 同 `xSpans`。如果只输入一个值，会按 `yCount` 重复。 |
| `xLabels` | X 方向轴号 | 可选，例如 `1 2 3 4 5`。数量必须为 `xSpans.Count + 1`，否则自动生成 `1,2,3...`。 |
| `yLabels` | Y 方向轴号 | 可选，例如 `A B C D E`。数量必须为 `ySpans.Count + 1`，否则自动生成 `A,B,C...`。 |
| `extension` | 轴线延长 | 轴线超出网格边界的长度，默认 `0`。 |
| `dimOffset` | 尺寸偏移基础距离 | 控制尺寸线离轴网的距离，默认 `600`，最小夹紧到 `200`。 |
| `labelOffset` | 轴号偏移距离 | 控制轴号圆离轴网的距离，默认 `3600`，且不小于 `dimOffset + 200`。 |
| `dimTextHeight` | 尺寸文字高度 | 默认 `350`，最小夹紧到 `50`。 |
| `axisTextHeight` | 轴号文字高度 | 默认 `600`，最小夹紧到 `50`；轴号圆半径约为 `axisTextHeight * 1.2`。 |
| `xCount` | X 方向跨数 | 当 `xSpans` 只有一个数时，用它重复生成多少个 X 跨。 |
| `yCount` | Y 方向跨数 | 当 `ySpans` 只有一个数时，用它重复生成多少个 Y 跨。 |
| `drawingScale` | 出图比例 | 默认 `100`，夹紧到 `10-1000`；脚本用 `scale / 100` 调整文字高度和 `DimensionScale`。 |

## “5*5轴网”的默认解释

用户说“5*5轴网”但没有说明“5跨”还是“5根轴线”时，按建筑表达默认理解为 **5 根 X 方向轴线 + 5 根 Y 方向轴线**。

对本 reference 来说：

- 5 根轴线 = 4 跨。
- 若每跨 8000，应设置：
  - `xSpans = 8000`
  - `ySpans = 8000`
  - `xCount = 4`
  - `yCount = 4`
  - `xLabels` 可空，自动生成 `1..5`
  - `yLabels` 可空，自动生成 `A..E`

如果用户明确说“5跨*5跨”，则 `xCount = 5`、`yCount = 5`，会生成 6 根轴线。

## 输出端口

| 输出 | 内容 |
|---|---|
| `axisLinesOut` | 轴线 `Line` 列表，包含 `extension` 延长。 |
| `dimensionLinesOut` | 尺寸线、界线、tick 斜线等 `Line` 列表。 |
| `dimensionTextsOut` | 尺寸文字 `TextEntity` 列表。 |
| `axisLabelCirclesOut` | 轴号圆 `Circle` 列表。 |
| `axisLabelTextsOut` | 轴号文字 `TextEntity` 列表。 |
| `leaderLinesOut` | 轴号引线 `Line` 列表。 |
| `reportOut` | 解析日志、总宽高、对象数量、自动标签提示。 |

## 生成逻辑

- X 跨距生成 `xCoords`，控制竖向轴线的位置。
- Y 跨距生成 `yCoords`，控制横向轴线的位置。
- X 标签用于下/上两侧轴号圆，默认数字。
- Y 标签用于左/右两侧轴号圆，默认字母。
- 水平尺寸生成下侧和上侧逐跨尺寸 + 总尺寸。
- 垂直尺寸生成左侧和右侧逐跨尺寸 + 总尺寸。
- 尺寸输出是线和文字，不是 RhinoCommon `LinearDimension` 对象。

## 推荐工作流

1. 读取 `skills/reference_index.md`，确认任务属于轴网标注。
2. 读取本 skill。
3. 调用 `import_reference_gh(file_name="00 axis with dimensions.gh", group_name="Axis with dimensions")`。
4. 用 `get_gh_components` 定位导入的 C# 电池和输入 slider/panel。
5. 设置 `xSpans/ySpans/xCount/yCount`。对“5*5轴网”默认设为 4 跨。
6. 按需要设置 `extension/dimOffset/labelOffset/dimTextHeight/axisTextHeight/drawingScale`。
7. 检查 `reportOut`，确认总宽高、轴线数、尺寸数和轴号数。

## 常见参数模板

5 根轴线 x 5 根轴线，每跨 8000：

```text
xSpans = 8000
ySpans = 8000
xCount = 4
yCount = 4
xLabels = 空
yLabels = 空
extension = 3000
dimOffset = 600
labelOffset = 3600
dimTextHeight = 350
axisTextHeight = 600
drawingScale = 100
```

6 根 X 轴线、5 根 Y 轴线，每跨 8000：

```text
xSpans = 8000
ySpans = 8000
xCount = 5
yCount = 4
xLabels = 空
yLabels = 空
```

非均匀跨距：

```text
xSpans = 7200 8400 8400 7200
ySpans = 6000 6000 9000
xCount = 任意；xSpans 已给完整列表时不会使用 xCount 重复
yCount = 任意；ySpans 已给完整列表时不会使用 yCount 重复
```

## 注意事项

- `xCount/yCount` 是跨数，不是轴线根数；只有在 `xSpans/ySpans` 单值输入时用于重复跨距。
- `xLabels/yLabels` 数量必须比对应跨距数量多 1；不匹配时脚本会自动生成标签。
- `drawingScale` 会影响文字实际高度：脚本内部使用 `TextHeight = height / (drawingScale / 100)`，并设置 `DimensionScale = drawingScale / 100`。
- 输出是分开的几何和文字对象；如果要 Bake，需要分别处理线、圆、文字。
- 这个一体化 reference 已经包含尺寸标注，不需要再导入 `02 dimensions.gh`，除非用户明确要求拆分式工作流。

## 何时改 C# 脚本

只有出现以下需求时才改脚本：

- 需要斜交轴网或非正交轴网。
- 需要把尺寸输出改为 `LinearDimension` 而不是线和文字。
- 需要自定义字母规则、跳过 I/O、中文轴号或复杂编号。
- 需要轴号文字旋转、单侧轴号、单侧尺寸或取消总尺寸。
- 需要直接 Bake 到 Rhino 图层。

普通轴网标注任务优先调参数和复用 `00 axis with dimensions.gh`。
