# ADD_Agent / ADDGH

`ADD_Agent` 是一个面向 Rhino + Grasshopper 的 AI 辅助插件项目。当前仓库的核心是 `ADDGH` 插件，本体使用 C# / .NET Framework 4.8 开发，并通过内嵌 `CanvasWeb` 前端提供更现代的画布交互界面。

项目当前存在几套命名并行使用的情况：

- 仓库名：`ADD_Agent`
- 插件程序集：`ADDGH`
- Grasshopper 菜单名称：`Squirrel`

如果你是第一次接触这个仓库，优先把它理解为“一个运行在 Grasshopper 内部的 AI 助手插件”即可。

## 项目结构

```text
.
├─ ADDGH/                 Grasshopper 插件主工程（C# / net48）
├─ CanvasWeb/             内嵌前端（React + TypeScript + Vite）
├─ nuget-offline/         离线 NuGet 包
├─ release/               打包后的插件发布文件
├─ skills/                运行时提示词 / 技能配置
├─ DEMOagent.sln          Visual Studio 解决方案
└─ NuGet.config           NuGet 配置
```

## 主要组成

### 1. `ADDGH`

`ADDGH` 是 Grasshopper 插件主工程，目标框架为 `net48`，输出扩展名为 `.gha`。

主要依赖：

- Grasshopper 7
- WebView2
- Newtonsoft.Json

插件中已经包含：

- Grasshopper 菜单集成
- 聊天窗口与消息渲染
- 工具调度与技能路由
- 图片工作流
- 内嵌 Canvas 视图

### 2. `CanvasWeb`

`CanvasWeb` 是配套前端，技术栈如下：

- React 18
- TypeScript
- Vite
- `@xyflow/react`

构建后的 `dist/` 内容会被插件工程作为运行时资源复制到输出目录中，因此前端改动后通常需要重新构建，再回到 `ADDGH` 工程编译。

## 开发环境

建议环境：

- Windows
- Rhino 7 / Grasshopper
- Visual Studio 2022
- Node.js
- pnpm
- WebView2 Runtime

## 本地开发

### 前端

在 [CanvasWeb](C:/Users/26933/.codex/worktrees/fdf4/DEMOagent/CanvasWeb) 目录下：

```bash
pnpm install
pnpm build
```

如需本地预览：

```bash
pnpm dev
```

### 插件

使用 Visual Studio 打开 [DEMOagent.sln](C:/Users/26933/.codex/worktrees/fdf4/DEMOagent/DEMOagent.sln)，编译 `ADDGH` 项目即可。

项目已经配置：

- 目标框架：`.NET Framework 4.8`
- 输出类型：`.gha`
- 离线包恢复目录：`nuget-offline/`

## 安装插件

发布包安装步骤可参考 [release/ADDGH-install-readme.txt](C:/Users/26933/.codex/worktrees/fdf4/DEMOagent/release/ADDGH-install-readme.txt)，简化流程如下：

1. 关闭 Rhino 和 Grasshopper。
2. 解压发布包。
3. 将解压后的 `ADDGH` 文件夹复制到 `%AppData%\Grasshopper\Libraries`。
4. 如果 Windows 显示 `Unblock`，先对 `ADDGH.gha` 执行解除阻止。
5. 启动 Rhino，打开 Grasshopper，在菜单中查找 `Squirrel` / `ADDGH` 相关入口。

注意：

- 发布目录内文件需要保持完整，不要只复制单个 `.gha`。
- `CanvasWeb` 资源和多个 DLL 都是运行时依赖。

## 当前仓库状态

这个仓库除了插件与前端源码外，还包含一些发布产物、草稿文档和辅助脚本，因此当前更接近“开发工作区”而不是严格精简后的开源仓库结构。

如果后续要面向 GitHub 做长期维护，建议逐步补齐：

- 更明确的版本说明
- API / Provider 配置说明
- 开发与发布流程
- 截图或演示 GIF
- License

## 说明

当前 `README.md` 的目标是先补上仓库首页最基础的可读性信息，便于单独提交和推送。等网络恢复后，可以基于远端最新代码把这个文件作为独立提交推到新分支。
