namespace FloatTodo.Models;

public enum DockSide { None, Left, Right }

public sealed class AppSettings
{
    // 这里使用 Win32 的物理像素保存位置，避免不同屏幕 DPI 换算混乱。
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }
    public DockSide Dock { get; set; } = DockSide.None;
    public int DockThresholdPx { get; set; } = 30;
    public int VisiblePercent { get; set; } = 40;
    public int CollapseDelayMs { get; set; } = 350;
}
