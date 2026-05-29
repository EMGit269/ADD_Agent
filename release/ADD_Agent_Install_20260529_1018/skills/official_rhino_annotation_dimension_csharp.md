---
name: official-rhino-annotation-dimension-csharp
description: 官方内置 reference C# 代码。逐跨尺寸与总尺寸自动标注；当任务需要生成轴线、尺寸线、斜线标记与标注文字时读取。
---

# Reference C# Scripts

- Reference JSON: `reference/official_annotation_dimension_csharp.json`
- 描述: 逐跨尺寸与总尺寸自动标注（含尺寸界线、箭头、标注文字，偏移可调）
- 使用方式: 先读取 reference JSON 理解电池连接和端口，再读取本 skill 中对应 C# 代码块复用或改造。

## 轴线生成

- id: `01`
- guid: `f4588b04-92ea-49af-9ff0-0ee0a5ab95f2`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:xCount`, `1:xSpacing`, `2:yCount`, `3:ySpacing`, `4:x`
- outputs: `0:out`, `1:a`, `2:b`, `3:c`

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

    private void RunScript(
		object xCount,
		object xSpacing,
		object yCount,
		object ySpacing,
		object x,
		ref object a,
		ref object b,
		ref object c)
    {
    int _xCount = Convert.ToInt32(xCount);
    double _xSpacing = Convert.ToDouble(xSpacing);
    int _yCount = Convert.ToInt32(yCount);
    double _ySpacing = Convert.ToDouble(ySpacing);
    double _extend = Convert.ToDouble(x);
    
    double totalX = _xCount * _xSpacing;
    double totalY = _yCount * _ySpacing;
    
    List<Line> xLines = new List<Line>();
    List<Line> yLines = new List<Line>();
    
    for (int i = 0; i <= _yCount; i++)
    {
        double yy = i * _ySpacing;
        xLines.Add(new Line(-_extend, yy, 0, totalX + _extend, yy, 0));
    }
    
    for (int i = 0; i <= _xCount; i++)
    {
        double xx = i * _xSpacing;
        yLines.Add(new Line(xx, -_extend, 0, xx, totalY + _extend, 0));
    }
    
    b = xLines;
    c = yLines;
}
}
```

## 尺寸标注

- id: `08`
- guid: `5157ceb6-05b2-4e3e-b2f8-fb793b722dd0`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:xCount`, `1:xSpacing`, `2:yCount`, `3:ySpacing`, `4:dimOffset`, `5:x`, `6:y`
- outputs: `0:out`, `1:a`, `2:b`, `3:c`, `4:d`, `5:e`, `6:f`, `7:g`, `8:h`, `9:i`

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

    private void RunScript(
		object xCount,
		object xSpacing,
		object yCount,
		object ySpacing,
		object dimOffset,
		object x,
		object y,
		ref object a,
		ref object b,
		ref object c,
		ref object d,
		ref object e,
		ref object f,
		ref object g,
		ref object h,
		ref object i)
    {
    int _xCount = Convert.ToInt32(xCount);
    double _xSpacing = Convert.ToDouble(xSpacing);
    int _yCount = Convert.ToInt32(yCount);
    double _ySpacing = Convert.ToDouble(ySpacing);
    double _dimOffset = Convert.ToDouble(dimOffset);
    double _extend = Convert.ToDouble(x);
    double _textHeight = Convert.ToDouble(y);
    
    double tickSize = _textHeight * 0.85;
    double halfTick = tickSize / 2.0;
    double textGap = _textHeight;
    double dimLineGap = _dimOffset * 0.65;
    double totalDimGap = _dimOffset * 1.6;
    
    List<Line> xDimLines = new List<Line>();
    List<TextEntity> xDimTexts = new List<TextEntity>();
    
    // X方向逐跨标注（下侧）
    double extStartY = -(_dimOffset + _extend);
    double extEndY = -(_dimOffset + _extend + dimLineGap);
    
    for (int j = 0; j < _xCount; j++)
    {
        double x1 = j * _xSpacing;
        double x2 = (j + 1) * _xSpacing;
        double midX = (x1 + x2) / 2;
    
        xDimLines.Add(new Line(x1, extStartY, 0, x1, extEndY, 0));
        xDimLines.Add(new Line(x2, extStartY, 0, x2, extEndY, 0));
        xDimLines.Add(new Line(x1, extEndY, 0, x2, extEndY, 0));
        xDimLines.Add(new Line(x1 + halfTick, extEndY - halfTick, 0, x1 - halfTick, extEndY + halfTick, 0));
        xDimLines.Add(new Line(x2 + halfTick, extEndY - halfTick, 0, x2 - halfTick, extEndY + halfTick, 0));
    
        TextEntity te = new TextEntity();
        te.Plane = new Plane(new Point3d(midX, extEndY - textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
        te.PlainText = string.Format("{0}", (int)_xSpacing);
        te.TextHeight = _textHeight;
        xDimTexts.Add(te);
    }
    
    // X总尺寸（下侧）
    double xTotalY1 = -(_dimOffset + totalDimGap + _extend);
    double xTotalY2 = -(_dimOffset + totalDimGap + _extend + dimLineGap);
    double xTotalX2 = _xCount * _xSpacing;
    double xTotalMidX = xTotalX2 / 2;
    
    List<Line> xTotalLines = new List<Line>();
    xTotalLines.Add(new Line(0, xTotalY1, 0, 0, xTotalY2, 0));
    xTotalLines.Add(new Line(xTotalX2, xTotalY1, 0, xTotalX2, xTotalY2, 0));
    xTotalLines.Add(new Line(0, xTotalY2, 0, xTotalX2, xTotalY2, 0));
    xTotalLines.Add(new Line(halfTick, xTotalY2 - halfTick, 0, -halfTick, xTotalY2 + halfTick, 0));
    xTotalLines.Add(new Line(xTotalX2 + halfTick, xTotalY2 - halfTick, 0, xTotalX2 - halfTick, xTotalY2 + halfTick, 0));
    
    TextEntity xTotalText = new TextEntity();
    xTotalText.Plane = new Plane(new Point3d(xTotalMidX, xTotalY2 - textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
    xTotalText.PlainText = string.Format("{0}", (int)(_xCount * _xSpacing));
    xTotalText.TextHeight = _textHeight;
    
    // Y方向逐跨标注（左侧）
    List<Line> yDimLines = new List<Line>();
    List<TextEntity> yDimTexts = new List<TextEntity>();
    double extStartX = -(_dimOffset + _extend);
    double extEndX = -(_dimOffset + _extend + dimLineGap);
    
    for (int j = 0; j < _yCount; j++)
    {
        double y1 = j * _ySpacing;
        double y2 = (j + 1) * _ySpacing;
        double midY = (y1 + y2) / 2;
    
        yDimLines.Add(new Line(extStartX, y1, 0, extEndX, y1, 0));
        yDimLines.Add(new Line(extStartX, y2, 0, extEndX, y2, 0));
        yDimLines.Add(new Line(extEndX, y1, 0, extEndX, y2, 0));
        yDimLines.Add(new Line(extEndX - halfTick, y1 + halfTick, 0, extEndX + halfTick, y1 - halfTick, 0));
        yDimLines.Add(new Line(extEndX - halfTick, y2 + halfTick, 0, extEndX + halfTick, y2 - halfTick, 0));
    
        TextEntity te = new TextEntity();
        te.Plane = new Plane(new Point3d(extEndX - textGap, midY, 0), Vector3d.YAxis, Vector3d.XAxis);
        te.PlainText = string.Format("{0}", (int)_ySpacing);
        te.TextHeight = _textHeight;
        yDimTexts.Add(te);
    }
    
    // Y总尺寸（左侧）
    double yTotalExtStartX = -(_dimOffset + totalDimGap + _extend);
    double yTotalExtEndX = -(_dimOffset + totalDimGap + _extend + dimLineGap);
    double yTotalY2 = _yCount * _ySpacing;
    double yTotalMidY = yTotalY2 / 2;
    
    List<Line> yTotalLines = new List<Line>();
    yTotalLines.Add(new Line(yTotalExtStartX, 0, 0, yTotalExtEndX, 0, 0));
    yTotalLines.Add(new Line(yTotalExtStartX, yTotalY2, 0, yTotalExtEndX, yTotalY2, 0));
    yTotalLines.Add(new Line(yTotalExtEndX, 0, 0, yTotalExtEndX, yTotalY2, 0));
    yTotalLines.Add(new Line(yTotalExtEndX - halfTick, halfTick, 0, yTotalExtEndX + halfTick, -halfTick, 0));
    yTotalLines.Add(new Line(yTotalExtEndX - halfTick, yTotalY2 + halfTick, 0, yTotalExtEndX + halfTick, yTotalY2 - halfTick, 0));
    
    TextEntity yTotalText = new TextEntity();
    yTotalText.Plane = new Plane(new Point3d(yTotalExtEndX - textGap, yTotalMidY, 0), Vector3d.YAxis, Vector3d.XAxis);
    yTotalText.PlainText = string.Format("{0}", (int)(_yCount * _ySpacing));
    yTotalText.TextHeight = _textHeight;
    
    b = xDimLines;
    c = yDimLines;
    d = xTotalLines;
    e = yTotalLines;
    f = xDimTexts;
    g = yDimTexts;
    h = new List<TextEntity> { xTotalText };
    i = new List<TextEntity> { yTotalText };
}
}
```

## 轴号标注

- id: `09`
- guid: `9c0fd556-1ca9-4eac-ac2e-c8c719072281`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:xCount`, `1:xSpacing`, `2:yCount`, `3:ySpacing`, `4:labelOffset`, `5:x`, `6:y`
- outputs: `0:out`, `1:a`, `2:b`, `3:c`, `4:d`, `5:e`, `6:f`, `7:g`

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

    private void RunScript(
		object xCount,
		object xSpacing,
		object yCount,
		object ySpacing,
		object labelOffset,
		object x,
		object y,
		ref object a,
		ref object b,
		ref object c,
		ref object d,
		ref object e,
		ref object f,
		ref object g)
    {
    int _xCount = Convert.ToInt32(xCount);
    double _xSpacing = Convert.ToDouble(xSpacing);
    int _yCount = Convert.ToInt32(yCount);
    double _ySpacing = Convert.ToDouble(ySpacing);
    double _labelOffset = Convert.ToDouble(labelOffset);
    double _dimOffset = Convert.ToDouble(x);
    double _extend = Convert.ToDouble(y);
    
    double circleR = 400;
    double dimLinePos = _dimOffset * 1.65;
    
    List<Circle> xCircles = new List<Circle>();
    List<TextDot> xTexts = new List<TextDot>();
    List<Line> xLines = new List<Line>();
    List<Circle> yCircles = new List<Circle>();
    List<TextDot> yTexts = new List<TextDot>();
    List<Line> yLines = new List<Line>();
    
    // X方向轴号（下侧）
    for (int j = 0; j <= _xCount; j++)
    {
        double xx = j * _xSpacing;
        Point3d center = new Point3d(xx, -(_labelOffset + _extend), 0);
        Circle circle = new Circle(Plane.WorldXY, center, circleR);
        TextDot text = new TextDot((j + 1).ToString(), center);
        Line line = new Line(new Point3d(xx, -(dimLinePos + _extend), 0), new Point3d(xx, -(_labelOffset + _extend - circleR), 0));
    
        xCircles.Add(circle);
        xTexts.Add(text);
        xLines.Add(line);
    }
    
    // Y方向轴号（左侧）
    for (int j = 0; j <= _yCount; j++)
    {
        double yy = j * _ySpacing;
        char letter = (char) ('A' + j);
        Point3d center = new Point3d(-(_labelOffset + _extend), yy, 0);
        Circle circle = new Circle(Plane.WorldXY, center, circleR);
        TextDot text = new TextDot(letter.ToString(), center);
        Line line = new Line(new Point3d(-(dimLinePos + _extend), yy, 0), new Point3d(-(_labelOffset + _extend - circleR), yy, 0));
    
        yCircles.Add(circle);
        yTexts.Add(text);
        yLines.Add(line);
    }
    
    b = xCircles;
    c = xTexts;
    d = yCircles;
    e = yTexts;
    f = xLines;
    g = yLines;
}
}
```

