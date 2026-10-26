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
    public bool WaterEnabled { get; set; } = true;
    public int WaterIntervalMinutes { get; set; } = 60;
    public bool SittingEnabled { get; set; } = true;
    public int SittingIntervalMinutes { get; set; } = 50;
    public bool ReminderSound { get; set; } = false;
    public bool SuppressFullscreen { get; set; } = true;
    public int QuietStartHour { get; set; } = 22;
    public int QuietEndHour { get; set; } = 9;
}
