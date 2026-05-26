---
name: official-rhino-clipping-drawings-csharp
description: 官方 reference。Rhino 8 / Grasshopper C# Script 电池组，用于 ClippingDrawings 出图整理、图层剖切样式、打印线宽和玻璃对象归类。
---

# Rhino / Grasshopper C# 出图整理电池组

## Reference
- 画布文件：`reference/RHINO PLP.gh`
- 适用环境：Rhino 8、Grasshopper、Rhino 文档中的真实对象或图层
- 触发方式：所有执行类输入建议接 `Button`，实现点一次执行一次

这组 Grasshopper C# Script 电池用于配合 Rhino / ClippingDrawings / 图层管理做出图整理，主要包含：
- 修改图层剖切样式
- 修改图层打印线宽
- 按颜色筛选物件并归类到同级 `玻璃` 图层
- 运行 ClippingDrawings 后自动整理新生成图层和物件

## 1. Set Layer Section Style

功能：给 Rhino 图层设置 Section Style，用于 ClippingPlane 剖切显示。

输入：
- `layerPath`：图层路径字符串，建议完整路径，例如 `A-Wall::Cut`
- `hatchName`：Rhino 当前文件中已存在的 hatch pattern 名称，例如 `Solid`
- `hatchColor`：剖切填充颜色
- `boundaryColor`：剖切边界颜色
- `hatchScale`：hatch 比例
- `boundaryVisible`：是否显示边界
- `clearStyle`：是否清除当前图层剖切样式
- `apply`：布尔开关，True 时执行，建议接 Button

输出：
- `a`：执行结果文本

注意：
- 当前显示模式必须开启 `Use section styles`
- 没有 ClippingPlane 时，普通视图里通常看不到效果
- `hatchName` 必须是 Rhino 当前文件里已存在的 hatch pattern 名称

## 2. Set Layer Print Width

功能：修改 Rhino 图层的 Print Width / PlotWeight。

输入：
- `layerInput`：图层对象或图层名字符串
- `printWidth`：打印线宽，单位 mm
- `apply`：按钮或布尔值，触发执行

输出：
- `a`：执行结果文本

常用线宽：
- `0.00`
- `0.13`
- `0.18`
- `0.25`
- `0.35`

注意：
- 只修改图层线宽，不改对象单独设置
- 如果对象自身不是 ByLayer，打印结果可能仍不一致

## 3. Move Objects By Glass Color

功能：从指定源图层中筛选出显示颜色与玻璃颜色一致的物件，并移动到同级 `玻璃` 图层。

输入：
- `sourceLayerInput`：要筛选的源图层
- `glassLayerInput`：颜色来源图层，脚本读取这个图层颜色作为玻璃颜色
- `apply`：按钮，点一次执行一次

输出：
- `a`：执行结果文本

逻辑：
- 从 `sourceLayerInput` 中遍历所有物件
- 读取每个物件在 Rhino 中的最终显示颜色
- 与 `glassLayerInput` 的图层颜色比较
- 匹配则移动到同级 `玻璃` 图层
- 如果没有同级 `玻璃` 图层，则自动创建

层级规则：
- 源图层 `A::B::剖面图Curve`
- 目标图层 `A::B::玻璃`
- 不会创建到最外层

注意：
- 比较的是最终显示颜色，不是只看对象自身颜色字段
- 当前版本是移动，不是复制
- 同级已存在 `玻璃` 图层时会直接复用

## 4. Run ClippingDrawings + Cleanup

功能：完整执行 ClippingDrawings 并整理结果图层和物件。

流程：
- 输入 Rhino 中已有的 ClippingPlane
- 执行一次 ClippingDrawings
- 输出剖切面的原点坐标
- 自动识别新生成结果中的 Curve / 曲线 图层
- 调整新生成图层打印线宽
- 隐藏 Solid / 实体 图层
- 把曲线图层中颜色匹配玻璃颜色的物件移到同级 `玻璃` 图层

输入：
- `run`：接 Button
- `clippingPlaneInput`：Rhino 中现有的 ClippingPlane 引用
- `glassLayerInput`：玻璃颜色来源图层
- `allWidth`：新生成普通图层打印线宽，单位 mm
- `curveWidth`：新生成 Curve / 曲线 图层打印线宽，单位 mm

输出：
- `a`：执行日志
- `b`：剖切面原点坐标 `Point3d` 列表

自动处理规则：
- 普通图层：所有新生成对象所在图层都会被识别，普通图层打印线宽设为 `allWidth`
- Curve / 曲线图层：图层名或完整路径包含 `Curve` 或 `曲线` 时，打印线宽设为 `curveWidth`
- Solid / 实体图层：图层名或完整路径包含 `Solid` 或 `实体` 时，自动隐藏
- 玻璃整理：新生成 Curve / 曲线图层中，对象显示颜色等于 `glassLayerInput` 图层颜色时，移动到同级 `玻璃` 图层；玻璃图层会自动设置颜色和打印线宽

## 推荐使用步骤

1. 在 Rhino 中先放好真实 ClippingPlane。
2. 在 GH 中引用这个 ClippingPlane，不要输入 GH 自己构造的普通 Plane。
3. 选择一个玻璃颜色来源图层。
4. 设置 `allWidth` 和 `curveWidth`。
5. 点击 `run`。

结果会自动完成：
- 生成 ClippingDrawing
- 修改新图层线宽
- 隐藏实体图层
- 分类玻璃曲线到同级 `玻璃` 图层
- 输出剖面点坐标

## 推荐接线方式

图层输入优先使用：
- 文本 Panel 输入完整图层路径
- 或 Rhino 真实图层引用

如果脚本提示 `Layer not found`，优先检查：
- 图层名是否为完整路径
- 输入是否真的是 Rhino 图层，而不是 GH 的显示描述文本

按钮输入：
- `run / apply` 接 Button
- 不建议长期接 Boolean Toggle = True

## 常见问题

点了按钮没有反应：
- 检查 Rhino 命令行是否有报错
- 检查输入对象/图层是否存在
- 检查输出 `a` 面板是否有提示信息

修改剖切样式后看不到效果：
- 检查是否真的有 ClippingPlane
- 检查当前显示模式是否开启 `Use section styles`

图层找不到：
- 通常是输入了 GH 的显示文本，不是图层路径字符串
- 建议直接用 Panel 输入完整路径，例如 `A::B::剖面图Curve`

玻璃对象没有被识别：
- 颜色比较基于对象最终显示颜色
- 检查 `glassLayerInput` 的图层颜色是否正确
- 检查对象是否确实在脚本扫描的目标图层内

## 建议组件命名

- `Set Layer Section Style`
- `Set Layer Print Width`
- `Move Objects By Glass Color`
- `Run ClippingDrawings + Cleanup`

## 建筑剖面出图建议工作流

1. Rhino 中整理模型图层。
2. 放置 ClippingPlane。
3. 使用 `Run ClippingDrawings + Cleanup` 自动生成和整理剖面图层。
4. 如有需要，再单独使用：
   - `Set Layer Print Width`
   - `Move Objects By Glass Color`
   - `Set Layer Section Style`
