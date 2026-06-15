---
name: reference-index
description: 在完成初步 GH 建模逻辑规划之后查阅；仅当条目与已定方案相关时，再调用 read_reference_json 读取 JSON 对照实现。若 reference_metadata.csharp_scripts 存在，应优先检查其中 C# 代码。
---

# Reference Index

使用流程：
1. 先规划：用简短步骤说明本任务的 GH 逻辑（数据流、关键电池、风险点等）。
2. 再浏览：查阅下列参考条目，看是否与**已定方案**高度相关。
3. 后读取：若相关，调用 `read_reference_json` 并传入对应 `file_name`，用 JSON 对齐细节、补充或改造实现；若条目含 `技能：skills/official_*_csharp.md`，先用 `read_skill_file` 读取官方拆分后的 C# 代码；若 JSON 含 `reference_metadata.csharp_scripts`，也要检查其中代码、端口和用途。
4. 只有在 C# Script 的具体 RhinoCommon/Grasshopper SDK API 不确定、已有 API 编译错误、涉及 RhinoDoc/Bake/图层/标注/Clipping 等高风险文档操作，或用户明确要求官方 API 查证时，才读取 `skills/official_rhinocommon_api_reference.md`；普通建模和已有 reference/skill 可复用时不要默认读取或联网搜索。

## 轴网标注选型

轴网标注目前有两套 reference 体系，先判断要哪一套：

| 体系 | reference | 适用场景 | 输出特点 |
|---|---|---|---|
| 轻量正交轴网体系 | `00 axis with dimensions.gh` / `01 axis.gh` / `02 dimensions.gh` / `03 axis number.gh` | 常规正交轴网、轴号、尺寸线，用户没有要求 Rhino 原生尺寸对象 | 多数输出为 `Line`、`Circle`、`TextEntity` |
| Rhino 标注对象体系 | `axis.gh` + `dimension.gh` | 需要 `LinearDimension`、尺寸样式/模型空间比例、尺寸文字高度二次调整、斜交轴网、`AxisAngleDeg`、或明确复用 `axis.gh` / `dimension.gh` | `axis.gh` 生成 `AxisDimensions`，`dimension.gh` 调整并输出 `AdjustedDimensions` |

默认规则：
1. 普通“创建轴网并标注 / 5*5轴网并标注 / 轴号泡泡和尺寸”优先用轻量正交轴网体系的 `00 axis with dimensions.gh`。
2. 用户明确要求“斜交轴网”“Rhino原生尺寸”“LinearDimension”“尺寸样式”“模型空间比例”“调整尺寸文字高度/尺寸线距离/内外尺寸间距”时，改用 `axis.gh + dimension.gh`。
3. 用户明确只要某一部分时，使用轻量正交轴网体系的拆分 reference：`01` 轴线、`02` 尺寸、`03` 轴号。
4. 不要把两套体系混接：`02 dimensions.gh` 不接 `axis.gh` 的 `AxisDimensions`；`dimension.gh` 必须接 `axis.gh` 的 `AxisDimensions`。

轻量正交轴网体系内部，先判断用户要的是“完整轴网标注”还是“拆分式组合”：

| 场景 | 首选 reference | 作用 |
|---|---|---|
| 轴线 + 轴号 + 尺寸一次完成 | `00 axis with dimensions.gh` | 一体化入口，优先级最高 |
| 只要轴线 | `01 axis.gh` | 只生成轴线和 report |
| 只要尺寸 | `02 dimensions.gh` | 只生成逐跨尺寸和总尺寸 |
| 只要轴号 | `03 axis number.gh` | 只生成轴号圆、文字和引线 |

拆分式组合时，按这个顺序理解：
1. `01 axis.gh` 负责轴线。
2. `02 dimensions.gh` 负责尺寸。
3. `03 axis number.gh` 负责轴号。
4. 三者都要用同一组 `xSpans/ySpans/xCount/yCount/extension`。

默认优先级：
1. 用户明确说“轴网并标注”“创建5*5轴网并标注”“建筑轴线标注”时，优先 `00 axis with dimensions.gh`。
2. 用户明确只说“轴线”“轴网”“不需要尺寸”时，优先 `01 axis.gh`。
3. 用户明确只说“加尺寸”“逐跨尺寸”“总尺寸”时，优先 `02 dimensions.gh`。
4. 用户明确只说“加轴号”“轴号泡泡”“轴线编号”时，优先 `03 axis number.gh`。
5. 上述任务一旦出现 `LinearDimension`、斜交轴网或尺寸对象二次调整要求，切换到 `axis.gh + dimension.gh`。

## References
- 描述：一体化建筑轴网 + 轴线尺寸标注（例如“创建5*5轴网并标注”“轴网并标注”“建筑轴线标注”“轴号泡泡和尺寸”）。首选读取该 skill 并导入 `00 axis with dimensions.gh`；该文件已经包含轴线、轴号、引线、逐跨尺寸和总尺寸。
  文件：reference/00 axis with dimensions.gh
  调用：先 read_skill_file("official_rhino_axis_with_dimensions_csharp.md")，再 import_reference_gh(file_name="00 axis with dimensions.gh")
  技能：skills/official_rhino_axis_with_dimensions_csharp.md
- 描述：Rhino 标注对象体系的建筑轴网生成（例如“斜交轴网”“需要 LinearDimension”“需要 AxisDimensions 输出”“按轴线数量和 AxisAngleDeg 生成轴网”“复用 axis.gh”）。会生成轴线、轴号泡泡、原始 `AxisDimensions` 和端点数据；若用户要尺寸标注最终效果，必须继续导入 `dimension.gh` 并连接 `Axis.AxisDimensions -> Dimensions.AxisDimensions`。
  文件：reference/axis.gh
  调用：先 read_skill_file("official_rhino_axis_grid_csharp.md")，再 import_reference_gh(file_name="axis.gh")
  技能：skills/official_rhino_axis_grid_csharp.md
- 描述：Rhino `LinearDimension` 轴网尺寸二次调整（例如“调整尺寸文字高度”“调整模型空间比例”“调整尺寸线距离”“调整内外尺寸线间距”“复用 dimension.gh”）。不能单独使用；必须接收 `axis.gh` 的 `AxisDimensions` 输出，并以 `AdjustedDimensions` 作为最终尺寸输出。
  文件：reference/dimension.gh
  调用：先 read_skill_file("official_rhino_axis_dimension_adjust_csharp.md")，再 import_reference_gh(file_name="dimension.gh")，并连接 Axis.AxisDimensions -> Dimensions.AxisDimensions
  技能：skills/official_rhino_axis_dimension_adjust_csharp.md
- 描述：轻量建筑轴网轴线生成（例如“创建轴网”“只生成轴线”“建筑轴线”“结构轴网”“不要尺寸标注”）。只输出轴线和 report；不生成轴号泡泡、引线、逐跨尺寸或总尺寸。
  文件：reference/01 axis.gh
  调用：先 read_skill_file("official_rhino_axis_csharp.md")，再 import_reference_gh(file_name="01 axis.gh")
  技能：skills/official_rhino_axis_csharp.md
- 描述：建筑轴网逐跨尺寸与总尺寸自动标注（例如“给轴网加尺寸”“只要尺寸标注”“逐跨尺寸”“总尺寸”“轴线尺寸”）。可与 `01 axis.gh` 分步组合，但需要保持 `xSpans/ySpans/xCount/yCount/extension` 参数一致；若要完整轴网+轴号+尺寸，首选 `00 axis with dimensions.gh`。
  文件：reference/02 dimensions.gh
  调用：先 read_skill_file("official_rhino_dimensions_csharp.md")，再 import_reference_gh(file_name="02 dimensions.gh")
  技能：skills/official_rhino_dimensions_csharp.md
- 描述：建筑轴网轴号泡泡、轴号文字和引线（例如“添加轴号”“轴号泡泡”“轴线编号”“轴网编号”“只要轴号”）。可与 `01 axis.gh` 和 `02 dimensions.gh` 分步组合，但需要保持 `xSpans/ySpans/xCount/yCount/extension` 参数一致；若要完整轴网+轴号+尺寸，首选 `00 axis with dimensions.gh`。
  文件：reference/03 axis number.gh
  调用：先 read_skill_file("official_rhino_axis_number_csharp.md")，再 import_reference_gh(file_name="03 axis number.gh")
  技能：skills/official_rhino_axis_number_csharp.md
- 描述：基于 C# Script 的 ClippingDrawing 批量自动化出图工作流
  文件：reference/official_clippingdrawing_batch_csharp.json
  调用：read_reference_json(file_name="official_clippingdrawing_batch_csharp.json")
  技能：skills/official_rhino_clippingdrawing_batch_csharp.md

## API Skills
- 描述：低频 RhinoCommon / Grasshopper C# 官方 API 查证流程；仅在 API 签名不确定、API 编译错误、高风险 Rhino 文档操作或用户明确要求官方查证时使用。普通 GH 建模不要默认触发。
  技能：skills/official_rhinocommon_api_reference.md

## 自训练 skill 索引

