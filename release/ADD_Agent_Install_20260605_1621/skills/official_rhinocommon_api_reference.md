---
name: official-rhinocommon-api-reference
description: 低频使用的官方 RhinoCommon API 查证 skill。仅当正在编写/修改 C# 且具体 RhinoCommon/Grasshopper SDK 类、方法、构造函数、属性、枚举或参数顺序不确定，已有代码出现 API 编译错误，涉及 RhinoDoc/Bake/图层/标注/Clipping 等高风险文档操作，或用户明确要求查官方 API 时使用；普通 GH 建模、已有 reference/skill 可复用、基础几何逻辑明确时不要触发。
---

# RhinoCommon API 参考约束

## 目标

只在 API 不确定或风险较高时，用官方文档查证类、构造函数、属性和方法签名，避免凭记忆编造 API。普通 GH 建模、已有 reference/skill 可复用、或只使用熟悉的基础几何类型时，不需要读取本 skill，也不需要联网。

本 skill 不是离线 API 全量副本。它提供官方入口、查证流程和写代码约束。

## 官方入口

- RhinoCommon Guides: https://developer.rhino3d.com/guides/rhinocommon/
- RhinoCommon API root: https://developer.rhino3d.com/api/rhinocommon/rhino
- API References: https://developer.rhino3d.com/api
- RhinoCommon API docs root: https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/R_Project_RhinoCommon.htm
- Grasshopper SDK root: https://mcneel.github.io/grasshopper-api-docs/api/grasshopper/html/723c01da-9986-4db2-8f53-6f3a7494df75.htm

必要时可用 `web_research` 查询这些官方域名。不要因为要建模就默认联网；只有 API 报错、签名不确定、用户明确要求官方查证或找不到具体官方页面时才查询。查 API 时优先设置：

- `allowed_domains`: `["developer.rhino3d.com", "mcneel.github.io"]`
- `mode`: 已知官方 URL 时直接用 `fetch`；尽量少用 `search`。只有 API 报错、签名/类型不确定、已有代码疑似用错，或不知道具体官方页面时才用 `search` 找候选 URL，再用 `fetch` 读取正文。

## 精准查询策略

1. 先判断任务属于 RhinoCommon 还是 Grasshopper SDK：
   - Rhino 几何、文档对象、图层、材质、Bake、Make2D/HiddenLine、ClippingPlane：RhinoCommon。
   - Grasshopper 数据树、`GH_Path`、`IGH_Goo`、组件、参数、Canvas、Group、端口：Grasshopper SDK。
2. 已知入口页时先 `fetch` 官方根 URL，不要先搜索：
   - RhinoCommon: `https://mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html/R_Project_RhinoCommon.htm`
   - Grasshopper SDK: `https://mcneel.github.io/grasshopper-api-docs/api/grasshopper/html/723c01da-9986-4db2-8f53-6f3a7494df75.htm`
3. 不知道具体类型/方法页时才 `search`，并限制官方域名：
   - RhinoCommon 查询格式：`site:mcneel.github.io/rhinocommon-api-docs/api/RhinoCommon/html <类型或概念> RhinoCommon`
   - Grasshopper 查询格式：`site:mcneel.github.io/grasshopper-api-docs/api/grasshopper/html <类型或概念> Grasshopper`
4. 搜到类型页后继续 `fetch`，沿 Sandcastle 文档结构找方法和属性：
   - `T_...` 类型页
   - `M_...` 方法页
   - `P_...` 属性页
   - `Methods_...` 方法列表页
   - `Properties_...` 属性列表页
5. 概念名称和 API 名不一致时，先搜概念对应物，不要编造 API：
   - Make2D / 绘制二维：查 `HiddenLineDrawing`、`HiddenLineDrawingParameters`。
   - 剖切线：查 `SectionCut`、`ClippingPlaneIndex`、`AddClippingPlane`。
   - ClippingDrawings 命令流程：若找不到稳定 RhinoCommon 封装，使用 `RhinoApp.RunScript` 作为命令级 fallback。

## 何时才需要查证

满足任一条件时，才读取本 skill，并按需查官方 API：

- 不确定类名、构造函数、方法名、参数顺序、返回类型或枚举值。
- 已有 C# 编译错误指向 RhinoCommon / Grasshopper SDK API 签名。
- 任务涉及会修改 Rhino 文档的高风险操作：`RhinoDoc` 写入、Bake、删除对象、图层、对象属性、材质、标注样式、视图、Display、文件读写或 Clipping。
- 用户明确提到“官方 API”“RhinoCommon”“C# Rhino 库”“避免 API 用错”。

## 查证方向

- 几何构造、变换、相交、曲线、曲面、Brep、Mesh、Plane、Point、Vector：查 `Rhino.Geometry`。
- Rhino 文档、添加/查找/删除对象、视图刷新：查 `Rhino.RhinoDoc` 与 `doc.Objects` 相关 API。
- 图层、对象属性、打印线宽、颜色、材质索引：查 `Rhino.DocObjects.Layer`、`ObjectAttributes`、`Material`。
- Bake 或写入 Rhino 文档：查 `doc.Objects.Add...` 方法和 `ObjectAttributes` 配套用法。
- Grasshopper C# Script 中的普通几何建模如果只用常见 `Point3d`、`Vector3d`、`Line`、`Curve`、`Brep` 等基础类型，且写法明确，不需要查本 skill；不要因为运行环境是 C# 电池就默认联网。
- 只有涉及 Grasshopper 自身数据结构或组件机制时查 Grasshopper SDK，例如 `DataTree<T>`、`GH_Path`、`IGH_Goo`、`Grasshopper.Kernel`、参数、组件、Canvas、Group、端口。
- 标注、尺寸样式、文字样式：先读 `skills/official_rhino_annotation_style.md` 和相关 C# skill，再查 Annotation / DimStyle API。
- ClippingDrawing 或出图整理：先读 `skills/official_rhino_clipping_drawings_csharp.md` 或批量 ClippingDrawing C# skill，再查 RhinoCommon；若官方 API 不覆盖命令能力，使用 `RhinoApp.RunScript`。
- 预览、显示管线、自定义显示：查 Display 相关 API；若只是 Grasshopper 预览上色，优先读 `skills/official_grasshopper_visualization_preview.md`，不要为了颜色写 C#。

## C# Script 写法约束

- Grasshopper C# 专用工具通常只接收 `RunScript` 方法体。不要输出 `using`、class、完整模板或自定义 `RunScript` 签名。
- 端口名和顺序必须与工具声明一致。输出变量使用宿主生成的实际变量（通常 `b/c/d...`），业务语义写进端口标签、描述或注释，不要在代码里发明不存在的输出变量名。
- 访问 Rhino 文档时使用 `RhinoDocument ?? Rhino.RhinoDoc.ActiveDoc`，并处理 null。
- 修改 Rhino 文档、Bake、图层、对象属性前，应有明确触发输入（如 Button / run bool），避免 Grasshopper 重算导致重复写入。
- 对用户输入做 null、空集合、极端值和单位检查。失败时给报告输出，不要静默返回错误几何。

## 禁止事项

- 不要把 Rhino 命令名、Grasshopper 电池名、旧版本记忆或相似英文名直接当作 RhinoCommon 方法。
- 不要编造看似合理的方法，例如在未查证时写不存在的 `CreateXxx`、`AddXxx`、`SetXxx`。
- 如果官方 API 查不到稳定能力，改用可验证方案：现有专项 skill、Grasshopper 原生电池、`RhinoApp.RunScript`，或明确说明 RhinoCommon 暂无直接 API。
- 不要把能用标准 GH 电池清晰表达的简单参数、颜色预览或基础数学硬塞进 C#。

## 输出前检查

- 关键 RhinoCommon 类型和方法是否已按官方 API 查证。
- 是否只输出 C# Script body，而不是完整模板。
- 是否处理了 `RhinoDoc` 为 null、输入为空、重复 Bake/重复写文档等风险。
- 是否避免了未查证 API、旧版本 API 和命令名/API 混用。
- 如果使用 `RhinoApp.RunScript`，是否说明这是命令级 fallback，而非 RhinoCommon 类型 API。
