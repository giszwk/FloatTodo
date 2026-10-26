# FloatTodo v0.3.5 更新说明

- 为半隐藏状态内置 `Assets/Skins/LuoXiaoHei/peek.gif`（用户提供，256×341，6 帧）。
- 半隐藏默认使用 `peek.gif` 第 0 帧，60–120 秒随机触发一次单轮动画，静止状态不循环。
- 右侧吸边使用原始 GIF，左侧吸边进行左右镜像（沿竖直轴翻转，`ScaleX=-1`）。
- 保留 v0.3.4 的 `BitmapMetadata` API 编译修复、托盘、排序、提醒及数据库逻辑。

本环境未安装 Windows/WPF .NET 编译器，需在 Windows 实机通过 `dotnet build` 和鼠标交互验收。
