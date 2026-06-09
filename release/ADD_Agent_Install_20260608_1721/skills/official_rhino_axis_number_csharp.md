---
name: official-rhino-axis-number-csharp
description: 官方 reference。用于创建建筑轴网轴号泡泡、轴号文字和引线的 Grasshopper C# Script 画布；当用户说添加轴号、轴号泡泡、轴线编号、轴网编号、只要轴号、轴号圆、轴号引线，或要求复用 reference/03 axis number.gh 时使用。若用户要一次性创建轴线、尺寸和轴号，应优先使用 official-rhino-axis-with-dimensions-csharp 和 reference/00 axis with dimensions.gh。
---

# Axis Number 轴号

## Reference

- 画布文件：`reference/03 axis number.gh`
- 脚本用途：根据 X/Y 跨距生成轴网四侧的轴号圆、轴号文字和引线。
- 适用场景：已有轴网参数，需要单独生成轴号；或与 `01 axis.gh`、`02 dimensions.gh` 分步组合。
- 推荐调用：先读取本 skill，再调用 `import_reference_gh(file_name="03 axis number.gh")` 导入参考画布。

这个 reference 只生成轴号相关对象，不生成轴线或尺寸标注。需要完整“轴网并标注”时使用 `00 axis with dimensions.gh`。

## 输入端口

| 输入 | 含义 | 说明 |
|---|---|---|
| `xSpans` | X 方向跨距 | 文本或数字序列，支持逗号、分号、空格分隔，例如 `8000 8000 9000`。如果只输入一个值，会按 `xCount` 重复。 |
| `ySpans` | Y 方向跨距 | 同 `xSpans`。如果只输入一个值，会按 `yCount` 重复。 |
| `xLabels` | X 方向轴号 | 可选。数量必须等于 `xSpans.Count + 1`，否则自动生成 `1,2,3...`。 |
| `yLabels` | Y 方向轴号 | 可选。数量必须等于 `ySpans.Count + 1`，否则自动生成 `A,B,C...`。 |
| `extension` | 轴线延长参考值 | 用于把轴号和引线位置向外避让，与轴线 reference 的 `extension` 保持一致。最小夹紧到 `0`。 |
| `labelOffset` | 轴号偏移距离 | 控制轴号圆心离轴网边界的距离，默认 `3600`，最小夹紧到 `500`。 |
| `axisTextHeight` | 轴号文字高度 | 默认 `600`，最小夹紧到 `50`。脚本会结合 `drawingScale` 计算实际 `TextHeight`。 |
| `xCount` | X 方向跨数 | 当 `xSpans` 只有一个数时，用它重复生成多少个 X 跨；脚本夹紧到 `1..20`。 |
| `yCount` | Y 方向跨数 | 当 `ySpans` 只有一个数时，用它重复生成多少个 Y 跨；脚本夹紧到 `1..20`。 |
| `drawingScale` | 出图比例 | 默认 `100`，夹紧到 `10..1000`。脚本使用 `scaleFactor = drawingScale / 100`。 |

## 输出端口

| 输出 | 内容 |
|---|---|
| `axisLabelCirclesOut` | 轴号圆 `Circle` 列表。 |
| `axisLabelTextsOut` | 轴号文字 `TextEntity` 列表。 |
| `leaderLinesOut` | 轴号引线 `Line` 列表。 |
| `reportOut` | 出图比例、单位、跨数、总宽高、轴号圆数量、轴号文字数量和引线数量。 |

## 与 01 Axis / 02 Dimensions 联动

`03 axis number.gh` 不接收 `01 axis.gh` 的 `axisLinesOut`，也不接收 `02 dimensions.gh` 的尺寸输出。它通过同样的跨距参数重算轴号位置，所以组合使用时必须让以下输入保持一致：

```text
xSpans
ySpans
xCount
yCount
extension
```

推荐分步场景：
1. 导入 `01 axis.gh` 生成轴线。
2. 如需尺寸，导入 `02 dimensions.gh`。
3. 导入 `03 axis number.gh` 生成轴号圆、文字和引线。
4. 三个 reference 的 `xSpans/ySpans/xCount/yCount/extension` 保持相同。
5. 如果用户要完整轴网、尺寸和轴号，优先改用 `00 axis with dimensions.gh`。

## 默认理解

用户说“5*5 轴网轴号”但没有说明“5 跨”还是“5 根轴线”时，默认按建筑表达理解为 **5 根 X 方向轴线 + 5 根 Y 方向轴线**。

对本 reference 来说：
- 5 根轴线 = 4 跨。
- 若每跨 8000，应设置 `xSpans = 8000`、`ySpans = 8000`、`xCount = 4`、`yCount = 4`。
- 默认会生成 X 轴号 `1..5` 和 Y 轴号 `A..E`。
- 如果用户明确说“5跨*5跨”，才设置 `xCount = 5`、`yCount = 5`，会生成 6 组 X/Y 轴号。

## 生成逻辑

- X 方向轴号：在轴网下侧和上侧各生成一组，默认数字编号。
- Y 方向轴号：在轴网左侧和右侧各生成一组，默认字母编号。
- 轴号圆半径：`axisTextHeight * 1.2`。
- 引线起点距离：`labelOffset * 0.35`，终点接近轴号圆边缘。
- 轴号文字平面：全部使用 `Plane.WorldXY`，当前脚本不会把 Y 方向轴号文字旋转 90 度。
- 输出是圆、线和文字对象，不是 Rhino 标注样式对象。

## 推荐工作流

1. 读取 `skills/reference_index.md`，确认任务是“单独轴号”或“分步轴网 + 轴号”。
2. 读取本 skill。
3. 调用 `import_reference_gh(file_name="03 axis number.gh", group_name="Axis Number")`。
4. 用 `get_gh_components` 定位导入的 C# 电池和输入参数。
5. 设置 `xSpans/ySpans/xCount/yCount/extension/labelOffset/axisTextHeight/drawingScale`。
6. 如用户指定轴号，设置 `xLabels/yLabels`；否则保持空值让脚本自动生成。
7. 检查 `reportOut`，确认轴号圆、文字和引线数量。

## 常见参数模板

5 根轴线 x 5 根轴线，每跨 8000：
```text
xSpans = 8000
ySpans = 8000
xCount = 4
yCount = 4
xLabels = 空，自动生成 1..5
yLabels = 空，自动生成 A..E
extension = 3000
labelOffset = 3600
axisTextHeight = 600
drawingScale = 100
```

自定义轴号：
```text
xSpans = 8000 8000 9000
ySpans = 6000 6000
xLabels = 1 2 3 4
yLabels = A B C
extension = 3000
labelOffset = 3600
axisTextHeight = 600
drawingScale = 100
```

## 注意事项

- `xCount/yCount` 是跨数，不是轴线根数；只在 `xSpans/ySpans` 单值输入时用于重复跨距。
- `xLabels/yLabels` 数量必须比对应跨距数量多 1；不匹配时脚本会自动改用默认编号，并在 `reportOut` 提示。
- `drawingScale` 会影响文字实际高度：脚本内部设置 `TextHeight = axisTextHeight / (drawingScale / 100)`，并设置 `DimensionScale = drawingScale / 100`。
- 轴号圆半径使用原始 `axisTextHeight * 1.2`，不会除以 `drawingScale / 100`。
- 如果用户要求轴线、轴号和尺寸一次完成，不要拆分调用 `01 axis.gh`、`02 dimensions.gh` 和 `03 axis number.gh`，优先导入 `00 axis with dimensions.gh`。
