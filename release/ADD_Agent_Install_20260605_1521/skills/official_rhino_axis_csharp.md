---
name: official-rhino-axis-csharp
description: 官方 reference。用于只创建建筑轴网轴线的 Grasshopper C# Script 画布；当用户说创建轴网、生成轴线、建筑轴线、结构轴网、只有轴线、不需要尺寸标注，或要求复用 reference/01 axis.gh 时使用。若用户要求轴号泡泡、引线、逐跨尺寸或总尺寸，应优先使用 official-rhino-axis-with-dimensions-csharp 和 reference/00 axis with dimensions.gh。
---

# Axis 轴线

## Reference

- 画布文件：`reference/01 axis.gh`
- 脚本用途：根据 X/Y 跨距生成二维正交建筑轴网轴线，并输出解析报告。
- 适用场景：只需要轴线几何、后续还要单独接尺寸或轴号工具、或用户明确说“不需要标注/只要轴线”。
- 推荐调用：先读取本 skill，再调用 `import_reference_gh(file_name="01 axis.gh")` 导入参考画布。

这个 reference 是轻量轴线生成入口。它不生成轴号圆、轴号文字、引线、逐跨尺寸或总尺寸；需要完整“轴网并标注”时使用 `00 axis with dimensions.gh`。

## 输入端口

| 输入 | 含义 | 说明 |
|---|---|---|
| `xSpans` | X 方向跨距 | 文本或数字序列，支持逗号、分号、空格分隔，例如 `8000 8000 9000`。如果只输入一个值，会按 `xCount` 重复。 |
| `ySpans` | Y 方向跨距 | 同 `xSpans`。如果只输入一个值，会按 `yCount` 重复。 |
| `xLabels` | X 方向轴号 | 目前只用于检查数量是否等于 X 轴线数量，不会输出轴号几何或文字。 |
| `yLabels` | Y 方向轴号 | 目前只用于检查数量是否等于 Y 轴线数量，不会输出轴号几何或文字。 |
| `extension` | 轴线延长 | 轴线超出网格边界的长度，最小夹紧到 `0`。 |
| `xCount` | X 方向跨数 | 当 `xSpans` 只有一个数时，用它重复生成多少个 X 跨；脚本夹紧到 `1..20`。 |
| `yCount` | Y 方向跨数 | 当 `ySpans` 只有一个数时，用它重复生成多少个 Y 跨；脚本夹紧到 `1..20`。 |

## 输出端口

| 输出 | 内容 |
|---|---|
| `reportOut` | 输入解析日志、跨数、总宽高、轴线数量，以及 label 数量不匹配提示。 |
| `axisLinesOut` | 轴线 `Line` 列表，包含 `extension` 延长。 |

## 默认理解

用户说“5*5 轴网”但没有说明“5 跨”还是“5 根轴线”时，默认按建筑表达理解为 **5 根 X 方向轴线 + 5 根 Y 方向轴线**。

对本 reference 来说：
- 5 根轴线 = 4 跨。
- 若每跨 8000，应设置 `xSpans = 8000`、`ySpans = 8000`、`xCount = 4`、`yCount = 4`。
- 如果用户明确说“5跨*5跨”，才设置 `xCount = 5`、`yCount = 5`，会生成 6 根 X 方向轴线和 6 根 Y 方向轴线。

## 推荐工作流

1. 读取 `skills/reference_index.md`，确认任务是“只生成轴线”而不是“轴网并标注”。
2. 读取本 skill。
3. 调用 `import_reference_gh(file_name="01 axis.gh", group_name="Axis")`。
4. 用 `get_gh_components` 定位导入的 C# 电池和输入参数。
5. 设置 `xSpans/ySpans/xCount/yCount/extension`。
6. 检查 `reportOut`，确认总宽高、跨数和轴线数量。

## 常见参数模板

5 根轴线 x 5 根轴线，每跨 8000：
```text
xSpans = 8000
ySpans = 8000
xCount = 4
yCount = 4
extension = 3000
xLabels = 空
yLabels = 空
```

非均匀跨距：
```text
xSpans = 7200 8400 8400 7200
ySpans = 6000 6000 9000
xCount = 任意，xSpans 已给完整列表时不会使用 xCount 重复
yCount = 任意，ySpans 已给完整列表时不会使用 yCount 重复
extension = 3000
```

## 注意事项

- `xCount/yCount` 是跨数，不是轴线根数；只在 `xSpans/ySpans` 单值输入时用于重复跨距。
- `xLabels/yLabels` 不会生成轴号标注，只会在数量不等于轴线数量时写入 `reportOut` 提示。
- 如果用户要轴号圆、轴号文字、引线或尺寸标注，不要使用这个 reference 作为最终方案；改用 `00 axis with dimensions.gh`，或按用户要求再导入单独的轴号/尺寸 reference。
- 输出只有轴线几何和 report；如需 Bake，需要后续连接 Bake 工具或单独处理 `axisLinesOut`。
