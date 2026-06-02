---
name: official-rhino-clippingdrawing-batch-csharp
description: 官方内置 reference C# 代码。ClippingDrawing 批量自动化出图工作流；当任务需要批量生成剖切图、整理图层和设置线宽时读取。
---

# Reference C# Scripts

- Reference JSON: `reference/official_clippingdrawing_batch_csharp.json`
- 描述: 基于 C# Script 的 ClippingDrawing 批量自动化出图工作流
- 使用方式: 先读取 reference JSON 理解电池连接和端口，再读取本 skill 中对应 C# 代码块复用或改造。

## C# Script

- id: `01`
- guid: `c99f081f-f281-4175-8b1e-b876d08d45c3`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:run`, `1:allWidth`, `2:curveWidth`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object run,
		double allWidth,
		double curveWidth,
		ref object a)
  {
    bool doRun = ToBool(run, false);
    if (!doRun)
    {
      a = "Idle. run 接 Button。allWidth 和 curveWidth 单位为 mm。";
      return;
    }

    RhinoDoc doc = RhinoDocument ?? RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      a = "找不到 Rhino 文档。";
      return;
    }

    double allWidthMm = Math.Max(0.0, allWidth);
    double curveWidthMm = Math.Max(0.0, curveWidth);

    List<Guid> objectIdsBefore = GetExistingObjectIds(doc);

    List<Guid> clippingPlaneIds = PickClippingPlanes(doc);
    if (clippingPlaneIds.Count == 0)
    {
      a = "没有选择 ClippingPlane，已取消生成。";
      return;
    }

    doc.Objects.UnselectAll();

    int selected = SelectObjectsById(doc, clippingPlaneIds);
    if (selected == 0)
    {
      a = "没有成功选中 ClippingPlane。";
      return;
    }

    string command =
      "_ClippingDrawings " +
      "_Angle=0 " +
      "_PrintWidth=_ByLayer " +
      "_DisplayColor=_ByInputObject " +
      "_ShowHatch=_Yes " +
      "_ShowSolid=_Yes " +
      "_AddBackground=_Yes " +
      "_Projection=_Parallel " +
      "_AddHidden=_No " +
      "_AddSilhouette=_Yes " +
      "_ShowLabel=_Yes " +
      "_LabelStyle=_Dot " +
      "_ApplyToAll=_No " +
      "_Pause " +
      "_Enter";

    bool result = RhinoApp.RunScript(command, true);

    doc.Objects.UnselectAll();
    doc.Views.Redraw();

    List<Guid> newObjectIds = GetNewObjectIds(doc, objectIdsBefore);

    int foundAll;
    int changedAllLayers;
    int foundCurve;
    int changedCurveLayers;
    int resetObjectsToByLayer;
    int hiddenSolidLayers;

    ApplyLayerPlotWeightsToNewDrawing(
      doc,
      newObjectIds,
      allWidthMm,
      curveWidthMm,
      out foundAll,
      out changedAllLayers,
      out foundCurve,
      out changedCurveLayers,
      out resetObjectsToByLayer,
      out hiddenSolidLayers
    );

    doc.Views.Redraw();

    a =
      (result ? "完成。已创建 ClippingDrawing。\n" : "ClippingDrawings 命令没有成功完成，请查看 Rhino 命令行历史。\n") +
      "本次新对象数：" + newObjectIds.Count + "\n" +
      "普通图层目标：" + allWidthMm.ToString("0.###") + " mm\n" +
      "Curve 图层目标：" + curveWidthMm.ToString("0.###") + " mm\n" +
      "普通图层：找到 " + foundAll + "，修改 " + changedAllLayers + "\n" +
      "Curve 图层：找到 " + foundCurve + "，修改 " + changedCurveLayers + "\n" +
      "对象打印线宽设为 ByLayer 数：" + resetObjectsToByLayer + "\n" +
      "隐藏 Solid 图层数：" + hiddenSolidLayers;
  }

  private static void ApplyLayerPlotWeightsToNewDrawing(
    RhinoDoc doc,
    List<Guid> newObjectIds,
    double allWidthMm,
    double curveWidthMm,
    out int foundAll,
    out int changedAllLayers,
    out int foundCurve,
    out int changedCurveLayers,
    out int resetObjectsToByLayer,
    out int hiddenSolidLayers)
  {
    foundAll = 0;
    changedAllLayers = 0;
    foundCurve = 0;
    changedCurveLayers = 0;
    resetObjectsToByLayer = 0;
    hiddenSolidLayers = 0;

    List<int> touchedLayerIndexes = new List<int>();
    List<int> hiddenSolidLayerIndexes = new List<int>();

    foreach (Guid objectId in newObjectIds)
    {
      RhinoObject obj = doc.Objects.FindId(objectId);
      if (obj == null)
        continue;

      Layer layer = doc.Layers[obj.Attributes.LayerIndex];
      bool isCurveLayer = IsCurveLayer(layer);
      bool isSolidLayer = IsSolidLayer(layer);
      double targetWidth = isCurveLayer ? curveWidthMm : allWidthMm;

      if (!touchedLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        touchedLayerIndexes.Add(obj.Attributes.LayerIndex);

        if (isCurveLayer)
          foundCurve++;
        else
          foundAll++;

        Layer layerCopy = new Layer();
        layerCopy.CopyAttributesFrom(layer);
        layerCopy.PlotWeight = targetWidth;

        bool layerOk = doc.Layers.Modify(layerCopy, layer.Id, true);
        if (layerOk)
        {
          if (isCurveLayer)
            changedCurveLayers++;
          else
            changedAllLayers++;
        }
      }

      if (isSolidLayer && !hiddenSolidLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        hiddenSolidLayerIndexes.Add(obj.Attributes.LayerIndex);

        Layer hideLayerCopy = new Layer();
        hideLayerCopy.CopyAttributesFrom(layer);
        hideLayerCopy.IsVisible = false;

        bool hideOk = doc.Layers.Modify(hideLayerCopy, layer.Id, true);
        if (hideOk)
          hiddenSolidLayers++;
      }

      ObjectAttributes attr = obj.Attributes.Duplicate();
      attr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromLayer;

      bool objOk = doc.Objects.ModifyAttributes(obj, attr, true);
      if (objOk)
        resetObjectsToByLayer++;
    }
  }

  private static bool IsCurveLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsSolidLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static List<Guid> GetExistingObjectIds(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj != null && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> GetNewObjectIds(RhinoDoc doc, List<Guid> beforeIds)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj == null)
        continue;

      if (beforeIds.Contains(obj.Id))
        continue;

      if (!ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> PickClippingPlanes(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    GetObject go = new GetObject();
    go.SetCommandPrompt("选择要生成 ClippingDrawing 的 ClippingPlane，回车结束");
    go.GeometryFilter = ObjectType.ClipPlane;
    go.EnablePreSelect(true, true);
    go.EnablePostSelect(true);
    go.GetMultiple(1, 0);

    if (go.CommandResult() != Rhino.Commands.Result.Success)
      return ids;

    for (int i = 0; i < go.ObjectCount; i++)
    {
      RhinoObject obj = go.Object(i).Object();
      if (IsClippingPlane(obj) && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static bool IsClippingPlane(RhinoObject obj)
  {
    return obj != null && obj.Geometry is ClippingPlaneSurface;
  }

  private static int SelectObjectsById(RhinoDoc doc, IEnumerable<Guid> ids)
  {
    int count = 0;

    foreach (Guid id in ids)
    {
      RhinoObject obj = doc.Objects.FindId(id);
      if (obj == null)
        continue;

      obj.Select(true);
      count++;
    }

    doc.Views.Redraw();
    return count;
  }

  private static bool ToBool(object value, bool fallback)
  {
    if (value == null)
      return fallback;

    if (value is bool)
      return (bool)value;

    bool parsed;
    if (bool.TryParse(value.ToString(), out parsed))
      return parsed;

    return fallback;
  }
}
```

## C# Script

- id: `12`
- guid: `ba9266bf-d9a9-4a2a-8972-f015cb7767f6`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:hatchPatternInput`, `1:layerInput`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object hatchPatternInput,
		object layerInput,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to update.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    int hatchIndex = ResolveHatchPatternIndex(hatchPatternInput);
    if (hatchIndex < 0)
    {
      a = "Hatch pattern not found.";
      return;
    }

    Layer sourceLayer = ResolveLayer(layerInput);
    if (sourceLayer == null)
    {
      a = "Layer not found.";
      return;
    }

    var layerCopy = new Layer();
    layerCopy.CopyAttributesFrom(sourceLayer);

    var style = sourceLayer.GetCustomSectionStyle();
    if (style == null)
      style = new SectionStyle();
    else
      style.EnsurePrivateCopy();

    style.HatchIndex = hatchIndex;
    layerCopy.SetCustomSectionStyle(style);

    bool ok = RhinoDocument.Layers.Modify(layerCopy, sourceLayer.Id, true);
    RhinoDocument.Views.Redraw();

    a = ok ? "Updated layer hatch pattern." : "Update failed.";
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      var lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted && string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
        return lyr;
    }

    return null;
  }

  private int ResolveHatchPatternIndex(object input)
  {
    object obj = Unwrap(input);

    if (obj is HatchPattern hp)
      return hp.Index;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return -1;

    foreach (var p in RhinoDocument.HatchPatterns)
    {
      if (p == null || p.IsDeleted)
        continue;

      if (string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
        return p.Index;
    }

    return -1;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      var prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      var next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `17`
- guid: `f4bbd090-4284-4540-9ff9-fe7d66f1cea5`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:layerInput`, `1:printWidth`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object layerInput,
		double printWidth,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to update.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(layerInput);
    if (sourceLayer == null)
    {
      a = "Layer not found.";
      return;
    }

    if (printWidth < 0.0)
    {
      a = "Print width must be >= 0. Use 0 for hairline/default behavior.";
      return;
    }

    var layerCopy = new Layer();
    layerCopy.CopyAttributesFrom(sourceLayer);

    layerCopy.PlotWeight = printWidth;

    bool ok = RhinoDocument.Layers.Modify(layerCopy, sourceLayer.Id, true);
    RhinoDocument.Views.Redraw();

    a = ok
      ? string.Format("Updated print width: {0} -> {1}", sourceLayer.FullPath, printWidth)
      : "Update failed.";
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      var lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted && string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
        return lyr;
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      var prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      var next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `23`
- guid: `1b0db1c8-b2a9-45e1-a563-c63b86dd56a0`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:layerInput`, `1:printWidth`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object layerInput,
		double printWidth,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to update.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(layerInput);
    if (sourceLayer == null)
    {
      a = "Layer not found.";
      return;
    }

    if (printWidth < 0.0)
    {
      a = "Print width must be >= 0. Use 0 for hairline/default behavior.";
      return;
    }

    var layerCopy = new Layer();
    layerCopy.CopyAttributesFrom(sourceLayer);

    layerCopy.PlotWeight = printWidth;

    bool ok = RhinoDocument.Layers.Modify(layerCopy, sourceLayer.Id, true);
    RhinoDocument.Views.Redraw();

    a = ok
      ? string.Format("Updated print width: {0} -> {1}", sourceLayer.FullPath, printWidth)
      : "Update failed.";
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      var lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted && string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
        return lyr;
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      var prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      var next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `27`
- guid: `0478309f-94ed-4218-823c-5d9d1fc72dac`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:sourceLayerInput`, `1:glassLayerInput`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object sourceLayerInput,
		object glassLayerInput,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to run.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(sourceLayerInput);
    if (sourceLayer == null)
    {
      a = "Source layer not found.";
      return;
    }

    Layer glassSourceLayer = ResolveLayer(glassLayerInput);
    if (glassSourceLayer == null)
    {
      a = "Glass color layer not found.";
      return;
    }

    Color glassColor = glassSourceLayer.Color;

    int targetLayerIndex = EnsureSiblingLayer(sourceLayer, "玻璃", glassColor);
    if (targetLayerIndex < 0)
    {
      a = "Failed to create or find target layer.";
      return;
    }

    int movedCount = 0;

    foreach (RhinoObject obj in RhinoDocument.Objects)
    {
      if (obj == null) continue;
      if (obj.IsDeleted) continue;

      if (obj.Attributes.LayerIndex != sourceLayer.Index)
        continue;

      Color objColor = obj.Attributes.DrawColor(RhinoDocument);
      if (objColor.ToArgb() != glassColor.ToArgb())
        continue;

      ObjectAttributes newAttr = obj.Attributes.Duplicate();
      newAttr.LayerIndex = targetLayerIndex;

      bool ok = RhinoDocument.Objects.ModifyAttributes(obj, newAttr, true);
      if (ok) movedCount++;
    }

    RhinoDocument.Views.Redraw();

    Layer targetLayer = RhinoDocument.Layers[targetLayerIndex];
    a = string.Format(
      "Moved {0} object(s) from \"{1}\" to \"{2}\" using color from layer \"{3}\".",
      movedCount,
      sourceLayer.FullPath,
      targetLayer.FullPath,
      glassSourceLayer.FullPath
    );
  }

  private int EnsureSiblingLayer(Layer sourceLayer, string targetLeafName, Color layerColor)
  {
    string targetFullPath = BuildSiblingLayerFullPath(sourceLayer, targetLeafName);

    int existingIndex = RhinoDocument.Layers.FindByFullPath(targetFullPath, -1);
    if (existingIndex >= 0)
      return existingIndex;

    Layer newLayer = new Layer();
    newLayer.Name = targetLeafName;
    newLayer.Color = layerColor;
    newLayer.ParentLayerId = sourceLayer.ParentLayerId;

    return RhinoDocument.Layers.Add(newLayer);
  }

  private string BuildSiblingLayerFullPath(Layer sourceLayer, string targetLeafName)
  {
    string fullPath = sourceLayer.FullPath;
    string separator = Rhino.RhinoMath.UnsetValue.ToString();

    string[] parts = fullPath.Split(new string[] { "::" }, StringSplitOptions.None);
    if (parts.Length <= 1)
      return targetLeafName;

    string[] parentParts = new string[parts.Length - 1];
    Array.Copy(parts, parentParts, parts.Length - 1);

    return string.Join("::", parentParts) + "::" + targetLeafName;
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      Layer lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted &&
          string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return lyr;
      }
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      PropertyInfo prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      object next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `29`
- guid: `179fa1bd-9781-4190-aef1-f302f2d2dfc3`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:run`, `1:allWidth`, `2:curveWidth`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object run,
		double allWidth,
		double curveWidth,
		ref object a)
  {
    bool doRun = ToBool(run, false);
    if (!doRun)
    {
      a = "Idle. run 接 Button。allWidth 和 outlineWidth 单位为 mm。";
      return;
    }

    RhinoDoc doc = RhinoDocument ?? RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      a = "找不到 Rhino 文档。";
      return;
    }

    double allWidthMm = Math.Max(0.0, allWidth);
    double outlineWidthMm = Math.Max(0.0, curveWidth);

    List<Guid> objectIdsBefore = GetExistingObjectIds(doc);

    List<Guid> clippingPlaneIds = PickClippingPlanes(doc);
    if (clippingPlaneIds.Count == 0)
    {
      a = "没有选择 ClippingPlane，已取消生成。";
      return;
    }

    doc.Objects.UnselectAll();

    int selected = SelectObjectsById(doc, clippingPlaneIds);
    if (selected == 0)
    {
      a = "没有成功选中 ClippingPlane。";
      return;
    }

    string command =
      "_ClippingDrawings " +
      "_Angle=0 " +
      "_PrintWidth=_ByLayer " +
      "_DisplayColor=_ByInputObject " +
      "_ShowHatch=_Yes " +
      "_ShowSolid=_Yes " +
      "_AddBackground=_Yes " +
      "_Projection=_Parallel " +
      "_AddHidden=_No " +
      "_AddSilhouette=_Yes " +
      "_ShowLabel=_Yes " +
      "_LabelStyle=_Dot " +
      "_ApplyToAll=_No " +
      "_Pause " +
      "_Enter";

    bool result = RhinoApp.RunScript(command, true);

    doc.Objects.UnselectAll();
    doc.Views.Redraw();

    List<Guid> newObjectIds = GetNewObjectIds(doc, objectIdsBefore);

    int foundAll;
    int changedAllLayers;
    int foundOutline;
    int changedOutlineLayers;
    int resetObjectsToByLayer;
    int hiddenSolidLayers;

    ApplyLayerPlotWeightsToNewDrawing(
      doc,
      newObjectIds,
      allWidthMm,
      outlineWidthMm,
      out foundAll,
      out changedAllLayers,
      out foundOutline,
      out changedOutlineLayers,
      out resetObjectsToByLayer,
      out hiddenSolidLayers
    );

    doc.Views.Redraw();

    a =
      (result ? "完成。已创建立面 ClippingDrawing。\n" : "ClippingDrawings 命令没有成功完成，请查看 Rhino 命令行历史。\n") +
      "本次新对象数：" + newObjectIds.Count + "\n" +
      "普通图层目标：" + allWidthMm.ToString("0.###") + " mm\n" +
      "外轮廓图层目标：" + outlineWidthMm.ToString("0.###") + " mm\n" +
      "普通图层：找到 " + foundAll + "，修改 " + changedAllLayers + "\n" +
      "外轮廓图层：找到 " + foundOutline + "，修改 " + changedOutlineLayers + "\n" +
      "对象打印线宽设为 ByLayer 数：" + resetObjectsToByLayer + "\n" +
      "隐藏 Solid 图层数：" + hiddenSolidLayers;
  }

  private static void ApplyLayerPlotWeightsToNewDrawing(
    RhinoDoc doc,
    List<Guid> newObjectIds,
    double allWidthMm,
    double outlineWidthMm,
    out int foundAll,
    out int changedAllLayers,
    out int foundOutline,
    out int changedOutlineLayers,
    out int resetObjectsToByLayer,
    out int hiddenSolidLayers)
  {
    foundAll = 0;
    changedAllLayers = 0;
    foundOutline = 0;
    changedOutlineLayers = 0;
    resetObjectsToByLayer = 0;
    hiddenSolidLayers = 0;

    List<int> touchedLayerIndexes = new List<int>();
    List<int> hiddenSolidLayerIndexes = new List<int>();

    foreach (Guid objectId in newObjectIds)
    {
      RhinoObject obj = doc.Objects.FindId(objectId);
      if (obj == null)
        continue;

      Layer layer = doc.Layers[obj.Attributes.LayerIndex];
      bool isOutlineLayer = IsOutlineLayer(layer);
      bool isSolidLayer = IsSolidLayer(layer);
      double targetWidth = isOutlineLayer ? outlineWidthMm : allWidthMm;

      if (!touchedLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        touchedLayerIndexes.Add(obj.Attributes.LayerIndex);

        if (isOutlineLayer)
          foundOutline++;
        else
          foundAll++;

        Layer layerCopy = new Layer();
        layerCopy.CopyAttributesFrom(layer);
        layerCopy.PlotWeight = targetWidth;

        bool layerOk = doc.Layers.Modify(layerCopy, layer.Id, true);
        if (layerOk)
        {
          if (isOutlineLayer)
            changedOutlineLayers++;
          else
            changedAllLayers++;
        }
      }

      if (isSolidLayer && !hiddenSolidLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        hiddenSolidLayerIndexes.Add(obj.Attributes.LayerIndex);

        Layer hideLayerCopy = new Layer();
        hideLayerCopy.CopyAttributesFrom(layer);
        hideLayerCopy.IsVisible = false;

        bool hideOk = doc.Layers.Modify(hideLayerCopy, layer.Id, true);
        if (hideOk)
          hiddenSolidLayers++;
      }

      ObjectAttributes attr = obj.Attributes.Duplicate();
      attr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromLayer;

      bool objOk = doc.Objects.ModifyAttributes(obj, attr, true);
      if (objOk)
        resetObjectsToByLayer++;
    }
  }

  private static bool IsOutlineLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Silhouette", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Silhouette", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("轮廓", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("轮廓", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("外轮廓", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("外轮廓", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsSolidLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static List<Guid> GetExistingObjectIds(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj != null && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> GetNewObjectIds(RhinoDoc doc, List<Guid> beforeIds)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj == null)
        continue;

      if (beforeIds.Contains(obj.Id))
        continue;

      if (!ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> PickClippingPlanes(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    GetObject go = new GetObject();
    go.SetCommandPrompt("选择要生成 ClippingDrawing 的 ClippingPlane，回车结束");
    go.GeometryFilter = ObjectType.ClipPlane;
    go.EnablePreSelect(true, true);
    go.EnablePostSelect(true);
    go.GetMultiple(1, 0);

    if (go.CommandResult() != Rhino.Commands.Result.Success)
      return ids;

    for (int i = 0; i < go.ObjectCount; i++)
    {
      RhinoObject obj = go.Object(i).Object();
      if (IsClippingPlane(obj) && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static bool IsClippingPlane(RhinoObject obj)
  {
    return obj != null && obj.Geometry is ClippingPlaneSurface;
  }

  private static int SelectObjectsById(RhinoDoc doc, IEnumerable<Guid> ids)
  {
    int count = 0;

    foreach (Guid id in ids)
    {
      RhinoObject obj = doc.Objects.FindId(id);
      if (obj == null)
        continue;

      obj.Select(true);
      count++;
    }

    doc.Views.Redraw();
    return count;
  }

  private static bool ToBool(object value, bool fallback)
  {
    if (value == null)
      return fallback;

    if (value is bool)
      return (bool)value;

    bool parsed;
    if (bool.TryParse(value.ToString(), out parsed))
      return parsed;

    return fallback;
  }
}
```

## C# Script

- id: `32`
- guid: `de8ffda8-abad-42db-a343-51930ebce14e`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:x`
- outputs: `0:out`, `1:a`

```csharp
// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(object x, ref object a)
    {
        bool run = ToBool(x, false);
        if (!run)
        {
            a = "Idle. 把 x 接 Button，点击后运行。";
            return;
        }

        RhinoDoc doc = RhinoDocument ?? RhinoDoc.ActiveDoc;
        if (doc == null)
        {
            a = "找不到 Rhino 文档。";
            return;
        }

        List<Guid> clippingPlaneIds = PickClippingPlanes(doc);

        if (clippingPlaneIds.Count == 0)
        {
            a = "没有选择 ClippingPlane，已取消。";
            return;
        }

        doc.Objects.UnselectAll();

        int selected = SelectObjectsById(doc, clippingPlaneIds);
        if (selected == 0)
        {
            a = "没有成功选中 ClippingPlane。";
            return;
        }

        string command =
            "_ClippingDrawings " +
            "_Angle=0 " +
            "_PrintWidth=_ByLayer " +
            "_DisplayColor=_ByInputObject " +
            "_ShowHatch=_Yes " +
            "_ShowSolid=_Yes " +
            "_AddBackground=_Yes " +
            "_Projection=_Parallel " +
            "_AddHidden=_No " +
            "_AddSilhouette=_No " +
            "_ShowLabel=_Yes " +
            "_LabelStyle=_Dot " +
            "_ApplyToAll=_No " +
            "_Pause " +
            "_Enter";

        bool result = RhinoApp.RunScript(command, true);

        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        if (result)
            a = "完成。已按指定参数在 Rhino 当前文件中创建 ClippingDrawing。";
        else
            a = "ClippingDrawings 命令没有成功完成，请查看 Rhino 命令行历史。";
    }

    private static List<Guid> PickClippingPlanes(RhinoDoc doc)
    {
        List<Guid> ids = new List<Guid>();

        GetObject go = new GetObject();
        go.SetCommandPrompt("选择要生成 ClippingDrawing 的 ClippingPlane，回车结束");
        go.GeometryFilter = ObjectType.ClipPlane;
        go.EnablePreSelect(true, true);
        go.EnablePostSelect(true);
        go.GetMultiple(1, 0);

        if (go.CommandResult() != Rhino.Commands.Result.Success)
            return ids;

        for (int i = 0; i < go.ObjectCount; i++)
        {
            RhinoObject obj = go.Object(i).Object();
            if (IsClippingPlane(obj) && !ids.Contains(obj.Id))
                ids.Add(obj.Id);
        }

        return ids;
    }

    private static bool IsClippingPlane(RhinoObject obj)
    {
        return obj != null && obj.Geometry is ClippingPlaneSurface;
    }

    private static int SelectObjectsById(RhinoDoc doc, IEnumerable<Guid> ids)
    {
        int count = 0;

        foreach (Guid id in ids)
        {
            RhinoObject obj = doc.Objects.FindId(id);
            if (obj == null)
                continue;

            obj.Select(true);
            count++;
        }

        doc.Views.Redraw();
        return count;
    }

    private static bool ToBool(object value, bool fallback)
    {
        if (value == null)
            return fallback;

        if (value is bool)
            return (bool)value;

        bool parsed;
        if (bool.TryParse(value.ToString(), out parsed))
            return parsed;

        return fallback;
    }
}
```

## C# Script

- id: `42`
- guid: `fdce2a8b-a45f-418e-b487-fca7df1072b4`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:layerInput`, `1:printWidth`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object layerInput,
		double printWidth,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to update.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(layerInput);
    if (sourceLayer == null)
    {
      a = "Layer not found.";
      return;
    }

    if (printWidth < 0.0)
    {
      a = "Print width must be >= 0. Use 0 for hairline/default behavior.";
      return;
    }

    var layerCopy = new Layer();
    layerCopy.CopyAttributesFrom(sourceLayer);

    layerCopy.PlotWeight = printWidth;

    bool ok = RhinoDocument.Layers.Modify(layerCopy, sourceLayer.Id, true);
    RhinoDocument.Views.Redraw();

    a = ok
      ? string.Format("Updated print width: {0} -> {1}", sourceLayer.FullPath, printWidth)
      : "Update failed.";
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      var lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted && string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
        return lyr;
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      var prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      var next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `46`
- guid: `7c17adfd-7352-4adf-adc5-acd797d6772f`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:sourceLayerInput`, `1:glassLayerInput`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object sourceLayerInput,
		object glassLayerInput,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to run.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(sourceLayerInput);
    if (sourceLayer == null)
    {
      a = "Source layer not found.";
      return;
    }

    Layer glassSourceLayer = ResolveLayer(glassLayerInput);
    if (glassSourceLayer == null)
    {
      a = "Glass color layer not found.";
      return;
    }

    Color glassColor = glassSourceLayer.Color;

    int targetLayerIndex = EnsureSiblingLayer(sourceLayer, "玻璃", glassColor);
    if (targetLayerIndex < 0)
    {
      a = "Failed to create or find target layer.";
      return;
    }

    int movedCount = 0;

    foreach (RhinoObject obj in RhinoDocument.Objects)
    {
      if (obj == null) continue;
      if (obj.IsDeleted) continue;

      if (obj.Attributes.LayerIndex != sourceLayer.Index)
        continue;

      Color objColor = obj.Attributes.DrawColor(RhinoDocument);
      if (objColor.ToArgb() != glassColor.ToArgb())
        continue;

      ObjectAttributes newAttr = obj.Attributes.Duplicate();
      newAttr.LayerIndex = targetLayerIndex;

      bool ok = RhinoDocument.Objects.ModifyAttributes(obj, newAttr, true);
      if (ok) movedCount++;
    }

    RhinoDocument.Views.Redraw();

    Layer targetLayer = RhinoDocument.Layers[targetLayerIndex];
    a = string.Format(
      "Moved {0} object(s) from \"{1}\" to \"{2}\" using color from layer \"{3}\".",
      movedCount,
      sourceLayer.FullPath,
      targetLayer.FullPath,
      glassSourceLayer.FullPath
    );
  }

  private int EnsureSiblingLayer(Layer sourceLayer, string targetLeafName, Color layerColor)
  {
    string targetFullPath = BuildSiblingLayerFullPath(sourceLayer, targetLeafName);

    int existingIndex = RhinoDocument.Layers.FindByFullPath(targetFullPath, -1);
    if (existingIndex >= 0)
      return existingIndex;

    Layer newLayer = new Layer();
    newLayer.Name = targetLeafName;
    newLayer.Color = layerColor;
    newLayer.ParentLayerId = sourceLayer.ParentLayerId;

    return RhinoDocument.Layers.Add(newLayer);
  }

  private string BuildSiblingLayerFullPath(Layer sourceLayer, string targetLeafName)
  {
    string fullPath = sourceLayer.FullPath;
    string separator = Rhino.RhinoMath.UnsetValue.ToString();

    string[] parts = fullPath.Split(new string[] { "::" }, StringSplitOptions.None);
    if (parts.Length <= 1)
      return targetLeafName;

    string[] parentParts = new string[parts.Length - 1];
    Array.Copy(parts, parentParts, parts.Length - 1);

    return string.Join("::", parentParts) + "::" + targetLeafName;
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      Layer lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted &&
          string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return lyr;
      }
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      PropertyInfo prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      object next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `54`
- guid: `d2ada7b5-131b-4dd3-b265-033fb53a99d9`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:layerInput`, `1:printWidth`, `2:apply`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object layerInput,
		double printWidth,
		bool apply,
		ref object a)
  {
    if (!apply)
    {
      a = "Press the button to update.";
      return;
    }

    if (RhinoDocument == null)
    {
      a = "RhinoDocument is null.";
      return;
    }

    Layer sourceLayer = ResolveLayer(layerInput);
    if (sourceLayer == null)
    {
      a = "Layer not found.";
      return;
    }

    if (printWidth < 0.0)
    {
      a = "Print width must be >= 0. Use 0 for hairline/default behavior.";
      return;
    }

    var layerCopy = new Layer();
    layerCopy.CopyAttributesFrom(sourceLayer);

    layerCopy.PlotWeight = printWidth;

    bool ok = RhinoDocument.Layers.Modify(layerCopy, sourceLayer.Id, true);
    RhinoDocument.Views.Redraw();

    a = ok
      ? string.Format("Updated print width: {0} -> {1}", sourceLayer.FullPath, printWidth)
      : "Update failed.";
  }

  private Layer ResolveLayer(object input)
  {
    object obj = Unwrap(input);

    if (obj is Layer l)
      return l;

    string name = obj as string;
    if (string.IsNullOrWhiteSpace(name))
      return null;

    int idx = RhinoDocument.Layers.FindByFullPath(name, -1);
    if (idx >= 0)
      return RhinoDocument.Layers[idx];

    for (int i = 0; i < RhinoDocument.Layers.Count; i++)
    {
      var lyr = RhinoDocument.Layers[i];
      if (lyr != null && !lyr.IsDeleted && string.Equals(lyr.Name, name, StringComparison.OrdinalIgnoreCase))
        return lyr;
    }

    return null;
  }

  private object Unwrap(object input)
  {
    object current = input;
    for (int i = 0; i < 3 && current != null; i++)
    {
      var prop = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
      if (prop == null) break;

      var next = prop.GetValue(current, null);
      if (next == null || ReferenceEquals(next, current)) break;

      current = next;
    }
    return current;
  }
}
```

## C# Script

- id: `60`
- guid: `30759ec8-1cd6-4efb-9197-94b46aa063d8`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:run`, `1:allWidth`, `2:curveWidth`
- outputs: `0:out`, `1:a`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object run,
		double allWidth,
		double curveWidth,
		ref object a)
  {
    bool doRun = ToBool(run, false);
    if (!doRun)
    {
      a = "Idle. run 接 Button。allWidth 和 curveWidth 单位为 mm。";
      return;
    }

    RhinoDoc doc = RhinoDocument ?? RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      a = "找不到 Rhino 文档。";
      return;
    }

    double allWidthMm = Math.Max(0.0, allWidth);
    double curveWidthMm = Math.Max(0.0, curveWidth);

    List<Guid> objectIdsBefore = GetExistingObjectIds(doc);

    List<Guid> clippingPlaneIds = PickClippingPlanes(doc);
    if (clippingPlaneIds.Count == 0)
    {
      a = "没有选择 ClippingPlane，已取消生成。";
      return;
    }

    doc.Objects.UnselectAll();

    int selected = SelectObjectsById(doc, clippingPlaneIds);
    if (selected == 0)
    {
      a = "没有成功选中 ClippingPlane。";
      return;
    }

    string command =
      "_ClippingDrawings " +
      "_Angle=0 " +
      "_PrintWidth=_ByLayer " +
      "_DisplayColor=_ByInputObject " +
      "_ShowHatch=_Yes " +
      "_ShowSolid=_Yes " +
      "_AddBackground=_Yes " +
      "_Projection=_Parallel " +
      "_AddHidden=_No " +
      "_AddSilhouette=_Yes " +
      "_ShowLabel=_Yes " +
      "_LabelStyle=_Dot " +
      "_ApplyToAll=_No " +
      "_Pause " +
      "_Enter";

    bool result = RhinoApp.RunScript(command, true);

    doc.Objects.UnselectAll();
    doc.Views.Redraw();

    List<Guid> newObjectIds = GetNewObjectIds(doc, objectIdsBefore);

    int foundAll;
    int changedAllLayers;
    int changedAllObjects;
    int foundCurve;
    int changedCurveLayers;
    int changedCurveObjects;
    int hiddenSolidLayers;

    ApplyPlotWeightsToNewDrawing(
      doc,
      newObjectIds,
      allWidthMm,
      curveWidthMm,
      out foundAll,
      out changedAllLayers,
      out changedAllObjects,
      out foundCurve,
      out changedCurveLayers,
      out changedCurveObjects,
      out hiddenSolidLayers
    );

    doc.Views.Redraw();

    a =
      (result ? "完成。已创建 ClippingDrawing。\n" : "ClippingDrawings 命令没有成功完成，请查看 Rhino 命令行历史。\n") +
      "本次新对象数：" + newObjectIds.Count + "\n" +
      "普通线目标：" + allWidthMm.ToString("0.###") + " mm\n" +
      "Curve 目标：" + curveWidthMm.ToString("0.###") + " mm\n" +
      "普通对象：找到 " + foundAll + "，图层修改 " + changedAllLayers + "，对象兜底修改 " + changedAllObjects + "\n" +
      "Curve 对象：找到 " + foundCurve + "，图层修改 " + changedCurveLayers + "，对象兜底修改 " + changedCurveObjects + "\n" +
      "隐藏 Solid 图层数：" + hiddenSolidLayers;
  }

  private static void ApplyPlotWeightsToNewDrawing(
    RhinoDoc doc,
    List<Guid> newObjectIds,
    double allWidthMm,
    double curveWidthMm,
    out int foundAll,
    out int changedAllLayers,
    out int changedAllObjects,
    out int foundCurve,
    out int changedCurveLayers,
    out int changedCurveObjects,
    out int hiddenSolidLayers)
  {
    foundAll = 0;
    changedAllLayers = 0;
    changedAllObjects = 0;
    foundCurve = 0;
    changedCurveLayers = 0;
    changedCurveObjects = 0;
    hiddenSolidLayers = 0;

    List<int> touchedLayerIndexes = new List<int>();
    List<int> hiddenSolidLayerIndexes = new List<int>();

    foreach (Guid objectId in newObjectIds)
    {
      RhinoObject obj = doc.Objects.FindId(objectId);
      if (obj == null)
        continue;

      Layer layer = doc.Layers[obj.Attributes.LayerIndex];
      bool isCurveLayer = IsCurveLayer(layer);
      bool isSolidLayer = IsSolidLayer(layer);
      double targetWidth = isCurveLayer ? curveWidthMm : allWidthMm;

      if (isCurveLayer)
        foundCurve++;
      else
        foundAll++;

      if (!touchedLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        touchedLayerIndexes.Add(obj.Attributes.LayerIndex);

        Layer layerCopy = new Layer();
        layerCopy.CopyAttributesFrom(layer);
        layerCopy.PlotWeight = targetWidth;

        bool layerOk = doc.Layers.Modify(layerCopy, layer.Id, true);
        if (layerOk)
        {
          if (isCurveLayer)
            changedCurveLayers++;
          else
            changedAllLayers++;
        }
      }

      if (isSolidLayer && !hiddenSolidLayerIndexes.Contains(obj.Attributes.LayerIndex))
      {
        hiddenSolidLayerIndexes.Add(obj.Attributes.LayerIndex);

        Layer hideLayerCopy = new Layer();
        hideLayerCopy.CopyAttributesFrom(layer);
        hideLayerCopy.IsVisible = false;

        bool hideOk = doc.Layers.Modify(hideLayerCopy, layer.Id, true);
        if (hideOk)
          hiddenSolidLayers++;
      }

      ObjectAttributes attr = obj.Attributes.Duplicate();
      attr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
      attr.PlotWeight = targetWidth;

      bool objOk = doc.Objects.ModifyAttributes(obj, attr, true);
      if (objOk)
      {
        if (isCurveLayer)
          changedCurveObjects++;
        else
          changedAllObjects++;
      }
    }
  }

  private static bool IsCurveLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsSolidLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static List<Guid> GetExistingObjectIds(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj != null && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> GetNewObjectIds(RhinoDoc doc, List<Guid> beforeIds)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj == null)
        continue;

      if (beforeIds.Contains(obj.Id))
        continue;

      if (!ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> PickClippingPlanes(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    GetObject go = new GetObject();
    go.SetCommandPrompt("选择要生成 ClippingDrawing 的 ClippingPlane，回车结束");
    go.GeometryFilter = ObjectType.ClipPlane;
    go.EnablePreSelect(true, true);
    go.EnablePostSelect(true);
    go.GetMultiple(1, 0);

    if (go.CommandResult() != Rhino.Commands.Result.Success)
      return ids;

    for (int i = 0; i < go.ObjectCount; i++)
    {
      RhinoObject obj = go.Object(i).Object();
      if (IsClippingPlane(obj) && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static bool IsClippingPlane(RhinoObject obj)
  {
    return obj != null && obj.Geometry is ClippingPlaneSurface;
  }

  private static int SelectObjectsById(RhinoDoc doc, IEnumerable<Guid> ids)
  {
    int count = 0;

    foreach (Guid id in ids)
    {
      RhinoObject obj = doc.Objects.FindId(id);
      if (obj == null)
        continue;

      obj.Select(true);
      count++;
    }

    doc.Views.Redraw();
    return count;
  }

  private static bool ToBool(object value, bool fallback)
  {
    if (value == null)
      return fallback;

    if (value is bool)
      return (bool)value;

    bool parsed;
    if (bool.TryParse(value.ToString(), out parsed))
      return parsed;

    return fallback;
  }
}
```

## C# Script

- id: `64`
- guid: `0cf202d3-e4f1-423f-adf4-dbe5c7829b70`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:run`, `1:clippingPlaneInput`, `2:glassLayerInput`, `3:allWidth`, `4:curveWidth`
- outputs: `0:out`, `1:a`, `2:b`

```csharp
#region Usings
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(
		object run,
		object clippingPlaneInput,
		object glassLayerInput,
		double allWidth,
		double curveWidth,
		ref object a,
		ref object b)
  {
    bool doRun = ToBool(run, false);
    if (!doRun)
    {
      a = "Idle. run 接 Button；clippingPlaneInput 接 Rhino 里的 ClippingPlane；glassLayerInput 接玻璃颜色来源图层。";
      b = null;
      return;
    }

    RhinoDoc doc = RhinoDocument ?? RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      a = "找不到 Rhino 文档。";
      b = null;
      return;
    }

    Layer glassSourceLayer = ResolveLayer(doc, glassLayerInput);
    if (glassSourceLayer == null)
    {
      a = "Glass color layer not found.";
      b = null;
      return;
    }

    Color glassColor = glassSourceLayer.Color;
    double allWidthMm = Math.Max(0.0, allWidth);
    double curveWidthMm = Math.Max(0.0, curveWidth);

    List<Point3d> sectionPoints;
    List<Guid> clippingPlaneIds = ResolveClippingPlaneIds(doc, clippingPlaneInput, out sectionPoints);
    if (clippingPlaneIds.Count == 0)
    {
      a = "没有识别到有效的 ClippingPlane。请给 clippingPlaneInput 输入 Rhino 中已存在的 ClippingPlane 引用。";
      b = null;
      return;
    }

    b = sectionPoints;

    List<Guid> objectIdsBefore = GetExistingObjectIds(doc);

    doc.Objects.UnselectAll();
    int selected = SelectObjectsById(doc, clippingPlaneIds);
    if (selected == 0)
    {
      a = "没有成功选中 ClippingPlane。";
      return;
    }

    string command =
      "_ClippingDrawings " +
      "_Angle=0 " +
      "_PrintWidth=_ByLayer " +
      "_DisplayColor=_ByInputObject " +
      "_ShowHatch=_Yes " +
      "_ShowSolid=_Yes " +
      "_AddBackground=_Yes " +
      "_Projection=_Parallel " +
      "_AddHidden=_No " +
      "_AddSilhouette=_Yes " +
      "_ShowLabel=_Yes " +
      "_LabelStyle=_Dot " +
      "_ApplyToAll=_No " +
      "_Pause " +
      "_Enter";

    bool result = RhinoApp.RunScript(command, true);

    doc.Objects.UnselectAll();
    doc.Views.Redraw();

    List<Guid> newObjectIds = GetNewObjectIds(doc, objectIdsBefore);

    int foundAll;
    int changedAllLayers;
    int foundCurve;
    int changedCurveLayers;
    int resetObjectsToByLayer;
    int hiddenSolidLayers;
    int movedGlassObjects;
    int changedGlassLayers;
    List<string> glassLayerPaths;

    ApplyLayerRulesToNewDrawing(
      doc,
      newObjectIds,
      glassColor,
      allWidthMm,
      curveWidthMm,
      out foundAll,
      out changedAllLayers,
      out foundCurve,
      out changedCurveLayers,
      out resetObjectsToByLayer,
      out hiddenSolidLayers,
      out movedGlassObjects,
      out changedGlassLayers,
      out glassLayerPaths
    );

    doc.Views.Redraw();

    string glassLayerSummary = glassLayerPaths.Count > 0
      ? string.Join(", ", glassLayerPaths.ToArray())
      : "无";

    a =
      (result ? "完成。已创建 ClippingDrawing。\n" : "ClippingDrawings 命令没有成功完成，请查看 Rhino 命令行历史。\n") +
      "剖面点数量：" + sectionPoints.Count + "\n" +
      "本次新对象数：" + newObjectIds.Count + "\n" +
      "普通图层目标线宽：" + allWidthMm.ToString("0.###") + " mm\n" +
      "Curve 图层目标线宽：" + curveWidthMm.ToString("0.###") + " mm\n" +
      "普通图层：找到 " + foundAll + "，修改 " + changedAllLayers + "\n" +
      "Curve 图层：找到 " + foundCurve + "，修改 " + changedCurveLayers + "\n" +
      "对象打印线宽设为 ByLayer 数：" + resetObjectsToByLayer + "\n" +
      "隐藏 Solid 图层数：" + hiddenSolidLayers + "\n" +
      "玻璃物件移动数：" + movedGlassObjects + "\n" +
      "玻璃图层修改数：" + changedGlassLayers + "\n" +
      "玻璃目标图层：" + glassLayerSummary;
  }

  private static void ApplyLayerRulesToNewDrawing(
    RhinoDoc doc,
    List<Guid> newObjectIds,
    Color glassColor,
    double allWidthMm,
    double curveWidthMm,
    out int foundAll,
    out int changedAllLayers,
    out int foundCurve,
    out int changedCurveLayers,
    out int resetObjectsToByLayer,
    out int hiddenSolidLayers,
    out int movedGlassObjects,
    out int changedGlassLayers,
    out List<string> glassLayerPaths)
  {
    foundAll = 0;
    changedAllLayers = 0;
    foundCurve = 0;
    changedCurveLayers = 0;
    resetObjectsToByLayer = 0;
    hiddenSolidLayers = 0;
    movedGlassObjects = 0;
    changedGlassLayers = 0;
    glassLayerPaths = new List<string>();

    HashSet<int> touchedLayerIndexes = new HashSet<int>();
    HashSet<int> hiddenSolidLayerIndexes = new HashSet<int>();
    HashSet<int> touchedGlassLayerIndexes = new HashSet<int>();

    foreach (Guid objectId in newObjectIds)
    {
      RhinoObject obj = doc.Objects.FindId(objectId);
      if (obj == null || obj.IsDeleted)
        continue;

      Layer layer = doc.Layers[obj.Attributes.LayerIndex];
      if (layer == null)
        continue;

      bool isCurveLayer = IsCurveLayer(layer);
      bool isSolidLayer = IsSolidLayer(layer);
      double targetWidth = isCurveLayer ? curveWidthMm : allWidthMm;

      if (!touchedLayerIndexes.Contains(layer.Index))
      {
        touchedLayerIndexes.Add(layer.Index);

        if (isCurveLayer)
          foundCurve++;
        else
          foundAll++;

        Layer layerCopy = new Layer();
        layerCopy.CopyAttributesFrom(layer);
        layerCopy.PlotWeight = targetWidth;

        bool layerOk = doc.Layers.Modify(layerCopy, layer.Id, true);
        if (layerOk)
        {
          if (isCurveLayer)
            changedCurveLayers++;
          else
            changedAllLayers++;
        }
      }

      if (isSolidLayer && !hiddenSolidLayerIndexes.Contains(layer.Index))
      {
        hiddenSolidLayerIndexes.Add(layer.Index);

        Layer hideLayerCopy = new Layer();
        hideLayerCopy.CopyAttributesFrom(layer);
        hideLayerCopy.IsVisible = false;

        bool hideOk = doc.Layers.Modify(hideLayerCopy, layer.Id, true);
        if (hideOk)
          hiddenSolidLayers++;
      }

      Color objColor = obj.Attributes.DrawColor(doc);

      ObjectAttributes attr = obj.Attributes.Duplicate();
      attr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromLayer;

      if (isCurveLayer && objColor.ToArgb() == glassColor.ToArgb())
      {
        int glassLayerIndex = EnsureSiblingLayer(doc, layer, "玻璃", glassColor, curveWidthMm);
        if (glassLayerIndex >= 0)
        {
          attr.LayerIndex = glassLayerIndex;
          movedGlassObjects++;

          if (!touchedGlassLayerIndexes.Contains(glassLayerIndex))
          {
            touchedGlassLayerIndexes.Add(glassLayerIndex);

            Layer glassLayer = doc.Layers[glassLayerIndex];
            if (glassLayer != null && !glassLayerPaths.Contains(glassLayer.FullPath))
              glassLayerPaths.Add(glassLayer.FullPath);

            changedGlassLayers++;
          }
        }
      }

      bool objOk = doc.Objects.ModifyAttributes(obj, attr, true);
      if (objOk)
        resetObjectsToByLayer++;
    }
  }

  private static int EnsureSiblingLayer(
    RhinoDoc doc,
    Layer sourceLayer,
    string targetLeafName,
    Color layerColor,
    double plotWeightMm)
  {
    string targetFullPath = BuildSiblingLayerFullPath(sourceLayer, targetLeafName);

    int existingIndex = doc.Layers.FindByFullPath(targetFullPath, -1);
    if (existingIndex >= 0)
    {
      Layer existing = doc.Layers[existingIndex];
      Layer existingCopy = new Layer();
      existingCopy.CopyAttributesFrom(existing);
      existingCopy.Color = layerColor;
      existingCopy.PlotWeight = plotWeightMm;
      doc.Layers.Modify(existingCopy, existing.Id, true);
      return existingIndex;
    }

    Layer newLayer = new Layer();
    newLayer.Name = targetLeafName;
    newLayer.Color = layerColor;
    newLayer.ParentLayerId = sourceLayer.ParentLayerId;
    newLayer.PlotWeight = plotWeightMm;

    return doc.Layers.Add(newLayer);
  }

  private static string BuildSiblingLayerFullPath(Layer sourceLayer, string targetLeafName)
  {
    string fullPath = sourceLayer.FullPath ?? sourceLayer.Name ?? "";

    string[] parts = fullPath.Split(new string[] { "::" }, StringSplitOptions.None);
    if (parts.Length <= 1)
      return targetLeafName;

    string[] parentParts = new string[parts.Length - 1];
    Array.Copy(parts, parentParts, parts.Length - 1);

    return string.Join("::", parentParts) + "::" + targetLeafName;
  }

  private static bool IsCurveLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("曲线", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsSolidLayer(Layer layer)
  {
    if (layer == null)
      return false;

    string name = layer.Name ?? "";
    string fullPath = layer.FullPath ?? name;

    return name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0
      || fullPath.IndexOf("实体", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static List<Guid> GetExistingObjectIds(RhinoDoc doc)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj != null && !ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static List<Guid> GetNewObjectIds(RhinoDoc doc, List<Guid> beforeIds)
  {
    List<Guid> ids = new List<Guid>();

    ObjectEnumeratorSettings settings = new ObjectEnumeratorSettings();
    settings.HiddenObjects = true;
    settings.LockedObjects = true;
    settings.NormalObjects = true;
    settings.IncludeGrips = false;
    settings.IncludeLights = false;

    foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
    {
      if (obj == null)
        continue;

      if (beforeIds.Contains(obj.Id))
        continue;

      if (!ids.Contains(obj.Id))
        ids.Add(obj.Id);
    }

    return ids;
  }

  private static int SelectObjectsById(RhinoDoc doc, IEnumerable<Guid> ids)
  {
    int count = 0;

    foreach (Guid id in ids)
    {
      RhinoObject obj = doc.Objects.FindId(id);
      if (obj == null)
        continue;

      obj.Select(true);
      count++;
    }

    doc.Views.Redraw();
    return count;
  }

  private static List<Guid> ResolveClippingPlaneIds(RhinoDoc doc, object input, out List<Point3d> points)
  {
    points = new List<Point3d>();
    List<Guid> ids = new List<Guid>();
    List<object> items = new List<object>();

    FlattenInput(input, items);

    foreach (object item in items)
    {
      object obj = Unwrap(item);
      Guid id = Guid.Empty;
      RhinoObject rhObj = null;

      if (obj is Guid)
      {
        id = (Guid)obj;
        rhObj = doc.Objects.FindId(id);
      }
      else if (obj is RhinoObject)
      {
        rhObj = obj as RhinoObject;
        if (rhObj != null) id = rhObj.Id;
      }
      else if (obj != null)
      {
        P
...[truncated 2628 chars]
```

