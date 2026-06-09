---
name: official-rhino-batch-bake-csharp
description: 官方内置 C# Script。把多个 Grasshopper 输入中的 Rhino Geometry 批量 Bake 到指定 Rhino 图层；当任务需要按构件类别批量烘焙到轴线、柱子、玻璃立面、楼板、杆件、屋顶柱、梁等图层时读取。
---

# Rhino / Grasshopper C# 批量 Bake 电池

## 功能

这个 C# Script 用于从 Grasshopper 的多个输入端收集几何体，并按固定图层名称批量 Bake 到 Rhino 文档中。

典型用途：
- 把 GH 生成的建筑构件一次性烘焙到 Rhino 图层。
- 按构件类别分图层输出，方便后续出图、赋材质、设置线宽或导出。
- 避免 GH 数据匹配导致同一次求解中重复 Bake。

## 适用环境

- Rhino 8
- Grasshopper C# Script
- 输入必须是 Rhino/Grasshopper 可转换为 `Rhino.Geometry.GeometryBase` 的几何对象
- 执行类输入建议接 Button，不建议长期接 Boolean Toggle = True

## 输入

该脚本按输入序号读取几何：

- `x` / `run`
  - 建议作为第 0 个输入，用 Button 触发。
  - 原代码片段未显式检查该输入，但实际作为执行类电池时建议增加触发判断。
- 第 1 个几何输入：Bake 到 `轴线`
- 第 2 个几何输入：Bake 到 `轴线`
- 第 3 个几何输入：Bake 到 `柱子`
- 第 4 个几何输入：Bake 到 `玻璃立面`
- 第 5 个几何输入：Bake 到 `楼板`
- 第 6 个几何输入：Bake 到 `菱形杆件`
- 第 7 个几何输入：Bake 到 `屋顶柱`
- 第 8 个几何输入：Bake 到 `梁`

## 输出

- `b`
  - 执行结果文本，例如：`Baked 120 objects. DISABLE ME!`

## 行为规则

- 只在 `Iteration == 0` 时执行 Bake，避免 Grasshopper 数据匹配导致同一轮求解重复烘焙。
- 脚本会遍历每个输入端的 `VolatileData.AllData(true)`，从中提取：
  - 单个 `Rhino.Geometry.GeometryBase`
  - 可枚举集合中的 `Rhino.Geometry.GeometryBase`
- 如果目标图层不存在，会自动创建。
- Bake 后调用 `doc.Views.Redraw()` 刷新 Rhino 视图。

## 使用建议

- 建议把组件命名为：`Batch Bake By Layer`
- 第 0 输入建议接 Button，并在脚本中增加 `run/apply` 判断。
- Bake 完成后应立即断开 Button 或禁用组件，避免重复 Bake。
- 如果需要团队复用，建议把图层数组改成输入端或集中配置项。
- 如果对象需要材质、颜色、打印线宽，建议在 Bake 后配合图层管理脚本继续处理。

## 常见问题

1. 重复 Bake
   - 原因通常是 Button/Toggle 持续触发，或组件重新计算。
   - 建议接 Button，并在执行后禁用组件。

2. 某个输入没有 Bake
   - 检查输入数据是否真的是 `GeometryBase` 或包含 `GeometryBase` 的集合。
   - 检查该输入端是否为空。

3. Bake 到错误图层
   - 检查输入顺序。脚本按 `i + 1` 读取几何输入，第 0 输入通常预留给 run/apply。

## C# Script

- component: `C# Script`
- runtime: `CSharpComponent`
- suggested inputs: `0:run`, `1:axisA`, `2:axisB`, `3:columns`, `4:glassFacade`, `5:floors`, `6:diamondMembers`, `7:roofColumns`, `8:beams`
- suggested outputs: `0:out`, `1:b`

```csharp
{
    // 只在首次迭代执行 Bake，避免数据匹配导致重复
    if (Iteration != 0)
    {
        b = "";
        return;
    }
    
    List<Rhino.Geometry.GeometryBase> GetAllGeo(int inputIdx)
    {
        var res = new List<Rhino.Geometry.GeometryBase>();
        var param = Component.Params.Input[inputIdx];
        if (param == null) return res;
        var vlist = param.VolatileData;
        foreach (var dp in vlist.AllData(true))
        {
            if (dp == null) continue;
            var val = dp.ScriptVariable();
            if (val is Rhino.Geometry.GeometryBase g) res.Add(g);
            else if (val is System.Collections.IEnumerable en)
            {
                foreach (var v in en)
                    if (v is Rhino.Geometry.GeometryBase g2) res.Add(g2);
            }
        }
        return res;
    }
    
    string[] layers = {"轴线","轴线","柱子","玻璃立面","楼板","菱形杆件","屋顶柱","梁"};
    var doc = Rhino.RhinoDoc.ActiveDoc;
    int total = 0;
    
    for (int i = 0; i < 8; i++)
    {
        var geos = GetAllGeo(i + 1);
        Print("Input " + (i + 1) + ": " + geos.Count + " geos");
        if (geos.Count == 0) continue;
        string ln = layers[i];
        int li = doc.Layers.FindByFullPath(ln, -1);
        if (li < 0) li = doc.Layers.Add(ln, System.Drawing.Color.FromArgb(0, 0, 0));
        var attr = new Rhino.DocObjects.ObjectAttributes();
        attr.LayerIndex = li;
        foreach (var geo in geos)
        {
            doc.Objects.Add(geo, attr);
            total++;
        }
    }
    doc.Views.Redraw();
    b = "Baked " + total + " objects. DISABLE ME!";
}
```

## 推荐增强版

用于实际生成组件时，优先使用这个版本：显式增加 `run` 判断，并处理 Rhino 文档为空的情况。

```csharp
{
    bool doRun = false;
    if (run is bool rb) doRun = rb;
    else if (run != null) bool.TryParse(run.ToString(), out doRun);

    if (!doRun)
    {
        b = "Idle. Connect run/apply to a Button.";
        return;
    }

    if (Iteration != 0)
    {
        b = "";
        return;
    }

    var doc = Rhino.RhinoDoc.ActiveDoc;
    if (doc == null)
    {
        b = "No active Rhino document.";
        return;
    }

    List<Rhino.Geometry.GeometryBase> GetAllGeo(int inputIdx)
    {
        var res = new List<Rhino.Geometry.GeometryBase>();
        if (inputIdx < 0 || inputIdx >= Component.Params.Input.Count) return res;
        var param = Component.Params.Input[inputIdx];
        if (param == null) return res;

        foreach (var dp in param.VolatileData.AllData(true))
        {
            if (dp == null) continue;
            var val = dp.ScriptVariable();
            if (val is Rhino.Geometry.GeometryBase g)
            {
                res.Add(g);
            }
            else if (val is System.Collections.IEnumerable en && !(val is string))
            {
                foreach (var v in en)
                    if (v is Rhino.Geometry.GeometryBase g2) res.Add(g2);
            }
        }
        return res;
    }

    string[] layers = { "轴线", "轴线", "柱子", "玻璃立面", "楼板", "菱形杆件", "屋顶柱", "梁" };
    int total = 0;

    for (int i = 0; i < layers.Length; i++)
    {
        var geos = GetAllGeo(i + 1);
        Print("Input " + (i + 1) + ": " + geos.Count + " geos");
        if (geos.Count == 0) continue;

        string layerName = layers[i];
        int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
        if (layerIndex < 0)
            layerIndex = doc.Layers.Add(layerName, System.Drawing.Color.FromArgb(0, 0, 0));

        var attr = new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
        foreach (var geo in geos)
        {
            doc.Objects.Add(geo, attr);
            total++;
        }
    }

    doc.Views.Redraw();
    b = "Baked " + total + " objects. DISABLE ME!";
}
```
