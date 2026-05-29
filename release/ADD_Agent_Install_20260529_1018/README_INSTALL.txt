ADD Agent / ADDGH 安装说明

适用环境：
- Windows
- Rhino 8 / Grasshopper
- Microsoft Edge WebView2 Runtime（通常 Windows 已自带；若画布空白，请安装 WebView2 Runtime）

安装方式：
1. 解压整个 ADD_Agent_Install_20260529_1018 文件夹，不要只复制单个 ADDGH.gha。
2. 打开 Rhino，运行 Grasshopper。
3. 在 Grasshopper 中打开 File > Special Folders > Components Folder。
4. 将本包中的 ADDGH 文件夹复制到 Components Folder 中。
5. 右键 ADDGH\ADDGH.gha，打开属性；如果看到“解除锁定/Unblock”，请勾选并应用。
6. 重启 Rhino / Grasshopper。

目录说明：
- ADDGH：插件和运行依赖。
- ADDGH\CanvasWeb\dist：画布前端资源，必须保留相对路径。
- skills：agent 技能文件。
- reference：官方参考画布文件。

如果需要把 skills/reference 放在别的位置，可设置环境变量 ADDGH_PROJECT_ROOT 指向本解压目录。

