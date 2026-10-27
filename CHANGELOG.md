# FloatTodo v0.3.6 更新说明

- 展开状态点击悬浮球：随机播放 `idle.gif` / `waiting.gif` / `review.gif` 之一，并照常打开待办面板。
- 新增「自定义 GIF」皮肤：优先读取 EXE 旁 `Assets\Skins\Custom\` 的同名 4 个 GIF，随包分发；旧的 `%LOCALAPPDATA%\FloatTodo\skins\custom\` 作为兼容回退。任一文件缺失或损坏时整体回退默认蓝色悬浮球。
- 新增悬浮球大小设置（50–200%，默认 100%）：默认球以 64px、猫类皮肤以 96px 为基准等比缩放。
- 修复单文件发布未复制 `AppIcon.ico` 导致托盘显示系统通用图标的问题；内容资源显式 `ExcludeFromSingleFile`，避免发布时素材被误删。
- 托盘图标在外部 ico 缺失时改为从 EXE 内嵌图标提取。
- 移除不再使用的 `cat-visible/cat-peek/cat-peek-blink` PNG 兜底素材。

# FloatTodo v0.3.5 更新说明

- 为半隐藏状态内置 `Assets/Skins/LuoXiaoHei/peek.gif`（用户提供，256×341，6 帧）。
- 半隐藏默认使用 `peek.gif` 第 0 帧，60–120 秒随机触发一次单轮动画，静止状态不循环。
- 右侧吸边使用原始 GIF，左侧吸边进行左右镜像（沿竖直轴翻转，`ScaleX=-1`）。
- 保留 v0.3.4 的 `BitmapMetadata` API 编译修复、托盘、排序、提醒及数据库逻辑。

本环境未安装 Windows/WPF .NET 编译器，需在 Windows 实机通过 `dotnet build` 和鼠标交互验收。
