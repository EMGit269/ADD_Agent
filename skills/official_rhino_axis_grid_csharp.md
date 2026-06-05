---
name: official-rhino-axis-grid-csharp
description: 官方 reference。用于 Rhino 标注对象体系的建筑轴网生成；当任务明确需要 reference/axis.gh、斜交轴网、AxisAngleDeg、AxisDimensions、Rhino 原生 LinearDimension、轴网尺寸对象后续调整，或需要和 reference/dimension.gh 联动时使用。普通正交“轴网并标注/5*5轴网并标注”默认优先使用 official-rhino-axis-with-dimensions-csharp 和 reference/00 axis with dimensions.gh。
---

# 建筑轴线 / Axis Grid C# 电池

## Reference

- 画布文件：`reference/axis.gh`
- 主要用途：从轴线数量、跨距、轴线角度和标注参数生成建筑轴网。
- 适用环境：Rhino 8、Grasshopper、C# Script 电池。
- 推荐方式：优先复用 `reference/axis.gh` 中的现成电池；需要改造逻辑时再读取/编辑其中 C# Script。
- 配套尺寸调整：如果选择 `axis.gh` 这套 Rhino 标注对象体系，且用户要最终尺寸标注效果，不要只导入 `axis.gh`；还必须读取 `skills/official_rhino_axis_dimension_adjust_csharp.md`，导入 `reference/dimension.gh`，并把本电池 `AxisDimensions` 输出接到 dimension 电池的 `AxisDimensions` 输入。

如果宿主提供内部 reference 导入流程，可直接导入 `axis.gh` 到当前画布；导入后再用 `get_gh_components` 定位新增电池并接入当前模型。不要让 Agent 自行重写整段轴网 C#，除非用户明确要求定制算法。

## 功能

生成一组建筑轴线及其配套标注：

- `AxisLines`：轴线延长线。
- `AxisBubble`：轴号泡泡，数据树 `{0}=Curves`、`{1}=Texts`。
- `AxisDimensions`：轴线尺寸标注，数据树 `{0}=X方向标注`、`{1}=Y方向标注`。
- `AxisEndPoints`：轴线端点，数据树 `{0}=XStart`、`{1}=XEnd`、`{2}=YStart`、`{3}=YEnd`。
- `Log`：参数解析、跨距、输出数量和错误信息。

轴号规则：

- X 方向轴号为数字：`1, 2, 3...`
- Y 方向轴号为字母：`A, B, C...`
- 字母序列跳过常见易混淆字母：`I`、`O` 等不在内置 `ValidLetters` 中。

## 输入端口

| 输入 | 类型/含义 | 说明 |
|---|---|---|
| `XAxisCount` | int | X 方向轴线数量，必须 `>= 1`。示例中为 `6`。 |
| `YAxisCount` | int | Y 方向轴线数量，必须 `>= 1`。示例中为 `5`。 |
| `XAverageSpacing` | double | 当 `XSpacings` 为空时使用的 X 平均跨距。示例中为 `8000`。 |
| `YAverageSpacing` | double | 当 `YSpacings` 为空时使用的 Y 平均跨距。示例中为 `8000`。 |
| `XSpacings` | list/tree double | 可选逐跨 X 间距；数量必须为 `XAxisCount - 1`。有输入时覆盖平均跨距。 |
| `YSpacings` | list/tree double | 可选逐跨 Y 间距；数量必须为 `YAxisCount - 1`。有输入时覆盖平均跨距。 |
| `AxisAngleDeg` | double | Y 轴方向相对 X 轴的角度，默认 `90`；不能为 `0/180`。示例中为 `86`，用于斜交轴网。 |
| `BubbleDiameter` | double | 轴号圆直径，默认容错为 `3000`；文字高度约为直径 `0.6`。示例中为 `3085`。 |
| `AxisExtension` | double | 轴线相对网格交点范围向两端延长的长度。示例中为 `5000`。 |
| `LeaderLength` | double | 轴线端点到轴号圆前的引线长度。示例中为 `9000`。 |

单位跟随 Rhino/GH 当前模型单位。建筑毫米制中常用值：跨距 `6000-9000`，泡泡直径 `2500-3500`，轴线延长 `3000-6000`，引线 `6000-10000`。

## 输出解释

`AxisLines`：
- 输出 `Curve` 列表，已经包含 `AxisExtension` 延长。
- 可直接接 `Curve` 参数、Bake、图层分类或后续标注整理。

`AxisBubble`：
- `{0}` 是引线和圆圈曲线。
- `{1}` 是 `TextEntity` 轴号文字。
- 使用时不要把整个树当单一曲线列表；需要曲线和文字分别处理时按分支拆开。

`AxisDimensions`：
- `{0}` 是 X 方向逐跨和总尺寸。
- `{1}` 是 Y 方向逐跨和总尺寸。
- 脚本内部尺寸偏移固定：内层 `4500`，外层 `6500`；尺寸文字高度固定 `600`。

`AxisEndPoints`：
- `{0}` X 轴线负向端点。
- `{1}` X 轴线正向端点。
- `{2}` Y 轴线负向端点。
- `{3}` Y 轴线正向端点。
- 适合后续接引线、标注、轴线裁剪或定位辅助几何。

`Log`：
- 先看 `Log` 判断参数是否有效。
- 常见错误：`XSpacings/YSpacings` 数量不等于轴线数量减一、跨距小于等于 0、角度为 0 或 180。

## 使用流程

1. 如果用户只是要普通正交轴网或普通“轴网并标注”，优先使用 `reference/00 axis with dimensions.gh` 或 `01/02/03` 轻量拆分体系。
2. 如果用户明确需要 `axis.gh`、斜交轴网、`AxisAngleDeg`、`LinearDimension`、`AxisDimensions` 或尺寸对象二次调整，才导入/复用 `reference/axis.gh`。
3. 如果选择 `axis.gh` 且用户要尺寸最终效果，必须同时导入/复用 `reference/dimension.gh`，并按 `Axis.AxisDimensions -> Dimensions.AxisDimensions` 联动；最终使用 `Dimensions.AdjustedDimensions` 作为尺寸输出。
3. 设置 `XAxisCount`、`YAxisCount` 和平均跨距。
4. 若存在非均匀跨距，给 `XSpacings` / `YSpacings` 输入逐跨列表；列表长度必须为轴线数量减一。
5. 正交轴网用 `AxisAngleDeg=90`；斜交轴网输入实际角度，例如 `86`。
6. 根据图纸比例调整 `BubbleDiameter`、`AxisExtension`、`LeaderLength`。
7. 检查 `Log`，再把 `AxisLines`、`AxisBubble`、`AxisDimensions` 输出接入后续显示、Bake 或图层处理。

## 建模注意

- `XAxisCount/YAxisCount` 表示轴线根数，不是跨数；跨数为轴线数减一。
- `XAverageSpacing/YAverageSpacing` 只在逐跨列表为空时生效。
- `AxisAngleDeg` 控制 Y 方向轴线与 X 方向的夹角；小角度或接近 180 度会导致交点计算失效。
- 文字和尺寸对象是 RhinoCommon annotation，不是普通曲线；Bake 或图层处理时要区分 `Curve`、`TextEntity`、`LinearDimension`。
- 脚本已显式处理 `object` 输入，使用 `TryGetInt/TryGetDouble/TryGetDoubleList`，不要把端口强类型注入作为必要前提。

## 何时改 C# 脚本

只有在这些需求出现时才改脚本：

- 需要自定义轴号起始值，如 X 从 `101` 开始、Y 从 `K` 开始。
- 需要修改尺寸偏移、尺寸文字高度、泡泡文字比例。
- 需要单侧轴号、单侧尺寸或取消总尺寸。
- 需要把输出直接 bake 到指定图层。
- 需要改变字母编号规则。

改脚本时优先保留现有输入输出端口名称和数据树分支约定，避免破坏下游画布。
