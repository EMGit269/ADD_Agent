---
name: official-rhino-annotation-dimension-csharp
description: 官方内置 C# 代码。逐跨尺寸、总尺寸、轴线、轴号圆、TextEntity 标注文字；包含文字居中、TextHeight、高度、模型空间缩放比、注解样式排障规则。
---

# Reference C# Scripts

- Reference JSON: `reference/official_annotation_dimension_csharp.json`
- 描述: 逐跨尺寸与总尺寸自动标注（含尺寸界线、箭头、标注文字，偏移可调）
- 使用方式: 先读取 reference JSON 理解电池连接和端口，再读取本 skill 中对应 C# 代码块复用或改造。
- 默认 Slider: `标注偏移(mm)` 默认值为 `2000` mm；`轴号偏移(mm)` 默认值为 `8000` mm。除非用户明确指定其它距离，创建或复用该标注工作流时应按这两个默认值初始化。

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

## 轴网一定要双侧尺寸标注 + 轴号标注

- id: `08`
- component: `C# Script`
- runtime: `CSharpComponent`
- inputs: `0:xSpans`, `1:ySpans`, `2:xSpacing`, `3:ySpacing`, `4:extension`, `5:dimOffset`, `6:labelOffset`, `7:x`, `8:y`
- outputs: `0:out`, `1:a`, `2:b`, `3:c`, `4:d`, `5:e`, `6:f`
- paired slider: `dimOffset` 应连接 `标注偏移(mm)`，默认值 `2000` mm；`labelOffset` 应连接 `轴号偏移(mm)`，默认值 `8000` mm。
- output semantics: `b=dimLines`, `c=dimTexts`, `d=labelCircles`, `e=labelTexts`, `f=labelLines`。
- 描述: 一体化生成 X/Y 逐跨尺寸、X/Y 总尺寸、上下左右双侧尺寸标注，以及上下左右双侧轴号圆、轴号文字和轴号引线。

### 文字大小排障

本脚本输出的标注文字使用 `TextEntity`。如果文字高度在代码中看起来正确，但 Rhino/GH 预览里明显过大或过小，优先提醒用户检查 Rhino 文档的 `选项 > 注解样式`：

- 当前注解样式是否正确。
- 是否启用了模型空间缩放。
- 是否启用了图纸空间缩放。
- 当前注解样式的文字高度、模型空间比例和图纸空间比例是否与同事或参考文件一致。

`TextEntity` 属于 Rhino 标注对象，可能受注解样式缩放影响；`Line`、`Circle` 是普通几何，不会自动跟随注解样式缩放。因此当文字和轴号圆比例异常时，不要只改 C# 中的 `TextHeight` 或 `circleR`，应先检查注解样式设置。

#### 注解样式里的「高度」与「模型空间缩放比」

Rhino 注解样式中这两个字段含义不同，agent 处理文字大小问题时必须区分：

- `高度`：文字本身的基准高度，Rhino 官方说明为 model units 中的 text height。对应 C# 中 `TextEntity.TextHeight` 或标注样式里的 `TextHeight`。
- `模型空间缩放比`：模型视口中的显示缩放系数。启用 model space scaling 时，标注元素的显示尺寸 = 组件基准尺寸 * 模型空间缩放比。它会影响文字高度、箭头长度、延伸线偏移等标注元素，不只影响文字。
- 例子：`高度 = 2.0`，`模型空间缩放比 = 0.5`，模型视口中实际显示文字高度约为 `1.0`；若缩放比为 `1.0`，显示高度保持 `2.0`。
- 判断优先级：如果所有标注元素一起变大/变小，优先检查 `模型空间缩放比`；如果只有文字相对圆、线、箭头比例不对，优先检查 `高度` / `TextEntity.TextHeight` / 文字样式。
- 不要把 `模型空间缩放比` 当作几何尺寸输入，也不要为了抵消它盲目修改脚本里的所有坐标。脚本只负责生成模型单位下的标注几何和 TextEntity；注解样式负责最终注解显示比例。
- 为了让 Slider 控制的 `TextEntity.TextHeight` 尽量等于模型空间中的实际文字高度，本 skill 的 C# 脚本必须在创建每个 `TextEntity` 后设置 `te.DimensionScale = 1.0`。如果漏掉这句，文字可能继承当前注解样式的缩放；例如 `TextHeight = 50` 且有效缩放为 `100` 时，Rhino 中测到的文字高度会接近 `5000mm`。

### 轴号文字居中规则

轴号圆内文字必须使用 `TextEntity.Justification = Rhino.Geometry.TextJustification.MiddleCenter`，并将 `TextEntity.Plane.Origin` 直接放在轴号圆圆心。不要再通过 `GetBoundingBox(true)` 计算文字包围盒中心后 `Translate(...)`，因为不同字符的包围盒中心不等于视觉中心，会导致数字、字母在圆内偏移。

尺寸文字也应设置 `TextEntity.Justification = Rhino.Geometry.TextJustification.MiddleCenter`，让文字基准点与 `Plane.Origin` 的关系稳定，不要依赖默认左下角对齐。

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
        object xSpans,
        object ySpans,
        object xSpacing,
        object ySpacing,
        object extension,
        object dimOffset,
        object labelOffset,
        object x,
        object y,
        ref object a,
        ref object b,
        ref object c,
        ref object d,
        ref object e,
        ref object f)
    {
            // ===== 双侧尺寸标注 + 轴号标注 =====
            int _xSpans = Math.Max(1, Convert.ToInt32(xSpans));
            int _ySpans = Math.Max(1, Convert.ToInt32(ySpans));
            double _xSpacing = Math.Max(100.0, Convert.ToDouble(xSpacing));
            double _ySpacing = Math.Max(100.0, Convert.ToDouble(ySpacing));
            double _ext = Math.Max(0.0, Convert.ToDouble(extension));
            double _dimOff = Math.Max(500.0, Convert.ToDouble(dimOffset));
            double _labelOff = Math.Max(500.0, Convert.ToDouble(labelOffset));
            double _dimTextH = Math.Max(50.0, Convert.ToDouble(x));
            double _lbTextH = Math.Max(50.0, Convert.ToDouble(y));
        
            double totalX = _xSpans * _xSpacing;
            double totalY = _ySpans * _ySpacing;
        
            double textH = _dimTextH;
            double tick = textH * 0.85;
            double halfTick = tick / 2.0;
            double textGap = textH;
            double dimLineGap = _dimOff * 0.65;
            double totalDimGap = _dimOff * 1.6;
        
            List<Line> dimLines = new List<Line>();
            List<TextEntity> dimTexts = new List<TextEntity>();
        
            // ===== X逐跨 下侧 =====
            double extY1 = -(_dimOff + _ext);
            double extY2 = extY1 - dimLineGap;
            for (int seg = 0; seg < _xSpans; seg++)
            {
                double x1 = seg * _xSpacing;
                double x2 = (seg + 1) * _xSpacing;
                double mid = (x1 + x2) / 2;
                dimLines.Add(new Line(x1, extY1, 0, x1, extY2, 0));
                dimLines.Add(new Line(x2, extY1, 0, x2, extY2, 0));
                dimLines.Add(new Line(x1, extY2, 0, x2, extY2, 0));
                dimLines.Add(new Line(x1 + halfTick, extY2 - halfTick, 0, x1 - halfTick, extY2 + halfTick, 0));
                dimLines.Add(new Line(x2 + halfTick, extY2 - halfTick, 0, x2 - halfTick, extY2 + halfTick, 0));
                TextEntity te = new TextEntity();
                te.Plane = new Plane(new Point3d(mid, extY2 - textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
                te.PlainText = ((int)_xSpacing).ToString();
                te.TextHeight = textH;
                te.DimensionScale = 1.0;
                te.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                dimTexts.Add(te);
            }
        
            // X总尺寸 下侧
            double xTotalY1 = -(_dimOff + totalDimGap + _ext);
            double xTotalY2 = xTotalY1 - dimLineGap;
            dimLines.Add(new Line(0, xTotalY1, 0, 0, xTotalY2, 0));
            dimLines.Add(new Line(totalX, xTotalY1, 0, totalX, xTotalY2, 0));
            dimLines.Add(new Line(0, xTotalY2, 0, totalX, xTotalY2, 0));
            dimLines.Add(new Line(halfTick, xTotalY2 - halfTick, 0, -halfTick, xTotalY2 + halfTick, 0));
            dimLines.Add(new Line(totalX + halfTick, xTotalY2 - halfTick, 0, totalX - halfTick, xTotalY2 + halfTick, 0));
            TextEntity xTotalText = new TextEntity();
            xTotalText.Plane = new Plane(new Point3d(totalX / 2, xTotalY2 - textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
            xTotalText.PlainText = ((int)totalX).ToString();
            xTotalText.TextHeight = textH;
            xTotalText.DimensionScale = 1.0;
            xTotalText.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
            dimTexts.Add(xTotalText);
        
            // ===== X逐跨 上侧 =====
            double extY1T = totalY + _dimOff + _ext;
            double extY2T = extY1T + dimLineGap;
            for (int seg = 0; seg < _xSpans; seg++)
            {
                double x1 = seg * _xSpacing;
                double x2 = (seg + 1) * _xSpacing;
                double mid = (x1 + x2) / 2;
                dimLines.Add(new Line(x1, extY1T, 0, x1, extY2T, 0));
                dimLines.Add(new Line(x2, extY1T, 0, x2, extY2T, 0));
                dimLines.Add(new Line(x1, extY2T, 0, x2, extY2T, 0));
                dimLines.Add(new Line(x1 + halfTick, extY2T + halfTick, 0, x1 - halfTick, extY2T - halfTick, 0));
                dimLines.Add(new Line(x2 + halfTick, extY2T + halfTick, 0, x2 - halfTick, extY2T - halfTick, 0));
                TextEntity te = new TextEntity();
                te.Plane = new Plane(new Point3d(mid, extY2T + textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
                te.PlainText = ((int)_xSpacing).ToString();
                te.TextHeight = textH;
                te.DimensionScale = 1.0;
                te.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                dimTexts.Add(te);
            }
        
            // X总尺寸 上侧
            double xTotalY1T = totalY + _dimOff + totalDimGap + _ext;
            double xTotalY2T = xTotalY1T + dimLineGap;
            dimLines.Add(new Line(0, xTotalY1T, 0, 0, xTotalY2T, 0));
            dimLines.Add(new Line(totalX, xTotalY1T, 0, totalX, xTotalY2T, 0));
            dimLines.Add(new Line(0, xTotalY2T, 0, totalX, xTotalY2T, 0));
            dimLines.Add(new Line(halfTick, xTotalY2T + halfTick, 0, -halfTick, xTotalY2T - halfTick, 0));
            dimLines.Add(new Line(totalX + halfTick, xTotalY2T + halfTick, 0, totalX - halfTick, xTotalY2T - halfTick, 0));
            TextEntity xTotalTextT = new TextEntity();
            xTotalTextT.Plane = new Plane(new Point3d(totalX / 2, xTotalY2T + textGap, 0), Vector3d.XAxis, Vector3d.YAxis);
            xTotalTextT.PlainText = ((int)totalX).ToString();
            xTotalTextT.TextHeight = textH;
            xTotalTextT.DimensionScale = 1.0;
            xTotalTextT.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
            dimTexts.Add(xTotalTextT);
        
            // ===== Y逐跨 左侧 =====
            double extX1L = -(_dimOff + _ext);
            double extX2L = extX1L - dimLineGap;
            for (int seg = 0; seg < _ySpans; seg++)
            {
                double y1 = seg * _ySpacing;
                double y2 = (seg + 1) * _ySpacing;
                double mid = (y1 + y2) / 2;
                dimLines.Add(new Line(extX1L, y1, 0, extX2L, y1, 0));
                dimLines.Add(new Line(extX1L, y2, 0, extX2L, y2, 0));
                dimLines.Add(new Line(extX2L, y1, 0, extX2L, y2, 0));
                dimLines.Add(new Line(extX2L - halfTick, y1 + halfTick, 0, extX2L + halfTick, y1 - halfTick, 0));
                dimLines.Add(new Line(extX2L - halfTick, y2 + halfTick, 0, extX2L + halfTick, y2 - halfTick, 0));
                TextEntity te = new TextEntity();
                te.Plane = new Plane(new Point3d(extX2L - textGap, mid, 0), Vector3d.YAxis, Vector3d.XAxis);
                te.PlainText = ((int)_ySpacing).ToString();
                te.TextHeight = textH;
                te.DimensionScale = 1.0;
                te.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                dimTexts.Add(te);
            }
        
            // Y总尺寸 左侧
            double yTotalX1L = -(_dimOff + totalDimGap + _ext);
            double yTotalX2L = yTotalX1L - dimLineGap;
            dimLines.Add(new Line(yTotalX1L, 0, 0, yTotalX2L, 0, 0));
            dimLines.Add(new Line(yTotalX1L, totalY, 0, yTotalX2L, totalY, 0));
            dimLines.Add(new Line(yTotalX2L, 0, 0, yTotalX2L, totalY, 0));
            dimLines.Add(new Line(yTotalX2L - halfTick, halfTick, 0, yTotalX2L + halfTick, -halfTick, 0));
            dimLines.Add(new Line(yTotalX2L - halfTick, totalY + halfTick, 0, yTotalX2L + halfTick, totalY - halfTick, 0));
            TextEntity yTotalTextL = new TextEntity();
            yTotalTextL.Plane = new Plane(new Point3d(yTotalX2L - textGap, totalY / 2, 0), Vector3d.YAxis, Vector3d.XAxis);
            yTotalTextL.PlainText = ((int)totalY).ToString();
            yTotalTextL.TextHeight = textH;
            yTotalTextL.DimensionScale = 1.0;
            yTotalTextL.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
            dimTexts.Add(yTotalTextL);
        
            // ===== Y逐跨 右侧 =====
            double extX1R = totalX + _dimOff + _ext;
            double extX2R = extX1R + dimLineGap;
            for (int seg = 0; seg < _ySpans; seg++)
            {
                double y1 = seg * _ySpacing;
                double y2 = (seg + 1) * _ySpacing;
                double mid = (y1 + y2) / 2;
                dimLines.Add(new Line(extX1R, y1, 0, extX2R, y1, 0));
                dimLines.Add(new Line(extX1R, y2, 0, extX2R, y2, 0));
                dimLines.Add(new Line(extX2R, y1, 0, extX2R, y2, 0));
                dimLines.Add(new Line(extX2R + halfTick, y1 + halfTick, 0, extX2R - halfTick, y1 - halfTick, 0));
                dimLines.Add(new Line(extX2R + halfTick, y2 + halfTick, 0, extX2R - halfTick, y2 - halfTick, 0));
                TextEntity te = new TextEntity();
                te.Plane = new Plane(new Point3d(extX2R + textGap, mid, 0), Vector3d.YAxis, Vector3d.XAxis);
                te.PlainText = ((int)_ySpacing).ToString();
                te.TextHeight = textH;
                te.DimensionScale = 1.0;
                te.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                dimTexts.Add(te);
            }
        
            // Y总尺寸 右侧
            double yTotalX1R = totalX + _dimOff + totalDimGap + _ext;
            double yTotalX2R = yTotalX1R + dimLineGap;
            dimLines.Add(new Line(yTotalX1R, 0, 0, yTotalX2R, 0, 0));
            dimLines.Add(new Line(yTotalX1R, totalY, 0, yTotalX2R, totalY, 0));
            dimLines.Add(new Line(yTotalX2R, 0, 0, yTotalX2R, totalY, 0));
            dimLines.Add(new Line(yTotalX2R + halfTick, halfTick, 0, yTotalX2R - halfTick, -halfTick, 0));
            dimLines.Add(new Line(yTotalX2R + halfTick, totalY + halfTick, 0, yTotalX2R - halfTick, totalY - halfTick, 0));
            TextEntity yTotalTextR = new TextEntity();
            yTotalTextR.Plane = new Plane(new Point3d(yTotalX2R + textGap, totalY / 2, 0), Vector3d.YAxis, Vector3d.XAxis);
            yTotalTextR.PlainText = ((int)totalY).ToString();
            yTotalTextR.TextHeight = textH;
            yTotalTextR.DimensionScale = 1.0;
            yTotalTextR.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
            dimTexts.Add(yTotalTextR);
        
            // ===== 轴号标注（双侧）=====
            double labelTextH = _lbTextH;
            double circleR = labelTextH * 1.2;
            double dimLinePos = _dimOff * 1.65;
            List<Circle> labelCircles = new List<Circle>();
            List<TextEntity> labelTexts = new List<TextEntity>();
            List<Line> labelLines = new List<Line>();
        
            for (int col = 0; col <= _xSpans; col++)
            {
                double xx = col * _xSpacing;
                string numStr = (col + 1).ToString();
        
                Point3d centerBot = new Point3d(xx, -(_labelOff + _ext), 0);
                labelCircles.Add(new Circle(Plane.WorldXY, centerBot, circleR));
                TextEntity teBot = new TextEntity();
                Plane pBot = Plane.WorldXY;
                pBot.Origin = centerBot;
                teBot.Plane = pBot;
                teBot.PlainText = numStr;
                teBot.TextHeight = labelTextH;
                teBot.DimensionScale = 1.0;
                teBot.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                labelTexts.Add(teBot);
                labelLines.Add(new Line(new Point3d(xx, -(dimLinePos + _ext), 0),
                                        new Point3d(xx, -(_labelOff + _ext - circleR), 0)));
        
                Point3d centerTop = new Point3d(xx, totalY + _labelOff + _ext, 0);
                labelCircles.Add(new Circle(Plane.WorldXY, centerTop, circleR));
                TextEntity teTop = new TextEntity();
                Plane pTop = Plane.WorldXY;
                pTop.Origin = centerTop;
                teTop.Plane = pTop;
                teTop.PlainText = numStr;
                teTop.TextHeight = labelTextH;
                teTop.DimensionScale = 1.0;
                teTop.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                labelTexts.Add(teTop);
                labelLines.Add(new Line(new Point3d(xx, totalY + dimLinePos + _ext, 0),
                                        new Point3d(xx, totalY + _labelOff + _ext - circleR, 0)));
            }
        
            for (int row = 0; row <= _ySpans; row++)
            {
                double yy = row * _ySpacing;
                char letter = (char)('A' + row);
                string letterStr = letter.ToString();
        
                Point3d centerLeft = new Point3d(-(_labelOff + _ext), yy, 0);
                labelCircles.Add(new Circle(Plane.WorldXY, centerLeft, circleR));
                TextEntity teLeft = new TextEntity();
                Plane pLeft = new Plane(centerLeft, Vector3d.YAxis, Vector3d.XAxis);
                teLeft.Plane = pLeft;
                teLeft.PlainText = letterStr;
                teLeft.TextHeight = labelTextH;
                teLeft.DimensionScale = 1.0;
                teLeft.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                labelTexts.Add(teLeft);
                labelLines.Add(new Line(new Point3d(-(dimLinePos + _ext), yy, 0),
                                        new Point3d(-(_labelOff + _ext - circleR), yy, 0)));
        
                Point3d centerRight = new Point3d(totalX + _labelOff + _ext, yy, 0);
                labelCircles.Add(new Circle(Plane.WorldXY, centerRight, circleR));
                TextEntity teRight = new TextEntity();
                Plane pRight = new Plane(centerRight, Vector3d.YAxis, Vector3d.XAxis);
                teRight.Plane = pRight;
                teRight.PlainText = letterStr;
                teRight.TextHeight = labelTextH;
                teRight.DimensionScale = 1.0;
                teRight.Justification = Rhino.Geometry.TextJustification.MiddleCenter;
                labelTexts.Add(teRight);
                labelLines.Add(new Line(new Point3d(totalX + dimLinePos + _ext, yy, 0),
                                        new Point3d(totalX + _labelOff + _ext - circleR, yy, 0)));
            }
        
            b = dimLines;
            c = dimTexts;
            d = labelCircles;
            e = labelTexts;
            f = labelLines;
}
}
```
