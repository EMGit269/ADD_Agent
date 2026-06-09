---
name: official-rhino-dimensions-csharp
description: 官方 reference。用于创建建筑轴网的逐跨尺寸和总尺寸标注 Grasshopper C# Script 画布；当用户说给轴网加尺寸、只要尺寸标注、逐跨尺寸、总尺寸、轴线尺寸、尺寸线和尺寸文字，或要求复用 reference/02 dimensions.gh 时使用。若用户要一次性创建轴线、轴号和尺寸，应优先使用 official-rhino-axis-with-dimensions-csharp 和 reference/00 axis with dimensions.gh。
---

# Dimensions 尺寸标注

## Reference

- 画布文件：`reference/02 dimensions.gh`
- 脚本用途：根据 X/Y 跨距生成轴网四侧的逐跨尺寸、总尺寸、尺寸界线、tick 斜线和尺寸文字。
- 适用场景：已有轴网参数，需要单独生成尺寸标注；或与 `01 axis.gh` 分步组合。
- 推荐调用：先读取本 skill，再调用 `import_reference_gh(file_name="02 dimensions.gh")` 导入参考画布。

这个 reference 只生成尺寸相关对象，不生成轴线、轴号圆、轴号文字或轴号引线。需要完整“轴网并标注”时使用 `00 axis with dimensions.gh`。

## 输入端口

| 输入 | 含义 | 说明 |
|---|---|---|
| `xSpans` | X 方向跨距 | 文本或数字序列，支持逗号、分号、空格分隔，例如 `8000 8000 9000`。如果只输入一个值，会按 `xCount` 重复。 |
| `ySpans` | Y 方向跨距 | 同 `xSpans`。如果只输入一个值，会按 `yCount` 重复。 |
| `extension` | 轴线延长参考值 | 用于把尺寸位置向外避让，与轴线 reference 的 `extension` 保持一致。最小夹紧到 `0`。 |
| `dimOffset` | 尺寸偏移基础距离 | 控制尺寸线离轴网边界的距离，默认 `600`，最小夹紧到 `200`。 |
| `dimTextHeight` | 尺寸文字高度 | 默认 `350`，最小夹紧到 `50`。脚本会结合 `drawingScale` 计算实际 `TextHeight`。 |
| `xCount` | X 方向跨数 | 当 `xSpans` 只有一个数时，用它重复生成多少个 X 跨；脚本夹紧到 `1..20`。 |
| `yCount` | Y 方向跨数 | 当 `ySpans` 只有一个数时，用它重复生成多少个 Y 跨；脚本夹紧到 `1..20`。 |
| `drawingScale` | 出图比例 | 默认 `100`，夹紧到 `10..1000`。脚本使用 `scaleFactor = drawingScale / 100`。 |

## 输出端口

| 输出 | 内容 |
|---|---|
| `dimensionLinesOut` | 尺寸线、尺寸界线、tick 斜线等 `Line` 列表。 |
| `dimensionTextsOut` | 尺寸文字 `TextEntity` 列表。 |
| `reportOut` | 出图比例、单位、跨数、总宽高、尺寸线数量和尺寸文字数量。 |

## 与 01 Axis 联动

`02 dimensions.gh` 不接收 `01 axis.gh` 的 `axisLinesOut`。它通过同样的跨距参数重算尺寸位置，所以和 `01 axis.gh` 组合时必须让以下输入保持一致：

```text
xSpans
ySpans
xCount
yCount
extension
```

推荐分步场景：
1. 导入 `01 axis.gh` 生成轴线。
2. 导入 `02 dimensions.gh` 生成尺寸。
3. 将两组画布的 `xSpans/ySpans/xCount/yCount/extension` 设置为相同值。
4. 需要轴号时再导入轴号 reference，或直接改用 `00 axis with dimensions.gh`。

## 默认理解

用户说“5*5 轴网尺寸”但没有说明“5 跨”还是“5 根轴线”时，默认按建筑表达理解为 **5 根 X 方向轴线 + 5 根 Y 方向轴线**。

对本 reference 来说：
- 5 根轴线 = 4 跨。
- 若每跨 8000，应设置 `xSpans = 8000`、`ySpans = 8000`、`xCount = 4`、`yCount = 4`。
- 如果用户明确说“5跨*5跨”，才设置 `xCount = 5`、`yCount = 5`，会标注 5 个逐跨尺寸和 1 个总尺寸。

## 生成逻辑

- 水平尺寸：在轴网上侧和下侧各生成一组逐跨尺寸，并各生成一条总尺寸。
- 垂直尺寸：在轴网左侧和右侧各生成一组逐跨尺寸，并各生成一条总尺寸。
- 尺寸文字：水平文字使用 `Plane.WorldXY`；垂直文字使用 `new Plane(origin, Vector3d.YAxis, Vector3d.XAxis)`，因此会按竖向尺寸方向旋转。
- 尺寸值：整数显示为整数；非整数保留最多 3 位小数。
- 输出是线和文字对象，不是 RhinoCommon `LinearDimension` 对象。

## 推荐工作流

1. 读取 `skills/reference_index.md`，确认任务是“单独尺寸标注”或“分步轴网 + 尺寸”。
2. 读取本 skill。
3. 调用 `import_reference_gh(file_name="02 dimensions.gh", group_name="Dimensions")`。
4. 用 `get_gh_components` 定位导入的 C# 电池和输入参数。
5. 设置 `xSpans/ySpans/xCount/yCount/extension/dimOffset/dimTextHeight/drawingScale`。
6. 检查 `reportOut`，确认总宽高、尺寸线数量和尺寸文字数量。

## 常见参数模板

5 根轴线 x 5 根轴线，每跨 8000：
```text
xSpans = 8000
ySpans = 8000
xCount = 4
yCount = 4
extension = 3000
dimOffset = 600
dimTextHeight = 350
drawingScale = 100
```

非均匀跨距：
```text
xSpans = 7200 8400 8400 7200
ySpans = 6000 6000 9000
xCount = 任意，xSpans 已给完整列表时不会使用 xCount 重复
yCount = 任意，ySpans 已给完整列表时不会使用 yCount 重复
extension = 3000
dimOffset = 600
dimTextHeight = 350
drawingScale = 100
```

## 注意事项

- `xCount/yCount` 是跨数，不是轴线根数；只在 `xSpans/ySpans` 单值输入时用于重复跨距。
- `drawingScale` 会影响文字实际高度：脚本内部设置 `TextHeight = dimTextHeight / (drawingScale / 100)`，并设置 `DimensionScale = drawingScale / 100`。
- `dimOffset` 控制第一道逐跨尺寸的基础偏移；总尺寸会在逐跨尺寸外侧继续偏移。
- 如果用户要求轴线、轴号和尺寸一次完成，不要拆分调用 `01 axis.gh` 和 `02 dimensions.gh`，优先导入 `00 axis with dimensions.gh`。
- 如果用户要求真正的 Rhino 标注对象，需要修改 C# 输出 `LinearDimension`；本 reference 当前输出的是 `Line` + `TextEntity`。
