---
name: official-grasshopper-visualization-preview
description: 官方可视化 reference。用于在 Grasshopper 中为几何结果建立可调颜色预览，优先使用 Custom Preview 与 Colour RGB/HSV/HSL/CMYK 等可参数化颜色电池；不要使用 Colour Swatch。
---

# Grasshopper 可视化预览工作流

## 目标

当用户需要给几何结果上色、区分构件类别、调试多路输出、制作截图前的清晰预览或建立可调色的展示效果时，优先使用这个工作流。

核心原则：
- 几何输入接入 `Custom Preview` 的 `G` 端。
- 颜色/材质输入接入 `Custom Preview` 的 `M` 端。
- 颜色应由 `Colour RGB`、`Colour RGB (f)`、`Colour HSV`、`Colour HSL`、`Colour CMYK` 等可参数化颜色电池生成。
- 使用 `Colour RGB`、`Colour RGB (f)`、`Colour HSV`、`Colour HSL`、`Colour CMYK` 等颜色电池时，必须在这些电池的每个颜色参数输入端口创建并连接 `Number Slider`；不要让颜色通道停留在电池默认值，也不要只放一个未接 slider 的颜色电池。
- 不要使用 `Colour Swatch` 作为默认方案，因为它不能方便地由 Slider 调整颜色。

## 推荐电池

- `Custom Preview`：最终可视化出口。每一路需要独立颜色的几何，建议单独一个 `Custom Preview`。
- `Colour RGB`：使用 0-255 的 R/G/B 通道，适合用户给出常规 RGB 值。
- `Colour RGB (f)`：使用 0.0-1.0 的 R/G/B 通道，适合归一化参数、渐变或计算结果。
- `Colour HSV`：适合用 Hue Slider 快速切换色相，保持饱和度和明度稳定。
- `Colour HSL`：适合控制亮度层级，让同一类别产生深浅变化。
- `Colour CMYK`：适合印刷或出图语境中的颜色表达。
- `Create Material`：当用户需要材质效果，而不只是纯色预览时使用；输出材质接入 `Custom Preview.M`。
- `Number Slider`：驱动颜色通道，必须给清晰 label，例如 `红色R(0-255)`、`色相H(0-1)`、`透明度A(0-1)`。

## 禁用默认方案

- 不要默认创建 `Colour Swatch`。
- 只有当用户明确要求固定不可调色块，或正在复用已有画布中已经存在的 `Colour Swatch` 时，才可以保留它。
- 如果用户说“颜色可调”“之后还要改颜色”“不同构件用不同颜色”“截图前清晰区分”，应使用 Colour 系列电池加 Slider。

## 常用连接模式

### 单一路几何上色

1. 目标几何输出连接到 `Custom Preview.G`。
2. `Colour RGB` 或 `Colour HSV` 输出连接到 `Custom Preview.M`。
3. 颜色通道由 Slider 控制。
4. 关闭上游中间几何预览，只保留最终 `Custom Preview`。

### 多类别几何上色

1. 每个类别分别创建一组 `Colour ...` + `Custom Preview`。
2. 每类几何接对应 `Custom Preview.G`。
3. 每类颜色参数使用明确名称，例如 `梁_R`、`柱_H`、`玻璃_透明度`。
4. 如果类别很多，优先使用 `Colour HSV`，用一组 Hue Slider 分配色相。

### C# 输出结果上色

1. C# Script 只负责输出几何数据，不要把预览颜色硬编码进脚本。
2. C# 输出端口应带业务语义标签，便于识别要接哪一路几何。
3. 在脚本外部使用 `Custom Preview` 和 Colour 系列电池做可视化。
4. 需要截图或视觉复核时，隐藏 C# Script 自身预览，保留最终 `Custom Preview`。

### 材质预览

1. 如果用户明确提出材质、透明度、反射、粗糙度、玻璃、金属、木材等需求，优先使用 `Create Material`。
2. 颜色仍由 `Colour RGB` / `Colour HSV` / `Colour HSL` 等可调颜色电池提供，再接入 `Create Material` 的颜色相关输入。
3. `Create Material` 输出接入 `Custom Preview.M`，几何仍接 `Custom Preview.G`。
4. 不要用 `Colour Swatch` 代替材质；材质参数应尽量由 Slider 或明确输入控制。

## 默认参数建议

- `Colour RGB` 通道 Slider：
  - R/G/B 范围 `0` 到 `255`，整数，默认按任务选择。
- `Colour RGB (f)` / `Colour HSV` / `Colour HSL` 通道 Slider：
  - 通道范围 `0.0` 到 `1.0`，小数 2-3 位。
- 透明度/Alpha：
  - 如对应 Colour 电池支持 Alpha，范围 `0.0` 到 `1.0`。
  - 建筑体块预览常用 `0.35` 到 `0.75`。
- 颜色命名：
  - Slider label 必须表达通道和范围，不要只写 `x`、`a`、`color`。

## Agent 操作要求

- 创建可视化工作流时，优先搜索或添加 `Custom Preview` 与 Colour 系列电池。
- 一旦添加 `Colour RGB` / `Colour HSV` / `Colour HSL` / `Colour CMYK` 等颜色电池，必须同步为 R/G/B、H/S/V、H/S/L、C/M/Y/K、Alpha 等输入端口添加并连接 `Number Slider`，让颜色参数可见、可调、可复核。
- 有材质需求时，使用 `Create Material` 生成材质并接到 `Custom Preview.M`。
- 不要为了调颜色创建 C# Script；颜色通道用 Slider + Colour 电池表达。
- 不要把颜色参数直接写死在 Panel 或脚本里，除非用户明确要求固定色。
- 如果已有模型输出很多过程几何，应关闭中间预览，只让最终 `Custom Preview` 可见。
- 如果用户上传参考图要求颜色接近，应先用视觉事实判断目标色，再用可调 Slider 近似，而不是放一个不可调色块。

## 检查清单

- 是否至少有一个 `Custom Preview` 作为最终预览出口。
- 是否使用 `Colour RGB` / `Colour RGB (f)` / `Colour HSV` / `Colour HSL` / `Colour CMYK` 之一提供颜色。
- 每个颜色电池的颜色参数输入端口是否都已连接清晰命名的 `Number Slider`，而不是依赖默认值或未接线输入。
- 如果用户要求材质，是否使用 `Create Material` 而不是只接纯色。
- 是否避免了默认 `Colour Swatch`。
- Slider 是否有清晰中文 label、范围和默认值。
- 中间过程几何预览是否已隐藏，最终预览是否清晰。
