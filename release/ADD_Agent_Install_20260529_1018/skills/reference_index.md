---
name: reference-index
description: 在完成初步 GH 建模逻辑规划之后查阅；仅当条目与已定方案相关时，再调用 read_reference_json 读取 JSON 对照实现。若 reference_metadata.csharp_scripts 存在，应优先检查其中 C# 代码。
---

# Reference Index

使用流程：
1. 先规划：用简短步骤说明本任务的 GH 逻辑（数据流、关键电池、风险点等）。
2. 再浏览：查阅下列参考条目，看是否与**已定方案**高度相关。
3. 后读取：若相关，调用 `read_reference_json` 并传入对应 `file_name`，用 JSON 对齐细节、补充或改造实现；若条目含 `技能：skills/official_*_csharp.md`，先用 `read_skill_file` 读取官方拆分后的 C# 代码；若 JSON 含 `reference_metadata.csharp_scripts`，也要检查其中代码、端口和用途。

## References
- 描述：逐跨尺寸与总尺寸自动标注（含尺寸界线、箭头、标注文字，偏移可调）
  文件：reference/official_annotation_dimension_csharp.json
  调用：read_reference_json(file_name="official_annotation_dimension_csharp.json")
  技能：skills/official_rhino_annotation_dimension_csharp.md
- 描述：基于 C# Script 的 ClippingDrawing 批量自动化出图工作流
  文件：reference/official_clippingdrawing_batch_csharp.json
  调用：read_reference_json(file_name="official_clippingdrawing_batch_csharp.json")
  技能：skills/official_rhino_clippingdrawing_batch_csharp.md
